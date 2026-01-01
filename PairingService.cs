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



        public async Task<(bool Success, string Email, string Username, string Message)> GetConnectedUserInfoAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(DeviceToken))
                {
                    return (false, "", "", "Kein Device Token vorhanden");
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
                        string? email = null;
                        string? username = null;

                        if (doc.RootElement.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                        {
                            email = em.GetString();
                        }

                        if (doc.RootElement.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
                        {
                            username = un.GetString();
                        }

                        if (!string.IsNullOrEmpty(email))
                        {
                            ConnectedUserEmail = email;
                            return (true, email, username ?? "", "Benutzerinfo abgerufen");
                        }

                        // try nested object 'user.email' and 'user.username'
                        if (doc.RootElement.TryGetProperty("user", out var userElem) && userElem.ValueKind == JsonValueKind.Object)
                        {
                            if (userElem.TryGetProperty("email", out var em2) && em2.ValueKind == JsonValueKind.String)
                            {
                                email = em2.GetString();
                            }
                            if (userElem.TryGetProperty("username", out var un2) && un2.ValueKind == JsonValueKind.String)
                            {
                                username = un2.GetString();
                            }
                            if (!string.IsNullOrEmpty(email))
                            {
                                ConnectedUserEmail = email;
                                return (true, email, username ?? "", "Benutzerinfo abgerufen");
                            }
                        }

                        return (false, "", "", "Email nicht gefunden in Antwort");
                    }
                    catch (JsonException)
                    {
                        return (false, "", "", "Invalid user info response");
                    }
                }
                else
                {
                    return (false, "", "", $"Error fetching user info: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, "", "", $"Error: {ex.Message}");
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
