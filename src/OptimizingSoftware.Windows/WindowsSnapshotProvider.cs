using System.Runtime.Versioning;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

/// <summary>
/// Prend la photo du PC. Chaque collecte est isolée : si l'une échoue, le reste du diagnostic continue.
/// Aucune de ces lectures ne demande de droits administrateur et aucune ne modifie le système.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSnapshotProvider : ISystemSnapshotProvider
{
    public Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var crashes = Try(() => GpuCrashReader.ReadSince(DateTime.Now - GpuCrashCheckWindow));
            return new SystemSnapshot
            {
                TakenAt = DateTimeOffset.Now,
                Os = Try(HardwareReader.ReadOs),
                Cpu = Try(HardwareReader.ReadCpu),
                Gpus = Try(HardwareReader.ReadGpus) ?? [],
                Memory = Try(HardwareReader.ReadMemory),
                Disks = Try(DiskReader.Read) ?? [],
                Displays = Try(DisplayReader.Read) ?? [],
                GpuCrashes = crashes ?? [],
                GpuCrashesCollected = crashes is not null,
                HardwareGpuSchedulingEnabled = Try(SettingsReader.ReadHardwareGpuScheduling),
                PowerPlan = Try(SettingsReader.ReadPowerPlan),
            };
        }, cancellationToken);

    private static readonly TimeSpan GpuCrashCheckWindow = Core.Diagnostics.Checks.GpuCrashCheck.Window;

    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Collecte échouée : {ex}");
            return default;
        }
    }
}
