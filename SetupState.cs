using System;
using System.IO;

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
