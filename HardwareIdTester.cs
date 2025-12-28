// Hardware-ID Debug Tool
// Dieses kleine Tool kann verwendet werden, um die Hardware-IDs zu testen
using System;
using FramebaseApp;

namespace FramebaseApp.Debug
{
    public class HardwareIdTester
    {
        public static void RunTest()
        {
            Console.WriteLine("=== Framebase Hardware-ID Debug Tool ===\n");
            
            try
            {
                Console.WriteLine(SystemInfoHelper.GetAllHardwareInfo());
                
                Console.WriteLine("\nDrücken Sie eine beliebige Taste zum Beenden...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }
    }
}
