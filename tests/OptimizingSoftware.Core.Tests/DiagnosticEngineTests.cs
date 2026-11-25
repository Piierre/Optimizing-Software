using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Core.Tests;

public class DiagnosticEngineTests
{
    private static Dictionary<string, CheckStatus> Statuses(SystemSnapshot s) =>
        new DiagnosticEngine().Run(s).ToDictionary(r => r.Id, r => r.Status);

    [Fact]
    public void Pierres_pc_reproduces_what_was_found_by_hand()
    {
        var statuses = Statuses(Snapshots.PierreBefore());

        Assert.Equal(CheckStatus.Problem, statuses["gpu-driver-age"]);   // pilote d'avril, 7 mois
        Assert.Equal(CheckStatus.Warning, statuses["gpu-crashes"]);      // LiveKernelEvent 141 du 15 novembre
        Assert.Equal(CheckStatus.Warning, statuses["display-refresh"]);  // 165 Hz + 60 Hz sous Windows 10
        Assert.Equal(CheckStatus.Warning, statuses["gpu-scheduling"]);   // désactivée
        Assert.Equal(CheckStatus.Warning, statuses["memory-usage"]);     // 13 Go sur 16
        Assert.Equal(CheckStatus.Warning, statuses["system-disk"]);      // 105 Go libres sur 1 To
        Assert.Equal(CheckStatus.Warning, statuses["power-plan"]);       // Utilisation normale
        Assert.Equal(CheckStatus.Warning, statuses["cpu-threads"]);      // 6 cœurs sans hyperthreading
        Assert.Equal(CheckStatus.Ok, statuses["memory-channel"]);        // 2 barrettes de 8 Go
        Assert.Equal(CheckStatus.Ok, statuses["memory-speed"]);          // 3200 MHz
    }

    [Fact]
    public void Healthy_pc_is_all_ok()
    {
        var notOk = new DiagnosticEngine().Run(Snapshots.Healthy()).Where(r => r.Status != CheckStatus.Ok).ToList();
        Assert.Empty(notOk);
    }

    [Fact]
    public void Empty_snapshot_gives_unknown_instead_of_crashing()
    {
        var results = new DiagnosticEngine().Run(new SystemSnapshot());
        Assert.All(results, r => Assert.Equal(CheckStatus.Unknown, r.Status));
    }

    [Fact]
    public void Results_are_sorted_by_severity()
    {
        var results = new DiagnosticEngine().Run(Snapshots.PierreBefore());
        Assert.Equal(CheckStatus.Problem, results[0].Status);
        Assert.Equal(CheckStatus.Ok, results[^1].Status);
    }

    [Fact]
    public void A_failing_check_becomes_unknown()
    {
        var engine = new DiagnosticEngine([new ThrowingCheck()]);
        var result = Assert.Single(engine.Run(Snapshots.PierreBefore()));
        Assert.Equal(CheckStatus.Unknown, result.Status);
    }

    private sealed class ThrowingCheck : IDiagnosticCheck
    {
        public string Id => "boom";
        public string Title => "Boom";
        public CheckResult Evaluate(SystemSnapshot snapshot) => throw new InvalidOperationException("boom");
    }
}
