using System.Runtime.Versioning;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

[SupportedOSPlatform("windows")]
internal static class DiskReader
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    public static IReadOnlyList<DiskInfo> Read()
    {
        // Lettre de lecteur → numéro de disque physique.
        var diskNumberByLetter = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", StorageScope)
            .Where(p => p["DriveLetter"] is char c && c != '\0')
            .GroupBy(p => char.ToUpperInvariant((char)p["DriveLetter"]))
            .ToDictionary(g => g.Key, g => g.First().Int("DiskNumber"));

        // Numéro de disque → type (SSD/HDD) et modèle. MediaType : 3 = HDD, 4 = SSD.
        var physical = Wmi.Query("SELECT DeviceId, MediaType, FriendlyName FROM MSFT_PhysicalDisk", StorageScope)
            .GroupBy(d => d.Str("DeviceId") ?? "")
            .ToDictionary(g => g.Key, g => (Media: ToMediaType(g.First().Int("MediaType")), Model: g.First().Str("FriendlyName")));

        var systemLetter = char.ToUpperInvariant(Path.GetPathRoot(Environment.SystemDirectory)?[0] ?? 'C');

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d =>
            {
                var letter = char.ToUpperInvariant(d.Name[0]);
                var info = diskNumberByLetter.TryGetValue(letter, out var n) && n is not null
                           && physical.TryGetValue(n.Value.ToString(), out var p)
                    ? p
                    : (Media: DiskMediaType.Unknown, Model: (string?)null);
                return new DiskInfo($"{letter}:", info.Model, info.Media, d.TotalSize, d.AvailableFreeSpace,
                    IsSystemDrive: letter == systemLetter);
            })
            .ToList();
    }

    private static DiskMediaType ToMediaType(int? mediaType) => mediaType switch
    {
        3 => DiskMediaType.Hdd,
        4 => DiskMediaType.Ssd,
        _ => DiskMediaType.Unknown,
    };
}
