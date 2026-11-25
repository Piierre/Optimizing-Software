namespace OptimizingSoftware.Core.Diagnostics.Checks;

public sealed class HardwareGpuSchedulingCheck : IDiagnosticCheck
{
    public string Id => "gpu-scheduling";
    public string Title => "Planification GPU à accélération matérielle";

    public CheckResult Evaluate(SystemSnapshot s) => s.HardwareGpuSchedulingEnabled switch
    {
        true => new(Id, Title, CheckStatus.Ok, "Activée."),
        false => new(Id, Title, CheckStatus.Warning,
            "Désactivée. Elle peut réduire un peu la latence et soulager le processeur.",
            "Paramètres > Système > Affichage > Paramètres graphiques : active l'option, puis redémarre le PC."),
        null => new(Id, Title, CheckStatus.Unknown,
            "Réglage introuvable : la carte graphique ou son pilote ne la prend peut-être pas en charge."),
    };
}
