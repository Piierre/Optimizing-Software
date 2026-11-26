using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptimizingSoftware.Core.Apps;

/// <summary>
/// Lecture des bibliothèques des launchers de jeux (Steam, Epic) et repérage des jeux parmi les applications
/// déclarées dans Windows. Les jeux Steam et Epic ne sont souvent pas (ou mal) déclarés dans la liste des programmes.
/// </summary>
public static partial class GameLibraries
{
    /// <summary>Dossiers des bibliothèques Steam listés dans libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> ParseSteamLibraryFolders(string vdf) =>
        VdfPairs(vdf).Where(p => p.Key.Equals("path", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Lit un appmanifest_*.acf. <paramref name="libraryFolder"/> est la bibliothèque qui le contient
    /// (le jeu est dans « steamapps\common\installdir »).
    /// </summary>
    public static InstalledApp? ParseSteamManifest(string acf, string libraryFolder)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in VdfPairs(acf)) values.TryAdd(k, v);
        if (!values.TryGetValue("appid", out var appId) || !values.TryGetValue("name", out var name)
            || !values.TryGetValue("installdir", out var dir))
            return null;
        if (SteamTools.Contains(appId)) return null;
        long? size = values.TryGetValue("SizeOnDisk", out var s) && long.TryParse(s, out var b) && b > 0 ? b : null;
        var location = Path.Combine(libraryFolder, "steamapps", "common", dir);
        return new InstalledApp(name, null, null, null, size, location, $"steam://uninstall/{appId}",
            $"steam:{appId}", IsBroken: false, Source: "Steam", IsGame: true);
    }

    /// <summary>Lit un manifeste Epic Games (*.item, en JSON).</summary>
    public static InstalledApp? ParseEpicManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Str(string n) => root.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var name = Str("DisplayName");
            var location = Str("InstallLocation");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location)) return null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) return null;
            long? size = root.TryGetProperty("InstallSize", out var sz) && sz.ValueKind == JsonValueKind.Number
                         && sz.TryGetInt64(out var b) && b > 0 ? b : null;
            return new InstalledApp(name, null, Str("AppVersionString"), null, size, location.Replace('/', Path.DirectorySeparatorChar),
                null, $"epic:{Str("AppName") ?? name}", IsBroken: false, Source: "Epic Games", IsGame: true);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Éditeurs dont les applications sont des jeux ou des launchers de jeux.</summary>
    private static readonly (string Key, string Source)[] GamePublishers =
    [
        ("valve", "Steam"), ("epicgames", "Epic Games"), ("electronicarts", "EA"), ("ubisoft", "Ubisoft"),
        ("blizzard", "Battle.net"), ("gogcom", "GOG"), ("gogsp", "GOG"), ("riotgames", "Riot Games"),
        ("rockstargames", "Rockstar"), ("bethesda", "Bethesda"), ("cdprojekt", "GOG"), ("mojang", "Minecraft"),
        ("activision", "Battle.net"), ("bandainamco", ""), ("squareenix", ""), ("2kgames", ""), ("sega", ""),
        ("paradoxinteractive", ""), ("hoyoverse", ""), ("cognosphere", ""), ("wargaming", ""), ("innersloth", ""),
    ];

    /// <summary>Morceaux de chemin typiques des dossiers de jeux.</summary>
    private static readonly (string Part, string Source)[] GameFolders =
    [
        (@"\steamapps\common\", "Steam"), (@"\Epic Games\", "Epic Games"), (@"\XboxGames\", "Xbox"),
        (@"\GOG Games\", "GOG"), (@"\GOG Galaxy\Games\", "GOG"), (@"\EA Games\", "EA"),
        (@"\Ubisoft Game Launcher\games\", "Ubisoft"), (@"\Riot Games\", "Riot Games"), (@"\Battle.net\", "Battle.net"),
    ];

    /// <summary>Marque comme jeux les applications déclarées dans Windows qui en sont.</summary>
    public static InstalledApp Classify(InstalledApp app)
    {
        if (app.IsGame) return app;
        var location = app.InstallLocation is null ? "" : Path.TrimEndingDirectorySeparator(app.InstallLocation).Replace('/', '\\') + "\\";
        foreach (var (part, source) in GameFolders)
            if (location.Contains(part, StringComparison.OrdinalIgnoreCase))
                return app with { IsGame = true, Source = app.Source ?? source };
        var publisher = NameMatcher.Normalize(app.Publisher);
        foreach (var (key, source) in GamePublishers)
            if (publisher.StartsWith(key, StringComparison.Ordinal))
                return app with { IsGame = true, Source = app.Source ?? (source.Length == 0 ? null : source) };
        return app;
    }

    /// <summary>
    /// Fusionne la liste de Windows et celle des launchers. Un jeu présent des deux côtés garde l'entrée de Windows
    /// (qui sait le désinstaller) complétée par le launcher (taille, dossier, source).
    /// </summary>
    public static IReadOnlyList<InstalledApp> Merge(IEnumerable<InstalledApp> registryApps, IEnumerable<InstalledApp> launcherGames)
    {
        var result = registryApps.Select(Classify).ToList();
        foreach (var game in launcherGames)
        {
            var gameName = NameMatcher.Normalize(game.Name);
            var gameDir = Normalize(game.InstallLocation);
            var index = result.FindIndex(a =>
                (gameDir is not null && Normalize(a.InstallLocation) == gameDir)
                || (gameName.Length >= 4 && NameMatcher.Normalize(a.Name) == gameName));
            if (index < 0)
            {
                if (!result.Any(a => a.RegistryKey == game.RegistryKey)) result.Add(game);
                continue;
            }
            var a = result[index];
            result[index] = a with
            {
                IsGame = true,
                Source = game.Source ?? a.Source,
                SizeBytes = game.SizeBytes ?? a.SizeBytes,
                InstallLocation = a.InstallLocation ?? game.InstallLocation,
            };
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.TrimEndingDirectorySeparator(path.Replace('/', '\\')).ToUpperInvariant();

    /// <summary>Outils de Steam qui ne sont pas des jeux (redistribuables, runtimes).</summary>
    private static readonly HashSet<string> SteamTools = ["228980", "1070560", "1391110", "1628350", "1493710"];

    private static IEnumerable<(string Key, string Value)> VdfPairs(string text)
    {
        foreach (Match m in VdfPair().Matches(text))
            yield return (m.Groups[1].Value, m.Groups[2].Value.Replace(@"\\", @"\"));
    }

    [GeneratedRegex("\"([^\"]+)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex VdfPair();
}
