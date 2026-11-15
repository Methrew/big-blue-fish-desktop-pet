// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
#if COMPANION_OCR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace BigBlueFish.Windows;

internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height,
    int WordCount, double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words,
    long WindowHandle = 0, bool IsDesktop = false);

internal static class CompanionOcr
{
    // fullScreen: when the foreground window cannot be captured, read every screen instead. A remote model gets
    // false: the user agreed to send it the window's text, so without the window it gets no screen text at all.
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token, bool fullScreen = true)
    {
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            int maximum = Math.Min(2560, (int)OcrEngine.MaxImageDimension);
            var captured = fullScreen
                ? await CompanionCaptureFallback.ReadAsync(
                    () => Task.FromResult(CompanionScreenCapture.Capture(handle, maximum, true)),
                    () => Task.FromResult(CompanionScreenCapture.CaptureScreen(maximum)), token).ConfigureAwait(false)
                : new CompanionCaptureResult<CompanionWindowImage>(CompanionScreenCapture.Capture(handle, maximum, true), false);
            var image = captured.Image ?? throw new IOException("未能取得可用的螢幕畫面");
            var identity = captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : CompanionNative.ForegroundIdentity(handle);
            double captureMs = watch.Elapsed.TotalMilliseconds;
            var result = await RecognizeAsync(image, identity, captureMs, token, !captured.FullScreen && CompanionNative.IsDesktopWindow(handle)).ConfigureAwait(false);
            return result with { WindowHandle = captured.FullScreen ? 0 : handle.ToInt64() };
        }, token).ConfigureAwait(false);
    }

    internal static async Task<CompanionOcrSnapshot> RecognizeAsync(CompanionWindowImage image,
        string identity, double captureMs, CancellationToken token, bool desktop = false)
    {
        var watch = new Stopwatch();
        var languages = OcrEngine.AvailableRecognizerLanguages;
        var language = languages.FirstOrDefault(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(x => x.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault();
        if (language == null)
            throw new IOException("Windows 尚未安裝文字辨識語言，請在語言設定中安裝中文（繁體或簡體）文字辨識");
        var engine = OcrEngine.TryCreateFromLanguage(new Language(language.LanguageTag))
            ?? throw new IOException("無法啟動本機文字辨識");
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
        uint maximum = OcrEngine.MaxImageDimension;
        double ratio = Math.Min(1d, maximum / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform {
            ScaledWidth = Math.Max(1u, (uint)(decoder.PixelWidth * ratio)),
            ScaledHeight = Math.Max(1u, (uint)(decoder.PixelHeight * ratio))
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
        watch.Restart();
        var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        double recognizeMs = watch.Elapsed.TotalMilliseconds;
        var words = result.Lines.SelectMany(x => x.Words).Select(x => new CompanionOcrWord(
            x.Text, x.BoundingRect.X, x.BoundingRect.Y, x.BoundingRect.Width, x.BoundingRect.Height)).ToArray();
        // Desktop shortcut captions are background, not an open document or task.
        // Small shadowed labels produce unreliable OCR; keep the captured scene identity
        // rather than presenting those fragments as the owner's current topic.
        var context = desktop
            ? identity + "\n畫面是桌布與捷徑圖示，沒有展開的應用程式正文。"
            : identity + "\n" + CompanionOcrLayout.Format(words, bitmap.PixelWidth, bitmap.PixelHeight);
        return new CompanionOcrSnapshot(context, language.LanguageTag, bitmap.PixelWidth, bitmap.PixelHeight,
            words.Length, captureMs, recognizeMs, words, IsDesktop: desktop);
    }
}
#endif
