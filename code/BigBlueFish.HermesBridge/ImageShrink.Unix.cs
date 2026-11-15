// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
#if !WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BigBlueFish.HermesBridge;

// The macOS and Linux counterpart of ImageShrink.Windows.cs: re-encodes an image as JPEG, smaller step by step, until it
// fits a byte budget. macOS has sips (ImageIO, which also reads HEIC and TIFF from an iPhone or a screenshot); Linux has
// no image tool the bridge can count on, so an image too big stays out of the message and Hermes gets only its path.
internal static partial class ImageShrink
{
    // Long edge in pixels (0 = as it is) and JPEG quality, tried in order; the same table as on Windows.
    private static readonly (int Edge, int Quality)[] Steps = [(0, 90), (2560, 90), (1920, 85), (1280, 80), (960, 75), (640, 70)];
    private const string Sips = "/usr/bin/sips";
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(30);

    // Null when the bytes are not an image sips can read, no step fits, or there is no sips (Linux).
    internal static async Task<byte[]?> ToJpegAsync(byte[] source, long budget, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(Sips)) return null;
        // A folder only this user can read (CreateTempSubdirectory makes it 0700), removed again whatever happens.
        var folder = Directory.CreateTempSubdirectory("bbf-shrink");
        try
        {
            string input = Path.Combine(folder.FullName, "source" + Extension(source));
            await File.WriteAllBytesAsync(input, source, token);
            if (await RunAsync(token, "-g", "pixelWidth", "-g", "pixelHeight", input) is not { } size
                || Dimension().Matches(size) is not { Count: 2 } found) return null;
            int longEdge = found.Max(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            string output = Path.Combine(folder.FullName, "shrunk.jpg");
            foreach (var (edge, quality) in Steps)
            {
                token.ThrowIfCancellationRequested();
                if (edge != 0 && edge >= longEdge) continue;
                File.Delete(output);
                var arguments = new List<string> { "-s", "format", "jpeg", "-s", "formatOptions", quality.ToString(CultureInfo.InvariantCulture) };
                if (edge != 0) arguments.AddRange(["-Z", edge.ToString(CultureInfo.InvariantCulture)]);
                arguments.AddRange([input, "--out", output]);
                if (await RunAsync(token, [.. arguments]) == null || !File.Exists(output)) return null;
                if (new FileInfo(output).Length <= budget) return await File.ReadAllBytesAsync(output, token);
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or FormatException or OverflowException) { return null; }
        finally
        {
            try { folder.Delete(true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    // sips' standard output, or null when it failed or took too long. No shell is involved: every argument goes as it is.
    private static async Task<string?> RunAsync(CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(Sips) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process == null) return null;
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(StepLimit);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            token.ThrowIfCancellationRequested();
            return null;
        }
        return process.ExitCode == 0 ? await output : null;
    }

    // ImageIO looks at the bytes, but a matching extension keeps it from guessing wrong.
    private static string Extension(byte[] bytes)
    {
        var head = bytes.AsSpan();
        if (head.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47])) return ".png";
        if (head.StartsWith((byte[])[0xFF, 0xD8, 0xFF])) return ".jpg";
        if (head.StartsWith("GIF8"u8)) return ".gif";
        if (head.StartsWith("BM"u8)) return ".bmp";
        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8)) return ".tiff";
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8)) return ".heic";
        return ".img";
    }

    [GeneratedRegex(@"pixel(?:Width|Height):\s*(\d+)")]
    private static partial Regex Dimension();
}
#endif
