// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
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
    }

    partial void ConfigurePlatformWindow(Window window) => CompanionNative.ConfigureSpaces(window, Preferences.ShowOnAllSpaces, !Preferences.HideOnFullscreen);
    partial void ApplyPlatformSettings()
    {
        ConfigurePlatformWindow(this); ConfigurePlatformWindow(bubbleWindow);
#if !COMPANION_SAFE
        if (mcpInput != null) ConfigurePlatformWindow(mcpInput);
#endif
    }

    // Every 40 ms: who is frontmost (for handing the keyboard back) and click-through even while no new frame arrives.
    partial void TickPlatform() { CompanionNative.TrackActivation(); CompanionNative.UpdatePointerPassThrough(); }

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
        body.Children.Add(Paragraph("切換桌面空間時，桌寵、對話泡泡和輸入框會跟著過去，Mission Control 也不會移動它們。關閉「全螢幕時自動隱藏桌寵」時，桌寵也會顯示在全螢幕 App 上方。"));
    }
}
