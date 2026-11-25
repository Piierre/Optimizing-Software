using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace OptimizingSoftware.Windows;

[SupportedOSPlatform("windows")]
public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Relance l'application avec la fenêtre de confirmation de Windows (UAC). Faux si l'utilisateur refuse.</summary>
    public static bool RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // Refusé dans la fenêtre UAC.
        }
    }
}
