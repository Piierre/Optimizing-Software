using System.Runtime.Versioning;
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Windows;

/// <summary>
/// Ce que l'application sait nettoyer sans droits administrateur. Uniquement des fichiers que Windows
/// ou les applications recréent d'eux-mêmes : aucun document, aucune sauvegarde, aucun réglage.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsCleanupRules
{
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Corbeille interne de l'application.</summary>
    public static string QuarantineRoot => Path.Combine(Local, "OptimizingSoftware", "Corbeille");

    private const string CloseBrowser = "Ferme le navigateur avant de nettoyer, sinon une partie du cache est ignorée. Tu resteras connecté à tes sites.";

    public static IReadOnlyList<CleanupRule> Default() =>
    [
        new("user-temp", "Fichiers temporaires",
            "Fichiers laissés par Windows, les installations et les applications dans le dossier temporaire.",
            [Path.GetTempPath()], TimeSpan.FromDays(1)),

        new("crash-reports", "Rapports de plantage",
            "Rapports d'erreur Windows et vidages mémoire des applications qui ont planté.",
            [
                Path.Combine(Local, "CrashDumps"),
                Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue"),
            ], TimeSpan.FromDays(1)),

        new("chrome", "Cache de Google Chrome", "Pages et images gardées en mémoire par Chrome.",
            BrowserCaches(Path.Combine(Local, @"Google\Chrome\User Data")), TimeSpan.FromHours(1), Note: CloseBrowser),

        new("edge", "Cache de Microsoft Edge", "Pages et images gardées en mémoire par Edge.",
            BrowserCaches(Path.Combine(Local, @"Microsoft\Edge\User Data")), TimeSpan.FromHours(1), Note: CloseBrowser),

        new("brave", "Cache de Brave", "Pages et images gardées en mémoire par Brave.",
            BrowserCaches(Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")), TimeSpan.FromHours(1), Note: CloseBrowser),

        new("opera-gx", "Cache d'Opera GX", "Pages et images gardées en mémoire par Opera GX.",
            [
                Path.Combine(Local, @"Opera Software\Opera GX Stable\Cache"),
                Path.Combine(Roaming, @"Opera Software\Opera GX Stable\Code Cache"),
                Path.Combine(Roaming, @"Opera Software\Opera GX Stable\GPUCache"),
            ], TimeSpan.FromHours(1), Note: CloseBrowser),

        new("firefox", "Cache de Firefox", "Pages et images gardées en mémoire par Firefox.",
            [Path.Combine(Local, @"Mozilla\Firefox\Profiles\*\cache2")], TimeSpan.FromHours(1), Note: CloseBrowser),

        new("discord", "Cache de Discord", "Images, vidéos et fichiers déjà vus dans Discord.",
            [
                Path.Combine(Roaming, @"discord\Cache"),
                Path.Combine(Roaming, @"discord\Code Cache"),
                Path.Combine(Roaming, @"discord\GPUCache"),
            ], TimeSpan.FromHours(1), Note: "Ferme Discord avant de nettoyer."),

        new("steam-web", "Cache web de Steam", "Pages du magasin et de la communauté Steam. Ne touche pas aux jeux.",
            [Path.Combine(Local, @"Steam\htmlcache")], TimeSpan.FromHours(1), Note: "Ferme Steam avant de nettoyer."),

        new("shader-cache", "Caches de shaders (carte graphique)",
            "Shaders précompilés par le pilote graphique pour les jeux.",
            [
                Path.Combine(Local, @"NVIDIA\DXCache"),
                Path.Combine(Local, @"NVIDIA\GLCache"),
                Path.Combine(Local, "D3DSCache"),
                Path.Combine(Local, @"AMD\DxCache"),
                Path.Combine(Local, @"AMD\GLCache"),
            ], TimeSpan.FromDays(1), SelectedByDefault: false,
            Note: "À ne vider que juste après une mise à jour du pilote ou en cas de bugs graphiques : sinon les jeux saccadent le temps de reconstruire ce cache."),
    ];

    /// <summary>Caches des navigateurs basés sur Chromium, pour tous les profils.</summary>
    private static string[] BrowserCaches(string userData) =>
    [
        Path.Combine(userData, @"*\Cache"),
        Path.Combine(userData, @"*\Code Cache"),
        Path.Combine(userData, @"*\GPUCache"),
        Path.Combine(userData, @"*\Service Worker\CacheStorage"),
    ];
}
