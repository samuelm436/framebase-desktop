using System;
using System.Diagnostics;
using System.Management;
using System.Linq;

namespace FramebaseApp
{
    public class HardwareMonitor : IDisposable
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private PerformanceCounter? _gpuEngineCounter;
        private PerformanceCounter? _gpuMemoryCounter;
        private ulong _totalRamMB;
        private float _cachedVramTotal = -1;
        private DateTime _lastUpdate = DateTime.MinValue;
        private HardwareMetrics _cachedMetrics = new();

        public HardwareMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                _totalRamMB = GetTotalRamMB();
                
                _cpuCounter.NextValue();
                _ramCounter.NextValue();

                InitGpuCounters();
                _cachedVramTotal = GetVramTotalGB();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HardwareMonitor Init Error: {ex.Message}");
            }
        }

        public class HardwareMetrics
        {
            public float CpuLoad { get; set; } = -1;
            public float GpuLoad { get; set; } = -1;
            public float RamLoad { get; set; } = -1;
            public float VramLoad { get; set; } = -1;
        }

        public HardwareMetrics GetMetrics()
        {
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 1000)
                return _cachedMetrics;

            _lastUpdate = DateTime.Now;

            try
            {
                var metrics = new HardwareMetrics();

                // CPU Load
                if (_cpuCounter != null)
                    metrics.CpuLoad = _cpuCounter.NextValue();

                // RAM Load
                if (_ramCounter != null && _totalRamMB > 0)
                {
                    float ramFreeMB = _ramCounter.NextValue();
                    float ramUsedMB = _totalRamMB - ramFreeMB;
                    metrics.RamLoad = (ramUsedMB / _totalRamMB) * 100f;
                }

                // GPU Load
                if (_gpuEngineCounter != null)
                {
                    try { metrics.GpuLoad = _gpuEngineCounter.NextValue(); }
                    catch { }
                }

                // VRAM Load
                if (_gpuMemoryCounter != null && _cachedVramTotal > 0)
                {
                    try
                    {
                        float vramUsedBytes = _gpuMemoryCounter.NextValue();
                        float vramUsedGB = vramUsedBytes / (1024f * 1024f * 1024f);
                        metrics.VramLoad = (vramUsedGB / _cachedVramTotal) * 100f;
                    }
                    catch { }
                }

                _cachedMetrics = metrics;
                return metrics;
            }
            catch
            {
                return _cachedMetrics;
            }
        }

        private void InitGpuCounters()
        {
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    var category = new PerformanceCounterCategory("GPU Engine");
                    var instanceNames = category.GetInstanceNames();
                    var gpu3dInstance = instanceNames.FirstOrDefault(name => name.Contains("engtype_3D"));
                    if (!string.IsNullOrEmpty(gpu3dInstance))
                    {
                        _gpuEngineCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", gpu3dInstance);
                        _gpuEngineCounter.NextValue();
                    }
                }

                if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                {
                    var category = new PerformanceCounterCategory("GPU Adapter Memory");
                    var instanceNames = category.GetInstanceNames();
                    if (instanceNames.Length > 0)
                    {
                        _gpuMemoryCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instanceNames[0]);
                        _gpuMemoryCounter.NextValue();
                    }
                }
            }
            catch { }
        }

        private ulong GetTotalRamMB()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                ulong total = 0;
                foreach (var obj in searcher.Get())
                    total += Convert.ToUInt64(obj["Capacity"]);
                return total / (1024 * 1024);
            }
            catch { return 16384; }
        }

        private float GetVramTotalGB()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var ram = Convert.ToUInt64(obj["AdapterRAM"]);
                    if (ram > 0)
                        return ram / (1024f * 1024f * 1024f);
                }
            }
            catch { }
            return -1;
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            _gpuEngineCounter?.Dispose();
            _gpuMemoryCounter?.Dispose();
        }
    }
}
