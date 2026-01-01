using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Win32;

namespace FramebaseApp
{
    public class HardwareMonitor : IDisposable
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private List<PerformanceCounter> _gpuEngineCounters = new();
        private PerformanceCounter? _gpuMemoryCounter;
        private ulong _vramTotalBytes = 0;
        private ulong _totalRamMB;
        private DateTime _lastUpdate = DateTime.MinValue;
        private HardwareMetrics _cachedMetrics = new();
        private DateTime _lastVramCheck = DateTime.MinValue;

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
                _vramTotalBytes = GetVramTotalBytes();
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

                // VRAM Load - Task Manager uses Dedicated Usage / Physical VRAM
                if (_vramTotalBytes > 0)
                {
                    // Re-initialize VRAM counter if needed (every 5 seconds check)
                    if (_gpuMemoryCounter == null && (DateTime.Now - _lastVramCheck).TotalSeconds > 5)
                    {
                        _lastVramCheck = DateTime.Now;
                        InitVramCounter();
                    }

                    if (_gpuMemoryCounter != null)
                    {
                        try
                        {
                            float vramUsedBytes = _gpuMemoryCounter.NextValue();
                            if (vramUsedBytes >= 0 && vramUsedBytes <= _vramTotalBytes)
                            {
                                metrics.VramLoad = (vramUsedBytes / _vramTotalBytes) * 100f;
                            }
                            else if (vramUsedBytes < 0)
                            {
                                // Counter returned invalid value, re-init on next cycle
                                _gpuMemoryCounter?.Dispose();
                                _gpuMemoryCounter = null;
                            }
                        }
                        catch
                        {
                            // Counter failed, re-init on next cycle
                            _gpuMemoryCounter?.Dispose();
                            _gpuMemoryCounter = null;
                        }
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
                // GPU Load - Get ALL 3D engine instances (Task Manager uses max)
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    var category = new PerformanceCounterCategory("GPU Engine");
                    var instanceNames = category.GetInstanceNames();
                    
                    var gpu3dInstances = instanceNames
                        .Where(name => name.Contains("engtype_3D"))
                        .ToList();
                    
                    foreach (var instance in gpu3dInstances)
                    {
                        try
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                            counter.NextValue();
                            _gpuEngineCounters.Add(counter);
                        }
                        catch { }
                    }
                }

                InitVramCounter();
            }
            catch { }
        }

        private void InitVramCounter()
        {
            try
            {
                // VRAM Usage - Dedicated Usage (same as Task Manager)
                if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                {
                    var category = new PerformanceCounterCategory("GPU Adapter Memory");
                    var instanceNames = category.GetInstanceNames();
                    
                    if (instanceNames.Length > 0)
                    {
                        _gpuMemoryCounter?.Dispose();
                        _gpuMemoryCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instanceNames[0]);
                        _gpuMemoryCounter.NextValue(); // Prime the counter
                    }
                }
            }
            catch { }
        }

        private ulong GetVramTotalBytes()
        {
            // Try Registry first (most accurate for modern GPUs)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                if (key != null)
                {
                    var memSize = key.GetValue("HardwareInformation.qwMemorySize");
                    if (memSize != null && memSize is long qwMem)
                    {
                        return (ulong)qwMem;
                    }
                }
            }
            catch { }

            // Fallback to WMI
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var ram = Convert.ToUInt64(obj["AdapterRAM"]);
                    if (ram > 0)
                        return ram;
                }
            }
            catch { }

            return 0;
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
