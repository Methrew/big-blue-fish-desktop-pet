using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkiaSharp;

namespace BigBlueFish.Windows;

internal static class MacCapture
{
    internal static Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
        => Task.Run(() => { token.ThrowIfCancellationRequested(); return CompanionNative.Capture(window, maximum, fullScreen); }, token);
}

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path) => File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(DataUrl.IndexOf(',') + 1)..]));
}

// Window handles are CGWindowIDs. The desktop has no window of its own, so it
// is represented by DesktopHandle, while IntPtr.Zero still means "no window".
internal static partial class CompanionNative
{
    const string Library = "libBigBlueFishMac";
    internal static readonly IntPtr DesktopHandle = new(-1);
    static readonly byte[] alpha = new byte[640 * 360];
    static IntPtr shapedWindow;
    static bool shapeMirrored, passingThrough;
    static Foreground snapshot;
    static long? snapshotAt;

    [StructLayout(LayoutKind.Sequential)] struct Foreground { public int Pid; public uint Window; public int Desktop, Fullscreen; }

    // The 40 ms UI tick asks for the foreground several times; the window list is enumerated at most every 250 ms.
    static Foreground Snapshot()
    {
        long now = Environment.TickCount64;
        if (snapshotAt is not { } taken || now - taken >= 250) { bbf_foreground_snapshot(out snapshot); snapshotAt = now; }
        return snapshot;
    }
    internal static bool OnAC => !(App.Args.Contains("--test-mode") && App.Args.Contains("--test-battery")) && bbf_on_ac() != 0;
    internal static double IdleSeconds => bbf_idle_seconds();
    internal static IntPtr GetForegroundWindow()
    {
        var current = Snapshot();
        return current.Desktop != 0 ? DesktopHandle : current.Window == 0 ? IntPtr.Zero : new IntPtr(current.Window);
    }
    internal static bool IsDesktopWindow(IntPtr handle) => handle == IntPtr.Zero || handle == DesktopHandle;
    internal static IntPtr ResolveForeground(IntPtr current, IntPtr previous)
    {
        static bool Valid(IntPtr handle) => handle == DesktopHandle
            || handle != IntPtr.Zero && bbf_window_owner((uint)handle.ToInt64(), out int pid) != 0 && pid != Environment.ProcessId;
        if (Valid(current)) return current;
        if (Valid(previous)) return previous;
        return DesktopHandle;
    }
    internal static bool IsFullscreen() { var current = Snapshot(); return current.Fullscreen != 0 && current.Pid != Environment.ProcessId; }
    internal static string ForegroundIdentity(IntPtr handle)
    {
        if (IsDesktopWindow(handle)) return "前台窗口：macOS 桌面\n画面是壁纸与桌面图标。";
        var pointer = bbf_window_identity((uint)handle.ToInt64());
        try {
            using var identity = JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer) ?? "{}");
            string Read(string name) => identity.RootElement.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            return $"前台应用：{Read("app")}\n窗口标题：{Read("title")}";
        }
        finally { bbf_free(pointer); }
    }
    internal static bool ScreenCaptureAllowed(bool request) => bbf_screen_capture_access(request ? 1 : 0) != 0;

    // Desktop and full-screen requests capture every display with the pet's own windows excluded.
    internal static CompanionWindowImage Capture(IntPtr handle, int maximum, bool full)
    {
        bool whole = full || IsDesktopWindow(handle);
        if (bbf_capture(whole ? 0 : (uint)handle.ToInt64(), whole ? 1 : 0, maximum, out var pixels, out int width, out int height, out int stride, out var error) == 0)
        {
            string message = Marshal.PtrToStringUTF8(error) ?? "截图失败。"; bbf_free(error);
            throw new IOException(message);
        }
        try
        {
            if (width <= 0 || height <= 0 || (long)width * height > 150_000_000) throw new IOException("截图区域无效。");
            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            for (int row = 0; row < height; row++)
                unsafe { Buffer.MemoryCopy((byte*)pixels + (long)row * stride, (byte*)bitmap.GetPixels() + (long)row * bitmap.RowBytes, bitmap.RowBytes, width * 4L); }
            return Encode(bitmap, maximum);
        }
        finally { bbf_free(pixels); }
    }
    internal static CompanionWindowImage Encode(SKBitmap bitmap, int maximum)
    {
        double scale = Math.Min(1d, maximum / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var small = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        using var image = SKImage.FromBitmap(small ?? bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), image.Width, image.Height);
    }
    internal static CompanionOcrWord[] Recognize(byte[] image)
    {
        var pointer = bbf_recognize_text(image, image.Length, out var error);
        if (pointer == IntPtr.Zero) { string message = Marshal.PtrToStringUTF8(error) ?? "本地 OCR 未完成。"; bbf_free(error); throw new IOException(message); }
        try { return JsonSerializer.Deserialize<CompanionOcrWord[]>(Marshal.PtrToStringUTF8(pointer) ?? "[]") ?? []; }
        finally { bbf_free(pointer); }
    }

    internal static void RememberFrontmostApplication() => bbf_remember_frontmost();
    internal static void RestoreFrontmostApplication() => bbf_restore_frontmost();
    internal static void ConfigureWindow(Avalonia.Controls.TopLevel window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor == "NSWindow") bbf_configure_window(handle.Handle);
    }
    // macOS has no input shape: each presented frame keeps its alpha, and the pet
    // window ignores the pointer whenever it is over a transparent pixel.
    internal static void SetInputShape(IntPtr window, byte[] pixels, double scale, bool mirror)
    {
        if (window == IntPtr.Zero) return;
        for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        shapedWindow = window; shapeMirrored = mirror;
        UpdatePointerPassThrough();
    }
    internal static void UpdatePointerPassThrough()
    {
        if (shapedWindow == IntPtr.Zero) return;
        int buttons = bbf_pointer_in_window(shapedWindow, out double x, out double y, out double width, out double height);
        if (buttons != 0 && !passingThrough) return; // keep a press or drag that started on the pet
        bool opaque = false;
        if (width > 0 && height > 0 && x >= 0 && y >= 0 && x < width && y < height)
        {
            int column = Math.Min(639, (int)(x / width * 640)), row = Math.Min(359, (int)(y / height * 360));
            opaque = alpha[row * 640 + (shapeMirrored ? 639 - column : column)] >= 8;
        }
        if (passingThrough == !opaque) return;
        passingThrough = !opaque; bbf_set_ignores_mouse(shapedWindow, passingThrough ? 1 : 0);
    }

    [DllImport(Library)] static extern void bbf_free(IntPtr pointer);
    [DllImport(Library)] static extern double bbf_idle_seconds();
    [DllImport(Library)] static extern int bbf_on_ac();
    [DllImport(Library)] static extern void bbf_configure_window(IntPtr window);
    [DllImport(Library)] static extern int bbf_pointer_in_window(IntPtr window, out double x, out double y, out double width, out double height);
    [DllImport(Library)] static extern void bbf_set_ignores_mouse(IntPtr window, int ignore);
    [DllImport(Library)] static extern void bbf_remember_frontmost();
    [DllImport(Library)] static extern void bbf_restore_frontmost();
    [DllImport(Library)] static extern void bbf_foreground_snapshot(out Foreground result);
    [DllImport(Library)] static extern int bbf_window_owner(uint window, out int pid);
    [DllImport(Library)] static extern IntPtr bbf_window_identity(uint window);
    [DllImport(Library)] static extern int bbf_screen_capture_access(int request);
    [DllImport(Library)] static extern int bbf_capture(uint window, int whole, int maximum, out IntPtr pixels, out int width, out int height, out int stride, out IntPtr error);
    [DllImport(Library)] static extern IntPtr bbf_recognize_text(byte[] data, int length, out IntPtr error);
}

internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height, int WordCount,
    double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words, long WindowHandle = 0, bool IsDesktop = false);
internal static class CompanionOcr
{
    internal const string Languages = "zh-Hans+zh-Hant+en-US";
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var image = await MacCapture.CaptureAsync(handle, 2560, false, token);
        double captureTime = clock.Elapsed.TotalMilliseconds;
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        var words = await Task.Run(() => CompanionNative.Recognize(bytes), token);
        bool desktop = CompanionNative.IsDesktopWindow(handle);
        string context = CompanionNative.ForegroundIdentity(handle) + "\n" + (desktop ? "画面是壁纸与桌面图标，没有展开的应用正文。" : CompanionOcrLayout.Format(words, image.Width, image.Height));
        return new(context, Languages, image.Width, image.Height, words.Length, captureTime, clock.Elapsed.TotalMilliseconds - captureTime, words, handle.ToInt64(), desktop);
    }
}
