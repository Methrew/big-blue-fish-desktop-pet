using SkiaSharp;

namespace BigBlueFish.Windows;

// What a remote vision model sees on macOS and Linux: the foreground window alone, never the desktop (which macOS
// captures as the whole screen, every other window included) and never a full-screen fallback. Its long side is at
// most MaximumSide, and it goes out as JPEG, a fraction of the lossless PNG the local OCR reads. The capture and the
// encoding run off the UI thread.
internal static class CompanionRemoteScreen
{
    internal const int MaximumSide = 1568;
    internal const int JpegQuality = 85;

    internal static Task<CompanionWindowImage?> CaptureAsync(IntPtr handle, CancellationToken token) =>
        CompanionNative.IsDesktopWindow(handle) ? Task.FromResult<CompanionWindowImage?>(null)
            : Task.Run(async () => (CompanionWindowImage?)ToJpeg(await LinuxCapture.CaptureAsync(handle, MaximumSide, false, token), MaximumSide), token);

    // Transparent pixels (the window's rounded corners) become white rather than JPEG's black.
    internal static CompanionWindowImage ToJpeg(CompanionWindowImage image, int maximum)
    {
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        using var source = SKImage.FromEncodedData(bytes) ?? throw new IOException("視窗截圖不是有效的影像。");
        double scale = Math.Min(1d, maximum / (double)Math.Max(source.Width, source.Height));
        int width = Math.Clamp((int)Math.Round(source.Width * scale), 1, maximum), height = Math.Clamp((int)Math.Round(source.Height * scale), 1, maximum);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new IOException("無法準備視窗截圖。");
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var result = surface.Snapshot();
        using var jpeg = result.Encode(SKEncodedImageFormat.Jpeg, JpegQuality) ?? throw new IOException("無法壓縮視窗截圖。");
        return new("data:image/jpeg;base64," + Convert.ToBase64String(jpeg.ToArray()), width, height);
    }
}
