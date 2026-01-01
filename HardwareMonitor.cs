using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace FramebaseApp
{
    public class HardwareMonitor : IDisposable
    {
        private readonly Computer _computer;
        private DateTime _lastUpdate = DateTime.MinValue;
        private HardwareMetrics _cachedMetrics = new();

        public HardwareMonitor()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true
            };

            _computer.Open();
            _computer.Accept(new UpdateVisitor());
        }

        public class HardwareMetrics
        {
            public float CpuLoad { get; set; } = -1;
            public float CpuTemp { get; set; } = -1;
            public float GpuLoad { get; set; } = -1;
            public float GpuTemp { get; set; } = -1;
            public float RamLoad { get; set; } = -1;
            public float RamUsed { get; set; } = -1;
            public float RamTotal { get; set; } = -1;
            public float VramLoad { get; set; } = -1;
            public float VramUsed { get; set; } = -1;
            public float VramTotal { get; set; } = -1;
        }

        public HardwareMetrics GetMetrics()
        {
            // Update max once per second
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 1000)
                return _cachedMetrics;

            _lastUpdate = DateTime.Now;

            try
            {
                _computer.Accept(new UpdateVisitor());

                var metrics = new HardwareMetrics();

                // CPU
                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    metrics.CpuLoad = GetSensorValue(cpu, SensorType.Load, "CPU Total") ?? -1;
                    metrics.CpuTemp = GetSensorValue(cpu, SensorType.Temperature, "CPU Package") 
                                   ?? GetSensorValue(cpu, SensorType.Temperature, "Core Average")
                                   ?? GetSensorValue(cpu, SensorType.Temperature) ?? -1;
                }

                // GPU
                var gpu = _computer.Hardware.FirstOrDefault(h => 
                    h.HardwareType == HardwareType.GpuNvidia || 
                    h.HardwareType == HardwareType.GpuAmd ||
                    h.HardwareType == HardwareType.GpuIntel);
                
                if (gpu != null)
                {
                    metrics.GpuLoad = GetSensorValue(gpu, SensorType.Load, "GPU Core") 
                                   ?? GetSensorValue(gpu, SensorType.Load, "D3D 3D")
                                   ?? GetSensorValue(gpu, SensorType.Load) ?? -1;
                    
                    metrics.GpuTemp = GetSensorValue(gpu, SensorType.Temperature, "GPU Core")
                                   ?? GetSensorValue(gpu, SensorType.Temperature) ?? -1;

                    metrics.VramUsed = GetSensorValue(gpu, SensorType.SmallData, "GPU Memory Used")
                                    ?? GetSensorValue(gpu, SensorType.SmallData, "D3D Dedicated Memory Used") ?? -1;
                    
                    metrics.VramTotal = GetSensorValue(gpu, SensorType.SmallData, "GPU Memory Total") ?? -1;

                    if (metrics.VramTotal > 0 && metrics.VramUsed >= 0)
                    {
                        metrics.VramLoad = (metrics.VramUsed / metrics.VramTotal) * 100f;
                    }
                }

                // RAM
                var memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
                if (memory != null)
                {
                    metrics.RamUsed = GetSensorValue(memory, SensorType.Data, "Memory Used") ?? -1;
                    metrics.RamTotal = GetSensorValue(memory, SensorType.Data, "Memory Available") ?? -1;
                    
                    if (metrics.RamTotal > 0)
                    {
                        metrics.RamTotal += metrics.RamUsed;
                        metrics.RamLoad = (metrics.RamUsed / metrics.RamTotal) * 100f;
                    }
                }

                _cachedMetrics = metrics;
                return metrics;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMetrics Error: {ex.Message}");
                return _cachedMetrics;
            }
        }

        private float? GetSensorValue(IHardware hardware, SensorType type, string? nameContains = null)
        {
            var sensors = hardware.Sensors.Where(s => s.SensorType == type);
            
            if (!string.IsNullOrEmpty(nameContains))
            {
                sensors = sensors.Where(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
            }

            var sensor = sensors.FirstOrDefault();
            return sensor?.Value;
        }

        public void Dispose()
        {
            _computer.Close();
        }

        private class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                    subHardware.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
