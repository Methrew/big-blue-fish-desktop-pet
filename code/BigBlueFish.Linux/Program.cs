// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        App.Args = args;
        // Avalonia shapes text in the current culture, and HarfBuzz gets only its two-letter language: zh-TW turns into
        // "zh", which OpenType reads as Simplified Chinese (ZHS), so PingFang TC (and Noto Sans CJK TC) would draw the
        // Mainland forms of ，。：； (in the lower left) and of some characters. The invariant culture keeps the Taiwan
        // forms (LinuxFonts.cs); dates and numbers the fish shows are formatted with explicit patterns.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 2 && args[0] == "--wait-for-exit" && int.TryParse(args[1], out int pid)) {
            try { using var old = System.Diagnostics.Process.GetProcessById(pid); if (!old.WaitForExit(30000)) return; } catch (ArgumentException) { }
        }
        // One fish at a time; a second copy leaves the data folder alone and only says so (App).
        App.AlreadyRunning = !SingleInstance.Claim();
        if (App.AlreadyRunning) { Build().StartWithClassicDesktopLifetime(args); return; }
        // macOS: this edition's own folder (bigbluefish-tw), apart from the original's, which it can also be installed beside.
        // Linux keeps the original's folder: its packages replace the original's, so the two never run side by side.
        var directory = Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? (OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support", CompanionEdition.MacDataFolder)
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "bigbluefish"));
#if COMPANION_MAC
        if (Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") == null && !args.Contains("--test-mode"))
        {
            MacDataImport.Run(directory, Path.Combine(Path.GetDirectoryName(directory)!, CompanionEdition.OriginalMacDataFolder));
            MacLoginItem.Refresh();
        }
#endif
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        if (OperatingSystem.IsMacOS()) ImportLoginPath();
        Build().StartWithClassicDesktopLifetime(args);
    }
    // Finder starts apps with only the system PATH; Agent CLIs installed by npm or Homebrew are on the login shell's PATH.
    // macOS reads it in the background with a hard time limit (MacLoginPath); only the Agent connection waits for it.
    static void ImportLoginPath()
    {
#if COMPANION_MAC && !COMPANION_SAFE
        MacLoginPath.Start(state => DiagnosticLog.Write(DiagnosticEvent.AgentFailed, kind: "terminal", state: state));
#endif
    }
    public static AppBuilder Build() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] }).WithTaiwanFonts();
}
public sealed class App : Application
{
    public static string[] Args = [];
    internal static bool AlreadyRunning;
    public override void Initialize() {
        var theme = new FluentTheme(); theme.Palettes[Avalonia.Styling.ThemeVariant.Light] = new ColorPaletteResources { Accent = Avalonia.Media.Color.FromRgb(80,96,152) };
        Styles.Add(theme); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            if (AlreadyRunning) SingleInstance.TellAlreadyRunning(desktop); else desktop.MainWindow = new LinuxPetWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

// One fish at a time. Kept apart from LinuxPetWindow: Program.Main claims it before Avalonia starts, when nothing of
// Avalonia may be created yet.
internal static class SingleInstance
{
    static Mutex? sessionLock, userLock;
    static bool ownsSession, ownsUser;

    // Before the data folder is touched. The name is the original's, which takes it per login session as well, so this
    // edition, its Safe edition and the original never run together in one session; the second lock is the same name per
    // user across sessions, for a copy started from Terminal, over SSH or by another job. --test-mode runs on its own data
    // folder (BIGBLUEFISH_DATA) and takes only the first, so a test can run beside the user's own fish.
    internal static bool Claim()
    {
        string name = "BigBlueFish.Linux." + Environment.UserName;
        sessionLock = new Mutex(true, name, out ownsSession);
        if (App.Args.Contains("--test-mode")) return ownsSession;
        userLock = new Mutex(true, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out ownsUser);
        return ownsSession && ownsUser;
    }

    // On the thread that claimed them (the main thread, Avalonia's UI thread): a lock this copy took while the other was
    // held is let go at once, so the notice it shows never blocks the next start.
    internal static void Release()
    {
        if (ownsSession) try { sessionLock!.ReleaseMutex(); } catch (ApplicationException) { }
        if (ownsUser) try { userLock!.ReleaseMutex(); } catch (ApplicationException) { }
        ownsSession = ownsUser = false;
        sessionLock?.Dispose(); userLock?.Dispose();
    }

    // A second copy the user opened says why nothing else appears (the app has no Dock icon); one the login item or a
    // test started just ends.
    internal static void TellAlreadyRunning(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Release();
        string? job = Environment.GetEnvironmentVariable("XPC_SERVICE_NAME");
        bool quiet = App.Args.Contains("--test-mode") || job is CompanionEdition.MacBundleId or CompanionEdition.MacSafeBundleId
            || !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DESKTOP_AUTOSTART_ID") != null;
        if (quiet) { Dispatcher.UIThread.Post(() => desktop.Shutdown()); return; }
        string bar = OperatingSystem.IsMacOS() ? "選單列" : "系統匣", app = CompanionEdition.AppName;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "已經有一隻魚在執行", FontSize = 20, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Text = "已經有一隻大肥魚在執行（可能是藍色大肥魚、藍色大肥魚無MCP版或原版），同一時間只能開一隻。"
            + "請按" + bar + "上的魚魚圖示叫出牠；想改用" + app + "，先在那個圖示的選單選「關閉桌寵」，再重新開啟" + app + "。" });
        var window = new Window { Title = app, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = LinuxFonts.Interface, FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)), Content = new Border { Padding = new Thickness(24), Child = panel } };
        var ok = new Button { Content = "知道了", Padding = new Thickness(18, 8), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => window.Close(); panel.Children.Add(ok);
        window.Closed += (_, _) => desktop.Shutdown();
        window.Show(); window.Activate();
    }
}
