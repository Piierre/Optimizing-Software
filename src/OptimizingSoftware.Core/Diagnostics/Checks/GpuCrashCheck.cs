namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>Plantages ou redémarrages du pilote graphique dans les 30 derniers jours.</summary>
public sealed class GpuCrashCheck : IDiagnosticCheck
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public string Id => "gpu-crashes";
    public string Title => "Plantages de la carte graphique";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (!s.GpuCrashesCollected)
            return new(Id, Title, CheckStatus.Unknown, "Impossible de lire les journaux d'événements de Windows.");

        var recent = s.GpuCrashes
            .Where(c => s.TakenAt - c.OccurredAt <= Window)
            .OrderByDescending(c => c.OccurredAt)
            .ToList();

        if (recent.Count == 0)
            return new(Id, Title, CheckStatus.Ok, "Aucun plantage de la carte graphique depuis 30 jours.");

        var last = recent[0];
        var summary = recent.Count == 1
            ? $"1 plantage depuis 30 jours : {last.Kind} le {Format.Date(last.OccurredAt)}."
            : $"{recent.Count} plantages depuis 30 jours, le dernier ({last.Kind}) le {Format.Date(last.OccurredAt)}.";

        const string advice =
            "Fais une installation propre du dernier pilote, surveille la température de la carte en jeu " +
            "et retire tout overclock. Si ça continue, vérifie l'alimentation.";

        return new(Id, Title, recent.Count >= 3 ? CheckStatus.Problem : CheckStatus.Warning, summary, advice);
    }
}
