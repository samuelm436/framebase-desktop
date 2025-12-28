using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class TelemetrySample
    {
        public DateTime Timestamp { get; set; }
        public string? Game { get; set; }
        public double AvgFps { get; set; }
        public double AvgFrametime { get; set; }
        public double OnePercentLow { get; set; }
        public int SampleCount { get; set; }
    }

    public class TelemetryCollector : IDisposable
    {
        private readonly Channel<TelemetrySample> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _workerTask;
        private readonly string _uploadUrl;
        private readonly Func<string?> _getToken;
        private readonly string _walPath;
        private readonly string _logPath;
        private readonly SemaphoreSlim _walLock = new(1,1);
        private readonly int _maxBatchSize;
        private readonly TimeSpan _flushInterval;
        private volatile bool _collectEnabled = true;
        private int _pendingCount = 0;

        // Additional context for desktop-upload format
        private string? _currentGame;
        private string? _currentCpu;
        private string? _currentGpu;
        private string? _currentCpuId;
        private string? _currentGpuId;

        // track recent uploads for UI
        private readonly List<DateTime> _lastUploads = new();
        public IReadOnlyList<DateTime> LastUploads => _lastUploads.AsReadOnly();
        public event Action<DateTime,int>? UploadCompleted; // timestamp, itemCount

        public TelemetryCollector(string uploadUrl, Func<string?> getToken, string? walPath = null, int maxBatchSize = 50, TimeSpan? flushInterval = null)
        {
            _uploadUrl = uploadUrl ?? throw new ArgumentNullException(nameof(uploadUrl));
            _getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
            _walPath = walPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_wal.jsonl");
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_log.txt");
            _maxBatchSize = maxBatchSize;
            _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);

            // Initialize system info
            InitializeSystemInfo();

            var options = new BoundedChannelOptions(10000) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest };
            _channel = Channel.CreateBounded<TelemetrySample>(options);

            // load WAL into channel for durability
            _ = RestoreWalAsync();

            _workerTask = Task.Run(WorkerLoopAsync);
        }

        private void InitializeSystemInfo()
        {
            try
            {
                _currentCpu = SystemInfoHelper.GetCpu();
                _currentGpu = SystemInfoHelper.GetGpu();
                _currentCpuId = SystemInfoHelper.GetCpuId();
                _currentGpuId = SystemInfoHelper.GetGpuId();
            }
            catch (Exception ex)
            {
                _ = AppendLogAsync($"Failed to get system info: {ex.Message}");
                _currentCpu = "Unknown CPU";
                _currentGpu = "Unknown GPU";
                _currentCpuId = "unknown-cpu";
                _currentGpuId = "unknown-gpu";
            }
        }

        public void SetCurrentGame(string? game)
        {
            _currentGame = game;
        }

        public bool IsCollecting => _collectEnabled;
        public int PendingCount => _pendingCount;

        public void SetCollectEnabled(bool enabled)
        {
            _collectEnabled = enabled;
            _ = AppendLogAsync($"CollectEnabled set to {enabled}");
        }

        public bool TryEnqueue(TelemetrySample sample)
        {
            if (!_collectEnabled) return false;
            // append to WAL then channel
            _ = AppendToWalAsync(sample);
            var ok = _channel.Writer.TryWrite(sample);
            if (ok) Interlocked.Increment(ref _pendingCount);
            _ = AppendLogAsync($"Enqueue sample ts={sample.Timestamp:o} ok={ok} pending={_pendingCount}");
            return ok;
        }

        public async Task FlushAsync()
        {
            await AppendLogAsync("Flush requested");
            // force worker to upload everything
            await _channel.Writer.WriteAsync(new TelemetrySample { Timestamp = DateTime.UtcNow, AvgFps = double.NaN, AvgFrametime = double.NaN, OnePercentLow = double.NaN, SampleCount = -1 });
            // special marker - worker treats NaN/Count=-1 as flush signal
            await Task.Delay(50);
        }

        private async Task RestoreWalAsync()
        {
            try
            {
                if (!File.Exists(_walPath)) return;
                var lines = await File.ReadAllLinesAsync(_walPath);
                await AppendLogAsync($"Restoring WAL with {lines.Length} lines");
                foreach (var l in lines)
                {
                    try
                    {
                        var s = JsonSerializer.Deserialize<TelemetrySample>(l);
                        if (s != null)
                        {
                            if (_channel.Writer.TryWrite(s)) Interlocked.Increment(ref _pendingCount);
                        }
                    }
                    catch { }
                }
                await AppendLogAsync($"Restored pending={_pendingCount}");
            }
            catch (Exception ex)
            {
                await AppendLogAsync($"RestoreWal failed: {ex.Message}");
            }
        }

        private async Task AppendToWalAsync(TelemetrySample sample)
        {
            try
            {
                var line = JsonSerializer.Serialize(sample);
                await _walLock.WaitAsync();
                try
                {
                    await File.AppendAllTextAsync(_walPath, line + "\n");
                }
                finally { _walLock.Release(); }
            }
            catch (Exception ex)
            {
                await AppendLogAsync($"AppendToWal failed: {ex.Message}");
            }
        }

        private async Task TrimWalAsync(int removedCount)
        {
            try
            {
                await _walLock.WaitAsync();
                try
                {
                    if (!File.Exists(_walPath)) return;
                    var lines = await File.ReadAllLinesAsync(_walPath);
                    if (removedCount >= lines.Length)
                    {
                        // remove file
                        File.Delete(_walPath);
                    }
                    else if (removedCount > 0)
                    {
                        var remaining = lines.Skip(removedCount).ToArray();
                        await File.WriteAllLinesAsync(_walPath, remaining);
                    }
                }
                finally { _walLock.Release(); }
            }
            catch (Exception ex)
            {
                await AppendLogAsync($"TrimWal failed: {ex.Message}");
            }
            finally
            {
                if (removedCount > 0)
                {
                    Interlocked.Add(ref _pendingCount, -removedCount);
                    if (_pendingCount < 0) Interlocked.Exchange(ref _pendingCount, 0);
                    _ = AppendLogAsync($"TrimWal removed={removedCount} pending={_pendingCount}");
                }
            }
        }

        private async Task WorkerLoopAsync()
        {
            var http = new System.Net.Http.HttpClient();
            var buffer = new List<TelemetrySample>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    // read with timeout
                    while (buffer.Count < _maxBatchSize)
                    {
                        var waitTask = _channel.Reader.ReadAsync(_cts.Token).AsTask();
                        var delay = Task.Delay(_flushInterval, _cts.Token);
                        var finished = await Task.WhenAny(waitTask, delay);
                        if (finished == waitTask)
                        {
                            var item = await waitTask; // may throw if canceled
                            // flush-signal marker
                            if (double.IsNaN(item.AvgFps) && item.SampleCount == -1)
                            {
                                // trigger immediate upload of current buffer
                                await AppendLogAsync("Flush marker received in worker");
                                break;
                            }
                            buffer.Add(item);
                            continue;
                        }
                        else
                        {
                            // timeout
                            break;
                        }
                    }

                    if (buffer.Count == 0)
                    {
                        // nothing to send, wait a bit
                        await Task.Delay(200, _cts.Token);
                        continue;
                    }

                    await AppendLogAsync($"Processing session upload count={buffer.Count}");

                    // Process as session-based upload for desktop-upload API
                    await ProcessSessionUploadAsync(buffer);
                    buffer.Clear();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    await AppendLogAsync($"WorkerLoop exception: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"TelemetryCollector error: {ex.Message}");
                    await Task.Delay(2000);
                }
            }
        }

        private static byte[] Gzip(byte[] input)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
            {
                gz.Write(input, 0, input.Length);
            }
            return ms.ToArray();
        }

        private static TimeSpan RandomJitter()
        {
            var r = new Random();
            return TimeSpan.FromMilliseconds(r.Next(0, 500));
        }

        private async Task AppendLogAsync(string msg)
        {
            try
            {
                var line = $"[{DateTime.UtcNow:o}] {msg}{Environment.NewLine}";
                await File.AppendAllTextAsync(_logPath, line);
            }
            catch { }
        }

        private async Task ProcessSessionUploadAsync(List<TelemetrySample> buffer)
        {
            if (buffer.Count == 0) return;
            
            try
            {
                // Aggregate session data
                var sessionGame = buffer.FirstOrDefault()?.Game ?? _currentGame ?? "Unknown";
                var avgFps = buffer.Average(b => b.AvgFps);
                var avgLows = buffer.Average(b => b.OnePercentLow);
                var sessionStart = buffer.First().Timestamp;
                var sessionEnd = buffer.Last().Timestamp;
                var sessionMinutes = (sessionEnd - sessionStart).TotalMinutes;

                // Get current preset information
                string currentSetting = "Performance"; // Default
                Dictionary<string, string>? presetConfig = null;
                
                try
                {
                    var gameKey = sessionGame.ToLower();
                    var status = await GraphicsConfigurator.CheckPresetStatusAsync(gameKey);
                    if (status.PresetMatched && !string.IsNullOrEmpty(status.MatchedPresetName))
                    {
                        currentSetting = status.MatchedPresetName;
                        var presetDict = await GraphicsConfigurator.DownloadPresetConfigAsync(gameKey);
                        if (presetDict.TryGetValue(currentSetting, out var preset))
                        {
                            presetConfig = preset;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await AppendLogAsync($"Failed to get preset info: {ex.Message}");
                }

                // Create desktop-upload compatible payload
                var payload = new
                {
                    cpu = _currentCpu ?? "Unknown CPU",
                    gpu = _currentGpu ?? "Unknown GPU",
                    cpuId = _currentCpuId ?? "unknown-cpu",
                    gpuId = _currentGpuId ?? "unknown-gpu",
                    game = sessionGame,
                    setting = currentSetting,
                    resolution = "1920x1080", // Default resolution, could be detected
                    avgFps = Math.Round(avgFps, 1),
                    lows = Math.Round(avgLows, 1),
                    sessionDurationMinutes = Math.Round(sessionMinutes, 0),
                    sampleCount = buffer.Count,
                    isWeightedUpdate = true,
                    presetConfig = presetConfig ?? new Dictionary<string, string> { { "telemetry", "session" } }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

                // Send with retry logic
                await SendWithRetryAsync(content, buffer.Count);
            }
            catch (Exception ex)
            {
                await AppendLogAsync($"ProcessSessionUpload failed: {ex.Message}");
            }
        }

        private async Task SendWithRetryAsync(System.Net.Http.StringContent content, int itemCount)
        {
            using var http = new System.Net.Http.HttpClient();
            var token = _getToken();
            if (!string.IsNullOrEmpty(token)) 
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            int attempt = 0;
            bool success = false;
            while (attempt < 6 && !_cts.IsCancellationRequested)
            {
                try
                {
                    await AppendLogAsync($"HTTP attempt={attempt+1} items={itemCount}");
                    var resp = await http.PostAsync(_uploadUrl, content, _cts.Token);
                    await AppendLogAsync($"HTTP status={(int)resp.StatusCode}");
                    
                    if (resp.IsSuccessStatusCode)
                    {
                        success = true;
                        break;
                    }
                    else if ((int)resp.StatusCode == 429)
                    {
                        var delaySec = 5 * (attempt + 1);
                        await AppendLogAsync($"Rate limited, sleeping {delaySec}s");
                        await Task.Delay(TimeSpan.FromSeconds(delaySec) + RandomJitter(), _cts.Token);
                    }
                    else
                    {
                        string errorBody = await resp.Content.ReadAsStringAsync();
                        await AppendLogAsync($"HTTP failure status={(int)resp.StatusCode}, body={errorBody}");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)) + RandomJitter(), _cts.Token);
                    }
                }
                catch (Exception ex) when (!_cts.IsCancellationRequested)
                {
                    await AppendLogAsync($"HTTP exception: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)) + RandomJitter(), _cts.Token);
                }
                attempt++;
            }

            if (success)
            {
                await TrimWalAsync(itemCount);
                var now = DateTime.UtcNow;
                _lastUploads.Insert(0, now);
                if (_lastUploads.Count > 10) _lastUploads.RemoveAt(10);
                UploadCompleted?.Invoke(now, itemCount);
                await AppendLogAsync($"Session upload successful items={itemCount}");
            }
            else
            {
                await AppendLogAsync($"Session upload failed after {attempt} attempts");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _workerTask.Wait(2000); } catch { }
            _cts.Dispose();
            _walLock.Dispose();
        }
    }
}
