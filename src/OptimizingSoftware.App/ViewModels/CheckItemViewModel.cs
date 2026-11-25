using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.App.ViewModels;

public sealed class CheckItemViewModel(CheckResult result)
{
    public string Title => result.Title;
    public string Summary => result.Summary;
    public string? Advice => result.Advice;
    public bool HasAdvice => !string.IsNullOrEmpty(result.Advice);
    public CheckStatus Status => result.Status;

    public string StatusLabel => result.Status switch
    {
        CheckStatus.Problem => "À corriger",
        CheckStatus.Warning => "À surveiller",
        CheckStatus.Ok => "OK",
        _ => "Inconnu",
    };

    /// <summary>Clé de la couleur définie dans App.xaml.</summary>
    public string BrushKey => result.Status switch
    {
        CheckStatus.Problem => "ProblemBrush",
        CheckStatus.Warning => "WarningBrush",
        CheckStatus.Ok => "OkBrush",
        _ => "UnknownBrush",
    };
}
