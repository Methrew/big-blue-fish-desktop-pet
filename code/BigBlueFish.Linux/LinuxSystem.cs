// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Runtime.InteropServices;
using System.Text;

namespace BigBlueFish.Windows;

// Linux counterparts of BigBlueFish.Mac/MacSystem.cs, so the shared reminders (LinuxProactive.cs) and get_context
// build and run on X11 too. Only BigBlueFish.Linux compiles this file; the Mac app links MacSystem.cs instead.
internal static partial class CompanionNative
{
    // --test-mode overrides (LinuxProactive.ReminderTestCommand); null means the real value.
    internal static int? TestBatteryOverride;
    internal static bool? TestFullscreenOverride;
    internal static bool? TestPowerOverride;

    // CLOCK_BOOTTIME keeps counting while the computer is suspended; Environment.TickCount64 does not.
    internal static long SteadyMilliseconds
    {
        get
        {
            try { if (clock_gettime(7 /* CLOCK_BOOTTIME */, out var now) == 0) return now.Seconds * 1000 + now.Nanoseconds / 1_000_000; }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
            return Environment.TickCount64;
        }
    }

    // A system battery's charge (not a wireless mouse's, whose scope is Device); null without one.
    internal static int? BatteryPercent
    {
        get
        {
            if (TestBatteryOverride is { } forced) return forced;
            try
            {
                foreach (var supply in Directory.GetDirectories("/sys/class/power_supply"))
                {
                    string Read(string name) => File.Exists(Path.Combine(supply, name)) ? File.ReadAllText(Path.Combine(supply, name)).Trim() : "";
                    if (Read("type") != "Battery" || Read("scope") == "Device") continue;
                    if (int.TryParse(Read("capacity"), out var percent)) return Math.Clamp(percent, 0, 100);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }

    // There is no lock state every desktop shares without asking loginctl in a subprocess; the idle time already
    // keeps reminders back while the screen is locked, so a locked screen only counts as not watching a film.
    internal static bool Locked => false;

    // get_context: the program (WM_CLASS) and window title in front; null for the pet itself or no window.
    internal static object? ForegroundApp(IntPtr handle)
    {
        if (handle == IntPtr.Zero || display == IntPtr.Zero) return null;
        if (IsDesktopWindow(handle)) return new { process = "desktop", title = "Linux 桌面" };
        lock (gate)
        {
            ulong window = (ulong)handle.ToInt64();
            if (Integers(window, "_NET_WM_PID").FirstOrDefault() == (ulong)Environment.ProcessId) return null;
            var title = Encoding.UTF8.GetString(Property(window, "_NET_WM_NAME", out _)).TrimEnd('\0');
            if (title.Length == 0) title = Encoding.UTF8.GetString(Property(window, "WM_NAME", out _)).TrimEnd('\0');
            var classes = Encoding.UTF8.GetString(Property(window, "WM_CLASS", out _)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            string name = classes.Length > 1 ? classes[1] : classes.FirstOrDefault() ?? "";
            return new { process = name.Length > 0 ? name : "未知的程式", title = title.Length <= 200 ? title : title[..200] + "…" };
        }
    }

    // get_context's openFolders: file manager windows are not read on Linux.
    internal static Task<(IReadOnlyList<(string Title, string Folder)> Folders, string? Note)> OpenFoldersAsync(CancellationToken token) =>
        Task.FromResult<(IReadOnlyList<(string Title, string Folder)>, string?)>(([], null));

    [StructLayout(LayoutKind.Sequential)] struct Timespec { public long Seconds, Nanoseconds; }
    [DllImport("libc.so.6")] static extern int clock_gettime(int clock, out Timespec time);
}
