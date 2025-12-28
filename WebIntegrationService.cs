using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class WebIntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public WebIntegrationService(string baseUrl = "https://framebase-web.vercel.app")
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
        }

        public async Task<bool> SendFpsDataAsync(FpsDataModel fpsData)
        {
            try
            {
                var json = JsonSerializer.Serialize(fpsData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/desktop-upload", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send FPS data: {ex.Message}");
                return false;
            }
        }

        public async Task<WebStatsModel?> GetWebStatsAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_baseUrl}/api/fps");
                return JsonSerializer.Deserialize<WebStatsModel>(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get web stats: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class FpsDataModel
    {
        public string GameId { get; set; } = string.Empty;
        public double Fps { get; set; }
        public double Frametime { get; set; }
        public double Low1 { get; set; }
        public DateTime Timestamp { get; set; }
        public SystemSpecsModel SystemSpecs { get; set; } = new();
    }

    public class SystemSpecsModel
    {
        public string Cpu { get; set; } = string.Empty;
        public string Gpu { get; set; } = string.Empty;
        public string Ram { get; set; } = string.Empty;
    }

    public class WebStatsModel
    {
        public double LatestFps { get; set; }
        public double AverageFps { get; set; }
        public int Sessions { get; set; }
    }
}