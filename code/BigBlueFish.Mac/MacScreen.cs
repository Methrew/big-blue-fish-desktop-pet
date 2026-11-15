// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BigBlueFish.Windows;

// Runs runtime/BigBlueFish.MacScreen (mac-screen.swift) for find_on_screen and walk_to's desktop_item. The helper
// stops reading at its own budget and exits by itself a few seconds later; this kills it after 20 seconds anyway.
internal static class MacScreenHelper
{
    static string HelperPath => Path.Combine(AppContext.BaseDirectory, "runtime/BigBlueFish.MacScreen");
    static int accessibilityAsked;

    internal static string AccessibilityNeeded(string what) =>
        $"{what}需要「輔助使用」權限：請使用者到「系統設定 › 隱私權與安全性 › 輔助使用」打開「{CompanionEdition.AppName}」，打開後再呼叫一次。"
        + "如果已經打開還是不行，請先關掉再重新打開這個開關（臨時簽章的 App 更新後授權可能失效），再重新啟動魚魚。";

    internal static async Task<MacScreenSnapshot> FindAsync(string where, CancellationToken token, double budget = 6)
    {
        var output = await RunAsync(token, "find", where, "--exclude-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--budget", budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var snapshot = MacScreenSearch.Parse(output);
        if (!snapshot.Trusted) AskForAccessibility();
        return snapshot;
    }

    // Once per run, in the background: macOS shows its own prompt that leads to the Accessibility settings.
    internal static void AskForAccessibility()
    {
        if (Interlocked.Exchange(ref accessibilityAsked, 1) != 0) return;
        _ = Task.Run(async () => { try { await RunAsync(CancellationToken.None, "trusted", "--prompt"); } catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { } });
    }

    static async Task<string> RunAsync(CancellationToken token, params string[] arguments)
    {
        if (!File.Exists(HelperPath)) throw new IOException("缺少隨程式安裝的畫面讀取元件（runtime/BigBlueFish.MacScreen），請重新安裝。");
        var start = new ProcessStartInfo(HelperPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = LinuxProcesses.Start(start);
        using var kill = limit.Token.Register(() => { try { process.Kill(true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            var text = await output;
            if (text.Trim().Length == 0) throw new IOException("讀取畫面內容的輔助程式沒有回應：" + (await errors).Trim());
            return text;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("讀取畫面內容逾時，請稍後再試。"); }
    }
}

internal static partial class CompanionNative
{
    // The rectangle a capture of LinuxCapture.CaptureAsync shows, in global points: screencapture -l reads one window
    // (its CGWindowList bounds, without the shadow), and with one output file the whole main display.
    internal static ScreenBox CaptureSource(IntPtr window, bool fullScreen)
    {
        if (!fullScreen && !IsDesktopWindow(window) && MacScreenApi.WindowBounds(window.ToInt64()) is { } bounds) return bounds;
        var main = MacScreenApi.CGDisplayBounds(MacScreenApi.CGMainDisplayID());
        return new(main.X, main.Y, main.Width, main.Height);
    }
}

file static class MacScreenApi
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public double X, Y, Width, Height; }

    internal static ScreenBox? WindowBounds(long id)
    {
        if (id <= 0 || id > uint.MaxValue) return null;
        var array = CGWindowListCopyWindowInfo(8 /* kCGWindowListOptionIncludingWindow */, (uint)id);
        if (array == IntPtr.Zero) return null;
        try
        {
            if (CFArrayGetCount(array) == 0) return null;
            var key = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", 0x08000100);
            try
            {
                var value = CFDictionaryGetValue(CFArrayGetValueAtIndex(array, 0), key);
                return value != IntPtr.Zero && CGRectMakeWithDictionaryRepresentation(value, out var r) && r.Width > 0 && r.Height > 0 ? new ScreenBox(r.X, r.Y, r.Width, r.Height) : null;
            }
            finally { CFRelease(key); }
        }
        finally { CFRelease(array); }
    }

    [DllImport(CoreGraphics)] internal static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] internal static extern Rect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out Rect rect);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
}
