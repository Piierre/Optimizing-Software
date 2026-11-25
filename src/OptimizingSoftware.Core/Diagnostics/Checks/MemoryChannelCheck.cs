namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>Une seule barrette = single channel, ce qui coûte beaucoup en jeu.</summary>
public sealed class MemoryChannelCheck : IDiagnosticCheck
{
    public string Id => "memory-channel";
    public string Title => "Barrettes de RAM";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (s.Memory is not { Modules.Count: > 0 } mem)
            return new(Id, Title, CheckStatus.Unknown, "Barrettes de RAM introuvables.");

        var count = mem.Modules.Count;
        var slots = mem.TotalSlots is { } t ? $" sur {t} emplacements" : "";
        var sizes = mem.Modules.Select(m => m.CapacityBytes).Distinct().Count();

        if (count == 1)
            return new(Id, Title, CheckStatus.Problem,
                $"Une seule barrette ({Format.Gigabytes(mem.Modules[0].CapacityBytes)}){slots} : la RAM tourne en single channel.",
                "Ajoute une deuxième barrette identique pour passer en dual channel. C'est l'un des gains les moins chers en jeu.");

        if (count % 2 == 1 || sizes > 1)
            return new(Id, Title, CheckStatus.Warning,
                $"{count} barrettes de tailles ou en nombre dépareillés{slots} : le dual channel n'est peut-être que partiel.",
                "Idéalement, utilise 2 ou 4 barrettes identiques.");

        return new(Id, Title, CheckStatus.Ok,
            $"{count} barrettes de {Format.Gigabytes(mem.Modules[0].CapacityBytes)}{slots}, en dual channel.");
    }
}
