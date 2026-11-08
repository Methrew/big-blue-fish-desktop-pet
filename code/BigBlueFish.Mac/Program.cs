using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace BigBlueFish.Windows;

internal static class MacApplication
{
    internal const string BundleIdentifier = "io.github.bigbluefish.pet";
    // Contents/MacOS/ of BigBlueFish.app, or null when running from a build directory.
    internal static string? BundlePath
    {
        get
        {
            var macOS = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/'));
            var bundle = macOS.Parent?.Parent;
            return macOS.Name == "MacOS" && macOS.Parent?.Name == "Contents" && bundle?.Extension == ".app" ? bundle.FullName : null;
        }
    }
}

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        App.Args = args;
        if (args.Length == 2 && args[0] == "--wait-for-exit" && int.TryParse(args[1], out int pid)) {
            try { using var old = System.Diagnostics.Process.GetProcessById(pid); if (!old.WaitForExit(30000)) return; } catch (ArgumentException) { }
        }
        var directory = Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/BigBlueFish");
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        CompanionNative.RememberFrontmostApplication();
        Build().StartWithClassicDesktopLifetime(args);
    }
    // An accessory app: no Dock icon or menu bar, only the pet, its bubble and the status item.
    public static AppBuilder Build() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true });
}
public sealed class App : Application
{
    public static string[] Args = [];
    public override void Initialize() {
        var theme = new FluentTheme(); theme.Palettes[Avalonia.Styling.ThemeVariant.Light] = new ColorPaletteResources { Accent = Avalonia.Media.Color.FromRgb(80,96,152) };
        Styles.Add(theme); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown; desktop.MainWindow = new LinuxPetWindow(); }
        base.OnFrameworkInitializationCompleted();
    }
}
