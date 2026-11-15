// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
namespace BigBlueFish.Windows;

// One layer-0 window from CGWindowListCopyWindowInfo, in points (the top-left origin of CoreGraphics).
internal readonly record struct MacWindow(long Id, int Pid, double X, double Y, double Width, double Height, double Alpha);

// The pure rules behind the macOS foreground window (no AppKit or CoreGraphics calls, so they can be verified on their own).
internal static class MacWindowRules
{
    internal const string FinderBundle = "com.apple.finder";
    // Helper windows of a few points (status items, drag images, invisible trackers) are not what the user is looking at.
    internal const double MinimumSide = 48;

    // The window the user is looking at in the frontmost app, from the front-to-back list: its first window that is not
    // fully transparent and not tiny. No window: the desktop only when the frontmost app is Finder (as on Windows, where
    // the desktop is Explorer's own window); any other app without a window (a menu bar app, a popover, a floating panel)
    // gives none, so the previous window stays in use.
    internal static (long Window, bool Desktop) Foreground(IEnumerable<MacWindow> frontToBack, int pid, int self, string? bundle)
    {
        if (pid == 0 || pid == self) return (0, false);
        foreach (var window in frontToBack)
            if (window.Pid == pid && window.Alpha > 0 && window.Width >= MinimumSide && window.Height >= MinimumSide) return (window.Id, false);
        return (0, bundle == FinderBundle);
    }

    // Full screen when the window covers a whole display; covering rather than matching tolerates windows a little larger than it.
    internal static bool Covers(MacWindow window, IEnumerable<(double X, double Y, double Width, double Height)> displays)
        => displays.Any(d => window.X <= d.X && window.Y <= d.Y && window.X + window.Width >= d.X + d.Width && window.Y + window.Height >= d.Y + d.Height);
}
