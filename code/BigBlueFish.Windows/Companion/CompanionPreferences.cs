// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
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
    public bool StopModelOnBattery { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
#if COMPANION_MAC
    // The pet, its bubble and the input box on every desktop Space (Mission Control leaves them in place).
    public bool ShowOnAllSpaces { get; set; } = true;
#endif
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool AgentEnabled { get; set; }
    public string AgentHarnessId { get; set; } = "codex";
    public bool AgentDailySpeech { get; set; }
    public bool AgentRespondToClick { get; set; }
    public bool AgentRespondToDrag { get; set; }
    public bool ChatLogShowAll { get; set; }
#if COMPANION_LINUX || COMPANION_MAC
    // Old UI/animation helpers use this internal alias. Legacy MCP settings do not turn on a new CLI connection.
    [JsonIgnore] public bool McpEnabled { get => AgentEnabled; set => AgentEnabled = value; }
#else
    // The Windows MCP interface for external clients (BigBlueFish.Mcp on the fixed pipe, such as the Hermes bridge).
    // It is switched apart from the ACP connection above and never turns that connection on; MCP can only read it.
    public bool McpEnabled { get; set; }
#endif
    public bool PuppetMode { get; set; }
#if COMPANION_LINUX || COMPANION_MAC
    [JsonIgnore] public bool McpChatEnabled { get => AgentEnabled; set { } }
#else
    // Double-click input for the external MCP clients; with the ACP connection on, the input is always offered.
    public bool McpChatEnabled { get; set; }
#endif
    [JsonIgnore] public bool McpWakeEnabled { get => false; set { } }
    [JsonIgnore] public bool McpWakeOnText { get => true; set { } }
    [JsonIgnore] public bool McpWakeOnClick { get => AgentRespondToClick; set => AgentRespondToClick = value; }
    [JsonIgnore] public bool McpWakeOnDrag { get => AgentRespondToDrag; set => AgentRespondToDrag = value; }
    // Either way of handing the fish to an agent: the ACP connection or (Windows) the external MCP interface.
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
    // The fish's own reminders (CompanionReminders, Windows only): minutes of use before a break reminder (0 = off)
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
        "5秒",
        "10秒",
        "15秒",
        "30秒",
        "1分鐘",
        "2分鐘",
        "3分鐘",
        "直至下一句"
    };
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string ModelSource { get; set; } = "Local";
    public string RemoteUrl { get; set; } = "";
    public string RemoteModel { get; set; } = "";
    public string? RemoteApiKeyProtected { get; set; }
    public bool ScreenImages { get; set; }
    public string? CwaApiKeyProtected { get; set; }

    // Secrets never appear in MCP get_settings (which also leaves out every [JsonIgnore] alias above), and update_settings
    // refuses them; any process of this user could still read them.
    internal static readonly string[] PrivateSettings = [nameof(RemoteApiKeyProtected), nameof(CwaApiKeyProtected)];
    internal bool UseRemoteModel => ModelSource == "Remote" && Uri.TryCreate(RemoteUrl.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https";
#if COMPANION_MAC
    // macOS keeps both secrets in the login keychain (MacKeychain); preferences.json only says "keychain" for a stored one.
    // LoadSecrets reads them into memory once, off the UI thread, and the getters return those values without blocking.
    // Setting one writes the keychain at once and blocks for up to a few seconds, so the settings page sets it off the
    // UI thread and saves afterwards; a refused write throws, keeps the old value and never leaves a copy in the file.
    internal const string KeychainMarker = "keychain";
    private readonly Dictionary<string, string> revealed = new();
    internal string CwaApiKey { get => Revealed(MacKeychain.CwaAccount); set => CwaApiKeyProtected = Conceal(value, MacKeychain.CwaAccount); }
    internal string RemoteApiKey { get => Revealed(MacKeychain.RemoteAccount); set => RemoteApiKeyProtected = Conceal(value, MacKeychain.RemoteAccount); }
    private string Revealed(string account) { lock (revealed) return revealed.GetValueOrDefault(account, ""); }
    private string? Conceal(string value, string account)
    {
        value = value.Trim();
        MacKeychain.Write(account, value);
        lock (revealed) revealed[account] = value;
        return value.Length == 0 ? null : KeychainMarker;
    }
    private static string Reveal(string? stored, string account) => stored == KeychainMarker ? MacKeychain.Read(account) : "";

    // Blocking: reads the stored secrets from the keychain. A value an earlier build kept in the file (Base64) moves into
    // the keychain; the result then says to save. The first failure is returned (it carries no secret); that secret reads
    // as empty, so the weather uses its estimate until the keychain answers.
    internal (bool Moved, Exception? Error) LoadSecrets()
    {
        bool moved = false;
        Exception? error = null;
        foreach (var account in new[] { MacKeychain.CwaAccount, MacKeychain.RemoteAccount })
            try
            {
                var stored = account == MacKeychain.CwaAccount ? CwaApiKeyProtected : RemoteApiKeyProtected;
                if (stored is null or KeychainMarker) { var value = Reveal(stored, account); lock (revealed) revealed[account] = value; continue; }
                var legacy = Encoding.UTF8.GetString(Convert.FromBase64String(stored)).Trim();
                lock (revealed) revealed[account] = legacy;
                var marker = Conceal(legacy, account);
                if (account == MacKeychain.CwaAccount) CwaApiKeyProtected = marker; else RemoteApiKeyProtected = marker;
                moved = true;
            }
            catch (Exception e) when (e is IOException or ArgumentException or FormatException or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            { error ??= e; }
        return (moved, error);
    }
#else
    internal string CwaApiKey { get => Reveal(CwaApiKeyProtected); set => CwaApiKeyProtected = Conceal(value); }
    internal string RemoteApiKey { get => Reveal(RemoteApiKeyProtected); set => RemoteApiKeyProtected = Conceal(value); }
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
                    s.standardAgent = (s.AgentEnabled, s.PuppetMode, s.AgentDailySpeech);
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
                    s.ModelSource = s.ModelSource == "Remote" ? "Remote" : "Local";
                    s.RemoteUrl ??= ""; s.RemoteModel ??= "";
                    return s;
                }
            }
            catch
            {
            }

        return new();
    }

#if COMPANION_SAFE && (COMPANION_LINUX || COMPANION_MAC)
    // The Standard edition's own Agent switches, as read; the Safe edition runs with them off but never overwrites them.
    (bool AgentEnabled, bool PuppetMode, bool AgentDailySpeech) standardAgent;
#endif

    public void Save()
    {
        var t = "preferences.json.tmp";
#if COMPANION_SAFE && (COMPANION_LINUX || COMPANION_MAC)
        var node = JsonSerializer.SerializeToNode(this)!.AsObject();
        node[nameof(AgentEnabled)] = standardAgent.AgentEnabled;
        node[nameof(PuppetMode)] = standardAgent.PuppetMode;
        node[nameof(AgentDailySpeech)] = standardAgent.AgentDailySpeech;
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
