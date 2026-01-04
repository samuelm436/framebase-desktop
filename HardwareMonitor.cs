using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;

namespace FramebaseApp
{
    public class HardwareMonitor : IDisposable
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private ulong _totalRamMB;
        private DateTime _lastUpdate = DateTime.MinValue;
        private HardwareMetrics _cachedMetrics = new();
        
        // LibreHardwareMonitor
        private Computer? _computer;
        private IHardware? _gpu;
        private IHardware? _cpu;

        public HardwareMonitor()
        {
            try
            {
                // Initialize LibreHardwareMonitor
                _computer = new Computer
                {
                    IsCpuEnabled = true, // Try CPU for temps (might fail without admin/driver)
                    IsGpuEnabled = true, // GPU usually works via NVAPI/ADL without kernel driver
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false
                };

                try
                {
                    _computer.Open();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HardwareMonitor] LHM Open Error (Driver issue?): {ex.Message}");
                    // If driver fails, we might still get some data or partial init
                }

                // Find GPU
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia || 
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        _gpu = hardware;
                        System.Diagnostics.Debug.WriteLine($"[HardwareMonitor] Found GPU: {_gpu.Name}");
                        break; // Use first GPU
                    }
                }

                // Find CPU
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        _cpu = hardware;
                        break;
                    }
                }

                // Fallback counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                _totalRamMB = GetTotalRamMB();
                
                try { _cpuCounter.NextValue(); } catch {}
                try { _ramCounter.NextValue(); } catch {}
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
            
            // New detailed metrics
            public float GpuTemp { get; set; } = -1;
            public float GpuCoreClock { get; set; } = -1;
            public float GpuMemoryClock { get; set; } = -1;
            public float CpuTemp { get; set; } = -1;
            public float CpuPower { get; set; } = -1;
        }

        public HardwareMetrics GetMetrics()
        {
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 1000)
                return _cachedMetrics;

            _lastUpdate = DateTime.Now;
            var metrics = new HardwareMetrics();

            try
            {
                // Update LHM
                if (_gpu != null) _gpu.Update();
                if (_cpu != null) _cpu.Update();

                // 1. CPU Load & Temp
                if (_cpu != null)
                {
                    // Load
                    var loadSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                    if (loadSensor != null && loadSensor.Value.HasValue)
                        metrics.CpuLoad = loadSensor.Value.Value;
                    
                    // Temp (Package or Average)
                    var tempSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package")) 
                                     ?? _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (tempSensor != null && tempSensor.Value.HasValue)
                        metrics.CpuTemp = tempSensor.Value.Value;

                    // Power
                    var powerSensor = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"));
                    if (powerSensor != null && powerSensor.Value.HasValue)
                        metrics.CpuPower = powerSensor.Value.Value;
                }

                // Fallback CPU Load
                if (metrics.CpuLoad < 0 && _cpuCounter != null)
                    metrics.CpuLoad = _cpuCounter.NextValue();

                // 2. RAM Load
                if (_ramCounter != null && _totalRamMB > 0)
                {
                    float ramFreeMB = _ramCounter.NextValue();
                    float ramUsedMB = _totalRamMB - ramFreeMB;
                    metrics.RamLoad = (ramUsedMB / _totalRamMB) * 100f;
                }

                // 3. GPU Metrics
                if (_gpu != null)
                {
                    // Load
                    var loadSensor = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"));
                    if (loadSensor != null && loadSensor.Value.HasValue)
                        metrics.GpuLoad = loadSensor.Value.Value;

                    // Temp
                    var tempSensor = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"));
                    if (tempSensor != null && tempSensor.Value.HasValue)
                        metrics.GpuTemp = tempSensor.Value.Value;

                    // Clocks
                    var coreClock = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"));
                    if (coreClock != null && coreClock.Value.HasValue)
                        metrics.GpuCoreClock = coreClock.Value.Value;

                    var memClock = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Memory"));
                    if (memClock != null && memClock.Value.HasValue)
                        metrics.GpuMemoryClock = memClock.Value.Value;

                    // VRAM Load
                    var memUsed = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"));
                    var memTotal = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"));
                    
                    if (memUsed != null && memTotal != null && memUsed.Value.HasValue && memTotal.Value.HasValue && memTotal.Value.Value > 0)
                    {
                        metrics.VramLoad = (memUsed.Value.Value / memTotal.Value.Value) * 100f;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HardwareMonitor] Update Error: {ex.Message}");
            }

            _cachedMetrics = metrics;
            return metrics;
        }

        private ulong GetTotalRamMB()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                ulong total = 0;
                foreach (var o in searcher.Get())
                {
                    total += (ulong)o["Capacity"];
                }
                return total / (1024 * 1024);
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            
            if (_computer != null)
            {
                _computer.Close();
                _computer = null;
            }
        }
    }
}
