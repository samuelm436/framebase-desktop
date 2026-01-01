using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Collections.Generic;

namespace FramebaseApp
{
    public class HardwareMonitor : IDisposable
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private List<PerformanceCounter> _gpuEngineCounters = new();
        private PerformanceCounter? _gpuMemoryCounter;
        private float _vramTotalGB = -1;
        private ulong _totalRamMB;
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
                _vramTotalGB = GetVramTotalGB();
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

                // GPU Load - Max of all 3D engines (like Task Manager shows highest utilization)
                if (_gpuEngineCounters.Count > 0)
                {
                    try 
                    { 
                        float maxLoad = 0;
                        foreach (var counter in _gpuEngineCounters)
                        {
                            try
                            {
                                float value = counter.NextValue();
                                if (value > maxLoad)
                                    maxLoad = value;
                            }
                            catch { }
                        }
                        metrics.GpuLoad = maxLoad;
                    }
                    catch { }
                }

                // VRAM Load - Use Physical VRAM Total (like Task Manager)
                if (_gpuMemoryCounter != null && _vramTotalGB > 0)
                {
                    try
                    {
                        float vramUsedBytes = _gpuMemoryCounter.NextValue();
                        float vramUsedGB = vramUsedBytes / (1024f * 1024f * 1024f);
                        metrics.VramLoad = (vramUsedGB / _vramTotalGB) * 100f;
                        
                        // Debug output
                        Console.WriteLine($"VRAM Used: {vramUsedGB:F2} GB / {_vramTotalGB:F2} GB = {metrics.VramLoad:F1}%");
                    }
                    catch (Exception ex) 
                    { 
                        Console.WriteLine($"VRAM Read Error: {ex.Message}");
                    }
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
                // GPU Load - Get ALL 3D engine instances and average them (same as Task Manager)
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    var category = new PerformanceCounterCategory("GPU Engine");
                    var instanceNames = category.GetInstanceNames();
                    
                    // Get all 3D engine instances (Task Manager averages all of them)
                    var gpu3dInstances = instanceNames
                        .Where(name => name.Contains("engtype_3D"))
                        .ToList();
                    
                    Console.WriteLine($"Found {gpu3dInstances.Count} GPU 3D engines:");
                    foreach (var instance in gpu3dInstances)
                    {
                        try
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                            counter.NextValue(); // Initialize
                            _gpuEngineCounters.Add(counter);
                            Console.WriteLine($"  - {instance}");
                        }
                        catch { }
                    }
                }

                // VRAM Usage - Only Dedicated Usage (Task Manager uses physical VRAM total from WMI)
                if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                {
                    var category = new PerformanceCounterCategory("GPU Adapter Memory");
                    var instanceNames = category.GetInstanceNames();
                    
                    if (instanceNames.Length > 0)
                    {
                        var instance = instanceNames[0];
                        
                        // Dedicated Usage (bytes in use) - same as Task Manager
                        _gpuMemoryCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance);
                        _gpuMemoryCounter.NextValue();
                        
                        Console.WriteLine($"VRAM Counter: {instance}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GPU Counter Init Error: {ex.Message}");
            }
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
                    {
                        float totalGB = ram / (1024f * 1024f * 1024f);
                        Console.WriteLine($"Physical VRAM Total: {totalGB:F2} GB");
                        return totalGB;
                    }
                }
            }
            catch { }
            return -1;
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

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            foreach (var counter in _gpuEngineCounters)
                counter?.Dispose();
            _gpuMemoryCounter?.Dispose();
        }
    }
}
