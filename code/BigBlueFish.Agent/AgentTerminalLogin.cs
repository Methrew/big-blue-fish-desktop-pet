// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Diagnostics;

namespace BigBlueFish.Agent;

internal sealed class AgentAuthenticationCancelledException() : IOException("Agent 驗證已取消；按「重新連線」可以再試一次。");
// The login terminal was given five minutes (AgentCoordinator.Connect) and the login did not finish in them. Terminal.app
// keeps its window and the login in it; on Windows and Linux, Run has closed the login window.
internal sealed class AgentLoginTimeoutException() : IOException(OperatingSystem.IsMacOS()
    ? "等了 5 分鐘還沒完成 Agent 的登入，已停止等待；在「終端機」完成登入後，按「重新連線」。"
    : "等了 5 分鐘還沒完成 Agent 的登入，登入視窗已經關閉；按「重新連線」可以再試一次。");

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
        var directory = Directory.CreateTempSubdirectory("bigbluefish-login-").FullName;
        string script = Path.Combine(directory, "login.command"), status = Path.Combine(directory, "status");
        try
        {
            File.WriteAllText(script, LoginScript(configured, method, status));
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-a", "Terminal", script }) start.ArgumentList.Add(argument);
            using (var open = Process.Start(start) ?? throw new IOException("無法開啟 Agent 的登入終端機。"))
            {
                var output = open.StandardOutput.ReadToEndAsync(token); var errors = open.StandardError.ReadToEndAsync(token);
                await open.WaitForExitAsync(token); await output; string message = (await errors).Trim();
                // A failed open would otherwise leave the Agent page waiting for a status that never comes.
                if (open.ExitCode != 0) throw new IOException("無法開啟「終端機」來登入 Agent" + (message.Length > 0 ? "：" + message : "")
                    + "。請依「安裝與登入說明」在終端機自行登入後重新連線。");
            }
            while (!File.Exists(status) || new FileInfo(status).Length == 0) await Task.Delay(500, token);
            int code = int.TryParse(File.ReadAllText(status).Trim(), out var value) ? value : 1;
            if (code is 129 or 130 or 143) throw new IOException("Agent 的登入在「終端機」裡被中斷了（視窗已關閉或按了 Control-C）；按「重新連線」可以再試一次。");
            if (code != 0) throw new IOException("Agent 的登入終端機沒有順利完成，結束代碼：" + code + "。請重新連線後再試。");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    // The .command script: the login command, then its exit status written to a temporary file and renamed into place,
    // so the status is never read half written. A closed window (HUP) or TERM reports 129 or 143; Ctrl+C (INT) stops the
    // login command but not the script, which goes on to report the command's 130.
    internal static string LoginScript(ProcessStartInfo configured, AgentAuthenticationMethod method, string status)
    {
        static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
        var environment = new Dictionary<string, string?>(configured.Environment);
        foreach (var pair in method.Environment) environment[pair.Key] = pair.Value;
        var inherited = Environment.GetEnvironmentVariables();
        var lines = new List<string> { "#!/bin/sh",
            "finish() { printf '%s\\n' \"$1\" > " + Quote(status + ".tmp") + " && mv -f " + Quote(status + ".tmp") + " " + Quote(status) + "; }",
            "trap 'finish 129; exit 129' HUP", "trap 'finish 143; exit 143' TERM", "trap : INT" };
        if (configured.WorkingDirectory.Length > 0) lines.Add("cd " + Quote(configured.WorkingDirectory));
        foreach (var pair in environment)
            if (pair.Value != null && !Equals(inherited[pair.Key], pair.Value) && System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[A-Za-z_][A-Za-z0-9_]*$"))
                lines.Add("export " + pair.Key + "=" + Quote(pair.Value));
        lines.Add(string.Join(' ', configured.ArgumentList.Concat(method.Arguments).Prepend(configured.FileName).Select(Quote)));
        lines.Add("finish $?");
        return string.Join('\n', lines) + "\n";
    }
}
