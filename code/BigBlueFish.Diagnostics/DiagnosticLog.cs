// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Diagnostics;

public enum DiagnosticEvent
{
    AppStarted, AppStopped, SettingsOpened, SettingsClosed, SettingsChanged, VisibilityChanged,
    SessionRecovery, Interaction, AnimationStarted, AnimationCompleted, AnimationFailed, TrayState, AppFailed,
    SpeechRequested, SpeechSkipped, SpeechStarted, SpeechCompleted, SpeechCancelled, SpeechFailed,
    ScreenReadStarted, ScreenReadCompleted, ScreenReadFallback, BubbleShown, BubbleClosed,
    RunnerStarting, RunnerReady, RunnerStopped, RunnerFailed, RunnerCpuFallback,
    WeatherRefresh, WeatherCached, WeatherFailed, NewsFetch, NewsSelected, NewsFailed,
    AgentConnecting, AgentConnected, AgentDisconnected, AgentRequest, AgentMaterials, AgentReply,
    AgentCancelled, AgentFailed, AgentPermission, AgentConfiguration, AgentWorkspace, McpTool,
    LogExported, LogExportFailed
}

public sealed record DiagnosticEntry(long Sequence, DateTimeOffset Time, string Level, string Code, string Message, string Details)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{Code}] {Message}" + (Details.Length == 0 ? "" : " | " + Details);
}

/// <summary>Shareable metadata only. No API accepts prompt text, OCR, images or exception messages.</summary>
public static class DiagnosticLog
{
    public const int Capacity = 32768;
    static readonly object gate = new();
    static readonly Queue<DiagnosticEntry> entries = new();
    static readonly byte[] pathSalt = RandomNumberGenerator.GetBytes(16);
    static long sequence, revision;
    static string appDirectory = Path.GetFullPath(AppContext.BaseDirectory), dataDirectory = Path.GetFullPath(Environment.CurrentDirectory);
    static readonly HashSet<string> tokens = new(("start success failed cancelled blocked ready stopped hidden visible fullscreen user automatic test weather local remote agent "
        + "persona news screen image ocr text GPU CPU cache cold warm startup exit idle Event Idle Click Drag Greeting Daily Startup Exit External PuppetIdle "
        + "Outbound Return BetweenMoves WalkStart WalkLoop WalkStop click drag_start drag_end user_message user_returned daily_care speech_test weather_request wakeup start_care connect reconnect new_conversation "
        + "set_configuration authentication permission read write model mode thought_level other unavailable unsupported empty expired changed enabled disabled "
        + "Standard Safe OCR Vision Windows Linux macOS Preview timeout first_content hardware_fallback notice manual busy power model_missing puppet "
        + "get_settings get_context update_settings list_actions capture_foreground capture_screen play_action interrupt_action walk_to find_on_screen say say_next request_local_speech "
        + "get_operation_guide get_wakeups schedule_wakeup cancel_wakeup terminal oauth restored created "
        + "claude codex gemini dsh kimi qwen antigravity copilot cursor opencode cline codebuddy qoder trae kiro pi devin grok hermes openclaw minimax").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    static readonly HashSet<string> actions = LoadActions();
    static readonly Dictionary<string, string> previousSettings = new();
    static readonly HashSet<string> settingNames = new(("ModelEnabled GpuEnabled PublicInfo ForegroundEnabled StopModelOnBattery HideOnFullscreen CheckUpdatesOnStartup "
        + "AgentEnabled AgentHarnessId AgentDailySpeech PuppetMode AgentRespondToClick AgentRespondToDrag WeatherRegionCode Scale BubbleScale SpeechMinMinutes BubbleDurationIndex "
        + "AnimationIntervalSeconds BreakfastHour LunchHour DinnerHour LastBrushDate Left Top StartupEnabled "
        + "McpEnabled McpDoubleClickChat McpWakeEnabled McpWakeOnText McpWakeOnClick McpWakeOnDrag AgentWorkspaceId "
        // Taiwan edition settings, by name only: the remote API key and the CWA authorization code are never observed.
        + "McpChatEnabled ModelSource RemoteUrl RemoteModel ScreenImages SitReminderMinutes MealReminder LateReminder LateReminderHour "
        + "BatteryReminder RainReminder DailyBriefing WelcomeBackMinutes EventReminderMinutes "
        // macOS only; on other platforms the preferences have no such property, so it is never observed there.
        + "ShowOnAllSpaces").Split(' '), StringComparer.Ordinal);
    static HashSet<string> LoadActions()
    {
        using var stream = typeof(DiagnosticLog).Assembly.GetManifestResourceStream("BuiltinActions.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray().Select(x => x.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
    }
    public static long Revision { get { lock (gate) return revision; } }
    public static DiagnosticEntry[] Snapshot() { lock (gate) return entries.ToArray(); }
    public static void Clear() { lock (gate) { entries.Clear(); revision++; } }
    public static void ConfigureDirectories(string app, string data)
    {
        lock (gate) { appDirectory = Path.GetFullPath(app); dataDirectory = Path.GetFullPath(data); }
    }
    public static void ObserveSettings(object settings)
    {
        try { ObserveSettingsCore(settings); } catch { /* Diagnostics cannot interrupt settings or pet operation. */ }
    }
    static void ObserveSettingsCore(object settings)
    {
        var changed = new List<string>();
        lock (gate)
        {
            foreach (var p in settings.GetType().GetProperties().Where(p => settingNames.Contains(p.Name)))
            {
                string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(p.GetValue(settings))));
                if (!previousSettings.TryGetValue(p.Name, out var before) || before != hash) changed.Add(p.Name);
                previousSettings[p.Name] = hash;
            }
        }
        if (changed.Count > 0) Write(DiagnosticEvent.SettingsChanged, changed: changed);
    }
    static string Token(string value) => tokens.Contains(value) ? value : "other";
    public static void Write(DiagnosticEvent code, string? kind = null, string? state = null, int? count = null,
        int? chars = null, double? elapsedMs = null, bool? enabled = null, Exception? error = null,
        string? action = null, string? directory = null, IEnumerable<string>? changed = null)
    {
        try { WriteCore(code, kind, state, count, chars, elapsedMs, enabled, error, action, directory, changed); }
        catch { /* Logging is best effort and must not break the operation being diagnosed. */ }
    }
    static void WriteCore(DiagnosticEvent code, string? kind, string? state, int? count,
        int? chars, double? elapsedMs, bool? enabled, Exception? error,
        string? action, string? directory, IEnumerable<string>? changed)
    {
        var fields = new List<string>();
        if (kind != null) fields.Add("kind=" + Token(kind));
        if (state != null) fields.Add("state=" + Token(state));
        if (count != null) fields.Add("count=" + Math.Max(0, count.Value).ToString(CultureInfo.InvariantCulture));
        if (chars != null) fields.Add("chars=" + Math.Max(0, chars.Value).ToString(CultureInfo.InvariantCulture));
        if (elapsedMs != null && double.IsFinite(elapsedMs.Value)) fields.Add("elapsed_ms=" + Math.Max(0, elapsedMs.Value).ToString("0.##", CultureInfo.InvariantCulture));
        if (enabled != null) fields.Add("enabled=" + enabled.Value.ToString().ToLowerInvariant());
        if (action != null) fields.Add("action=" + (actions.Contains(action) ? action : "<unknown-action>"));
        if (directory != null) fields.Add("directory=" + DirectoryAlias(directory));
        if (changed != null) fields.Add("changed=" + string.Join(",", changed.Where(settingNames.Contains).Distinct().Order()));
        if (error != null)
        {
            fields.Add("exception=" + error.GetType().Name);
            fields.Add("hresult=0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
            if (error is System.ComponentModel.Win32Exception win32) fields.Add("win32=" + win32.NativeErrorCode);
            if (error is HttpRequestException http && http.StatusCode != null) fields.Add("http_status=" + (int)http.StatusCode);
            var methods = new StackTrace(error, false).GetFrames()?.Select(x => x.GetMethod())
                .Where(x => x?.DeclaringType?.Namespace?.StartsWith("BigBlueFish", StringComparison.Ordinal) == true)
                .Take(5).Select(x => x!.DeclaringType!.Name + "." + x.Name).ToArray();
            if (methods is { Length: > 0 }) fields.Add("at=" + string.Join(" > ", methods));
        }
        string level = error != null || state is "failed" or "unsupported" ? "WARN" : "INFO";
        lock (gate)
        {
            entries.Enqueue(new(++sequence, DateTimeOffset.Now, level, code.ToString(), Message(code), string.Join("; ", fields)));
            while (entries.Count > Capacity) entries.Dequeue();
            revision++;
        }
    }
    public static string DirectoryAlias(string directory)
    {
        string full;
        try { full = Path.GetFullPath(directory); } catch { return "<invalid-directory>"; }
        lock (gate)
        {
            foreach (var (basis, label) in new[] { (dataDirectory, "<Data>"), (appDirectory, "<App>") })
            {
                var relative = Path.GetRelativePath(basis, full);
                if (relative == ".") return label;
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    var safe = new HashSet<string>(["data", "models", "cache", "agent", "adapters", "workspaces", "logs", "runtime", "media", "gstreamer", "assets", "fish"], StringComparer.OrdinalIgnoreCase);
                    return label + "/" + string.Join("/", relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Select(p => safe.Contains(p) ? p : "<private>"));
                }
            }
        }
        return "<ExternalDirectory#" + Convert.ToHexString(SHA256.HashData(pathSalt.Concat(Encoding.UTF8.GetBytes(full)).ToArray()))[..8] + ">";
    }
    public static string ExportText()
    {
        var snapshot = Snapshot();
        return "藍色大肥魚 · 疑難排解記錄\nVersion: " + typeof(DiagnosticLog).Assembly.GetName().Version!.ToString(3)
            + "\nOS: " + RuntimeInformation.OSDescription + "\nArchitecture: " + RuntimeInformation.ProcessArchitecture
            + "\nRuntime: " + RuntimeInformation.FrameworkDescription + "\nEntries: " + snapshot.Length + "/" + Capacity
            + "\n只包含這次執行的疑難排解中繼資料；資料夾路徑已匿名化。\n\n" + string.Join(Environment.NewLine, snapshot.Select(x => x.ToString())) + Environment.NewLine;
    }
    public static void SaveText(string path) => File.WriteAllText(path, ExportText(), new UTF8Encoding(true));
    static string Message(DiagnosticEvent code) => code switch
    {
        DiagnosticEvent.AppStarted => "桌寵啟動", DiagnosticEvent.AppStopped => "桌寵結束", DiagnosticEvent.AppFailed => "桌寵執行異常", DiagnosticEvent.TrayState => "狀態圖示變化",
        DiagnosticEvent.SettingsOpened => "開啟設定", DiagnosticEvent.SettingsClosed => "關閉設定", DiagnosticEvent.SettingsChanged => "設定已儲存",
        DiagnosticEvent.VisibilityChanged => "桌寵顯示狀態變化", DiagnosticEvent.SessionRecovery => "恢復桌面工作階段並清理舊工作", DiagnosticEvent.Interaction => "收到桌寵互動",
        DiagnosticEvent.AnimationStarted => "開始播放動作", DiagnosticEvent.AnimationCompleted => "動作播放結束", DiagnosticEvent.AnimationFailed => "動畫播放失敗",
        DiagnosticEvent.SpeechRequested => "收到說話要求", DiagnosticEvent.SpeechSkipped => "這次說話沒有執行", DiagnosticEvent.SpeechStarted => "開始準備說話",
        DiagnosticEvent.SpeechCompleted => "模型已產生要說的話", DiagnosticEvent.SpeechCancelled => "說話已取消", DiagnosticEvent.SpeechFailed => "產生要說的話失敗",
        DiagnosticEvent.ScreenReadStarted => "開始讀取前景畫面", DiagnosticEvent.ScreenReadCompleted => "前景資料準備完成", DiagnosticEvent.ScreenReadFallback => "無法讀取畫面，改用其餘資料產生",
        DiagnosticEvent.BubbleShown => "文字已交給氣泡顯示", DiagnosticEvent.BubbleClosed => "氣泡已關閉",
        DiagnosticEvent.RunnerStarting => "啟動本機推論程式", DiagnosticEvent.RunnerReady => "模型已就緒", DiagnosticEvent.RunnerStopped => "推論程式已停止",
        DiagnosticEvent.RunnerFailed => "推論程式發生故障", DiagnosticEvent.RunnerCpuFallback => "切換到 CPU 相容模式",
        DiagnosticEvent.WeatherRefresh => "開始更新天氣", DiagnosticEvent.WeatherCached => "使用天氣快取", DiagnosticEvent.WeatherFailed => "天氣取得失敗",
        DiagnosticEvent.NewsFetch => "取得科技新聞", DiagnosticEvent.NewsSelected => "本輪選用新聞資料", DiagnosticEvent.NewsFailed => "新聞取得失敗",
        DiagnosticEvent.AgentConnecting => "開始連線 Agent", DiagnosticEvent.AgentConnected => "Agent 工作階段已連線", DiagnosticEvent.AgentDisconnected => "Agent 連線已關閉",
        DiagnosticEvent.AgentRequest => "向 Agent 提交事件", DiagnosticEvent.AgentMaterials => "準備或送交 Agent 資料", DiagnosticEvent.AgentReply => "Agent 本輪處理結束",
        DiagnosticEvent.AgentCancelled => "Agent 要求已取消", DiagnosticEvent.AgentFailed => "Agent 要求失敗", DiagnosticEvent.AgentPermission => "Agent 要求權限或驗證",
        DiagnosticEvent.AgentConfiguration => "調整 Agent 工作階段設定", DiagnosticEvent.AgentWorkspace => "Agent 工作區狀態變化", DiagnosticEvent.McpTool => "收到 MCP 工具呼叫",
        DiagnosticEvent.LogExported => "疑難排解記錄已儲存成 TXT", DiagnosticEvent.LogExportFailed => "疑難排解記錄儲存失敗", _ => code.ToString()
    };
}
