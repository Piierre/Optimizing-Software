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

    private readonly IRecycleBin _recycleBin;
    private readonly Func<bool> _restartAsAdmin;

    /// <param name="confirm">Affiche une question oui/non à l'utilisateur.</param>
    /// <param name="restartAsAdmin">Relance l'appli en administrateur ; faux si l'utilisateur refuse.</param>
    public CleanupViewModel(IReadOnlyList<CleanupRule> rules, Quarantine quarantine, Func<string, bool> confirm,
        IRecycleBin recycleBin, bool isAdministrator, Func<bool> restartAsAdmin)
    {
        _rules = rules;
        _quarantine = quarantine;
        _confirm = confirm;
        _recycleBin = recycleBin;
        _restartAsAdmin = restartAsAdmin;
        IsAdministrator = isAdministrator;
    }

    public bool IsAdministrator { get; }
    public bool IsNotAdministrator => !IsAdministrator;

    [ObservableProperty]
    private string _recycleBinText = "";

    [ObservableProperty]
    private bool _recycleBinHasItems;

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (_restartAsAdmin())
            System.Windows.Application.Current.Shutdown();
        else
            Status = "Relance annulée.";
    }

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        var (bytes, items) = _recycleBin.Query();
        if (items == 0) return;
        if (!_confirm($"Vider la corbeille de Windows ({items.ToString("N0", Fr)} éléments, {Sizes.Format(bytes)}) ?\n\n" +
                      "Attention : contrairement au reste du nettoyage, ces fichiers ne pourront pas être restaurés."))
            return;
        await Task.Run(_recycleBin.Empty);
        RefreshRecycleBin();
        Status = $"Corbeille de Windows vidée : {Sizes.Format(bytes)} libérés.";
    }

    private void RefreshRecycleBin()
    {
        var (bytes, items) = _recycleBin.Query();
        RecycleBinHasItems = items > 0;
        RecycleBinText = items == 0
            ? "La corbeille de Windows est vide."
            : $"La corbeille de Windows contient {items.ToString("N0", Fr)} éléments ({Sizes.Format(bytes)}).";
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
            var freed = await Task.Run(() => _quarantine.PurgeExpired(DateTimeOffset.Now));
            RefreshSessions();
            RefreshRecycleBin();
            await ScanAsync();
            if (freed > 0)
                Status = $"{Sizes.Format(freed)} de nettoyages de plus de 7 jours ont été supprimés définitivement. " + Status;
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
        await Task.Run(() => _quarantine.DeletePermanently(session.Id));
        RefreshSessions();
        Status = $"{session.Size} libérés définitivement.";
    }

    [RelayCommand]
    private async Task EmptyAsync()
    {
        var sessions = _quarantine.List();
        if (sessions.Count == 0) return;
        var total = Sizes.Format(sessions.Sum(s => s.TotalBytes));
        if (!_confirm($"Vider la corbeille d'Optimizing Software et libérer {total} tout de suite ?\n\nCette action ne peut pas être annulée.")) return;
        await Task.Run(() =>
        {
            foreach (var s in sessions) _quarantine.DeletePermanently(s.Id);
        });
        RefreshSessions();
        Status = $"{total} libérés définitivement.";
    }

    /// <summary>Appelé quand un autre onglet a déplacé des fichiers dans la corbeille interne.</summary>
    public void RefreshSessions()
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
