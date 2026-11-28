using System.Globalization;
using FsltlDownscaler.Package;
using FsltlDownscaler.Pipeline;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler;

internal static class Program
{
    private static readonly int[] AllowedTargets = [2048, 1024, 512];

    private const string Usage = """
        FSLTL Downscaler (prototype)

        Usage:
          FsltlDownscaler analyze <package> [--target 1024] [--no-mips]
          FsltlDownscaler run      <package> --target <2048|1024|512> [--backup folder|zip|none] [--backup-dir <dir>] [--no-mips] [--yes]
          FsltlDownscaler restore  <package> [--backup-dir <dir>] [--force]
          FsltlDownscaler compress <package> [--backup-dir <dir>]
          FsltlDownscaler repair   <package> [--backup-dir <dir>]
          FsltlDownscaler verify   <package>

        <package>     The fsltl-traffic-base folder.
        --target      Maximum texture size (long side). Default 1024.
        --no-mips     Do not generate mip maps for textures that have none.
        --backup      folder (default): originals are moved. zip: moved, then packed into files.zip.
                      none: originals are deleted, no restore possible.
        --backup-dir  Where backups go. Default: <folder above Community>\FSLTL-Downscaler-Backup (links are resolved)
        --yes         Do not ask for confirmation.
        --force       Restore even though layout.json changed after the run.
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2 || args[0] is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            var opts = Options.Parse(args.Skip(2).ToArray());
            var pkg = PackageContext.Open(args[1]);
            Console.WriteLine($"Package: {pkg.Title} {pkg.Version}");
            Console.WriteLine($"Folder:  {pkg.Root}");
            if (pkg.IsLinked)
            {
                Console.WriteLine($"Link:    {pkg.SelectedPath}");
            }
            Console.WriteLine();

            return args[0].ToLowerInvariant() switch
            {
                "analyze" => Analyze(pkg, opts),
                "run" => Run(pkg, opts),
                "restore" => Restore(pkg, opts),
                "compress" => Compress(pkg, opts),
                "repair" => Repair(pkg, opts),
                "verify" => Verify(pkg),
                _ => throw new UserException($"Unknown command '{args[0]}'.{Environment.NewLine}{Environment.NewLine}{Usage}"),
            };
        }
        catch (UserException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex}");
            return 3;
        }
    }

    private static int Analyze(PackageContext pkg, Options o)
    {
        var plan = BuildPlan(pkg, o);
        PrintPlan(plan);
        PrintSessions(pkg, o);
        return 0;
    }

    private static int Run(PackageContext pkg, Options o)
    {
        if (o.Target is null)
        {
            throw new UserException("Please choose --target 2048, 1024 or 512.");
        }

        SystemChecks.EnsureSimulatorNotRunning();

        string backupBase = o.BackupDir ?? BackupSession.DefaultBaseDir(pkg.Root);
        var sessions = Sessions(pkg, o);
        var unfinished = sessions.FirstOrDefault(s => s.Info.Status is "running" or "incomplete");
        if (unfinished is not null)
        {
            throw new UserException($"An unfinished run was found ({unfinished.Dir}). Run '{(unfinished.IsNoBackup ? "repair" : "restore")}' first.");
        }

        var plan = BuildPlan(pkg, o);
        PrintPlan(plan);

        if (!plan.ToProcess.Any())
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        var (required, available) = SystemChecks.CheckSpace(pkg.Root, backupBase, plan, o.Mode);
        Console.WriteLine($"Disk space needed: {Gb(required)}, available: {Gb(available)}");
        if (available < required)
        {
            throw new UserException("Not enough free disk space.");
        }

        Console.WriteLine($"Backup base folder: {backupBase}, mode: {o.Mode}");
        if (!o.Yes)
        {
            Console.Write("Type YES to start: ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.Ordinal))
            {
                Console.WriteLine("Aborted. Nothing was changed.");
                return 1;
            }
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var result = Runner.Execute(pkg, plan, backupBase, Console.WriteLine, cts.Token, null, o.Mode);
        if (o.Mode == BackupMode.Zip && result.Replaced > 0 && result.Status is "completed" or "cancelled" or "failed"
            && BackupSession.TryLoad(result.SessionDir) is { } done)
        {
            long zipBytes = done.CompressToZip(Console.WriteLine, null, CancellationToken.None);
            Console.WriteLine($"Backup compressed: {Gb(done.Info.BytesBefore)} -> {Gb(zipBytes)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Status:   {result.Status}");
        Console.WriteLine($"Replaced: {result.Replaced} textures, {Gb(result.BytesBefore)} -> {Gb(result.BytesAfter)}");
        Console.WriteLine($"Errors:   {result.Failed}");
        foreach (var e in result.Errors.Take(20))
        {
            Console.WriteLine($"  {e}");
        }

        Console.WriteLine($"Backup:   {result.SessionDir}");
        Console.WriteLine();
        return Verify(pkg) == 0 && result.Failed == 0 && result.Status == "completed" ? 0 : 4;
    }

    private static int Restore(PackageContext pkg, Options o)
    {
        SystemChecks.EnsureSimulatorNotRunning();
        var session = Sessions(pkg, o).FirstOrDefault(s => s.Info.Status != "restored")
            ?? throw new UserException("No backup to restore found.");

        Console.WriteLine($"Restoring from {session.Dir} (status {session.Info.Status}, {session.Info.FilesReplaced} files)");
        if (!o.Yes)
        {
            Console.Write("Type YES to restore: ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.Ordinal))
            {
                Console.WriteLine("Aborted. Nothing was changed.");
                return 1;
            }
        }

        int n = Restorer.Restore(pkg, session, o.Force, Console.WriteLine);
        Console.WriteLine($"Restored {n} files plus layout.json and manifest.json.");
        Console.WriteLine();
        return Verify(pkg);
    }

    private static int Compress(PackageContext pkg, Options o)
    {
        int n = 0;
        foreach (var s in Sessions(pkg, o).Where(s => !s.IsNoBackup && !s.IsZip && s.Info.Status is "completed" or "cancelled" or "failed"))
        {
            long zipBytes = s.CompressToZip(Console.WriteLine, null, CancellationToken.None);
            Console.WriteLine($"{Path.GetFileName(s.Dir)}: {Gb(s.Info.BytesBefore)} -> {Gb(zipBytes)}");
            n++;
        }

        Console.WriteLine(n == 0 ? "No backup to compress found." : $"{n} backup(s) compressed.");
        return 0;
    }

    private static int Repair(PackageContext pkg, Options o)
    {
        SystemChecks.EnsureSimulatorNotRunning();
        var session = Sessions(pkg, o).FirstOrDefault(s => s.Info.Status is "running" or "incomplete" && s.IsNoBackup)
            ?? throw new UserException("No interrupted run without backup found.");
        Repairer.Repair(pkg, session, Console.WriteLine);
        Console.WriteLine();
        return Verify(pkg);
    }

    private static int Verify(PackageContext pkg)
    {
        var r = Verifier.Run(pkg);
        Console.WriteLine("Verification");
        Console.WriteLine($"  layout.json entries:        {r.Entries}");
        Console.WriteLine($"  DDS entries:                {r.DdsEntries}");
        Console.WriteLine($"  DDS size mismatches:        {r.DdsMismatches}");
        Console.WriteLine($"  Other size mismatches:      {r.OtherMismatches} (already present in the FSLTL original, not touched)");
        Console.WriteLine($"  Entries without file:       {r.MissingFiles}");
        string mode = r.TotalMatchesEntries ? "matches layout sum"
            : r.TotalMatchesEntriesPlusMeta ? "matches layout sum + layout.json + manifest.json"
            : "does NOT match";
        Console.WriteLine($"  total_package_size:         {r.ManifestTotal} ({mode})");
        foreach (var s in r.DdsMismatchSamples)
        {
            Console.WriteLine($"    {s}");
        }

        bool ok = r.DdsMismatches == 0 && (r.TotalMatchesEntries || r.TotalMatchesEntriesPlusMeta);
        Console.WriteLine(ok ? "  Result: OK" : "  Result: PROBLEMS FOUND");
        return ok ? 0 : 5;
    }

    private static Plan BuildPlan(PackageContext pkg, Options o)
    {
        int target = o.Target ?? 1024;
        Console.WriteLine($"Analysing textures for target {target} ...");
        var layout = LayoutFile.Load(pkg.LayoutPath);
        var progress = new Progress<int>(n => Console.WriteLine($"  {n} files read"));
        return Planner.Build(pkg, layout, target, !o.NoMips, progress);
    }

    private static void PrintPlan(Plan plan)
    {
        Console.WriteLine();
        Console.WriteLine($"Textures found:            {plan.Items.Count}");
        Console.WriteLine($"  Path A (drop mip levels): {plan.Count(PlanAction.StripMips)}");
        Console.WriteLine($"  Path B (re-encode):       {plan.Count(PlanAction.Reencode)}{(plan.GenerateMips ? " (with new mip maps)" : string.Empty)}");
        Console.WriteLine($"  Already small enough:     {plan.Count(PlanAction.Keep)}");
        Console.WriteLine($"  Skipped:                  {plan.Count(PlanAction.Skip)}");
        Console.WriteLine($"  Not listed in layout.json:{plan.Items.Count(i => !i.InLayout),5}");
        Console.WriteLine($"Texture size before:       {Gb(plan.SizeBefore)}");
        Console.WriteLine($"Texture size after:        {Gb(plan.SizeAfter)}");
        Console.WriteLine($"Saving:                    {Gb(plan.SizeBefore - plan.SizeAfter)}");

        var skipped = plan.Items.Where(i => i.Action == PlanAction.Skip).ToList();
        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Skipped textures:");
            foreach (var g in skipped.GroupBy(i => i.Note ?? "?"))
            {
                Console.WriteLine($"  {g.Count(),5} x {g.Key}  (e.g. {g.First().RelPath})");
            }
        }

        var corrected = plan.ToProcess.Count(i => i.Note is not null);
        if (corrected > 0)
        {
            Console.WriteLine($"Note: {corrected} textures contain more mip levels than their header declares. This is handled.");
        }

        Console.WriteLine();
    }

    private static void PrintSessions(PackageContext pkg, Options o)
    {
        var sessions = Sessions(pkg, o);
        if (sessions.Count == 0)
        {
            Console.WriteLine("No previous runs found.");
            return;
        }

        Console.WriteLine("Previous runs:");
        foreach (var s in sessions)
        {
            Console.WriteLine($"  {Path.GetFileName(s.Dir)}: target {s.Info.Target}, status {s.Info.Status}, {s.Info.FilesReplaced} files, backup {s.Info.BackupFormat}");
        }
    }

    /// <summary>Backups of this package, newest first. With --backup-dir only that folder is searched.</summary>
    private static List<BackupSession> Sessions(PackageContext pkg, Options o) =>
        o.BackupDir is { } dir
            ? BackupSession.FindAll(dir, pkg.Root).OrderByDescending(s => s.Info.StartedUtc).ToList()
            : BackupSession.FindForPackage(pkg);

    private static string Gb(long bytes) => (bytes / 1_000_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";

    private sealed class Options
    {
        public int? Target { get; private set; }
        public string? BackupDir { get; private set; }
        public bool NoMips { get; private set; }
        public bool Yes { get; private set; }
        public bool Force { get; private set; }
        public BackupMode Mode { get; private set; } = BackupMode.Folder;

        public static Options Parse(string[] a)
        {
            var o = new Options();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--target":
                        if (i + 1 >= a.Length || !int.TryParse(a[++i], out int t) || !AllowedTargets.Contains(t))
                        {
                            throw new UserException("--target must be 2048, 1024 or 512.");
                        }

                        o.Target = t;
                        break;
                    case "--backup-dir":
                        if (i + 1 >= a.Length)
                        {
                            throw new UserException("--backup-dir needs a folder.");
                        }

                        o.BackupDir = a[++i];
                        break;
                    case "--backup":
                        o.Mode = i + 1 < a.Length ? a[++i].ToLowerInvariant() switch
                        {
                            "folder" => BackupMode.Folder,
                            "zip" => BackupMode.Zip,
                            "none" => BackupMode.None,
                            _ => throw new UserException("--backup must be folder, zip or none."),
                        } : throw new UserException("--backup must be folder, zip or none.");
                        break;
                    case "--no-mips":
                        o.NoMips = true;
                        break;
                    case "--yes":
                        o.Yes = true;
                        break;
                    case "--force":
                        o.Force = true;
                        break;
                    default:
                        throw new UserException($"Unknown option '{a[i]}'.");
                }
            }

            return o;
        }
    }
}
