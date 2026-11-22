// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using Avalonia.Controls;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    TrayIcon? tray;
    void RestorePet() { hidden = false; Show(); player.Paused = false; Clamp(); }
    void InitializeTray()
    {
        var menu = new NativeMenu();
        NativeMenuItem Item(string title, Action action) { var item = new NativeMenuItem(title); item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
        Item("顯示桌寵", RestorePet); Item("設定", OpenSettings);
        // As on Windows, the tray's 天氣 and 說話測試 are the pet menu's own (SpeakManual): they bring a hidden fish back, go to
        // the Agent when it writes the care, say why a test brought nothing and stay available in puppet mode, as the pet
        // menu's do; they used to start the automatic speech and were greyed out in puppet mode.
        Item("天氣", async () => await SpeakManual(true)); Item("說話測試", async () => await SpeakManual(false)); Item("關閉桌寵", Close);
        tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico")), ToolTipText = CompanionEdition.AppName, Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => RestorePet();
        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { tray });
        Closed += (_, _) => tray.Dispose();
    }
}
