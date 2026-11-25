namespace OptimizingSoftware.Core.Recording;

/// <summary>
/// Une mesure prise pendant l'enregistrement (en général une par seconde). Les pourcentages vont de 0 à 100.
/// Une valeur null signifie « non mesurable sur ce PC ».
/// </summary>
/// <param name="CpuPercent">Utilisation totale du processeur.</param>
/// <param name="CpuBusiestCorePercent">Utilisation du cœur le plus chargé : un jeu limité par un seul cœur le montre ici.</param>
/// <param name="GpuPercent">Utilisation de la carte graphique (moteur 3D).</param>
/// <param name="RamPercent">Part de la mémoire utilisée.</param>
/// <param name="DiskPercent">Activité du disque le plus occupé.</param>
public sealed record PerformanceSample(
    TimeSpan At,
    double CpuPercent,
    double CpuBusiestCorePercent,
    double? GpuPercent,
    double RamPercent,
    double DiskPercent,
    double? GpuTemperatureC = null,
    double? CpuTemperatureC = null);

public enum Limiter
{
    /// <summary>Pas assez de mesures pendant une partie.</summary>
    NotEnoughData,
    /// <summary>Aucun composant ne sature.</summary>
    None,
    Gpu,
    Cpu,
    Ram,
    Disk,
}

/// <summary>Moyenne et pic (95e centile, pour ignorer les pointes isolées) d'une mesure.</summary>
public sealed record MetricStats(string Name, double Average, double P95, double Max);
