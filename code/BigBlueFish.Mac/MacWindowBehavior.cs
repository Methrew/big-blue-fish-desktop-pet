// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

// AppKit window behaviour of the pet through objc_msgSend, like the rest of CompanionNative (no native library of our own).
// Everything here runs on the UI (AppKit main) thread.
internal static partial class CompanionNative
{
    // ---- Click-through between frames ----
    // The alpha plane of the last presented frame, re-evaluated by the 40 ms tick as well, so click-through still follows
    // the pointer while frames stop (a clip switch, a failed decoder). Before the first frame the window passes clicks
    // through, so a cold start never leaves an invisible rectangle that blocks clicks.
    static byte[] shapeAlpha = [];
    static int shapeWidth, shapeHeight;
    static bool shapeMirrored;
    static IntPtr shapeWindow;

    static IntPtr WindowOf(IntPtr handle) => handle == IntPtr.Zero ? IntPtr.Zero
        : SendBool(handle, "respondsToSelector:", Selector("setIgnoresMouseEvents:")) ? handle : Send(handle, "window");

    static void KeepShape(IntPtr handle, byte[] pixels, int width, int height, bool mirror)
    {
        if (shapeAlpha.Length != width * height) shapeAlpha = new byte[width * height];
        for (int i = 0; i < shapeAlpha.Length; i++) shapeAlpha[i] = pixels[i * 4 + 3];
        shapeWidth = width; shapeHeight = height; shapeMirrored = mirror;
        if (shapeWindow == IntPtr.Zero) shapeWindow = WindowOf(handle);
    }

    // The pet window as soon as it is shown: clicks pass through it until a frame gives it a shape.
    internal static void TrackPointerWindow(IntPtr handle)
    {
        var window = WindowOf(handle); if (window == IntPtr.Zero) return;
        shapeWindow = window; UpdatePointerPassThrough();
    }

    internal static void UpdatePointerPassThrough()
    {
        var window = shapeWindow;
        if (window == IntPtr.Zero || Send(Class("NSEvent"), "pressedMouseButtons") != IntPtr.Zero) return; // never drop a press or drag in progress
        bool opaque = false;
        if (shapeWidth > 0)
        {
            var mouse = SendPoint(Class("NSEvent"), "mouseLocation"); var frame = SendRect(window, "frame");
            if (frame.Width <= 0 || frame.Height <= 0) return;
            int x = (int)Math.Floor((mouse.X - frame.X) * shapeWidth / frame.Width), y = (int)Math.Floor((frame.Y + frame.Height - mouse.Y) * shapeHeight / frame.Height);
            if (shapeMirrored) x = shapeWidth - 1 - x;
            opaque = x >= 0 && y >= 0 && x < shapeWidth && y < shapeHeight && shapeAlpha[y * shapeWidth + x] >= 8;
        }
        // Only a change is sent to AppKit (Avalonia never sets ignoresMouseEvents on a window itself).
        if (objc_msgSend_getbool(window, Selector("ignoresMouseEvents")) != !opaque) SendBool(window, "setIgnoresMouseEvents:", !opaque);
    }

    // ---- Spaces ----
    const nuint CanJoinAllSpaces = 1 << 0, Stationary = 1 << 4, IgnoresCycle = 1 << 6, FullScreenAuxiliary = 1 << 8;
    // The groups those bits belong to, each holding mutually exclusive options (AppKit raises an exception for a
    // conflicting pair): Spaces (CanJoinAllSpaces, MoveToActiveSpace), Exposé (Managed, Transient, Stationary),
    // window cycling (ParticipatesInCycle, IgnoresCycle) and full screen (Primary, Auxiliary, None).
    const nuint SpacesGroups = 0b11_1111_1111;
    // The behaviour Avalonia gave each window before it was changed here, to put back when the setting is turned off.
    static readonly ConditionalWeakTable<Avalonia.Controls.TopLevel, StrongBox<nuint>> originalBehaviour = new();

    // On every Space, left alone by Mission Control and out of the window cycle; over full-screen apps only when the
    // pet is not hidden there anyway. Avalonia resets the behaviour of owned windows when it shows them again, so this is
    // applied again on every Opened.
    internal static void ConfigureSpaces(Avalonia.Controls.TopLevel topLevel, bool allSpaces, bool overFullScreen)
    {
        var window = WindowOf(topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        if (window == IntPtr.Zero || !SendBool(window, "respondsToSelector:", Selector("setCollectionBehavior:"))) return;
        nuint current = objc_msgSend_nuint(window, Selector("collectionBehavior"));
        nuint wanted;
        if (allSpaces)
        {
            originalBehaviour.GetValue(topLevel, _ => new StrongBox<nuint>(current));
            wanted = (current & ~SpacesGroups) | CanJoinAllSpaces | Stationary | IgnoresCycle | (overFullScreen ? FullScreenAuxiliary : 0);
        }
        else if (originalBehaviour.TryGetValue(topLevel, out var original)) { wanted = original.Value; originalBehaviour.Remove(topLevel); }
        else return; // never changed here: Avalonia's own behaviour stays
        if (wanted != current) objc_msgSend_set_nuint(window, Selector("setCollectionBehavior:"), wanted);
    }

    // ---- Keyboard focus after a click or drag ----
    // A click on any window of an inactive app activates the app (Avalonia has no non-activating panel), so petting or
    // dragging the fish would take the keyboard from the app the user is typing in; Windows avoids this with
    // WS_EX_NOACTIVATE. The tick remembers the last other frontmost app and whether this app was active; a press keeps
    // that pid only when this app was not active, and the release hands activation back to it.
    static int lastOtherPid, rememberedPid;
    static bool activeAtLastTick;

    internal static void TrackActivation()
    {
        int pid = FrontmostPid();
        if (pid != 0 && pid != Environment.ProcessId) lastOtherPid = pid;
        activeAtLastTick = objc_msgSend_getbool(Send(Class("NSRunningApplication"), "currentApplication"), Selector("isActive"));
    }
    internal static void RememberFrontmost() => rememberedPid = activeAtLastTick ? 0 : lastOtherPid;
    internal static void ForgetFrontmost() => rememberedPid = 0;
    internal static void RestoreFrontmost()
    {
        int pid = rememberedPid; rememberedPid = 0;
        if (pid == 0 || pid == Environment.ProcessId) return;
        var current = Send(Class("NSRunningApplication"), "currentApplication");
        if (!objc_msgSend_getbool(current, Selector("isActive"))) return; // the user has already moved on
        var application = objc_msgSend_pid(Class("NSRunningApplication"), Selector("runningApplicationWithProcessIdentifier:"), pid);
        if (application == IntPtr.Zero || objc_msgSend_getbool(application, Selector("isTerminated"))) return;
        // Cooperative activation (macOS 14, the minimum this app supports): yield, then let that app activate itself.
        var shared = Send(Class("NSApplication"), "sharedApplication");
        if (!SendBool(shared, "respondsToSelector:", Selector("yieldActivationToApplication:"))
            || !SendBool(application, "respondsToSelector:", Selector("activateFromApplication:options:"))) return;
        objc_msgSend_ptr(shared, Selector("yieldActivationToApplication:"), application);
        objc_msgSend_activate(application, Selector("activateFromApplication:options:"), current, 0);
    }

    [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool objc_msgSend_getbool(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nuint objc_msgSend_nuint(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void objc_msgSend_set_nuint(IntPtr target, IntPtr selector, nuint value);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_pid(IntPtr target, IntPtr selector, int pid);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void objc_msgSend_ptr(IntPtr target, IntPtr selector, IntPtr argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool objc_msgSend_activate(IntPtr target, IntPtr selector, IntPtr application, nuint options);
}
