using System;
using System.Management;
using System.Windows.Controls;
using System.Security.Cryptography;
using System.Text;

namespace FramebaseApp
{
    // GPU Specifications class
    public class GpuSpecs
    {
        public string Name { get; set; } = "";
        public string VendorId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public int VramMB { get; set; }
        public string VramType { get; set; } = "";
        public string VideoProcessor { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public int RefreshRate { get; set; }
    }

    // CPU Specifications class
    public class CpuSpecs
    {
        public string Name { get; set; } = "";
        public string VendorId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public int Cores { get; set; }
        public int Threads { get; set; }
        public int MaxClockMHz { get; set; }
        public int CurrentClockMHz { get; set; }
        public int L2CacheKB { get; set; }
        public int L3CacheKB { get; set; }
        public string Architecture { get; set; } = "";
        public string Manufacturer { get; set; } = "";
    }

    public static class SystemInfoHelper
    {
        public static string GetCpu()
        {
            try
            {
                string cpu = string.Empty;
                var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
                foreach (var o in searcher.Get())
                {
                    cpu = o["Name"]?.ToString() ?? "Unbekannt";
                    
                    // Entferne @ und alles danach (Taktfrequenz)
                    int cut = cpu.IndexOfAny(new char[] {'@', '(', '-'});
                    if (cut > 0) cpu = cpu.Substring(0, cut).Trim();
                    
                    // Entferne AMD-spezifische Core-Bezeichnungen
                    string[] coreWords = { "Single", "Dual", "Triple", "Quad", "Six", "Eight", "Twelve", "Sixteen", 
                                         "Core", "Cores", "-Core", "Processor", "CPU" };
                    
                    foreach (string word in coreWords)
                    {
                        // Entferne das Wort und eventuelle Bindestriche davor/danach
                        cpu = System.Text.RegularExpressions.Regex.Replace(cpu, 
                            $@"\s*-?\s*{System.Text.RegularExpressions.Regex.Escape(word)}\s*-?\s*", 
                            " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                    
                    // Bereinige mehrfache Leerzeichen und trimme
                    cpu = System.Text.RegularExpressions.Regex.Replace(cpu, @"\s+", " ").Trim();
                    
                    break;
                }
                return cpu;
            }
            catch { return "Unbekannt"; }
        }

        public static string GetRam()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                ulong total = 0;
                foreach (var o in searcher.Get())
                {
                    total += (ulong)o["Capacity"];
                }
                return $"{total / (1024 * 1024 * 1024)} GB";
            }
            catch { return "Unbekannt"; }
        }

        public static string GetGpu()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
                foreach (var o in searcher.Get())
                {
                    string gpu = o["Name"]?.ToString() ?? "Unbekannt";
                    
                    // Entferne @ und alles danach (Taktfrequenz, Klammern, etc.)
                    int cut = gpu.IndexOfAny(new char[] {'@', '(', '-'});
                    if (cut > 0) gpu = gpu.Substring(0, cut).Trim();
                    
                    // Entferne "Series" am Ende
                    gpu = System.Text.RegularExpressions.Regex.Replace(gpu, 
                        @"\s+Series\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    // Erkenne und formatiere Hersteller
                    string manufacturer = "";
                    string model = gpu;
                    
                    if (gpu.ToLower().Contains("nvidia") || gpu.ToLower().Contains("geforce"))
                    {
                        manufacturer = "NVIDIA";
                        model = System.Text.RegularExpressions.Regex.Replace(gpu, 
                            @"nvidia\s*|geforce\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    }
                    else if (gpu.ToLower().Contains("amd") || gpu.ToLower().Contains("radeon"))
                    {
                        manufacturer = "AMD";
                        model = System.Text.RegularExpressions.Regex.Replace(gpu, 
                            @"amd\s*|radeon\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    }
                    else if (gpu.ToLower().Contains("intel"))
                    {
                        manufacturer = "Intel";
                        model = System.Text.RegularExpressions.Regex.Replace(gpu, 
                            @"intel\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    }
                    
                    // Bereinige mehrfache Leerzeichen
                    model = System.Text.RegularExpressions.Regex.Replace(model, @"\s+", " ").Trim();
                    
                    // Gib formatierten Namen zurück (Hersteller + Modell)
                    if (!string.IsNullOrEmpty(manufacturer) && !string.IsNullOrEmpty(model))
                    {
                        return $"{manufacturer} {model}";
                    }
                    
                    return gpu;
                }
                return "Unbekannt";
            }
            catch { return "Unbekannt"; }
        }

        public static string GetGpuId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select PNPDeviceID, Name, DeviceID from Win32_VideoController");
                foreach (var o in searcher.Get())
                {
                    string pnpDeviceId = o["PNPDeviceID"]?.ToString() ?? "";
                    string name = o["Name"]?.ToString() ?? "";
                    string deviceId = o["DeviceID"]?.ToString() ?? "";
                    
                    // Kombiniere alle verfügbaren Hardware-IDs für eindeutige Identifikation
                    string combined = $"{pnpDeviceId}|{deviceId}|{name}";
                    return GenerateStableHash(combined);
                }
                return GenerateStableHash("unknown-gpu");
            }
            catch { return GenerateStableHash("unknown-gpu"); }
        }

        public static string GetGpuVendorId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select PNPDeviceID from Win32_VideoController");
                foreach (var o in searcher.Get())
                {
                    string pnpDeviceId = o["PNPDeviceID"]?.ToString() ?? "";
                    // Format: PCI\VEN_10DE&DEV_2684&SUBSYS_...
                    // Extrahiere VEN_XXXX
                    var venMatch = System.Text.RegularExpressions.Regex.Match(pnpDeviceId, @"VEN_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (venMatch.Success)
                    {
                        return venMatch.Groups[1].Value.ToUpper();
                    }
                }
                return "0000";
            }
            catch { return "0000"; }
        }

        public static string GetGpuDeviceId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select PNPDeviceID from Win32_VideoController");
                foreach (var o in searcher.Get())
                {
                    string pnpDeviceId = o["PNPDeviceID"]?.ToString() ?? "";
                    // Format: PCI\VEN_10DE&DEV_2684&SUBSYS_...
                    // Extrahiere DEV_XXXX
                    var devMatch = System.Text.RegularExpressions.Regex.Match(pnpDeviceId, @"DEV_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (devMatch.Success)
                    {
                        return devMatch.Groups[1].Value.ToUpper();
                    }
                }
                return "0000";
            }
            catch { return "0000"; }
        }

        // Get complete PCI Device ID in format "VendorID:DeviceID" (e.g., "10DE:2684" for RTX 4090)
        public static string GetGpuPciId()
        {
            try
            {
                string vendorId = GetGpuVendorId();
                string deviceId = GetGpuDeviceId();
                
                if (vendorId != "0000" && deviceId != "0000")
                {
                    return $"{vendorId}:{deviceId}";
                }
                return null;
            }
            catch { return null; }
        }

        // Get detailed GPU specifications
        public static GpuSpecs GetGpuSpecs()
        {
            try
            {
                var specs = new GpuSpecs();
                var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                
                foreach (var o in searcher.Get())
                {
                    // Basic info
                    specs.Name = o["Name"]?.ToString() ?? "Unknown";
                    specs.VendorId = GetGpuVendorId();
                    specs.DeviceId = GetGpuDeviceId();
                    
                    // Memory (AdapterRAM in bytes)
                    if (o["AdapterRAM"] != null)
                    {
                        ulong ramBytes = Convert.ToUInt64(o["AdapterRAM"]);
                        specs.VramMB = (int)(ramBytes / (1024 * 1024));
                    }
                    
                    // Memory type (GDDR6, GDDR6X, etc.)
                    specs.VramType = o["VideoMemoryType"]?.ToString() ?? "";
                    
                    // Driver version
                    specs.DriverVersion = o["DriverVersion"]?.ToString() ?? "";
                    
                    // Current refresh rate
                    if (o["CurrentRefreshRate"] != null)
                    {
                        specs.RefreshRate = Convert.ToInt32(o["CurrentRefreshRate"]);
                    }
                    
                    // Video processor (chip name)
                    specs.VideoProcessor = o["VideoProcessor"]?.ToString() ?? "";
                    
                    break;
                }
                
                return specs;
            }
            catch
            {
                return new GpuSpecs { Name = "Unknown" };
            }
        }

        // Get detailed CPU specifications
        public static CpuSpecs GetCpuSpecs()
        {
            try
            {
                var specs = new CpuSpecs();
                var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
                
                foreach (var o in searcher.Get())
                {
                    // Basic info
                    specs.Name = o["Name"]?.ToString() ?? "Unknown";
                    specs.VendorId = GetCpuVendorId();
                    specs.DeviceId = GetCpuDeviceId();
                    
                    // Cores and threads
                    if (o["NumberOfCores"] != null)
                    {
                        specs.Cores = Convert.ToInt32(o["NumberOfCores"]);
                    }
                    if (o["NumberOfLogicalProcessors"] != null)
                    {
                        specs.Threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    }
                    
                    // Clock speeds (in MHz)
                    if (o["MaxClockSpeed"] != null)
                    {
                        specs.MaxClockMHz = Convert.ToInt32(o["MaxClockSpeed"]);
                    }
                    if (o["CurrentClockSpeed"] != null)
                    {
                        specs.CurrentClockMHz = Convert.ToInt32(o["CurrentClockSpeed"]);
                    }
                    
                    // Cache sizes (in KB)
                    if (o["L2CacheSize"] != null)
                    {
                        specs.L2CacheKB = Convert.ToInt32(o["L2CacheSize"]);
                    }
                    if (o["L3CacheSize"] != null)
                    {
                        specs.L3CacheKB = Convert.ToInt32(o["L3CacheSize"]);
                    }
                    
                    // Architecture
                    if (o["Architecture"] != null)
                    {
                        int arch = Convert.ToInt32(o["Architecture"]);
                        specs.Architecture = arch switch
                        {
                            0 => "x86",
                            1 => "MIPS",
                            2 => "Alpha",
                            3 => "PowerPC",
                            5 => "ARM",
                            6 => "ia64",
                            9 => "x64",
                            12 => "ARM64",
                            _ => $"Unknown ({arch})"
                        };
                    }
                    
                    // TDP (Design voltage and current)
                    specs.Manufacturer = o["Manufacturer"]?.ToString() ?? "";
                    
                    break;
                }
                
                return specs;
            }
            catch
            {
                return new CpuSpecs { Name = "Unknown" };
            }
        }

        public static string GetCpuVendorId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select Manufacturer from Win32_Processor");
                foreach (var o in searcher.Get())
                {
                    string manufacturer = o["Manufacturer"]?.ToString()?.ToLower() ?? "";
                    // Standardisierte Vendor IDs (keine PCI IDs für CPUs, aber logische Bezeichnung)
                    if (manufacturer.Contains("intel")) return "INTEL";
                    if (manufacturer.Contains("amd") || manufacturer.Contains("advanced micro devices")) return "AMD";
                    return manufacturer.ToUpper();
                }
                return "UNKNOWN";
            }
            catch { return "UNKNOWN"; }
        }

        public static string GetCpuDeviceId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select ProcessorId from Win32_Processor");
                foreach (var o in searcher.Get())
                {
                    string processorId = o["ProcessorId"]?.ToString() ?? "";
                    // ProcessorId ist bereits eine eindeutige ID
                    return processorId;
                }
                return "UNKNOWN";
            }
            catch { return "UNKNOWN"; }
        }

        // Get CPUID in format "VendorString:Family-Model-Stepping" (e.g., "GenuineIntel:06-B7-09")
        public static string GetCpuId()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
                foreach (var o in searcher.Get())
                {
                    // Get vendor string (GenuineIntel or AuthenticAMD)
                    string manufacturer = o["Manufacturer"]?.ToString()?.ToLower() ?? "";
                    string vendorString = "";
                    
                    if (manufacturer.Contains("intel"))
                        vendorString = "GenuineIntel";
                    else if (manufacturer.Contains("amd") || manufacturer.Contains("advanced micro devices"))
                        vendorString = "AuthenticAMD";
                    else
                        return GenerateStableHash($"{o["ProcessorId"]}|{o["Name"]}"); // Fallback to hash for unknown vendors
                    
                    // Parse ProcessorId to extract Family, Model, Stepping
                    // ProcessorId format varies, but typically contains these values
                    string processorId = o["ProcessorId"]?.ToString() ?? "";
                    
                    // Try to extract from ProcessorId (usually in hex format)
                    // Example: BFEBFBFF000906E9 for Intel
                    if (!string.IsNullOrEmpty(processorId) && processorId.Length >= 16)
                    {
                        try
                        {
                            // Last 8 hex digits contain Family_Model_Stepping info
                            string lastEightDigits = processorId.Substring(processorId.Length - 8);
                            uint cpuidValue = Convert.ToUInt32(lastEightDigits, 16);
                            
                            // Extract Family, Model, Stepping from CPUID value
                            // CPUID format: bits 0-3: Stepping, 4-7: Model, 8-11: Family, 12-13: Type, 16-19: Extended Model, 20-27: Extended Family
                            int stepping = (int)(cpuidValue & 0xF);
                            int model = (int)((cpuidValue >> 4) & 0xF);
                            int family = (int)((cpuidValue >> 8) & 0xF);
                            int extendedModel = (int)((cpuidValue >> 16) & 0xF);
                            int extendedFamily = (int)((cpuidValue >> 20) & 0xFF);
                            
                            // Calculate effective Family and Model
                            int effectiveFamily = family;
                            if (family == 0xF)
                                effectiveFamily = family + extendedFamily;
                            
                            int effectiveModel = model;
                            if (family == 0x6 || family == 0xF)
                                effectiveModel = (extendedModel << 4) + model;
                            
                            // Format as CPUID: VendorString:Family-Model-Stepping
                            string cpuid = $"{vendorString}:{effectiveFamily:X2}-{effectiveModel:X2}-{stepping:X2}";
                            return cpuid;
                        }
                        catch
                        {
                            // If parsing fails, fall through to hash method
                        }
                    }
                    
                    // Fallback: Generate stable hash
                    return GenerateStableHash($"{processorId}|{o["Name"]}");
                }
                return GenerateStableHash("unknown-cpu");
            }
            catch { return GenerateStableHash("unknown-cpu"); }
        }

        private static string GenerateStableHash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknown";

            using (var sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                
                // Verwende die ersten 12 Zeichen des Hex-Strings für eine kompakte aber eindeutige ID
                string hex = Convert.ToHexString(hashedBytes);
                return hex.Substring(0, Math.Min(12, hex.Length)).ToLower();
            }
        }

        // Debug-Methode um alle Hardware-IDs anzuzeigen
        public static string GetAllHardwareInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CPU: {GetCpu()}");
            sb.AppendLine($"CPU-ID: {GetCpuId()}");
            sb.AppendLine($"CPU-Vendor: {GetCpuVendorId()}");
            sb.AppendLine($"CPU-Device: {GetCpuDeviceId()}");
            sb.AppendLine($"GPU: {GetGpu()}");
            sb.AppendLine($"GPU-ID: {GetGpuId()}");
            sb.AppendLine($"GPU-PCI-ID: {GetGpuPciId()}");
            sb.AppendLine($"GPU-Vendor: {GetGpuVendorId()}");
            sb.AppendLine($"GPU-Device: {GetGpuDeviceId()}");
            sb.AppendLine($"RAM: {GetRam()}");
            return sb.ToString();
        }
    }
}
