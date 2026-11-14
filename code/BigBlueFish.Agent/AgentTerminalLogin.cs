// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.Diagnostics;

namespace BigBlueFish.Agent;

internal sealed class AgentAuthenticationCancelledException() : IOException("Agent 驗證已取消；按「重新連線」可以再試一次。");

internal static class AgentTerminalLogin
{
    private static (string Command, string[] Arguments)? Terminal()
    {
        foreach (var (command, arguments) in new[] {
            ("gnome-terminal", new[] { "--wait", "--" }),
            ("konsole", new[] { "--nofork", "-e" }), ("xterm", new[] { "-e" }) })
            if (HarnessCatalog.FindExecutable(command) is { } path) return (path, arguments);
        return null;
    }

    public static bool CanLaunch(ProcessStartInfo configured) => OperatingSystem.IsWindows()
        ? configured.Arguments.Length == 0 && !new[] { "cmd", "powershell", "pwsh" }.Contains(Path.GetFileNameWithoutExtension(configured.FileName).ToLowerInvariant())
        : OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() && Terminal() != null;

    public static async Task Run(ProcessStartInfo configured, AgentAuthenticationMethod method, CancellationToken token)
    {
        if (!CanLaunch(configured)) throw new IOException("目前的 Agent 入口無法開啟互動式登入終端機，請依「安裝與登入說明」完成登入後重新連線。");
        if (OperatingSystem.IsMacOS()) { await RunInTerminalApp(configured, method, token); return; }
        var start = new ProcessStartInfo { FileName = configured.FileName, WorkingDirectory = configured.WorkingDirectory,
            UseShellExecute = false, CreateNoWindow = false };
        foreach (var pair in configured.Environment) start.Environment[pair.Key] = pair.Value;
        foreach (var pair in method.Environment) start.Environment[pair.Key] = pair.Value;
        if (!OperatingSystem.IsWindows())
        {
            var terminal = Terminal()!.Value; start.FileName = terminal.Command;
            foreach (var arg in terminal.Arguments) start.ArgumentList.Add(arg);
            start.ArgumentList.Add(configured.FileName);
        }
        foreach (var arg in configured.ArgumentList.Concat(method.Arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("無法開啟 Agent 的登入終端機。");
        using var ownership = new ProcessOwnership(process);
        try { await process.WaitForExitAsync(token); }
        catch { try { if (!process.HasExited) process.Kill(true); } catch (Exception) { } throw; }
        if (process.ExitCode != 0) throw new IOException("Agent 的登入終端機沒有順利完成，結束代碼：" + process.ExitCode + "。請重新連線後再試。");
    }

    // Terminal.app gives no child process to wait on: run a .command script and wait for the exit code it writes.
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static async Task RunInTerminalApp(ProcessStartInfo configured, AgentAuthenticationMethod method, CancellationToken token)
    {
        static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
        var directory = Directory.CreateTempSubdirectory("bigbluefish-login-").FullName;
        string script = Path.Combine(directory, "login.command"), status = Path.Combine(directory, "status");
        var environment = new Dictionary<string, string?>(configured.Environment);
        foreach (var pair in method.Environment) environment[pair.Key] = pair.Value;
        var inherited = Environment.GetEnvironmentVariables();
        var lines = new List<string> { "#!/bin/sh", "trap 'echo 129 > " + Quote(status) + "; exit 129' HUP" };
        if (configured.WorkingDirectory.Length > 0) lines.Add("cd " + Quote(configured.WorkingDirectory));
        foreach (var pair in environment)
            if (pair.Value != null && !Equals(inherited[pair.Key], pair.Value) && System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[A-Za-z_][A-Za-z0-9_]*$"))
                lines.Add("export " + pair.Key + "=" + Quote(pair.Value));
        lines.Add(string.Join(' ', configured.ArgumentList.Concat(method.Arguments).Prepend(configured.FileName).Select(Quote)));
        lines.Add("echo $? > " + Quote(status));
        try
        {
            File.WriteAllText(script, string.Join('\n', lines) + "\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using (var open = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-a", "Terminal", script } }) ?? throw new IOException("無法開啟 Agent 的登入終端機。"))
                await open.WaitForExitAsync(token);
            while (!File.Exists(status) || new FileInfo(status).Length == 0) await Task.Delay(500, token);
            int code = int.TryParse(File.ReadAllText(status).Trim(), out var value) ? value : 1;
            if (code != 0) throw new IOException("Agent 的登入終端機沒有順利完成，結束代碼：" + code + "。請重新連線後再試。");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }
}
