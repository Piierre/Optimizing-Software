namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>
/// Moins de 8 threads, c'est juste pour les jeux récents : ça se traduit par des saccades plutôt que des FPS bas.
/// Ce n'est qu'un indice : seul l'enregistrement en jeu (étape suivante) confirme que le processeur limite.
/// </summary>
public sealed class CpuThreadsCheck : IDiagnosticCheck
{
    public const int RecommendedThreads = 8;

    public string Id => "cpu-threads";
    public string Title => "Processeur";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (s.Cpu is not { } cpu)
            return new(Id, Title, CheckStatus.Unknown, "Processeur introuvable.");

        var summary = $"{cpu.Name} : {cpu.Cores} cœurs, {cpu.LogicalProcessors} threads.";
        if (cpu.LogicalProcessors < RecommendedThreads)
            return new(Id, Title, CheckStatus.Warning,
                summary + " C'est léger pour les jeux récents, et ça se manifeste souvent par des saccades.",
                "Pendant une partie qui rame, regarde si le processeur est à 90-100 % alors que la carte graphique ne l'est pas : c'est alors lui qui limite.");
        return new(Id, Title, CheckStatus.Ok, summary);
    }
}
