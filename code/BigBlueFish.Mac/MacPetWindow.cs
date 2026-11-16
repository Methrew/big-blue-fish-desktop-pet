// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

// The macOS parts of the shared Avalonia pet: the platform hooks declared in LinuxPetWindow.cs.
internal sealed partial class LinuxPetWindow
{
    partial void InitializePlatform()
    {
        // Avalonia raises Opened again on every Show after Hide, and resets the behaviour of owned windows when it shows
        // them, so the window behaviour is applied on each Opened. Until the first frame the pet lets clicks through.
        Opened += (_, _) => { CompanionNative.TrackPointerWindow(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero); ConfigurePlatformWindow(this); };
        bubbleWindow.Opened += (_, _) => ConfigurePlatformWindow(bubbleWindow);
        // Openings of the clips a click or a drag starts, from where Play starts them (the drag clip at 1.5 s): about 33 MB.
        foreach (var rule in policy.Rules.Where(r => r.Pool == "Click")) player.Preload(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File));
        player.Preload(Path.Combine(AppContext.BaseDirectory, "assets/fish", policy.Find("被鼠标拖拽悬空反馈").File), 1.5);
        // Opening the app again from Finder, Launchpad or Spotlight while it runs (LaunchServices reopens this copy): a
        // hidden fish comes back, as the menu bar's 顯示桌寵 does.
        if (Avalonia.Application.Current?.TryGetFeature<Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime>() is { } lifetime)
            lifetime.Activated += (_, e) => { if (e.Kind == Avalonia.Controls.ApplicationLifetimes.ActivationKind.Reopen && !closed && phase != "Exit") RestorePet(); };
    }

    partial void ConfigurePlatformWindow(Window window) => CompanionNative.ConfigureSpaces(window, Preferences.ShowOnAllSpaces, !Preferences.HideOnFullscreen);
    partial void ApplyPlatformSettings()
    {
        ConfigurePlatformWindow(this); ConfigurePlatformWindow(bubbleWindow);
#if !COMPANION_SAFE
        if (mcpInput != null) ConfigurePlatformWindow(mcpInput);
#endif
    }

    // Every 40 ms: who is frontmost (for handing the keyboard back), click-through even while no new frame arrives, and a
    // keychain read to retry once the screen is unlocked (MacSecrets.cs).
    partial void TickPlatform() { CompanionNative.TrackActivation(); CompanionNative.UpdatePointerPassThrough(); WatchSecretUnlock(); }

    // The bubble grows line by line with a streamed reply, and Avalonia's Metal surface can keep an earlier size after such
    // a resize (CompanionNative.RefreshStaleSurface): the text was then drawn stretched, its last line cut off. Once the
    // bubble's size has held for 200 ms, each tick compares its surface with the window and asks AppKit for a repaint on
    // the main thread, which sizes the surface again. If that has not helped after 240 ms, the window is resized by one
    // point and back, which hands AppKit the size afresh. Reported once per size in the log console. Tick runs it before
    // its Exit return, so the goodbye bubble shown while the Exit clip plays is kept too.
    DateTime bubbleResizedAt;
    int bubbleStaleTicks;
    double? bubbleNudgedFrom; // the height before the one-point resize, put back on the next tick
    bool bubbleSurfaceCheck = true; // --test-mode: "surface-check off" leaves a stale surface alone, to see it last
    partial void BubbleResized() { bubbleResizedAt = DateTime.UtcNow; bubbleStaleTicks = 0; bubbleNudgedFrom = null; }
    partial void KeepSurfacePlatform()
    {
        if (bubbleNudgedFrom is { } before)
        {
            if (bubbleWindow.Height == before + 1) bubbleWindow.Height = before;
            bubbleNudgedFrom = null; return;
        }
        if (!bubbleSurfaceCheck || !bubbleWindow.IsVisible || DateTime.UtcNow - bubbleResizedAt < TimeSpan.FromMilliseconds(200)) return;
        if (!CompanionNative.RefreshStaleSurface(bubbleWindow)) { bubbleStaleTicks = 0; return; }
        if (bubbleStaleTicks++ == 0) DiagnosticLog.Write(DiagnosticEvent.BubbleShown, kind: "macOS", state: "stale_surface");
        if (bubbleStaleTicks == 7) { bubbleNudgedFrom = bubbleWindow.Height; bubbleWindow.Height += 1; }
    }

    // A left press remembers the app the user was working in; any other press, or a lost drag, forgets it.
    partial void RememberFocusPlatform(bool remember) { if (remember) CompanionNative.RememberFrontmost(); else CompanionNative.ForgetFrontmost(); }
    // After every release, once the click has been handled: the keyboard goes back unless the click opened something to type in.
    partial void ReturnFocusPlatform() => Dispatcher.UIThread.Post(() => {
        if (!closed && settings == null && consentWindow == null && modelDialog == null && !InputOpen) CompanionNative.RestoreFrontmost();
        else CompanionNative.ForgetFrontmost();
    }, DispatcherPriority.Background);
#if COMPANION_SAFE
    static bool InputOpen => false;
#else
    bool InputOpen => mcpInput?.IsVisible == true;
#endif

    partial void AddPlatformGeneralOptions(Panel body)
    {
        var box = new CheckBox { Content = "在所有桌面空間顯示桌寵", IsChecked = Preferences.ShowOnAllSpaces, Margin = new Thickness(0, 4) };
        box.IsCheckedChanged += (_, _) => { Preferences.ShowOnAllSpaces = box.IsChecked == true; ApplyPlatformSettings(); Save(); };
        body.Children.Add(box);
#if COMPANION_SAFE
        const string follows = "桌寵和對話氣泡"; // the Safe app has no input box
#else
        const string follows = "桌寵、對話氣泡和輸入框";
#endif
        body.Children.Add(Paragraph("切換桌面空間時，" + follows + "會跟著過去，Mission Control 也不會移動它們。關閉「全螢幕時自動隱藏桌寵」時，桌寵也會顯示在全螢幕 App 上方。"));
    }
}
