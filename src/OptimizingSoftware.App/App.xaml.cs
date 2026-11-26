using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OptimizingSoftware.App;

public partial class App : Application
{
    /// <summary>Journal des erreurs inattendues, pour pouvoir les corriger.</summary>
    public static string ErrorLog { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizingSoftware", "erreurs.log");

    private bool _smokeTest;

    protected override void OnStartup(StartupEventArgs e)
    {
        _smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
        if (_smokeTest) _ = RunSmokeTestAsync();
    }

    /// <summary>
    /// « --smoke-test » (utilisé par la CI) : ouvre la fenêtre, lance toutes les analyses d'ouverture, puis quitte
    /// avec le code 0 si tout s'est bien passé, 1 en cas d'erreur, 2 si c'est trop long.
    /// </summary>
    private async Task RunSmokeTestAsync()
    {
        var code = 0;
        try
        {
            while (MainWindow is not MainWindow { IsLoaded: true }) await Task.Delay(100);
            var window = (MainWindow)MainWindow;
            await Task.Delay(500); // laisse le temps au Loaded de lancer les analyses
            var done = await Task.WhenAny(window.InitialScans, Task.Delay(TimeSpan.FromMinutes(5)));
            if (done != window.InitialScans)
            {
                Report("Analyses d'ouverture trop longues.");
                code = 2;
            }
            else
            {
                await window.InitialScans;
                foreach (var error in window.ScanErrors())
                {
                    Report(error);
                    code = 1;
                }
            }
        }
        catch (Exception ex)
        {
            Report(ex);
            code = 1;
        }
        Shutdown(code);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        if (_smokeTest)
        {
            Report(e.Exception);
            e.Handled = true;
            Shutdown(1);
            return;
        }
        // Une erreur inattendue ne doit pas fermer l'appli au milieu d'un nettoyage : on prévient et on continue.
        e.Handled = true;
        ConfirmDialog.Inform(MainWindow, $"Une erreur inattendue s'est produite\n\n{e.Exception.Message}\n\nElle a été notée dans {ErrorLog}.");
    }

    /// <summary>En test de démarrage : écrit le message dans le journal (une appli fenêtrée n'a pas de console).</summary>
    private static void Report(object message)
    {
        Console.Error.WriteLine(message);
        Log(message.ToString() ?? "");
    }

    private static void Log(Exception ex) => Log(ex.ToString());

    private static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLog)!);
            File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n");
        }
        catch (Exception)
        {
            // Impossible d'écrire le journal : rien de plus à faire.
        }
    }
}
