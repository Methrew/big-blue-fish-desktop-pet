// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace BigBlueFish.Agent;

/// <summary>
/// What the host adds to the process of one harness (AgentCoordinator.Launch asks for it each time it starts one).
/// Environment: variables for that process alone; a null value removes one the fish itself inherited. Profile: a name
/// for a setup whose conversations the Agent keeps apart (empty for the Agent's own setup); the fish saves one
/// conversation per harness, workspace and profile. Unavailable: why the harness must not start now; nothing starts,
/// and the status says it. CredentialNote: the host supplies the credential, so the Agent's own sign-in is never offered
/// (it could carry the variables into a login script); an Agent that asks to sign in shows this note instead.
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
    // Kept in memory only, to tell whether a new process is needed; it never leaves the coordinator.
    internal string Fingerprint { get; }

    public AgentLaunch(string profile, IReadOnlyDictionary<string, string?> environment, string? unavailable = null, string? credentialNote = null)
    {
        if (profile.Length > 0 && !ProfilePattern().IsMatch(profile)) throw new ArgumentException("設定檔名稱只能用小寫英文字母、數字和連字號。", nameof(profile));
        Profile = profile; Environment = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
        Unavailable = unavailable; CredentialNote = credentialNote;
        var text = new StringBuilder().Append(profile).Append('\0').Append(unavailable).Append('\0').Append(credentialNote);
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
/// Claude Code on a Claude API Key instead of the subscription the user signed in with. A Claude Code that has a
/// subscription login uses it and ignores ANTHROPIC_API_KEY (Claude Code 2.1.311, checked against a local server), so the
/// fish's Claude Code gets a configuration folder of its own (CLAUDE_CONFIG_DIR, empty but for what Claude Code writes
/// there) with the key in its environment; the user's own Claude Code keeps ~/.claude and the subscription. Conversations
/// live in that folder too, so they are a profile of their own. Variables in the fish's own environment that would send
/// the key to another address or bill another way are removed from that process. The workspace's .claude/settings.json
/// and settings.local.json still apply (Claude Code reads their env after the process environment), so a base URL or a
/// hook set there can still take the key elsewhere; this covers what the fish hands on, not a workspace it did not write.
/// </summary>
public static class ClaudeApiKeyLaunch
{
    public const string Harness = "claude", Profile = "claude-api", Folder = "claude-api";
    // Removed from the child: a bearer or OAuth token would be used before the key (or beside it); a base URL or another
    // backend would take the key elsewhere.
    internal static readonly string[] Removed = ["ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY"];
    // A rejected key waits (AgentCoordinator retries no further) until 重新連線 or a different launch: saving the same key
    // again changes nothing, so the note sends the user to 重新連線. Billing and quota errors are not rejections; they come
    // back as an ordinary failure with Claude Code's own message and are retried later.
    public const string CredentialNote = "Claude Code 拒絕了 Claude API Key（可能已失效、被撤銷或停用）。請到 Claude Console 確認這把 API Key：Key 沒問題就按「重新連線」；"
        + "要換一把新的 Key，就到「設定」的雲端 AI 服務儲存，魚魚會自動重新啟動 Claude Code；也可以關閉「Claude Code 改用 Claude API Key」，改回訂閱登入。";

    /// <summary>The folder Claude Code uses as its configuration folder in this mode, inside the Agent's data folder.</summary>
    public static string ConfigFolder(string agentRoot) => Path.Combine(Path.GetFullPath(agentRoot), Folder);
    /// <summary>Claude Code's user settings in that folder (for example {"model": "sonnet"}), read when it starts; only this
    /// Claude Code reads them, never the user's own.</summary>
    public static string SettingsFile(string agentRoot) => Path.Combine(ConfigFolder(agentRoot), "settings.json");

    /// <summary>With a key: the private configuration folder (created, for this user only) and the variables. Without one:
    /// nothing starts, and <paramref name="missing"/> says why (the subscription is never used in its place).</summary>
    public static AgentLaunch Create(string agentRoot, string? key, string missing)
    {
        key = key?.Trim() ?? "";
        if (key.Length == 0) return new(Profile, new Dictionary<string, string?>(), missing);
        string folder = ConfigFolder(agentRoot);
        if (OperatingSystem.IsWindows()) PrivateWindowsFolder(folder);
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["CLAUDE_CONFIG_DIR"] = folder, ["ANTHROPIC_API_KEY"] = key };
        foreach (var name in Removed) environment[name] = null;
        return new(Profile, environment, credentialNote: CredentialNote);
    }

    // Windows: the folder would inherit the data folder's rules, which the installer opens to every local user (Users: full
    // control). Its rules are replaced with this user and SYSTEM only, inheritance off (what is inside follows), every time
    // and not only when the folder is new, since an installation or an update gives Users access to it again. A link is
    // refused: the rules would go to whatever it points at.
    [SupportedOSPlatform("windows")]
    static void PrivateWindowsFolder(string folder)
    {
        var directory = Directory.CreateDirectory(folder);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Claude Code 的設定資料夾是一個連結，不是一般的資料夾；為了安全，先不啟動 Claude Code。請刪除這個連結後再試：" + folder);
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User ?? throw new IOException("無法取得目前 Windows 帳戶的識別碼，先不啟動 Claude Code。");
        var security = new DirectorySecurity();
        security.SetOwner(me);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { me, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
