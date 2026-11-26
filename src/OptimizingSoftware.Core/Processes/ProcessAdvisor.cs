using OptimizingSoftware.Core.Startup;

namespace OptimizingSoftware.Core.Processes;

/// <summary>
/// Un programme en cours, avec ce qu'on sait de son fichier. Les processus d'un même programme (Chrome, Discord…)
/// sont regroupés : mémoire et processeur sont additionnés.
/// </summary>
/// <param name="Path">Programme du processus ; null si Windows ne le donne pas (processus protégé).</param>
/// <param name="CpuPercent">Part de tout le processeur, mesurée sur environ une seconde.</param>
public sealed record ProcessInfo(
    IReadOnlyList<int> Pids,
    string Name,
    string? Path,
    long MemoryBytes,
    double CpuPercent,
    string? Publisher,
    bool? Signed,
    string? Description);

public enum ProcessVerdict
{
    /// <summary>Rien à signaler.</summary>
    Normal,
    /// <summary>Utilise beaucoup de mémoire ou de processeur.</summary>
    Heavy,
    /// <summary>Tourne en arrière-plan sans être utile en permanence (launcher, outil de mise à jour…).</summary>
    Unnecessary,
    /// <summary>Signes qu'il pourrait être malveillant.</summary>
    Suspicious,
}

public sealed record ProcessAssessment(ProcessVerdict Verdict, IReadOnlyList<string> Reasons, bool CanClose);

/// <summary>
/// Règles prudentes pour repérer les processus suspects ou inutiles. Elles ne remplacent pas un antivirus :
/// elles signalent ce qui mérite un coup d'œil.
/// </summary>
public static class ProcessAdvisor
{
    public const long HeavyMemoryBytes = 1L << 30;      // 1 Go
    public const double HeavyCpuPercent = 25;

    /// <summary>Processus de Windows qu'on ne ferme jamais (le PC planterait ou redémarrerait).</summary>
    public static readonly IReadOnlySet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "fontdrvhost", "dwm", "explorer", "Memory Compression", "Secure System", "MsMpEng", "NisSrv", "SecurityHealthService",
        "spoolsv", "audiodg", "sihost", "taskhostw", "ctfmon", "conhost", "RuntimeBroker", "StartMenuExperienceHost",
        "ShellExperienceHost", "SearchHost", "TextInputHost", "dllhost", "WmiPrvSE", "OptimizingSoftware",
    };

    /// <summary>Noms de processus système souvent imités par les logiciels malveillants, et leur dossier normal.</summary>
    private static readonly Dictionary<string, string[]> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["svchost"] = [@"\Windows\System32\", @"\Windows\SysWOW64\"],
        ["lsass"] = [@"\Windows\System32\"],
        ["csrss"] = [@"\Windows\System32\"],
        ["winlogon"] = [@"\Windows\System32\"],
        ["services"] = [@"\Windows\System32\"],
        ["smss"] = [@"\Windows\System32\"],
        ["wininit"] = [@"\Windows\System32\"],
        ["taskhostw"] = [@"\Windows\System32\"],
        ["dwm"] = [@"\Windows\System32\"],
        ["conhost"] = [@"\Windows\System32\"],
        ["rundll32"] = [@"\Windows\System32\", @"\Windows\SysWOW64\"],
        ["explorer"] = [@"\Windows\"],
    };

    /// <summary>Mineurs de cryptomonnaie et outils d'accès à distance souvent installés à l'insu de l'utilisateur.</summary>
    private static readonly string[] KnownBad = ["xmrig", "minerd", "cpuminer", "nbminer", "phoenixminer", "t-rex", "lolminer", "nanominer"];

    /// <summary>Programmes qui tournent en fond sans être utiles en permanence.</summary>
    private static readonly (string Name, string Why)[] Background =
    [
        ("EpicGamesLauncher", "Launcher Epic Games ouvert en arrière-plan."),
        ("EADesktop", "Launcher EA ouvert en arrière-plan."),
        ("upc", "Ubisoft Connect ouvert en arrière-plan."),
        ("Battle.net", "Launcher Battle.net ouvert en arrière-plan."),
        ("GalaxyClient", "Launcher GOG Galaxy ouvert en arrière-plan."),
        ("RiotClientServices", "Launcher Riot ouvert en arrière-plan."),
        ("steamwebhelper", "Page web intégrée de Steam : elle se relance si Steam est ouvert."),
        ("AdobeARM", "Outil de mise à jour d'Adobe."),
        ("AdobeIPCBroker", "Service d'arrière-plan d'Adobe."),
        ("CCleaner64", "Nettoyeur qui surveille en permanence."),
        ("Overwolf", "Overlay pour jeux qui consomme en arrière-plan."),
        ("Cortana", "Assistant rarement utilisé."),
        ("OneDrive", "Synchronisation OneDrive : à garder si tu t'en sers."),
    ];

    public static ProcessAssessment Assess(ProcessInfo p)
    {
        var reasons = new List<string>();
        var name = p.Name;
        var path = p.Path?.Replace('/', '\\');
        var critical = Critical.Contains(name);

        // 1. Signes de logiciel malveillant.
        if (KnownBad.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)))
            reasons.Add("Son nom est celui d'un mineur de cryptomonnaie connu : il utilise ton PC pour quelqu'un d'autre.");
        if (path is not null && SystemNames.TryGetValue(name, out var expected)
            && !expected.Any(e => path.Contains(e, StringComparison.OrdinalIgnoreCase)))
            reasons.Add($"Il porte le nom d'un programme de Windows ({name}.exe) mais ne se trouve pas dans le dossier de Windows : c'est une technique classique pour se cacher.");
        if (path is not null && p.Signed == false && StartupAdvisor.IsUserWritable(path) && string.IsNullOrWhiteSpace(p.Publisher))
            reasons.Add("Programme sans signature ni éditeur, lancé depuis un dossier personnel ou temporaire.");
        if (reasons.Count > 0)
            return new ProcessAssessment(ProcessVerdict.Suspicious, reasons, CanClose: true); // y compris un faux « svchost »

        // 2. Arrière-plan inutile.
        foreach (var (bg, why) in Background)
            if (name.Equals(bg, StringComparison.OrdinalIgnoreCase))
                return new ProcessAssessment(ProcessVerdict.Unnecessary, [why], CanClose: true);

        // 3. Gourmand.
        if (p.MemoryBytes >= HeavyMemoryBytes)
            reasons.Add($"Utilise {Cleanup.Sizes.Format(p.MemoryBytes)} de mémoire.");
        if (p.CpuPercent >= HeavyCpuPercent)
            reasons.Add($"Utilise {p.CpuPercent:0} % du processeur.");
        return reasons.Count > 0
            ? new ProcessAssessment(ProcessVerdict.Heavy, reasons, CanClose: !critical)
            : new ProcessAssessment(ProcessVerdict.Normal, [], CanClose: !critical);
    }
}
