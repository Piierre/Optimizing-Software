namespace OptimizingSoftware.Core.Diagnostics.Checks;

/// <summary>Windows doit être sur un SSD, avec assez de place libre.</summary>
public sealed class SystemDiskCheck : IDiagnosticCheck
{
    public const double ProblemFreeRatio = 0.10;
    public const double WarningFreeRatio = 0.15;
    public const long ProblemFreeBytes = 20L * 1024 * 1024 * 1024;

    public string Id => "system-disk";
    public string Title => "Disque de Windows";

    public CheckResult Evaluate(SystemSnapshot s)
    {
        var disk = s.Disks.FirstOrDefault(d => d.IsSystemDrive);
        if (disk is null)
            return new(Id, Title, CheckStatus.Unknown, "Disque système introuvable.");

        var kind = disk.MediaType switch
        {
            DiskMediaType.Ssd => "SSD",
            DiskMediaType.Hdd => "disque dur (HDD)",
            _ => "disque",
        };
        var free = $"{Format.Gigabytes(disk.FreeBytes)} libres sur {Format.Gigabytes(disk.TotalBytes)} ({Format.Percent(disk.FreeRatio)})";
        var summary = $"{disk.DriveLetter} sur {kind}{(disk.Model is null ? "" : $" {disk.Model}")} : {free}.";

        if (disk.MediaType == DiskMediaType.Hdd)
            return new(Id, Title, CheckStatus.Problem, summary,
                "Windows sur un disque dur ralentit tout. Passer sur un SSD est le changement le plus visible pour la fluidité.");

        const string spaceAdvice =
            "Libère de la place : fichiers temporaires, jeux inutilisés, ou déplace des jeux sur un autre disque.";

        if (disk.FreeRatio < ProblemFreeRatio || disk.FreeBytes < ProblemFreeBytes)
            return new(Id, Title, CheckStatus.Problem, summary, spaceAdvice);
        if (disk.FreeRatio < WarningFreeRatio)
            return new(Id, Title, CheckStatus.Warning, summary, spaceAdvice);
        return new(Id, Title, CheckStatus.Ok, summary);
    }
}
