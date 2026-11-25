namespace OptimizingSoftware.Core.Diagnostics.Checks;

public sealed class MemoryUsageCheck : IDiagnosticCheck
{
    public const double WarningRatio = 0.80;
    public const double ProblemRatio = 0.90;

    public string Id => "memory-usage";
    public string Title => "Mémoire utilisée";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (s.Memory is not { TotalBytes: > 0 } mem)
            return new(Id, Title, CheckStatus.Unknown, "Quantité de RAM introuvable.");

        var summary = $"{Format.Percent(mem.UsedRatio)} de {Format.Gigabytes(mem.TotalBytes)} utilisés en ce moment.";
        const string advice =
            "Ferme le navigateur, Discord ou OBS pendant que tu joues. Si ça reste haut, passer à 32 Go est peu coûteux.";

        return mem.UsedRatio switch
        {
            >= ProblemRatio => new(Id, Title, CheckStatus.Problem, summary, advice),
            >= WarningRatio => new(Id, Title, CheckStatus.Warning, summary, advice),
            _ => new(Id, Title, CheckStatus.Ok, summary),
        };
    }
}
