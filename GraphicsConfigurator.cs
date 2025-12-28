namespace FramebaseApp
{
    public static class GraphicsConfigurator
    {
        // Liefert den Pfad zu Valorant GameUserSettings.ini und RiotUserSettings.ini (als Tuple)
        public static (string riotUserSettingsPath, string gameUserSettingsPath) GetValorantGameUserSettingsPath()
        {
            string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string riotLocalMachineIni = System.IO.Path.Combine(localAppData, "VALORANT", "Saved", "Config", "Windows", "RiotLocalMachine.ini");
            string logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "valorant_config_log.txt");
            void Log(string msg) {
                try { System.IO.File.AppendAllText(logPath, System.DateTime.Now+": "+msg+System.Environment.NewLine); } catch { }
            }
            if (!System.IO.File.Exists(riotLocalMachineIni)) {
                Log($"[Valorant] RiotLocalMachine.ini not found: {riotLocalMachineIni}");
                return (string.Empty, string.Empty);
            }
            string? lastKnownUser = null;
            foreach (var line in System.IO.File.ReadAllLines(riotLocalMachineIni))
            {
                if (line.Trim().StartsWith("LastKnownUser="))
                {
                    var parts = line.Split('=');
                    if (parts.Length > 1)
                        lastKnownUser = parts[1].Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(lastKnownUser)) {
                Log($"[Valorant] LastKnownUser not found in {riotLocalMachineIni}");
                return (string.Empty, string.Empty);
            }
            // Suche nach Ordner, der mit LastKnownUser beginnt
            string configRoot = System.IO.Path.Combine(localAppData, "VALORANT", "Saved", "Config");
            string? userFolder = null;
            if (System.IO.Directory.Exists(configRoot))
            {
                var candidates = System.IO.Directory.GetDirectories(configRoot)
                    .Where(d => System.IO.Path.GetFileName(d).StartsWith(lastKnownUser, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (candidates.Count > 0)
                    userFolder = candidates[0];
            }
            if (string.IsNullOrEmpty(userFolder))
            {
                Log($"[Valorant] No config folder found starting with {lastKnownUser} in {configRoot}");
                return (string.Empty, string.Empty);
            }
            string userConfigDir = System.IO.Path.Combine(userFolder, "Windows");
            string riotUserSettingsPath = System.IO.Path.Combine(userConfigDir, "RiotUserSettings.ini");
            string gameUserSettingsPath = System.IO.Path.Combine(userConfigDir, "GameUserSettings.ini");
            if (!System.IO.File.Exists(riotUserSettingsPath))
                Log($"[Valorant] RiotUserSettings.ini not found: {riotUserSettingsPath}");
            if (!System.IO.File.Exists(gameUserSettingsPath))
                Log($"[Valorant] GameUserSettings.ini not found: {gameUserSettingsPath}");
            if (!System.IO.File.Exists(riotUserSettingsPath) || !System.IO.File.Exists(gameUserSettingsPath)) return (string.Empty, string.Empty);
            return (riotUserSettingsPath, gameUserSettingsPath);
        }

        // Preset-Check-Ergebnis für UI
        public class PresetCheckResult
        {
            public bool ConfigFound { get; set; }
            public bool PresetMatched { get; set; }
            public string? MatchedPresetName { get; set; }
            public List<string> AvailablePresets { get; set; } = new();
            public string ConfigPath { get; set; } = "";
            public string Message { get; set; } = "";
        }

        // Helper: normalize JsonElement to string for comparisons
        private static string ToCanon(System.Text.Json.JsonElement v)
        {
            switch (v.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return v.GetString() ?? string.Empty;
                case System.Text.Json.JsonValueKind.Number:
                    if (v.TryGetInt64(out long li)) return li.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (v.TryGetDouble(out double ld)) return ld.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    return v.ToString();
                case System.Text.Json.JsonValueKind.True:
                    return "true";
                case System.Text.Json.JsonValueKind.False:
                    return "false";
                default:
                    return v.ToString();
            }
        }

        // Prüft, ob die aktuelle Config einem Preset entspricht (für UI)
        public static async Task<PresetCheckResult> CheckPresetStatusAsync(string game, string? configContent = null)
        {
            var result = new PresetCheckResult();
            string gameKey = game.ToLower();
            string configPath = string.Empty;
            string? riotUserSettingsPath = string.Empty;
            string? gameUserSettingsPath = string.Empty;
            
            // Debug logging
            System.Diagnostics.Debug.WriteLine($"[DEBUG] CheckPresetStatusAsync für {game} ({gameKey}) mit configContent: {configContent?.Length ?? 0} Zeichen");
            
            if (gameKey == "valorant")
            {
                // Nutze die robuste Methode für Valorant-Config
                (riotUserSettingsPath, gameUserSettingsPath) = GetValorantGameUserSettingsPath();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Valorant Pfade: RiotUserSettings={riotUserSettingsPath}, GameUserSettings={gameUserSettingsPath}");
                if (string.IsNullOrEmpty(riotUserSettingsPath) || string.IsNullOrEmpty(gameUserSettingsPath))
                {
                    result.ConfigFound = false;
                    result.Message = $"Valorant configuration files not found. Searched paths: {riotUserSettingsPath} | {gameUserSettingsPath} (Please start Valorant at least once)";
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Valorant Config not found: {result.Message}");
                    return result;
                }
                configPath = riotUserSettingsPath + ";" + gameUserSettingsPath;
                result.ConfigPath = configPath;
            }
            else
            {
                configPath = gameKey switch
                {
                    "cs2" => GetCS2VideoConfigPath(),
                    "fortnite" => GetFortniteConfigPath(),
                    "forza horizon 5" => GetForzaHorizon5ConfigPath(),
                    "cp2077" or "cyberpunk 2077" => GetCp2077ConfigPath(),
                    _ => string.Empty
                };
                result.ConfigPath = configPath;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} Config Path: {configPath}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} Config exists: {System.IO.File.Exists(configPath)}");
                if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
                {
                    result.ConfigFound = false;
                    result.Message = "Configuration file not found. Please make sure the game has been started at least once.";
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} Config not found or empty");
                    return result;
                }
            }

            // Load local presets (from \\presets next to EXE)
            var presetDict = await DownloadPresetConfigAsync(gameKey);
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} Presets loaded: {presetDict?.Count ?? 0} entries");
            if (presetDict != null && presetDict.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} Available Presets: {string.Join(", ", presetDict.Keys)}");
            }
            if (presetDict == null || presetDict.Count == 0)
            {
                result.ConfigFound = true;
                result.Message = $"❌ No presets found for '{game}' (expecting 'presets' folder next to EXE)";
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {gameKey} No presets found");
                return result;
            }

            // --- Cyberpunk 2077 Special Case ---
            if (gameKey == "cp2077" || gameKey == "cyberpunk 2077")
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Cyberpunk 2077 preset check started");
                result.ConfigFound = true;
                result.AvailablePresets = presetDict.Keys.ToList();

                // Debug-Datei erstellen
                var debugPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "cyberpunk_debug.txt");
                var debugOutput = new List<string>();
                debugOutput.Add($"=== CYBERPUNK 2077 PRESET DEBUG LOG ===");
                debugOutput.Add($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                debugOutput.Add($"Config Path: {configPath}");
                debugOutput.Add($"Available Presets: {string.Join(", ", presetDict.Keys)}");
                debugOutput.Add("");

                try
                {
                    var configJson = System.IO.File.ReadAllText(configPath);
                    using var configDoc = System.Text.Json.JsonDocument.Parse(configJson);
                    var root = configDoc.RootElement;

                    // Sammle Einstellungen aus 'data' Array.
                    // Lese nur die spezifischen Gruppen, die im Preset definiert sind (effizient)
                    var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        debugOutput.Add($"=== PROCESSING GROUPS ===");
                        
                        // Definiere die benötigten Gruppen für Cyberpunk
                        var requiredGroups = new[] { "/graphics/presets", "/graphics/advanced", "/graphics/raytracing", "/graphics/basic", "/gameplay/performance" };
                        
                        foreach (var requiredGroup in requiredGroups)
                        {
                            var targetGroup = dataArr.EnumerateArray().FirstOrDefault(g =>
                                g.TryGetProperty("group_name", out var gn) && 
                                gn.ValueKind == System.Text.Json.JsonValueKind.String &&
                                string.Equals(gn.GetString(), requiredGroup, StringComparison.OrdinalIgnoreCase));
                            
                            if (targetGroup.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                            {
                                debugOutput.Add($"Group {requiredGroup} not found in UserSettings");
                                continue;
                            }
                            
                            debugOutput.Add($"Processing group: {requiredGroup}");
                            
                            if (targetGroup.TryGetProperty("options", out var opts) && opts.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                int extractedFromGroup = 0;
                                foreach (var opt in opts.EnumerateArray())
                                {
                                    if (opt.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                                    string? name = null;
                                    System.Text.Json.JsonElement valElem = default;
                                    if (opt.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == System.Text.Json.JsonValueKind.String)
                                        name = nameElem.GetString();
                                    // Prefer explicit 'value' property
                                    if (!opt.TryGetProperty("value", out valElem) && !opt.TryGetProperty("Value", out valElem))
                                    {
                                        // If no 'value', check for 'index' and try to resolve via 'values' array
                                        if (opt.TryGetProperty("index", out var idxElem) && idxElem.ValueKind == System.Text.Json.JsonValueKind.Number)
                                        {
                                            int idx = 0;
                                            try { idx = idxElem.GetInt32(); } catch { idx = 0; }
                                            if (opt.TryGetProperty("values", out var vals) && vals.ValueKind == System.Text.Json.JsonValueKind.Array)
                                            {
                                                if (idx >= 0 && idx < vals.GetArrayLength())
                                                {
                                                    valElem = vals[idx];
                                                }
                                                else
                                                {
                                                    // fallback to index number
                                                    valElem = idxElem;
                                                }
                                            }
                                            else
                                            {
                                                // no values array, keep numeric index
                                                valElem = idxElem;
                                            }
                                        }
                                        else if (!opt.TryGetProperty("default_value", out valElem))
                                        {
                                            // leave valElem undefined
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(name) && valElem.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                                    {
                                        try { 
                                            var canonValue = ToCanon(valElem);
                                            actual[name] = canonValue;
                                            extractedFromGroup++;
                                        } catch { actual[name] = valElem.ToString() ?? string.Empty; }
                                    }
                                }
                                debugOutput.Add($"  Extracted {extractedFromGroup} settings from {requiredGroup}");
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Extracted {actual.Count} settings from UserSettings.json");
                    debugOutput.Add($"=== EXTRACTED USERSETTINGS VALUES ===");
                    debugOutput.Add($"Total extracted: {actual.Count} settings");
                    foreach (var kv in actual.OrderBy(x => x.Key))
                    {
                        debugOutput.Add($"  {kv.Key} = '{kv.Value}'");
                    }
                    debugOutput.Add("");

                    // Vergleiche mit Presets - prüfe alle verfügbaren Presets und gib detaillierte Abweichungen
                    var allPresetIssues = new List<string>();
                    foreach (var preset in presetDict)
                    {
                        debugOutput.Add($"=== CHECKING PRESET: {preset.Key} ===");
                        debugOutput.Add($"Preset has {preset.Value.Count} settings");
                        
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Checking preset: {preset.Key} with {preset.Value.Count} settings");
                        bool match = true;
                        var issues = new List<string>();
                        
                        foreach (var kv in preset.Value.OrderBy(x => x.Key))
                        {
                            string key = kv.Key;
                            string expected = kv.Value; // Already normalized by DownloadPresetConfigAsync
                            debugOutput.Add($"  Checking: {key}");
                            debugOutput.Add($"    Expected: '{expected}'");

                            if (!actual.TryGetValue(key, out var actualStr))
                            {
                                debugOutput.Add($"    ❌ MISSING in UserSettings!");
                                issues.Add($"Missing key: {key}");
                                match = false;
                                continue;
                            }

                            debugOutput.Add($"    Actual: '{actualStr}'");
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] Comparing {key}: expected='{expected}' vs actual='{actualStr}'");

                            bool equal;
                            if (double.TryParse(expected, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ed) &&
                                double.TryParse(actualStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ad))
                            {
                                equal = Math.Abs(ed - ad) < 0.001;
                            }
                            else
                            {
                                equal = string.Equals(expected, actualStr, StringComparison.OrdinalIgnoreCase);
                            }

                            if (equal)
                            {
                                debugOutput.Add($"    ✅ MATCH");
                            }
                            else
                            {
                                debugOutput.Add($"    ❌ MISMATCH");
                                issues.Add($"Mismatch {key}: expected='{expected}' vs actual='{actualStr}'");
                                match = false;
                            }
                        }
                        
                        debugOutput.Add($"  Preset Result: {(match ? "✅ MATCHED" : "❌ NO MATCH")}");
                        if (!match)
                        {
                            debugOutput.Add($"  Issues: {string.Join(", ", issues)}");
                        }
                        debugOutput.Add("");

                        if (match)
                        {
                            debugOutput.Add($"🎉 FINAL RESULT: Preset '{preset.Key}' MATCHED!");
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] ✅ Preset '{preset.Key}' matched!");
                            result.PresetMatched = true;
                            result.MatchedPresetName = preset.Key;
                            result.Message = $"Cyberpunk Preset erkannt: {preset.Key}";
                            
                            // Schreibe Debug-Datei vor Return
                            System.IO.File.WriteAllLines(debugPath, debugOutput);
                            return result;
                        }

                        // Sammle Issues pro Preset für Debugging (aber prüfe weiter andere Presets)
                        if (issues.Count > 0)
                        {
                            allPresetIssues.Add($"{preset.Key}: {string.Join(", ", issues)}");
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] ❌ Preset '{preset.Key}' did not match: {string.Join(", ", issues)}");
                        }
                    }

                    // Kein Preset matched
                    debugOutput.Add($"🔴 FINAL RESULT: NO PRESET MATCHED");
                    debugOutput.Add($"All Issues:");
                    foreach (var issue in allPresetIssues)
                    {
                        debugOutput.Add($"  - {issue}");
                    }
                    
                    // Schreibe Debug-Datei
                    System.IO.File.WriteAllLines(debugPath, debugOutput);
                    result.PresetMatched = false;
                    if (allPresetIssues.Count > 0)
                        result.Message = $"Abweichungen pro Preset: {string.Join(" | ", allPresetIssues)}";
                    else
                        result.Message = "Cyberpunk Preset stimmt nicht überein";
                    return result;
                }
                catch (System.Exception ex)
                {
                    debugOutput.Add($"💥 EXCEPTION: {ex.Message}");
                    debugOutput.Add($"Stack Trace: {ex.StackTrace}");
                    System.IO.File.WriteAllLines(debugPath, debugOutput);
                    
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Cyberpunk 2077 Error: {ex.Message}");
                    result.PresetMatched = false;
                    result.Message = $"Error checking Cyberpunk config: {ex.Message}";
                    return result;
                }
            }

            result.AvailablePresets = presetDict.Keys.ToList();

            // Prüfe alle Presets, welches aktuell angewendet ist
            if (gameKey == "valorant")
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Valorant Preset-Prüfung gestartet");
                result.ConfigFound = true;
                
                // Verwende übergebenen configContent oder lese Dateien
                List<string> riotLines, gameLines;
                
                if (!string.IsNullOrEmpty(configContent) && configContent.Contains(";"))
                {
                    // configContent enthält beide Dateien getrennt durch ";"
                    var parts = configContent.Split(';', 2);
                    riotLines = parts[0].Split('\n').Select(l => l.Trim()).ToList();
                    gameLines = parts.Length > 1 ? parts[1].Split('\n').Select(l => l.Trim()).ToList() : new List<string>();
                }
                else
                {
                    // Fallback: Dateien direkt lesen
                    riotLines = System.IO.File.ReadAllLines(riotUserSettingsPath).Select(l => l.Trim()).ToList();
                    gameLines = System.IO.File.ReadAllLines(gameUserSettingsPath).Select(l => l.Trim()).ToList();
                }
                
                // Ermittle alle Keys, die aktuell in RiotUserSettings.ini stehen
                var riotKeys = new HashSet<string>(riotLines.Where(l => l.Contains("=")).Select(l => l.Split('=')[0].Trim()));
                
                foreach (var preset in presetDict)
                {
                    bool match = true;
                    foreach (var kv in preset.Value)
                    {
                        string key = kv.Key.Trim();
                        string presetValue = kv.Value.Trim();
                        string? value = null;
                        
                        // Entscheide anhand der Datei-Inhalte, wo der Key steht
                        if (riotKeys.Contains(key) || key.StartsWith("EAres"))
                        {
                            value = riotLines.FirstOrDefault(l => l.StartsWith(key + "="))?.Split('=', 2).LastOrDefault()?.Trim();
                        }
                        else
                        {
                            value = gameLines.FirstOrDefault(l => l.StartsWith(key + "="))?.Split('=', 2).LastOrDefault()?.Trim();
                        }
                        
                        if (value == null || value != presetValue)
                        {
                            match = false;
                            break;
                        }
                    }
                    
                    if (match)
                    {
                        result.PresetMatched = true;
                        result.MatchedPresetName = preset.Key;
                        result.Message = $"Preset erkannt: {preset.Key}";
                        return result;
                    }
                }
                
                // Kein Preset gematcht
                result.PresetMatched = false;
                result.Message = "Config gefunden, aber kein bekanntes Preset erkannt";
                return result;
            }
            else
            {
                var lines = System.IO.File.ReadAllLines(configPath);
                foreach (var preset in presetDict)
                {
                    bool match = true;
                    foreach (var kv in preset.Value)
                    {
                        string? value = null;
                        switch (gameKey)
                        {
                            case "cs2":
                                var line = lines.FirstOrDefault(l => l.Contains(kv.Key));
                                if (line != null)
                                {
                                    var parts = line.Split('"');
                                    // Format: "key"		"value" -> parts[3] ist der Wert
                                    value = parts.Length >= 4 ? parts[3] : null;
                                }
                                break;
                            case "fortnite":
                                var fortniteLine = lines.FirstOrDefault(l => l.StartsWith(kv.Key + "="));
                                if (fortniteLine != null)
                                {
                                    value = fortniteLine.Split('=')[1];
                                }
                                break;
                            case "forza horizon 5":
                                // FH5 uses XML format: <option id="VSync" value="0" />
                                var fh5Line = lines.FirstOrDefault(l => l.Contains($"id=\"{kv.Key}\""));
                                if (fh5Line != null)
                                {
                                    var regexMatch = System.Text.RegularExpressions.Regex.Match(fh5Line, @"value=""([^""]+)""");
                                    if (regexMatch.Success)
                                    {
                                        value = regexMatch.Groups[1].Value;
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                        if (value == null || value != kv.Value)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        result.ConfigFound = true;
                        result.PresetMatched = true;
                        result.MatchedPresetName = preset.Key;
                        result.Message = $"Preset erkannt: {preset.Key}";
                        return result;
                    }
                }
            }
            // Kein Preset matched
            result.ConfigFound = true;
            result.PresetMatched = false;
            result.Message = $"Preset stimmt nicht überein. Verfügbare Presets: {string.Join(", ", result.AvailablePresets)}";
            return result;
        }

        // --- Lokale Presets laden ---
        private static string GetPresetsDirectory()
        {
            // Primär: Ordner "presets" neben der EXE
            var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
            var primary = System.IO.Path.Combine(baseDir, "presets");
            if (System.IO.Directory.Exists(primary)) return primary;
            
            // Dev-Fallbacks
            var candidates = new[]
            {
                System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "fps-webapp", "public", "presets"),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "presets")
            };
            foreach (var c in candidates)
            {
                try { var full = System.IO.Path.GetFullPath(c); if (System.IO.Directory.Exists(full)) return full; } catch { }
            }
            return primary; // auch wenn nicht existiert
        }

        private static readonly Dictionary<string, string> PresetFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cs2"] = "cs2_video_settings.json",
            ["fortnite"] = "fortnite_video_settings.json",
            ["forza horizon 5"] = "forzahorizon5_video_settings.json",
            ["forzahorizon5"] = "forzahorizon5_video_settings.json",
            ["valorant"] = "valorant_video_settings.json",
            ["cp2077"] = "cp2077_video_settings.json",
            ["cyberpunk 2077"] = "cp2077_video_settings.json",
        };

        public static async Task<Dictionary<string, Dictionary<string, string>>> DownloadPresetConfigAsync(string game)
        {
            string key = game.ToLower().Trim();
            if (!PresetFileNames.TryGetValue(key, out var fileName))
            {
                return new();
            }
            var dir = GetPresetsDirectory();
            var filePath = System.IO.Path.Combine(dir, fileName);
            string? json = null;

            // Try local file first
            if (System.IO.File.Exists(filePath))
            {
                try { json = await System.IO.File.ReadAllTextAsync(filePath); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[Presets] Fehler beim Lesen {filePath}: {ex.Message}"); }
            }

            // HTTP fallback to web presets if local not available
            if (string.IsNullOrEmpty(json))
            {
                try
                {
                    string baseUrl = "https://framebase-web.vercel.app/presets";
                    try
                    {
                        var hintPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "presets_url.txt");
                        if (System.IO.File.Exists(hintPath))
                        {
                            var hint = (System.IO.File.ReadAllText(hintPath) ?? "").Trim();
                            if (hint.StartsWith("http", StringComparison.OrdinalIgnoreCase)) baseUrl = hint.TrimEnd('/');
                        }
                    }
                    catch { }

                    using var http = new System.Net.Http.HttpClient();
                    var url = $"{baseUrl}/{fileName}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        json = await resp.Content.ReadAsStringAsync();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Presets] HTTP {resp.StatusCode} beim Laden {url}");
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Presets] HTTP Fehler: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(json))
            {
                System.Diagnostics.Debug.WriteLine($"[Presets] Keine Presets verfügbar (lokal/HTTP) für {fileName}");
                return new();
            }

            try
            {
                if (key == "cp2077" || key == "cyberpunk 2077")
                {
                    // Unterstütze neues gruppiertes Format: { "Performance": { "/graphics/presets": [...], "/graphics/advanced": [...] } }
                    var root = System.Text.Json.JsonDocument.Parse(json!).RootElement;
                    var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var presetProp in root.EnumerateObject())
                    {
                        var inner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var val = presetProp.Value;
                        
                        if (val.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            // New efficient grouped format: iterate through each group
                            foreach (var groupProp in val.EnumerateObject())
                            {
                                if (groupProp.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var opt in groupProp.Value.EnumerateArray())
                                    {
                                        if (opt.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                                        if (!opt.TryGetProperty("name", out var nameElem) || nameElem.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                                        string name = nameElem.GetString() ?? string.Empty;
                                        
                                        // Extract the actual value directly
                                        string value = string.Empty;
                                        if (opt.TryGetProperty("value", out var valElem))
                                        {
                                            value = ToCanon(valElem);
                                        }
                                        else if (opt.TryGetProperty("index", out var idxElem))
                                        {
                                            value = ToCanon(idxElem);
                                        }
                                        
                                        inner[name] = value;
                                    }
                                }
                            }
                        }
                        result[presetProp.Name] = inner;
                    }
                    return result;
                }
                else
                {
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json!);
                    return dict ?? new();
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Presets] Fehler beim Lesen {filePath}: {ex.Message}");
                return new();
            }
        }

        // --- Config Path Methods ---
        public static string GetCS2VideoConfigPath()
        {
            string steamPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86) + @"\Steam";
            string steamId3 = GetSteamId3();
            return System.IO.Path.Combine(steamPath, "userdata", steamId3, "730", "local", "cfg", "cs2_video.txt");
        }

        public static string GetFortniteConfigPath()
        {
            string user = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(user, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
        }

        // GetForzaHorizon5ConfigPath
        public static string GetForzaHorizon5ConfigPath()
        {
            string user = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            // Microsoft Store Version
            string msStorePath = System.IO.Path.Combine(user, "Packages", "Microsoft.624F8B84B80_8wekyb3d8bbwe", "LocalCache", "Local", "User_GamingLocalStorageDirectory", "ConnectedStorage", "ForzaUserConfigSelections", "UserConfigSelections");
            // Steam Version
            string steamPath = System.IO.Path.Combine(user, "ForzaHorizon5", "User_SteamLocalStorageDirectory", "ConnectedStorage", "ForzaUserConfigSelections", "UserConfigSelections");
            if (System.IO.File.Exists(msStorePath))
                return msStorePath;
            if (System.IO.File.Exists(steamPath))
                return steamPath;
            return string.Empty;
        }

        // GetCp2077ConfigPath
        public static string GetCp2077ConfigPath()
        {
            string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string path = System.IO.Path.Combine(localAppData, "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json");
            return System.IO.File.Exists(path) ? path : string.Empty;
        }

        // GetSteamId3
        public static string GetSteamId3()
        {
            string loginVdf = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "loginusers.vdf");
            if (!System.IO.File.Exists(loginVdf)) return "*";
            string text = System.IO.File.ReadAllText(loginVdf);
            var match = System.Text.RegularExpressions.Regex.Match(text, @"""(\d{17})""");
            if (match.Success)
            {
                if (ulong.TryParse(match.Groups[1].Value, out ulong steamId64))
                {
                    ulong baseId = 76561197960265728;
                    ulong steamId3 = steamId64 - baseId;
                    return steamId3.ToString();
                }
            }
            return "*";
        }

        // GetValorantConfigPath (für RiotUserSettings.ini)
        public static string GetValorantConfigPath()
        {
            string user = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string path = System.IO.Path.Combine(user, "VALORANT", "Saved", "Config", "Windows", "RiotUserSettings.ini");
            return System.IO.File.Exists(path) ? path : string.Empty;
        }

        // UpdateCp2077VideoConfigAsync
        public static async Task<(bool success, string error)> UpdateCp2077VideoConfigAsync(string configPath, string presetName)
        {
            string lastError = string.Empty;
            try
            {
                var presets = await DownloadPresetConfigAsync("cp2077");
                if (presets.TryGetValue(presetName, out var presetDict))
                {
                    // presetDict enthält JSON-Strings als Werte
                    try
                    {
                        var jelem = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(presetDict)).RootElement;
                        return (UpdateCp2077VideoConfig(configPath, jelem), string.Empty);
                    }
                    catch (System.Exception ex)
                    {
                        lastError = $"Fehler beim Verarbeiten des Presets: {ex.Message}";
                        return (false, lastError);
                    }
                }
                lastError = $"Preset '{presetName}' nicht gefunden.";
                return (false, lastError);
            }
            catch (System.Exception ex)
            {
                lastError = $"Fehler beim Aktualisieren der Cyberpunk-Konfiguration: {ex.Message}\n{ex.StackTrace}";
                return (false, lastError);
            }
        }

        // UpdateCp2077VideoConfig (Helper)
        public static bool UpdateCp2077VideoConfig(string configPath, Dictionary<string, string> preset)
        {
            if (!System.IO.File.Exists(configPath)) return false;
            var json = System.IO.File.ReadAllText(configPath);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            bool updated = false;
            foreach (var kv in preset)
            {
                object valueToSet;
                try
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value) && kv.Value.TrimStart().StartsWith("{"))
                    {
                        valueToSet = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(kv.Value);
                    }
                    else
                    {
                        valueToSet = kv.Value;
                    }
                }
                catch
                {
                    valueToSet = kv.Value;
                }
                if (!dict.ContainsKey(kv.Key) || dict[kv.Key]?.ToString() != kv.Value)
                {
                    dict[kv.Key] = valueToSet;
                    updated = true;
                }
            }
            if (updated)
            {
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    string newJson = System.Text.Json.JsonSerializer.Serialize(dict, options);
                    System.IO.File.WriteAllText(configPath, newJson);
                }
                catch { }
            }
            return updated;
        }

        // Überladung für JsonElement-Presets (verschachtelte Werte)
        public static bool UpdateCp2077VideoConfig(string configPath, System.Text.Json.JsonElement preset)
        {
            if (!System.IO.File.Exists(configPath)) return false;
            var json = System.IO.File.ReadAllText(configPath);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            bool updated = false;
            foreach (var prop in preset.EnumerateObject())
            {
                dict[prop.Name] = prop.Value;
                updated = true;
            }
            if (updated)
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string newJson = System.Text.Json.JsonSerializer.Serialize(dict, options);
                System.IO.File.WriteAllText(configPath, newJson);
            }
            return updated;
        }

        // UpdateFortniteVideoConfigAsync
        public static async Task<bool> UpdateFortniteVideoConfigAsync(string configPath, string presetName)
        {
            var presets = await DownloadPresetConfigAsync("fortnite");
            if (!presets.TryGetValue(presetName, out var preset)) return false;
            return UpdateFortniteVideoConfig(configPath, preset);
        }

        // UpdateFortniteVideoConfig (Helper)
        public static bool UpdateFortniteVideoConfig(string configPath, Dictionary<string, string> preset)
        {
            if (!System.IO.File.Exists(configPath)) return false;
            var lines = System.IO.File.ReadAllLines(configPath).ToList();
            bool updated = false;
            foreach (var kv in preset)
            {
                int idx = lines.FindIndex(l => l.StartsWith(kv.Key + "="));
                if (idx >= 0)
                {
                    if (lines[idx] != kv.Key + "=" + kv.Value)
                    {
                        lines[idx] = kv.Key + "=" + kv.Value;
                        updated = true;
                    }
                }
                else
                {
                    lines.Add(kv.Key + "=" + kv.Value);
                    updated = true;
                }
            }
            if (updated)
                System.IO.File.WriteAllLines(configPath, lines);
            return updated;
        }

        // UpdateCS2VideoConfigAsync
        public static async Task<bool> UpdateCS2VideoConfigAsync(string configPath, string presetName)
        {
            var presets = await DownloadPresetConfigAsync("cs2");
            if (!presets.TryGetValue(presetName, out var preset)) return false;
            return UpdateCS2VideoConfig(configPath, preset);
        }

        // UpdateCS2VideoConfig (Helper)
        public static bool UpdateCS2VideoConfig(string configPath, Dictionary<string, string> preset)
        {
            if (!System.IO.File.Exists(configPath)) return false;
            var lines = System.IO.File.ReadAllLines(configPath).ToList();
            bool updated = false;
            foreach (var kv in preset)
            {
                int idx = lines.FindIndex(l => l.Contains($"\"{kv.Key}\""));
                if (idx >= 0)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(lines[idx], $"^\\s*\"{System.Text.RegularExpressions.Regex.Escape(kv.Key)}\"\\s+\".*\"");
                    if (match.Success)
                    {
                        string newLine = $"\"{kv.Key}\" \"{kv.Value}\"";
                        if (lines[idx] != newLine)
                        {
                            lines[idx] = newLine;
                            updated = true;
                        }
                    }
                }
                else
                {
                    lines.Add($"\"{kv.Key}\" \"{kv.Value}\"");
                    updated = true;
                }
            }
            if (updated)
                System.IO.File.WriteAllLines(configPath, lines);
            return updated;
        }

        // UpdateForzaHorizon5ConfigAsync
        public static async Task<bool> UpdateForzaHorizon5ConfigAsync(string configPath, string presetName)
        {
            var presets = await DownloadPresetConfigAsync("forza horizon 5");
            if (!presets.TryGetValue(presetName, out var preset)) return false;
            return UpdateForzaHorizon5Config(configPath, preset);
        }

        // UpdateForzaHorizon5Config (Helper)
        public static bool UpdateForzaHorizon5Config(string configPath, Dictionary<string, string> preset)
        {
            if (!System.IO.File.Exists(configPath)) return false;
            var doc = new System.Xml.XmlDocument();
            doc.Load(configPath);
            var selectionsNode = doc.SelectSingleNode("//selections");
            bool updated = false;
            if (selectionsNode != null)
            {
                foreach (var kv in preset)
                {
                    var optionNode = selectionsNode.SelectSingleNode($"option[@id='{kv.Key}']");
                    if (optionNode != null)
                    {
                        var valueAttr = optionNode.Attributes?["value"];
                        if (valueAttr != null && valueAttr.Value != kv.Value)
                        {
                            valueAttr.Value = kv.Value;
                            updated = true;
                        }
                    }
                }
            }
            if (updated)
                doc.Save(configPath);
            return updated;
        }

        // UpdateValorantConfigAsync
        public static async Task<bool> UpdateValorantConfigAsync(string configPath, string presetName)
        {
            var presets = await DownloadPresetConfigAsync("valorant");
            if (!presets.TryGetValue(presetName, out var preset)) return false;
            // configPath ist jetzt ein zusammengesetzter String: riotUserSettingsPath;gameUserSettingsPath
            var paths = configPath.Split(';');
            if (paths.Length != 2) return false;
            string riotUserSettingsPath = paths[0];
            string gameUserSettingsPath = paths[1];
            if (!System.IO.File.Exists(riotUserSettingsPath) || !System.IO.File.Exists(gameUserSettingsPath)) return false;
            return UpdateValorantConfig(riotUserSettingsPath, gameUserSettingsPath, preset);
        }

        // UpdateValorantConfig (Helper)
        public static bool UpdateValorantConfig(string riotUserSettingsPath, string gameUserSettingsPath, Dictionary<string, string> preset)
        {
            if (!System.IO.File.Exists(riotUserSettingsPath) || !System.IO.File.Exists(gameUserSettingsPath)) return false;
            var riotLines = System.IO.File.ReadAllLines(riotUserSettingsPath).ToList();
            var gameLines = System.IO.File.ReadAllLines(gameUserSettingsPath).ToList();
            var riotKeys = new HashSet<string>(
                riotLines.Where(l => l.Contains("=") && !l.TrimStart().StartsWith("["))
                         .Select(l => l.Split('=')[0].Trim())
            );
            bool updated = false;
            foreach (var kv in preset)
            {
                string key = kv.Key.Trim();
                string value = kv.Value.Trim();
                if (riotKeys.Contains(key) || key.StartsWith("EAres"))
                {
                    int idx = riotLines.FindIndex(l => l.StartsWith(key + "="));
                    if (idx >= 0)
                    {
                        if (riotLines[idx] != key + "=" + value)
                        {
                            riotLines[idx] = key + "=" + value;
                            updated = true;
                        }
                    }
                    else
                    {
                        riotLines.Add(key + "=" + value);
                        updated = true;
                    }
                }
                else
                {
                    int idx = gameLines.FindIndex(l => l.StartsWith(key + "="));
                    if (idx >= 0)
                    {
                        if (gameLines[idx] != key + "=" + value)
                        {
                            gameLines[idx] = key + "=" + value;
                            updated = true;
                        }
                    }
                    else
                    {
                        gameLines.Add(key + "=" + value);
                        updated = true;
                    }
                }
            }
            if (updated)
            {
                System.IO.File.WriteAllLines(riotUserSettingsPath, riotLines);
                System.IO.File.WriteAllLines(gameUserSettingsPath, gameLines);
            }
            return updated;
        }

        // AreGraphicsSettingsUnchanged
        public static bool AreGraphicsSettingsUnchanged(string gameKey, string presetName)
        {
            try
            {
                gameKey = gameKey.ToLowerInvariant();
                var presets = DownloadPresetConfigAsync(gameKey).Result;
                if (!presets.TryGetValue(presetName, out var preset))
                    return false;

                if (gameKey == "valorant")
                {
                    var (riotUserSettingsPath, gameUserSettingsPath) = GetValorantGameUserSettingsPath();
                    if (string.IsNullOrEmpty(riotUserSettingsPath) || string.IsNullOrEmpty(gameUserSettingsPath))
                        return false;

                    var riotLines = System.IO.File.ReadAllLines(riotUserSettingsPath);
                    var gameLines = System.IO.File.ReadAllLines(gameUserSettingsPath);
                    var riotKeys = new HashSet<string>(
                        riotLines.Where(l => l.Contains("=") && !l.TrimStart().StartsWith("["))
                                .Select(l => l.Split('=')[0].Trim())
                    );

                    foreach (var kv in preset)
                    {
                        string key = kv.Key.Trim();
                        string expected = kv.Value.Trim();
                        string? actual = null;
                        if (riotKeys.Contains(key) || key.StartsWith("EAres"))
                        {
                            var line = riotLines.FirstOrDefault(l => l.StartsWith(key + "="));
                            actual = line?.Substring(key.Length + 1).Trim();
                        }
                        else
                        {
                            var line = gameLines.FirstOrDefault(l => l.StartsWith(key + "="));
                            actual = line?.Substring(key.Length + 1).Trim();
                        }
                        if (actual == null || !string.Equals(actual, expected, StringComparison.Ordinal))
                            return false;
                    }
                    return true;
                }
                else if (gameKey == "cp2077" || gameKey == "cyberpunk 2077")
                {
                    string configPath = GetCp2077ConfigPath();
                    if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
                        return false;

                    var configJson = System.IO.File.ReadAllText(configPath);
                    using var configDoc = System.Text.Json.JsonDocument.Parse(configJson);
                    var root = configDoc.RootElement;
                    if (!root.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != System.Text.Json.JsonValueKind.Array)
                        return false;

                    var graphicsGroup = dataArr.EnumerateArray().FirstOrDefault(g =>
                        g.TryGetProperty("group_name", out var gn) && (gn.GetString() == "/graphics/presets" || gn.GetString() == "Graphics"));
                    if (graphicsGroup.ValueKind != System.Text.Json.JsonValueKind.Object ||
                        !graphicsGroup.TryGetProperty("options", out var options) || options.ValueKind != System.Text.Json.JsonValueKind.Array)
                        return false;

                    foreach (var kv in preset)
                    {
                        // preset value may be raw JSON like {"value":"High"}
                        string expected;
                        try
                        {
                            var presetElem = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(kv.Value);
                            System.Text.Json.JsonElement v;
                            if (presetElem.TryGetProperty("value", out v))
                            {
                                expected = v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString();
                            }
                            else
                            {
                                expected = kv.Value;
                            }
                        }
                        catch
                        {
                            expected = kv.Value;
                        }

                        var opt = options.EnumerateArray().FirstOrDefault(o =>
                            (o.TryGetProperty("name", out var nm) && nm.GetString() == kv.Key) ||
                            (o.TryGetProperty("id", out var id) && id.GetString() == kv.Key));
                        if (opt.ValueKind != System.Text.Json.JsonValueKind.Object || !opt.TryGetProperty("value", out var actualElem))
                            return false;
                        string actual = actualElem.ValueKind == System.Text.Json.JsonValueKind.String ? (actualElem.GetString() ?? string.Empty) : actualElem.ToString();
                        if (!string.Equals(actual, expected, StringComparison.Ordinal))
                            return false;
                    }
                    return true;
                }
                else
                {
                    string configPath = gameKey switch
                    {
                        "cs2" => GetCS2VideoConfigPath(),
                        "fortnite" => GetFortniteConfigPath(),
                        "forza horizon 5" => GetForzaHorizon5ConfigPath(),
                        _ => string.Empty
                    };
                    if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
                        return false;

                    var lines = System.IO.File.ReadAllLines(configPath);
                    foreach (var kv in preset)
                    {
                        string? value = null;
                        switch (gameKey)
                        {
                            case "cs2":
                                var line = lines.FirstOrDefault(l => l.Contains(kv.Key));
                                if (line != null)
                                {
                                    var parts = line.Split('"');
                                    value = parts.Length >= 4 ? parts[3] : null;
                                }
                                break;
                            case "fortnite":
                                var l2 = lines.FirstOrDefault(l => l.StartsWith(kv.Key + "="));
                                if (l2 != null) value = l2.Split('=')[1];
                                break;
                            case "forza horizon 5":
                                var l3 = lines.FirstOrDefault(l => l.Contains($"id=\"{kv.Key}\""));
                                if (l3 != null)
                                {
                                    var m = System.Text.RegularExpressions.Regex.Match(l3, @"value=""([^""]+)""");
                                    if (m.Success) value = m.Groups[1].Value;
                                }
                                break;
                        }
                        if (value == null || !string.Equals(value, kv.Value, StringComparison.Ordinal))
                            return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // Extract current resolution and preset (preset may be empty when unknown)
        public static (string resolution, string preset) GetGameResolutionAndPreset(string gameKey, string configPath)
        {
            try
            {
                gameKey = gameKey.ToLowerInvariant();
                // Valorant can pass two paths separated by ';'
                string[] paths = configPath.Contains(';') ? configPath.Split(';') : new[] { configPath };
                string ReadAll(string p) => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : string.Empty;
                string[] ReadLines(string p) => System.IO.File.Exists(p) ? System.IO.File.ReadAllLines(p) : System.Array.Empty<string>();

                switch (gameKey)
                {
                    case "cs2":
                    {
                        var lines = ReadLines(paths[0]);
                        string? w = null, h = null;
                        // CS2 uses pairs like: "setting.defaultres" "1920"
                        string GetQuoted(string key)
                        {
                            var line = lines.FirstOrDefault(l => l.Contains(key));
                            if (line == null) return string.Empty;
                            var parts = line.Split('"');
                            return parts.Length >= 4 ? parts[3] : string.Empty;
                        }
                        w = GetQuoted("setting.defaultres");
                        h = GetQuoted("setting.defaultresheight");
                        if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                            return ($"{w}x{h}", string.Empty);
                        break;
                    }
                    case "fortnite":
                    {
                        var lines = ReadLines(paths[0]);
                        string? w = null, h = null;
                        string GetIni(string key)
                        {
                            var l = lines.FirstOrDefault(x => x.StartsWith(key + "="));
                            return l != null ? l.Substring(key.Length + 1) : string.Empty;
                        }
                        w = GetIni("ResolutionSizeX");
                        h = GetIni("ResolutionSizeY");
                        if (string.IsNullOrEmpty(w) || string.IsNullOrEmpty(h))
                        {
                            w = GetIni("LastUserConfirmedResolutionSizeX");
                            h = GetIni("LastUserConfirmedResolutionSizeY");
                        }
                        if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                            return ($"{w}x{h}", string.Empty);
                        break;
                    }
                    case "forza horizon 5":
                    {
                        var lines = ReadLines(paths[0]);
                        string? w = null, h = null;
                        var lw = lines.FirstOrDefault(l => l.Contains("<ResolutionWidth"));
                        var lh = lines.FirstOrDefault(l => l.Contains("<ResolutionHeight"));
                        if (lw != null)
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(lw, @"value=""([^""]+)""");
                            if (m.Success) w = m.Groups[1].Value;
                        }
                        if (lh != null)
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(lh, @"value=""([^""]+)""");
                            if (m.Success) h = m.Groups[1].Value;
                        }
                        if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                            return ($"{w}x{h}", string.Empty);
                        break;
                    }
                    case "valorant":
                    {
                        // Search across both files
                        var allLines = paths.SelectMany(p => ReadLines(p)).ToArray();
                        string? w = null, h = null;
                        // Try common keys
                        string[] wKeys = new[] { "ResX", "resWidth", "ResolutionWidth" };
                        string[] hKeys = new[] { "ResY", "resHeight", "ResolutionHeight" };
                        foreach (var k in wKeys)
                        {
                            var l = allLines.FirstOrDefault(x => x.StartsWith(k + "=", System.StringComparison.OrdinalIgnoreCase));
                            if (l != null) { w = l.Split('=')[1].Trim(); break; }
                        }
                        foreach (var k in hKeys)
                        {
                            var l = allLines.FirstOrDefault(x => x.StartsWith(k + "=", System.StringComparison.OrdinalIgnoreCase));
                            if (l != null) { h = l.Split('=')[1].Trim(); break; }
                        }
                        if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                            return ($"{w}x{h}", string.Empty);
                        break;
                    }
                    case "cp2077":
                    case "cyberpunk 2077":
                    {
                        var json = ReadAll(paths[0]);
                        if (!string.IsNullOrEmpty(json))
                        {
                            // Try to match any common resolution pattern like 1920x1080
                            var m = System.Text.RegularExpressions.Regex.Match(json, @"\b(\d{3,5})\s*[xX,\s]+(\d{3,5})\b");
                            if (m.Success)
                                return ($"{m.Groups[1].Value}x{m.Groups[2].Value}", string.Empty);
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(json);
                                var root = doc.RootElement;
                                // heuristics: look for width/height properties
                                int width = 0, height = 0;
                                void Walk(System.Text.Json.JsonElement e)
                                {
                                    if (e.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        foreach (var prop in e.EnumerateObject())
                                        {
                                            var n = prop.Name.ToLowerInvariant();
                                            if ((n.Contains("width") || n == "w") && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                width = prop.Value.GetInt32();
                                            else if ((n.Contains("height") || n == "h") && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                height = prop.Value.GetInt32();
                                            else
                                                Walk(prop.Value);
                                        }
                                    }
                                    else if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        foreach (var x in e.EnumerateArray()) Walk(x);
                                    }
                                }
                                Walk(root);
                                if (width > 0 && height > 0)
                                    return ($"{width}x{height}", string.Empty);
                            }
                            catch { }
                        }
                        break;
                    }
                }
            }
            catch { }
            return (string.Empty, string.Empty);
        }
    }
}
