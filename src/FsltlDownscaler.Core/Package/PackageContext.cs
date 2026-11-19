using System.Text.Json.Nodes;

namespace FsltlDownscaler.Package;

/// <summary>An FSLTL package folder that passed the basic checks.</summary>
public sealed class PackageContext
{
    public const string ExpectedTitle = "FSLTL Traffic Base";

    private PackageContext(string root, string selectedPath, string title, string version)
    {
        Root = root;
        SelectedPath = selectedPath;
        Title = title;
        Version = version;
    }

    /// <summary>The real package folder. Junctions and symbolic links are resolved.</summary>
    public string Root { get; }

    /// <summary>The folder as the user or the auto detection gave it. Can be a link.</summary>
    public string SelectedPath { get; }

    /// <summary>True when the selected folder (or one of its parents) is a junction or symbolic link.</summary>
    public bool IsLinked => !string.Equals(Root, SelectedPath, StringComparison.OrdinalIgnoreCase);
    public string Title { get; }
    public string Version { get; }
    public string ManifestPath => Path.Combine(Root, "manifest.json");
    public string LayoutPath => Path.Combine(Root, "layout.json");

    public static PackageContext Open(string path, bool allowOtherPackages = false)
    {
        string selected = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(selected))
        {
            throw new UserException($"Folder not found: {selected}");
        }

        string root = PathResolver.ResolveFinal(selected);
        if (!Directory.Exists(root))
        {
            throw new UserException($"The link {selected} points to a folder that doesn't exist: {root}");
        }

        string manifest = Path.Combine(root, "manifest.json");
        string layout = Path.Combine(root, "layout.json");
        if (!File.Exists(manifest))
        {
            throw new UserException("manifest.json not found. Please select the fsltl-traffic-base folder.");
        }

        if (!File.Exists(layout))
        {
            throw new UserException("layout.json not found. Please select the fsltl-traffic-base folder.");
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(manifest));
        string title = node?["title"]?.GetValue<string>() ?? string.Empty;
        string version = node?["package_version"]?.GetValue<string>() ?? "?";

        if (!allowOtherPackages && !string.Equals(title, ExpectedTitle, StringComparison.Ordinal))
        {
            throw new UserException($"This is not an FSLTL Traffic Base package (title: '{title}').");
        }

        return new PackageContext(root, selected, title, version);
    }

    public string Relative(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    public IEnumerable<string> EnumerateDdsFiles() =>
        Directory.EnumerateFiles(Root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            })
            .Where(f => f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
}

/// <summary>An error that is shown to the user as a plain message.</summary>
public sealed class UserException(string message) : Exception(message);
