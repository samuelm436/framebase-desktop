using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class FpsUploader
    {
        private readonly HttpClient _http = new();
        private readonly List<double> _fpsBuffer = new();
        private readonly object _lock = new();

        public void AddFps(double fps)
        {
            lock (_lock) { _fpsBuffer.Add(fps); }
        }

        public (double avgFps, double onePercentLow) GetStats()
        {
            lock (_lock)
            {
                if (_fpsBuffer.Count == 0) return (0, 0);
                
                double avg = _fpsBuffer.Average();
                
                // Need at least 10 samples for meaningful 1% low calculation
                if (_fpsBuffer.Count < 10)
                {
                    // For small sample sizes, return the minimum FPS
                    double min = _fpsBuffer.Min();
                    return (Math.Round(avg, 1), Math.Round(min, 1));
                }
                
                var sorted = _fpsBuffer.OrderBy(x => x).ToArray();
                int worstCount = Math.Max(1, sorted.Length / 100);
                double onePercentLow = sorted.Take(worstCount).Average();
                
                return (Math.Round(avg, 1), Math.Round(onePercentLow, 1));
            }
        }

        public void Clear()
        {
            lock (_lock) { _fpsBuffer.Clear(); }
        }

        public async Task<string> UploadAsync(string cpuId, string gpuId, string cpuName, string gpuName, string game, string setting, string resolution, int durationSeconds)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] UploadAsync called: game={game}, cpu={cpuName}, gpu={gpuName}, duration={durationSeconds}s\n"
                );
            }
            catch { }
            
            var (avgFps, onePercentLow) = GetStats();
            if (avgFps == 0) return "No data";

            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] Downloading preset config for game: {game.ToLower()}\n"
                );
            }
            catch { }

            var presetDict = await GraphicsConfigurator.DownloadPresetConfigAsync(game.ToLower());
            var status = await GraphicsConfigurator.CheckPresetStatusAsync(game.ToLower());
            
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] Preset check: matched={status.PresetMatched}, name={status.MatchedPresetName}\n"
                );
            }
            catch { }
            
            if (!status.PresetMatched || string.IsNullOrEmpty(status.MatchedPresetName))
                return "Preset mismatch";

            if (!presetDict.TryGetValue(status.MatchedPresetName, out var clientPreset))
                return "Preset not found";

            // Read device token
            string? deviceToken = null;
            try
            {
                if (File.Exists("device_token.json"))
                {
                    deviceToken = File.ReadAllText("device_token.json").Trim();
                }
            }
            catch { }

            if (string.IsNullOrEmpty(deviceToken))
            {
                return "Not paired: Missing device token";
            }

            int sampleCount;
            lock (_lock) { sampleCount = _fpsBuffer.Count; }

            // Simple payload matching API expectations
            var payload = new
            {
                deviceToken = deviceToken,
                cpuId = string.IsNullOrEmpty(cpuId) ? "Unknown" : cpuId,
                gpuId = string.IsNullOrEmpty(gpuId) ? "Unknown" : gpuId,
                cpuName = string.IsNullOrEmpty(cpuName) ? "Unknown" : cpuName,
                gpuName = string.IsNullOrEmpty(gpuName) ? "Unknown" : gpuName,
                game = game,
                setting = setting,
                avgFps = avgFps,
                lows = onePercentLow,  // API expects "lows" not "onePercentLow"
                resolution = string.IsNullOrEmpty(resolution) ? "Unknown" : resolution,
                duration = durationSeconds
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] Posting to API...\n"
                );
            }
            catch { }

            try
            {
                var response = await _http.PostAsync("https://framebase.gg/api/fps", content);
                
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                        $"[{DateTime.Now:HH:mm:ss}] API Response: {response.StatusCode}\n"
                    );
                }
                catch { }
                
                return response.IsSuccessStatusCode ? "Upload successful" : $"Upload failed: {response.StatusCode}";
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_debug.log"),
                        $"[{DateTime.Now:HH:mm:ss}] Upload exception: {ex.Message}\n"
                    );
                }
                catch { }
                
                return $"Upload error: {ex.Message}";
            }
        }
    }
}
