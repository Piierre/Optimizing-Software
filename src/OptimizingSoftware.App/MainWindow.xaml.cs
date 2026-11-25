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

        var cleanup = new CleanupViewModel(
            WindowsCleanupRules.Default(),
            new Quarantine(WindowsCleanupRules.QuarantineRoot),
            Confirm);
        var diagnostic = new MainViewModel(new WindowsSnapshotProvider(), new DiagnosticEngine());
        var recording = new RecordingViewModel(() => diagnostic.LastSnapshot);

        CleanupTab.DataContext = cleanup;
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
