namespace OptimizingSoftware.Core.Diagnostics;

/// <summary>
/// Photo de l'état du PC à un instant donné. Chaque champ peut être absent (null ou liste vide)
/// si la collecte a échoué : les vérifications répondent alors « Inconnu » au lieu de planter.
/// </summary>
public sealed record SystemSnapshot
{
    public DateTimeOffset TakenAt { get; init; } = DateTimeOffset.Now;
    public OsInfo? Os { get; init; }
    public CpuInfo? Cpu { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public MemoryInfo? Memory { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];
    public IReadOnlyList<GpuCrashEvent> GpuCrashes { get; init; } = [];

    /// <summary>Vrai si l'historique des plantages a pu être lu (sinon on ne sait pas s'il y a eu des plantages).</summary>
    public bool GpuCrashesCollected { get; init; }

    /// <summary>Planification GPU à accélération matérielle (null = inconnu ou non prise en charge).</summary>
    public bool? HardwareGpuSchedulingEnabled { get; init; }

    public PowerPlanInfo? PowerPlan { get; init; }
}

public sealed record OsInfo(string Name, string Version, int BuildNumber)
{
    /// <summary>Windows 11 commence au build 22000.</summary>
    public bool IsWindows11 => BuildNumber >= 22000;
}

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, int MaxClockMHz);

public sealed record GpuInfo(string Name, string? DriverVersion, DateOnly? DriverDate);

public sealed record MemoryInfo(
    long TotalBytes,
    long AvailableBytes,
    IReadOnlyList<MemoryModule> Modules,
    int? TotalSlots)
{
    public double UsedRatio => TotalBytes <= 0 ? 0 : 1 - (double)AvailableBytes / TotalBytes;
}

/// <param name="RatedSpeedMHz">Vitesse annoncée par la barrette (champ « Speed » de WMI).</param>
/// <param name="ConfiguredSpeedMHz">Vitesse à laquelle la barrette tourne réellement.</param>
public sealed record MemoryModule(
    long CapacityBytes,
    int? RatedSpeedMHz,
    int? ConfiguredSpeedMHz,
    MemoryType Type,
    string? Slot);

public enum MemoryType { Unknown, Ddr3, Ddr4, Ddr5 }

public enum DiskMediaType { Unknown, Hdd, Ssd }

public sealed record DiskInfo(
    string DriveLetter,
    string? Model,
    DiskMediaType MediaType,
    long TotalBytes,
    long FreeBytes,
    bool IsSystemDrive)
{
    public double FreeRatio => TotalBytes <= 0 ? 0 : (double)FreeBytes / TotalBytes;
}

public sealed record DisplayInfo(
    string Name,
    int Width,
    int Height,
    int CurrentRefreshHz,
    int MaxRefreshHz,
    bool IsPrimary);

/// <param name="Kind">Origine : « LiveKernelEvent 141 », « Pilote d'affichage relancé (4101) », etc.</param>
public sealed record GpuCrashEvent(DateTimeOffset OccurredAt, string Kind);

public sealed record PowerPlanInfo(Guid Id, string? Name)
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}
