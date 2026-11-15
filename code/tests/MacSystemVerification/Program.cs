// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// What the reminders and get_context read on this Mac (MacSystem.cs), checked against the system itself:
// the idle time is not the stuck HID system state v1.1.3 read, the steady clock keeps counting, the battery,
// lock and foreground readings answer, and Finder's reply is parsed. --finder also asks Finder for its open
// folders (this may show the macOS Automation prompt once, for the app running the test).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BigBlueFish.Windows;

Console.OutputEncoding = Encoding.UTF8;
// The pet has AppKit loaded through Avalonia; a console process has to load it for NSWorkspace and NSRunningApplication.
NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
CompanionNative.TestPowerOverride = null; CompanionNative.TestFullscreenOverride = null; // the real readings only
int passes = 0, failures = 0;
void Check(bool condition, string name) { if (condition) passes++; else failures++; Console.WriteLine((condition ? "PASS " : "FAIL ") + name); }
string Json(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

// Idle time: the smaller of HIDIdleTime and the combined session state, never the HID system state alone.
double idle = CompanionNative.IdleSeconds, hid = Probe.HidIdleSeconds(), combined = Probe.Idle(0), system = Probe.Idle(1);
double uptime = CompanionNative.SteadyMilliseconds / 1000d;
Console.WriteLine($"     idle {idle:F1} s; HIDIdleTime {hid:F1} s, combined session {combined:F1} s, HID system state (v1.1.3) {system:F1} s, since boot {uptime:F0} s");
Check(double.IsFinite(idle) && idle >= 0 && idle <= uptime + 1, "idle time is a real duration since boot");
Check(Math.Abs(idle - Math.Min(hid, combined)) < 2, "idle time follows HIDIdleTime and the combined session state");
if (system > idle + 3600) Console.WriteLine($"     note: the HID system state is {system - idle:F0} s behind the real input on this Mac; v1.1.3 would never have spoken");

// Steady clock: monotonic, real time, and at least the uptime that stops during sleep.
long first = CompanionNative.SteadyMilliseconds; var watch = Stopwatch.StartNew(); Thread.Sleep(300); long second = CompanionNative.SteadyMilliseconds;
Check(second - first >= 250 && second - first <= watch.ElapsedMilliseconds + 50, $"steady clock advances with real time ({second - first} ms over {watch.ElapsedMilliseconds} ms)");
// TickCount64 is CLOCK_UPTIME_RAW here, which stops during sleep; CLOCK_MONOTONIC is slewed by NTP by a few ppm.
long uptimeRaw = Environment.TickCount64, slept = CompanionNative.SteadyMilliseconds - uptimeRaw;
Console.WriteLine($"     steady clock minus TickCount64: {slept / 1000d:F1} s (time asleep since boot, minus NTP slewing)");
Check(slept > -(uptimeRaw / 10_000 + 1000), "steady clock counts at least what TickCount64 counts (within 100 ppm)");

// Battery: null without one, 0–100 otherwise; the test override wins.
var battery = CompanionNative.BatteryPercent;
Console.WriteLine($"     battery {Json(battery)}; providing power {(Probe.ProvidingPowerSource() ?? "unknown")}");
Check(battery is null or (>= 0 and <= 100), "battery is a percentage or null");
CompanionNative.TestBatteryOverride = 15;
Check(CompanionNative.BatteryPercent == 15, "battery-percent test override is read");
CompanionNative.TestBatteryOverride = null;

bool locked = CompanionNative.Locked;
Console.WriteLine($"     locked {locked}");
Check(locked == Probe.SessionFlag("CGSSessionScreenIsLocked") || locked && !Probe.SessionFlag("kCGSSessionOnConsoleKey", true), "lock state follows the login session");

// Foreground: nothing for no window, Finder's desktop (or the windowless app in front), and a real window's owner.
// While the screen is locked every handle reads as loginwindow: the windows behind the lock still count as on screen.
const string LockScreen = "{\"process\":\"loginwindow\",\"bundleId\":\"com.apple.loginwindow\",\"title\":\"\"}";
Check(locked ? Json(CompanionNative.ForegroundApp(IntPtr.Zero)) == LockScreen : CompanionNative.ForegroundApp(IntPtr.Zero) == null, "no window: no foreground app (the lock screen while locked)");
var desktop = Json(CompanionNative.ForegroundApp(CompanionNative.Desktop));
Console.WriteLine("     desktop " + desktop);
Check(desktop.Contains("\"process\":") && desktop.Contains("\"bundleId\":"), "the desktop names an app and its bundle id");
Check(locked ? Json(CompanionNative.ForegroundApp(new IntPtr(uint.MaxValue - 7))) == LockScreen : CompanionNative.ForegroundApp(new IntPtr(uint.MaxValue - 7)) == null,
    "a window that does not exist: null (the lock screen while locked)");
if (Probe.SomeWindow() is { } window)
{
    var app = Json(CompanionNative.ForegroundApp(new IntPtr(window.Id)));
    Console.WriteLine($"     window {window.Id} of {window.Owner}: {app}");
    Check(app.Contains("\"process\":\"") && !app.Contains("\"process\":\"\""), "a window on screen names its app");
}
else Console.WriteLine("     no other app's window on screen to look up");

// Finder's reply: unit separator between name and folder, record separator between windows.
var parsed = CompanionNative.ParseFinderWindows("下載\u001f/Users/me/Downloads/\u001eMacintosh HD\u001f/\u001ewith\ttab\u001f/Users/me/a\tb/\u001e\n");
Check(parsed.Count == 3 && parsed[0] == ("下載", "/Users/me/Downloads") && parsed[1] == ("Macintosh HD", "/") && parsed[2] == ("with\ttab", "/Users/me/a\tb"),
    "Finder windows parse front to back, names may hold tabs, the root keeps its slash");
Check(CompanionNative.ParseFinderWindows("\n").Count == 0 && CompanionNative.ParseFinderWindows("broken\u001e").Count == 0, "an empty or broken reply is no folder");

if (args.Contains("--finder"))
{
    var clock = Stopwatch.StartNew();
    var (folders, note) = await CompanionNative.OpenFoldersAsync(CancellationToken.None);
    Console.WriteLine($"     Finder in {clock.ElapsedMilliseconds} ms: {Json(folders.Select(f => new { f.Title, f.Folder }))} note {Json(note)}");
    Check(clock.ElapsedMilliseconds < 4000, "Finder folders answer within the three-second limit");
}

Console.WriteLine($"passed {passes}, failed {failures}");
return failures == 0 ? 0 : 1;

// Independent readings of the same sources, straight from the system.
static class Probe
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    internal static double Idle(int state) => CGEventSourceSecondsSinceLastEventType(state, uint.MaxValue);
    internal static double HidIdleSeconds()
    {
        uint service = IOServiceGetMatchingService(0, IOServiceMatching("IOHIDSystem"));
        var key = CFStringCreateWithCString(IntPtr.Zero, "HIDIdleTime", 0x08000100);
        try { var value = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0); try { return value != IntPtr.Zero && CFNumberGetValue(value, 4, out long ns) ? ns / 1e9 : double.NaN; } finally { if (value != IntPtr.Zero) CFRelease(value); } }
        finally { CFRelease(key); IOObjectRelease(service); }
    }
    internal static bool SessionFlag(string name, bool missing = false)
    {
        var session = CGSessionCopyCurrentDictionary(); if (session == IntPtr.Zero) return missing;
        var key = CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
        try { var value = CFDictionaryGetValue(session, key); return value == IntPtr.Zero ? missing : CFBooleanGetValue(value); }
        finally { CFRelease(key); CFRelease(session); }
    }
    internal static string? ProvidingPowerSource()
    {
        var info = IOPSCopyPowerSourcesInfo(); if (info == IntPtr.Zero) return null;
        try { var type = IOPSGetProvidingPowerSourceType(info); return type == IntPtr.Zero ? null : Text(type); } finally { CFRelease(info); }
    }
    internal static (long Id, string Owner)? SomeWindow()
    {
        var array = CGWindowListCopyWindowInfo(1 | 16, 0); if (array == IntPtr.Zero) return null;
        try
        {
            IntPtr layerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", 0x08000100), numberKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowNumber", 0x08000100),
                pidKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", 0x08000100), ownerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", 0x08000100);
            for (long i = 0, count = CFArrayGetCount(array); i < count; i++)
            {
                var item = CFArrayGetValueAtIndex(array, i);
                long Number(IntPtr key) { var value = CFDictionaryGetValue(item, key); return value != IntPtr.Zero && CFNumberGetValue(value, 4, out long number) ? number : -1; }
                if (Number(layerKey) == 0 && Number(pidKey) != Environment.ProcessId) { var owner = CFDictionaryGetValue(item, ownerKey); return (Number(numberKey), owner == IntPtr.Zero ? "" : Text(owner)); }
            }
            return null;
        }
        finally { CFRelease(array); }
    }
    static string Text(IntPtr value)
    {
        long length = CFStringGetLength(value); var buffer = new byte[length * 4 + 1];
        return CFStringGetCString(value, buffer, buffer.Length, 0x08000100) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
    }
    [DllImport(CoreGraphics)] static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
    [DllImport(CoreGraphics)] static extern IntPtr CGSessionCopyCurrentDictionary();
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(IOKit)] static extern IntPtr IOServiceMatching(string name);
    [DllImport(IOKit)] static extern uint IOServiceGetMatchingService(uint port, IntPtr matching);
    [DllImport(IOKit)] static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
    [DllImport(IOKit)] static extern int IOObjectRelease(uint item);
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)] static extern IntPtr IOPSGetProvidingPowerSourceType(IntPtr info);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFBooleanGetValue(IntPtr value);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern long CFStringGetLength(IntPtr text);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFStringGetCString(IntPtr text, byte[] buffer, long size, uint encoding);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
}
