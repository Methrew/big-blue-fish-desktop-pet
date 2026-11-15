// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BigBlueFish.Windows;

// The input box's attachment rules, one copy for the Windows pet (CompanionMcpAttachments.cs), the macOS and Linux pet
// (LinuxMcpAttachments.cs) and the Hermes bridge, so the sides cannot drift apart: which files count as images or videos,
// how many go with one message, and how long pasted screenshots stay. user_message names each file by its local path only,
// as {path, name, kind, mimeType, size, source}; the bytes never travel on the MCP pipe.
internal static class CompanionAttachmentRules
{
    internal const int Limit = 10;
    // Agents usually receive images as data URLs, a third larger than the file.
    internal const long LargeImage = 20L << 20;
    // Sent screenshots stay a week so the agent can look back at them; the folder is capped near 500 MB, oldest first.
    internal static readonly TimeSpan PasteLifetime = TimeSpan.FromDays(7);
    internal const long PasteFolderLimit = 500L << 20;

    private static readonly Dictionary<string, (string Kind, string Mime)> Types = new(StringComparer.OrdinalIgnoreCase) {
        [".png"] = ("image", "image/png"), [".jpg"] = ("image", "image/jpeg"), [".jpeg"] = ("image", "image/jpeg"), [".gif"] = ("image", "image/gif"),
        [".webp"] = ("image", "image/webp"), [".bmp"] = ("image", "image/bmp"), [".tif"] = ("image", "image/tiff"), [".tiff"] = ("image", "image/tiff"),
        [".heic"] = ("image", "image/heic"), [".heif"] = ("image", "image/heif"),
        [".mp4"] = ("video", "video/mp4"), [".m4v"] = ("video", "video/x-m4v"), [".mov"] = ("video", "video/quicktime"), [".mkv"] = ("video", "video/x-matroska"),
        [".webm"] = ("video", "video/webm"), [".avi"] = ("video", "video/x-msvideo"), [".wmv"] = ("video", "video/x-ms-wmv"), [".mpg"] = ("video", "video/mpeg"),
        [".mpeg"] = ("video", "video/mpeg"), [".3gp"] = ("video", "video/3gpp"),
        [".pdf"] = ("file", "application/pdf"), [".txt"] = ("file", "text/plain"), [".md"] = ("file", "text/markdown"), [".csv"] = ("file", "text/csv"),
        [".json"] = ("file", "application/json"), [".zip"] = ("file", "application/zip"),
        [".docx"] = ("file", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        [".xlsx"] = ("file", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        [".pptx"] = ("file", "application/vnd.openxmlformats-officedocument.presentationml.presentation") };

    // By the extension; anything else is a file of unknown type.
    internal static (string Kind, string Mime) Type(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out var type) ? type : ("file", "application/octet-stream");

    internal static string KindOf(string mime) =>
        mime.StartsWith("image/", StringComparison.Ordinal) ? "image" : mime.StartsWith("video/", StringComparison.Ordinal) ? "video" : "file";

    // The extensions of one kind, with their dots, for the file pickers' filters.
    internal static string[] Extensions(string kind) => Types.Where(t => t.Value.Kind == kind).Select(t => t.Key).ToArray();

    // How paths are told apart: Windows and the default macOS volumes ignore case, Linux file systems do not.
    internal static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // A pasted screenshot's own name in the attachments folder; only paste-* files are ever swept or deleted.
    internal static string PasteName(DateTime now) => $"paste-{now:yyyyMMdd-HHmmss-fff}-{Random.Shared.Next(0x10000):x4}.png";

    // Only the pet's own pasted files are ever deleted, never the user's originals.
    internal static bool Owns(string folder, string path) =>
        Path.GetFileName(path).StartsWith("paste-", StringComparison.Ordinal)
        && path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);

    // Drops sent screenshots older than a week, half-written ones older than an hour, and the oldest beyond the size cap.
    // Synchronous: callers run it in the background.
    internal static void Sweep(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            long kept = 0; DateTime now = DateTime.UtcNow;
            foreach (var file in new DirectoryInfo(folder).GetFiles("paste-*").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                bool stale = file.LastWriteTimeUtc < now - PasteLifetime || file.Extension == ".tmp" && file.LastWriteTimeUtc < now.AddHours(-1);
                if (!stale && kept + file.Length <= PasteFolderLimit) { kept += file.Length; continue; }
                try { file.Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static string FormatSize(long bytes) => bytes switch {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB", >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB", _ => $"{bytes} B" };
}
