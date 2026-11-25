using System.Diagnostics;
using System.Windows;
using OptimizingSoftware.App.ViewModels;
using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Diagnostics;
using OptimizingSoftware.Windows;

namespace OptimizingSoftware.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var isAdmin = Elevation.IsAdministrator();
        if (isAdmin) Title += " (administrateur)";
        var quarantine = new Quarantine(WindowsCleanupRules.QuarantineRoot, registry: new WindowsRegistryStore());

        var cleanup = new CleanupViewModel(
            WindowsCleanupRules.For(isAdmin), quarantine, Confirm,
            new WindowsRecycleBin(), isAdmin, Elevation.RestartAsAdministrator);
        var files = new FilesViewModel(
            quarantine, Confirm,
            () => KnownFolders.Downloads,
            KnownFolders.PersonalFolders,
            KnownFolders.PersonalFoldersAndDataDrives,
            onCleaned: cleanup.RefreshSessions,
            reveal: path => Process.Start("explorer.exe", $"/select,\"{path}\""));
        var apps = new AppsViewModel(
            InstalledAppsReader.Read, AppUninstaller.LeftoverParents, app => AppUninstaller.RunAsync(app),
            quarantine, Confirm, isAdmin, Elevation.RestartAsAdministrator,
            onCleaned: cleanup.RefreshSessions,
            reveal: path => Process.Start("explorer.exe", $"\"{path}\""));
        var diagnostic = new MainViewModel(new WindowsSnapshotProvider(), new DiagnosticEngine());
        var recording = new RecordingViewModel(() => diagnostic.LastSnapshot);

        CleanupTab.DataContext = cleanup;
        FilesTab.DataContext = files;
        AppsTab.DataContext = apps;
        DiagnosticTab.DataContext = diagnostic;
        RecordingTab.DataContext = recording;
        Closed += (_, _) => recording.Dispose();

        // Analyse et diagnostic sont en lecture seule : on peut les lancer dès l'ouverture.
        Loaded += async (_, _) =>
        {
            diagnostic.RunDiagnosticCommand.Execute(null);
            await cleanup.InitializeAsync();
        };
    }

    private bool Confirm(string question) =>
        MessageBox.Show(this, question, "Optimizing Software", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
