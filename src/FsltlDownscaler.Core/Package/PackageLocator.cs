using System.Text.RegularExpressions;

namespace FsltlDownscaler.Package;

/// <summary>
/// Finds the fsltl-traffic-base folder via the MSFS 2024 UserCfg.opt (InstalledPackagesPath).
/// Only a suggestion. The user can always pick another folder.
/// </summary>
public static partial class PackageLocator
{
    public const string FolderName = "fsltl-traffic-base";

    [GeneratedRegex("^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex InstalledPathRegex();

    public static IReadOnlyList<string> FindCandidates()
    {
        var result = new List<string>();
        foreach (string cfg in UserCfgLocations())
        {
            try
            {
                if (!File.Exists(cfg))
                {
                    continue;
                }

                var match = InstalledPathRegex().Match(File.ReadAllText(cfg));
                if (match.Success)
                {
                    string packages = match.Groups[1].Value;
                    AddIfPackage(result, Path.Combine(packages, "Community", FolderName));
                    AddIfPackage(result, Path.Combine(packages, "Community2024", FolderName));
                }

                // Steam default when InstalledPackagesPath is not set.
                AddIfPackage(result, Path.Combine(Path.GetDirectoryName(cfg)!, "Packages", "Community", FolderName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort only.
            }
        }

        return result;
    }

    private static IEnumerable<string> UserCfgLocations()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt");
        yield return Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
    }

    private static void AddIfPackage(List<string> list, string path)
    {
        if (File.Exists(Path.Combine(path, "manifest.json"))
            && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
        }
    }
}
