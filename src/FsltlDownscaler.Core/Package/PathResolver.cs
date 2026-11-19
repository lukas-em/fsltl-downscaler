namespace FsltlDownscaler.Package;

/// <summary>
/// Resolves junctions and symbolic links in a folder path to the real folder.
/// Many users link packages into the Community folder (Addon Linker, MSFS Addons Linker, mklink /J),
/// or link the whole Community folder to another drive. The tool always works on the real folder,
/// so the backup ends up on the same drive as the package and files are only renamed, never copied.
/// </summary>
public static class PathResolver
{
    private const int MaxLinks = 32;
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>The real folder behind <paramref name="path"/>. Returns the full path unchanged if no link is involved.</summary>
    public static string ResolveFinal(string path)
    {
        string current = TrimEnd(Path.GetFullPath(path));
        for (int i = 0; i < MaxLinks; i++)
        {
            string? next = ResolveFirstLink(current);
            if (next is null)
            {
                return current;
            }

            current = next;
        }

        throw new UserException("The folder path contains too many nested links.");
    }

    /// <summary>Like <see cref="ResolveFinal"/>, but never throws. Falls back to the full path.</summary>
    public static string TryResolveFinal(string path)
    {
        try
        {
            return ResolveFinal(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or UserException)
        {
            try
            {
                return TrimEnd(Path.GetFullPath(path));
            }
            catch (Exception ex2) when (ex2 is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path;
            }
        }
    }

    public static bool SameFolder(string a, string b) =>
        string.Equals(TryResolveFinal(a), TryResolveFinal(b), StringComparison.OrdinalIgnoreCase);

    public static bool SameVolume(string a, string b)
    {
        string ra = Path.GetPathRoot(TryResolveFinal(a)) ?? string.Empty;
        string rb = Path.GetPathRoot(TryResolveFinal(b)) ?? string.Empty;
        return ra.Length > 0 && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replaces the first linked folder in the path by its target. Null if the path contains no link.</summary>
    private static string? ResolveFirstLink(string full)
    {
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string[] parts = full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        string current = root;

        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            var dir = new DirectoryInfo(current);
            if (!dir.Exists || dir.LinkTarget is null)
            {
                continue;
            }

            var target = dir.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null || IsVolumeGuidPath(target.FullName))
            {
                // Mounted volumes without a drive letter are left as they are.
                continue;
            }

            string resolved = TrimEnd(Path.GetFullPath(target.FullName));
            string rest = string.Join(Path.DirectorySeparatorChar, parts[(i + 1)..]);
            return rest.Length == 0 ? resolved : Path.Combine(resolved, rest);
        }

        return null;
    }

    private static bool IsVolumeGuidPath(string p) =>
        p.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith(@"Volume{", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes trailing separators, but keeps the root ("D:\").</summary>
    private static string TrimEnd(string full)
    {
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string trimmed = full.TrimEnd(Separators);
        return trimmed.Length < root.Length ? root : trimmed;
    }
}
