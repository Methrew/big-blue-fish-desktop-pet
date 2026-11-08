using System.Diagnostics;
using System.Security;

namespace BigBlueFish.Windows;

internal static class MacProcesses
{
    static string Runtime => Path.Combine(AppContext.BaseDirectory, "runtime");
    // The bundled build is preferred; a Homebrew FFmpeg (built with libvpx) serves source checkouts.
    internal static string FfmpegPath => new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.Prepend(Path.Combine(Runtime, "ffmpeg"))
        .FirstOrDefault(File.Exists) ?? Path.Combine(Runtime, "ffmpeg");
    internal static Process Start(ProcessStartInfo original)
    {
        var guard = Path.Combine(Runtime, "BigBlueFish.RunnerGuard");
        if (!File.Exists(guard)) throw new IOException("缺少随程序安装的进程退出保障组件。");
        var command = original.FileName; original.FileName = guard;
        original.ArgumentList.Insert(0, command);
        return Process.Start(original) ?? throw new IOException("无法启动本地组件。");
    }
    internal static void OpenUrl(string url)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false }; start.ArgumentList.Add(url);
        Process.Start(start)?.Dispose();
    }

    // Login item: a per-user LaunchAgent starts the executable when the user logs in.
    internal static string LaunchAgentFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents", MacApplication.BundleIdentifier + ".plist");
    internal static bool StartupEnabled => File.Exists(LaunchAgentFile);
    internal static void SetStartup(bool on)
    {
        if (!on) { File.Delete(LaunchAgentFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentFile)!);
        string executable = SecurityElement.Escape(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mac"));
        File.WriteAllText(LaunchAgentFile, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{MacApplication.BundleIdentifier}</string>
              <key>ProgramArguments</key><array><string>{executable}</string></array>
              <key>RunAtLoad</key><true/>
              <key>ProcessType</key><string>Interactive</string>
              <key>LimitLoadToSessionType</key><string>Aqua</string>
            </dict>
            </plist>

            """);
    }
}
internal sealed class CompanionJob : IDisposable
{
    private readonly List<Process> children = [];
    internal bool Attach(Process process) { children.Add(process); return true; }
    public void Dispose() { foreach (var child in children) try { if (!child.HasExited) child.Kill(true); } catch { } children.Clear(); }
}
