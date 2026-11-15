// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

internal static partial class CompanionNative
{
    // The 40 ms tick asks for the foreground window and for full screen several times, and every full window-list
    // enumeration is WindowServer IPC on the UI thread that also presents the animation. One enumeration, without
    // owner or title strings, answers all of them for 250 ms; the snapshot is immutable and swapped as a whole.
    sealed record ForegroundSnapshot(long Taken, int Pid, long Window, bool Desktop, bool Fullscreen);
    const long SnapshotMilliseconds = 250;
    static volatile ForegroundSnapshot? snapshot;

    static ForegroundSnapshot Snapshot()
    {
        long now = Environment.TickCount64;
        var current = snapshot;
        if (current != null && now - current.Taken is >= 0 and < SnapshotMilliseconds) return current;
        var application = Send(Send(Class("NSWorkspace"), "sharedWorkspace"), "frontmostApplication");
        int pid = application == IntPtr.Zero ? 0 : SendInt(application, "processIdentifier");
        var windows = Windows(names: false).Select(w => new MacWindow(w.Id, w.Pid, w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height, w.Alpha)).ToList();
        var (window, desktop) = MacWindowRules.Foreground(windows, pid, Environment.ProcessId, null);
        if (window == 0 && pid != 0 && pid != Environment.ProcessId)
        {
            // Only an app without a usable window needs its bundle identifier (NSString is toll-free bridged to CFString).
            var identifier = Send(application, "bundleIdentifier");
            (window, desktop) = MacWindowRules.Foreground(windows, pid, Environment.ProcessId, identifier == IntPtr.Zero ? null : Text(identifier));
        }
        bool fullscreen = false;
        if (window != 0)
        {
            var front = windows.First(w => w.Id == window); var displays = new uint[16];
            if (CGGetActiveDisplayList(16, displays, out uint count) == 0)
                fullscreen = MacWindowRules.Covers(front, displays.Take((int)count).Select(CGDisplayBounds).Select(d => (d.X, d.Y, d.Width, d.Height)));
        }
        return snapshot = new(now, pid, window, desktop, fullscreen);
    }

    // screencapture failed: tell a missing Screen Recording permission apart from other failures. Checked only after a
    // failure, because the in-process preflight answer is not refreshed until the app restarts, while a new screencapture
    // process already has a permission granted meanwhile.
    internal static IOException CaptureFailure(string detail)
    {
        if (!CGPreflightScreenCaptureAccess())
            return new IOException("讀取螢幕需要「螢幕與系統錄音」權限：請到「系統設定 › 隱私權與安全性 › 螢幕與系統錄音」允許「" + EditionName
                + "」。如果已經勾選，請先關閉再重新開啟（或移除後重新加入），然後重新啟動桌寵。每次重新建置或更新 App 後，macOS 可能需要這樣重新授權一次。");
        return new IOException("螢幕截圖沒有完成" + (detail.Length > 0 ? "：" + detail : "。"));
    }
    static string EditionName => CompanionEdition.AppName;
    // The settings pane is 螢幕與系統錄音 from macOS 15 on, 螢幕錄製 on macOS 14.
    internal static string ScreenRecordingPane => OperatingSystem.IsMacOSVersionAtLeast(15) ? "螢幕與系統錄音" : "螢幕錄製";

    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberGetValue(IntPtr number, int type, out double value);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFBooleanGetValue(IntPtr boolean);
}
