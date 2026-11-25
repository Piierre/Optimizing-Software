namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>Plantages ou redémarrages du pilote graphique dans les 30 derniers jours.</summary>
public sealed class GpuCrashCheck : IDiagnosticCheck
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>
    /// Windows enregistre souvent un même plantage plusieurs fois (rapport WER créé puis envoyé,
    /// événement 4101 en plus). Les événements à moins de 10 minutes d'écart comptent pour un seul plantage.
    /// </summary>
    public static readonly TimeSpan SameIncidentGap = TimeSpan.FromMinutes(10);

    public string Id => "gpu-crashes";
    public string Title => "Plantages de la carte graphique";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (!s.GpuCrashesCollected)
            return new(Id, Title, CheckStatus.Unknown, "Impossible de lire les journaux d'événements de Windows.");

        var incidents = GroupIncidents(s.GpuCrashes.Where(c => s.TakenAt - c.OccurredAt <= Window));

        if (incidents.Count == 0)
            return new(Id, Title, CheckStatus.Ok, "Aucun plantage de la carte graphique depuis 30 jours.");

        var dates = string.Join(", ", incidents.Select(i => Format.Date(i.OccurredAt)).Distinct());
        var last = incidents[0];
        var summary = incidents.Count == 1
            ? $"1 plantage depuis 30 jours : {last.Kind} le {Format.Date(last.OccurredAt)}."
            : $"{incidents.Count} plantages depuis 30 jours ({dates}), le dernier : {last.Kind}.";

        const string advice =
            "Fais une installation propre du dernier pilote, surveille la température de la carte en jeu " +
            "et retire tout overclock. Si ça continue, vérifie l'alimentation.";

        return new(Id, Title, incidents.Count >= 3 ? CheckStatus.Problem : CheckStatus.Warning, summary, advice);
    }

    /// <summary>Regroupe les événements proches en plantages distincts, du plus récent au plus ancien.</summary>
    public static IReadOnlyList<GpuCrashEvent> GroupIncidents(IEnumerable<GpuCrashEvent> events)
    {
        var incidents = new List<List<GpuCrashEvent>>();
        foreach (var e in events.OrderBy(e => e.OccurredAt))
        {
            if (incidents.Count > 0 && e.OccurredAt - incidents[^1][^1].OccurredAt <= SameIncidentGap)
                incidents[^1].Add(e);
            else
                incidents.Add([e]);
        }

        // Pour chaque plantage, on garde l'événement le plus parlant (LiveKernelEvent plutôt que 4101).
        return incidents
            .Select(group => group.FirstOrDefault(e => e.Kind.StartsWith("LiveKernelEvent", StringComparison.Ordinal)) ?? group[0])
            .OrderByDescending(e => e.OccurredAt)
            .ToList();
    }
}
