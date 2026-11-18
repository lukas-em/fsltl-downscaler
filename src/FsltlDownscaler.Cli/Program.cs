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
          FsltlDownscaler run     <package> --target <2048|1024|512> [--backup-dir <dir>] [--no-mips] [--yes]
          FsltlDownscaler restore <package> [--backup-dir <dir>] [--force]
          FsltlDownscaler verify  <package>

        <package>     The fsltl-traffic-base folder.
        --target      Maximum texture size (long side). Default 1024.
        --no-mips     Do not generate mip maps for textures that have none.
        --backup-dir  Where backups go. Default: <folder above Community>\FSLTL-Downscaler-Backup
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
            Console.WriteLine();

            return args[0].ToLowerInvariant() switch
            {
                "analyze" => Analyze(pkg, opts),
                "run" => Run(pkg, opts),
                "restore" => Restore(pkg, opts),
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
        var sessions = BackupSession.FindAll(backupBase, pkg.Root).ToList();
        var unfinished = sessions.FirstOrDefault(s => s.Info.Status is "running");
        if (unfinished is not null)
        {
            throw new UserException($"An unfinished run was found ({unfinished.Dir}). Run 'restore' first.");
        }

        var plan = BuildPlan(pkg, o);
        PrintPlan(plan);

        if (!plan.ToProcess.Any())
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        var (required, available) = SystemChecks.CheckSpace(pkg.Root, backupBase, plan);
        Console.WriteLine($"Disk space needed: {Gb(required)}, available: {Gb(available)}");
        if (available < required)
        {
            throw new UserException("Not enough free disk space.");
        }

        Console.WriteLine($"Backup base folder: {backupBase}");
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

        var result = Runner.Execute(pkg, plan, backupBase, Console.WriteLine, cts.Token);

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
        string backupBase = o.BackupDir ?? BackupSession.DefaultBaseDir(pkg.Root);
        var session = BackupSession.FindAll(backupBase, pkg.Root).FirstOrDefault(s => s.Info.Status != "restored")
            ?? throw new UserException($"No backup to restore found in {backupBase}.");

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
        string backupBase = o.BackupDir ?? BackupSession.DefaultBaseDir(pkg.Root);
        var sessions = BackupSession.FindAll(backupBase, pkg.Root).ToList();
        if (sessions.Count == 0)
        {
            Console.WriteLine($"No previous runs found in {backupBase}.");
            return;
        }

        Console.WriteLine("Previous runs:");
        foreach (var s in sessions)
        {
            Console.WriteLine($"  {Path.GetFileName(s.Dir)}: target {s.Info.Target}, status {s.Info.Status}, {s.Info.FilesReplaced} files");
        }
    }

    private static string Gb(long bytes) => (bytes / 1_000_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";

    private sealed class Options
    {
        public int? Target { get; private set; }
        public string? BackupDir { get; private set; }
        public bool NoMips { get; private set; }
        public bool Yes { get; private set; }
        public bool Force { get; private set; }

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
