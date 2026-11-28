namespace FsltlDownscaler.Safety;

/// <summary>What happens to the original textures during a run.</summary>
public enum BackupMode
{
    /// <summary>Originals are moved to the backup folder. Fast, but they take as much space as before.</summary>
    Folder,

    /// <summary>Like <see cref="Folder"/>, then the originals are packed into files.zip (FSLTL: about a fifth of the size).</summary>
    Zip,

    /// <summary>Originals are deleted. "Restore original" is not possible, only a reinstall of FSLTL.</summary>
    None,
}

/// <summary>Values of <see cref="SessionInfo.BackupFormat"/>.</summary>
public static class BackupFormats
{
    public const string Folder = "folder";
    public const string Zip = "zip";
    public const string None = "none";
}
