using System;
using System.IO;
using System.Reflection;

namespace framebase_app
{
    public static class ResourceExtractor
    {
        public static void ExtractResource(string resourceName, string outputFileName)
        {
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputFileName);
            
            // If file exists, we might want to overwrite it if it's a new version, 
            // but for simplicity and speed, we skip if it exists.
            // Or better: check if we can write to it (it might be locked if running).
            if (File.Exists(outputPath)) return;

            var assembly = Assembly.GetExecutingAssembly();
            
            // Try to find the resource by ending
            var resources = assembly.GetManifestResourceNames();
            string? foundResource = null;
            
            foreach (var res in resources)
            {
                if (res.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    foundResource = res;
                    break;
                }
            }

            if (foundResource == null)
            {
                // Fail silently or log? For this app, maybe just return.
                // If PresentMon is missing, the recorder will just not work, handled elsewhere.
                return; 
            }

            try
            {
                using (Stream? stream = assembly.GetManifestResourceStream(foundResource))
                {
                    if (stream == null) return;

                    using (FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract resource: {ex.Message}");
            }
        }
    }
}
