#pragma warning disable CA1416 // Les tests sont ignorés hors Windows par WindowsFact.
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Windows.Tests;

/// <summary>
/// Vérifie que chaque lecture Windows ne plante pas et renvoie quelque chose de cohérent.
/// La machine de GitHub n'a ni carte graphique ni vrai écran : on reste prudent sur ces parties.
/// </summary>
public class ReadersTests
{
    [WindowsFact]
    public void Os_est_lu()
    {
        var os = HardwareReader.ReadOs();
        Assert.NotNull(os);
        Assert.False(string.IsNullOrWhiteSpace(os.Name));
        Assert.True(os.BuildNumber >= 10240, $"Build {os.BuildNumber}");
    }

    [WindowsFact]
    public void Processeur_est_lu()
    {
        var cpu = HardwareReader.ReadCpu();
        Assert.NotNull(cpu);
        Assert.True(cpu.Cores >= 1);
        Assert.True(cpu.LogicalProcessors >= cpu.Cores);
    }

    [WindowsFact]
    public void Memoire_est_lue()
    {
        var mem = HardwareReader.ReadMemory();
        Assert.NotNull(mem);
        Assert.True(mem.TotalBytes > 1L << 30);
        Assert.InRange(mem.AvailableBytes, 0, mem.TotalBytes);
    }

    [WindowsFact]
    public void Cartes_graphiques_et_ecrans_ne_plantent_pas()
    {
        Assert.NotNull(HardwareReader.ReadGpus());
        Assert.NotNull(DisplayReader.Read());
    }

    [WindowsFact]
    public void Disque_systeme_est_trouve()
    {
        var disks = DiskReader.Read();
        var system = Assert.Single(disks, d => d.IsSystemDrive);
        Assert.True(system.TotalBytes > 0);
        Assert.InRange(system.FreeBytes, 0, system.TotalBytes);
    }

    [WindowsFact]
    public void Journal_des_plantages_est_lisible()
    {
        Assert.NotNull(GpuCrashReader.ReadSince(DateTime.Now.AddDays(-30)));
    }

    [WindowsFact]
    public void Reglages_ne_plantent_pas()
    {
        _ = SettingsReader.ReadHardwareGpuScheduling();
        _ = SettingsReader.ReadPowerPlan(); // Peut manquer sur un serveur virtuel.
    }

    [WindowsFact]
    public async Task Photo_complete_du_pc()
    {
        var snapshot = await new WindowsSnapshotProvider().CaptureAsync();
        Assert.NotNull(snapshot.Os);
        Assert.NotNull(snapshot.Cpu);
        Assert.NotNull(snapshot.Memory);
        Assert.NotEmpty(snapshot.Disks);
    }

    [WindowsFact]
    public void Dossiers_personnels_existent()
    {
        Assert.True(Directory.Exists(KnownFolders.Downloads), KnownFolders.Downloads);
        Assert.NotEmpty(KnownFolders.PersonalFolders());
        Assert.All(KnownFolders.PersonalFoldersAndDataDrives(), p => Assert.True(Path.IsPathRooted(p)));
    }

    [WindowsFact]
    public void Disques_fixes_sont_listes_avec_celui_de_windows_en_premier()
    {
        var drives = KnownFolders.FixedDrives();
        Assert.NotEmpty(drives);
        Assert.True(drives[0].IsSystem);
        Assert.All(drives, d => Assert.InRange(d.FreeBytes, 0, d.TotalBytes));
        Assert.Equal(KnownFolders.PersonalFolders().Count,
            Core.Files.DriveFilter.Keep(KnownFolders.PersonalFolders(), [drives[0].Root]).Count);
    }

    [WindowsFact]
    public void Analyse_du_nettoyage_ne_touche_a_rien()
    {
        var temp = Path.Combine(Path.GetTempPath(), "os-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var file = Path.Combine(temp, "vieux.tmp");
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
        try
        {
            var scans = CleanupScanner.Scan(WindowsCleanupRules.Default(), DateTime.UtcNow);
            Assert.NotEmpty(scans);
            Assert.All(scans, s => Assert.All(s.Files, f => Assert.True(f.SizeBytes >= 0)));
            Assert.True(File.Exists(file), "L'analyse ne doit rien supprimer.");
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [WindowsFact]
    public void Corbeille_windows_est_lisible()
    {
        var (bytes, items) = new WindowsRecycleBin().Query();
        Assert.True(bytes >= 0);
        Assert.True(items >= 0);
    }

    [WindowsFact]
    public void Applications_installees_sont_listees()
    {
        var apps = InstalledAppsReader.Read();
        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
        Assert.Equal(apps.Count, apps.Select(a => a.RegistryKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [WindowsFact]
    public void Dossiers_a_restes_existent()
    {
        var (all, programs) = AppUninstaller.LeftoverParents();
        Assert.NotEmpty(all);
        Assert.All(all, p => Assert.True(Directory.Exists(p), p));
        Assert.All(programs, p => Assert.Contains(p, all));
    }

    [WindowsFact]
    public void Registre_export_puis_import_d_une_cle_de_test()
    {
        const string key = @"HKCU\Software\OptimizingSoftware.Tests";
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\OptimizingSoftware.Tests"))
            k.SetValue("Valeur", "42");
        var reg = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        var store = new WindowsRegistryStore();
        try
        {
            Assert.True(store.Export(key, reg));
            Assert.True(store.Delete(key));
            Assert.Null(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\OptimizingSoftware.Tests"));
            Assert.True(store.Import(reg));
            using var back = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\OptimizingSoftware.Tests");
            Assert.Equal("42", back?.GetValue("Valeur"));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\OptimizingSoftware.Tests", false);
            File.Delete(reg);
        }
    }
}
