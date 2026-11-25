using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Core.Tests;

/// <summary>Photos de PC servant aux tests.</summary>
internal static class Snapshots
{
    public const long GiB = 1024L * 1024 * 1024;
    public static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 0, 0, TimeSpan.FromHours(1));

    /// <summary>
    /// Le PC de Pierre tel que décrit par son rapport dxdiag du 25 novembre 2026, avant les réglages :
    /// i5-9600K, RTX 2070 Super avec un pilote d'avril, 2×8 Go DDR4, Windows 10, écrans 165 Hz + 60 Hz.
    /// </summary>
    public static SystemSnapshot PierreBefore() => new()
    {
        TakenAt = Now,
        Os = new OsInfo("Windows 10 Professionnel", "22H2", 19045),
        Cpu = new CpuInfo("Intel(R) Core(TM) i5-9600K CPU @ 3.70GHz", 6, 6, 3700),
        Gpus = [new GpuInfo("NVIDIA GeForce RTX 2070 SUPER", "596.36", new DateOnly(2026, 4, 15))],
        Memory = new MemoryInfo(16 * GiB, 3 * GiB,
            [
                new MemoryModule(8 * GiB, 3200, 3200, MemoryType.Ddr4, "DIMM A2"),
                new MemoryModule(8 * GiB, 3200, 3200, MemoryType.Ddr4, "DIMM B2"),
            ],
            TotalSlots: 4),
        Disks =
        [
            new DiskInfo("C:", "CT1000MX500SSD1", DiskMediaType.Ssd, 931 * GiB, 105 * GiB, IsSystemDrive: true),
            new DiskInfo("D:", "TOSHIBA DT01ACA100", DiskMediaType.Hdd, 931 * GiB, 400 * GiB, IsSystemDrive: false),
        ],
        Displays =
        [
            new DisplayInfo("HKC (DisplayPort)", 1920, 1080, 165, 165, IsPrimary: true),
            new DisplayInfo("Samsung (HDMI)", 1920, 1080, 60, 60, IsPrimary: false),
        ],
        GpuCrashesCollected = true,
        GpuCrashes = [new GpuCrashEvent(new DateTimeOffset(2026, 11, 15, 21, 12, 0, TimeSpan.FromHours(1)), "LiveKernelEvent 141")],
        HardwareGpuSchedulingEnabled = false,
        PowerPlan = new PowerPlanInfo(PowerPlanInfo.Balanced, "Utilisation normale"),
    };

    /// <summary>Un PC sain : tout doit être OK.</summary>
    public static SystemSnapshot Healthy() => PierreBefore() with
    {
        Os = new OsInfo("Windows 11 Famille", "24H2", 26100),
        Cpu = new CpuInfo("AMD Ryzen 7 7800X3D", 8, 16, 4200),
        Gpus = [new GpuInfo("NVIDIA GeForce RTX 4070", "621.40", new DateOnly(2026, 11, 24))],
        Memory = new MemoryInfo(32 * GiB, 20 * GiB,
            [
                new MemoryModule(16 * GiB, 6000, 6000, MemoryType.Ddr5, "A2"),
                new MemoryModule(16 * GiB, 6000, 6000, MemoryType.Ddr5, "B2"),
            ],
            TotalSlots: 4),
        Disks = [new DiskInfo("C:", "Samsung 990 PRO", DiskMediaType.Ssd, 1863 * GiB, 900 * GiB, IsSystemDrive: true)],
        Displays = [new DisplayInfo("Écran principal", 2560, 1440, 144, 144, IsPrimary: true)],
        GpuCrashes = [],
        HardwareGpuSchedulingEnabled = true,
        PowerPlan = new PowerPlanInfo(PowerPlanInfo.HighPerformance, "Performances élevées"),
    };
}
