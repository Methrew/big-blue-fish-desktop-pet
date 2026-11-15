// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BigBlueFish.Windows;

// Finder starts apps with launchd's short PATH (/usr/bin:/bin:/usr/sbin:/sbin); Agent CLIs installed by npm, Homebrew or a
// version manager are on the login shell's PATH. `$SHELL -ilc` reads .zprofile and .zshrc, as a new Terminal window would.
// The shell runs in the background with a hard time limit: a profile that hangs, or leaves a background job holding the
// output pipe, can no longer hold up startup. Only the Agent connection waits for it; the Safe edition has no Agent and
// starts everything else by absolute path, so it skips the import.
internal static partial class MacLoginPath
{
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(5), Drain = TimeSpan.FromSeconds(1);
    // Used only when the login shell gives no PATH: the usual per-user and Homebrew install folders that exist.
    static readonly string[] Fallback = ["/opt/homebrew/bin", "/usr/local/bin", "~/.local/bin", "~/.volta/bin", "~/.bun/bin"];
    const string Command = "printf '\\n<bbf-path>%s</bbf-path>\\n' \"$PATH\"";

    // Completes (never faults) once PATH has been imported or the import has given up.
    internal static Task Ready { get; private set; } = Task.CompletedTask;

    internal static void Start(Action<string>? failed = null)
    {
        Ready = Task.Run(async () =>
        {
            string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } configured && Path.IsPathRooted(configured) && File.Exists(configured) ? configured : "/bin/zsh";
            string? login = null, state = "unavailable";
            try { (login, state) = await Read(shell, Limit, Drain); }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { state = "failed"; }
            if (login == null) failed?.Invoke(state);
            Environment.SetEnvironmentVariable("PATH", Merge(login, Environment.GetEnvironmentVariable("PATH"),
                login == null ? ExistingFallback(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) : []));
        });
    }

    // Runs `shell -ilc` and returns the PATH between the markers as soon as that line arrives, even while a background job
    // keeps the pipe open. Gives up `limit` after the start, or `drain` after the shell has exited without the marker; the
    // shell and its children are then killed. State: "success", "timeout" or "unavailable" (exited without the marker).
    internal static async Task<(string? Path, string State)> Read(string shell, TimeSpan limit, TimeSpan drain)
    {
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-ilc"); start.ArgumentList.Add(Command);
        using var process = Process.Start(start) ?? throw new IOException("login shell did not start");
        using var stop = new CancellationTokenSource(limit);
        try
        {
            process.StandardInput.Close();
            _ = process.StandardError.ReadToEndAsync(stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
            var reading = Marker(process.StandardOutput, stop.Token);
            var exited = process.WaitForExitAsync(stop.Token).ContinueWith(async _ => { if (!stop.IsCancellationRequested) await Task.Delay(drain, stop.Token); }, TaskScheduler.Default).Unwrap();
            var timeout = Task.Delay(Timeout.Infinite, stop.Token);
            var first = await Task.WhenAny(reading, exited, timeout);
            Observe(reading); Observe(exited);
            if (reading.IsCompletedSuccessfully && reading.Result != null) return (reading.Result, "success");
            if (first == reading) // end of output without the marker; the shell is done with it
                return (null, "unavailable");
            return (null, stop.IsCancellationRequested ? "timeout" : "unavailable");
        }
        finally
        {
            stop.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    static void Observe(Task task) => task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    static async Task<string?> Marker(StreamReader output, CancellationToken token)
    {
        while (await output.ReadLineAsync(token) is { } line)
            if (MarkerLine().Match(line) is { Success: true } match) return match.Groups[1].Value;
        return null;
    }
    [GeneratedRegex("<bbf-path>(.*)</bbf-path>")] private static partial Regex MarkerLine();

    // The login shell's own order first (Homebrew and user folders keep their precedence over the system ones), then the
    // entries of the current PATH it lacks, then any fallback folders. Empty and relative entries are dropped.
    internal static string Merge(string? login, string? current, IEnumerable<string> fallback)
    {
        var entries = new List<string>();
        foreach (var list in new[] { login ?? "", current ?? "", string.Join(':', fallback) })
            foreach (var item in list.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Path.IsPathRooted(item) && !entries.Contains(item, StringComparer.Ordinal)) entries.Add(item);
        return string.Join(':', entries);
    }

    internal static string[] ExistingFallback(string home) => Fallback
        .Select(folder => folder.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, folder[2..]) : folder)
        .Where(Directory.Exists).ToArray();
}
