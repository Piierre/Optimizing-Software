namespace OptimizingSoftware.Core.Apps;

/// <summary>Une application installée, telle que déclarée dans la liste « Applications et fonctionnalités ».</summary>
/// <param name="RegistryKey">Clé de désinstallation complète (ex. « HKEY_LOCAL_MACHINE\SOFTWARE\...\Uninstall\Steam »).</param>
/// <param name="SizeBytes">Taille déclarée par l'installateur, ou mesurée sur le dossier d'installation.</param>
public sealed record InstalledApp(
    string Name,
    string? Publisher,
    string? Version,
    DateOnly? InstallDate,
    long? SizeBytes,
    string? InstallLocation,
    string? UninstallCommand,
    string RegistryKey,
    bool IsBroken)
{
    /// <summary>Mots qui servent à reconnaître les dossiers de cette application.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            yield return NameMatcher.Normalize(Name);
            if (Publisher is not null) yield return NameMatcher.Normalize(Publisher);
            if (InstallLocation is not null) yield return NameMatcher.Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(InstallLocation)));
        }
    }
}

public static class NameMatcher
{
    /// <summary>« NVIDIA Corporation » → « nvidiacorporation ». Garde lettres et chiffres.</summary>
    public static string Normalize(string? text) =>
        text is null ? "" : new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Vrai si le nom de dossier correspond à l'un des mots-clés (dans un sens ou dans l'autre, 4 lettres minimum).</summary>
    public static bool Matches(string folderName, IEnumerable<string> keys)
    {
        var folder = Normalize(folderName);
        if (folder.Length < 4) return false;
        foreach (var key in keys)
        {
            if (key.Length < 4) continue;
            if (key.Contains(folder, StringComparison.Ordinal) || folder.Contains(key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
