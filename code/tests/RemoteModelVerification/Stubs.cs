// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
global using BigBlueFish.Diagnostics;

namespace BigBlueFish.Windows;

// What the linked shared files expect from the macOS app around them. The screen side records every request, so the
// checks can tell exactly what was captured and read.
internal static class App { internal static string[] Args = []; }

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path) => File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(DataUrl.IndexOf(',') + 1)..]));
}

internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height, int WordCount,
    double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words, long WindowHandle = 0, bool IsDesktop = false);

internal static class CompanionNative
{
    internal static readonly IntPtr Desktop = new(-1);
    internal static IntPtr Foreground = new(42);
    internal static bool OnAC => true;
    internal static IntPtr GetForegroundWindow() => Foreground;
    internal static bool IsDesktopWindow(IntPtr handle) => handle == IntPtr.Zero || handle == Desktop;
    internal static string ForegroundIdentity(IntPtr handle) => IsDesktopWindow(handle)
        ? "前景視窗：macOS 桌面\n畫面是背景圖片與桌面圖示。" : "前景程式：備忘錄\n視窗標題：購物清單";
}

// Window captures (MacCapture.cs runs /usr/sbin/screencapture here): each call and the image it returned.
internal static class LinuxCapture
{
    internal static readonly List<(IntPtr Window, int Maximum, bool FullScreen)> Calls = [];
    internal static Func<CompanionWindowImage> Image = () => throw new IOException("no capture prepared");
    internal static Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
    {
        lock (Calls) Calls.Add((window, maximum, fullScreen));
        return Task.FromResult(Image());
    }
}

// OCR (Apple Vision in MacCapture.cs): each call, with the fullScreen fallback it was allowed.
internal static class CompanionOcr
{
    internal const string WindowText = "牛奶、雞蛋、全麥吐司";
    internal static readonly List<(IntPtr Handle, bool FullScreen)> Calls = [];
    internal static Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token, bool fullScreen = true)
    {
        lock (Calls) Calls.Add((handle, fullScreen));
        return Task.FromResult(new CompanionOcrSnapshot(CompanionNative.ForegroundIdentity(handle) + "\n" + WindowText,
            "zh-Hant+zh-Hans+en-US", 800, 600, 3, 1, 1, [], handle.ToInt64()));
    }
}
