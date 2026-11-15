// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using BigBlueFish.Agent;
using BigBlueFish.Control;
using BigBlueFish.Diagnostics;

// Claude Code on the Claude API Key (AgentLaunch, ClaudeApiKeyLaunch), through actual fixture processes started as the
// "claude" harness (shims named claude and claude-agent-acp): the variables reach that process only, the subscription's
// tokens and a redirecting base URL are taken out of it, a change starts a new process, no key means no start, the key
// stays out of the log, the saved conversations and the prompts, and each setup resumes its own conversation.
static class LaunchVerification
{
    // Shapes of real keys, not keys: nothing here talks to Anthropic.
    public const string FakeKey = "sk-ant-api03-FAKE-launch-verification-key-1";
    public const string OtherFakeKey = "sk-ant-api03-FAKE-launch-verification-key-2";
    const string Missing = "fixture: no Claude API Key saved";

    sealed record ChildEnvironment(int Pid, string? ConfigDir, string ApiKey, bool AuthToken, bool OauthToken, bool BaseUrl, bool Bedrock);

    // The fixture's side: what its own environment holds, by kind only (never the key).
    public static void Record()
    {
        static string? Variable(string name) => Environment.GetEnvironmentVariable(name);
        string key = Variable("ANTHROPIC_API_KEY") switch { null => "absent", FakeKey => "fake", OtherFakeKey => "fake2", _ => "other" };
        File.AppendAllText("fixture-env.log", JsonSerializer.Serialize(new ChildEnvironment(Environment.ProcessId, Variable("CLAUDE_CONFIG_DIR"), key,
            Variable("ANTHROPIC_AUTH_TOKEN") != null, Variable("CLAUDE_CODE_OAUTH_TOKEN") != null, Variable("ANTHROPIC_BASE_URL") != null,
            Variable("CLAUDE_CODE_USE_BEDROCK") != null)) + "\n");
    }

    [SupportedOSPlatform("windows")]
    static bool PrivateOnWindows(string folder)
    {
        var security = new DirectoryInfo(folder).GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        var allowed = new IdentityReference[] { identity.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) };
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        return security.AreAccessRulesProtected && rules.Length > 0
            && rules.All(r => !r.IsInherited && r.AccessControlType == AccessControlType.Allow && allowed.Contains(r.IdentityReference));
    }

    public static async Task Run(string root, string fixtureExe, string shimDirectory, Action<bool, string> check, Func<Func<bool>, string, int, Task> until)
    {
        // The library's own rules first.
        var none = ClaudeApiKeyLaunch.Create(Path.Combine(root, "rules"), "  ", Missing);
        check(none.Unavailable == Missing && none.Environment.Count == 0 && none.Profile == ClaudeApiKeyLaunch.Profile && !Directory.Exists(ClaudeApiKeyLaunch.ConfigFolder(Path.Combine(root, "rules"))),
            "no Claude API Key: nothing to start, no folder, the reason kept");
        var keyed = ClaudeApiKeyLaunch.Create(Path.Combine(root, "rules"), " " + FakeKey + "\n", Missing);
        check(keyed.Unavailable == null && keyed.Environment["ANTHROPIC_API_KEY"] == FakeKey && keyed.Environment["CLAUDE_CONFIG_DIR"] == ClaudeApiKeyLaunch.ConfigFolder(Path.Combine(root, "rules"))
            && new[] { "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL" }.All(name => keyed.Environment.TryGetValue(name, out var value) && value == null)
            && keyed.CredentialNote != null, "a Claude API Key: its own config folder, the key, and the subscription's tokens and base URL removed");
        check(!keyed.ToString().Contains(FakeKey) && !keyed.ToString().Contains("sk-ant"), "a launch never prints its variables");
        if (!OperatingSystem.IsWindows())
            check(File.GetUnixFileMode(ClaudeApiKeyLaunch.ConfigFolder(Path.Combine(root, "rules"))) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute),
                "the Claude Code config folder is for this user only (0700)");
        if (OperatingSystem.IsWindows())
        {
            string rules = ClaudeApiKeyLaunch.ConfigFolder(Path.Combine(root, "rules"));
            check(PrivateOnWindows(rules), "the Claude Code config folder is for this user only (inheritance off; this user and SYSTEM)");
            // An installation or an update gives Users access again; the next start takes it away.
            var opened = new DirectoryInfo(rules).GetAccessControl();
            opened.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(rules).SetAccessControl(opened);
            ClaudeApiKeyLaunch.Create(Path.Combine(root, "rules"), FakeKey, Missing);
            check(PrivateOnWindows(rules), "an existing Claude Code config folder is made private again");
        }
        bool badProfile = false; try { _ = new AgentLaunch("../x", new Dictionary<string, string?>()); } catch (ArgumentException) { badProfile = true; }
        check(badProfile, "a profile is a plain name, never a path");
        check(AgentWorkspaceStore.HarnessOfRecord(Path.Combine("x", "claude.claude-api.json")) == "claude" && AgentWorkspaceStore.HarnessOfRecord("codex.json") == "codex",
            "a saved conversation's file names its harness before the profile");

        // Shims: the "claude" CLI and its ACP adapter are the fixture.
        foreach (var name in new[] { "claude", "claude-agent-acp" })
        {
            string wrapper = Path.Combine(shimDirectory, OperatingSystem.IsWindows() ? name + ".cmd" : name);
            AtomicText.Write(wrapper, OperatingSystem.IsWindows() ? "@echo off\r\nnode \"%dp0%\\fixture.cjs\"\r\n" : "#!/bin/sh\nexec \"" + fixtureExe + "\" --fixture\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // What the fish may have inherited and must not hand on in this mode.
        string[] inherited = ["ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL", "ANTHROPIC_API_KEY", "CLAUDE_CONFIG_DIR", "CLAUDE_CODE_USE_BEDROCK"];
        var saved = inherited.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "fixture-bearer"); Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", "fixture-oauth");
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "http://127.0.0.1:9"); Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK", "1");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null); Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);

        string agentRoot = Path.Combine(root, "launch-host"), workspace = Path.Combine(root, "launch-workspace");
        string pipe = OperatingSystem.IsMacOS() ? "/tmp/bbf-launch-" + Guid.NewGuid().ToString("N")[..12] : "BigBlueFish.LaunchVerification." + Guid.NewGuid().ToString("N");
        string bridge = Path.Combine(root, "launch-bridge-placeholder"); File.WriteAllText(bridge, "fixture uses the configured pipe directly");
        AgentCoordinator? host = null;
        int said = 0, permissions = 0;
        bool apiKeyOn = false; string? key = FakeKey;
        var asked = new List<string>();
        using var server = new ControlServer((name, values, token) => {
            if (name == "say") { host?.MarkSpoken(); Interlocked.Increment(ref said); return Task.FromResult(ControlTools.Text(new { displayed = true })); }
            return Task.FromResult(ControlTools.Text(host!.ExecuteTool(name, values)));
        }, pipe);
        try
        {
            host = new AgentCoordinator(agentRoot, bridge, pipe, (_, _) => { Interlocked.Increment(ref permissions); return Task.FromResult<string?>(null); }, workspace);
            // The host's policy, as LinuxMcp and CompanionMcp have it: Claude Code with the option on gets the key's launch.
            host.Launch = (harness, _) => {
                lock (asked) asked.Add(harness);
                return Task.FromResult(harness == ClaudeApiKeyLaunch.Harness && apiKeyOn ? ClaudeApiKeyLaunch.Create(agentRoot, key, Missing) : AgentLaunch.Default);
            };
            var statuses = new List<string>();
            host.Changed += () => { lock (statuses) { if (statuses.Count == 0 || statuses[^1] != host.Status) statuses.Add(host.Status); } Console.WriteLine("LAUNCH " + host.Status + " " + host.Detail); };
            int Mark() { lock (statuses) return statuses.Count; }
            bool SeenAfter(int mark, string status) { lock (statuses) return statuses.Skip(mark).Contains(status); }
            ChildEnvironment Child()
            {
                string line = File.ReadAllLines(Path.Combine(workspace, "fixture-env.log")).Last(l => l.Length > 0);
                return JsonSerializer.Deserialize<ChildEnvironment>(line)!;
            }
            int Starts() => File.Exists(Path.Combine(workspace, "fixture-env.log")) ? File.ReadAllLines(Path.Combine(workspace, "fixture-env.log")).Count(l => l.Length > 0) : 0;
            AgentSession? Saved(string profile)
            {
                string path = host.Workspaces.SessionPath("claude", workspace, profile);
                return File.Exists(path) ? JsonSerializer.Deserialize<AgentSession>(File.ReadAllText(path)) : null;
            }

            host.Configure("claude", new(true, false, false));
            await until(() => host.Connected && !host.Busy, "Claude Code (fixture) connects with its own login", 15);
            var subscription = Child(); string subscriptionSession = host.SessionId;
            check(subscription.ConfigDir == null && subscription.ApiKey == "absent" && subscription.AuthToken && subscription.OauthToken && subscription.BaseUrl,
                "option off: the inherited environment is left as it was");
            check(host.LaunchProfile == "" && Saved("")?.SessionId == subscriptionSession && Saved(ClaudeApiKeyLaunch.Profile) == null, "option off: the conversation is saved as Claude Code's own");
            check(asked.All(h => h == "claude") && asked.Count >= 1, "the launch is asked with the harness being started");
            check(!await host.LaunchChanged(), "unchanged settings start nothing new");

            apiKeyOn = true;
            check(await host.LaunchChanged(), "turning the option on starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 2, "Claude Code restarts on the Claude API Key", 15);
            var api = Child(); string apiSession = host.SessionId;
            check(api.Pid != subscription.Pid && api.ConfigDir == ClaudeApiKeyLaunch.ConfigFolder(agentRoot) && api.ApiKey == "fake"
                && !api.AuthToken && !api.OauthToken && !api.BaseUrl && !api.Bedrock,
                "option on: a new process with its own config folder and the key, without the subscription's tokens, the base URL or another backend");
            check(apiSession != subscriptionSession && host.LaunchProfile == ClaudeApiKeyLaunch.Profile && Saved(ClaudeApiKeyLaunch.Profile) is { Profile: ClaudeApiKeyLaunch.Profile } record
                && record.SessionId == apiSession && Saved("")?.SessionId == subscriptionSession,
                "the API Key's conversation is a new one, saved apart; the subscription's stays");
            check(!await host.LaunchChanged() && Starts() == 2, "the same key starts nothing new");

            key = OtherFakeKey;
            check(await host.LaunchChanged(), "saving another Claude API Key starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 3, "Claude Code restarts with the new key", 15);
            check(Child().ApiKey == "fake2" && host.SessionId == apiSession, "the new key is used and the API Key's conversation resumes");

            apiKeyOn = false;
            check(await host.LaunchChanged(), "turning the option off starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 4, "Claude Code restarts on its own login", 15);
            check(Child() is { ApiKey: "absent", ConfigDir: null, AuthToken: true } && host.SessionId == subscriptionSession && host.LaunchProfile == "",
                "option off again: the inherited environment and the subscription's own conversation come back");

            apiKeyOn = true; key = "";
            check(await host.LaunchChanged(), "the option without a key is a change too");
            await until(() => !host.Connected && host.Status == "尚未啟動 · Claude Code" && host.Detail == Missing, "without a Claude API Key nothing starts and the status says why", 15);
            host.Tick(DateTimeOffset.Now.AddMinutes(10), true);
            check(host.SendUser("hello while waiting for a key"), "a message while waiting is taken but starts nothing");
            await Task.Delay(800);
            check(Starts() == 4 && !host.Connected && host.Status == "尚未啟動 · Claude Code", "no Claude Code starts on the subscription in its place, not even on retry");

            key = FakeKey;
            check(await host.LaunchChanged(), "saving the key ends the wait");
            await until(() => host.Connected && !host.Busy && Starts() == 5, "Claude Code starts once the key is saved", 15);
            check(Child().ApiKey == "fake" && host.SessionId == apiSession, "the API Key's conversation resumes after the wait");

            int saidBefore = said;
            check(host.SendUser("hi"), "a message on the API Key accepted");
            await until(() => said == saidBefore + 1 && !host.Busy, "a turn on the API Key completes through say", 15);
            check(!File.ReadAllText(Path.Combine(workspace, "fixture-last-prompt.json")).Contains("sk-ant"), "the key never enters the ACP prompt");

            // Another harness never gets the key's variables.
            host.Configure("opencode", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == 6, "switching to another harness starts it", 15);
            check(Child() is { ApiKey: "absent", ConfigDir: null, AuthToken: true, BaseUrl: true } && host.LaunchProfile == "" && asked.Last() == "opencode",
                "the variables reach only the harness they were given for");
            host.Configure("claude", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == 7 && host.SessionId == apiSession, "back on Claude Code with the API Key's conversation", 15);

            // A key Claude Code turns down: the Agent's own sign-in is never offered, the note says what to do.
            File.WriteAllText(Path.Combine(workspace, "fixture-prompt-auth"), ""); File.Delete(Path.Combine(workspace, "fixture-prompt-auth-used"));
            check(host.SendUser("rejected key"), "a turn with a rejected key accepted");
            await until(() => !host.Busy && host.Status == "驗證失敗 · Claude Code" && host.Detail == ClaudeApiKeyLaunch.CredentialNote, "a rejected key shows the note, not a sign-in", 15);
            check(Starts() == 7 && permissions == 0, "a rejected key is not retried through another process or the Agent's sign-in");
            File.Delete(Path.Combine(workspace, "fixture-prompt-auth"));
            File.WriteAllText(Path.Combine(workspace, "fixture-auth-methods"), "");
            int resumed = Mark();
            host.Reconnect();
            await until(() => SeenAfter(resumed, "正在恢復上次的魚魚對話…") && host.Status == "驗證失敗 · Claude Code" && !host.Connected && Starts() == 8,
                "an Agent asking to sign in at resume gets the note too", 15);
            check(permissions == 0, "no sign-in method is offered for a key the fish supplies");
            File.Delete(Path.Combine(workspace, "fixture-auth-methods"));
            host.Reconnect();
            await until(() => host.Connected && !host.Busy && host.SessionId == apiSession, "重新連線 resumes once the key is accepted", 15);

            // A running turn ends when the setup changes; the new process uses the other conversation.
            check(host.SendUser("wait"), "a long turn started");
            await until(() => host.Busy, "the long turn is running", 15);
            apiKeyOn = false;
            check(await host.LaunchChanged(), "a change during a turn starts again");
            await until(() => host.Connected && !host.Busy && host.SessionId == subscriptionSession && host.LaunchProfile == "", "the running turn stops and Claude Code's own conversation resumes", 15);

            // 開始新對話 replaces only the conversation of the setup in use.
            apiKeyOn = true;
            check(await host.LaunchChanged(), "back on the API Key");
            await until(() => host.Connected && !host.Busy && host.SessionId == apiSession, "the API Key's conversation once more", 15);
            check(host.NewConversation(), "new conversation accepted");
            await until(() => host.Connected && !host.Busy && host.SessionId != apiSession, "a new conversation on the API Key", 15);
            check(Saved(ClaudeApiKeyLaunch.Profile)?.SessionId == host.SessionId && Saved("")?.SessionId == subscriptionSession, "the subscription's conversation is kept");

            // 清空工作區 ends the reservations of every saved conversation, the API Key's included.
            string current = host.SessionId;
            host.Wakeups.Schedule("claude", current, "fixture care", 60, null, null, DateTimeOffset.Now);
            await host.ClearWorkspace(workspace);
            check(host.Wakeups.List("claude", current).Length == 0, "clearing the workspace ends the API Key conversation's reservations");
            await until(() => host.Connected && !host.Busy, "Claude Code connects again after clearing", 15);

            // The key stays out of everything the fish writes or shows.
            var written = Directory.EnumerateFiles(agentRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(ClaudeApiKeyLaunch.ConfigFolder(agentRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
            check(written.Length > 0 && written.All(f => !File.ReadAllText(f).Contains("sk-ant")), "the key is in no file the fish writes (sessions, wakeups, workspace state)");
            check(!Directory.EnumerateFileSystemEntries(ClaudeApiKeyLaunch.ConfigFolder(agentRoot), "*", SearchOption.AllDirectories).Any(), "the fish writes nothing into Claude Code's config folder");
            string log = DiagnosticLog.ExportText();
            check(!log.Contains("sk-ant") && !log.Contains("FAKE") && log.Contains("claude-api"), "the diagnostic log names the profile, never the key");
            check(!(host.Status + host.Detail).Contains("sk-ant"), "the status never shows the key");
            DiagnosticLog.ObserveSettings(new { AgentClaudeApiKey = false }); DiagnosticLog.ObserveSettings(new { AgentClaudeApiKey = true });
            check(DiagnosticLog.ExportText().Contains("changed=AgentClaudeApiKey"), "the diagnostic log names a change of the switch by name only");
            host.Configure("claude", new(false, false, false));
            await until(() => !host.Connected && server.Connections == 0, "disabling closes Claude Code", 15);
        }
        finally
        {
            host?.Dispose();
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
        }
    }
}
