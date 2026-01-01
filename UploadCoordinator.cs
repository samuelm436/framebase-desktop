using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using framebase_app;

namespace FramebaseApp
{
    public class UploadCoordinator : IDisposable
    {
        private readonly PresentMonRecorder _recorder;
        private readonly InputActivityMonitor _activity;
        private readonly FpsUploader _uploader;

        private string _gameName = "Unknown";
        private string _cpu = "Unknown";
        private string _gpu = "Unknown";
        private string _resolution = "Unknown";
        private string _setting = "Performance";

        private bool _isRecording = false;
        private bool _isActive = false;
        private double _currentFps = 0;
        private double _currentFrametime = 0;
        private double _current1PercentLow = 0;

        private DispatcherTimer? _sessionTimer;
        private int _sessionSeconds = 0;

        public event Action<double, double, double, string>? LiveMetrics; // fps, frametime, 1%low, status
        public event Action<int>? SessionCounterChanged;
        public event Action<bool, string, double, double>? UploadCompleted; // success, msg, avgFps, 1%low

        public UploadCoordinator()
        {
            _recorder = new PresentMonRecorder();
            _activity = new InputActivityMonitor();
            _uploader = new FpsUploader();

            _recorder.OnFpsData += (fps, liveLow1) =>
            {
                _currentFps = fps;
                _currentFrametime = fps > 0 ? 1000.0 / fps : 0;
                
                // Always use the live 1% low from recorder for the overlay
                _current1PercentLow = liveLow1;
                
                if (_isRecording && _isActive)
                {
                    _uploader.AddFps(fps);
                }
                
                LiveMetrics?.Invoke(_currentFps, _currentFrametime, _current1PercentLow, GetStatus());
            };

            _activity.RecordingStatusChanged += recording =>
            {
                _isRecording = recording;
                
                if (recording && _isActive && _sessionTimer == null)
                {
                    StartSessionTimer();
                }
                else if (!recording)
                {
                    PauseSessionTimer();
                }
                
                LiveMetrics?.Invoke(_currentFps, _currentFrametime, _current1PercentLow, GetStatus());
            };

            _activity.ActivityStatusChanged += async active =>
            {
                _isActive = active;
                
                if (active && _isRecording && _sessionTimer == null)
                {
                    StartSessionTimer();
                }
                else if (!active)
                {
                    PauseSessionTimer();
                    
                    // Auto-upload if session > 30s
                    if (_sessionSeconds > 30)
                    {
                        await UploadSessionInternalAsync();
                    }
                }
                
                LiveMetrics?.Invoke(_currentFps, _currentFrametime, _current1PercentLow, GetStatus());
            };
        }

        private string GetStatus()
        {
            if (!_isRecording) return "Waiting for game";
            return _isActive ? "Active" : "Inactive";
        }

        public void ConfigureEnvironment(string cpu, string gpu, string resolution, string? setting = null)
        {
            // Store names, but we will also need IDs for upload
            if (!string.IsNullOrWhiteSpace(cpu))
            {
                _cpu = cpu;
            }
            if (!string.IsNullOrWhiteSpace(gpu))
            {
                _gpu = gpu;
            }
            if (!string.IsNullOrWhiteSpace(resolution))
            {
                _resolution = resolution;
            }
            if (!string.IsNullOrWhiteSpace(setting))
            {
                _setting = setting;
            }
        }

        public void Start(string processName, string? gameName = null)
        {
            _gameName = gameName ?? processName;
            
            _uploader.Clear();
            _sessionSeconds = 0;
            SessionCounterChanged?.Invoke(0);
            _currentFps = 0;

            _activity.SetTargetProcesses(new[] { processName });
            _recorder.Start(processName, _gameName);
            _activity.StartMonitoring();
        }

        public void Stop()
        {
            PauseSessionTimer();
            _activity.StopMonitoring();
            _recorder.Stop();
        }

        public async Task StopAndUploadAsync()
        {
            Stop();
            await UploadSessionInternalAsync();
        }

        private async Task UploadSessionInternalAsync()
        {
            var (avgFps, onePercentLow) = _uploader.GetStats();
            
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] Uploading: avgFps={avgFps:F1}, 1%low={onePercentLow:F1}\n"
                );
            }
            catch { }

            if (avgFps == 0)
            {
                UploadCompleted?.Invoke(false, "No data recorded", 0, 0);
                return;
            }

            // Get IDs for upload
            string cpuId = SystemInfoHelper.GetCpuId();
            string gpuId = SystemInfoHelper.GetGpuId();

            string result = await _uploader.UploadAsync(cpuId, gpuId, _cpu, _gpu, _gameName, _setting, _resolution, _sessionSeconds);
            bool success = result.Contains("successful", StringComparison.OrdinalIgnoreCase);
            
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] Upload result: {result} (success={success})\n"
                );
            }
            catch { }
            
            // Reset session after upload (successful or not)
            _uploader.Clear();
            _sessionSeconds = 0;
            SessionCounterChanged?.Invoke(0);
            _currentFps = 0;
            _currentFrametime = 0;
            _current1PercentLow = 0;
            
            UploadCompleted?.Invoke(success, result, avgFps, onePercentLow);
        }

        private void StartSessionTimer()
        {
            if (_sessionTimer != null) return;
            
            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, __) =>
            {
                _sessionSeconds++;
                SessionCounterChanged?.Invoke(_sessionSeconds);
            };
            _sessionTimer.Start();
        }

        private void PauseSessionTimer()
        {
            _sessionTimer?.Stop();
            _sessionTimer = null;
        }

        public void Dispose()
        {
            Stop();
        }

        public System.Collections.Generic.List<double> GetFrametimeHistory()
        {
            return _recorder.GetFrametimeHistory();
        }
    }
}
