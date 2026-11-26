using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Windows;

/// <summary>Export, suppression et réimport de clés de registre (via reg.exe pour les sauvegardes .reg).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryStore : IRegistryStore
{
    public bool Export(string key, string regFile) => RunReg($"export \"{key}\" \"{regFile}\" /y");

    public bool Import(string regFile) => RunReg($"import \"{regFile}\"");

    public bool Delete(string key)
    {
        var (hive, subKey) = Split(key);
        if (hive is null || string.IsNullOrEmpty(subKey)) return false;
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive.Value, RegistryView.Registry64);
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static (RegistryHive? Hive, string SubKey) Split(string key)
    {
        var slash = key.IndexOf('\\');
        if (slash < 0) return (null, "");
        RegistryHive? hive = key[..slash].ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            _ => null, // Jamais ailleurs (HKEY_CLASSES_ROOT, HKEY_USERS…).
        };
        return (hive, key[(slash + 1)..]);
    }

    private static bool RunReg(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return false;
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
