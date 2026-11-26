using System.Runtime.Versioning;
using Microsoft.Win32;
using OptimizingSoftware.Core.Startup;

namespace OptimizingSoftware.Windows;

/// <summary>
/// Programmes lancés à l'ouverture de session (clés Run et dossiers Démarrage). L'activation et la désactivation
/// passent par les mêmes valeurs que le Gestionnaire des tâches : l'entrée elle-même n'est jamais supprimée,
/// et ce qu'on désactive ici apparaît aussi désactivé dans le Gestionnaire des tâches.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupPrograms
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static IReadOnlyList<StartupEntry> Read()
    {
        var entries = new List<StartupEntry>();
        ReadRun(RegistryHive.CurrentUser, RegistryView.Default, StartupLocation.RunUser, entries);
        ReadRun(RegistryHive.LocalMachine, RegistryView.Registry64, StartupLocation.RunMachine, entries);
        ReadRun(RegistryHive.LocalMachine, RegistryView.Registry32, StartupLocation.RunMachine32, entries);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.FolderUser, entries);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.FolderCommon, entries);
        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Active ou désactive. Faux si c'est refusé (entrée pour tout le PC sans droits administrateur).</summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        var (hive, sub, valueName) = Approved(entry);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.CreateSubKey(sub, writable: true);
            key.SetValue(valueName, enabled ? StartupApproved.Enabled() : StartupApproved.Disabled(DateTime.UtcNow), RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static (RegistryHive Hive, string SubKey, string ValueName) Approved(StartupEntry e) => e.Location switch
    {
        StartupLocation.RunUser => (RegistryHive.CurrentUser, ApprovedPath + @"\Run", e.Name),
        StartupLocation.RunMachine => (RegistryHive.LocalMachine, ApprovedPath + @"\Run", e.Name),
        StartupLocation.RunMachine32 => (RegistryHive.LocalMachine, ApprovedPath + @"\Run32", e.Name),
        StartupLocation.FolderUser => (RegistryHive.CurrentUser, ApprovedPath + @"\StartupFolder", e.Name + ".lnk"),
        _ => (RegistryHive.LocalMachine, ApprovedPath + @"\StartupFolder", e.Name + ".lnk"),
    };

    private static void ReadRun(RegistryHive hive, RegistryView view, StartupLocation location, List<StartupEntry> entries)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var run = root.OpenSubKey(RunPath);
            if (run is null) return;
            foreach (var name in run.GetValueNames())
            {
                if (string.IsNullOrEmpty(name) || run.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command)) continue;
                var exe = ExeOf(command);
                entries.Add(Build(name, command, exe, location));
            }
        }
        catch (Exception)
        {
            // Branche illisible : on continue.
        }
    }

    private static void ReadFolder(string folder, StartupLocation location, List<StartupEntry> entries)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)) continue; // desktop.ini
            var name = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(file) : Path.GetFileName(file);
            var target = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : file;
            entries.Add(Build(name, target ?? file, target, location) with { Command = file });
        }
    }

    private static StartupEntry Build(string name, string command, string? exe, StartupLocation location)
    {
        var (publisher, signed) = FileTrust.Check(exe);
        var exists = exe is null || File.Exists(exe);
        return new StartupEntry(name, command, exe, location, IsEnabled(location, name), publisher, signed, exists);
    }

    private static bool IsEnabled(StartupLocation location, string name)
    {
        var probe = new StartupEntry(name, "", null, location, true, null, null, true);
        var (hive, sub, valueName) = Approved(probe);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(sub);
            return StartupApproved.IsEnabled(key?.GetValue(valueName) as byte[]);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Programme lancé par une commande (« "C:\X\app.exe" --minimized » → C:\X\app.exe).</summary>
    internal static string? ExeOf(string command)
    {
        var (exe, _) = CommandLine.Split(Environment.ExpandEnvironmentVariables(command));
        if (string.IsNullOrWhiteSpace(exe)) return null;
        if (!Path.IsPathRooted(exe))
        {
            var inSystem = Path.Combine(Environment.SystemDirectory, exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe");
            return File.Exists(inSystem) ? inSystem : null;
        }
        return exe;
    }

    /// <summary>Cible d'un raccourci .lnk, via le shell de Windows.</summary>
    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
