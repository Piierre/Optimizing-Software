using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Core.Apps;

/// <summary>Un dossier qui semble appartenir à une application qui n'est plus installée.</summary>
public sealed record LeftoverFolder(string Path, long SizeBytes, DateTime LastWriteUtc, string Reason, bool Suggested);

/// <summary>
/// Cherche, dans les dossiers où les applications s'installent et rangent leurs données, ceux qui ne
/// correspondent plus à aucune application installée.
/// </summary>
public static class LeftoverFinder
{
    /// <summary>Un dossier modifié récemment est sans doute encore utilisé (appli portable, jeu, sauvegardes).</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(60);

    /// <summary>Dossiers partagés ou système à ne jamais proposer, quel que soit leur contenu.</summary>
    public static readonly IReadOnlySet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Common Files", "Microsoft Shared", "WindowsApps", "Packages", "Temp", "Programs",
        "Windows Defender", "Windows Mail", "Windows Media Player", "Windows NT", "Windows Photo Viewer",
        "Windows Portable Devices", "Windows Security", "WindowsPowerShell", "Internet Explorer", "Microsoft.NET",
        "dotnet", "Reference Assemblies", "MSBuild", "ModifiableWindowsApps", "Uninstall Information",
        "Package Cache", "regid.1991-06.com.microsoft", "ssh", "USOShared", "USOPrivate", "SoftwareDistribution",
        "Comms", "ConnectedDevicesPlatform", "D3DSCache", "CrashDumps", "PeerDistRepub", "PlaceholderTileLogoFolder",
        "Publishers", "Intel", "AMD", "NVIDIA", "NVIDIA Corporation", "Realtek", "Google", "Mozilla",
        "OptimizingSoftware", "OptimizingSoftware.Corbeille", "IsolatedStorage", "History", "Application Data",
        // Sauvegardes de jeux et réglages souvent rangés ici sans application « installée ».
        "My Games", "Saved Games", "Steam", "EpicGamesLauncher", "Riot Games", "Battle.net", "Ubisoft", "EA Desktop",
        "Origin", "GOG.com", "Minecraft", ".minecraft",
    };

    /// <param name="parents">Dossiers à inspecter (Program Files, AppData, ProgramData…) ; on regarde leurs sous-dossiers directs.</param>
    /// <param name="programFolders">Parmi <paramref name="parents"/>, ceux qui contiennent des programmes (Program Files).</param>
    /// <param name="justUninstalled">
    /// Application qu'on vient de désinstaller : ses dossiers sont récents, on les propose quand même
    /// (s'ils ne correspondent à aucune autre application installée).
    /// </param>
    public static IReadOnlyList<LeftoverFolder> Find(
        IReadOnlyList<InstalledApp> apps,
        IEnumerable<string> parents,
        IReadOnlySet<string> programFolders,
        DateTime nowUtc,
        CancellationToken ct = default,
        InstalledApp? justUninstalled = null)
    {
        // L'éditeur n'est pas utilisé ici : « Valve » ou « Ubisoft » désigneraient aussi d'autres applications.
        var removedKeys = justUninstalled is null
            ? []
            : new[] { NameMatcher.Normalize(justUninstalled.Name), FolderKey(justUninstalled.InstallLocation) }
                .Where(k => k.Length >= 4).ToList();
        var removedLocation = string.IsNullOrWhiteSpace(justUninstalled?.InstallLocation)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(justUninstalled.InstallLocation));

        var keys = apps.SelectMany(a => a.Keys).Where(k => k.Length >= 4).Distinct().ToList();
        var locations = apps
            .Select(a => a.InstallLocation)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => Path.TrimEndingDirectorySeparator(Path.GetFullPath(l!)))
            .ToList();

        var found = new List<LeftoverFolder>();
        foreach (var parent in parents.Where(Directory.Exists))
        {
            var isProgramFolder = programFolders.Contains(parent);
            foreach (var dir in SafeSubdirectories(parent))
            {
                ct.ThrowIfCancellationRequested();
                var name = dir.Name;
                if (Protected.Contains(name) || name.StartsWith('.') || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) || dir.Attributes.HasFlag(FileAttributes.System))
                    continue;
                if (NameMatcher.Matches(name, keys) || IsInstallLocation(dir.FullName, locations))
                    continue;

                var (size, lastWrite, hasExe) = Measure(dir, ct);
                var fromRemoved = NameMatcher.Matches(name, removedKeys)
                                  || (removedLocation is not null && IsInstallLocation(dir.FullName, [removedLocation]));
                if (fromRemoved)
                {
                    found.Add(new LeftoverFolder(dir.FullName, size, lastWrite,
                        $"Reste de {justUninstalled!.Name}, que tu viens de désinstaller.", Suggested: true));
                    continue;
                }
                if (nowUtc - lastWrite < MinAge) continue;

                var age = (int)((nowUtc - lastWrite).TotalDays / 30);
                var empty = size == 0;
                var (reason, suggested) = empty
                    ? ("Dossier vide, aucune application installée ne s'y rattache.", true)
                    : isProgramFolder && !hasExe
                        ? ($"Plus aucun programme dedans, ni aucune application installée correspondante. Pas modifié depuis {age} mois.", true)
                        : ($"Aucune application installée ne correspond. Pas modifié depuis {age} mois. Vérifie que ce n'est pas une appli portable ou des sauvegardes.", false);
                found.Add(new LeftoverFolder(dir.FullName, size, lastWrite, reason, suggested));
            }
        }
        return found.OrderByDescending(f => f.SizeBytes).ToList();
    }

    private static string FolderKey(string? location) =>
        string.IsNullOrWhiteSpace(location) ? "" : NameMatcher.Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(location)));

    /// <summary>Tous les fichiers d'un dossier de restes, sans suivre les liens ni les jonctions.</summary>
    public static IEnumerable<FileInfo> FilesIn(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return new DirectoryInfo(folder).EnumerateFiles("*", options);
    }

    /// <summary>Supprime les dossiers restés vides (du plus profond au dossier lui-même). Ne touche à aucun fichier.</summary>
    public static void RemoveEmptyDirectories(string folder)
    {
        if (!Directory.Exists(folder) || PathSafety.IsLink(folder)) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(folder))
                if (!PathSafety.IsLink(sub)) RemoveEmptyDirectories(sub);
            if (Directory.EnumerateFileSystemEntries(folder).Any()) return;
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier protégé ou en cours d'utilisation : on le laisse.
        }
    }

    private static bool IsInstallLocation(string dir, List<string> locations)
    {
        var full = Path.TrimEndingDirectorySeparator(dir);
        return locations.Any(l =>
            string.Equals(l, full, StringComparison.OrdinalIgnoreCase)
            || l.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static (long Size, DateTime LastWriteUtc, bool HasExe) Measure(DirectoryInfo dir, CancellationToken ct)
    {
        long size = 0;
        var last = dir.LastWriteTimeUtc;
        var hasExe = false;
        foreach (var file in FilesIn(dir.FullName))
        {
            ct.ThrowIfCancellationRequested();
            size += file.Length;
            if (file.LastWriteTimeUtc > last) last = file.LastWriteTimeUtc;
            if (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) hasExe = true;
        }
        return (size, last, hasExe);
    }

    private static IEnumerable<DirectoryInfo> SafeSubdirectories(string parent)
    {
        try
        {
            return new DirectoryInfo(parent).GetDirectories();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
