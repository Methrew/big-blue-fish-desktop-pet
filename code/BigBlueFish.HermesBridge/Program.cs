// Fork-original file of the taiwan-edition fork by acer1204 (2026-10-26); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
// Chat bridge for the taiwan-edition fork: text typed into the pet's double-click box goes to the
// Hermes Agent API server, and Hermes' answer comes back as the pet's speech bubble. The pet side is
// the stdio MCP bridge (BigBlueFish.Mcp, .exe on Windows); the Hermes side is its local OpenAI-compatible API.
// It runs on Windows, macOS and Linux; only the places it looks in and the image shrinking differ.
using System.Runtime.InteropServices;
using System.Text;
using BigBlueFish.Windows;

namespace BigBlueFish.HermesBridge;

// CalendarRefresh: how old the calendar read for event reminders may get; only tests set it (five minutes otherwise).
internal sealed record BridgeOptions(string McpCommand, Uri HermesUrl, string ApiKey, string StatePath, string HermesHome, string HermesLogPath,
    string? Ask, IReadOnlyList<Attachment> Attachments, string? Brief = null, TimeSpan? CalendarRefresh = null);

internal static class Program
{
    private const string KeyVariable = "BIGBLUEFISH_HERMES_KEY";
    private static readonly string McpFile = OperatingSystem.IsWindows() ? "BigBlueFish.Mcp.exe" : "BigBlueFish.Mcp";
    private const string Usage = "用法：BigBlueFish.HermesBridge [--mcp BigBlueFish.Mcp 路徑] [--hermes-url http://127.0.0.1:8642] [--state 對話紀錄檔] [--ask 測試訊息] [--attach 附件路徑（可重複）] [--brief daily|welcome:<分鐘>]";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is ["--help"] or ["-h"]) { Console.WriteLine(Usage); return 0; }
        BridgeOptions options;
        try { options = ReadOptions(args); }
        catch (ArgumentException error)
        {
            Log.Write(error.Message);
            Console.WriteLine(Usage);
            return 2;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        // Closing the terminal window (SIGHUP) or launchd/systemd stopping it (SIGTERM) winds down the same way as Ctrl+C.
        using var hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        void Stop(PosixSignalContext context) { context.Cancel = true; stop.Cancel(); }
        try
        {
            var bridge = new Bridge(options);
            if (options.Ask != null) return await bridge.AskOnceAsync(new PetMessage(options.Ask, options.Attachments), stop.Token);
            // --brief daily, or --brief welcome:<minutes away>
            if (options.Brief is { } brief)
                return await bridge.BriefOnceAsync(brief == "daily" ? Briefings.Kind.Daily : Briefings.Kind.Welcome,
                    brief.StartsWith("welcome:", StringComparison.Ordinal) && int.TryParse(brief[8..], out var away) ? away : null, stop.Token);
            // Two listening bridges would answer every message twice. One per user across sessions: on macOS and Linux a
            // Local\ name covers one session only, and every Terminal tab or launchd job is a session of its own.
            using var single = OperatingSystem.IsWindows() ? new Mutex(true, @"Local\BigBlueFish.HermesBridge", out bool first)
                : new Mutex(true, "BigBlueFish.HermesBridge", new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out first);
            if (!first) { Log.Write("另一個橋接程式已經在執行，這個就先結束。"); return 1; }
            return await bridge.RunAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 130; }
        catch (Exception error) when (error is IOException or HermesException or HttpRequestException
            or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Log.Write(error.Message);
            return 1;
        }
    }

    private static BridgeOptions ReadOptions(string[] args)
    {
        string? mcp = Environment.GetEnvironmentVariable("BIGBLUEFISH_MCP"), ask = null, state = null, brief = null;
        string url = Environment.GetEnvironmentVariable("HERMES_API_URL") ?? "http://127.0.0.1:8642";
        var attachments = new List<Attachment>();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"{args[i - 1]} 後面缺少參數。");
            switch (args[i])
            {
                case "--mcp": mcp = Value(); break;
                case "--hermes-url": url = Value(); break;
                case "--state": state = Value(); break;
                case "--ask": ask = Value(); break;
                case "--brief":
                    brief = Value();
                    if (brief != "daily" && !(brief.StartsWith("welcome:", StringComparison.Ordinal) && int.TryParse(brief[8..], out var minutes) && minutes > 0))
                        throw new ArgumentException("--brief 要填 daily 或 welcome:<離開的分鐘數>。");
                    break;
                // Test path for files the way the pet sends them: the original path, as if picked.
                case "--attach":
                    string file = Path.GetFullPath(Value());
                    if (!File.Exists(file)) throw new ArgumentException("附件不存在：" + file);
                    if (attachments.Count == Attachment.Limit) throw new ArgumentException($"一則訊息最多只能附 {Attachment.Limit} 個檔案。");
                    attachments.Add(Attachment.FromFile(file, "picker"));
                    break;
                default: throw new ArgumentException("不認得的參數：" + args[i]);
            }
        }
        // Files alone are a message too, as when the user sends them without typing.
        if (attachments.Count > 0) ask ??= "";
        if (string.IsNullOrWhiteSpace(mcp)) mcp = FindMcpBridge() ?? throw new ArgumentException($"找不到 {McpFile}，請用 --mcp 指定它的路徑。");
        if (!File.Exists(mcp)) throw new ArgumentException($"{McpFile} 不存在：" + mcp);
        var hermesUrl = CheckHermesUrl(url);
        // Hermes keeps its data in %LOCALAPPDATA%\hermes on Windows and in ~/.hermes on macOS and Linux.
        string home = Environment.GetEnvironmentVariable("HERMES_HOME") is { Length: > 0 } given ? given
            : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hermes");
        var fromEnvironment = Environment.GetEnvironmentVariable(KeyVariable);
        string key = (string.IsNullOrWhiteSpace(fromEnvironment) ? ReadHermesKey(home) : fromEnvironment.Trim())
            ?? throw new ArgumentException($"找不到 Hermes 的 API_SERVER_KEY：請在 Hermes 的 .env 設定，或用 {KeyVariable} 環境變數提供。");
        state = Path.GetFullPath(state ?? Path.Combine(DataFolder(), "hermes-bridge.json"));
        home = Path.GetFullPath(home);
        return new(Path.GetFullPath(mcp), hermesUrl, key, state, home, Path.Combine(home, "logs", "agent.log"), ask, attachments, brief);
    }

    // The bearer key goes with every chat request, so it may only travel to this machine or over https.
    private static Uri CheckHermesUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            throw new ArgumentException("Hermes 網址必須是 http 或 https：" + url);
        if (parsed.UserInfo.Length > 0) throw new ArgumentException("Hermes 網址不能包含帳號密碼。");
        if (parsed.Scheme == Uri.UriSchemeHttp && !parsed.IsLoopback)
            throw new ArgumentException("不在這台電腦上的 Hermes 必須使用 https，避免金鑰以明文傳送。");
        // A trailing slash keeps a profile prefix such as /p/name/ when relative paths are appended.
        return new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
    }

    // Where the bridge keeps hermes-bridge.json: next to the pet's own data on macOS and Linux (BIGBLUEFISH_DATA moves both),
    // %LOCALAPPDATA%\BigBlueFish on Windows as before.
    private static string DataFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BigBlueFish");
        if (Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") is { Length: > 0 } data) return data;
        if (OperatingSystem.IsMacOS()) return Path.Combine(profile, "Library", "Application Support", CompanionEdition.MacDataFolder);
        return Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(profile, ".local", "share"), "bigbluefish");
    }

    // Only fixed places: the bridge's own folder (inside the macOS app it sits next to BigBlueFish.Mcp), this checkout's
    // pet build output, and an installed pet. The original edition's names come last, for a pet installed before the rename.
    private static string? FindMcpBridge()
    {
        // bin/Release/<framework>/ of this project, four levels below code/.
        string code = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, McpFile) };
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(code, "BigBlueFish.Windows", "bin", "x64", "Release", "net10.0-windows10.0.26100.0", McpFile));
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.AddRange([Path.Combine(programs, CompanionEdition.DisplayName, McpFile), Path.Combine(programs, "蓝色大肥鱼", McpFile)]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // tools/build-macos.sh puts the app in release/; Finder installs go to /Applications or ~/Applications.
            string[] folders = [Path.Combine(code, "..", "release"), "/Applications", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")];
            foreach (var name in new[] { CompanionEdition.DisplayName, "蓝色大肥鱼" })
                candidates.AddRange(folders.Select(folder => Path.GetFullPath(Path.Combine(folder, name + ".app", "Contents", "MacOS", McpFile))));
        }
        else candidates.Add(Path.Combine("/opt", "bigbluefish", McpFile));
        return candidates.FirstOrDefault(File.Exists);
    }

    // The key stays in Hermes' own .env; the bridge only reads it at startup and never prints it.
    private static string? ReadHermesKey(string home)
    {
        string file = Path.Combine(home, ".env");
        return File.Exists(file) ? EnvValue(File.ReadLines(file), "API_SERVER_KEY") : null;
    }

    // A value from .env lines as python-dotenv, which Hermes loads the file with, reads it: an optional `export`, a
    // quoted value up to its closing quote, an unquoted one up to a # after whitespace; the last assignment wins.
    internal static string? EnvValue(IEnumerable<string> lines, string name)
    {
        string? found = null;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] == '#') continue;
            if (text.StartsWith("export", StringComparison.Ordinal) && text.Length > 6 && char.IsWhiteSpace(text[6])) text = text[6..].TrimStart();
            int equals = text.IndexOf('=');
            if (equals < 0 || text[..equals].Trim() != name) continue;
            var value = text[(equals + 1)..].TrimStart();
            if (value.Length > 0 && value[0] is '"' or '\'')
            {
                int end = value.IndexOf(value[0], 1);
                value = end < 0 ? value[1..] : value[1..end];
            }
            else
            {
                for (int i = 1; i < value.Length; i++)
                    if (value[i] == '#' && char.IsWhiteSpace(value[i - 1])) { value = value[..i]; break; }
                value = value.Trim();
            }
            found = value;
        }
        return string.IsNullOrEmpty(found) ? null : found;
    }
}

internal static class Log
{
    private static readonly object Gate = new();
    internal static void Write(string message)
    {
        lock (Gate) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
