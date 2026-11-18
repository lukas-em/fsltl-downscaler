using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FsltlDownscaler.Package;

/// <summary>
/// manifest.json is patched as text: only the digits of total_package_size change.
/// The zero-padded width is kept, so the file size of manifest.json stays the same.
/// </summary>
public static partial class ManifestFile
{
    [GeneratedRegex("(\"total_package_size\"\\s*:\\s*\")(\\d+)(\")")]
    private static partial Regex TotalRegex();

    public static long ReadTotal(string manifestText)
    {
        var matches = TotalRegex().Matches(manifestText);
        if (matches.Count != 1)
        {
            throw new UserException("manifest.json: total_package_size not found or ambiguous.");
        }

        return long.Parse(matches[0].Groups[2].Value, CultureInfo.InvariantCulture);
    }

    public static string WithTotal(string manifestText, long newTotal)
    {
        var matches = TotalRegex().Matches(manifestText);
        if (matches.Count != 1)
        {
            throw new UserException("manifest.json: total_package_size not found or ambiguous.");
        }

        if (newTotal < 0)
        {
            throw new InvalidOperationException("Negative package size.");
        }

        int width = matches[0].Groups[2].Value.Length;
        string digits = newTotal.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        var m = matches[0];
        var sb = new StringBuilder(manifestText.Length);
        sb.Append(manifestText, 0, m.Groups[2].Index);
        sb.Append(digits);
        sb.Append(manifestText, m.Groups[2].Index + m.Groups[2].Length, manifestText.Length - (m.Groups[2].Index + m.Groups[2].Length));
        return sb.ToString();
    }
}
