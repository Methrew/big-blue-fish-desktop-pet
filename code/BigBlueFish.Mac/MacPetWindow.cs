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
        player.PreferHardware = Preferences.HighQualityAnimation;
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

    // Windows whose size the app changes while they are shown: the bubble grows line by line with a streamed reply, and the
    // input box grows when attachments or a two-line notice arrive. Avalonia's Metal surface can keep an earlier size after
    // such a resize (CompanionNative.RefreshStaleSurface): the bubble's text was then drawn stretched with its last line cut
    // off, and the input box lost its lower part (the attachments cut off, no 傳送 button) after files were dropped on the
    // fish, or was squeezed into a strip after it shrank again. Once the bubble's size has held for 200 ms, and at every tick
    // for the input box (it changes size only now and then, and a box cut in half should not stay so even for a moment),
    // each tick compares the window's surface with the window and asks AppKit for a repaint on the main thread, which sizes
    // the surface again. If that has not helped after 240 ms, the window is resized
    // by one point and back, which hands AppKit the size afresh. The bubble's case is reported once per size in the log
    // console. Tick runs it before its Exit return, so the goodbye bubble shown while the Exit clip plays is kept too.
    sealed class SurfaceWatch
    {
        internal Size Seen; // the client size last seen: a change starts the settling time and the count before the nudge again
        internal DateTime ResizedAt;
        internal int StaleTicks;
        internal double? NudgedFrom; // the height before the one-point resize, put back on the next tick
        internal void Resized() { ResizedAt = DateTime.UtcNow; StaleTicks = 0; NudgedFrom = null; }
    }
    readonly SurfaceWatch bubbleSurface = new(), inputSurface = new();
    bool surfaceCheck = true; // --test-mode: "surface-check off" leaves a stale surface alone, to see it last
    partial void BubbleResized() => bubbleSurface.Resized();
    partial void KeepSurfacePlatform()
    {
        KeepSurface(bubbleWindow, bubbleSurface, TimeSpan.FromMilliseconds(200), true);
#if !COMPANION_SAFE
        if (mcpInput != null) KeepSurface(mcpInput, inputSurface, TimeSpan.Zero, false);
#endif
    }
    void KeepSurface(Window window, SurfaceWatch watch, TimeSpan settle, bool report)
    {
        if (watch.NudgedFrom is { } before)
        {
            if (window.Height == before + 1) window.Height = before;
            // Seen keeps the size from before the nudge: Avalonia applies a new Height at its next layout pass, so the
            // client size goes to +1 and back after this, and neither counts as a resize (one nudge and one report per size).
            watch.NudgedFrom = null; return;
        }
        if (window.ClientSize != watch.Seen) { watch.Seen = window.ClientSize; watch.Resized(); }
        if (!surfaceCheck || !window.IsVisible || DateTime.UtcNow - watch.ResizedAt < settle) return;
        if (!CompanionNative.RefreshStaleSurface(window)) { watch.StaleTicks = 0; return; }
        if (watch.StaleTicks++ == 0 && report) DiagnosticLog.Write(DiagnosticEvent.BubbleShown, kind: "macOS", state: "stale_surface");
        if (watch.StaleTicks == 7) { watch.NudgedFrom = window.Height; window.Height += 1; }
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
        var quality = new CheckBox { Content = "高畫質動畫（1280×720、每秒 60 格）", IsChecked = Preferences.HighQualityAnimation, Margin = new Thickness(0, 4) };
        quality.IsCheckedChanged += (_, _) => { Preferences.HighQualityAnimation = quality.IsChecked == true; player.PreferHardware = Preferences.HighQualityAnimation; Save(); };
        body.Children.Add(quality);
        // Normally done by the first clip already; a runtime that cannot load must not take the settings window down
        // with it (the log console, reached from here, is where the reason shows).
        bool ready;
        try { CompanionMedia.Initialize(); ready = true; }
        catch (Exception error) { ready = false; DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "unavailable", error: error); }
        body.Children.Add(Paragraph(!ready ? "動畫元件無法載入，所以無法使用高畫質動畫；原因會顯示在「記錄主控台」。"
            : CompanionMedia.HardwareAvailable ? "由 Mac 的硬體解碼，畫面更清晰、動作更流暢，從下一段動作開始套用。CPU 用量會比關閉時多一些；想省電可以關閉，改播 640×360、每秒 24 格的動畫。"
            : "這台 Mac 無法用硬體解碼這種動畫，所以播放 640×360、每秒 24 格的動畫。"));
        quality.IsEnabled = ready && CompanionMedia.HardwareAvailable;
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
