using System;
using System.IO;
using FramebaseApp;

namespace framebase_app
{
    public static class SetupState
    {
        private static string GetFlagPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Framebase");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "setup_completed.flag");
        }

        public static bool IsSetupCompleted()
        {
            try
            {
                // Check if device has a valid token using PairingService
                var pairingService = new PairingService();
                bool hasToken = !string.IsNullOrEmpty(pairingService.DeviceToken);
                
                // If no token, setup is not complete regardless of flag
                if (!hasToken) return false;

                // Check if setup flag exists
                string flag = GetFlagPath();
                return File.Exists(flag);
            }
            catch
            {
                return false;
            }
        }

        public static void MarkCompleted()
        {
            try
            {
                string flag = GetFlagPath();
                File.WriteAllText(flag, DateTime.UtcNow.ToString("o"));
            }
            catch
            {
                // ignore
            }
        }

        public static void Reset()
        {
            try
            {
                string flag = GetFlagPath();
                if (File.Exists(flag)) File.Delete(flag);
            }
            catch
            {
                // ignore
            }
        }
    }
}
