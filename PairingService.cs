using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class PairingService
    {
        public string? DeviceToken { get; private set; }
        public string? ConnectedUserEmail { get; private set; }

        public async Task<string> PairDeviceAsync(string code)
        {
            if (string.IsNullOrEmpty(code)) return "Pairing code missing!";
            try
            {
                // Sammle Systeminfos inklusive Hardware-IDs für eindeutige Identifikation
                var systemInfo = new
                {
                    cpu = SystemInfoHelper.GetCpu(),
                    gpu = SystemInfoHelper.GetGpu(),
                    ram = SystemInfoHelper.GetRam(),
                    cpuId = SystemInfoHelper.GetCpuId(),
                    gpuId = SystemInfoHelper.GetGpuId()
                };

                using var client = new HttpClient();
                var payload = JsonSerializer.Serialize(new { code, systemInfo });
                var resp = await client.PostAsync("https://framebase.gg/api/pair-device",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        // Try common token property names
                        string? token = null;
                        if (doc.RootElement.TryGetProperty("deviceToken", out var dt) && dt.ValueKind == JsonValueKind.String)
                            token = dt.GetString();
                        else if (doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                            token = t.GetString();
                        else if (doc.RootElement.TryGetProperty("accessToken", out var at) && at.ValueKind == JsonValueKind.String)
                            token = at.GetString();

                        if (!string.IsNullOrEmpty(token))
                        {
                            DeviceToken = token;
                            // write raw token
                            File.WriteAllText("device_token.json", DeviceToken);
                            return "Pairing successful!";
                        }

                        // If response root is string token
                        if (doc.RootElement.ValueKind == JsonValueKind.String)
                        {
                            token = doc.RootElement.GetString();
                            if (!string.IsNullOrEmpty(token))
                            {
                                DeviceToken = token;
                                File.WriteAllText("device_token.json", DeviceToken);
                                return "Pairing successful!";
                            }
                        }

                        return "Pairing failed: Token not found in response.";
                    }
                    catch (JsonException)
                    {
                        // fallback: try to extract ascii token from body
                        var cleaned = System.Text.RegularExpressions.Regex.Match(body ?? string.Empty, "[A-Za-z0-9_-]{16,}");
                        if (cleaned.Success)
                        {
                            DeviceToken = cleaned.Value;
                            File.WriteAllText("device_token.json", DeviceToken);
                            return "Pairing successful (Fallback)";
                        }
                        return "Pairing failed: Invalid server response.";
                    }
                }
                else
                {
                    return $"Pairing failed: {resp.StatusCode} - {body}";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void EnsureDeviceTokenLoaded()
        {
            if (!string.IsNullOrEmpty(DeviceToken)) return;
            if (!File.Exists("device_token.json")) return;
            try
            {
                var content = File.ReadAllText("device_token.json").Trim();
                // If file contains JSON object with token property
                if ((content.StartsWith("{") && content.EndsWith("}")))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("deviceToken", out var dt) && dt.ValueKind == JsonValueKind.String)
                        {
                            DeviceToken = dt.GetString();
                            return;
                        }
                        if (doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            DeviceToken = t.GetString();
                            return;
                        }
                    }
                    catch { }
                }

                // otherwise assume file contains raw token
                if (!string.IsNullOrEmpty(content)) DeviceToken = content;
            }
            catch { }
        }

        public async Task<(bool Success, string Email, string Message)> GetConnectedUserInfoAsync()
        {
            try
            {
                EnsureDeviceTokenLoaded();

                if (string.IsNullOrEmpty(DeviceToken))
                {
                    return (false, "", "Kein Device Token vorhanden");
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {DeviceToken}");

                var response = await client.GetAsync("https://framebase.gg/api/user-info");
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                        {
                            ConnectedUserEmail = em.GetString();
                            return (true, ConnectedUserEmail ?? "", "Benutzerinfo abgerufen");
                        }

                        // try nested object 'user.email'
                        if (doc.RootElement.TryGetProperty("user", out var userElem) && userElem.ValueKind == JsonValueKind.Object)
                        {
                            if (userElem.TryGetProperty("email", out var em2) && em2.ValueKind == JsonValueKind.String)
                            {
                                ConnectedUserEmail = em2.GetString();
                                return (true, ConnectedUserEmail ?? "", "Benutzerinfo abgerufen");
                            }
                        }

                        return (false, "", "Email nicht gefunden in Antwort");
                    }
                    catch (JsonException)
                    {
                        // fallback regex
                        var match = System.Text.RegularExpressions.Regex.Match(body ?? string.Empty, @"""email""
                            \s*:\s*""([^""]+)""");
                        if (match.Success)
                        {
                            ConnectedUserEmail = match.Groups[1].Value;
                            return (true, ConnectedUserEmail, "User info retrieved (Fallback)");
                        }
                        return (false, "", "Invalid user info response");
                    }
                }
                else
                {
                    return (false, "", $"Error fetching user info: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, "", $"Error: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> UnpairAsync()
        {
            try
            {
                EnsureDeviceTokenLoaded();

                if (string.IsNullOrEmpty(DeviceToken))
                {
                    if (File.Exists("device_token.json")) File.Delete("device_token.json");
                    return (true, "Local token file deleted (no active token found)");
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {DeviceToken}");

                try
                {
                    var response = await client.PostAsync("https://framebase.gg/api/unpair", null);
                    // delete local token regardless of server result
                    DeviceToken = null;
                    ConnectedUserEmail = null;
                    if (File.Exists("device_token.json")) File.Delete("device_token.json");

                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "Device successfully unpaired");
                    }
                    else
                    {
                        return (true, $"Local connection cleared (Server error: {response.StatusCode})");
                    }
                }
                catch (Exception ex)
                {
                    DeviceToken = null;
                    ConnectedUserEmail = null;
                    if (File.Exists("device_token.json")) File.Delete("device_token.json");
                    return (true, $"Local connection cleared (Network error: {ex.Message})");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error disconnecting: {ex.Message}");
            }
        }
    }
}
