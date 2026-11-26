using System.Runtime.Versioning;
using Microsoft.Win32;
using OptimizingSoftware.Core.Apps;

namespace OptimizingSoftware.Windows;

/// <summary>Jeux installés par Steam, Epic Games et l'appli Xbox, lus là où ces launchers les rangent.</summary>
[SupportedOSPlatform("windows")]
public static class GameLibrariesReader
{
    public static IReadOnlyList<InstalledApp> Read()
    {
        var games = new List<InstalledApp>();
        Try(() => games.AddRange(Steam()));
        Try(() => games.AddRange(Epic()));
        Try(() => games.AddRange(Xbox()));
        return games;
    }

    /// <summary>Liste de Windows + jeux des launchers, avec la taille des jeux qui n'en déclarent pas.</summary>
    public static IReadOnlyList<InstalledApp> ReadAllApps(CancellationToken ct = default)
    {
        var merged = GameLibraries.Merge(InstalledAppsReader.Read(), Read());
        return merged.Select(a =>
        {
            ct.ThrowIfCancellationRequested();
            return a is { IsGame: true, SizeBytes: null, InstallLocation: { } dir } && Directory.Exists(dir)
                ? a with { SizeBytes = LeftoverFinder.FilesIn(dir).Sum(f => f.Length) }
                : a;
        }).ToList();
    }

    private static IEnumerable<InstalledApp> Steam()
    {
        var steam = SteamPath();
        if (steam is null) return [];
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(GameLibraries.ParseSteamLibraryFolders(File.ReadAllText(vdf)));

        var games = new List<InstalledApp>();
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in SafeFiles(apps, "appmanifest_*.acf"))
                Try(() =>
                {
                    if (GameLibraries.ParseSteamManifest(File.ReadAllText(acf), lib) is { } g) games.Add(g);
                });
        }
        return games;
    }

    private static string? SteamPath()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (user?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Valve\Steam");
        return machine?.GetValue("InstallPath") is string m && Directory.Exists(m) ? m : null;
    }

    private static IEnumerable<InstalledApp> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var games = new List<InstalledApp>();
        foreach (var item in SafeFiles(dir, "*.item"))
            Try(() =>
            {
                if (GameLibraries.ParseEpicManifest(File.ReadAllText(item)) is { } g) games.Add(g);
            });
        return games;
    }

    /// <summary>Jeux du Game Pass et du Microsoft Store installés dans « X:\XboxGames ».</summary>
    private static IEnumerable<InstalledApp> Xbox()
    {
        var games = new List<InstalledApp>();
        foreach (var drive in KnownFolders.FixedDrives())
        {
            var root = Path.Combine(drive.Root, "XboxGames");
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name.Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                games.Add(new InstalledApp(name, null, null, null, null, dir, null, $"xbox:{name}",
                    IsBroken: false, Source: "Xbox", IsGame: true));
            }
        }
        return games;
    }

    private static string[] SafeFiles(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Launcher absent ou fichier illisible : on continue avec les autres.
        }
    }
}
