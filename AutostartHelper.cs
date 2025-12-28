using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace framebase_app
{
    [SupportedOSPlatform("windows")]
    public static class AutostartHelper
    {
        private static readonly string AppName = "Framebase";
        private static readonly string AppPath = Assembly.GetExecutingAssembly().Location;
        private static readonly RegistryKey? StartupKey = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

        public static bool IsAutostartEnabled()
        {
            try
            {
                if (StartupKey?.GetValue(AppName) != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Fehler beim Lesen der Registry
            }
            return false;
        }

        public static void EnableAutostart()
        {
            try
            {
                // Verwende den Pfad zur ausführbaren Datei
                string exePath = AppPath;
                if (exePath.EndsWith(".dll"))
                {
                    // Für .NET 5+ Apps, verwende den .exe Pfad
                    exePath = Path.ChangeExtension(exePath, ".exe");
                }

                StartupKey?.SetValue(AppName, $"\"{exePath}\"");
            }
            catch (Exception)
            {
                // Fehler beim Schreiben in die Registry
            }
        }

        public static void DisableAutostart()
        {
            try
            {
                StartupKey?.DeleteValue(AppName, false);
            }
            catch (Exception)
            {
                // Fehler beim Löschen aus der Registry
            }
        }

        public static void Dispose()
        {
            StartupKey?.Dispose();
        }
    }
}
