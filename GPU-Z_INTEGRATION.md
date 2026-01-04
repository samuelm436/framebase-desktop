# GPU-Z Portable Integration

## Automatische Installation

1. Download GPU-Z Portable: https://www.techpowerup.com/download/techpowerup-gpu-z/
2. Erstelle Ordner: `framebase-app/tools/`
3. Kopiere `GPU-Z.exe` in den tools Ordner

## Struktur

```
framebase-app/
├── framebase-app.exe
├── tools/
│   └── GPU-Z.exe          <-- Portable Version hier platzieren
```

## Funktionsweise

- **Automatischer Start**: GPU-Z wird beim App-Start automatisch im Hintergrund gestartet
- **Komplett unsichtbar**: Kein Fenster, kein Tray-Icon, keine User-Interaktion nötig
- **Automatisches Beenden**: GPU-Z wird beim Schließen der App automatisch beendet
- **Fallback**: Wenn GPU-Z nicht verfügbar ist, nutzt die App WMI (Windows Management)

## Vorteile

✅ **Präzise GPU-Erkennung**: PCI Device ID statt Hash-Raten  
✅ **Automatische VRAM-Erkennung**: 4GB vs 8GB Varianten werden korrekt erkannt  
✅ **Hersteller-Info**: ASUS, MSI, Gigabyte etc. werden unterschieden  
✅ **Echtzeit-Daten**: GPU-Takt, Temperatur, Auslastung während Gaming  
✅ **Zero User Friction**: Nutzer merkt nichts davon

## Download GPU-Z Portable

https://www.techpowerup.com/download/techpowerup-gpu-z/

**Wichtig**: Portable Version verwenden (keine Installation nötig)
