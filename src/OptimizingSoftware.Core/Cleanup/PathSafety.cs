namespace OptimizingSoftware.Core.Cleanup;

/// <summary>Garde-fous : un fichier n'est touché que s'il est bien rangé sous l'un des dossiers autorisés.</summary>
public static class PathSafety
{
    private static readonly StringComparison Cmp =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Vrai si <paramref name="path"/> est strictement à l'intérieur de l'un des <paramref name="roots"/>.</summary>
    public static bool IsInside(string path, IEnumerable<string> roots)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in roots)
        {
            // Une racine de disque (« D:\ ») se termine déjà par un séparateur : ne pas le doubler.
            var r = Path.GetFullPath(root);
            if (!Path.EndsInDirectorySeparator(r)) r += Path.DirectorySeparatorChar;
            if (full.Length > r.Length && full.StartsWith(r, Cmp))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Remplace chaque segment « * » par les sous-dossiers existants. Les liens symboliques et jonctions
    /// sont ignorés pour ne jamais sortir du dossier prévu.
    /// </summary>
    public static IReadOnlyList<string> Expand(string pattern)
    {
        var segments = pattern.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var star = Array.IndexOf(segments, "*");
        if (star < 0)
            return Directory.Exists(pattern) && !IsLink(pattern) ? [pattern] : [];

        var prefix = string.Join(Path.DirectorySeparatorChar, segments[..star]);
        if (prefix.Length == 0 || !Directory.Exists(prefix))
            return [];
        var rest = string.Join(Path.DirectorySeparatorChar, segments[(star + 1)..]);

        var results = new List<string>();
        foreach (var dir in SafeDirectories(prefix))
            results.AddRange(Expand(rest.Length == 0 ? dir : Path.Combine(dir, rest)));
        return results;
    }

    public static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)
                   || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception)
        {
            return true; // Dans le doute, on n'y touche pas.
        }
    }

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir).Where(d => !IsLink(d)).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
