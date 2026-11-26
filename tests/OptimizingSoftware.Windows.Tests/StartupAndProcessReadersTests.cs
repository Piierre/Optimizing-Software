#pragma warning disable CA1416 // Ignorés hors Windows par WindowsFact.
using Microsoft.Win32;
using OptimizingSoftware.Core.Startup;

namespace OptimizingSoftware.Windows.Tests;

public class StartupAndProcessReadersTests
{
    [WindowsFact]
    public void Programmes_au_demarrage_ne_plantent_pas() => Assert.NotNull(StartupPrograms.Read());

    [WindowsFact]
    public void Desactiver_puis_reactiver_un_programme_au_demarrage()
    {
        const string name = "OptimizingSoftware.Test";
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        using (var key = Registry.CurrentUser.CreateSubKey(run))
            key.SetValue(name, $"\"{Environment.SystemDirectory}\\notepad.exe\"");
        try
        {
            var entry = Assert.Single(StartupPrograms.Read(), e => e.Name == name);
            Assert.True(entry.Enabled);
            Assert.Equal(StartupLocation.RunUser, entry.Location);

            Assert.True(StartupPrograms.SetEnabled(entry, false));
            Assert.False(Assert.Single(StartupPrograms.Read(), e => e.Name == name).Enabled);

            Assert.True(StartupPrograms.SetEnabled(entry, true));
            Assert.True(Assert.Single(StartupPrograms.Read(), e => e.Name == name).Enabled);
        }
        finally
        {
            using (var key = Registry.CurrentUser.OpenSubKey(run, writable: true)) key?.DeleteValue(name, false);
            using (var key = Registry.CurrentUser.OpenSubKey(approved, writable: true)) key?.DeleteValue(name, false);
        }
    }

    [WindowsFact]
    public async Task Processus_en_cours_sont_listes()
    {
        var processes = await ProcessReader.ReadAsync();
        Assert.NotEmpty(processes);
        Assert.All(processes, p => Assert.InRange(p.CpuPercent, 0, 100.5));
        Assert.Contains(processes, p => p.Path is not null);
    }

    [WindowsFact]
    public void Signature_numerique_est_verifiee()
    {
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(dotnet)) Assert.True(FileTrust.IsSigned(dotnet));

        var unsigned = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 0, 0]);
        try
        {
            Assert.False(FileTrust.IsSigned(unsigned));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }
}
