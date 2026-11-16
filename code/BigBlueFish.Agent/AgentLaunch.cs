// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BigBlueFish.Agent;

/// <summary>
/// What the host adds to the process of one harness (AgentCoordinator.Launch asks for it each time it starts one).
/// Environment: variables for that process alone; a null value removes one the fish itself inherited, an empty one keeps
/// the name with nothing in it (for a CLI that would otherwise fill it in from a .env file). Profile: a name for a setup
/// whose conversations the Agent keeps apart (empty for the Agent's own setup); the fish saves one conversation per
/// harness, workspace and profile. Unavailable: why the harness must not start now; nothing starts, and the status says
/// it. CredentialNote: the host supplies the credential, so the Agent's own sign-in is never offered (it could carry the
/// variables into a login script); an Agent that asks to sign in, or turns the credential down, shows this note instead.
/// Rejections: what an Agent that reports a turned-down credential as an ordinary error says then (looked for in the
/// error, ignoring case), so the note shows instead of a retry. Secrets: values among the variables (the key) that an
/// Agent's error or output may quote back; the coordinator puts *** in their place before it shows a status.
/// The values can be secrets: they go into the child's environment only, never to a file, the log, the ACP messages or
/// the saved conversation, and ToString never shows them.
/// </summary>
public sealed partial class AgentLaunch
{
    public static AgentLaunch Default { get; } = new("", new Dictionary<string, string?>());
    public string Profile { get; }
    public IReadOnlyDictionary<string, string?> Environment { get; }
    public string? Unavailable { get; }
    public string? CredentialNote { get; }
    public IReadOnlyList<string> Rejections { get; }
    // Kept in memory only, to tell whether a new process is needed; it never leaves the coordinator.
    internal string Fingerprint { get; }
    // Kept in memory only, to mask in what the coordinator shows (they are among the variables, so the fingerprint has them).
    internal IReadOnlyList<string> Secrets { get; }

    public AgentLaunch(string profile, IReadOnlyDictionary<string, string?> environment, string? unavailable = null, string? credentialNote = null,
        IReadOnlyList<string>? rejections = null, IReadOnlyList<string>? secrets = null)
    {
        if (profile.Length > 0 && !ProfilePattern().IsMatch(profile)) throw new ArgumentException("設定檔名稱只能用小寫英文字母、數字和連字號。", nameof(profile));
        Profile = profile; Environment = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
        Unavailable = unavailable; CredentialNote = credentialNote; Rejections = rejections?.ToArray() ?? [];
        Secrets = secrets?.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var text = new StringBuilder().Append(profile).Append('\0').Append(unavailable).Append('\0').Append(credentialNote);
        foreach (var phrase in Rejections) text.Append('\0').Append(phrase);
        foreach (var pair in Environment.OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append('\0').Append(pair.Key).Append('=').Append(pair.Value == null ? "\u0001" : "\u0002" + pair.Value);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // Into the harness's start information (and from there into the terminal login's, which never runs with a CredentialNote).
    internal void Apply(ProcessStartInfo start)
    {
        foreach (var (name, value) in Environment)
            if (value == null) start.Environment.Remove(name); else start.Environment[name] = value;
    }

    public override string ToString() => Profile.Length == 0 ? "AgentLaunch(default)" : "AgentLaunch(" + Profile + ")";

    [GeneratedRegex("^[a-z0-9-]{1,40}$")] private static partial Regex ProfilePattern();
}

// A start the host's launch settings refuse (AgentLaunch.Unavailable); the message is the reason, shown as the detail.
internal sealed class AgentLaunchUnavailableException(string message) : IOException(message);

/// <summary>
/// An Agent CLI on an API Key of the cloud AI service, billed per use, instead of the sign-in the user gave it: Claude Code
/// on the Claude API Key (not its subscription), Codex CLI on the OpenAI API Key (not its ChatGPT sign-in), Gemini CLI on
/// the Gemini API Key (not its Google sign-in). Each of them prefers the sign-in it already has, or changes it when told to
/// use a key, so the CLI the fish starts gets a configuration folder of its own (inside the Agent's data folder, for this
/// user only) and the key in its environment; the user's own CLI keeps its folder and its sign-in. The CLI keeps its
/// conversations in that folder too, so they are a profile of their own, named as the folder. Variables in the fish's
/// own environment that would send the key to another address, put another credential before it or bill another way are
/// removed from that process (or set empty, where the CLI would fill them in from a .env file). This covers what the fish
/// hands on, not a workspace it did not write: the workspace's own settings for the CLI still apply.
/// The texts for the 連線 Agent page are here too, so the macOS, Linux and Windows pages say the same.
/// </summary>
public sealed class ApiKeyLaunch
{
    /// <summary>
    /// Claude Code with a subscription login uses it and ignores ANTHROPIC_API_KEY (Claude Code 2.1.311, checked against a
    /// local server), so it gets CLAUDE_CONFIG_DIR of its own, empty but for what Claude Code writes there. The workspace's
    /// .claude/settings.json and settings.local.json still apply (Claude Code reads their env after the process
    /// environment), so a base URL or a hook set there can still take the key elsewhere.
    /// </summary>
    public static ApiKeyLaunch Claude { get; } = new()
    {
        Harness = "claude", Profile = "claude-api", KeyName = "Claude API Key", Vendor = "Anthropic", KeyConsole = "Claude Console",
        SpendLimit = "支出上限", OwnSignIn = "訂閱登入", Fallback = "訂閱登入", SignInExample = "訂閱", SettingsName = "settings.json",
        FolderVariable = "CLAUDE_CONFIG_DIR", KeyVariable = "ANTHROPIC_API_KEY",
        // A bearer or OAuth token would be used before the key (or beside it); a base URL or another backend would take the
        // key elsewhere.
        Removed = ["ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY"],
        // A model picked under 模型與權限 lasts for the current conversation only (claude-agent-acp's set_config_option
        // changes the live session); settings.json in Claude Code's own folder holds one for every conversation.
        EditableSettings = true, EmptySettings = "{}\n",
        // Claude Code quietly does without a settings.json it cannot read, so a broken one (a curly quote from a text
        // editor) is found here, before the start, and nothing is written into its folder.
        Prepare = folder => ReadUserSettings("Claude Code", Path.Combine(folder, "settings.json")),
        ModelHintText = file => "想一直用比較省錢的 Sonnet 或 Haiku，請按「" + OpenSettingsLabel + "」，用文字編輯器打開裡面的 " + Path.GetFileName(file)
            + "，在最外層的大括號裡加上 \"model\": \"sonnet\"（或 \"haiku\"），例如 {\"model\": \"sonnet\"}" + QuoteNote
            + "已經開啟這個選項時，存檔後按「重新連線」就會套用。這個檔案只影響魚魚用 API Key 啟動的 Claude Code，第一輪和之後每一段對話都會用這個模型。"
            + "下方「模型與權限」選的模型只套用到目前這段對話，按「開始新對話」後會回到這個檔案指定的模型（檔案裡沒有 \"model\" 時，例如按鈕剛建立的 {}，是 Claude Code 預設的模型）。",
        ModelAdvice = file => "Claude Code 會用它預設的模型；想一直用比較省錢的 Sonnet 或 Haiku，請先按這個選項下方的「" + OpenSettingsLabel + "」，在 " + Path.GetFileName(file)
            + " 加上 \"model\": \"sonnet\"（或 \"haiku\"）再開啟，第一輪就會用它。「模型與權限」選的模型只套用到目前這段對話。",
        Exposure = "，所以它和它執行的指令也可能讀得到；工作區 .claude 資料夾裡設定檔的 env 和 hooks 也會生效，可能把 Key 送到別的主機，所以不要把來源不明的專案當作工作區。",
        WorkspaceNote = "工作區的 .claude/settings.local.json 照樣適用。",
    };

    /// <summary>
    /// Codex CLI through codex-acp (2.5.0 with Codex 0.165, checked against a local server): with the user's CODEX_HOME its
    /// ChatGPT login wins over the key, and an API Key sign-in there would replace that login in ~/.codex/auth.json. So it
    /// gets CODEX_HOME of its own, whose config.toml (written by the fish before each start) keeps the key in memory only
    /// (cli_auth_credentials_store = "ephemeral", Codex 0.93 or later), allows an API key only (forced_login_method) and
    /// keeps the key out of the environment of the commands Codex runs (shell_environment_policy). The adapter signs in with CODEX_API_KEY by
    /// itself whenever a session finds no account (DEFAULT_AUTH_REQUEST, which the adapter logs, so it never holds the key);
    /// NO_BROWSER leaves the ChatGPT sign-in out. A key OpenAI turns down comes back as ACP's "Authentication required".
    /// </summary>
    public static ApiKeyLaunch Codex { get; } = new()
    {
        Harness = "codex", Profile = "codex-api", KeyName = "OpenAI API Key", Vendor = "OpenAI", KeyConsole = "OpenAI Platform",
        SpendLimit = "支出上限或預算提醒", OwnSignIn = "原本的登入（例如 ChatGPT 帳號）", Fallback = "原本的登入", SignInExample = "ChatGPT 帳號",
        SettingsName = "config.toml", FolderVariable = "CODEX_HOME", KeyVariable = "CODEX_API_KEY",
        Fixed = new Dictionary<string, string> { ["DEFAULT_AUTH_REQUEST"] = "{\"methodId\":\"api-key\"}", ["NO_BROWSER"] = "1" },
        // The user's own key (CODEX_API_KEY already comes first; one copy is enough), a ChatGPT access token or a workload
        // identity Codex would sign in with, an organization or project sent beside the key, a base URL, the adapter's
        // own settings that can route a session to another provider, its log of what it sends Codex (the key included)
        // and a state folder shared with the user's own Codex.
        Removed = ["OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_ACCESS_TOKEN", "OPENAI_FEDERATION_RULE_ID", "OPENAI_IDENTITY_TOKEN_FILE",
            "OPENAI_WORKLOAD_IDENTITY_CONTEXT", "OPENAI_ORGANIZATION", "OPENAI_PROJECT", "CODEX_CONFIG", "MODEL_PROVIDER", "APP_SERVER_LOGS", "CODEX_SQLITE_HOME"],
        Rejections = ["invalid_api_key", "Incorrect API key provided"],
        Prepare = folder => WriteIfChanged(Path.Combine(folder, "config.toml"), CodexConfig),
        ModelHintText = file => "Codex CLI 會用它預設的模型；想換比較省錢的模型，可以在下方「模型與權限」選擇。"
            + "魚魚每次啟動這個 Codex CLI 前，都會重新寫入 " + file + "（讓它只用這把 API Key、不把 Key 存進檔案，也不交給它執行的指令），請不要修改這個檔案。",
        ModelAdvice = _ => "Codex CLI 會用它預設的模型；想換比較省錢的模型，可以在「模型與權限」選擇。",
        // shell_environment_policy keeps the key out of the environment of each command, not out of reach: a command outside
        // Codex's sandbox (the adapter's Full access mode, or one the user approved to run outside it) can read the
        // environment of the adapter or of Codex CLI itself, as the same user may (/proc/<pid>/environ on Linux, ps eww on macOS).
        Exposure = "，不會寫進檔案；魚魚也設定 Codex CLI 不把它放進所執行指令的環境變數。不過 Codex CLI 和它的 ACP 轉接器本身有它，"
            + "在沙箱外執行的指令（例如在「模型與權限」選了 Full access，或你核准在沙箱外執行的指令）仍可能從這兩個程式讀到，所以不要把來源不明的專案當作工作區。",
    };

    /// <summary>
    /// Gemini CLI (0.68.0, checked against a local server): with the user's ~/.gemini its selectedType (a Google sign-in,
    /// Vertex AI) wins over GEMINI_API_KEY, and its MCP servers and model apply. So it gets GEMINI_CLI_HOME of its own,
    /// whose .gemini/settings.json selects the Gemini API Key. Gemini CLI fills in variables the process lacks from the
    /// first .env file it finds from the workspace upward (the user's ~/.gemini/.env or ~/.env among them), so the ones
    /// that would take the key elsewhere or change the sign-in are set empty, not removed. Its trusted folders are in that
    /// home too, and an untrusted workspace starts no MCP server (the fish's included): GEMINI_CLI_TRUST_WORKSPACE trusts
    /// the workspace; GEMINI_FORCE_FILE_STORAGE keeps it away from the keychain. A key Google turns down comes back as an
    /// ordinary error with Google's own JSON in it: 400 API_KEY_INVALID, or 403 for a key that is blocked
    /// (API_KEY_SERVICE_BLOCKED, API_KEY_*_BLOCKED for a key restricted to other referrers, addresses or apps), suspended
    /// (CONSUMER_SUSPENDED, whose message can quote the key) or reported as leaked; hence the Rejections.
    /// </summary>
    public static ApiKeyLaunch Gemini { get; } = new()
    {
        Harness = "gemini", Profile = "gemini-api", KeyName = "Gemini API Key", Vendor = "Google", KeyConsole = "Google AI Studio",
        SpendLimit = "支出上限或預算提醒", OwnSignIn = "原本的登入（例如 Google 帳號）", Fallback = "原本的登入", SignInExample = "Google 帳號",
        SettingsName = ".gemini/settings.json", FolderVariable = "GEMINI_CLI_HOME", KeyVariable = "GEMINI_API_KEY",
        Fixed = new Dictionary<string, string> { ["GEMINI_CLI_TRUST_WORKSPACE"] = "true", ["GEMINI_FORCE_FILE_STORAGE"] = "true" },
        Blanked = ["GOOGLE_GEMINI_BASE_URL", "GEMINI_API_KEY_AUTH_MECHANISM", "GEMINI_CLI_CUSTOM_HEADERS", "GOOGLE_API_KEY", "GOOGLE_GENAI_USE_VERTEXAI",
            "GOOGLE_GENAI_USE_GCA", "GEMINI_CLI_AUTH_OVERRIDE", "GOOGLE_GENAI_API_VERSION", "GOOGLE_VERTEX_BASE_URL", "GOOGLE_CLOUD_PROJECT",
            "GOOGLE_CLOUD_PROJECT_ID", "GOOGLE_CLOUD_LOCATION", "GOOGLE_APPLICATION_CREDENTIALS", "CLOUD_SHELL", "GEMINI_CLI_USE_COMPUTE_ADC", "GEMINI_DEFAULT_AUTH_TYPE"],
        Rejections = ["API_KEY_INVALID", "API key not valid", "API_KEY_SERVICE_BLOCKED", "API_KEY_HTTP_REFERRER_BLOCKED", "API_KEY_IP_ADDRESS_BLOCKED",
            "API_KEY_ANDROID_APP_BLOCKED", "API_KEY_IOS_APP_BLOCKED", "CONSUMER_SUSPENDED", "reported as leaked"],
        Prepare = GeminiSettings,
        EditableSettings = true,
        ModelHintText = file => "想一直用比較省錢的模型，請按「" + OpenSettingsLabel + "」，用文字編輯器打開裡面的 " + Path.GetFileName(file)
            + "，在最外層的大括號裡加上 \"model\": {\"name\": \"gemini-3-flash-preview\"}（模型名稱以 Google 的說明為準），和其他設定之間用逗號隔開" + QuoteNote
            + "已經開啟這個選項時，存檔後按「重新連線」就會套用。這個檔案只影響魚魚用 API Key 啟動的 Gemini CLI；魚魚每次啟動前會把裡面的登入方式設回 Gemini API Key，其他設定都會保留。",
        ModelAdvice = file => "Gemini CLI 會用它預設的模型（由 Gemini CLI 自動挑選）；想一直用比較省錢的模型，請先按這個選項下方的「" + OpenSettingsLabel + "」，在 " + Path.GetFileName(file)
            + " 加上 \"model\": {\"name\": \"gemini-3-flash-preview\"} 再開啟，第一輪就會用它。",
        Exposure = "，所以它和它執行的指令也可能讀得到；工作區 .gemini 資料夾裡的設定和工作區的 .env 檔也會生效，可能改變登入方式或把 Key 送到別的主機，所以不要把來源不明的專案當作工作區。",
        WorkspaceNote = "工作區的 .gemini/settings.json 照樣適用。",
    };

    public static IReadOnlyList<ApiKeyLaunch> All { get; } = [Claude, Codex, Gemini];
    private ApiKeyLaunch() { }
    /// <summary>The API Key launch a harness offers, or null for the harnesses that have none.</summary>
    public static ApiKeyLaunch? For(string? harness) => All.FirstOrDefault(l => l.Harness == harness);
    /// <summary>The API Key launch an AgentLaunch.Profile belongs to (null for the Agent's own setup).</summary>
    public static ApiKeyLaunch? ForProfile(string? profile) => All.FirstOrDefault(l => l.Profile == profile);

    /// <summary>HarnessCatalog id.</summary>
    public required string Harness { get; init; }
    /// <summary>The AgentLaunch profile, and the name of the configuration folder inside the Agent's data folder.</summary>
    public required string Profile { get; init; }
    public string Product => HarnessCatalog.Get(Harness).Name;
    /// <summary>The key's official name, such as OpenAI API Key.</summary>
    public required string KeyName { get; init; }
    /// <summary>The company that bills the key.</summary>
    public required string Vendor { get; init; }
    /// <summary>Where the user manages the key and its spending.</summary>
    public required string KeyConsole { get; init; }
    /// <summary>What to set up there before turning the option on.</summary>
    public required string SpendLimit { get; init; }
    /// <summary>The sign-in the user's own CLI keeps (照樣使用…).</summary>
    public required string OwnSignIn { get; init; }
    /// <summary>The same in short: what is never used in the key's place (不會改用…), and what 關閉 goes back to (改回…).</summary>
    public required string Fallback { get; init; }
    public required string SignInExample { get; init; }
    /// <summary>The CLI's own settings file, relative to the configuration folder.</summary>
    public required string SettingsName { get; init; }
    /// <summary>Whether the user may edit that file (a model, for one); the page then offers 開啟設定檔資料夾. Codex CLI's
    /// config.toml is the fish's own, written again before each start.</summary>
    public bool EditableSettings { get; init; }
    /// <summary>The button under the model hint that shows the settings file in the file manager (EnsureSettingsFile).</summary>
    public const string OpenSettingsLabel = "開啟設定檔資料夾";
    /// <summary>What the button writes for a settings file that is not there yet; null: the settings Prepare writes.</summary>
    internal string? EmptySettings { get; init; }
    // Text editors that turn " into curly quotes make the file invalid JSON; TextEdit does it by default (智慧型引號).
    static string QuoteNote => "；引號要用半形的 \"，不能用 “ ” 這種彎引號"
        + (OperatingSystem.IsMacOS() ? "（用「文字編輯」時，請先在「編輯 › 替代」取消勾選「智慧型引號」）" : "") + "。";
    internal string FolderVariable { get; init; } = "";
    internal string KeyVariable { get; init; } = "";
    internal IReadOnlyDictionary<string, string> Fixed { get; init; } = new Dictionary<string, string>();
    internal IReadOnlyList<string> Removed { get; init; } = [];
    internal IReadOnlyList<string> Blanked { get; init; } = [];
    internal IReadOnlyList<string> Rejections { get; init; } = [];
    // Writes what the CLI must read from its configuration folder before it starts (given the folder).
    internal Action<string>? Prepare { get; init; }
    internal Func<string, string> ModelHintText { get; init; } = _ => "";
    internal Func<string, string> ModelAdvice { get; init; } = _ => "";
    internal string Exposure { get; init; } = "";
    internal string WorkspaceNote { get; init; } = "";

    /// <summary>The folder the CLI uses as its configuration folder in this mode, inside the Agent's data folder.</summary>
    public string ConfigFolder(string agentRoot) => Path.Combine(Path.GetFullPath(agentRoot), Profile);
    /// <summary>The CLI's settings in that folder, read when it starts; only this CLI reads them, never the user's own.</summary>
    public string SettingsFile(string agentRoot) => Path.Combine([ConfigFolder(agentRoot), .. SettingsName.Split('/')]);

    /// <summary>
    /// For 開啟設定檔資料夾: the CLI's settings file, so the user can open it and edit it. The configuration folder is made
    /// as Create makes it (for this user only, never through a link); a file that is not there yet is written with what
    /// the CLI would start from (Claude Code: an empty object; Gemini CLI: the settings the fish writes before a start),
    /// so there is a file to open. One that is there is left as it is, even when it is not valid JSON: that is what the
    /// user comes to fix.
    /// </summary>
    public string EnsureSettingsFile(string agentRoot)
    {
        if (!EditableSettings) throw new InvalidOperationException(Product + " 的設定檔由魚魚管理，請不要修改。");
        string folder = ConfigFolder(agentRoot), file = SettingsFile(agentRoot);
        PrivateFolder(folder, "先不開啟這個資料夾");
        if (File.Exists(file)) return file;
        if (EmptySettings != null) AtomicText.Write(file, EmptySettings); else Prepare?.Invoke(folder);
        return file;
    }

    /// <summary>With a key: the private configuration folder (created, for this user only, with what the CLI must read
    /// there) and the variables. Without one: nothing starts, and <paramref name="missing"/> says why (the CLI's own
    /// sign-in is never used in its place).</summary>
    public AgentLaunch Create(string agentRoot, string? key, string missing)
    {
        key = key?.Trim() ?? "";
        if (key.Length == 0) return new(Profile, new Dictionary<string, string?>(), missing);
        string folder = ConfigFolder(agentRoot);
        PrivateFolder(folder, "先不啟動 " + Product);
        Prepare?.Invoke(folder);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { [FolderVariable] = folder, [KeyVariable] = key };
        foreach (var (name, value) in Fixed) environment[name] = value;
        foreach (var name in Removed) environment[name] = null;
        foreach (var name in Blanked) environment[name] = "";
        return new(Profile, environment, credentialNote: CredentialNote, rejections: Rejections, secrets: [key]);
    }

    // The 連線 Agent page and the statuses. The model page is 「設定 › 模型與資訊」 on macOS and Linux, 「設定 › 模型與供電」 on Windows.
    /// <summary>The switch, as the page shows it.</summary>
    public string Label => Product + " 改用 " + KeyName + "（依用量計費）";
    /// <summary>The switch named in a sentence (「…」).</summary>
    public string Switch => Product + " 改用 " + KeyName;
    public string SignInDetail => KeyName + "（依用量計費）";
    public string OwnState => "目前使用 " + Product + " 自己的登入（例如" + (char.IsAscii(SignInExample[0]) ? " " : "") + SignInExample + "）。";
    public string KeySaved => "已儲存 " + KeyName + "，魚魚啟動的 " + Product + " 會用它連線。";
    // A rejected key waits (AgentCoordinator retries no further) until 重新連線 or a different launch: saving the same key
    // again changes nothing, so the note sends the user to 重新連線. Billing and quota errors are not rejections; they come
    // back as an ordinary failure with the CLI's own message and are retried later.
    public string CredentialNote => Product + " 拒絕了 " + KeyName + "（可能已失效、被撤銷或停用）。請到 " + KeyConsole + " 確認這把 API Key：Key 沒問題就按「重新連線」；"
        + "要換一把新的 Key，就到「設定」的雲端 AI 服務儲存，魚魚會自動重新啟動 " + Product + "；也可以關閉「" + Switch + "」，改回" + Fallback + "。";
    public string Description(string modelPage) => "開啟後，魚魚啟動的 " + Product + " 會改用「" + modelPage + "」雲端 AI 服務裡儲存的 " + KeyName + "，由 " + Vendor + " 依用量計費；"
        + "你自己開的 " + Product + " 照樣使用" + OwnSignIn + "，不受影響。" + Product + " 每一輪都會送出很長的上下文，用量比魚魚自己說一句話多很多，"
        + "建議先在 " + KeyConsole + " 設定" + SpendLimit + "。改用 API Key 後是另一段魚魚對話，關閉後會回到原本的那段" + (WorkspaceNote.Length > 0 ? "；" + WorkspaceNote : "。");
    public string ModelHint(string agentRoot) => ModelHintText(SettingsFile(agentRoot));
    public string Warning(string agentRoot) => "開啟後，魚魚啟動的 " + Product + " 會用你在雲端 AI 服務儲存的 " + KeyName + "，費用由 " + Vendor + " 依用量向這把 API Key 的帳戶收取。"
        + Product + " 每一輪都會送出很長的上下文（對話、讀到的檔案、工具結果），用量比魚魚自己說一句話多很多。\n\n"
        + "建議先到 " + KeyConsole + " 替這個帳戶設定" + SpendLimit + "。" + ModelAdvice(SettingsFile(agentRoot)) + "\n\n"
        + "API Key 只交給魚魚啟動的這個 " + Product + "，放在它的環境變數裡" + Exposure
        + "最好另外建立一把只給魚魚用的 API Key。你自己開的 " + Product + " 不受影響，照樣使用" + OwnSignIn + "。";
    /// <summary>Under the switch when no key is saved; <paramref name="service"/> is the cloud AI service's 服務 choice.</summary>
    public string MissingHint(string service) => "還沒有儲存 " + KeyName + "，" + Product + " 會先暫停（不會改用" + Fallback + "）。請按「前往雲端 AI 服務」，"
        + "模型來源選雲端 AI 服務、服務選「" + service + "」，貼上 API Key 後按「儲存並測試」；之後要讓魚魚自己說話改回原本的模型或服務也可以，API Key 會留著。";
    /// <summary>The status detail when the switch is on and no key is saved.</summary>
    public string Missing(string modelPage) => "已開啟「" + Switch + "」，但還沒有儲存 " + KeyName + "，所以先不啟動 " + Product + "（不會改用" + Fallback + "）。"
        + "請先在「" + modelPage + "」的雲端 AI 服務儲存 " + KeyName + "，或關閉這個選項。";

    // The config.toml of Codex CLI's own folder, written before each start (and only then rewritten when it differs).
    internal const string CodexConfig = "# 藍色大肥魚每次啟動 Codex CLI 前都會重新寫入這個檔案，請不要修改。\n"
        + "# 只用雲端 AI 服務的 OpenAI API Key（由環境變數提供）：只放在記憶體裡，也不交給 Codex CLI 執行的指令。\n"
        + "cli_auth_credentials_store = \"ephemeral\"\n"
        + "forced_login_method = \"api\"\n\n"
        + "[shell_environment_policy]\n"
        + "exclude = [\"CODEX_API_KEY\", \"OPENAI_API_KEY\"]\n";

    // Gemini CLI's user settings in its own folder: the sign-in is set to the Gemini API Key before each start; the rest is
    // the user's (a model, for one), with two defaults while unset: the key kept out of the environment of the commands it
    // runs, and no usage statistics (the opt-out the user may have in ~/.gemini does not reach this folder). Gemini CLI
    // allows comments there; a rewrite drops them. A file that is not JSON stops the start instead of being replaced.
    static void GeminiSettings(string folder)
    {
        string file = Path.Combine(folder, ".gemini", "settings.json");
        JsonObject settings = ReadUserSettings("Gemini CLI", file) ?? new JsonObject();
        var security = Section(settings, "security");
        Section(security, "auth")["selectedType"] = "gemini-api-key";
        if (security["environmentVariableRedaction"] is null) security["environmentVariableRedaction"] = new JsonObject { ["enabled"] = true };
        var privacy = Section(settings, "privacy");
        if (privacy["usageStatisticsEnabled"] is null) privacy["usageStatisticsEnabled"] = false;
        WriteIfChanged(file, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
    // A settings file the user edits (開啟設定檔資料夾), read with comments and trailing commas allowed: null when it is not
    // there or blank. One that is not a JSON object stops the start, and the reason sends the user back to the button.
    static JsonObject? ReadUserSettings(string product, string file)
    {
        string? text = File.Exists(file) ? File.ReadAllText(file) : null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new IOException(product + " 的設定檔不是有效的 JSON" + (text.IndexOfAny(['“', '”']) >= 0 ? "（裡面有 “ ” 這種彎引號，要改成半形的 \"）" : "")
                + "，先不啟動 " + product + "。請按 Agent 連線設定裡的「" + OpenSettingsLabel + "」，修正或刪除 " + Path.GetFileName(file) + " 後按「重新連線」。檔案位置：" + file);
        }
    }
    static JsonObject Section(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing) return existing;
        var created = new JsonObject(); parent[name] = created; return created;
    }
    static void WriteIfChanged(string file, string text)
    {
        if (File.Exists(file) && File.ReadAllText(file) == text) return;
        AtomicText.Write(file, text);
    }

    // For this user only, every time and not only when the folder is new. A link is refused: the rules would go to whatever
    // it points at, and the CLI would keep its sign-in there.
    // refused: what does not happen then (先不啟動 Claude Code, or 先不開啟這個資料夾 for the button).
    void PrivateFolder(string folder, string refused)
    {
        if (new DirectoryInfo(folder) is { Exists: true, LinkTarget: not null })
            throw new IOException(Product + " 的設定資料夾是一個連結，不是一般的資料夾；為了安全，" + refused + "。請刪除這個連結後再試：" + folder);
        if (OperatingSystem.IsWindows()) { PrivateWindowsFolder(folder, refused); return; }
        const UnixFileMode mine = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(folder, mine);
        File.SetUnixFileMode(folder, mine);
    }

    // Windows: the folder would inherit the data folder's rules, which the installer opens to every local user (Users: full
    // control). Its rules are replaced with this user and SYSTEM only, inheritance off (what is inside follows), every time,
    // since an installation or an update gives Users access to it again.
    [SupportedOSPlatform("windows")]
    void PrivateWindowsFolder(string folder, string refused)
    {
        var directory = Directory.CreateDirectory(folder);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Product + " 的設定資料夾是一個連結，不是一般的資料夾；為了安全，" + refused + "。請刪除這個連結後再試：" + folder);
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User ?? throw new IOException("無法取得目前 Windows 帳戶的識別碼，" + refused + "。");
        var security = new DirectorySecurity();
        security.SetOwner(me);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { me, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
