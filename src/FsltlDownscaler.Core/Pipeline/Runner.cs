using System.Collections.Concurrent;
using FsltlDownscaler.Dds;
using FsltlDownscaler.Package;
using FsltlDownscaler.Processing;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

public sealed record RunResult(int Replaced, int Failed, long BytesBefore, long BytesAfter, string SessionDir, string Status, List<string> Errors);

/// <summary>Executes a plan: replaces textures, then updates layout.json and manifest.json once.</summary>
public static class Runner
{
    private sealed record Done(string RelPath, long OldLayoutSize, long NewSize, long FileTime, bool InLayout);

    public static RunResult Execute(PackageContext pkg, Plan plan, string backupBaseDir, Action<string> log, CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null)
    {
        var layout = LayoutFile.Load(pkg.LayoutPath);
        var (manifestText, manifestBom) = TextFiles.ReadUtf8(pkg.ManifestPath);
        long oldTotal = ManifestFile.ReadTotal(manifestText);
        long oldLayoutBytes = new FileInfo(pkg.LayoutPath).Length;
        long oldManifestBytes = new FileInfo(pkg.ManifestPath).Length;
        long entrySum = layout.SumOfSizes();
        bool totalIncludesMeta = oldTotal == entrySum + oldLayoutBytes + oldManifestBytes;

        var session = BackupSession.Create(backupBaseDir, pkg, plan.Target, plan.GenerateMips);
        log($"Backup folder: {session.Dir}");

        var done = new ConcurrentBag<Done>();
        var errors = new ConcurrentQueue<string>();
        string status = "completed";
        int processed = 0;
        int total = plan.ToProcess.Count();

        void ProcessOne(TexturePlan item)
        {
            try
            {
                var fi = new FileInfo(item.FullPath);
                if (fi.Length != item.OldSize)
                {
                    throw new IOException("File changed since analysis.");
                }

                byte[] original = File.ReadAllBytes(item.FullPath);
                var tex = DdsTexture.Parse(original, original.LongLength);
                byte[] result = item.Action == PlanAction.StripMips
                    ? MipStripper.Strip(tex, original, plan.Target)
                    : Reencoder.Reencode(tex, original, plan.Target, plan.GenerateMips);

                Validate(result, plan.Target);

                long oldLayoutSize = item.OldSize;
                if (item.InLayout && layout.TryGetSize(item.RelPath, out long s))
                {
                    oldLayoutSize = s;
                }

                session.ReplaceFile(pkg, item.FullPath, result);
                long fileTime = new FileInfo(item.FullPath).LastWriteTimeUtc.ToFileTimeUtc();
                done.Add(new Done(item.RelPath, oldLayoutSize, result.LongLength, fileTime, item.InLayout));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Enqueue($"{item.RelPath}: {ex.Message}");
                session.Journal($"error\t{item.RelPath}\t{ex.Message}");
            }

            int n = Interlocked.Increment(ref processed);
            if (n % 10 == 0 || n == total)
            {
                progress?.Report((n, total));
            }

            if (n % 100 == 0 || n == total)
            {
                log($"  {n}/{total} textures processed");
            }
        }

        try
        {
            // Path A is I/O bound, many in parallel.
            Parallel.ForEach(
                plan.ToProcess.Where(i => i.Action == PlanAction.StripMips),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = ct },
                ProcessOne);

            // Path B is CPU and memory heavy (8K textures decode to 256 MB). Few at a time.
            Parallel.ForEach(
                plan.ToProcess.Where(i => i.Action == PlanAction.Reencode).OrderByDescending(i => i.OldSize),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 4), CancellationToken = ct },
                ProcessOne);
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            log("Cancelled. Finishing layout.json for the files already replaced ...");
        }
        catch (Exception ex)
        {
            status = "failed";
            errors.Enqueue($"Unexpected error: {ex.Message}");
            log($"Unexpected error: {ex.Message}. Finishing layout.json for the files already replaced ...");
        }
        finally
        {
            // Always bring layout.json and manifest.json in line with what is on disk.
            long delta = 0;
            foreach (var d in done.Where(d => d.InLayout))
            {
                layout.Update(d.RelPath, d.NewSize, d.FileTime);
                delta += d.NewSize - d.OldLayoutSize;
            }

            byte[] newLayout = layout.Serialize();
            long newTotal = oldTotal + delta;
            if (totalIncludesMeta)
            {
                newTotal += newLayout.LongLength - oldLayoutBytes;
            }

            byte[] newManifest = TextFiles.EncodeUtf8(ManifestFile.WithTotal(manifestText, newTotal), manifestBom);
            session.WriteMetaFile(pkg.LayoutPath, newLayout);
            session.WriteMetaFile(pkg.ManifestPath, newManifest);

            session.Info.Status = status;
            session.Info.FinishedUtc = DateTime.UtcNow;
            session.Info.FilesReplaced = done.Count;
            session.Info.BytesBefore = done.Sum(d => d.OldLayoutSize);
            session.Info.BytesAfter = done.Sum(d => d.NewSize);
            session.Info.FinalLayoutSha256 = BackupSession.Sha256(pkg.LayoutPath);
            session.Save();
            session.CleanupTmp();
        }

        return new RunResult(done.Count, errors.Count, session.Info.BytesBefore, session.Info.BytesAfter,
            session.Dir, status, errors.ToList());
    }

    /// <summary>The new file must parse cleanly, be within target and have exactly the expected data length.</summary>
    private static void Validate(byte[] data, int target)
    {
        var tex = DdsTexture.Parse(data, data.LongLength);
        if (!tex.IsSupported)
        {
            throw new InvalidOperationException($"Result failed validation: {tex.UnsupportedReason}");
        }

        if (tex.MipCountCorrected)
        {
            throw new InvalidOperationException("Result failed validation: mip count mismatch.");
        }

        if (tex.LongSide > target)
        {
            throw new InvalidOperationException("Result failed validation: still larger than target.");
        }
    }
}
