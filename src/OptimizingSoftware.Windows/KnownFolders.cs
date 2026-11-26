using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OptimizingSoftware.Windows;

/// <summary>Dossiers personnels réels (ils peuvent avoir été déplacés sur un autre disque).</summary>
[SupportedOSPlatform("windows")]
public static partial class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string Downloads =>
        TryGet(DownloadsId) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Bureau, Documents, Téléchargements, Images, Vidéos, Musique.</summary>
    public static IReadOnlyList<string> PersonalFolders() =>
        new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Downloads,
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            }
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Dossiers personnels + racine de chaque disque fixe autre que celui de Windows.</summary>
    public static IReadOnlyList<string> PersonalFoldersAndDataDrives()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady
                        && !string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.RootDirectory.FullName);
        return PersonalFolders().Concat(drives).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Disques fixes prêts (internes), celui de Windows en premier.</summary>
    public static IReadOnlyList<Core.Files.DriveChoice> FixedDrives()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var list = new List<Core.Files.DriveChoice>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new Core.Files.DriveChoice(d.RootDirectory.FullName,
                    string.IsNullOrWhiteSpace(d.VolumeLabel) ? null : d.VolumeLabel,
                    d.TotalSize, d.AvailableFreeSpace,
                    string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)));
            }
            catch (IOException)
            {
                // Disque débranché entre-temps.
            }
        }
        return list.OrderByDescending(d => d.IsSystem).ThenBy(d => d.Root).ToList();
    }

    private static string? TryGet(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) != 0) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
}
