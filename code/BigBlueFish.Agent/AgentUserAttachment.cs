// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Text;
using System.Text.Json.Serialization;

namespace BigBlueFish.Agent;

/// <summary>
/// A file the user attached in the input box, by its absolute local path: the entries of user_message.attachments, the same
/// on the Agent connection and on the MCP interface. Kind is image, video or file; Source is picker, drop, clipboard-files
/// (copied files, kept at their own paths) or clipboard (a pasted picture the pet saved as PNG).
/// </summary>
public sealed record AgentUserAttachment(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("source")] string Source)
{
    public const int Limit = 10;
    /// <summary>Long edge of a picture a host makes for an image block; the models scale larger ones down anyway.</summary>
    public const int ImageEdge = 1568;
    /// <summary>Largest image block: its base64 stays under 5 MB, which several model services refuse beyond.</summary>
    public const long ImageBytes = 3_750_000;
    /// <summary>All image blocks of one message together, so ten pictures never make a prompt line of tens of megabytes.</summary>
    public const long ImageBudget = 12_000_000;

    /// <summary>A whole local path and plain names: a control character could forge a line of the prompt.</summary>
    [JsonIgnore]
    public bool Valid => Path.Length > 0 && System.IO.Path.IsPathFullyQualified(Path) && !Path.Any(char.IsControl)
        && Name.Length > 0 && !Name.Any(char.IsControl) && Kind is "image" or "video" or "file" && !MimeType.Any(char.IsControl) && !Source.Any(char.IsControl);

    /// <summary>The file URL of the path (RFC 8089), each segment percent-encoded, so spaces and Chinese names survive.</summary>
    [JsonIgnore]
    public string FileUri => ToFileUri(Path);

    public static string ToFileUri(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        var uri = new StringBuilder("file://");
        IEnumerable<string> segments;
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share\file -> file://server/share/file
            var parts = full[2..].Split('\\', '/');
            uri.Append(Uri.EscapeDataString(parts[0]));
            segments = parts.Skip(1);
        }
        else if (full.Length >= 2 && full[1] == ':' && char.IsAsciiLetter(full[0]))
        {
            uri.Append('/').Append(full[0]).Append(':');
            segments = full[2..].Split('\\', '/').Skip(1);
        }
        else segments = full.Split('/').Skip(1);
        foreach (var segment in segments) uri.Append('/').Append(Uri.EscapeDataString(segment));
        return uri.ToString();
    }

    /// <summary>The ACP resource_link block every Agent must accept: the Agent reads the file itself, with the user's permissions.</summary>
    internal object ResourceLink() => Size >= 0
        ? new { type = "resource_link", uri = FileUri, name = Name, mimeType = MimeType, size = Size }
        : new { type = "resource_link", uri = FileUri, name = Name, mimeType = MimeType };

    /// <summary>
    /// The file's own bytes as an image block when it is a PNG, JPEG, GIF or WebP (told by its first bytes, whatever the
    /// extension says) of at most <paramref name="limit"/> bytes; otherwise null, and the Agent gets the link alone.
    /// Hosts that can decode pictures shrink larger ones themselves (AgentCoordinator.AttachmentImage).
    /// </summary>
    public static AgentPromptImage? ReadAsIs(string path, long limit = ImageBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0 || stream.Length > limit) return null;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return ImageMime(bytes) is { } mime ? new(mime, Convert.ToBase64String(bytes)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>The image types model services take as they are, from the signature.</summary>
    public static string? ImageMime(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return "image/gif";
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}

/// <summary>The user_message event: exactly { text } without attachments, as older clients expect.</summary>
internal sealed record AgentUserMessage(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("attachments"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentUserAttachment[]? Attachments);
