using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsltlDownscaler.Package;

/// <summary>
/// layout.json with minimal changes: only size and date of touched entries are updated.
/// Order and all other values stay as they are.
/// </summary>
public sealed class LayoutFile
{
    private readonly JsonNode _root;
    private readonly Dictionary<string, JsonObject> _byPath;

    private LayoutFile(JsonNode root, JsonArray content)
    {
        _root = root;
        _byPath = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in content)
        {
            if (item is JsonObject obj && obj["path"]?.GetValue<string>() is { } p)
            {
                _byPath.TryAdd(Normalize(p), obj);
            }
        }

        Count = content.Count;
    }

    public int Count { get; }

    public bool HasBom { get; private init; }

    /// <summary>Line break of the original file ("\n" for FSLTL).</summary>
    public string NewLine { get; private init; } = "\n";

    /// <summary>Indentation width of the original file (4 for FSLTL).</summary>
    public int IndentSize { get; private init; } = 4;

    public bool EndsWithNewLine { get; private init; }

    public static LayoutFile Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = TextFiles.HasUtf8Bom(bytes);
        var body = bom ? bytes.AsSpan(3) : bytes.AsSpan();
        JsonNode root = JsonNode.Parse(body)
            ?? throw new UserException("layout.json is empty.");
        if (root["content"] is not JsonArray content)
        {
            throw new UserException("layout.json has no 'content' list.");
        }

        var (newLine, indent) = DetectFormatting(body);
        return new LayoutFile(root, content)
        {
            HasBom = bom,
            NewLine = newLine,
            IndentSize = indent,
            EndsWithNewLine = body.Length > 0 && body[^1] == (byte)'\n',
        };
    }

    /// <summary>Reads line break and indentation from the first lines, so the rewritten file looks like the original.</summary>
    private static (string NewLine, int Indent) DetectFormatting(ReadOnlySpan<byte> body)
    {
        int lf = body.IndexOf((byte)'\n');
        if (lf < 0)
        {
            return ("\n", 4);
        }

        string newLine = lf > 0 && body[lf - 1] == (byte)'\r' ? "\r\n" : "\n";
        int spaces = 0;
        for (int i = lf + 1; i < body.Length && body[i] == (byte)' '; i++)
        {
            spaces++;
        }

        int indent = spaces is >= 1 and <= 8 ? spaces : 4;
        return (newLine, indent);
    }

    public static string Normalize(string relPath) => relPath.Replace('\\', '/').TrimStart('/');

    public IEnumerable<(string Path, long Size)> Entries =>
        _byPath.Values.Select(o => (o["path"]!.GetValue<string>(), o["size"]!.GetValue<long>()));

    public long SumOfSizes() => Entries.Sum(e => e.Size);

    public bool TryGetSize(string relPath, out long size)
    {
        if (_byPath.TryGetValue(Normalize(relPath), out var obj))
        {
            size = obj["size"]!.GetValue<long>();
            return true;
        }

        size = 0;
        return false;
    }

    public bool Contains(string relPath) => _byPath.ContainsKey(Normalize(relPath));

    public void Update(string relPath, long size, long fileTimeUtc)
    {
        if (!_byPath.TryGetValue(Normalize(relPath), out var obj))
        {
            throw new InvalidOperationException($"No layout entry for {relPath}.");
        }

        obj["size"] = JsonValue.Create(size);
        obj["date"] = JsonValue.Create(fileTimeUtc);
    }

    public byte[] Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = IndentSize,
            NewLine = NewLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(_root, options);
        if (EndsWithNewLine)
        {
            json = [.. json, .. System.Text.Encoding.UTF8.GetBytes(NewLine)];
        }

        return HasBom ? [.. TextFiles.Utf8Bom, .. json] : json;
    }
}

public static class TextFiles
{
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    public static (string Text, bool Bom) ReadUtf8(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = HasUtf8Bom(bytes);
        string text = new System.Text.UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        return (text, bom);
    }

    public static byte[] EncodeUtf8(string text, bool bom)
    {
        byte[] body = new System.Text.UTF8Encoding(false).GetBytes(text);
        return bom ? [.. Utf8Bom, .. body] : body;
    }
}
