using System.Diagnostics;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Apps;

namespace OptimizingSoftware.Windows;

[SupportedOSPlatform("windows")]
public static class AppUninstaller
{
    /// <summary>
    /// Lance le programme de désinstallation de l'application (avec sa propre fenêtre et, si besoin, la
    /// demande de droits de Windows) et attend qu'il se termine. Faux s'il n'a pas pu être lancé.
    /// </summary>
    public static async Task<bool> RunAsync(InstalledApp app, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(app.UninstallCommand)) return false;
        var (exe, args) = CommandLine.Split(app.UninstallCommand);
        if (exe is null) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            if (p is null) return true; // Lancé via le shell sans processus suivi.
            await p.WaitForExitAsync(ct);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // Introuvable, ou demande de droits refusée.
        }
    }

    /// <summary>Dossiers où les applications s'installent ou rangent leurs données.</summary>
    public static (IReadOnlyList<string> All, IReadOnlySet<string> ProgramFolders) LeftoverParents()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(local, "Programs"),
        }.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var data = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            local,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
        };
        // Local\Programs, par exemple, n'existe que si une appli s'y est déjà installée.
        return ([.. programs, .. data.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))],
            programs.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
