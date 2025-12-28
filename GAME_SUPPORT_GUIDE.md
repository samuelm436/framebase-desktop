# Game Support Guide - Framebase System

## Übersicht
Das Framebase-System ist jetzt vollständig erweiterbar für neue Spiele. Alle Upload- und Validierungsfunktionen sind modular aufgebaut.

## Unterstützte Spiele (aktuell)
✅ **CS2** - Vollständig implementiert
✅ **Fortnite** - Vollständig implementiert  
✅ **Forza Horizon 5** - Vollständig implementiert

## Neues Spiel hinzufügen

### 1. Config-Parsing erweitern (FpsUploader.cs)

```csharp
// In IsGameSupportedForValidation()
"neues spiel" => true,

// In GetGameConfigPath()  
"neues spiel" => GetNeuesSpielConfigPath(),

// In ParseConfigValue()
"neues spiel" => ParseNeuesSpielConfigValue(configLines, configPath, key),
```

### 2. Preset-URL hinzufügen (GraphicsConfigurator.cs)

```csharp
// In GetPresetUrlForGame()
"neues spiel" => "http://localhost:3000/presets/neuesspiel_video_settings.json",
```

### 3. UI-Integration (MainWindow.xaml + MainWindow.xaml.cs)

```xaml
<!-- In GameSelectionComboBox -->
<ComboBoxItem Content="Neues Spiel"/>
```

```csharp
// In ConfigureSelectedGame()
case "Neues Spiel":
    if (presetName == "Performance")
        await ConfigureNeuesSpiel("Performance");
    else if (presetName == "Quality")
        await ConfigureNeuesSpiel("Quality");
    break;
```

### 4. Aktivitätserkennung (InputActivityMonitor.cs)

```csharp
// In GAME_PROCESSES array
private static readonly string[] GAME_PROCESSES = { 
    "cs2", 
    "FortniteClient-Win64-Shipping", 
    "ForzaHorizon5",
    "NeuesSpielProcess" // <-- Hier hinzufügen
};
```

### 5. GraphicsConfigurator Methoden hinzufügen

```csharp
public string GetNeuesSpielConfigPath()
{
    // Spiel-spezifische Pfadlogik
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                       "NeuesSpiel", "config.xml");
}

public async Task<bool> UpdateNeuesSpielConfigAsync(string configPath, string presetName)
{
    // Spiel-spezifische Config-Update-Logik
    // Siehe UpdateForzaHorizon5ConfigAsync() als Referenz
}

private async Task ConfigureNeuesSpiel(string presetName)
{
    // Ähnlich wie ConfigureForzaHorizon5()
    // Pfad ermitteln, UpdateNeuesSpielConfigAsync() aufrufen
}
```

### 6. Preset-Datei erstellen

```json
// fps-webapp/public/presets/neuesspiel_video_settings.json
{
  "Performance": {
    "setting1": "value1",
    "setting2": "value2"
  },
  "Quality": {
    "setting1": "value3", 
    "setting2": "value4"
  }
}
```

## Vorteile des neuen Systems

### ✅ Robustheit
- Try-catch Blöcke um alle kritischen Operationen
- Graceful Fallbacks bei Fehlern
- Keine Hard-Coded Game-Listen mehr

### ✅ Erweiterbarkeit  
- Neue Spiele brauchen nur wenige Code-Änderungen
- Klare Separation of Concerns
- Modular aufgebaute Parser-Funktionen

### ✅ Wartbarkeit
- Einheitliche Code-Struktur für alle Spiele
- Zentrale Konfiguration von URLs und Pfaden
- Einfaches Debugging durch strukturierte Helper-Methoden

### ✅ Skalierbarkeit
- System kann beliebig viele Spiele handhaben
- Performance-optimiert durch Switch-Expressions
- Minimaler Memory-Footprint

## Debug-Features

Das System erstellt automatisch Debug-Dateien:
- `debug_[game]_path_[setting].txt` - Config-Pfad Informationen
- `debug_[game]_preset_match_[setting].txt` - Preset-Matching Resultate

Diese helfen bei der Fehlersuche und beim Hinzufügen neuer Spiele.

## Error-Handling

- **Config nicht gefunden**: Upload wird trotzdem ausgeführt, aber ohne Validierung
- **Preset nicht verfügbar**: Fallback auf CS2-Presets
- **Parsing-Fehler**: Detaillierte Debug-Logs für Analyse
- **Netzwerk-Fehler**: Leere Preset-Dictionary wird zurückgegeben

## Testing-Checklist für neue Spiele

- [ ] Config-Pfad wird korrekt ermittelt
- [ ] Preset-Download funktioniert  
- [ ] Config-Parsing extrahiert alle Keys
- [ ] Preset-Matching funktioniert bei Übereinstimmung
- [ ] Upload wird blockiert bei Nicht-Übereinstimmung
- [ ] UI-Integration (ComboBox, Buttons) funktioniert
- [ ] Aktivitätserkennung zeigt "Aktiv" an
- [ ] Debug-Dateien werden erstellt
