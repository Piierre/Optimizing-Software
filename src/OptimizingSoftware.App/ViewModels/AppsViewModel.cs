using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Apps;
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>
/// Onglet « Applications » : liste des applications installées (avec désinstallation), dossiers laissés par des
/// applications disparues, et entrées cassées de la liste des programmes. Tout ce qui est retiré passe par la
/// corbeille interne (fichiers déplacés, clés du registre sauvegardées en .reg).
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly Func<IReadOnlyList<InstalledApp>> _readApps;
    private readonly Func<(IReadOnlyList<string> All, IReadOnlySet<string> ProgramFolders)> _leftoverParents;
    private readonly Func<InstalledApp, Task<bool>> _uninstall;
    private readonly Quarantine _quarantine;
    private readonly Func<string, bool> _confirm;
    private readonly Func<bool> _restartAsAdmin;
    private readonly Action _onCleaned;
    private readonly Action<string> _reveal;

    public AppsViewModel(
        Func<IReadOnlyList<InstalledApp>> readApps,
        Func<(IReadOnlyList<string> All, IReadOnlySet<string> ProgramFolders)> leftoverParents,
        Func<InstalledApp, Task<bool>> uninstall,
        Quarantine quarantine, Func<string, bool> confirm, bool isAdministrator, Func<bool> restartAsAdmin,
        Action onCleaned, Action<string> reveal)
    {
        _readApps = readApps;
        _leftoverParents = leftoverParents;
        _uninstall = uninstall;
        _quarantine = quarantine;
        _confirm = confirm;
        IsAdministrator = isAdministrator;
        _restartAsAdmin = restartAsAdmin;
        _onCleaned = onCleaned;
        _reveal = reveal;
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = o => o is AppItem a && a.MatchesSearch(Search)
                                         && (Category == 0 || (Category == 1) == a.App.IsGame);
        AppsView.SortDescriptions.Add(new SortDescription(nameof(AppItem.SortSize), ListSortDirection.Descending));
    }

    public bool IsAdministrator { get; }
    public bool IsNotAdministrator => !IsAdministrator;

    public ObservableCollection<AppItem> Apps { get; } = [];
    public ICollectionView AppsView { get; }
    public ObservableCollection<LeftoverItem> Leftovers { get; } = [];
    public ObservableCollection<BrokenItem> Broken { get; } = [];

    /// <summary>0 = applications, 1 = dossiers restants, 2 = entrées cassées.</summary>
    [ObservableProperty]
    private int _section;

    [ObservableProperty]
    private string _search = "";

    partial void OnSearchChanged(string value) => AppsView.Refresh();

    /// <summary>0 = tout, 1 = jeux, 2 = logiciels.</summary>
    [ObservableProperty]
    private int _category;

    partial void OnCategoryChanged(int value) => AppsView.Refresh();

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string _appsSummary = "";

    [ObservableProperty]
    private string _leftoversTotal = "";

    [ObservableProperty]
    private string _brokenTotal = "";

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (_restartAsAdmin())
            System.Windows.Application.Current.Shutdown();
        else
            Status = "Relance annulée.";
    }

    [RelayCommand]
    private Task ScanAsync() => RunScanAsync(_lastUninstalled);

    /// <summary>
    /// Dernière application désinstallée. Beaucoup de désinstallateurs se relancent en arrière-plan et rendent
    /// la main avant d'avoir fini : on garde l'info pour que l'analyse suivante retrouve quand même ses restes.
    /// </summary>
    private InstalledApp? _lastUninstalled;

    private async Task RunScanAsync(InstalledApp? justUninstalled)
    {
        IsBusy = true;
        Status = "Analyse des applications et de leurs dossiers…";
        try
        {
            var (apps, leftovers) = await Task.Run(() =>
            {
                var apps = _readApps();
                var (parents, programs) = _leftoverParents();
                return (apps, LeftoverFinder.Find(apps, parents, programs, DateTime.UtcNow, justUninstalled: justUninstalled));
            });

            Apps.Clear();
            foreach (var a in apps.Where(a => !a.IsBroken)) Apps.Add(new AppItem(a));
            var games = Apps.Where(a => a.App.IsGame).ToList();
            var software = Apps.Where(a => !a.App.IsGame).ToList();
            AppsSummary = $"{games.Count} jeux ({Sizes.Format(games.Sum(a => a.App.SizeBytes ?? 0))}) · " +
                          $"{software.Count} logiciels ({Sizes.Format(software.Sum(a => a.App.SizeBytes ?? 0))})";

            foreach (var l in Leftovers) l.PropertyChanged -= OnLeftoverChanged;
            Leftovers.Clear();
            foreach (var l in leftovers)
            {
                var item = new LeftoverItem(l);
                item.PropertyChanged += OnLeftoverChanged;
                Leftovers.Add(item);
            }
            UpdateLeftoversTotal();

            foreach (var b in Broken) b.PropertyChanged -= OnBrokenChanged;
            Broken.Clear();
            foreach (var a in apps.Where(a => a.IsBroken))
            {
                var item = new BrokenItem(a);
                item.PropertyChanged += OnBrokenChanged;
                Broken.Add(item);
            }
            UpdateBrokenTotal();

            HasScanned = true;
            Status = $"{games.Count} jeux et {software.Count} logiciels, {Leftovers.Count} dossiers restants, {Broken.Count} entrées cassées.";
        }
        catch (Exception ex)
        {
            Status = $"L'analyse a échoué : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(AppItem item)
    {
        var app = item.App;
        if (!item.CanUninstall)
        {
            Status = $"{app.Name} se désinstalle depuis {app.Source ?? "son launcher"}.";
            return;
        }
        if (!_confirm($"Désinstaller {app.Name} ?\n\nLe programme de désinstallation de l'application va s'ouvrir : suis ses étapes. " +
                      "Ensuite, Optimizing Software cherchera les dossiers qu'il a laissés."))
            return;

        IsBusy = true;
        Status = $"Désinstallation de {app.Name} en cours… (termine-la dans sa fenêtre)";
        bool launched;
        try
        {
            launched = await _uninstall(app);
        }
        finally
        {
            IsBusy = false;
        }
        if (!launched)
        {
            Status = $"Le programme de désinstallation de {app.Name} n'a pas pu être lancé (introuvable ou droits refusés).";
            return;
        }

        _lastUninstalled = app;
        await RunScanAsync(justUninstalled: app);
        var stillThere = Apps.Any(a => string.Equals(a.App.RegistryKey, app.RegistryKey, StringComparison.OrdinalIgnoreCase));
        if (stillThere)
        {
            Status = $"{app.Name} est toujours installé (désinstallation annulée ou pas encore terminée). Clique sur « Analyser » quand elle est finie.";
            return;
        }
        var fromIt = Leftovers.Count(l => l.Leftover.Reason.Contains(app.Name, StringComparison.Ordinal));
        Status = fromIt == 0
            ? $"{app.Name} est désinstallé, et il n'a laissé aucun dossier."
            : $"{app.Name} est désinstallé. Il a laissé {fromIt} dossier(s), cochés dans « Dossiers restants ».";
        if (fromIt > 0) Section = 1;
    }

    [RelayCommand]
    private void Reveal(string path) => _reveal(path);

    [RelayCommand]
    private async Task CleanLeftoversAsync()
    {
        var selected = Leftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "Coche au moins un dossier.";
            return;
        }
        var bytes = selected.Sum(l => l.Leftover.SizeBytes);
        var preview = string.Join("\n", selected.Take(8).Select(l => "•  " + l.Path))
                      + (selected.Count > 8 ? $"\n… et {selected.Count - 8} autre(s)" : "");
        if (!_confirm($"Déplacer {selected.Count} dossiers ({Sizes.Format(bytes)}) vers la corbeille d'Optimizing Software ?\n\n{preview}\n\n" +
                      "Tu pourras les restaurer pendant 7 jours depuis l'onglet Nettoyage."))
            return;

        IsBusy = true;
        try
        {
            var roots = selected.Select(l => l.Path).ToList();
            var outcome = await Task.Run(() =>
            {
                var files = roots.SelectMany(r => LeftoverFinder.FilesIn(r).Select(f => f.FullName)).ToList();
                var result = files.Count == 0
                    ? new CleanOutcome(0, 0, 0, null)
                    : _quarantine.MoveIn("Restes d'applications", files, roots, DateTimeOffset.Now);
                foreach (var r in roots) LeftoverFinder.RemoveEmptyDirectories(r);
                return result;
            });

            foreach (var l in selected.Where(l => !System.IO.Directory.Exists(l.Path)))
            {
                l.PropertyChanged -= OnLeftoverChanged;
                Leftovers.Remove(l);
            }
            UpdateLeftoversTotal();
            var remaining = selected.Count(l => System.IO.Directory.Exists(l.Path));
            Status = $"{outcome.MovedFiles.ToString("N0", Fr)} fichiers ({Sizes.Format(outcome.MovedBytes)}) déplacés dans la corbeille."
                     + (remaining == 0 ? "" : $" {remaining} dossier(s) n'ont pas pu être vidés entièrement"
                                              + (IsAdministrator ? " (fichiers en cours d'utilisation ?)." : " : relance en administrateur pour Program Files et ProgramData."));
            _onCleaned();
        }
        catch (Exception ex)
        {
            Status = $"Le nettoyage a échoué : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveBrokenAsync()
    {
        var selected = Broken.Where(b => b.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "Coche au moins une entrée.";
            return;
        }
        var preview = string.Join("\n", selected.Take(8).Select(b => "•  " + b.Name))
                      + (selected.Count > 8 ? $"\n… et {selected.Count - 8} autre(s)" : "");
        if (!_confirm($"Retirer {selected.Count} entrées cassées de la liste des programmes ?\n\n{preview}\n\n" +
                      "Chaque entrée est d'abord sauvegardée. Tu pourras la remettre pendant 7 jours depuis l'onglet Nettoyage."))
            return;

        IsBusy = true;
        try
        {
            var outcome = await Task.Run(() =>
                _quarantine.MoveRegistryIn("Entrées cassées", selected.Select(b => b.App.RegistryKey), DateTimeOffset.Now));
            await RunScanAsync(_lastUninstalled);
            Section = 2;
            Status = $"{outcome.MovedFiles} entrée(s) retirée(s) et sauvegardée(s)."
                     + (outcome.SkippedFiles == 0 ? "" : $" {outcome.SkippedFiles} n'ont pas pu l'être"
                                                        + (IsAdministrator ? "." : " : relance en administrateur pour celles de tout le PC."));
            _onCleaned();
        }
        catch (Exception ex)
        {
            Status = $"Le nettoyage a échoué : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLeftoverChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverItem.IsSelected)) UpdateLeftoversTotal();
    }

    private void OnBrokenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrokenItem.IsSelected)) UpdateBrokenTotal();
    }

    private void UpdateLeftoversTotal()
    {
        var selected = Leftovers.Where(l => l.IsSelected).ToList();
        LeftoversTotal = $"Sélection : {selected.Count} dossiers, {Sizes.Format(selected.Sum(l => l.Leftover.SizeBytes))}";
    }

    private void UpdateBrokenTotal() =>
        BrokenTotal = $"Sélection : {Broken.Count(b => b.IsSelected)} entrées";
}

public sealed class AppItem(InstalledApp app)
{
    public InstalledApp App { get; } = app;
    public string Name => App.Name;
    public string Publisher => App.Publisher ?? "";
    public string Version => App.Version ?? "";
    public string Installed => App.InstallDate?.ToString("dd/MM/yyyy") ?? "";
    public string Size => App.SizeBytes is { } s ? Sizes.Format(s) : "";
    public long SortSize => App.SizeBytes ?? -1;
    public bool CanUninstall => !string.IsNullOrWhiteSpace(App.UninstallCommand);
    public string Kind => App.IsGame ? "Jeu" : "Logiciel";
    public string Source => App.Source ?? "";
    public string Drive => App.Drive?.TrimEnd('\\') ?? "";
    public string UninstallLabel => CanUninstall ? "Désinstaller" : $"Via {App.Source ?? "launcher"}";

    public bool MatchesSearch(string search) =>
        string.IsNullOrWhiteSpace(search)
        || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Source.Contains(search, StringComparison.OrdinalIgnoreCase);
}

public sealed partial class LeftoverItem(LeftoverFolder leftover) : ObservableObject
{
    public LeftoverFolder Leftover { get; } = leftover;
    public string Path => Leftover.Path;
    public string Name => System.IO.Path.GetFileName(Leftover.Path);
    public string Parent => System.IO.Path.GetDirectoryName(Leftover.Path) ?? "";
    public string Size => Sizes.Format(Leftover.SizeBytes);
    public long SizeBytes => Leftover.SizeBytes;
    public string Modified => Leftover.LastWriteUtc.ToLocalTime().ToString("dd/MM/yyyy");
    public string Reason => Leftover.Reason;

    [ObservableProperty]
    private bool _isSelected = leftover.Suggested;
}

public sealed partial class BrokenItem(InstalledApp app) : ObservableObject
{
    public InstalledApp App { get; } = app;
    public string Name => App.Name;
    public string Publisher => App.Publisher ?? "";
    public string Where => App.RegistryKey.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
        ? "Ton compte" : "Tout le PC (administrateur)";
    public string Reason => "Le programme de désinstallation et le dossier de l'application n'existent plus.";

    [ObservableProperty]
    private bool _isSelected = true;
}
