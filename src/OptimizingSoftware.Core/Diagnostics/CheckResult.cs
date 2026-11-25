namespace OptimizingSoftware.Core.Diagnostics;

public enum CheckStatus
{
    /// <summary>Tout va bien.</summary>
    Ok,
    /// <summary>À surveiller ou petit gain possible.</summary>
    Warning,
    /// <summary>Problème à corriger en priorité.</summary>
    Problem,
    /// <summary>Information non disponible sur ce PC.</summary>
    Unknown,
}

/// <param name="Id">Identifiant stable, par exemple « gpu-driver-age ».</param>
/// <param name="Title">Titre court affiché à l'utilisateur.</param>
/// <param name="Summary">Ce qui a été constaté, en une phrase.</param>
/// <param name="Advice">Que faire, si une action est utile.</param>
public sealed record CheckResult(
    string Id,
    string Title,
    CheckStatus Status,
    string Summary,
    string? Advice = null);
