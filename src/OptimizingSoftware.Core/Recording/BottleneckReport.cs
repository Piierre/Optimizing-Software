namespace OptimizingSoftware.Core.Recording;

/// <param name="Limiter">Le composant qui limite le plus.</param>
/// <param name="Verdict">Une phrase : ce qui limite.</param>
/// <param name="Explanation">Ce que les mesures montrent.</param>
/// <param name="Upgrades">Conseils d'amélioration, du plus utile au moins utile.</param>
/// <param name="Warnings">Points annexes (températures, etc.).</param>
/// <param name="LimiterShares">Part du temps en jeu où chaque composant saturait (0 à 1).</param>
public sealed record BottleneckReport(
    Limiter Limiter,
    string Verdict,
    string Explanation,
    IReadOnlyList<string> Upgrades,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MetricStats> Stats,
    IReadOnlyDictionary<Limiter, double> LimiterShares,
    TimeSpan Duration,
    TimeSpan LoadedDuration);
