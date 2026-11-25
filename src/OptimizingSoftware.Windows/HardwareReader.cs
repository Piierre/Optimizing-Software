using System.Management;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

[SupportedOSPlatform("windows")]
internal static class HardwareReader
{
    public static OsInfo? ReadOs()
    {
        var os = Wmi.Query("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (os is null) return null;
        var displayVersion = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string;
        return new OsInfo(
            os.Str("Caption")?.Replace("Microsoft ", "") ?? "Windows",
            displayVersion ?? os.Str("Version") ?? "",
            int.TryParse(os.Str("BuildNumber"), out var build) ? build : 0);
    }

    public static CpuInfo? ReadCpu()
    {
        var cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        if (cpu is null) return null;
        return new CpuInfo(
            cpu.Str("Name") ?? "Processeur inconnu",
            cpu.Int("NumberOfCores") ?? 0,
            cpu.Int("NumberOfLogicalProcessors") ?? Environment.ProcessorCount,
            cpu.Int("MaxClockSpeed") ?? 0);
    }

    public static IReadOnlyList<GpuInfo> ReadGpus() =>
        Wmi.Query("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController")
            .Select(g => (Name: g.Str("Name") ?? "Carte graphique", Wmi: g))
            .Select(g => new GpuInfo(
                g.Name,
                FriendlyDriverVersion(g.Name, g.Wmi.Str("DriverVersion")),
                ParseCimDate(g.Wmi.Str("DriverDate"))))
            // Adaptateurs virtuels (bureau à distance, pilote de base) : sans intérêt pour le diagnostic.
            .Where(g => !g.Name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)
                        && !g.Name.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                        && !g.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static MemoryInfo? ReadMemory()
    {
        var os = Wmi.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        if (os is null) return null;

        var modules = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, DeviceLocator FROM Win32_PhysicalMemory")
            .Select(m => new MemoryModule(
                m.Long("Capacity") ?? 0,
                m.Int("Speed"),
                m.Int("ConfiguredClockSpeed"),
                (m.Int("SMBIOSMemoryType") ?? 0) switch
                {
                    24 => MemoryType.Ddr3,
                    26 => MemoryType.Ddr4,
                    34 => MemoryType.Ddr5,
                    _ => MemoryType.Unknown,
                },
                m.Str("DeviceLocator")))
            .ToList();

        var slots = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray")
            .Sum(a => a.Int("MemoryDevices") ?? 0);

        return new MemoryInfo(
            (os.Long("TotalVisibleMemorySize") ?? 0) * 1024,
            (os.Long("FreePhysicalMemory") ?? 0) * 1024,
            modules,
            slots > 0 ? slots : null);
    }

    /// <summary>« 20260415000000.000000-000 » → 15/04/2026.</summary>
    internal static DateOnly? ParseCimDate(string? cim)
    {
        if (cim is null) return null;
        try
        {
            return DateOnly.FromDateTime(ManagementDateTimeConverter.ToDateTime(cim));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows affiche les pilotes NVIDIA comme « 32.0.15.9636 » ; NVIDIA les appelle « 596.36 »
    /// (5 derniers chiffres). On ajoute ce numéro, plus parlant, pour les cartes NVIDIA uniquement.
    /// </summary>
    internal static string? FriendlyDriverVersion(string gpuName, string? version)
    {
        if (version is null || !gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return version;
        var digits = new string(version.Where(char.IsDigit).ToArray());
        if (digits.Length < 5)
            return version;
        var last5 = digits[^5..];
        return $"{last5[..3]}.{last5[3..]}";
    }
}
