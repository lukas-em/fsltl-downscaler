using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>folder, zip or none (see <see cref="BackupFormats"/>). Backups made by version 1.2 and older count as folder.</summary>
    public string BackupFormat { get; set; } = BackupFormats.Folder;

    /// <summary>Bytes of files.zip once the backup is compressed.</summary>
    public long ZipBytes { get; set; }
}

/// <summary>
/// One run of the tool. Lives outside the package folder.
/// Originals are moved (not copied) into files/, so on the same drive the backup costs no extra space.
/// files/ can be packed into files.zip later. A run without backup only keeps layout.json, manifest.json and the journal.
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
    public string ZipPath => Path.Combine(Dir, "files.zip");
    private string ZipPartialPath => ZipPath + ".partial";
    private string ZipIndexPath => Path.Combine(MetaDir, "zip-index.tsv");

    /// <summary>The run was made without a backup. Its originals are gone.</summary>
    public bool IsNoBackup => Info.BackupFormat == BackupFormats.None;

    /// <summary>The originals are in files.zip.</summary>
    public bool IsZip => Info.BackupFormat == BackupFormats.Zip;

    /// <summary>True when there is at least one original texture in this backup.</summary>
    public bool HasOriginals => !IsNoBackup && (File.Exists(ZipPath) || BackedUpFiles().Any());

    /// <summary>Set on first use: the tmp folder is on the drive of the package (runs without backup only).</summary>
    private bool? _tmpOnPackageVolume;

    /// <summary>Default backup folder: next to the Community folder, never inside it.</summary>
    public static string DefaultBaseDir(string packageRoot)
    {
        var parent = Directory.GetParent(packageRoot);
        var grandParent = parent?.Parent;
        string baseDir = (grandParent ?? parent)?.FullName ?? packageRoot;
        return Path.Combine(baseDir, "FSLTL-Downscaler-Backup");
    }

    public static BackupSession Create(string baseDir, PackageContext pkg, int target, bool generateMips,
        BackupMode mode = BackupMode.Folder)
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
            // A ZIP backup starts as a folder. It is packed once the run is done.
            BackupFormat = mode == BackupMode.None ? BackupFormats.None : BackupFormats.Folder,
        };

        var session = new BackupSession(dir, info);
        if (mode != BackupMode.None)
        {
            Directory.CreateDirectory(session.FilesDir);
        }

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
    /// Without backup the tmp file replaces the original in one step and the original is gone.
    /// </summary>
    public void ReplaceFile(PackageContext pkg, string fullPath, byte[] newContent)
    {
        string rel = pkg.Relative(fullPath);
        string tmp = Path.Combine(TmpDir, Guid.NewGuid().ToString("N") + ".dds");
        if (IsNoBackup)
        {
            WriteTmp(tmp, newContent);
            ReplaceWithoutBackup(pkg, tmp, fullPath, newContent.LongLength);
            Journal($"replaced\t{rel}\t{newContent.LongLength}\tno backup");
            return;
        }

        string backupPath = Path.Combine(FilesDir, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(backupPath))
        {
            throw new InvalidOperationException($"Backup already contains {rel}.");
        }

        WriteTmp(tmp, newContent);

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

    private static void WriteTmp(string tmp, byte[] content)
    {
        File.WriteAllBytes(tmp, content);
        if (new FileInfo(tmp).Length != content.LongLength)
        {
            File.Delete(tmp);
            throw new IOException("Temporary file could not be written completely.");
        }
    }

    /// <summary>
    /// Same drive: the tmp file is renamed over the original, so the texture is either old or new, never half written.
    /// Other drive (linked package): copied over the original and checked.
    /// </summary>
    private void ReplaceWithoutBackup(PackageContext pkg, string tmp, string fullPath, long length)
    {
        _tmpOnPackageVolume ??= PathResolver.SameVolume(TmpDir, pkg.Root);
        ClearReadOnly(fullPath);
        if (_tmpOnPackageVolume == true)
        {
            File.Move(tmp, fullPath, overwrite: true);
        }
        else
        {
            File.Copy(tmp, fullPath, overwrite: true);
            File.Delete(tmp);
        }

        if (new FileInfo(fullPath).Length != length)
        {
            throw new IOException("The new texture could not be written completely.");
        }
    }

    public static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            var attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            }
        }
    }

    /// <summary>
    /// Writes a package file (layout.json, manifest.json). The new content goes to tmp first.
    /// Same drive: tmp is renamed over the target. Other drive: tmp is copied over the target,
    /// because replacing a file across drives with a move fails on some systems (access denied).
    /// </summary>
    public void WriteMetaFile(string targetPath, byte[] content)
    {
        Directory.CreateDirectory(TmpDir);
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

    // ================================================================ ZIP

    /// <summary>Name of a backed-up file inside files.zip, e.g. SimObjects/Airplanes/.../texture/x.dds.</summary>
    private string EntryName(string backedUpFile) =>
        Path.GetRelativePath(FilesDir, backedUpFile).Replace('\\', '/');

    /// <summary>
    /// Packs the originals in files/ into files.zip. files/ is only deleted after every file in the ZIP was read back
    /// and compared byte by byte with its original. On an error or cancel files/ stays as it is and the half-written ZIP is removed.
    /// Returns the size of the ZIP file.
    /// </summary>
    public long CompressToZip(Action<string> log, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        if (IsNoBackup)
        {
            throw new UserException("This run was made without a backup. There is nothing to compress.");
        }

        if (Info.Status is "running" or "incomplete" or "restored")
        {
            throw new UserException("Only the backup of a finished run can be compressed.");
        }

        TidyUp();
        if (IsZip && File.Exists(ZipPath))
        {
            return new FileInfo(ZipPath).Length;
        }

        var files = BackedUpFiles().OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            throw new UserException("The backup holds no original textures.");
        }

        long total = files.Sum(f => new FileInfo(f).Length);
        long needed = EstimateZipBytes(total) + 64L * 1024 * 1024;
        long free = PathResolver.FreeBytes(Dir);
        if (free < needed)
        {
            throw new UserException(
                $"There isn't enough free disk space to compress the backup. Needed: about {needed / 1_000_000_000d:0.0} GB, " +
                $"available: {free / 1_000_000_000d:0.0} GB.");
        }

        int steps = files.Count * 2;
        string partial = ZipPartialPath;
        try
        {
            var index = new StringBuilder();
            log($"Compressing {files.Count:N0} original textures ({total / 1_000_000_000d:0.0} GB) into {ZipPath} ...");
            using (var fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                int done = 0;
                foreach (var (path, data) in ReadAhead(files, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    string name = EntryName(path);
                    var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using (var es = entry.Open())
                    {
                        es.Write(data);
                    }

                    long fileTime = File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
                    index.Append(name).Append('\t').Append(data.LongLength.ToString(CultureInfo.InvariantCulture))
                        .Append('\t').Append(fileTime.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    done++;
                    if (done % 25 == 0 || done == files.Count)
                    {
                        progress?.Report((done, steps));
                    }
                }
            }

            log("Checking every file in the ZIP against its original ...");
            VerifyZip(partial, files, progress, steps, ct);

            // The index keeps the original file times. ZIP only stores them in 2 second steps and local time.
            File.WriteAllText(ZipIndexPath, index.ToString(), new UTF8Encoding(false));
            File.Move(partial, ZipPath, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        long zipBytes = new FileInfo(ZipPath).Length;
        Info.BackupFormat = BackupFormats.Zip;
        Info.ZipBytes = zipBytes;
        Save();
        Journal($"compressed\t{files.Count}\t{total}\t{zipBytes}");

        // Only now the originals in files/ may go. If this fails, TidyUp finishes it next time.
        try
        {
            DeleteDirectory(FilesDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"The folder {FilesDir} could not be removed completely ({ex.Message}). It is removed next time.");
        }

        return zipBytes;
    }

    /// <summary>FSLTL textures pack to about 20 %. Twice that leaves enough room for other textures.</summary>
    public static long EstimateZipBytes(long originalBytes) => originalBytes * 2 / 5;

    /// <summary>Reads the next files while the current one is compressed. At most three files are held in memory.</summary>
    private static IEnumerable<(string Path, byte[] Data)> ReadAhead(IReadOnlyList<string> files, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var queue = new BlockingCollection<(string, byte[])>(boundedCapacity: 3);
        var reader = Task.Run(() =>
        {
            try
            {
                foreach (string f in files)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    queue.Add((f, File.ReadAllBytes(f)), cts.Token);
                }
            }
            finally
            {
                queue.CompleteAdding();
            }
        });

        try
        {
            foreach (var item in queue.GetConsumingEnumerable(cts.Token))
            {
                yield return item;
            }

            // Surfaces a read error of the reader.
            reader.GetAwaiter().GetResult();
        }
        finally
        {
            cts.Cancel();
            try
            {
                reader.Wait();
            }
            catch (AggregateException)
            {
                // Already handled above or caused by the cancel.
            }

            queue.Dispose();
        }
    }

    private void VerifyZip(string zipPath, IReadOnlyList<string> files, IProgress<(int Done, int Total)>? progress, int steps, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count != files.Count)
        {
            throw new IOException($"The ZIP file holds {zip.Entries.Count:N0} files instead of {files.Count:N0}.");
        }

        var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            byName[e.FullName] = e;
        }

        var a = new byte[1 << 20];
        var b = new byte[1 << 20];
        int done = 0;
        foreach (string f in files)
        {
            ct.ThrowIfCancellationRequested();
            string name = EntryName(f);
            long length = new FileInfo(f).Length;
            if (!byName.TryGetValue(name, out var entry) || entry.Length != length)
            {
                throw new IOException($"{name} is missing in the ZIP file or has the wrong size.");
            }

            using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var packed = entry.Open())
            {
                while (true)
                {
                    int n = src.ReadAtLeast(a, a.Length, throwOnEndOfStream: false);
                    int m = packed.ReadAtLeast(b.AsSpan(0, n == 0 ? 1 : n), n == 0 ? 1 : n, throwOnEndOfStream: false);
                    if (n == 0)
                    {
                        if (m != 0)
                        {
                            throw new IOException($"{name} in the ZIP file is longer than the original.");
                        }

                        break;
                    }

                    if (m != n || !a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n)))
                    {
                        throw new IOException($"{name} in the ZIP file doesn't match the original.");
                    }
                }
            }

            done++;
            if (done % 25 == 0 || done == files.Count)
            {
                progress?.Report((files.Count + done, steps));
            }
        }
    }

    /// <summary>Original file times from the index written with the ZIP. Empty if there is none.</summary>
    public Dictionary<string, long> ReadZipIndex()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!File.Exists(ZipIndexPath))
        {
            return result;
        }

        foreach (string line in File.ReadAllLines(ZipIndexPath))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ft))
            {
                result[parts[0]] = ft;
            }
        }

        return result;
    }

    /// <summary>
    /// Finishes what an interrupted compression or restore left behind. Never touches the only copy of an original:
    /// files/ is only removed when a checked files.zip is registered in session.json, and the other way round.
    /// </summary>
    public void TidyUp()
    {
        try
        {
            TryDelete(ZipPartialPath);
            if (IsZip && File.Exists(ZipPath))
            {
                if (Info.Status == "restored")
                {
                    // The originals are back in the package.
                    TryDelete(ZipPath);
                }
                else if (Directory.Exists(FilesDir))
                {
                    DeleteDirectory(FilesDir);
                }
            }
            else if (!IsZip && File.Exists(ZipPath) && BackedUpFiles().Any())
            {
                // Stopped after the ZIP was moved into place but before session.json said so. files/ is still complete.
                TryDelete(ZipPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again next time.
        }
    }

    private static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }

        Directory.Delete(dir, recursive: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not critical.
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
