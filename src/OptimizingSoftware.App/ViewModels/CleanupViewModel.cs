using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>
/// Onglet « Nettoyage » : analyse ce qui peut être nettoyé, demande confirmation, déplace dans la
/// corbeille interne et permet de restaurer pendant 7 jours.
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IReadOnlyList<CleanupRule> _rules;
    private readonly Quarantine _quarantine;
    private readonly Func<string, bool> _confirm;

    /// <param name="confirm">Affiche une question oui/non à l'utilisateur.</param>
    public CleanupViewModel(IReadOnlyList<CleanupRule> rules, Quarantine quarantine, Func<string, bool> confirm)
    {
        _rules = rules;
        _quarantine = quarantine;
        _confirm = confirm;
    }

    public ObservableCollection<CategoryItem> Categories { get; } = [];
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string _selectedTotal = "";

    [ObservableProperty]
    private string _quarantineTotal = "";

    [ObservableProperty]
    private bool _hasSessions;

    /// <summary>À l'ouverture : purge les nettoyages de plus de 7 jours, puis analyse.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var purged = await Task.Run(() => _quarantine.PurgeExpired(DateTimeOffset.Now));
            RefreshSessions();
            await ScanAsync();
            if (purged.FreedBytes > 0)
                Status = $"{Sizes.Format(purged.FreedBytes)} de nettoyages de plus de 7 jours ont été supprimés définitivement. " + Status;
        }
        catch (Exception ex)
        {
            Status = $"L'analyse a échoué : {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        Status = "Analyse en cours…";
        var scans = await Task.Run(() => CleanupScanner.Scan(_rules, DateTime.UtcNow));
        foreach (var c in Categories) c.PropertyChanged -= OnCategoryChanged;
        Categories.Clear();
        foreach (var scan in scans.OrderByDescending(s => s.TotalBytes))
        {
            var item = new CategoryItem(scan);
            item.PropertyChanged += OnCategoryChanged;
            Categories.Add(item);
        }
        UpdateTotal();
        var total = scans.Sum(s => s.TotalBytes);
        Status = total == 0 ? "Rien à nettoyer pour l'instant." : $"{Sizes.Format(total)} peuvent être nettoyés.";
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var selected = Categories.Where(c => c.IsSelected && c.Scan.Files.Count > 0).ToList();
        if (selected.Count == 0)
        {
            Status = "Coche au moins une catégorie qui contient des fichiers.";
            return;
        }

        var files = selected.SelectMany(c => c.Scan.Files).ToList();
        var bytes = files.Sum(f => f.SizeBytes);
        var names = string.Join(", ", selected.Select(c => c.Name));
        var question =
            $"Déplacer {files.Count.ToString("N0", Fr)} fichiers ({Sizes.Format(bytes)}) vers la corbeille d'Optimizing Software ?\n\n" +
            $"{names}\n\n" +
            $"Tu pourras tout restaurer pendant 7 jours. Ensuite, ils seront supprimés définitivement.";
        if (!_confirm(question)) return;

        Status = "Nettoyage en cours…";
        var label = selected.Count <= 3 ? names : $"{string.Join(", ", selected.Take(3).Select(c => c.Name))} et {selected.Count - 3} autre(s)";
        var roots = selected.SelectMany(c => c.Scan.Roots).Distinct().ToList();
        var outcome = await Task.Run(() =>
            _quarantine.MoveIn(label, files.Select(f => f.Path), roots, DateTimeOffset.Now));

        RefreshSessions();
        await ScanAsync();
        Status = $"{outcome.MovedFiles.ToString("N0", Fr)} fichiers ({Sizes.Format(outcome.MovedBytes)}) déplacés dans la corbeille."
                 + (outcome.SkippedFiles > 0 ? $" {outcome.SkippedFiles.ToString("N0", Fr)} fichiers en cours d'utilisation ont été laissés en place." : "");
    }

    [RelayCommand]
    private async Task RestoreAsync(SessionItem session)
    {
        if (!_confirm($"Remettre en place les {session.FileCount} fichiers du nettoyage « {session.Label} » ?")) return;
        var outcome = await Task.Run(() => _quarantine.Restore(session.Id));
        RefreshSessions();
        await ScanAsync();
        Status = $"{outcome.RestoredFiles.ToString("N0", Fr)} fichiers restaurés."
                 + (outcome.SkippedFiles > 0 ? $" {outcome.SkippedFiles} n'ont pas pu l'être (un fichier du même nom existe déjà) et restent dans la corbeille." : "");
    }

    [RelayCommand]
    private async Task DeleteAsync(SessionItem session)
    {
        if (!_confirm($"Supprimer définitivement le nettoyage « {session.Label} » ({session.Size}) ?\n\nCette action ne peut pas être annulée.")) return;
        var outcome = await DeleteSafelyAsync([session.Id]);
        Status = Describe(outcome);
    }

    [RelayCommand]
    private async Task EmptyAsync()
    {
        var sessions = _quarantine.List();
        if (sessions.Count == 0) return;
        var total = Sizes.Format(sessions.Sum(s => s.TotalBytes));
        if (!_confirm($"Vider la corbeille d'Optimizing Software et libérer {total} tout de suite ?\n\nCette action ne peut pas être annulée.")) return;
        var outcome = await DeleteSafelyAsync(sessions.Select(s => s.Id).ToList());
        Status = Describe(outcome);
    }

    /// <summary>Supprime sans jamais faire planter l'appli, puis rafraîchit la corbeille.</summary>
    private async Task<DeleteOutcome> DeleteSafelyAsync(IReadOnlyList<string> sessionIds)
    {
        var outcome = await Task.Run(() =>
        {
            var total = new DeleteOutcome(0, 0, 0);
            foreach (var id in sessionIds)
            {
                try
                {
                    total += _quarantine.DeletePermanently(id);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    total += new DeleteOutcome(0, 0, 1);
                }
            }
            return total;
        });
        RefreshSessions();
        return outcome;
    }

    private static string Describe(DeleteOutcome o) =>
        $"{Sizes.Format(o.FreedBytes)} libérés définitivement."
        + (o.LockedFiles > 0
            ? $" {o.LockedFiles.ToString("N0", Fr)} fichier(s) encore utilisés par une application ouverte sont restés dans la corbeille : ferme l'application concernée ou redémarre le PC, puis réessaie."
            : "");

    private void RefreshSessions()
    {
        var sessions = _quarantine.List();
        Sessions.Clear();
        foreach (var s in sessions) Sessions.Add(new SessionItem(s));
        HasSessions = sessions.Count > 0;
        QuarantineTotal = HasSessions
            ? $"{Sizes.Format(sessions.Sum(s => s.TotalBytes))} en attente. Ils seront libérés définitivement à la date indiquée, ou tout de suite si tu vides la corbeille."
            : "La corbeille est vide.";
    }

    private void OnCategoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryItem.IsSelected)) UpdateTotal();
    }

    private void UpdateTotal()
    {
        var bytes = Categories.Where(c => c.IsSelected).Sum(c => c.Scan.TotalBytes);
        SelectedTotal = $"Sélection : {Sizes.Format(bytes)}";
    }
}

public sealed partial class CategoryItem(CategoryScan scan) : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public CategoryScan Scan { get; } = scan;
    public string Name => Scan.Rule.Name;
    public string Description => Scan.Rule.Description;
    public string? Note => Scan.Rule.Note;
    public bool HasNote => Note is not null;
    public string Size => Scan.Files.Count == 0 ? "Vide" : Sizes.Format(Scan.TotalBytes);
    public string Detail => Scan.Files.Count == 0
        ? (Scan.Roots.Count == 0 ? "Non présent sur ce PC" : "Rien à nettoyer")
        : $"{Scan.Files.Count.ToString("N0", Fr)} fichiers";

    [ObservableProperty]
    private bool _isSelected = scan.Rule.SelectedByDefault && scan.Files.Count > 0;
}

public sealed class SessionItem(QuarantineSession session)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public string Id => session.Id;
    public string Label => session.Label;
    public int FileCount => session.Entries.Count;
    public string Size => Sizes.Format(session.TotalBytes);
    public string When => session.CreatedAt.ToLocalTime().ToString("d MMMM 'à' HH:mm", Fr);
    public string Expires => "Suppression définitive le " +
                             (session.CreatedAt + Quarantine.RetentionPeriod).ToLocalTime().ToString("d MMMM", Fr);
    public string Summary => $"{FileCount.ToString("N0", Fr)} fichiers · {Size} · nettoyé le {When}";
}
