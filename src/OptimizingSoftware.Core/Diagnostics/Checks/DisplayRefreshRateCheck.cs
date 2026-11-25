namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>
/// Chaque écran doit tourner à sa fréquence maximale, et des écrans à fréquences différentes
/// causent des micro-saccades (surtout sous Windows 10).
/// </summary>
public sealed class DisplayRefreshRateCheck : IDiagnosticCheck
{
    /// <summary>59 Hz et 60 Hz (59,94 Hz arrondi) sont la même fréquence en pratique.</summary>
    private const int ToleranceHz = 1;

    public string Id => "display-refresh";
    public string Title => "Fréquence des écrans";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (s.Displays.Count == 0)
            return new(Id, Title, CheckStatus.Unknown, "Aucun écran détecté.");

        var underclocked = s.Displays.Where(d => d.MaxRefreshHz - d.CurrentRefreshHz > ToleranceHz).ToList();
        if (underclocked.Count > 0)
        {
            var list = string.Join(", ", underclocked.Select(d =>
                $"{d.Name} est à {d.CurrentRefreshHz} Hz alors qu'il peut monter à {d.MaxRefreshHz} Hz"));
            return new(Id, Title, CheckStatus.Problem, list + ".",
                "Paramètres > Système > Affichage > Affichage avancé : choisis la fréquence la plus haute. " +
                "Vérifie aussi que l'écran est branché sur la carte graphique, en DisplayPort ou HDMI récent.");
        }

        var rates = s.Displays.Select(d => d.CurrentRefreshHz).Distinct().Order().ToList();
        if (rates[^1] - rates[0] > ToleranceHz)
        {
            var detail = string.Join(" et ", s.Displays.Select(d => $"{d.Name} à {d.CurrentRefreshHz} Hz"));
            var advice = "Teste une partie avec le second écran éteint ou sans vidéo dessus : si les saccades disparaissent, c'est la cause.";
            if (s.Os is { IsWindows11: false })
                advice += " Sous Windows 10, ce mélange de fréquences est une cause connue de micro-saccades.";
            return new(Id, Title, CheckStatus.Warning, $"Écrans à fréquences différentes : {detail}.", advice);
        }

        var only = s.Displays.Count == 1
            ? $"{s.Displays[0].Name} tourne à {s.Displays[0].CurrentRefreshHz} Hz, sa fréquence maximale."
            : $"Les {s.Displays.Count} écrans tournent à {rates[^1]} Hz, leur fréquence maximale.";
        return new(Id, Title, CheckStatus.Ok, only);
    }
}
