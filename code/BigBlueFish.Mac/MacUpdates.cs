using System.Diagnostics;

namespace BigBlueFish.Windows;

// Releases publish BigBlueFish-v<version>[-Safe]-macOS-arm64.zip with a .sha256
// file. The verified archive is unpacked next to the app's data, and a detached
// shell script swaps the bundle once this process has exited, then reopens it.
internal static class MacUpdates
{
    internal const string Repository = "Bai-Cute/big-blue-fish-desktop-pet";
    internal static string LatestRelease => $"https://api.github.com/repos/{Repository}/releases/latest";
#if COMPANION_SAFE
    internal static string AssetName(Version version) => $"BigBlueFish-v{version.ToString(3)}-Safe-macOS-arm64.zip";
#else
    internal static string AssetName(Version version) => $"BigBlueFish-v{version.ToString(3)}-macOS-arm64.zip";
#endif
    static string Bundle => MacApplication.BundlePath ?? throw new IOException("当前不是从 BigBlueFish.app 运行，请从源码目录手动更新。");

    internal static void EnsureInstallable()
    {
        string parent = Path.GetDirectoryName(Bundle)!, probe = Path.Combine(parent, ".bigbluefish-update-" + Environment.ProcessId);
        try { File.WriteAllText(probe, ""); File.Delete(probe); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        { throw new IOException($"没有写入“{parent}”的权限，请下载新版后手动替换应用。"); }
    }

    internal static void InstallAfterExit(string archive)
    {
        string bundle = Bundle, staging = archive + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Run("/usr/bin/ditto", "-x", "-k", archive, staging);
        var update = Directory.GetDirectories(staging, "*.app").SingleOrDefault()
            ?? throw new IOException("更新包中没有找到应用程序。");
        if (!File.Exists(Path.Combine(update, "Contents/Info.plist")) || !File.ReadAllText(Path.Combine(update, "Contents/Info.plist")).Contains(MacApplication.BundleIdentifier))
            throw new IOException("更新包不是蓝色大肥鱼应用程序。");
        string script = Path.Combine(Path.GetDirectoryName(archive)!, "install-update.sh");
        File.WriteAllText(script, """
            #!/bin/sh
            # $1 waiting pid, $2 installed bundle, $3 verified new bundle
            while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
            rm -rf "$2.previous"
            mv "$2" "$2.previous" || exit 1
            if mv "$3" "$2"; then rm -rf "$2.previous"; else mv "$2.previous" "$2"; fi
            /usr/bin/open "$2"

            """);
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, Environment.ProcessId.ToString(), bundle, update }) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }

    static void Run(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 " + command);
        string errors = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("解压更新失败：" + errors.Trim());
    }
}
