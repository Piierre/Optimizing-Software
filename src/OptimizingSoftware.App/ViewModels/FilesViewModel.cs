using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Files;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>
/// Onglet « Fichiers » : vieux téléchargements, gros fichiers et doublons. Ce sont des fichiers personnels :
/// l'utilisateur choisit fichier par fichier, et tout passe par la corbeille interne.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly Quarantine _quarantine;
    private readonly Func<string, bool> _confirm;
    private readonly Func<string> _downloads;
    private readonly Func<IReadOnlyList<string>> _personalFolders;
    private readonly Func<IReadOnlyList<string>> _allDataFolders;
    private readonly Action _onCleaned;
    private readonly Action<string> _reveal;

    private IReadOnlyList<string> _roots = [];
    private string _label = "";

    public FilesViewModel(Quarantine quarantine, Func<string, bool> confirm, Func<string> downloads,
        Func<IReadOnlyList<string>> personalFolders, Func<IReadOnlyList<string>> allDataFolders,
        Action onCleaned, Action<string> reveal, Func<IReadOnlyList<DriveChoice>> drives)
    {
        _quarantine = quarantine;
        _confirm = confirm;
        _downloads = downloads;
        _personalFolders = personalFolders;
        _allDataFolders = allDataFolders;
        _onCleaned = onCleaned;
        _reveal = reveal;
        View = CollectionViewSource.GetDefaultView(Files);
        foreach (var d in drives()) Drives.Add(new DriveItem(d));
    }

    /// <summary>Disques à analyser (tous cochés par défaut).</summary>
    public ObservableCollection<DriveItem> Drives { get; } = [];

    /// <summary>Ne garde que les dossiers des disques cochés.</summary>
    private IReadOnlyList<string> OnSelectedDrives(IEnumerable<string> folders) =>
        Drives.Count == 0 ? folders.ToList() : DriveFilter.Keep(folders, Drives.Where(d => d.IsSelected).Select(d => d.Root));

    public ObservableCollection<FileItem> Files { get; } = [];

    /// <summary>Vue de la liste, regroupée par doublons quand il le faut.</summary>
    public ICollectionView View { get; }

    [ObservableProperty]
    private string _title = "Choisis une analyse";

    [ObservableProperty]
    private string _explanation =
        "Ces analyses portent sur tes fichiers personnels : rien n'est coché sans raison claire, et tout passe par la corbeille.";

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string _selectedTotal = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [RelayCommand]
    private Task ScanDownloadsAsync(CancellationToken ct) => RunScanAsync(
        "Vieux téléchargements",
        "Fichiers du dossier Téléchargements non modifiés depuis plus de 3 mois. Les installateurs et images disque sont cochés : ils se retéléchargent.",
        () =>
        {
            var d = _downloads();
            return OnSelectedDrives([d]).Count == 0
                ? ([], [])
                : ([d], OldDownloadsFinder.Find(d, DateTime.UtcNow, ct: ct));
        },
        grouped: false);

    [RelayCommand]
    private Task ScanLargeAsync(CancellationToken ct) => RunScanAsync(
        "Gros fichiers",
        "Fichiers de plus de 500 Mo dans tes dossiers personnels et tes autres disques. Les jeux installés sont exclus. Rien n'est coché : fais le tri.",
        () => { var roots = OnSelectedDrives(_allDataFolders()); return (roots, LargeFileFinder.Find(roots, ct: ct)); },
        grouped: false);

    [RelayCommand]
    private Task ScanDuplicatesAsync(CancellationToken ct) => RunScanAsync(
        "Doublons",
        "Fichiers de plus de 1 Mo au contenu strictement identique. Dans chaque groupe, un exemplaire est gardé (de préférence l'original, hors Téléchargements) et les copies sont cochées.",
        () =>
        {
            var roots = OnSelectedDrives(_allDataFolders());
            var groups = DuplicateFinder.Find(roots, ct: ct);
            return (roots, groups.SelectMany(g => g.Files).ToList());
        },
        grouped: true);

    private async Task RunScanAsync(string title, string explanation,
        Func<(IReadOnlyList<string> Roots, IReadOnlyList<FileCandidate> Files)> scan, bool grouped)
    {
        if (Drives.Count > 0 && !Drives.Any(d => d.IsSelected))
        {
            Status = "Coche au moins un disque.";
            return;
        }
        Title = title;
        Explanation = explanation;
        _label = title;
        Status = "Analyse en cours… (ça peut prendre quelques minutes sur un disque dur)";
        IsBusy = true;
        ClearFiles();
        try
        {
            var (roots, found) = await Task.Run(scan);
            _roots = roots;
            // En-tête de chaque groupe de doublons : taille et nom de l'exemplaire gardé (le premier du groupe).
            var groupLabels = found.Where(c => c.GroupKey is not null)
                .GroupBy(c => c.GroupKey!)
                .ToDictionary(g => g.Key, g => $"{g.Count()} exemplaires de {System.IO.Path.GetFileName(g.First().Path)} ({Sizes.Format(g.First().SizeBytes)} chacun)");
            foreach (var c in found)
            {
                var item = new FileItem(c) { GroupLabel = c.GroupKey is null ? null : groupLabels[c.GroupKey] };
                item.PropertyChanged += OnItemChanged;
                Files.Add(item);
            }
            View.GroupDescriptions.Clear();
            if (grouped) View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileItem.GroupLabel)));
            UpdateTotal();
            Status = found.Count == 0 ? "Rien trouvé." : $"{found.Count.ToString("N0", Fr)} fichiers trouvés.";
        }
        catch (OperationCanceledException)
        {
            Status = "Analyse annulée.";
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
    private void CancelScan()
    {
        ScanDownloadsCommand.Cancel();
        ScanLargeCommand.Cancel();
        ScanDuplicatesCommand.Cancel();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var f in Files) f.IsSelected = false;
    }

    [RelayCommand]
    private void Reveal(FileItem item) => _reveal(item.Path);

    [RelayCommand]
    private async Task CleanAsync()
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "Coche au moins un fichier.";
            return;
        }

        // Doublons : on refuse de retirer tous les exemplaires d'un même fichier.
        var wholeGroups = Files.Where(f => f.Candidate.GroupKey is not null)
            .GroupBy(f => f.Candidate.GroupKey)
            .Where(g => g.All(f => f.IsSelected))
            .Select(g => g.First().Name)
            .ToList();
        if (wholeGroups.Count > 0)
        {
            Status = $"Garde au moins un exemplaire de : {string.Join(", ", wholeGroups.Take(3))}.";
            return;
        }

        var bytes = selected.Sum(f => f.Candidate.SizeBytes);
        var preview = string.Join("\n", selected.Take(8).Select(f => "•  " + f.Name))
                      + (selected.Count > 8 ? $"\n… et {selected.Count - 8} autre(s)" : "");
        if (!_confirm($"Déplacer {selected.Count.ToString("N0", Fr)} fichiers ({Sizes.Format(bytes)}) vers la corbeille d'Optimizing Software ?\n\n{preview}\n\nTu pourras les restaurer pendant 7 jours depuis l'onglet Nettoyage."))
            return;

        IsBusy = true;
        try
        {
            var outcome = await Task.Run(() =>
                _quarantine.MoveIn(_label, selected.Select(f => f.Path), _roots, DateTimeOffset.Now));
            foreach (var f in selected.Where(f => !System.IO.File.Exists(f.Path)))
            {
                f.PropertyChanged -= OnItemChanged;
                Files.Remove(f);
            }
            UpdateTotal();
            Status = $"{outcome.MovedFiles.ToString("N0", Fr)} fichiers ({Sizes.Format(outcome.MovedBytes)}) déplacés dans la corbeille."
                     + (outcome.SkippedFiles > 0 ? $" {outcome.SkippedFiles} n'ont pas pu être déplacés (ouverts dans une application ?)." : "");
            _onCleaned();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearFiles()
    {
        foreach (var f in Files) f.PropertyChanged -= OnItemChanged;
        Files.Clear();
        UpdateTotal();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsSelected)) UpdateTotal();
    }

    private void UpdateTotal()
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        SelectedTotal = $"Sélection : {selected.Count.ToString("N0", Fr)} fichiers, {Sizes.Format(selected.Sum(f => f.Candidate.SizeBytes))}";
    }
}

public sealed partial class DriveItem(DriveChoice drive) : ObservableObject
{
    public string Root => drive.Root;
    public string Label =>
        $"{drive.Root.TrimEnd('\\')} {drive.Label ?? (drive.IsSystem ? "Windows" : "Disque")} ({Sizes.Format(drive.FreeBytes)} libres sur {Sizes.Format(drive.TotalBytes)})";

    [ObservableProperty]
    private bool _isSelected = true;
}

public sealed partial class FileItem(FileCandidate candidate) : ObservableObject
{
    public FileCandidate Candidate { get; } = candidate;
    public string Path => Candidate.Path;
    public string Name => System.IO.Path.GetFileName(Candidate.Path);
    public string Folder => System.IO.Path.GetDirectoryName(Candidate.Path) ?? "";
    public string Size => Sizes.Format(Candidate.SizeBytes);
    public long SizeBytes => Candidate.SizeBytes;
    public string Modified => Candidate.LastWriteUtc.ToLocalTime().ToString("dd/MM/yyyy");
    public string Reason => Candidate.Reason;

    /// <summary>En-tête de groupe pour les doublons.</summary>
    public string? GroupLabel { get; init; }

    [ObservableProperty]
    private bool _isSelected = candidate.Suggested;
}
