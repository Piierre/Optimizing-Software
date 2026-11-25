namespace OptimizingSoftware.Core.Diagnostics.Checks;

public sealed class PowerPlanCheck : IDiagnosticCheck
{
    public string Id => "power-plan";
    public string Title => "Mode d'alimentation";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        if (s.PowerPlan is not { } plan)
            return new(Id, Title, CheckStatus.Unknown, "Mode d'alimentation introuvable.");

        var name = plan.Name ?? "personnalisé";
        const string advice =
            "Panneau de configuration > Options d'alimentation : choisis « Performances élevées » pour jouer.";

        if (plan.Id == PowerPlanInfo.PowerSaver)
            return new(Id, Title, CheckStatus.Problem, $"Mode « {name} » : le processeur est bridé.", advice);
        if (plan.Id == PowerPlanInfo.Balanced)
            return new(Id, Title, CheckStatus.Warning,
                $"Mode « {name} » : correct au quotidien, mais pas le plus réactif en jeu.", advice);
        if (plan.Id == PowerPlanInfo.HighPerformance || plan.Id == PowerPlanInfo.UltimatePerformance)
            return new(Id, Title, CheckStatus.Ok, $"Mode « {name} ».");
        return new(Id, Title, CheckStatus.Ok, $"Mode « {name} » (plan personnalisé, souvent installé par le fabricant).");
    }
}
