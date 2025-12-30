using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class PresentMonRecorder
    {
        public event Action<double>? OnFpsData;
        
        private Process? _process;
        private volatile bool _isRunning = false;
        private int _msBetweenPresentsIndex = 24;
        private readonly Queue<(double frametime, DateTime timestamp)> _window = new(200);
        private readonly object _lock = new();

        public void Start(string processName, string? gameName = null)
        {
            if (_isRunning)
            {
                Stop();
                Thread.Sleep(100);
            }
            _isRunning = true;
            Task.Run(() => RunPresentMon(processName));
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(500);
                }
            }
            catch { }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
            lock (_lock) { _window.Clear(); }
        }

        public List<double> GetFrametimeHistory()
        {
            lock (_lock)
            {
                return _window.Select(x => x.frametime).ToList();
            }
        }

        private void RunPresentMon(string processName)
        {
            try
            {
                string presentMonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon.exe");
                if (!File.Exists(presentMonPath)) { _isRunning = false; return; }

                var psi = new ProcessStartInfo
                {
                    FileName = presentMonPath,
                    Arguments = $"--output_stdout --session_name framebase --stop_existing_session --process_name {processName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _process = Process.Start(psi);
                if (_process == null) { _isRunning = false; return; }
                _process.BeginErrorReadLine();

                int lineCount = 0;
                DateTime lastEmit = DateTime.MinValue;

                while (_isRunning && _process != null && !_process.HasExited)
                {
                    string? line = _process.StandardOutput.ReadLine();
                    if (line == null) break;
                    lineCount++;
                    
                    if (lineCount == 1 && line.Contains("MsBetweenPresents"))
                    {
                        var cols = line.Split(',');
                        for (int i = 0; i < cols.Length; i++)
                        {
                            if (cols[i].Contains("MsBetweenPresents"))
                            {
                                _msBetweenPresentsIndex = i;
                                break;
                            }
                        }
                        continue;
                    }

                    var columns = line.Split(',');
                    if (columns.Length <= _msBetweenPresentsIndex) continue;

                    if (!double.TryParse(columns[_msBetweenPresentsIndex], 
                        System.Globalization.NumberStyles.Any, 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        out double frametime)) continue;

                    if (frametime <= 0 || frametime > 1000) continue;

                    DateTime now = DateTime.Now;
                    lock (_lock)
                    {
                        _window.Enqueue((frametime, now));
                        while (_window.Count > 0 && (now - _window.Peek().timestamp).TotalSeconds > 2)
                        {
                            _window.Dequeue();
                        }
                    }

                    if ((now - lastEmit).TotalSeconds >= 1)
                    {
                        double avgFps = 0;
                        lock (_lock)
                        {
                            if (_window.Count > 0)
                            {
                                double avgFrametime = _window.Average(x => x.frametime);
                                avgFps = avgFrametime > 0 ? 1000.0 / avgFrametime : 0;
                            }
                        }
                        lastEmit = now;
                        if (avgFps > 0) { OnFpsData?.Invoke(avgFps); }
                    }
                }
            }
            catch { _isRunning = false; }
        }
    }
}
