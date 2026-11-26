using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;
using OptimizingSoftware.Core.Apps;

namespace OptimizingSoftware.Windows;

/// <summary>Lit la liste « Applications et fonctionnalités » dans le registre (programmes classiques).</summary>
[SupportedOSPlatform("windows")]
public static class InstalledAppsReader
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<InstalledApp> Read()
    {
        var apps = new List<InstalledApp>();
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, @"HKEY_LOCAL_MACHINE\" + UninstallPath, apps);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", apps);
        Read(RegistryHive.CurrentUser, RegistryView.Default, @"HKEY_CURRENT_USER\" + UninstallPath, apps);

        // Une même appli peut être déclarée deux fois (32 et 64 bits) : on garde la plus complète.
        return apps
            .GroupBy(a => (NameMatcher.Normalize(a.Name), a.Version))
            .Select(g => g.OrderByDescending(a => a.SizeBytes ?? 0).First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, string displayPath, List<InstalledApp> apps)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(UninstallPath);
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null) continue;
                    if (ToApp(key, $@"{displayPath}\{name}") is { } app) apps.Add(app);
                }
                catch (Exception)
                {
                    // Clé illisible : on passe à la suivante.
                }
            }
        }
        catch (Exception)
        {
            // Branche du registre inaccessible.
        }
    }

    private static InstalledApp? ToApp(RegistryKey key, string fullKey)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        // Composants système, mises à jour et correctifs : pas des applications à proprement parler.
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (key.GetValue("ParentKeyName") is string) return null;
        if (key.GetValue("ReleaseType") is string rt && (rt.Contains("Update") || rt.Contains("Hotfix"))) return null;

        var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
        if (string.IsNullOrEmpty(location)) location = null;
        var uninstall = (key.GetValue("UninstallString") as string)?.Trim();
        long? size = key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null;
        DateOnly? date = DateOnly.TryParseExact(key.GetValue("InstallDate") as string, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        return new InstalledApp(name, (key.GetValue("Publisher") as string)?.Trim(), key.GetValue("DisplayVersion") as string,
            date, size, location, uninstall, fullKey, IsBroken(location, uninstall));
    }

    /// <summary>
    /// Entrée cassée : le programme de désinstallation n'existe plus et le dossier d'installation non plus.
    /// La désinstaller est impossible, seule l'entrée reste dans la liste.
    /// </summary>
    internal static bool IsBroken(string? location, string? uninstallCommand)
    {
        if (uninstallCommand is null) return location is not null && !Directory.Exists(location);
        var (exe, _) = CommandLine.Split(uninstallCommand);
        if (exe is null || !Path.IsPathRooted(exe)) return false; // msiexec, rundll32… : on ne peut pas conclure
        return !File.Exists(exe) && (location is null || !Directory.Exists(location));
    }
}

public static class CommandLine
{
    /// <summary>« "C:\X\unins000.exe" /SILENT » → (C:\X\unins000.exe, /SILENT). Gère aussi les chemins sans guillemets.</summary>
    public static (string? Exe, string Args) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end < 0 ? (command.Trim('"'), "") : (command[1..end], command[(end + 1)..].Trim());
        }
        var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0) return (command[..(exeEnd + 4)], command[(exeEnd + 4)..].Trim());
        var space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }
}
