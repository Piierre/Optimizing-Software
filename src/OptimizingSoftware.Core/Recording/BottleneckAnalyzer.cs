using System.Globalization;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Core.Recording;

/// <summary>
/// Trouve le composant qui limite pendant une partie, avec les règles du diagnostic fait à la main :
/// carte graphique à 95-100 % → elle limite ; processeur à 85 % ou plus (ou un cœur à 90 % ou plus) avec la
/// carte graphique qui attend (sous 75 %) → le processeur ; RAM au-dessus de 90 % ; disque à 100 %.
/// Un processeur qui monte au maximum par pointes alors que la carte graphique reste très basse limite aussi :
/// ce sont ces pointes qui font les saccades.
/// </summary>
public static class BottleneckAnalyzer
{
    /// <summary>Une mesure compte « en jeu » si le processeur ou la carte graphique travaille vraiment.</summary>
    public const double LoadThreshold = 50;

    /// <summary>Il faut au moins 60 mesures en jeu (1 minute à 1 mesure par seconde).</summary>
    public const int MinLoadedSamples = 60;

    /// <summary>Un composant limite s'il sature pendant au moins 30 % du temps en jeu.</summary>
    public const double LimiterShare = 0.30;

    public const double GpuSaturated = 95;
    /// <summary>Sous ce niveau, la carte graphique « attend » le processeur.</summary>
    public const double GpuBelow = 75;
    public const double CpuSaturated = 85;
    public const double CoreSaturated = 90;

    /// <summary>Carte graphique en moyenne sous ce niveau : elle est clairement sous-utilisée.</summary>
    public const double GpuIdleAverage = 60;
    /// <summary>Processeur chargé en moyenne au-dessus de ce niveau (et pas juste un plafond de FPS).</summary>
    public const double CpuBusyAverage = 60;
    /// <summary>Pointes (95e centile) au-delà desquelles le processeur est au maximum par moments.</summary>
    public const double CpuPeak = 90;
    public const double CorePeak = 95;

    public const double RamSaturated = 90;
    /// <summary>RAM utilisée en moyenne au-dessus de ce niveau : on le signale, même si elle ne limite pas encore.</summary>
    public const double RamHigh = 80;
    public const double DiskSaturated = 95;
    public const double GpuHotC = 83;
    public const double CpuHotC = 90;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static BottleneckReport Analyze(IReadOnlyList<PerformanceSample> samples, SystemSnapshot? pc = null)
    {
        var duration = samples.Count == 0 ? TimeSpan.Zero : samples[^1].At - samples[0].At;
        var loaded = samples
            .Where(s => s.CpuPercent >= LoadThreshold || s.GpuPercent >= LoadThreshold)
            .ToList();
        var loadedDuration = TimeSpan.FromSeconds(loaded.Count * (samples.Count > 1 ? duration.TotalSeconds / (samples.Count - 1) : 1));
        var stats = Stats(loaded.Count > 0 ? loaded : samples);
        var warnings = TemperatureWarnings(samples);

        if (loaded.Count < MinLoadedSamples)
        {
            return new BottleneckReport(Limiter.NotEnoughData,
                "Pas assez de mesures pendant une partie.",
                $"Seulement {loaded.Count} seconde(s) où le PC travaillait vraiment. Lance l'enregistrement, " +
                "joue au moins 2 minutes à un passage qui rame, puis arrête l'enregistrement.",
                UpgradeAdvisor.Advise(Limiter.NotEnoughData, pc), warnings, stats, new Dictionary<Limiter, double>(), duration, loadedDuration);
        }

        var hasGpu = loaded.Any(s => s.GpuPercent is not null);
        var shares = new Dictionary<Limiter, double>
        {
            [Limiter.Gpu] = hasGpu ? Share(loaded, s => s.GpuPercent >= GpuSaturated) : 0,
            [Limiter.Cpu] = Share(loaded, s =>
                (s.CpuPercent >= CpuSaturated || s.CpuBusiestCorePercent >= CoreSaturated)
                && (s.GpuPercent is null || s.GpuPercent < GpuBelow)),
            [Limiter.Ram] = Share(loaded, s => s.RamPercent >= RamSaturated),
            [Limiter.Disk] = Share(loaded, s => s.DiskPercent >= DiskSaturated),
        };

        // En cas d'égalité, l'ordre du dictionnaire (GPU, CPU, RAM, disque) départage.
        var (limiter, share) = shares.MaxBy(kv => kv.Value) is var best && best.Value >= LimiterShare
            ? (best.Key, best.Value)
            : (Limiter.None, 0d);

        // Processeur au maximum par pointes alors que la carte graphique reste très basse.
        var cpuPeaks = false;
        if (limiter == Limiter.None && hasGpu)
        {
            var gpuAvg = loaded.Where(s => s.GpuPercent is not null).Average(s => s.GpuPercent!.Value);
            var cpuAvg = loaded.Average(s => s.CpuPercent);
            cpuPeaks = gpuAvg < GpuIdleAverage && cpuAvg >= CpuBusyAverage
                       && (P95(loaded.Select(s => s.CpuPercent)) >= CpuPeak || P95(loaded.Select(s => s.CpuBusiestCorePercent)) >= CorePeak);
            if (cpuPeaks) limiter = Limiter.Cpu;
        }

        var ramAvg = loaded.Average(s => s.RamPercent);
        var ramHigh = limiter != Limiter.Ram && ramAvg >= RamHigh;
        if (ramHigh)
            warnings.Insert(0, $"La RAM est utilisée à {ramAvg:0} % en moyenne pendant la partie (jusqu'à {loaded.Max(s => s.RamPercent):0} %). " +
                               "Ce n'est pas encore elle qui limite, mais il reste peu de marge : au-delà de 90 %, Windows se sert du disque et ça saccade.");

        var pct = (share * 100).ToString("0", Fr) + " %";
        var (verdict, explanation) = limiter switch
        {
            Limiter.Gpu => ("La carte graphique limite.",
                $"Elle est à {GpuSaturated:0} % ou plus pendant {pct} du temps en jeu. C'est le cas le plus fréquent : baisser la résolution ou les réglages graphiques aide tout de suite."),
            Limiter.Cpu when cpuPeaks => ("Le processeur limite.",
                "La carte graphique reste très peu utilisée pendant que le processeur monte au maximum par moments : elle attend qu'il lui prépare les images. Ces pointes donnent des saccades et des chutes de FPS."),
            Limiter.Cpu => ("Le processeur limite.",
                $"Il sature (ou l'un de ses cœurs est au maximum) pendant {pct} du temps en jeu, alors que la carte graphique attend. Ça se traduit par des saccades et un lag général plutôt que par des FPS bas partout."),
            Limiter.Ram => ("La mémoire (RAM) limite.",
                $"Plus de {RamSaturated:0} % de la RAM est utilisée pendant {pct} du temps en jeu : Windows doit alors se servir du disque, ce qui crée des saccades."),
            Limiter.Disk => ("Le disque limite.",
                $"Le disque est occupé à 100 % pendant {pct} du temps en jeu : chargements longs et micro-saccades."),
            _ => ("Aucun composant ne sature.",
                "Ni le processeur, ni la carte graphique, ni la RAM, ni le disque ne sont au maximum. Le lag vient peut-être d'un plafond de FPS (V-Sync, limite du jeu), du réseau, ou de saccades ponctuelles : regarde les autres vérifications."),
        };

        var advice = UpgradeAdvisor.Advise(limiter, pc).ToList();
        if (ramHigh) advice.AddRange(UpgradeAdvisor.Advise(Limiter.Ram, pc).Where(a => !advice.Contains(a)));

        return new BottleneckReport(limiter, verdict, explanation, advice, warnings,
            stats, shares, duration, loadedDuration);
    }

    private static double P95(IEnumerable<double> values)
    {
        var v = values.Order().ToList();
        return v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)Math.Ceiling(v.Count * 0.95) - 1)];
    }

    private static double Share(List<PerformanceSample> samples, Func<PerformanceSample, bool> predicate) =>
        samples.Count == 0 ? 0 : (double)samples.Count(predicate) / samples.Count;

    private static List<MetricStats> Stats(IReadOnlyList<PerformanceSample> samples)
    {
        var list = new List<MetricStats>();
        void Add(string name, IEnumerable<double?> values)
        {
            var v = values.Where(x => x is not null).Select(x => x!.Value).Order().ToList();
            if (v.Count == 0) return;
            var p95 = v[Math.Min(v.Count - 1, (int)Math.Ceiling(v.Count * 0.95) - 1)];
            list.Add(new MetricStats(name, v.Average(), p95, v[^1]));
        }
        Add("Processeur (%)", samples.Select(s => (double?)s.CpuPercent));
        Add("Cœur le plus chargé (%)", samples.Select(s => (double?)s.CpuBusiestCorePercent));
        Add("Carte graphique (%)", samples.Select(s => s.GpuPercent));
        Add("RAM (%)", samples.Select(s => (double?)s.RamPercent));
        Add("Disque (%)", samples.Select(s => (double?)s.DiskPercent));
        Add("Température carte graphique (°C)", samples.Select(s => s.GpuTemperatureC));
        Add("Température processeur (°C)", samples.Select(s => s.CpuTemperatureC));
        return list;
    }

    private static List<string> TemperatureWarnings(IReadOnlyList<PerformanceSample> samples)
    {
        var warnings = new List<string>();
        var gpuMax = samples.Max(s => s.GpuTemperatureC);
        if (gpuMax > GpuHotC)
            warnings.Add($"La carte graphique monte à {gpuMax:0} °C (au-delà de {GpuHotC:0} °C, elle ralentit d'elle-même) : dépoussière-la et vérifie la ventilation du boîtier. Ça peut aussi expliquer des plantages.");
        var cpuMax = samples.Max(s => s.CpuTemperatureC);
        if (cpuMax > CpuHotC)
            warnings.Add($"Le processeur monte à {cpuMax:0} °C : vérifie le ventirad et la pâte thermique.");
        if (samples.Count > 0 && samples.All(s => s.GpuTemperatureC is null))
            warnings.Add("Température de la carte graphique non disponible (prise en charge des cartes NVIDIA uniquement pour l'instant).");
        return warnings;
    }
}
