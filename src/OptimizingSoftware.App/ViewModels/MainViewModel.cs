using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.App.ViewModels;

public sealed partial class MainViewModel(ISystemSnapshotProvider provider, DiagnosticEngine engine) : ObservableObject
{
    public ObservableCollection<CheckItemViewModel> Checks { get; } = [];
    public ObservableCollection<InventoryLine> Inventory { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string? _summary;

    [ObservableProperty]
    private string? _error;

    public bool HasResults => Summary is not null;

    [RelayCommand]
    private async Task RunDiagnosticAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try
        {
            var snapshot = await provider.CaptureAsync(cancellationToken);
            var results = engine.Run(snapshot);

            Inventory.Clear();
            foreach (var line in InventoryLine.From(snapshot))
                Inventory.Add(line);

            Checks.Clear();
            foreach (var result in results)
                Checks.Add(new CheckItemViewModel(result));

            Summary = BuildSummary(results);
        }
        catch (Exception ex)
        {
            Error = $"Le diagnostic a échoué : {ex.Message}";
        }
    }

    private static string BuildSummary(IReadOnlyList<CheckResult> results)
    {
        int Count(CheckStatus s) => results.Count(r => r.Status == s);
        var problems = Count(CheckStatus.Problem);
        var warnings = Count(CheckStatus.Warning);
        var ok = Count(CheckStatus.Ok);
        return $"{problems} problème{(problems > 1 ? "s" : "")} à corriger, {warnings} point{(warnings > 1 ? "s" : "")} à surveiller, {ok} OK";
    }
}
