// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BigBlueFish.Windows;

// What the fish's own reminders and the MCP tool get_context read about the user on macOS: idle time, a clock
// that keeps counting while the Mac sleeps, the battery, the lock screen, the program in front and the folders
// open in Finder. MacCapture.cs keeps capture and window lookup; the native calls here live in MacSystemApi.
internal static partial class CompanionNative
{
    // --test-mode overrides (LinuxProactive.ReminderTestCommand); null means the real value.
    internal static int? TestBatteryOverride;
    internal static bool? TestFullscreenOverride;
    internal static bool? TestPowerOverride;

    // Seconds since the last keyboard, mouse or trackpad input. CGEventSourceSecondsSinceLastEventType with the
    // HID system state (what v1.1.3 read) stays stuck at days on macOS 27 (measured 211,380 s while the other two
    // sources said 1,022 s), which kept automatic speech and the reminders from ever running. IOHIDSystem's
    // HIDIdleTime is the primary source; the combined session state also sees input injected into the session
    // (Screen Sharing), so the smaller of the two counts.
    internal static double IdleSeconds
    {
        get
        {
            double? hid = MacSystemApi.HidIdleSeconds();
            double combined = MacSystemApi.CGEventSourceSecondsSinceLastEventType(0 /* kCGEventSourceStateCombinedSessionState */, uint.MaxValue /* kCGAnyInputEventType */);
            bool session = double.IsFinite(combined) && combined >= 0;
            if (hid is { } idle) return session ? Math.Min(idle, combined) : idle;
            return session ? combined : 10;
        }
    }

    // macOS always knows the idle time (Linux on some Wayland desktops does not).
    internal static bool IdleKnown => true;

    // Milliseconds on a clock that never jumps and keeps counting while the Mac sleeps. Environment.TickCount64 on
    // macOS is CLOCK_UPTIME_RAW, which stops during sleep: a lid closed for 40 minutes would read as a wall clock
    // moved forward, not as a break.
    internal static long SteadyMilliseconds
    {
        get
        {
            ulong nanoseconds = MacSystemApi.clock_gettime_nsec_np(6 /* CLOCK_MONOTONIC, counts sleep */);
            return nanoseconds == 0 ? Environment.TickCount64 : (long)(nanoseconds / 1_000_000);
        }
    }

    // The internal battery's charge, 0–100; null on a Mac without one (iMac, Mac mini, Mac Studio, Mac Pro).
    internal static int? BatteryPercent => TestBatteryOverride ?? MacSystemApi.InternalBatteryPercent();

    // The lock screen is up, or this login session is not the one on the console (fast user switching).
    internal static bool Locked => MacSystemApi.SessionLocked();

    // get_context: the program and window title in front; null for the pet itself or a window that is gone.
    // The title needs Screen Recording permission (CGWindowList leaves other apps' names out without it): null then.
    // The lock screen (or another user's session) is reported as loginwindow: the windows behind it still read as on screen.
    internal static object? ForegroundApp(IntPtr handle)
    {
        if (Locked) return new { process = "loginwindow", bundleId = (string?)"com.apple.loginwindow", title = "" };
        if (handle == IntPtr.Zero) return null;
        if (handle == Desktop)
        {
            // An app in front without a window of its own (a menu bar app) is named; otherwise it is Finder's desktop.
            var front = MacSystemApi.Frontmost();
            if (front is { } app && app.Pid != Environment.ProcessId && app.BundleId != "com.apple.finder")
                return new { process = app.Name, bundleId = app.BundleId, title = "" };
            return new { process = "Finder", bundleId = (string?)"com.apple.finder", title = "macOS 桌面" };
        }
        if (MacSystemApi.Window(handle.ToInt64()) is not { } window || window.Pid == Environment.ProcessId) return null;
        var owner = MacSystemApi.Application(window.Pid);
        string name = owner?.Name is { Length: > 0 } localized ? localized : window.Owner;
        string? title = window.Title.Length > 0 || MacSystemApi.ScreenCaptureAllowed() ? Limit(window.Title, 200) : null;
        return new { process = name.Length > 0 ? name : "未知的 App", bundleId = owner?.BundleId, title };
    }

    static string Limit(string value, int maximum)
    {
        var text = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= maximum ? text : text[..maximum] + "…";
    }

    static int finderAsked;
    // The prompt thread is waiting for the user's answer; until then every permission check waits on it too.
    static volatile bool finderPrompting;
    // The one permission check in flight: a check that stalls (tccd busy, Finder hung) is shared, not started again by
    // every get_context.
    static Task<int>? permissionQuery;
    static readonly object permissionGate = new();

    static Task<int> FinderPermission()
    {
        lock (permissionGate)
            return permissionQuery is { IsCompleted: false } running ? running
                : permissionQuery = Task.Factory.StartNew(() => MacSystemApi.FinderAutomation(false), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    // get_context: the folders open in Finder windows, the one in front first. Reading them needs the Automation
    // permission for Finder; without a decision yet, macOS is asked once per run in the background (get_context
    // itself never waits on the prompt), and Note says why the list is empty. Three seconds at most: the wait itself
    // is bounded, since a native call that has started cannot be cancelled.
    internal static async Task<(IReadOnlyList<(string Title, string Folder)> Folders, string? Note)> OpenFoldersAsync(CancellationToken token)
    {
        if (finderPrompting)
            return ([], "macOS 正在詢問是否允許" + CompanionEdition.AppName + "控制 Finder；使用者回答之前無法列出開著的資料夾，回答後再呼叫一次即可。");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(3));
        int permission;
        try { permission = await FinderPermission().WaitAsync(limit.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ([], "讀取 Finder 視窗逾時。"); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return ([], null); }
        switch (permission)
        {
            case 0: break;
            case -600: return ([], null); // procNotFound: Finder is not running
            case -1744: // errAEEventWouldRequireUserConsent
                // Its own thread: asking blocks until the user answers the prompt.
                if (Interlocked.Exchange(ref finderAsked, 1) == 0)
                {
                    finderPrompting = true;
                    new Thread(() => { try { MacSystemApi.FinderAutomation(true); } catch { } finally { finderPrompting = false; } }) { IsBackground = true, Name = "Finder automation prompt" }.Start();
                }
                return ([], "已請 macOS 詢問是否允許" + CompanionEdition.AppName + "讀取 Finder 視窗；使用者允許後再呼叫 get_context 就會列出開著的資料夾。");
            case -1743: // errAEEventNotPermitted
                return ([], "使用者沒有允許" + CompanionEdition.AppName + "控制 Finder，所以無法列出開著的資料夾（可在「系統設定 › 隱私權與安全性 › 自動化」開啟）。");
            default: return ([], null);
        }
        var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-e"); start.ArgumentList.Add(FinderWindowsScript);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("osascript");
            using var kill = limit.Token.Register(() => { try { process.Kill(true); } catch { } });
            var output = process.StandardOutput.ReadToEndAsync(limit.Token); _ = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            if (process.ExitCode != 0) return ([], null);
            return (ParseFinderWindows(await output), null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ([], "讀取 Finder 視窗逾時。"); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { return ([], null); }
    }

    // Front to back, minimized windows and windows without a folder (Recents, AirDrop, a search) left out.
    // Fields are split by the ASCII unit separator and windows by the record separator: names can hold tabs.
    const string FinderWindowsScript = """
        set fieldBreak to character id 31
        set windowBreak to character id 30
        set found to ""
        tell application "Finder"
            repeat with candidate in (every Finder window)
                try
                    if not (collapsed of candidate) then set found to found & (name of candidate) & fieldBreak & (POSIX path of ((target of candidate) as alias)) & windowBreak
                end try
            end repeat
        end tell
        return found
        """;

    internal static IReadOnlyList<(string Title, string Folder)> ParseFinderWindows(string output)
    {
        var folders = new List<(string, string)>();
        foreach (var record in output.TrimEnd('\n', '\r').Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\u001f');
            if (fields.Length != 2 || fields[1].Length == 0) continue;
            string folder = fields[1].Length > 1 ? fields[1].TrimEnd('/') : fields[1];
            folders.Add((fields[0], folder));
        }
        return folders;
    }
}

// The native calls behind the members above, apart from CompanionNative's own so that names cannot collide.
file static class MacSystemApi
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit", CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices",
        Objc = "/usr/lib/libobjc.A.dylib", LibSystem = "/usr/lib/libSystem.B.dylib";
    const uint Utf8 = 0x08000100;
    static readonly object gate = new();
    static uint hidSystem;

    internal static double? HidIdleSeconds()
    {
        lock (gate)
        {
            if (hidSystem == 0) hidSystem = IOServiceGetMatchingService(0 /* kIOMainPortDefault */, IOServiceMatching("IOHIDSystem")); // consumes the matching dictionary
            if (hidSystem == 0) return null;
            using var key = new CFString("HIDIdleTime");
            var value = IORegistryEntryCreateCFProperty(hidSystem, key.Handle, IntPtr.Zero, 0);
            if (value == IntPtr.Zero) { IOObjectRelease(hidSystem); hidSystem = 0; return null; }
            try { return CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, 4 /* kCFNumberSInt64Type */, out long nanoseconds) && nanoseconds >= 0 ? nanoseconds / 1e9 : null; }
            finally { CFRelease(value); }
        }
    }

    internal static int? InternalBatteryPercent()
    {
        var info = IOPSCopyPowerSourcesInfo(); if (info == IntPtr.Zero) return null;
        try
        {
            var list = IOPSCopyPowerSourcesList(info); if (list == IntPtr.Zero) return null;
            try
            {
                using CFString typeKey = new("Type"), presentKey = new("Is Present"), currentKey = new("Current Capacity"), maximumKey = new("Max Capacity");
                for (long i = 0, count = CFArrayGetCount(list); i < count; i++)
                {
                    var description = IOPSGetPowerSourceDescription(info, CFArrayGetValueAtIndex(list, i)); // not owned
                    if (description == IntPtr.Zero || Text(CFDictionaryGetValue(description, typeKey.Handle)) != "InternalBattery") continue;
                    var present = CFDictionaryGetValue(description, presentKey.Handle);
                    if (present != IntPtr.Zero && CFGetTypeID(present) == CFBooleanGetTypeID() && !CFBooleanGetValue(present)) continue;
                    if (Number(CFDictionaryGetValue(description, currentKey.Handle)) is not { } current || Number(CFDictionaryGetValue(description, maximumKey.Handle)) is not { } maximum || maximum <= 0) continue;
                    return (int)Math.Clamp(Math.Round(current * 100d / maximum), 0, 100);
                }
                return null;
            }
            finally { CFRelease(list); }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
        finally { CFRelease(info); }
    }

    internal static bool SessionLocked()
    {
        var session = CGSessionCopyCurrentDictionary(); if (session == IntPtr.Zero) return false;
        try
        {
            using CFString lockedKey = new("CGSSessionScreenIsLocked"), consoleKey = new("kCGSSessionOnConsoleKey");
            bool Flag(IntPtr key, bool missing) { var value = CFDictionaryGetValue(session, key); return value == IntPtr.Zero || CFGetTypeID(value) != CFBooleanGetTypeID() ? missing : CFBooleanGetValue(value); }
            return Flag(lockedKey.Handle, false) || !Flag(consoleKey.Handle, true);
        }
        finally { CFRelease(session); }
    }

    internal readonly record struct WindowOwner(int Pid, string Owner, string Title);
    // The one window, fresh from the window server (kCGWindowListOptionIncludingWindow).
    internal static WindowOwner? Window(long id)
    {
        if (id <= 0 || id > uint.MaxValue) return null;
        var array = CGWindowListCopyWindowInfo(8, (uint)id); if (array == IntPtr.Zero) return null;
        try
        {
            if (CFArrayGetCount(array) == 0) return null;
            using CFString pidKey = new("kCGWindowOwnerPID"), ownerKey = new("kCGWindowOwnerName"), nameKey = new("kCGWindowName");
            var item = CFArrayGetValueAtIndex(array, 0);
            return Number(CFDictionaryGetValue(item, pidKey.Handle)) is { } pid ? new((int)pid, Text(CFDictionaryGetValue(item, ownerKey.Handle)), Text(CFDictionaryGetValue(item, nameKey.Handle))) : null;
        }
        finally { CFRelease(array); }
    }

    internal readonly record struct App(int Pid, string Name, string? BundleId);
    internal static App? Frontmost()
    {
        var application = Send(Send(objc_getClass("NSWorkspace"), "sharedWorkspace"), "frontmostApplication");
        return application == IntPtr.Zero ? null : Describe(application);
    }
    internal static App? Application(int pid)
    {
        var application = objc_msgSend_pid(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
        return application == IntPtr.Zero ? null : Describe(application);
    }
    static App Describe(IntPtr application)
    {
        // NSString is toll-free bridged with CFString.
        string bundle = Text(Send(application, "bundleIdentifier"));
        return new(objc_msgSend_int(application, sel_registerName("processIdentifier")), Text(Send(application, "localizedName")), bundle.Length > 0 ? bundle : null);
    }

    internal static bool ScreenCaptureAllowed()
    {
        try { return CGPreflightScreenCaptureAccess(); }
        catch (EntryPointNotFoundException) { return true; }
    }

    // AEDeterminePermissionToAutomateTarget for Finder and any Apple event: 0 allowed, -1743 denied,
    // -1744 not decided yet (asking blocks until the user answers), -600 Finder not running.
    internal static int FinderAutomation(bool ask)
    {
        var bundle = Encoding.UTF8.GetBytes("com.apple.finder");
        if (AECreateDesc(0x62756E64 /* typeApplicationBundleID 'bund' */, bundle, bundle.Length, out var target) != 0) return -1;
        try { return AEDeterminePermissionToAutomateTarget(ref target, 0x2A2A2A2A /* typeWildCard */, 0x2A2A2A2A, ask); }
        finally { AEDisposeDesc(ref target); }
    }

    static long? Number(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, 4, out long number) ? number : null;
    static string Text(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID()) return "";
        long length = CFStringGetLength(value); var buffer = new byte[length * 4 + 1];
        return CFStringGetCString(value, buffer, buffer.Length, Utf8) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
    }
    static IntPtr Send(IntPtr target, string selector) => target == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(target, sel_registerName(selector));

    sealed class CFString : IDisposable
    {
        internal IntPtr Handle { get; }
        internal CFString(string text) => Handle = CFStringCreateWithCString(IntPtr.Zero, text, Utf8);
        public void Dispose() { if (Handle != IntPtr.Zero) CFRelease(Handle); }
    }

    [StructLayout(LayoutKind.Sequential)] internal struct AEDesc { public uint Type; public IntPtr Data; }
    [DllImport(CoreGraphics)] internal static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
    [DllImport(CoreGraphics)] static extern IntPtr CGSessionCopyCurrentDictionary();
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(IOKit)] static extern IntPtr IOServiceMatching(string name);
    [DllImport(IOKit)] static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);
    [DllImport(IOKit)] static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
    [DllImport(IOKit)] static extern int IOObjectRelease(uint item);
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesList(IntPtr blob);
    [DllImport(IOKit)] static extern IntPtr IOPSGetPowerSourceDescription(IntPtr blob, IntPtr source);
    [DllImport(CoreFoundation)] static extern long CFGetTypeID(IntPtr value);
    [DllImport(CoreFoundation)] static extern long CFNumberGetTypeID();
    [DllImport(CoreFoundation)] static extern long CFBooleanGetTypeID();
    [DllImport(CoreFoundation)] static extern long CFStringGetTypeID();
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFBooleanGetValue(IntPtr value);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern long CFStringGetLength(IntPtr text);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFStringGetCString(IntPtr text, byte[] buffer, long size, uint encoding);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
    [DllImport(CoreServices)] static extern short AECreateDesc(uint type, byte[] data, nint size, out AEDesc result);
    [DllImport(CoreServices)] static extern short AEDisposeDesc(ref AEDesc desc);
    [DllImport(CoreServices)] static extern int AEDeterminePermissionToAutomateTarget(ref AEDesc target, uint eventClass, uint eventId, [MarshalAs(UnmanagedType.I1)] bool askUserIfNeeded);
    [DllImport(LibSystem)] internal static extern ulong clock_gettime_nsec_np(int clock);
    [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
    [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
    [DllImport(Objc)] static extern IntPtr objc_msgSend(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern int objc_msgSend_int(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_pid(IntPtr target, IntPtr selector, int pid);
}
