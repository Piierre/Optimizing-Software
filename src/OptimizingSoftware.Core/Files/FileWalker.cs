namespace OptimizingSoftware.Core.Files;

/// <summary>
/// Parcourt des dossiers en sautant les liens, les dossiers système et ceux qu'on ne doit jamais proposer
/// (jeux installés, applications, corbeilles).
/// </summary>
public static class FileWalker
{
    /// <summary>Noms de dossiers jamais parcourus, où qu'ils soient.</summary>
    public static readonly IReadOnlySet<string> SkippedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Recovery", "Windows", "Windows.old",
        "Program Files", "Program Files (x86)", "ProgramData", "AppData",
        // Jeux installés : supprimer un de leurs fichiers casserait le jeu.
        "steamapps", "SteamLibrary", "Epic Games", "XboxGames", "WindowsApps", "Riot Games", "Battle.net",
        "EA Games", "Ubisoft Game Launcher", "GOG Galaxy",
        // Code et outils : beaucoup de petits fichiers identiques volontairement.
        "node_modules", ".git", ".nuget",
        "OptimizingSoftware.Corbeille", "OptimizingSoftware",
    };

    public static IEnumerable<FileInfo> Walk(IEnumerable<string> roots, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<DirectoryInfo>(roots.Where(Directory.Exists).Select(r => new DirectoryInfo(r)));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            if (!seen.Add(dir.FullName)) continue;

            FileSystemInfo[] entries;
            try
            {
                entries = dir.GetFileSystemInfos();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo sub)
                {
                    if (!SkippedFolderNames.Contains(sub.Name) && !sub.Attributes.HasFlag(FileAttributes.System))
                        stack.Push(sub);
                }
                else if (entry is FileInfo file && !file.Attributes.HasFlag(FileAttributes.System))
                {
                    yield return file;
                }
            }
        }
    }
}
