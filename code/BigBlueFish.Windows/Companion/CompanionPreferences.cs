// Modified by acer1204 on 2026-10-30 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
#if !COMPANION_LINUX && !COMPANION_MAC
using System.Security.Cryptography;
#endif

namespace BigBlueFish.Windows;
internal sealed class CompanionPreferences
{
    public bool ModelEnabled { get; set; } = true;
    public bool GpuEnabled { get; set; } = true;
    public bool PublicInfo { get; set; } = true;
    public bool ForegroundEnabled { get; set; } = true;
    // One switch for the laptop off its charger. The model on this computer pauses and frees its memory
    // (CompanionBrain.CanRunOnCurrentPower; a remote llama.cpp server or a cloud AI service keeps talking), and in the
    // edition with the Agent, AiPausedOnBattery pauses the Agent the fish started: its process, its tool calls, the daily
    // care it writes and what the owner hands it. The MCP interface for other clients is not paused.
    public bool StopModelOnBattery { get; set; } = true;
    [JsonIgnore] public bool AiPausedOnBattery => StopModelOnBattery && !CompanionNative.OnAC;
#if COMPANION_SAFE
    internal const string BatteryPauseLabel = "筆電拔掉電源時暫停本機模型";
#else
    internal const string BatteryPauseLabel = "筆電拔掉電源時暫停本機模型和 Agent";
#endif
    public bool HideOnFullscreen { get; set; } = true;
    // Leaves the pet, its bubble and the input box out of screenshots and screen recordings (SetWindowDisplayAffinity,
    // CompanionCaptureVisibility). Windows only; macOS and Linux only keep the value.
    public bool HideFromCapture { get; set; }
#if COMPANION_MAC
    // The pet, its bubble and the input box on every desktop Space (Mission Control leaves them in place).
    public bool ShowOnAllSpaces { get; set; } = true;
    // The 1280x720, 60 fps clips through VideoToolbox where the Mac decodes VP9 in hardware; off: the 640x360, 24 fps
    // clips on the CPU, which costs less CPU time in all (fewer frames to hand to the window).
    public bool HighQualityAnimation { get; set; } = true;
#endif
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool AgentEnabled { get; set; }
    public string AgentHarnessId { get; set; } = "codex";
    // The CLI the fish starts runs on an API Key of the cloud AI service, billed per use, instead of the sign-in it has
    // (BigBlueFish.Agent.ApiKeyLaunch): Claude Code (AgentHarnessId "claude") on ClaudeApiKey instead of its subscription,
    // Codex CLI ("codex") on OpenAiApiKey instead of its ChatGPT sign-in, Gemini CLI ("gemini") on GeminiApiKey instead of
    // its Google sign-in. The user's own: MCP can read them, never change them; the keys themselves never appear there.
    public bool AgentClaudeApiKey { get; set; }
    public bool AgentOpenAiApiKey { get; set; }
    public bool AgentGeminiApiKey { get; set; }
    public bool AgentDailySpeech { get; set; }
    public bool AgentRespondToClick { get; set; }
    public bool AgentRespondToDrag { get; set; }
    public bool ChatLogShowAll { get; set; }
    // The MCP interface for external clients (BigBlueFish.Mcp on the fixed pipe, such as the Hermes bridge), on every
    // platform. It is switched apart from the ACP connection above and never turns that connection on; MCP can only read it.
    public bool McpEnabled { get; set; }
    public bool PuppetMode { get; set; }
    // Double-click input goes to the clients of the MCP interface (only there) while one is connected; otherwise to the
    // ACP connection.
    public bool McpChatEnabled { get; set; }
    [JsonIgnore] public bool McpWakeEnabled { get => false; set { } }
    [JsonIgnore] public bool McpWakeOnText { get => true; set { } }
    [JsonIgnore] public bool McpWakeOnClick { get => AgentRespondToClick; set => AgentRespondToClick = value; }
    [JsonIgnore] public bool McpWakeOnDrag { get => AgentRespondToDrag; set => AgentRespondToDrag = value; }
    // Either way of handing the fish to an agent: the ACP connection or the external MCP interface.
    internal bool AnyAgentControl => AgentEnabled || McpEnabled;
    public string WeatherRegionCode { get; set; } = CompanionRegions.DefaultCode;
    public double Scale { get; set; } = 1;
    public double BubbleScale { get; set; } = 1;
    public int SpeechMinMinutes { get; set; } = 10;
    internal static readonly (int Min, int Max)[] SpeechRanges = [(2, 3), (3, 5), (5, 8), (10, 16), (15, 23), (20, 30)];
    internal int SpeechRangeIndex => Array.FindIndex(SpeechRanges, r => r.Min == NormalizeSpeechMinimum(SpeechMinMinutes));
    internal static int NormalizeSpeechMinimum(int value) => value switch
    {
        2 or 3 or 5 or 10 or 15 or 20 => value,
        <= 2 => 2,
        <= 4 => 3,
        <= 6 => 5,
        <= 12 => 10,
        <= 17 => 15,
        _ => 20
    };
    internal TimeSpan NextSpeechDelay(Random random)
    {
        var range = SpeechRanges[SpeechRangeIndex];
        return TimeSpan.FromMinutes(range.Min + random.NextDouble() * (range.Max - range.Min));
    }
    public int BubbleDurationIndex { get; set; } = 3;
    public int AnimationIntervalSeconds { get; set; } = 40;
    public int BreakfastHour { get; set; } = 8;
    public int LunchHour { get; set; } = 12;
    public int DinnerHour { get; set; } = 18;
    public string? LastBrushDate { get; set; }
    // The fish's own reminders (CompanionReminders; Windows, and LinuxProactive on Linux and macOS): minutes of use before a break reminder (0 = off)
    // and the hour from which a late night is mentioned (-1 = off).
    public int SitReminderMinutes { get; set; } = 60;
    public bool MealReminder { get; set; } = true;
    // On and off apart from the hour: with 0 meaning midnight, a small model took 0 for off (a scenario test, 2026-10-28).
    public bool LateReminder { get; set; } = true;
    public int LateReminderHour { get; set; } = 0;
    public bool BatteryReminder { get; set; } = true;
    public bool RainReminder { get; set; } = true;
    internal static readonly int[] SitReminderChoices = [0, 30, 45, 60, 90, 120];
    internal static readonly int[] LateReminderChoices = [22, 23, 0, 1, 2];
    // Spoken by the Hermes bridge, not the fish: a briefing the first time the user sits down each day, and what
    // happened while they were away after a break of at least this many minutes (0 = off).
    public bool DailyBriefing { get; set; } = true;
    public int WelcomeBackMinutes { get; set; } = 60;
    internal static readonly int[] WelcomeBackChoices = [0, 30, 60, 120, 240];
    // Also the bridge's, from the user's Google Calendar through Hermes: minutes before an event starts (0 = off).
    public int EventReminderMinutes { get; set; } = 10;
    internal static readonly int[] EventReminderChoices = [0, 5, 10, 15, 30];

    internal static readonly int[] BubbleDurations =
    {
        5,
        10,
        15,
        30,
        60,
        120,
        180,
        0
    };
    internal static readonly string[] BubbleDurationLabels =
    {
        "5 秒",
        "10 秒",
        "15 秒",
        "30 秒",
        "1 分鐘",
        "2 分鐘",
        "3 分鐘",
        "直到下一句"
    };
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string ModelSource { get; set; } = "Local";
    public string RemoteUrl { get; set; } = "";
    public string RemoteModel { get; set; } = "";
    public string? RemoteApiKeyProtected { get; set; }
    public bool ScreenImages { get; set; }
    public string? CwaApiKeyProtected { get; set; }
    // ModelSource "Cloud" (CompanionCloud.cs): the provider that writes the speech, the model chosen for each provider (kept
    // while another one is in use; empty = the provider's recommended model) and the requests allowed a day (0 = no limit).
    // Each provider's API key is a secret of its own. ScreenImages also decides whether a cloud request carries the
    // foreground window's screenshot.
    public string CloudProvider { get; set; } = "Claude";
    public string ClaudeModel { get; set; } = "";
    public string OpenAiModel { get; set; } = "";
    public string GeminiModel { get; set; } = "";
    public int CloudDailyLimit { get; set; } = 200;
    public string? ClaudeApiKeyProtected { get; set; }
    public string? OpenAiApiKeyProtected { get; set; }
    public string? GeminiApiKeyProtected { get; set; }
    // The ids CompanionCloud.Providers uses, in the same order.
    internal static readonly string[] CloudProviders = ["Claude", "OpenAI", "Gemini"];
    internal static readonly int[] CloudDailyLimitChoices = [50, 100, 200, 500, 0];

    // Secrets never appear in MCP get_settings (which also leaves out every [JsonIgnore] alias above), and update_settings
    // refuses them; any process of this user could still read them.
    internal static readonly string[] PrivateSettings = [nameof(RemoteApiKeyProtected), nameof(CwaApiKeyProtected),
        nameof(ClaudeApiKeyProtected), nameof(OpenAiApiKeyProtected), nameof(GeminiApiKeyProtected)];
    internal bool UseRemoteModel => ModelSource == "Remote" && Uri.TryCreate(RemoteUrl.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https";
    // The cloud source is chosen (the brain then uses it even before a key is entered, and says so instead of speaking).
    internal bool CloudSelected => ModelSource == "Cloud";
    // ... and usable: a known provider with a key.
    internal bool UseCloudModel => CloudSelected && CloudProviders.Contains(CloudProvider) && CloudApiKey(CloudProvider).Length > 0;
    internal string CloudModel(string provider) => provider switch { "OpenAI" => OpenAiModel, "Gemini" => GeminiModel, _ => ClaudeModel };
    internal void SetCloudModel(string provider, string model)
    {
        model = model.Trim();
        switch (provider)
        {
            case "OpenAI": OpenAiModel = model; break;
            case "Gemini": GeminiModel = model; break;
            default: ClaudeModel = model; break;
        }
    }
    internal string CloudApiKey(string provider) => provider switch { "OpenAI" => OpenAiApiKey, "Gemini" => GeminiApiKey, _ => ClaudeApiKey };
    // What the file holds for a provider's key (encrypted, or the keychain marker on macOS); null when none is stored.
    internal string? CloudApiKeyStored(string provider) => provider switch { "OpenAI" => OpenAiApiKeyProtected, "Gemini" => GeminiApiKeyProtected, _ => ClaudeApiKeyProtected };
    // The harnesses that can run on a cloud AI service's key (BigBlueFish.Agent.ApiKeyLaunch): the provider whose key each
    // one uses (null for the others), and its switch above.
    internal static string? AgentApiKeyProvider(string? harness) => harness switch { "claude" => "Claude", "codex" => "OpenAI", "gemini" => "Gemini", _ => null };
    internal bool AgentUsesApiKey(string? harness) => harness switch { "claude" => AgentClaudeApiKey, "codex" => AgentOpenAiApiKey, "gemini" => AgentGeminiApiKey, _ => false };
    internal void SetAgentUsesApiKey(string harness, bool on)
    {
        switch (harness)
        {
            case "claude": AgentClaudeApiKey = on; break;
            case "codex": AgentOpenAiApiKey = on; break;
            case "gemini": AgentGeminiApiKey = on; break;
            default: throw new ArgumentOutOfRangeException(nameof(harness));
        }
    }
    // On macOS this writes the keychain and blocks for up to a few seconds (see below); a refused write throws.
    internal void SetCloudApiKey(string provider, string value)
    {
        switch (provider)
        {
            case "OpenAI": OpenAiApiKey = value; break;
            case "Gemini": GeminiApiKey = value; break;
            default: ClaudeApiKey = value; break;
        }
    }
#if COMPANION_MAC
    // macOS keeps every secret (the CWA code, the remote and the three cloud API keys) in the login keychain (MacKeychain);
    // preferences.json only says "keychain" for a stored one.
    // LoadSecrets reads them into memory off the UI thread, and the getters return those values without blocking.
    // Setting one writes the keychain at once and blocks for up to a few seconds, so the settings page sets it off the
    // UI thread and saves afterwards; a refused write throws, keeps the old value and never leaves a copy in the file.
    internal const string KeychainMarker = "keychain";
    // What memory holds for each secret. Loaded: the keychain's value (or none). Failed: the keychain could not be read
    // (locked, or security did not answer); the secret reads as empty but is still stored, so nothing may take that empty
    // value for a removal. Legacy: a value an earlier build kept in the file (Base64), not yet moved into the keychain.
    // Unsaved: such a value the keychain refuses (a character outside MacKeychain.IsValid); it is used for this run only
    // and has left the file, so the user has to enter a key the keychain accepts.
    internal enum SecretState { Unread, Loaded, Failed, Legacy, Unsaved }
    private readonly Dictionary<string, string> revealed = new();
    private readonly Dictionary<string, (SecretState State, string? Problem)> secretStates = new();
    internal string CwaApiKey { get => Revealed(MacKeychain.CwaAccount); set => CwaApiKeyProtected = Conceal(value, MacKeychain.CwaAccount); }
    internal string RemoteApiKey { get => Revealed(MacKeychain.RemoteAccount); set => RemoteApiKeyProtected = Conceal(value, MacKeychain.RemoteAccount); }
    internal string ClaudeApiKey { get => Revealed(MacKeychain.ClaudeAccount); set => ClaudeApiKeyProtected = Conceal(value, MacKeychain.ClaudeAccount); }
    internal string OpenAiApiKey { get => Revealed(MacKeychain.OpenAiAccount); set => OpenAiApiKeyProtected = Conceal(value, MacKeychain.OpenAiAccount); }
    internal string GeminiApiKey { get => Revealed(MacKeychain.GeminiAccount); set => GeminiApiKeyProtected = Conceal(value, MacKeychain.GeminiAccount); }
    // The keychain account of a cloud provider's key, for StateOf, SecretProblem and the settings' notes.
    internal static string CloudAccount(string provider) => provider switch { "OpenAI" => MacKeychain.OpenAiAccount, "Gemini" => MacKeychain.GeminiAccount, _ => MacKeychain.ClaudeAccount };
    // The value in use for an account, whatever its state.
    internal string SecretValue(string account) => Revealed(account);
    private static readonly string[] SecretAccounts = [MacKeychain.CwaAccount, MacKeychain.RemoteAccount, MacKeychain.ClaudeAccount, MacKeychain.OpenAiAccount, MacKeychain.GeminiAccount];
    private string? StoredSecret(string account) => account switch
    {
        MacKeychain.CwaAccount => CwaApiKeyProtected, MacKeychain.RemoteAccount => RemoteApiKeyProtected, MacKeychain.ClaudeAccount => ClaudeApiKeyProtected,
        MacKeychain.OpenAiAccount => OpenAiApiKeyProtected, MacKeychain.GeminiAccount => GeminiApiKeyProtected, _ => throw new ArgumentOutOfRangeException(nameof(account))
    };
    private void StoreSecret(string account, string? stored)
    {
        switch (account)
        {
            case MacKeychain.CwaAccount: CwaApiKeyProtected = stored; break;
            case MacKeychain.RemoteAccount: RemoteApiKeyProtected = stored; break;
            case MacKeychain.ClaudeAccount: ClaudeApiKeyProtected = stored; break;
            case MacKeychain.OpenAiAccount: OpenAiApiKeyProtected = stored; break;
            case MacKeychain.GeminiAccount: GeminiApiKeyProtected = stored; break;
            default: throw new ArgumentOutOfRangeException(nameof(account));
        }
    }
    private string Revealed(string account) { lock (revealed) return revealed.GetValueOrDefault(account, ""); }
    internal SecretState StateOf(string account) { lock (revealed) return secretStates.GetValueOrDefault(account).State; }
    // Why the secret is not simply loaded, for the settings page; null when it is.
    internal string? SecretProblem(string account) { lock (revealed) return secretStates.GetValueOrDefault(account) is { State: not (SecretState.Loaded or SecretState.Unread) } entry ? entry.Problem : null; }
    // A secret a later read may still bring back: the keychain did not answer, or a legacy value has not moved yet.
    internal bool SecretsPending { get { lock (revealed) return secretStates.Values.Any(entry => entry.State is SecretState.Failed or SecretState.Legacy); } }
    private string? Conceal(string value, string account)
    {
        value = value.Trim();
        MacKeychain.Write(account, value);
        lock (revealed) { revealed[account] = value; secretStates[account] = (SecretState.Loaded, null); }
        return value.Length == 0 ? null : KeychainMarker;
    }
    private static string Reveal(string? stored, string account) => stored == KeychainMarker ? MacKeychain.Read(account) : "";

    // Blocking: reads every secret that is not loaded yet (all of them the first time, later only those whose read failed).
    // A legacy value moves into the keychain; Moved then says to save. A read that fails leaves the secret Failed for a later
    // call, and a later read never replaces a value the user saved meanwhile. The first failure is returned (it carries no
    // secret).
    internal (bool Moved, Exception? Error) LoadSecrets()
    {
        bool moved = false;
        Exception? error = null;
        foreach (var account in SecretAccounts)
        {
            if (StateOf(account) is SecretState.Loaded or SecretState.Unsaved) continue;
            var stored = StoredSecret(account);
            try
            {
                if (stored is null or KeychainMarker)
                {
                    var value = Reveal(stored, account);
                    lock (revealed) if (secretStates.GetValueOrDefault(account).State is not SecretState.Loaded) { revealed[account] = value; secretStates[account] = (SecretState.Loaded, null); }
                    continue;
                }
                string legacy;
                try { legacy = Encoding.UTF8.GetString(Convert.FromBase64String(stored)).Trim(); }
                catch (FormatException unreadable)
                {
                    // Not Base64 (an edited or damaged file): nothing to move, so it leaves the file as well.
                    lock (revealed) if (secretStates.GetValueOrDefault(account).State is not SecretState.Loaded) secretStates[account] = (SecretState.Unsaved, "設定檔裡舊版的內容無法讀取。");
                    StoreSecret(account, null);
                    moved = true;
                    error ??= unreadable;
                    continue;
                }
                lock (revealed) { if (secretStates.GetValueOrDefault(account).State is SecretState.Loaded) continue; revealed[account] = legacy; secretStates[account] = (SecretState.Legacy, null); }
                try
                {
                    var marker = Conceal(legacy, account);
                    StoreSecret(account, marker);
                    moved = true;
                }
                catch (ArgumentException refused)
                {
                    // For good: the value stays usable in memory for this run, and the file stops carrying it.
                    lock (revealed) secretStates[account] = (SecretState.Unsaved, refused.Message);
                    StoreSecret(account, null);
                    moved = true;
                    error ??= refused;
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                lock (revealed)
                    if (secretStates.GetValueOrDefault(account).State is not SecretState.Loaded)
                        secretStates[account] = (stored is null or KeychainMarker ? SecretState.Failed : SecretState.Legacy, e.Message);
                error ??= e;
            }
        }
        return (moved, error);
    }
#else
    internal string CwaApiKey { get => Reveal(CwaApiKeyProtected); set => CwaApiKeyProtected = Conceal(value); }
    internal string RemoteApiKey { get => Reveal(RemoteApiKeyProtected); set => RemoteApiKeyProtected = Conceal(value); }
    internal string ClaudeApiKey { get => Reveal(ClaudeApiKeyProtected); set => ClaudeApiKeyProtected = Conceal(value); }
    internal string OpenAiApiKey { get => Reveal(OpenAiApiKeyProtected); set => OpenAiApiKeyProtected = Conceal(value); }
    internal string GeminiApiKey { get => Reveal(GeminiApiKeyProtected); set => GeminiApiKeyProtected = Conceal(value); }
    internal static string? Conceal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = Encoding.UTF8.GetBytes(value.Trim());
#if COMPANION_LINUX
        // No DPAPI on Linux: the value stays in the user's own data directory, unencrypted; Save makes the files 0600.
        return Convert.ToBase64String(bytes);
#else
        return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
#endif
    }
    internal static string Reveal(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
#if COMPANION_LINUX
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored));
#else
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
#endif
        }
        catch { return ""; }
    }
#endif

    public static CompanionPreferences Read()
    {
        foreach (var p in new[]
        {
            "preferences.json",
            "preferences.json.bak"
        }

        )
            try
            {
                var s = JsonSerializer.Deserialize<CompanionPreferences>(File.ReadAllText(p));
                if (s != null)
                {
#if COMPANION_SAFE && (COMPANION_LINUX || COMPANION_MAC)
                    // Both editions share one data folder here: Save() writes these back as the Standard edition left them.
                    s.standardAgent = (s.AgentEnabled, s.PuppetMode, s.AgentDailySpeech, s.McpEnabled, s.McpChatEnabled);
#endif
#if COMPANION_SAFE
                    s.AgentEnabled = false;
                    s.McpEnabled = false;
                    s.PuppetMode = false;
                    s.McpChatEnabled = false;
                    s.McpWakeEnabled = false;
                    s.AgentDailySpeech = false;
#endif
#if !COMPANION_SAFE
                    // A harness no longer offered (one an older version saved) reads as the default, so the settings
                    // page selects what the connection would actually use.
                    if (!BigBlueFish.Agent.HarnessCatalog.All.Any(h => h.Id == s.AgentHarnessId)) s.AgentHarnessId = "codex";
#endif
                    if (!s.AnyAgentControl) s.PuppetMode = false;
                    s.Scale = double.IsFinite(s.Scale) ? Math.Clamp(s.Scale, .5, 2.0) : 1;
                    if (s.Left.HasValue && !double.IsFinite(s.Left.Value)) s.Left = null;
                    if (s.Top.HasValue && !double.IsFinite(s.Top.Value)) s.Top = null;
                    s.BubbleScale = double.IsFinite(s.BubbleScale) ? Math.Clamp(s.BubbleScale, .5, 2.0) : 1;
                    s.AnimationIntervalSeconds = Math.Clamp(s.AnimationIntervalSeconds, 30, 300);
                    s.BreakfastHour = Math.Clamp(s.BreakfastHour, 0, 23);
                    s.LunchHour = Math.Clamp(s.LunchHour, 0, 23);
                    s.DinnerHour = Math.Clamp(s.DinnerHour, 0, 23);
                    s.SpeechMinMinutes = NormalizeSpeechMinimum(s.SpeechMinMinutes);
                    if (!SitReminderChoices.Contains(s.SitReminderMinutes)) s.SitReminderMinutes = 60;
                    // -1 was off before LateReminder had a switch of its own.
                    if (s.LateReminderHour == -1) s.LateReminder = false;
                    if (!LateReminderChoices.Contains(s.LateReminderHour)) s.LateReminderHour = 0;
                    if (!WelcomeBackChoices.Contains(s.WelcomeBackMinutes)) s.WelcomeBackMinutes = 60;
                    if (!EventReminderChoices.Contains(s.EventReminderMinutes)) s.EventReminderMinutes = 10;
                    s.BubbleDurationIndex = Math.Clamp(s.BubbleDurationIndex, 0, BubbleDurations.Length - 1);
                    s.WeatherRegionCode = CompanionRegions.District(s.WeatherRegionCode).Code;
                    s.ModelSource = s.ModelSource is "Remote" or "Cloud" ? s.ModelSource : "Local";
                    s.RemoteUrl ??= ""; s.RemoteModel ??= "";
                    if (!CloudProviders.Contains(s.CloudProvider)) s.CloudProvider = CloudProviders[0];
                    s.ClaudeModel = s.ClaudeModel?.Trim() ?? ""; s.OpenAiModel = s.OpenAiModel?.Trim() ?? ""; s.GeminiModel = s.GeminiModel?.Trim() ?? "";
                    if (!CloudDailyLimitChoices.Contains(s.CloudDailyLimit)) s.CloudDailyLimit = 200;
                    return s;
                }
            }
            catch
            {
            }

        return new();
    }

#if COMPANION_SAFE && (COMPANION_LINUX || COMPANION_MAC)
    // The Standard edition's own Agent and MCP interface switches, as read; the Safe edition runs with them off but never overwrites them.
    (bool AgentEnabled, bool PuppetMode, bool AgentDailySpeech, bool McpEnabled, bool McpChatEnabled) standardAgent;
#endif

    public void Save()
    {
        var t = "preferences.json.tmp";
#if COMPANION_SAFE && (COMPANION_LINUX || COMPANION_MAC)
        var node = JsonSerializer.SerializeToNode(this)!.AsObject();
        node[nameof(AgentEnabled)] = standardAgent.AgentEnabled;
        node[nameof(PuppetMode)] = standardAgent.PuppetMode;
        node[nameof(AgentDailySpeech)] = standardAgent.AgentDailySpeech;
        node[nameof(McpEnabled)] = standardAgent.McpEnabled;
        node[nameof(McpChatEnabled)] = standardAgent.McpChatEnabled;
        string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
#else
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
#endif
#if COMPANION_LINUX
        // The Linux secrets are only Base64, so the file (and the backup File.Replace leaves) is for this user alone.
        File.Delete(t);
        using (var file = new FileStream(t, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
        using (var writer = new StreamWriter(file))
            writer.Write(json);
#else
        File.WriteAllText(t, json);
#endif
        if (File.Exists("preferences.json"))
            File.Replace(t, "preferences.json", "preferences.json.bak");
        else
            File.Move(t, "preferences.json");
#if COMPANION_LINUX
        if (File.Exists("preferences.json.bak")) File.SetUnixFileMode("preferences.json.bak", UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
    }
}
