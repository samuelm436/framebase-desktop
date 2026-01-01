using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using framebase_app;

namespace FramebaseApp
{
    public class PairingService
    {
        public const string TOKEN_FILE = "device_token.dat";
        public const string TOKEN_FILE_LEGACY = "device_token.json";
        public string? DeviceToken { get; private set; }
        public string? ConnectedUserEmail { get; private set; }

        public PairingService()
        {
            LoadSecureToken();
        }

        public string GeneratePairingLink()
        {
            var deviceId = GenerateDeviceId();
            return $"https://framebase.gg/pair-device?deviceId={Uri.EscapeDataString(deviceId)}";
        }

        public async Task<(bool Success, string Message, string? Email)> CheckPairingStatusAsync(string deviceId)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"https://framebase.gg/api/check-pairing?deviceId={Uri.EscapeDataString(deviceId)}");
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        
                        if (doc.RootElement.TryGetProperty("paired", out var paired) && paired.GetBoolean())
                        {
                            string? token = null;
                            if (doc.RootElement.TryGetProperty("deviceToken", out var dt))
                                token = dt.GetString();
                            
                            string? email = null;
                            if (doc.RootElement.TryGetProperty("email", out var em))
                                email = em.GetString();

                            if (!string.IsNullOrEmpty(token))
                            {
                                DeviceToken = token;
                                ConnectedUserEmail = email;
                                SaveSecureToken(token);
                                return (true, "Successfully paired!", email);
                            }
                        }
                        
                        return (false, "Pairing pending...", null);
                    }
                    catch (JsonException)
                    {
                        return (false, "Invalid server response", null);
                    }
                }
                else
                {
                    return (false, $"Server error: {response.StatusCode}", null);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", null);
            }
        }

        private string GenerateDeviceId()
        {
            var cpuId = SystemInfoHelper.GetCpuId() ?? "unknown";
            var gpuId = SystemInfoHelper.GetGpuId() ?? "unknown";
            var combined = $"{cpuId}_{gpuId}_{Environment.MachineName}";
            
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private void SaveSecureToken(string token)
        {
            try
            {
                var tokenBytes = Encoding.UTF8.GetBytes(token);
                var encryptedBytes = ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(TOKEN_FILE, encryptedBytes);
                
                if (File.Exists("device_token.json"))
                    File.Delete("device_token.json");
            }
            catch { }
        }

        private void LoadSecureToken()
        {
            try
            {
                if (File.Exists(TOKEN_FILE))
                {
                    var encryptedBytes = File.ReadAllBytes(TOKEN_FILE);
                    var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    DeviceToken = Encoding.UTF8.GetString(decryptedBytes);
                }
                else if (File.Exists("device_token.json"))
                {
                    var content = File.ReadAllText("device_token.json").Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        DeviceToken = content;
                        SaveSecureToken(content);
                    }
                }
            }
            catch { }
        }

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
                            SaveSecureToken(token);
                            return "Pairing successful!";
                        }

                        // If response root is string token
                        if (doc.RootElement.ValueKind == JsonValueKind.String)
                        {
                            token = doc.RootElement.GetString();
                            if (!string.IsNullOrEmpty(token))
                            {
                                DeviceToken = token;
                                SaveSecureToken(token);
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
                            SaveSecureToken(cleaned.Value);
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

        public async Task<(bool Success, string Email, string Message)> GetConnectedUserInfoAsync()
        {
            try
            {
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
                if (string.IsNullOrEmpty(DeviceToken))
                {
                    DeleteAllTokenFiles();
                    SetupState.Reset(); // Reset setup state to force setup on next start
                    return (true, "Local token file deleted (no active token found)");
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {DeviceToken}");

                try
                {
                    var response = await client.PostAsync("https://framebase.gg/api/unpair", null);
                    DeviceToken = null;
                    ConnectedUserEmail = null;
                    DeleteAllTokenFiles();
                    SetupState.Reset(); // Reset setup state to force setup on next start

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
                    DeleteAllTokenFiles();
                    SetupState.Reset(); // Reset setup state to force setup on next start
                    return (true, $"Local connection cleared (Network error: {ex.Message})");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error disconnecting: {ex.Message}");
            }
        }

        private void DeleteAllTokenFiles()
        {
            try
            {
                if (File.Exists(TOKEN_FILE)) File.Delete(TOKEN_FILE);
                if (File.Exists(TOKEN_FILE_LEGACY)) File.Delete(TOKEN_FILE_LEGACY);
            }
            catch { }
        }
    }
}
