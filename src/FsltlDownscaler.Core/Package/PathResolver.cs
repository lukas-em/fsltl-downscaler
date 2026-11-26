using System.Runtime.InteropServices;

namespace FsltlDownscaler.Package;

/// <summary>
/// Resolves junctions and symbolic links in a folder path to the real folder.
/// Many users link packages into the Community folder (Addon Linker, MSFS Addons Linker, mklink /J),
/// or link the whole Community folder to another drive. The tool always works on the real folder,
/// so the backup ends up on the same drive as the package and files are only renamed, never copied.
/// Folders that are the mount point of another drive (Disk Management "Mount in the following empty NTFS folder")
/// are real folders and stay as they are.
/// </summary>
public static class PathResolver
{
    private const int MaxLinks = 32;
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// The real folder behind <paramref name="path"/>. Returns the full path unchanged if no link is involved
    /// or if the links can't be followed to an end.
    /// </summary>
    public static string ResolveFinal(string path)
    {
        string start = TrimEnd(Path.GetFullPath(path));
        string current = start;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current };

        for (int i = 0; i < MaxLinks; i++)
        {
            string? next = ResolveFirstLink(current);
            if (next is null)
            {
                return current;
            }

            if (!seen.Add(next))
            {
                // The links lead back to a path we already had. Work on the selected path instead of blocking the user.
                return start;
            }

            current = next;
        }

        return start;
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

    /// <summary>
    /// True when both paths are on the same volume. Uses the volume mount point, so a drive mounted
    /// into a folder (e.g. E:\Games\SSD1) counts as its own volume, not as part of E:.
    /// </summary>
    public static bool SameVolume(string a, string b)
    {
        string ra = VolumeRoot(TryResolveFinal(a));
        string rb = VolumeRoot(TryResolveFinal(b));
        return ra.Length > 0 && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The mount point of the volume that holds <paramref name="path"/>, e.g. "D:\" or "E:\Games\SSD1\".
    /// Links are resolved and a folder that doesn't exist yet is looked up through its nearest existing parent.
    /// </summary>
    public static string VolumeRootOf(string path) => VolumeRoot(TryResolveFinal(NearestExisting(path)));

    /// <summary>
    /// Free space for the current user on the volume that holds <paramref name="path"/>.
    /// Unlike DriveInfo this is correct for drives mounted into a folder.
    /// </summary>
    public static long FreeBytes(string path)
    {
        string existing = TryResolveFinal(NearestExisting(path));
        if (File.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing) ?? existing;
        }

        if (OperatingSystem.IsWindows())
        {
            string dir = existing.EndsWith('\\') ? existing : existing + "\\";
            if (GetDiskFreeSpaceExW(dir, out ulong free, out _, out _))
            {
                return free > long.MaxValue ? long.MaxValue : (long)free;
            }
        }

        return new DriveInfo(Path.GetPathRoot(existing) ?? existing).AvailableFreeSpace;
    }

    /// <summary>The path itself if it exists, otherwise its nearest existing parent folder.</summary>
    public static string NearestExisting(string path)
    {
        string current = TrimEnd(Path.GetFullPath(path));
        while (!Directory.Exists(current) && !File.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return current;
    }

    /// <summary>Replaces the first linked folder in the path by its target. Null if the path contains no link to follow.</summary>
    private static string? ResolveFirstLink(string full)
    {
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string[] parts = full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        string current = root;

        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            var dir = new DirectoryInfo(current);
            string? linkTarget = dir.Exists ? dir.LinkTarget : null;
            if (linkTarget is null || IsVolumeGuidPath(linkTarget))
            {
                // No link, or a drive mounted into this folder. A mounted drive is a real folder:
                // following it would only lead back to the same path.
                continue;
            }

            // Follow one link at a time. The outer loop starts over until no link is left.
            var target = dir.ResolveLinkTarget(returnFinalTarget: false);
            if (target is null || IsVolumeGuidPath(target.FullName))
            {
                continue;
            }

            string resolved = TrimEnd(Path.GetFullPath(target.FullName));
            if (string.Equals(resolved, TrimEnd(current), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = string.Join(Path.DirectorySeparatorChar, parts[(i + 1)..]);
            return rest.Length == 0 ? resolved : Path.Combine(resolved, rest);
        }

        return null;
    }

    private static bool IsVolumeGuidPath(string p) =>
        p.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith(@"Volume{", StringComparison.OrdinalIgnoreCase)
        || p.Contains(@"\Volume{", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mount point of the volume that holds <paramref name="path"/>, e.g. "D:\" or "E:\Games\SSD1\".</summary>
    private static string VolumeRoot(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var buffer = new char[1024];
            if (GetVolumePathNameW(path, buffer, (uint)buffer.Length))
            {
                int len = Array.IndexOf(buffer, '\0');
                string mount = new string(buffer, 0, len < 0 ? buffer.Length : len);
                if (mount.Length > 0)
                {
                    return mount.EndsWith('\\') ? mount : mount + "\\";
                }
            }
        }

        return Path.GetPathRoot(path) ?? string.Empty;
    }

#pragma warning disable SYSLIB1054 // Plain DllImport keeps the project free of unsafe code.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string lpszFileName, [Out] char[] lpszVolumePathName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
#pragma warning restore SYSLIB1054

    /// <summary>Removes trailing separators, but keeps the root ("D:\").</summary>
    private static string TrimEnd(string full)
    {
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string trimmed = full.TrimEnd(Separators);
        return trimmed.Length < root.Length ? root : trimmed;
    }
}
