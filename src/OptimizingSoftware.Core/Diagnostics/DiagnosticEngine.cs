using OptimizingSoftware.Core.Diagnostics.Checks;

namespace OptimizingSoftware.Core.Diagnostics;

/// <summary>Lance toutes les vérifications sur une photo du PC.</summary>
public sealed class DiagnosticEngine(IEnumerable<IDiagnosticCheck> checks)
{
    private readonly IReadOnlyList<IDiagnosticCheck> _checks = checks.ToList();

    public static IReadOnlyList<IDiagnosticCheck> DefaultChecks() =>
    [
        new GpuCrashCheck(),
        new DisplayRefreshRateCheck(),
        new GpuDriverAgeCheck(),
        new HardwareGpuSchedulingCheck(),
        new MemoryChannelCheck(),
        new MemorySpeedCheck(),
        new MemoryUsageCheck(),
        new SystemDiskCheck(),
        new PowerPlanCheck(),
        new CpuThreadsCheck(),
    ];

    public DiagnosticEngine() : this(DefaultChecks()) { }

    /// <summary>Résultats triés : problèmes d'abord, puis avertissements, OK et inconnus.</summary>
    public IReadOnlyList<CheckResult> Run(SystemSnapshot snapshot) =>
        _checks
            .Select(check => SafeEvaluate(check, snapshot))
            .OrderBy(r => r.Status switch
            {
                CheckStatus.Problem => 0,
                CheckStatus.Warning => 1,
                CheckStatus.Ok => 2,
                _ => 3,
            })
            .ToList();

    private static CheckResult SafeEvaluate(IDiagnosticCheck check, SystemSnapshot snapshot)
    {
        try
        {
            return check.Evaluate(snapshot);
        }
        catch (Exception ex)
        {
            return new CheckResult(check.Id, check.Title, CheckStatus.Unknown,
                $"La vérification a échoué : {ex.Message}");
        }
    }
}
