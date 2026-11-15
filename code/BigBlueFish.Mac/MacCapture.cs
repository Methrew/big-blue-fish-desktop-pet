using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace BigBlueFish.Windows;

// macOS counterpart of BigBlueFish.Linux/LinuxCapture.cs. It keeps the same type and member names so the
// shared Avalonia pet compiles unchanged; X11 calls become CoreGraphics/AppKit, tesseract becomes Apple Vision.
internal static class LinuxCapture
{
    internal static async Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
    {
        string file = Path.Combine(Path.GetTempPath(), "bigbluefish-capture-" + Guid.NewGuid().ToString("N") + ".png");
        // screencapture is the system tool behind macOS Screen Recording permission; -l reads only that window, excluding the pet.
        var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var item in fullScreen || CompanionNative.IsDesktopWindow(window) ? ["-x", "-t", "png"] : new[] { "-x", "-o", "-t", "png", "-l" + window.ToInt64() }) start.ArgumentList.Add(item);
        start.ArgumentList.Add(file);
        try
        {
            using var process = LinuxProcesses.Start(start); using var cancel = token.Register(() => { try { process.Kill(true); } catch { } });
            var errors = process.StandardError.ReadToEndAsync(token); await process.WaitForExitAsync(token);
            if (process.ExitCode != 0 || !File.Exists(file)) throw CompanionNative.CaptureFailure((await errors).Trim());
            // Decoding, scaling and encoding a Retina screenshot takes over 100 ms: off the UI thread, which presents the animation.
            return await Task.Run(() =>
            {
                using var bitmap = SKBitmap.Decode(file) ?? throw new IOException("螢幕截圖沒有傳回有效的影像。");
                if ((long)bitmap.Width * bitmap.Height > 150_000_000) throw new IOException("截圖尺寸過大。");
                return CompanionNative.Encode(bitmap, maximum);
            }, token);
        }
        finally { File.Delete(file); }
    }
}

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path) => File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(DataUrl.IndexOf(',') + 1)..]));
}
internal static partial class CompanionNative
{
    // Stands for the desktop, like the X11 root window: Finder is frontmost without a visible window.
    internal static readonly IntPtr Desktop = new(-1);
    internal static bool OnAC
    {
        get
        {
            if (App.Args.Contains("--test-mode") && App.Args.Contains("--test-battery")) return false;
            try {
                var info = IOPSCopyPowerSourcesInfo(); if (info == IntPtr.Zero) return true;
                try { var type = IOPSGetProvidingPowerSourceType(info); return type == IntPtr.Zero || Text(type) != "Battery Power"; }
                finally { CFRelease(info); }
            } catch { return true; }
        }
    }
    readonly record struct WindowInfo(long Id, int Pid, int Layer, string Owner, string Title, CGRect Bounds, double Alpha = 1, bool OnScreen = true);
    static readonly IntPtr NumberKey = Key("kCGWindowNumber"), PidKey = Key("kCGWindowOwnerPID"), LayerKey = Key("kCGWindowLayer"),
        OwnerKey = Key("kCGWindowOwnerName"), NameKey = Key("kCGWindowName"), BoundsKey = Key("kCGWindowBounds"),
        AlphaKey = Key("kCGWindowAlpha"), OnScreenKey = Key("kCGWindowIsOnscreen");
    static IntPtr Key(string name) => CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
    // Front-to-back list of application windows (layer 0): all on-screen ones by default, or with
    // kCGWindowListOptionIncludingWindow (8) the one window asked for. Owner and title strings are read only when asked for.
    static List<WindowInfo> Windows(uint option = 1 | 16, uint relativeTo = 0, bool names = true)
    {
        var result = new List<WindowInfo>();
        var array = CGWindowListCopyWindowInfo(option, relativeTo); if (array == IntPtr.Zero) return result;
        try
        {
            for (long i = 0, count = CFArrayGetCount(array); i < count; i++)
            {
                var item = CFArrayGetValueAtIndex(array, i);
                long Number(IntPtr key) { var value = CFDictionaryGetValue(item, key); return value != IntPtr.Zero && CFNumberGetValue(value, 4, out long number) ? number : 0; }
                string Field(IntPtr key) { var value = CFDictionaryGetValue(item, key); return !names || value == IntPtr.Zero ? "" : Text(value); }
                var bounds = CFDictionaryGetValue(item, BoundsKey); CGRect rect = default;
                if (bounds != IntPtr.Zero) CGRectMakeWithDictionaryRepresentation(bounds, out rect);
                var alpha = CFDictionaryGetValue(item, AlphaKey); var onScreen = CFDictionaryGetValue(item, OnScreenKey);
                double opacity = alpha != IntPtr.Zero && CFNumberGetValue(alpha, 13, out double level) ? level : 1;
                if (Number(LayerKey) == 0) result.Add(new(Number(NumberKey), (int)Number(PidKey), 0, Field(OwnerKey), Field(NameKey), rect, opacity, onScreen != IntPtr.Zero && CFBooleanGetValue(onScreen)));
            }
        }
        finally { CFRelease(array); }
        return result;
    }
    // Current data of one window (an empty record once it is gone), without enumerating the others.
    static WindowInfo Window(long id, bool names = true) => id is > 0 and <= uint.MaxValue ? Windows(8, (uint)id, names).FirstOrDefault() : default;
    static int FrontmostPid()
    {
        var application = Send(Send(Class("NSWorkspace"), "sharedWorkspace"), "frontmostApplication");
        return application == IntPtr.Zero ? 0 : SendInt(application, "processIdentifier");
    }
    // From the foreground snapshot (at most 250 ms old): the frontmost app's window, Desktop when that app is Finder
    // without a window, and IntPtr.Zero for any other app without a window (a menu bar app, a floating panel).
    internal static IntPtr GetForegroundWindow()
    {
        var current = Snapshot();
        if (current.Pid == 0 || current.Pid == Environment.ProcessId) return IntPtr.Zero;
        return current.Window != 0 ? new IntPtr(current.Window) : current.Desktop ? Desktop : IntPtr.Zero;
    }
    internal static bool IsDesktopWindow(IntPtr handle) => handle == IntPtr.Zero || handle == Desktop;
    internal static IntPtr ResolveForeground(IntPtr current, IntPtr previous)
    {
        // One single-window query per candidate, with fresh data: still on screen and not the pet's own.
        static bool Valid(IntPtr handle) => handle == Desktop || handle != IntPtr.Zero && Window(handle.ToInt64(), false) is { Id: > 0, OnScreen: true } window && window.Pid != Environment.ProcessId;
        if (Valid(current)) return current;
        if (Valid(previous)) return previous;
        return Desktop;
    }
    internal static string ForegroundIdentity(IntPtr handle)
    {
        if (IsDesktopWindow(handle)) return "前台窗口：macOS 桌面\n画面是壁纸与快捷方式。";
        var window = Window(handle.ToInt64());
        return $"前台应用：{window.Owner}\n窗口标题：{window.Title}";
    }
    // The foreground window covers a whole display (kept as a cover test, which tolerates windows larger than the display).
    internal static bool IsFullscreen() { var current = Snapshot(); return current.Pid != 0 && current.Pid != Environment.ProcessId && current.Fullscreen; }
    internal static double IdleSeconds => CGEventSourceSecondsSinceLastEventType(1, uint.MaxValue);
    internal static CompanionWindowImage Encode(SKBitmap bitmap, int maximum)
    {
        double scale = Math.Min(1d, maximum / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var small = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        using var image = SKImage.FromBitmap(small ?? bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), image.Width, image.Height);
    }
    // AppKit has no input-shape region like XShape: let clicks through whenever the cursor is over a transparent pixel.
    // The frame's alpha is kept, so the 40 ms tick re-evaluates it while no new frame arrives (MacWindowBehavior.cs).
    internal static void SetInputShape(IntPtr handle, byte[] pixels, int width, int height, double scale, bool mirror)
    {
        if (handle == IntPtr.Zero || width <= 0 || height <= 0 || pixels.Length < width * height * 4) return;
        KeepShape(handle, pixels, width, height, mirror);
        UpdatePointerPassThrough();
    }
    static string Text(IntPtr value)
    {
        long length = CFStringGetLength(value); var buffer = new byte[length * 4 + 1];
        return CFStringGetCString(value, buffer, buffer.Length, 0x08000100) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
    }
    static IntPtr Class(string name) => objc_getClass(name);
    static IntPtr Selector(string name) => sel_registerName(name);
    static IntPtr Send(IntPtr target, string selector) => target == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(target, Selector(selector));
    static int SendInt(IntPtr target, string selector) => objc_msgSend_int(target, Selector(selector));
    static bool SendBool(IntPtr target, string selector, IntPtr argument) => objc_msgSend_bool(target, Selector(selector), argument);
    static void SendBool(IntPtr target, string selector, bool argument) => objc_msgSend_void(target, Selector(selector), argument);
    static CGPoint SendPoint(IntPtr target, string selector) => objc_msgSend_point(target, Selector(selector));
    static CGRect SendRect(IntPtr target, string selector) => objc_msgSend_rect(target, Selector(selector));
    [StructLayout(LayoutKind.Sequential)] internal struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct CGRect { public double X, Y, Width, Height; }
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit", Objc = "/usr/lib/libobjc.A.dylib";
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out CGRect rect);
    [DllImport(CoreGraphics)] static extern int CGGetActiveDisplayList(uint maximum, [Out] uint[] displays, out uint count);
    [DllImport(CoreGraphics)] static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] static extern long CFStringGetLength(IntPtr text);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFStringGetCString(IntPtr text, byte[] buffer, long size, uint encoding);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)] static extern IntPtr IOPSGetProvidingPowerSourceType(IntPtr info);
    [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
    [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
    [DllImport(Objc)] static extern IntPtr objc_msgSend(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern int objc_msgSend_int(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool objc_msgSend_bool(IntPtr target, IntPtr selector, IntPtr argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void objc_msgSend_void(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGPoint objc_msgSend_point(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGRect objc_msgSend_rect(IntPtr target, IntPtr selector);
}
internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height, int WordCount,
    double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words, long WindowHandle = 0, bool IsDesktop = false);
internal static class CompanionOcr
{
    // fullScreen as on Windows: false keeps a remote model to the foreground window.
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token, bool fullScreen = true)
    {
        var clock = Stopwatch.StartNew();
        var captured = fullScreen
            ? await CompanionCaptureFallback.ReadAsync<CompanionWindowImage>(
                async () => await LinuxCapture.CaptureAsync(handle, 2560, false, token),
                async () => await LinuxCapture.CaptureAsync(handle, 2560, true, token), token)
            : new CompanionCaptureResult<CompanionWindowImage>(await LinuxCapture.CaptureAsync(handle, 2560, false, token), false);
        var image = captured.Image ?? throw new IOException("未能取得可用读屏画面");
        double captureTime = clock.Elapsed.TotalMilliseconds;
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        // Apple Vision helper; it prints the same TSV layout as tesseract so parsing matches Linux.
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "runtime/BigBlueFish.MacOcr")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = LinuxProcesses.Start(start);
        using var cancel = token.Register(() => { try { process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        await process.StandardInput.BaseStream.WriteAsync(bytes, token); process.StandardInput.Close();
        await process.WaitForExitAsync(token); var text = await output;
        if (process.ExitCode != 0) throw new IOException("本地 OCR 未完成：" + await error);
        var words = new List<CompanionOcrWord>();
        foreach (var line in text.Split('\n').Skip(1))
        {
            var cells = line.TrimEnd('\r').Split('\t'); if (cells.Length < 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            if (double.TryParse(cells[6], out var x) && double.TryParse(cells[7], out var y) && double.TryParse(cells[8], out var width) && double.TryParse(cells[9], out var height)) words.Add(new(cells[11], x, y, width, height));
        }
        bool desktop = !captured.FullScreen && CompanionNative.IsDesktopWindow(handle);
        string identity = captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : CompanionNative.ForegroundIdentity(handle);
        string context = identity + "\n" + (desktop ? "画面是壁纸与快捷方式图标，没有展开的应用正文。" : CompanionOcrLayout.Format(words, image.Width, image.Height));
        return new(context, "zh-Hans+en-US", image.Width, image.Height, words.Count, captureTime, clock.Elapsed.TotalMilliseconds - captureTime, words.ToArray(), captured.FullScreen ? 0 : handle.ToInt64(), desktop);
    }
}
