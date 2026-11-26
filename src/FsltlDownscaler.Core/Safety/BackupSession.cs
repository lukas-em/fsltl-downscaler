using System.Security.Cryptography;
using System.Text.Json;
using FsltlDownscaler.Package;

namespace FsltlDownscaler.Safety;

public sealed class SessionInfo
{
    public string PackageRoot { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public int Target { get; set; }
    public bool GenerateMips { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    /// <summary>running, completed, cancelled, failed, incomplete, restored. "incomplete": textures replaced, but layout.json could not be updated.</summary>
    public string Status { get; set; } = "running";
    public string OriginalLayoutSha256 { get; set; } = string.Empty;
    public string? FinalLayoutSha256 { get; set; }
    public int FilesReplaced { get; set; }
    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }
    public string ToolVersion { get; set; } = string.Empty;
}

/// <summary>
/// One run of the tool. Lives outside the package folder.
/// Originals are moved (not copied) into files/, so on the same drive the backup costs no extra space.
/// </summary>
public sealed class BackupSession
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _journalLock = new();

    private BackupSession(string dir, SessionInfo info)
    {
        Dir = dir;
        Info = info;
    }

    public string Dir { get; }
    public SessionInfo Info { get; }
    public string FilesDir => Path.Combine(Dir, "files");
    public string MetaDir => Path.Combine(Dir, "meta");
    public string TmpDir => Path.Combine(Dir, "tmp");
    public string InfoPath => Path.Combine(Dir, "session.json");
    public string JournalPath => Path.Combine(Dir, "journal.log");

    /// <summary>Default backup folder: next to the Community folder, never inside it.</summary>
    public static string DefaultBaseDir(string packageRoot)
    {
        var parent = Directory.GetParent(packageRoot);
        var grandParent = parent?.Parent;
        string baseDir = (grandParent ?? parent)?.FullName ?? packageRoot;
        return Path.Combine(baseDir, "FSLTL-Downscaler-Backup");
    }

    public static BackupSession Create(string baseDir, PackageContext pkg, int target, bool generateMips)
    {
        string full = Path.GetFullPath(baseDir);
        if (IsInside(full, pkg.Root))
        {
            throw new UserException("The backup folder must not be inside the package folder.");
        }

        string name = $"{Path.GetFileName(pkg.Root)}_{DateTime.Now:yyyyMMdd-HHmmss}";
        string dir = Path.Combine(full, name);
        Directory.CreateDirectory(dir);

        var info = new SessionInfo
        {
            PackageRoot = pkg.Root,
            PackageVersion = pkg.Version,
            Target = target,
            GenerateMips = generateMips,
            StartedUtc = DateTime.UtcNow,
            OriginalLayoutSha256 = Sha256(pkg.LayoutPath),
            ToolVersion = typeof(BackupSession).Assembly.GetName().Version?.ToString() ?? "?",
        };

        var session = new BackupSession(dir, info);
        Directory.CreateDirectory(session.FilesDir);
        Directory.CreateDirectory(session.MetaDir);
        Directory.CreateDirectory(session.TmpDir);
        File.Copy(pkg.LayoutPath, Path.Combine(session.MetaDir, "layout.json"));
        File.Copy(pkg.ManifestPath, Path.Combine(session.MetaDir, "manifest.json"));
        session.Save();
        BackupLocations.Remember(full);
        return session;
    }

    /// <summary>
    /// Backups in <paramref name="baseDir"/> that belong to the package in <paramref name="packageRoot"/>.
    /// With <paramref name="currentLayoutHash"/> a backup also matches after the package was moved to another folder.
    /// </summary>
    public static IEnumerable<BackupSession> FindAll(string baseDir, string packageRoot, string? currentLayoutHash = null)
    {
        if (!Directory.Exists(baseDir))
        {
            yield break;
        }

        string realRoot = PathResolver.TryResolveFinal(packageRoot);
        foreach (var dir in Directory.EnumerateDirectories(baseDir).OrderByDescending(d => d, StringComparer.Ordinal))
        {
            var s = TryLoad(dir);
            if (s is not null && BelongsTo(s, realRoot, currentLayoutHash))
            {
                yield return s;
            }
        }
    }

    /// <summary>
    /// Same package folder, or a package that was moved: the layout.json written by a run is unique,
    /// because it holds the file times of the replaced textures. A copy doesn't count. If the package
    /// is still in its old place with the same layout.json, the backup stays with the old place.
    /// </summary>
    private static bool BelongsTo(BackupSession s, string realRoot, string? currentLayoutHash)
    {
        if (string.Equals(PathResolver.TryResolveFinal(s.Info.PackageRoot), realRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentLayoutHash is null || s.Info.Status == "restored"
            || !string.Equals(s.Info.FinalLayoutSha256, currentLayoutHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string oldLayout = Path.Combine(s.Info.PackageRoot, "layout.json");
        return !File.Exists(oldLayout) || !string.Equals(TrySha256(oldLayout), currentLayoutHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the backup was made while the package was in another folder.</summary>
    public bool IsFromOtherFolder(PackageContext pkg) =>
        !PathResolver.SameFolder(Info.PackageRoot, pkg.Root);

    /// <summary>
    /// Every folder where a backup of this package can be. Besides the default next to the real package,
    /// this covers backups made by version 1.0 through a linked folder: they are next to the link,
    /// often on another drive.
    /// </summary>
    public static IReadOnlyList<string> CandidateBaseDirs(PackageContext pkg)
    {
        var dirs = new List<string> { DefaultBaseDir(pkg.Root) };
        void Add(string packagePath)
        {
            string d = DefaultBaseDir(packagePath);
            if (!dirs.Contains(d, StringComparer.OrdinalIgnoreCase))
            {
                dirs.Add(d);
            }
        }

        Add(pkg.SelectedPath);
        foreach (var c in PackageLocator.FindCandidates())
        {
            Add(c.Path);
        }

        // Every backup folder the tool has used before. Finds the backup after the package was moved.
        foreach (string d in BackupLocations.Load())
        {
            if (!dirs.Contains(d, StringComparer.OrdinalIgnoreCase))
            {
                dirs.Add(d);
            }
        }

        return dirs;
    }

    /// <summary>All backups of this package in all candidate folders, newest first.</summary>
    public static List<BackupSession> FindForPackage(PackageContext pkg) =>
        FindForPackage(pkg, TrySha256(pkg.LayoutPath));

    /// <summary>All backups of this package in all candidate folders, newest first. Includes backups from before a move.</summary>
    public static List<BackupSession> FindForPackage(PackageContext pkg, string? currentLayoutHash)
    {
        var all = new List<BackupSession>();
        foreach (string baseDir in CandidateBaseDirs(pkg))
        {
            try
            {
                all.AddRange(FindAll(baseDir, pkg.Root, currentLayoutHash));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder we can't read can't hold our backup.
            }
        }

        return all
            .GroupBy(s => s.Dir, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(s => s.Info.StartedUtc)
            .ToList();
    }

    public static BackupSession? TryLoad(string dir)
    {
        string infoPath = Path.Combine(dir, "session.json");
        if (!File.Exists(infoPath))
        {
            return null;
        }

        try
        {
            var info = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(infoPath));
            return info is null ? null : new BackupSession(dir, info);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        string tmp = InfoPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Info, JsonOptions));
        File.Move(tmp, InfoPath, overwrite: true);
    }

    /// <summary>
    /// Replaces one package file safely:
    /// 1. new content to tmp and verified, 2. original moved to backup, 3. tmp moved into place.
    /// If step 3 fails the original is moved back.
    /// </summary>
    public void ReplaceFile(PackageContext pkg, string fullPath, byte[] newContent)
    {
        string rel = pkg.Relative(fullPath);
        string backupPath = Path.Combine(FilesDir, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(backupPath))
        {
            throw new InvalidOperationException($"Backup already contains {rel}.");
        }

        string tmp = Path.Combine(TmpDir, Guid.NewGuid().ToString("N") + ".dds");
        File.WriteAllBytes(tmp, newContent);
        if (new FileInfo(tmp).Length != newContent.LongLength)
        {
            File.Delete(tmp);
            throw new IOException("Temporary file could not be written completely.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Move(fullPath, backupPath);
        try
        {
            File.Move(tmp, fullPath);
        }
        catch
        {
            File.Move(backupPath, fullPath);
            throw;
        }

        Journal($"replaced\t{rel}\t{newContent.LongLength}");
    }

    /// <summary>
    /// Writes a package file (layout.json, manifest.json). The new content goes to tmp first.
    /// Same drive: tmp is renamed over the target. Other drive: tmp is copied over the target,
    /// because replacing a file across drives with a move fails on some systems (access denied).
    /// </summary>
    public void WriteMetaFile(string targetPath, byte[] content)
    {
        string tmp = Path.Combine(TmpDir, Path.GetFileName(targetPath) + ".new");
        File.WriteAllBytes(tmp, content);

        if (PathResolver.SameVolume(tmp, targetPath))
        {
            File.Move(tmp, targetPath, overwrite: true);
        }
        else
        {
            File.Copy(tmp, targetPath, overwrite: true);
            File.Delete(tmp);
        }

        if (new FileInfo(targetPath).Length != content.LongLength)
        {
            throw new IOException($"{Path.GetFileName(targetPath)} could not be written completely.");
        }

        Journal($"wrote\t{Path.GetFileName(targetPath)}\t{content.LongLength}");
    }

    /// <summary>
    /// Moves a file and replaces the target. Across drives the file is copied and the source deleted afterwards,
    /// because a replacing move across drives fails on some systems (access denied).
    /// </summary>
    public static void MoveOver(string source, string target) =>
        MoveOver(source, target, PathResolver.SameVolume(source, target));

    public static void MoveOver(string source, string target, bool sameVolume)
    {
        if (sameVolume)
        {
            File.Move(source, target, overwrite: true);
            return;
        }

        long length = new FileInfo(source).Length;
        File.Copy(source, target, overwrite: true);
        if (new FileInfo(target).Length != length)
        {
            throw new IOException($"{Path.GetFileName(target)} could not be copied completely.");
        }

        File.Delete(source);
    }

    public void Journal(string line)
    {
        lock (_journalLock)
        {
            File.AppendAllText(JournalPath, $"{DateTime.UtcNow:O}\t{line}{Environment.NewLine}");
        }
    }

    public void CleanupTmp()
    {
        try
        {
            if (Directory.Exists(TmpDir))
            {
                foreach (var f in Directory.EnumerateFiles(TmpDir))
                {
                    File.Delete(f);
                }
            }
        }
        catch (IOException)
        {
            // Not critical. The tmp folder is outside the package.
        }
    }

    public IEnumerable<string> BackedUpFiles() =>
        Directory.Exists(FilesDir)
            ? Directory.EnumerateFiles(FilesDir, "*", SearchOption.AllDirectories)
            : [];

    /// <summary>Bytes the backup takes on disk.</summary>
    public long SizeBytes()
    {
        try
        {
            return Directory.Exists(Dir)
                ? Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Deletes this backup for good. Afterwards "Restore original" is no longer possible for this run.
    /// Only folders that look like one of our backups are deleted, and never anything inside the package.
    /// </summary>
    public void Delete()
    {
        if (Info.Status is "running" or "incomplete")
        {
            throw new UserException("This backup belongs to an unfinished run. Please restore the original first.");
        }

        if (!File.Exists(InfoPath) || !Path.GetFileName(Path.GetDirectoryName(Dir) ?? string.Empty)
                .Equals("FSLTL-Downscaler-Backup", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserException($"{Dir} doesn't look like a backup of FSLTL Downscaler. Nothing was deleted.");
        }

        if (!string.IsNullOrEmpty(Info.PackageRoot) && IsInside(Dir, Info.PackageRoot))
        {
            throw new UserException("The backup is inside the package folder. Nothing was deleted.");
        }

        // Read-only files would stop Directory.Delete halfway.
        foreach (string f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }

        Directory.Delete(Dir, recursive: true);

        // Remove the base folder as well once it is empty.
        var parent = Directory.GetParent(Dir);
        if (parent is { Exists: true } && !parent.EnumerateFileSystemInfos().Any())
        {
            parent.Delete();
        }
    }

    public static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public static string? TrySha256(string path)
    {
        try
        {
            return File.Exists(path) ? Sha256(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsInside(string candidate, string folder)
    {
        string c = Path.GetFullPath(candidate).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return c.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
