using System.Globalization;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>Une ligne de l'inventaire matériel (« Processeur : Intel Core i5-9600K… »).</summary>
public sealed record InventoryLine(string Label, string Value)
{
    private static string Go(long bytes) =>
        Math.Round(bytes / 1024d / 1024 / 1024).ToString(CultureInfo.GetCultureInfo("fr-FR")) + " Go";

    public static IEnumerable<InventoryLine> From(SystemSnapshot s)
    {
        if (s.Os is { } os)
            yield return new("Windows", $"{os.Name} {os.Version} (build {os.BuildNumber})");
        if (s.Cpu is { } cpu)
            yield return new("Processeur", $"{cpu.Name} — {cpu.Cores} cœurs, {cpu.LogicalProcessors} threads");
        foreach (var gpu in s.Gpus)
            yield return new("Carte graphique", gpu.DriverVersion is null ? gpu.Name : $"{gpu.Name} — pilote {gpu.DriverVersion}");
        if (s.Memory is { } mem)
        {
            var speed = mem.Modules.Select(m => m.ConfiguredSpeedMHz).FirstOrDefault(v => v > 0);
            var type = mem.Modules.FirstOrDefault()?.Type is { } t and not MemoryType.Unknown ? $" {t.ToString().ToUpperInvariant()}" : "";
            var detail = mem.Modules.Count > 0 ? $" — {mem.Modules.Count} barrette(s){type}{(speed is null ? "" : $" à {speed} MHz")}" : "";
            yield return new("Mémoire", Go(mem.TotalBytes) + detail);
        }
        foreach (var disk in s.Disks)
        {
            var kind = disk.MediaType switch { DiskMediaType.Ssd => "SSD", DiskMediaType.Hdd => "HDD", _ => "disque" };
            yield return new($"Disque {disk.DriveLetter}",
                $"{kind}{(disk.Model is null ? "" : $" {disk.Model}")} — {Go(disk.FreeBytes)} libres sur {Go(disk.TotalBytes)}");
        }
        foreach (var d in s.Displays)
            yield return new(d.Name, $"{d.Width}×{d.Height} à {d.CurrentRefreshHz} Hz (max {d.MaxRefreshHz} Hz)");
    }
}
