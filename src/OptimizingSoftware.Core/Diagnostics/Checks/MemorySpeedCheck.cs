namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>
/// Détecte une RAM qui tourne sous sa vitesse, souvent parce que le profil XMP/EXPO n'est pas activé dans le BIOS.
/// Windows ne donne pas la vitesse XMP elle-même : on compare à la vitesse annoncée et aux vitesses JEDEC par défaut.
/// </summary>
public sealed class MemorySpeedCheck : IDiagnosticCheck
{
    /// <summary>Vitesses « de base » sans XMP. Au-dessus, la RAM est très probablement déjà en XMP.</summary>
    private static readonly Dictionary<MemoryType, int> JedecDefaultMaxMHz = new()
    {
        [MemoryType.Ddr3] = 1600,
        [MemoryType.Ddr4] = 2666,
        [MemoryType.Ddr5] = 4800,
    };

    public string Id => "memory-speed";
    public string Title => "Vitesse de la RAM";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        var modules = s.Memory?.Modules.Where(m => m.ConfiguredSpeedMHz is > 0).ToList() ?? [];
        if (modules.Count == 0)
            return new(Id, Title, CheckStatus.Unknown, "Vitesse de la RAM introuvable.");

        var configured = modules.Min(m => m.ConfiguredSpeedMHz!.Value);
        var rated = modules.Max(m => m.RatedSpeedMHz ?? 0);
        var type = modules[0].Type;
        var typeLabel = type == MemoryType.Unknown ? "" : $"{type.ToString().ToUpperInvariant()} ";
        const string xmpAdvice =
            "Dans le BIOS, active le profil XMP (Intel) ou EXPO (AMD) pour que la RAM tourne à sa vitesse prévue.";

        if (rated > 0 && configured < rated)
            return new(Id, Title, CheckStatus.Warning,
                $"La RAM {typeLabel}tourne à {configured} MHz alors que les barrettes annoncent {rated} MHz.", xmpAdvice);

        if (JedecDefaultMaxMHz.TryGetValue(type, out var jedecMax) && configured <= jedecMax)
            return new(Id, Title, CheckStatus.Warning,
                $"La RAM {typeLabel}tourne à {configured} MHz, une vitesse par défaut : le profil XMP n'est probablement pas activé.",
                xmpAdvice + " Si tes barrettes sont vendues pour cette vitesse, tu peux ignorer ce point.");

        return new(Id, Title, CheckStatus.Ok, $"La RAM {typeLabel}tourne à {configured} MHz.");
    }
}
