namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>Un pilote graphique de plus de 3 mois mérite une mise à jour, plus de 6 mois c'est un problème.</summary>
public sealed class GpuDriverAgeCheck : IDiagnosticCheck
{
    public const int WarningDays = 90;
    public const int ProblemDays = 180;

    public string Id => "gpu-driver-age";
    public string Title => "Pilote de la carte graphique";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        var gpus = s.Gpus.Where(g => g.DriverDate is not null).ToList();
        if (gpus.Count == 0)
            return new(Id, Title, CheckStatus.Unknown, "Date du pilote graphique introuvable.");

        // La carte la plus en retard décide du verdict.
        var gpu = gpus.OrderBy(g => g.DriverDate).First();
        var today = DateOnly.FromDateTime(s.TakenAt.Date);
        var ageDays = today.DayNumber - gpu.DriverDate!.Value.DayNumber;
        var months = ageDays / 30;
        var version = gpu.DriverVersion is null ? "" : $" (version {gpu.DriverVersion})";
        var summary = $"{gpu.Name} : pilote du {Format.Date(gpu.DriverDate.Value)}{version}"
                      + (months >= 1 ? $", il a {months} mois." : ", il est récent.");

        const string advice =
            "Mets à jour le pilote depuis l'application NVIDIA, AMD ou Intel, en choisissant l'installation personnalisée puis « installation propre ».";

        return ageDays switch
        {
            > ProblemDays => new(Id, Title, CheckStatus.Problem, summary, advice),
            > WarningDays => new(Id, Title, CheckStatus.Warning, summary, advice),
            _ => new(Id, Title, CheckStatus.Ok, summary),
        };
    }
}
