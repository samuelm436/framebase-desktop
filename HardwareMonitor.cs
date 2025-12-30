using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

namespace FramebaseApp
{
    public class HardwareMonitor
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private ulong _totalRamMB;

        public HardwareMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // Get Total RAM for percentage calc
                _totalRamMB = GetTotalRamMB();
                
                // First call always returns 0
                _cpuCounter.NextValue();
                _ramCounter.NextValue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error init hardware monitor: {ex.Message}");
            }
        }

        private ulong GetTotalRamMB()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                ulong total = 0;
                foreach (var obj in searcher.Get())
                {
                    total += Convert.ToUInt64(obj["Capacity"]);
                }
                return total / (1024 * 1024);
            }
            catch
            {
                return 16384; // Fallback 16GB
            }
        }

        public (float cpuLoad, float ramLoad, float ramUsed, float ramTotal) GetMetrics()
        {
            float cpu = 0;
            float ramFree = 0;

            try
            {
                if (_cpuCounter != null) cpu = _cpuCounter.NextValue();
                if (_ramCounter != null) ramFree = _ramCounter.NextValue();
            }
            catch { }

            float ramUsed = _totalRamMB - ramFree;
            float ramLoad = (ramUsed / _totalRamMB) * 100;

            return (cpu, ramLoad, ramUsed / 1024f, _totalRamMB / 1024f);
        }
    }
}
