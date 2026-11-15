// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using BigBlueFish.Agent;
using BigBlueFish.Control;
using BigBlueFish.Diagnostics;

// Claude Code, Codex CLI and Gemini CLI on an API Key of the cloud AI service (AgentLaunch, ApiKeyLaunch), through actual
// fixture processes started as those harnesses (shims named claude, claude-agent-acp, codex, codex-acp and gemini): the
// variables reach the chosen harness's process only, the sign-in's tokens and the variables that would redirect the key
// are taken out of it (set empty for Gemini CLI), a change starts a new process, no key means no start, a rejected key
// shows the note instead of a sign-in or a retry, the key stays out of the log, the saved conversations, the prompts and
// the files the fish writes, and each setup resumes its own conversation.
static class LaunchVerification
{
    // Shapes of real keys, not keys: nothing here talks to Anthropic, OpenAI or Google.
    public const string FakeKey = "sk-ant-api03-FAKE-launch-verification-key-1";
    public const string OtherFakeKey = "sk-ant-api03-FAKE-launch-verification-key-2";
    public const string OpenAiFakeKey = "sk-proj-FAKE-launch-verification-openai-1";
    public const string GeminiFakeKey = "AIzaSyFAKE-launch-verification-gemini-01";
    const string Missing = "fixture: no API Key saved";
    // What the fixture reports of its own environment: the secrets by kind only (never a key), the rest as they are.
    static readonly string[] Secrets = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "CODEX_ACCESS_TOKEN",
        "GEMINI_API_KEY", "GOOGLE_API_KEY"];
    static readonly string[] Plain = ["CLAUDE_CONFIG_DIR", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_USE_BEDROCK", "CODEX_HOME", "CODEX_PATH", "OPENAI_BASE_URL", "APP_SERVER_LOGS",
        "CODEX_CONFIG", "DEFAULT_AUTH_REQUEST", "NO_BROWSER", "GEMINI_CLI_HOME", "GOOGLE_GEMINI_BASE_URL", "GOOGLE_GENAI_USE_VERTEXAI", "GEMINI_CLI_AUTH_OVERRIDE",
        "GEMINI_CLI_TRUST_WORKSPACE", "GEMINI_FORCE_FILE_STORAGE"];
    // What Gemini CLI would otherwise fill in from a .env file: set empty, never removed.
    static readonly string[] GeminiBlanked = ["GOOGLE_GEMINI_BASE_URL", "GEMINI_API_KEY_AUTH_MECHANISM", "GEMINI_CLI_CUSTOM_HEADERS", "GOOGLE_API_KEY", "GOOGLE_GENAI_USE_VERTEXAI",
        "GOOGLE_GENAI_USE_GCA", "GEMINI_CLI_AUTH_OVERRIDE", "GOOGLE_GENAI_API_VERSION", "GOOGLE_VERTEX_BASE_URL", "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_PROJECT_ID",
        "GOOGLE_CLOUD_LOCATION", "GOOGLE_APPLICATION_CREDENTIALS", "CLOUD_SHELL", "GEMINI_CLI_USE_COMPUTE_ADC", "GEMINI_DEFAULT_AUTH_TYPE"];
    static readonly string[] CodexRemoved = ["OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_ACCESS_TOKEN", "OPENAI_FEDERATION_RULE_ID", "OPENAI_IDENTITY_TOKEN_FILE",
        "OPENAI_WORKLOAD_IDENTITY_CONTEXT", "OPENAI_ORGANIZATION", "OPENAI_PROJECT", "CODEX_CONFIG", "MODEL_PROVIDER", "APP_SERVER_LOGS", "CODEX_SQLITE_HOME"];

    sealed record ChildEnvironment(int Pid, Dictionary<string, string?> Variables)
    {
        public string? this[string name] => Variables.GetValueOrDefault(name);
    }

    // The fixture's side: what its own environment holds (null: absent, "": present and empty).
    public static void Record()
    {
        var variables = new Dictionary<string, string?>();
        foreach (var name in Secrets) variables[name] = Environment.GetEnvironmentVariable(name) switch
        {
            null => null, "" => "", FakeKey => "fake", OtherFakeKey => "fake2", OpenAiFakeKey => "openai", GeminiFakeKey => "gemini", _ => "other"
        };
        foreach (var name in Plain) variables[name] = Environment.GetEnvironmentVariable(name);
        File.AppendAllText("fixture-env.log", JsonSerializer.Serialize(new ChildEnvironment(Environment.ProcessId, variables)) + "\n");
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

    static JsonNode Settings(string file) => JsonNode.Parse(File.ReadAllText(file))!;

    public static async Task Run(string root, string fixtureExe, string shimDirectory, Action<bool, string> check, Func<Func<bool>, string, int, Task> until)
    {
        var claude = ApiKeyLaunch.Claude; var codex = ApiKeyLaunch.Codex; var gemini = ApiKeyLaunch.Gemini;
        string rules = Path.Combine(root, "rules");
        // The library's own rules first.
        check(ApiKeyLaunch.All.Select(l => l.Harness).SequenceEqual(["claude", "codex", "gemini"]) && ApiKeyLaunch.For("opencode") == null && ApiKeyLaunch.ForProfile("") == null
            && ApiKeyLaunch.All.All(l => ApiKeyLaunch.For(l.Harness) == l && ApiKeyLaunch.ForProfile(l.Profile) == l && l.Profile == l.Harness + "-api"),
            "Claude Code, Codex CLI and Gemini CLI offer an API Key launch, each a profile of its own; other harnesses none");
        check(claude.Label == "Claude Code 改用 Claude API Key（依用量計費）" && codex.Label == "Codex CLI 改用 OpenAI API Key（依用量計費）"
            && gemini.Label == "Gemini CLI 改用 Gemini API Key（依用量計費）" && codex.SignInDetail == "OpenAI API Key（依用量計費）"
            && codex.OwnState == "目前使用 Codex CLI 自己的登入（例如 ChatGPT 帳號）。" && claude.OwnState == "目前使用 Claude Code 自己的登入（例如訂閱）。",
            "the switches use the official names of the keys");
        foreach (var api in ApiKeyLaunch.All)
        {
            check(api.Description("設定 › 模型與資訊").Contains("「設定 › 模型與資訊」") && api.Description("x").Contains(api.Vendor) && api.Description("x").Contains(api.KeyConsole)
                && api.Missing("設定 › 模型與資訊").Contains("「" + api.Switch + "」") && api.CredentialNote.Contains("「" + api.Switch + "」") && api.CredentialNote.Contains(api.KeyConsole)
                && api.Warning(rules).Contains(api.Vendor) && api.MissingHint("服務名").Contains("服務選「服務名」"),
                api.Product + ": the page names the key, who bills it, where to manage it and the switch");
            var none = api.Create(rules, "  ", Missing);
            check(none.Unavailable == Missing && none.Environment.Count == 0 && none.Profile == api.Profile && !Directory.Exists(api.ConfigFolder(rules)),
                api.Product + " without a key: nothing to start, no folder, the reason kept");
        }
        check(codex.Warning(rules).Contains("ChatGPT") && !gemini.Warning(rules).Contains("ChatGPT") && !claude.Warning(rules).Contains("ChatGPT")
            && claude.Warning(rules).Contains(claude.SettingsFile(rules)) && gemini.Warning(rules).Contains(gemini.SettingsFile(rules)),
            "ChatGPT is named only for Codex CLI's own sign-in; the model advice names the CLI's own settings file");
        var keyed = claude.Create(rules, " " + FakeKey + "\n", Missing);
        check(keyed.Unavailable == null && keyed.Environment["ANTHROPIC_API_KEY"] == FakeKey && keyed.Environment["CLAUDE_CONFIG_DIR"] == claude.ConfigFolder(rules)
            && new[] { "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL" }.All(name => keyed.Environment.TryGetValue(name, out var value) && value == null)
            && keyed.CredentialNote == claude.CredentialNote && keyed.Rejections.Count == 0, "a Claude API Key: its own config folder, the key, and the subscription's tokens and base URL removed");
        check(!keyed.ToString().Contains(FakeKey) && !keyed.ToString().Contains("sk-ant"), "a launch never prints its variables");
        var codexKeyed = codex.Create(rules, OpenAiFakeKey, Missing);
        check(codexKeyed.Unavailable == null && codexKeyed.Environment["CODEX_API_KEY"] == OpenAiFakeKey && codexKeyed.Environment["CODEX_HOME"] == codex.ConfigFolder(rules)
            && codexKeyed.Environment["DEFAULT_AUTH_REQUEST"] == "{\"methodId\":\"api-key\"}" && codexKeyed.Environment["NO_BROWSER"] == "1"
            && CodexRemoved.All(name => codexKeyed.Environment.TryGetValue(name, out var value) && value == null) && !codexKeyed.Environment.ContainsKey("ANTHROPIC_API_KEY")
            && codexKeyed.CredentialNote == codex.CredentialNote && codexKeyed.Environment.Count == 4 + CodexRemoved.Length,
            "an OpenAI API Key: Codex CLI's own CODEX_HOME, the key, the adapter's own API Key sign-in, and the variables that would redirect, replace or log the key removed");
        string toml = File.ReadAllText(codex.SettingsFile(rules));
        check(codex.SettingsFile(rules) == Path.Combine(codex.ConfigFolder(rules), "config.toml") && toml.Contains("\ncli_auth_credentials_store = \"ephemeral\"\n")
            && toml.Contains("\nforced_login_method = \"api\"\n") && toml.Contains("\n[shell_environment_policy]\nexclude = [\"CODEX_API_KEY\", \"OPENAI_API_KEY\"]\n")
            && !toml.Contains("sk-") && Directory.EnumerateFileSystemEntries(codex.ConfigFolder(rules)).Count() == 1,
            "Codex CLI's config.toml keeps the key in memory, allows an API key only and keeps the key from the commands Codex runs; it holds no key");
        File.WriteAllText(codex.SettingsFile(rules), "model = \"user-edit\"\n");
        codex.Create(rules, OpenAiFakeKey, Missing);
        check(File.ReadAllText(codex.SettingsFile(rules)) == toml, "the fish writes Codex CLI's config.toml again before each start");
        var geminiKeyed = gemini.Create(rules, GeminiFakeKey, Missing);
        check(geminiKeyed.Unavailable == null && geminiKeyed.Environment["GEMINI_API_KEY"] == GeminiFakeKey && geminiKeyed.Environment["GEMINI_CLI_HOME"] == gemini.ConfigFolder(rules)
            && geminiKeyed.Environment["GEMINI_CLI_TRUST_WORKSPACE"] == "true" && geminiKeyed.Environment["GEMINI_FORCE_FILE_STORAGE"] == "true"
            && GeminiBlanked.All(name => geminiKeyed.Environment.TryGetValue(name, out var value) && value == "") && !geminiKeyed.Environment.Values.Contains(null)
            && geminiKeyed.Environment.Count == 4 + GeminiBlanked.Length
            && new[] { "API_KEY_INVALID", "API_KEY_SERVICE_BLOCKED", "API_KEY_HTTP_REFERRER_BLOCKED", "API_KEY_IP_ADDRESS_BLOCKED", "CONSUMER_SUSPENDED", "reported as leaked" }.All(geminiKeyed.Rejections.Contains),
            "a Gemini API Key: Gemini CLI's own home, the key, a trusted workspace, no keychain, and the variables a .env file could fill in set empty");
        string geminiFile = gemini.SettingsFile(rules);
        var geminiSettings = Settings(geminiFile);
        check(geminiFile == Path.Combine(gemini.ConfigFolder(rules), ".gemini", "settings.json") && (string?)geminiSettings["security"]?["auth"]?["selectedType"] == "gemini-api-key"
            && (bool?)geminiSettings["security"]?["environmentVariableRedaction"]?["enabled"] == true && (bool?)geminiSettings["privacy"]?["usageStatisticsEnabled"] == false
            && !File.ReadAllText(geminiFile).Contains("AIza"), "Gemini CLI's settings select the Gemini API Key, keep it from the commands it runs, send no usage statistics and hold no key");
        File.WriteAllText(geminiFile, "// mine\n{\"model\": {\"name\": \"gemini-3-flash-preview\"}, \"ui\": {\"theme\": \"藍色\"},\n \"security\": {\"auth\": {\"selectedType\": \"oauth-personal\"}},"
            + " \"privacy\": {\"usageStatisticsEnabled\": true},}\n");
        gemini.Create(rules, GeminiFakeKey, Missing);
        geminiSettings = Settings(geminiFile);
        check((string?)geminiSettings["security"]?["auth"]?["selectedType"] == "gemini-api-key" && (string?)geminiSettings["model"]?["name"] == "gemini-3-flash-preview"
            && (string?)geminiSettings["ui"]?["theme"] == "藍色" && File.ReadAllText(geminiFile).Contains("藍色") && (bool?)geminiSettings["privacy"]?["usageStatisticsEnabled"] == true,
            "Gemini CLI's settings keep the user's model and choices; the sign-in is set back to the Gemini API Key");
        File.WriteAllText(geminiFile, "{ not json");
        string? refused = null;
        try { gemini.Create(rules, GeminiFakeKey, Missing); } catch (IOException error) { refused = error.Message; }
        check(refused != null && refused.Contains(geminiFile) && File.ReadAllText(geminiFile) == "{ not json", "settings that are not JSON stop the start and are left as they are");
        File.Delete(geminiFile);
        foreach (var api in ApiKeyLaunch.All)
        {
            string folder = api.ConfigFolder(rules);
            if (!OperatingSystem.IsWindows())
                check(File.GetUnixFileMode(folder) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), api.Product + "'s config folder is for this user only (0700)");
            if (OperatingSystem.IsWindows())
            {
                check(PrivateOnWindows(folder), api.Product + "'s config folder is for this user only (inheritance off; this user and SYSTEM)");
                // An installation or an update gives Users access again; the next start takes it away.
                var opened = new DirectoryInfo(folder).GetAccessControl();
                opened.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(folder).SetAccessControl(opened);
                api.Create(rules, api == claude ? FakeKey : api == codex ? OpenAiFakeKey : GeminiFakeKey, Missing);
                check(PrivateOnWindows(folder), "an existing config folder of " + api.Product + " is made private again");
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            // A link in the folder's place would take the rules (and the CLI's sign-in) to whatever it points at.
            string linked = Path.Combine(root, "linked"), elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(linked); Directory.CreateDirectory(elsewhere);
            Directory.CreateSymbolicLink(codex.ConfigFolder(linked), elsewhere);
            string? linkRefused = null;
            try { codex.Create(linked, OpenAiFakeKey, Missing); } catch (IOException error) { linkRefused = error.Message; }
            check(linkRefused != null && linkRefused.Contains("連結") && !File.Exists(Path.Combine(elsewhere, "config.toml")), "a link in place of the config folder is refused");
        }
        bool badProfile = false; try { _ = new AgentLaunch("../x", new Dictionary<string, string?>()); } catch (ArgumentException) { badProfile = true; }
        check(badProfile, "a profile is a plain name, never a path");
        check(AgentWorkspaceStore.HarnessOfRecord(Path.Combine("x", "claude.claude-api.json")) == "claude" && AgentWorkspaceStore.HarnessOfRecord("codex.json") == "codex"
            && AgentWorkspaceStore.HarnessOfRecord("codex.codex-api.json") == "codex" && AgentWorkspaceStore.HarnessOfRecord("gemini.gemini-api.json") == "gemini",
            "a saved conversation's file names its harness before the profile");

        // Shims: the CLIs and their ACP adapters are the fixture.
        foreach (var name in new[] { "claude", "claude-agent-acp", "codex", "codex-acp", "gemini" })
        {
            string wrapper = Path.Combine(shimDirectory, OperatingSystem.IsWindows() ? name + ".cmd" : name);
            AtomicText.Write(wrapper, OperatingSystem.IsWindows() ? "@echo off\r\nnode \"%dp0%\\fixture.cjs\"\r\n" : "#!/bin/sh\nexec \"" + fixtureExe + "\" --fixture\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // What the fish may have inherited and must not hand on in these modes.
        var inherited = new Dictionary<string, string?>
        {
            ["ANTHROPIC_AUTH_TOKEN"] = "fixture-bearer", ["CLAUDE_CODE_OAUTH_TOKEN"] = "fixture-oauth", ["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:9", ["CLAUDE_CODE_USE_BEDROCK"] = "1",
            ["OPENAI_API_KEY"] = "fixture-user-openai", ["OPENAI_BASE_URL"] = "http://127.0.0.1:9/v1", ["CODEX_ACCESS_TOKEN"] = "fixture-chatgpt", ["APP_SERVER_LOGS"] = "/tmp/fixture-logs",
            ["CODEX_CONFIG"] = "{}", ["GOOGLE_GEMINI_BASE_URL"] = "http://127.0.0.1:9", ["GOOGLE_API_KEY"] = "fixture-user-google", ["GOOGLE_GENAI_USE_VERTEXAI"] = "true",
            ["GEMINI_CLI_AUTH_OVERRIDE"] = "oauth-personal",
            ["ANTHROPIC_API_KEY"] = null, ["CLAUDE_CONFIG_DIR"] = null, ["CODEX_API_KEY"] = null, ["CODEX_HOME"] = null, ["GEMINI_API_KEY"] = null, ["GEMINI_CLI_HOME"] = null,
            ["DEFAULT_AUTH_REQUEST"] = null, ["NO_BROWSER"] = null, ["GEMINI_CLI_TRUST_WORKSPACE"] = null, ["GEMINI_FORCE_FILE_STORAGE"] = null
        };
        var saved = inherited.Keys.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        foreach (var (name, value) in inherited) Environment.SetEnvironmentVariable(name, value);

        string agentRoot = Path.Combine(root, "launch-host"), workspace = Path.Combine(root, "launch-workspace");
        string pipe = OperatingSystem.IsMacOS() ? "/tmp/bbf-launch-" + Guid.NewGuid().ToString("N")[..12] : "BigBlueFish.LaunchVerification." + Guid.NewGuid().ToString("N");
        string bridge = Path.Combine(root, "launch-bridge-placeholder"); File.WriteAllText(bridge, "fixture uses the configured pipe directly");
        AgentCoordinator? host = null;
        int said = 0, permissions = 0;
        // The host's settings: which harness has its option on, and the key the cloud AI service holds for it.
        var on = new ConcurrentDictionary<string, bool>();
        var keys = new ConcurrentDictionary<string, string?> { ["claude"] = FakeKey, ["codex"] = OpenAiFakeKey, ["gemini"] = GeminiFakeKey };
        var asked = new List<string>();
        using var server = new ControlServer((name, values, token) => {
            if (name == "say") { host?.MarkSpoken(); Interlocked.Increment(ref said); return Task.FromResult(ControlTools.Text(new { displayed = true })); }
            return Task.FromResult(ControlTools.Text(host!.ExecuteTool(name, values)));
        }, pipe);
        try
        {
            host = new AgentCoordinator(agentRoot, bridge, pipe, (_, _) => { Interlocked.Increment(ref permissions); return Task.FromResult<string?>(null); }, workspace);
            // The host's policy, as LinuxMcp and CompanionMcp have it: a harness with an API Key launch and its option on gets it.
            host.Launch = (harness, _) => {
                lock (asked) asked.Add(harness);
                return Task.FromResult(ApiKeyLaunch.For(harness) is { } offered && on.GetValueOrDefault(harness) ? offered.Create(agentRoot, keys[harness], Missing) : AgentLaunch.Default);
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
            AgentSession? SavedOf(string harness, string profile)
            {
                string path = host.Workspaces.SessionPath(harness, workspace, profile);
                return File.Exists(path) ? JsonSerializer.Deserialize<AgentSession>(File.ReadAllText(path)) : null;
            }
            AgentSession? Saved(string profile) => SavedOf("claude", profile);
            // The next session/prompt answers with this ACP error, once.
            void PromptError(object error)
            {
                File.Delete(Path.Combine(workspace, "fixture-prompt-error-used"));
                File.WriteAllText(Path.Combine(workspace, "fixture-prompt-error.json"), JsonSerializer.Serialize(error));
            }

            host.Configure("claude", new(true, false, false));
            await until(() => host.Connected && !host.Busy, "Claude Code (fixture) connects with its own login", 15);
            var subscription = Child(); string subscriptionSession = host.SessionId;
            check(subscription["CLAUDE_CONFIG_DIR"] == null && subscription["ANTHROPIC_API_KEY"] == null && subscription["ANTHROPIC_AUTH_TOKEN"] == "other"
                && subscription["CLAUDE_CODE_OAUTH_TOKEN"] == "other" && subscription["ANTHROPIC_BASE_URL"] != null,
                "option off: the inherited environment is left as it was");
            check(host.LaunchProfile == "" && Saved("")?.SessionId == subscriptionSession && Saved(claude.Profile) == null, "option off: the conversation is saved as Claude Code's own");
            check(asked.All(h => h == "claude") && asked.Count >= 1, "the launch is asked with the harness being started");
            check(!await host.LaunchChanged(), "unchanged settings start nothing new");

            on["claude"] = true;
            check(await host.LaunchChanged(), "turning the option on starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 2, "Claude Code restarts on the Claude API Key", 15);
            var api = Child(); string apiSession = host.SessionId;
            check(api.Pid != subscription.Pid && api["CLAUDE_CONFIG_DIR"] == claude.ConfigFolder(agentRoot) && api["ANTHROPIC_API_KEY"] == "fake"
                && api["ANTHROPIC_AUTH_TOKEN"] == null && api["CLAUDE_CODE_OAUTH_TOKEN"] == null && api["ANTHROPIC_BASE_URL"] == null && api["CLAUDE_CODE_USE_BEDROCK"] == null,
                "option on: a new process with its own config folder and the key, without the subscription's tokens, the base URL or another backend");
            check(apiSession != subscriptionSession && host.LaunchProfile == claude.Profile && Saved(claude.Profile) is { } record && record.Profile == claude.Profile
                && record.SessionId == apiSession && Saved("")?.SessionId == subscriptionSession,
                "the API Key's conversation is a new one, saved apart; the subscription's stays");
            check(!await host.LaunchChanged() && Starts() == 2, "the same key starts nothing new");

            keys["claude"] = OtherFakeKey;
            check(await host.LaunchChanged(), "saving another Claude API Key starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 3, "Claude Code restarts with the new key", 15);
            check(Child()["ANTHROPIC_API_KEY"] == "fake2" && host.SessionId == apiSession, "the new key is used and the API Key's conversation resumes");

            on["claude"] = false;
            check(await host.LaunchChanged(), "turning the option off starts Claude Code again");
            await until(() => host.Connected && !host.Busy && Starts() == 4, "Claude Code restarts on its own login", 15);
            check(Child() is var own && own["ANTHROPIC_API_KEY"] == null && own["CLAUDE_CONFIG_DIR"] == null && own["ANTHROPIC_AUTH_TOKEN"] == "other"
                && host.SessionId == subscriptionSession && host.LaunchProfile == "",
                "option off again: the inherited environment and the subscription's own conversation come back");

            on["claude"] = true; keys["claude"] = "";
            check(await host.LaunchChanged(), "the option without a key is a change too");
            await until(() => !host.Connected && host.Status == "尚未啟動 · Claude Code" && host.Detail == Missing, "without a Claude API Key nothing starts and the status says why", 15);
            host.Tick(DateTimeOffset.Now.AddMinutes(10), true);
            check(host.SendUser("hello while waiting for a key"), "a message while waiting is taken but starts nothing");
            await Task.Delay(800);
            check(Starts() == 4 && !host.Connected && host.Status == "尚未啟動 · Claude Code", "no Claude Code starts on the subscription in its place, not even on retry");

            keys["claude"] = FakeKey;
            check(await host.LaunchChanged(), "saving the key ends the wait");
            await until(() => host.Connected && !host.Busy && Starts() == 5, "Claude Code starts once the key is saved", 15);
            check(Child()["ANTHROPIC_API_KEY"] == "fake" && host.SessionId == apiSession, "the API Key's conversation resumes after the wait");

            int saidBefore = said;
            check(host.SendUser("hi"), "a message on the API Key accepted");
            await until(() => said == saidBefore + 1 && !host.Busy, "a turn on the API Key completes through say", 15);
            check(!File.ReadAllText(Path.Combine(workspace, "fixture-last-prompt.json")).Contains("sk-ant"), "the key never enters the ACP prompt");

            // Another harness never gets the keys' variables, even with every option on.
            on["codex"] = true; on["gemini"] = true;
            host.Configure("opencode", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == 6, "switching to another harness starts it", 15);
            check(Child() is var other && other["ANTHROPIC_API_KEY"] == null && other["CLAUDE_CONFIG_DIR"] == null && other["ANTHROPIC_AUTH_TOKEN"] == "other" && other["ANTHROPIC_BASE_URL"] != null
                && other["CODEX_API_KEY"] == null && other["CODEX_HOME"] == null && other["OPENAI_API_KEY"] == "other" && other["GEMINI_API_KEY"] == null && other["GEMINI_CLI_HOME"] == null
                && other["GOOGLE_GEMINI_BASE_URL"] == "http://127.0.0.1:9" && host.LaunchProfile == "" && asked.Last() == "opencode",
                "the variables reach only the harness they were given for");
            on["codex"] = false; on["gemini"] = false;
            host.Configure("claude", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == 7 && host.SessionId == apiSession, "back on Claude Code with the API Key's conversation", 15);

            // A key Claude Code turns down: the Agent's own sign-in is never offered, the note says what to do.
            File.WriteAllText(Path.Combine(workspace, "fixture-prompt-auth"), ""); File.Delete(Path.Combine(workspace, "fixture-prompt-auth-used"));
            check(host.SendUser("rejected key"), "a turn with a rejected key accepted");
            await until(() => !host.Busy && host.Status == "驗證失敗 · Claude Code" && host.Detail == claude.CredentialNote, "a rejected key shows the note, not a sign-in", 15);
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
            on["claude"] = false;
            check(await host.LaunchChanged(), "a change during a turn starts again");
            await until(() => host.Connected && !host.Busy && host.SessionId == subscriptionSession && host.LaunchProfile == "", "the running turn stops and Claude Code's own conversation resumes", 15);

            // 開始新對話 replaces only the conversation of the setup in use.
            on["claude"] = true;
            check(await host.LaunchChanged(), "back on the API Key");
            await until(() => host.Connected && !host.Busy && host.SessionId == apiSession, "the API Key's conversation once more", 15);
            check(host.NewConversation(), "new conversation accepted");
            await until(() => host.Connected && !host.Busy && host.SessionId != apiSession, "a new conversation on the API Key", 15);
            check(Saved(claude.Profile)?.SessionId == host.SessionId && Saved("")?.SessionId == subscriptionSession, "the subscription's conversation is kept");
            string claudeCurrent = host.SessionId;

            // Codex CLI on the OpenAI API Key, with Claude Code's and Gemini CLI's options on as well.
            on["gemini"] = true;
            int before = Starts();
            host.Configure("codex", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == before + 1, "Codex CLI (fixture) connects with its own sign-in", 15);
            var codexOwn = Child(); string codexOwnSession = host.SessionId;
            check(codexOwn["CODEX_HOME"] == null && codexOwn["CODEX_API_KEY"] == null && codexOwn["OPENAI_API_KEY"] == "other" && codexOwn["CODEX_ACCESS_TOKEN"] == "other"
                && codexOwn["DEFAULT_AUTH_REQUEST"] == null && codexOwn["ANTHROPIC_API_KEY"] == null && host.LaunchProfile == "" && SavedOf("codex", "")?.SessionId == codexOwnSession,
                "Codex CLI option off: its own sign-in, the inherited environment, its own conversation");
            on["codex"] = true;
            check(await host.LaunchChanged(), "turning the Codex CLI option on starts it again");
            await until(() => host.Connected && !host.Busy && Starts() == before + 2, "Codex CLI restarts on the OpenAI API Key", 15);
            var codexApi = Child(); string codexApiSession = host.SessionId;
            check(codexApi.Pid != codexOwn.Pid && codexApi["CODEX_HOME"] == codex.ConfigFolder(agentRoot) && codexApi["CODEX_API_KEY"] == "openai"
                && codexApi["DEFAULT_AUTH_REQUEST"] == "{\"methodId\":\"api-key\"}" && codexApi["NO_BROWSER"] == "1" && codexApi["CODEX_PATH"] == HarnessCatalog.FindExecutable("codex")
                && new[] { "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_ACCESS_TOKEN", "APP_SERVER_LOGS", "CODEX_CONFIG" }.All(n => codexApi[n] == null),
                "Codex CLI option on: its own CODEX_HOME, the OpenAI API Key, the adapter's API Key sign-in, the installed CLI; the user's key, token, base URL, adapter settings and log removed");
            check(new[] { "ANTHROPIC_API_KEY", "CLAUDE_CONFIG_DIR", "GEMINI_API_KEY", "GEMINI_CLI_HOME", "GEMINI_CLI_TRUST_WORKSPACE" }.All(n => codexApi[n] == null)
                && codexApi["ANTHROPIC_AUTH_TOKEN"] == "other" && codexApi["GOOGLE_GEMINI_BASE_URL"] == "http://127.0.0.1:9",
                "Codex CLI gets none of Claude Code's or Gemini CLI's variables, though their options are on");
            check(codexApiSession != codexOwnSession && host.LaunchProfile == codex.Profile && SavedOf("codex", codex.Profile) is { } codexRecord
                && codexRecord.Profile == codex.Profile && codexRecord.SessionId == codexApiSession && SavedOf("codex", "")?.SessionId == codexOwnSession,
                "the OpenAI API Key's conversation is a new one, saved apart from Codex CLI's own");
            check(File.ReadAllText(codex.SettingsFile(agentRoot)) == toml, "Codex CLI starts with the fish's config.toml");
            check(!await host.LaunchChanged() && Starts() == before + 2, "the same OpenAI API Key starts nothing new");
            keys["codex"] = "";
            check(await host.LaunchChanged(), "clearing the OpenAI API Key is a change");
            await until(() => !host.Connected && host.Status == "尚未啟動 · Codex CLI" && host.Detail == Missing, "without an OpenAI API Key Codex CLI does not start", 15);
            check(host.SendUser("hello while waiting for the OpenAI key"), "a message while waiting for the OpenAI API Key is taken");
            await Task.Delay(800);
            check(Starts() == before + 2 && !host.Connected, "Codex CLI's own sign-in is never used in the key's place");
            keys["codex"] = OpenAiFakeKey;
            check(await host.LaunchChanged(), "saving the OpenAI API Key ends the wait");
            await until(() => host.Connected && !host.Busy && Starts() == before + 3 && host.SessionId == codexApiSession, "Codex CLI resumes the OpenAI API Key's conversation", 15);
            // A key OpenAI turns down comes back as ACP's Authentication required: the note, no sign-in, no retry.
            File.WriteAllText(Path.Combine(workspace, "fixture-prompt-auth"), ""); File.Delete(Path.Combine(workspace, "fixture-prompt-auth-used"));
            check(host.SendUser("rejected OpenAI key"), "a turn with a rejected OpenAI API Key accepted");
            await until(() => !host.Busy && host.Status == "驗證失敗 · Codex CLI" && host.Detail == codex.CredentialNote, "a rejected OpenAI API Key shows Codex CLI's note", 15);
            check(Starts() == before + 3 && permissions == 0, "a rejected OpenAI API Key is not retried and opens no sign-in");
            File.Delete(Path.Combine(workspace, "fixture-prompt-auth"));
            host.Reconnect();
            await until(() => host.Connected && !host.Busy && host.SessionId == codexApiSession, "重新連線 resumes Codex CLI's API Key conversation", 15);
            // No credit is not a rejection: an ordinary failure, tried again later (what Codex CLI says for 429 insufficient_quota).
            PromptError(new { code = -32603, message = "Internal error", data = new { message = "Quota exceeded. Check your plan and billing details.", codexErrorInfo = "usageLimitExceeded" } });
            check(host.SendUser("no credit"), "a turn without credit accepted");
            await until(() => !host.Busy && host.Status == "連線或要求失敗" && host.Detail.Contains("Quota exceeded"), "no credit is an ordinary failure, not a rejected key", 15);
            on["codex"] = false;
            check(await host.LaunchChanged(), "turning the Codex CLI option off starts it again");
            await until(() => host.Connected && !host.Busy && host.SessionId == codexOwnSession && host.LaunchProfile == "", "Codex CLI's own conversation comes back", 15);
            check(Child() is var codexBack && codexBack["CODEX_HOME"] == null && codexBack["CODEX_API_KEY"] == null && codexBack["OPENAI_API_KEY"] == "other",
                "Codex CLI option off again: the inherited environment comes back");

            // Gemini CLI on the Gemini API Key, with Claude Code's and Codex CLI's options on as well.
            on["gemini"] = false; on["codex"] = true;
            before = Starts();
            host.Configure("gemini", new(true, false, false));
            await until(() => host.Connected && !host.Busy && Starts() == before + 1, "Gemini CLI (fixture) connects with its own sign-in", 15);
            var geminiOwn = Child(); string geminiOwnSession = host.SessionId;
            check(geminiOwn["GEMINI_CLI_HOME"] == null && geminiOwn["GEMINI_API_KEY"] == null && geminiOwn["GOOGLE_GEMINI_BASE_URL"] == "http://127.0.0.1:9"
                && geminiOwn["GOOGLE_API_KEY"] == "other" && host.LaunchProfile == "", "Gemini CLI option off: its own sign-in and the inherited environment");
            // The same error with Gemini CLI's own sign-in is an ordinary failure: the phrases count only for a key the fish supplies.
            string invalidKey = "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\","
                + "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\",\"domain\":\"googleapis.com\"}]}}";
            PromptError(new { code = 400, message = invalidKey });
            check(host.SendUser("own key refused"), "a turn on Gemini CLI's own sign-in accepted");
            await until(() => !host.Busy && host.Status == "連線或要求失敗" && host.Detail.Contains("API_KEY_INVALID"), "Gemini's 400 on its own sign-in stays an ordinary failure", 15);
            on["gemini"] = true;
            check(await host.LaunchChanged(), "turning the Gemini CLI option on starts it again");
            await until(() => host.Connected && !host.Busy && Starts() == before + 2, "Gemini CLI restarts on the Gemini API Key", 15);
            var geminiApi = Child(); string geminiApiSession = host.SessionId;
            check(geminiApi["GEMINI_CLI_HOME"] == gemini.ConfigFolder(agentRoot) && geminiApi["GEMINI_API_KEY"] == "gemini" && geminiApi["GEMINI_CLI_TRUST_WORKSPACE"] == "true"
                && geminiApi["GEMINI_FORCE_FILE_STORAGE"] == "true" && geminiApi["GOOGLE_GEMINI_BASE_URL"] == "" && geminiApi["GOOGLE_API_KEY"] == ""
                && geminiApi["GOOGLE_GENAI_USE_VERTEXAI"] == "" && geminiApi["GEMINI_CLI_AUTH_OVERRIDE"] == "",
                "Gemini CLI option on: its own home, the Gemini API Key, a trusted workspace, no keychain, and the variables that would redirect the key or change the sign-in present but empty");
            check(new[] { "ANTHROPIC_API_KEY", "CLAUDE_CONFIG_DIR", "CODEX_API_KEY", "CODEX_HOME", "DEFAULT_AUTH_REQUEST", "NO_BROWSER" }.All(n => geminiApi[n] == null)
                && geminiApi["OPENAI_API_KEY"] == "other", "Gemini CLI gets none of Claude Code's or Codex CLI's variables, though their options are on");
            check(geminiApiSession != geminiOwnSession && host.LaunchProfile == gemini.Profile && SavedOf("gemini", gemini.Profile)?.SessionId == geminiApiSession
                && SavedOf("gemini", "")?.SessionId == geminiOwnSession, "the Gemini API Key's conversation is a new one, saved apart from Gemini CLI's own");
            string hostGeminiFile = gemini.SettingsFile(agentRoot);
            check((string?)Settings(hostGeminiFile)["security"]?["auth"]?["selectedType"] == "gemini-api-key", "Gemini CLI starts with the Gemini API Key selected");
            File.WriteAllText(hostGeminiFile, "{\"model\": {\"name\": \"gemini-3-flash-preview\"}, \"security\": {\"auth\": {\"selectedType\": \"oauth-personal\"}}}");
            check(!await host.LaunchChanged(), "editing Gemini CLI's settings is no new launch");
            check((string?)Settings(hostGeminiFile)["security"]?["auth"]?["selectedType"] == "gemini-api-key" && (string?)Settings(hostGeminiFile)["model"]?["name"] == "gemini-3-flash-preview",
                "the next launch check sets the sign-in back and keeps the user's model");
            // A key Google turns down comes back as an ordinary 400 (API_KEY_INVALID): the note all the same, no sign-in, no retry.
            PromptError(new { code = 400, message = invalidKey });
            check(host.SendUser("rejected Gemini key"), "a turn with a rejected Gemini API Key accepted");
            await until(() => !host.Busy && host.Status == "驗證失敗 · Gemini CLI" && host.Detail == gemini.CredentialNote, "Gemini's 400 API_KEY_INVALID shows Gemini CLI's note", 15);
            check(Starts() == before + 2 && permissions == 0, "a rejected Gemini API Key is not retried and opens no sign-in");
            // A suspended key comes back as a 403 whose message names the consumer by the key itself: the note, never that message.
            string suspended = "{\"error\":{\"code\":403,\"message\":\"Permission denied: Consumer 'api_key:" + GeminiFakeKey + "' has been suspended.\",\"status\":\"PERMISSION_DENIED\","
                + "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"CONSUMER_SUSPENDED\",\"domain\":\"googleapis.com\"}]}}";
            PromptError(new { code = 403, message = suspended });
            int suspendedMark = Mark();
            check(host.SendUser("suspended Gemini key"), "a turn with a suspended Gemini API Key accepted");
            await until(() => SeenAfter(suspendedMark, "Gemini CLI 正在處理…") && File.Exists(Path.Combine(workspace, "fixture-prompt-error-used")) && !host.Busy
                && host.Status == "驗證失敗 · Gemini CLI" && host.Detail == gemini.CredentialNote, "Google's 403 CONSUMER_SUSPENDED shows Gemini CLI's note", 15);
            // Any other error that quotes the key is shown with *** in its place.
            string quota = "{\"error\":{\"code\":429,\"message\":\"Quota exceeded for quota metric 'Generate Content API requests per minute' of service "
                + "'generativelanguage.googleapis.com' for consumer 'api_key:" + GeminiFakeKey + "'.\",\"status\":\"RESOURCE_EXHAUSTED\"}}";
            PromptError(new { code = 429, message = quota });
            check(host.SendUser("quota error quoting the key"), "a turn whose error quotes the Gemini API Key accepted");
            await until(() => !host.Busy && host.Status == "連線或要求失敗" && host.Detail.Contains("Quota exceeded") && host.Detail.Contains("'api_key:***'"),
                "an ordinary error that quotes the key stays an ordinary failure", 15);
            check(!host.Detail.Contains(GeminiFakeKey) && !host.Detail.Contains("AIza") && Starts() == before + 2 && permissions == 0,
                "the key an error quotes is masked before the status shows it");
            keys["gemini"] = null;
            check(await host.LaunchChanged(), "clearing the Gemini API Key is a change");
            await until(() => !host.Connected && host.Status == "尚未啟動 · Gemini CLI" && host.Detail == Missing, "without a Gemini API Key Gemini CLI does not start", 15);
            await Task.Delay(500);
            check(Starts() == before + 2, "Gemini CLI's own sign-in is never used in the key's place");
            keys["gemini"] = GeminiFakeKey;
            File.WriteAllText(hostGeminiFile, "{ not json");
            check(await host.LaunchChanged(), "a key saved while the settings are broken is a change");
            await until(() => !host.Connected && host.Status == "連線或要求失敗" && host.Detail.Contains(hostGeminiFile), "settings that are not JSON stop Gemini CLI and say which file", 15);
            File.Delete(hostGeminiFile);
            host.Reconnect();
            await until(() => host.Connected && !host.Busy && Starts() == before + 3 && host.SessionId == geminiApiSession, "重新連線 resumes the Gemini API Key's conversation", 15);
            check(Child()["GEMINI_API_KEY"] == "gemini" && (string?)Settings(hostGeminiFile)["security"]?["auth"]?["selectedType"] == "gemini-api-key",
                "the settings are written again for the start");
            on["codex"] = false; on["gemini"] = false;
            host.Configure("claude", new(true, false, false));
            await until(() => host.Connected && !host.Busy && host.SessionId == claudeCurrent && host.LaunchProfile == claude.Profile, "back on Claude Code's API Key conversation", 15);

            // 清空工作區 ends the reservations of every saved conversation, the API Key's included.
            string current = host.SessionId;
            host.Wakeups.Schedule("claude", current, "fixture care", 60, null, null, DateTimeOffset.Now);
            await host.ClearWorkspace(workspace);
            check(host.Wakeups.List("claude", current).Length == 0, "clearing the workspace ends the API Key conversation's reservations");
            await until(() => host.Connected && !host.Busy, "Claude Code connects again after clearing", 15);

            // The keys stay out of everything the fish writes or shows.
            var written = Directory.EnumerateFiles(agentRoot, "*", SearchOption.AllDirectories).ToArray();
            check(written.Length > 0 && written.All(f => File.ReadAllText(f) is var text && !text.Contains("sk-ant") && !text.Contains("sk-proj") && !text.Contains("AIza")),
                "no key is in a file the fish writes (sessions, wakeups, workspace state, the CLIs' settings)");
            check(!Directory.EnumerateFileSystemEntries(claude.ConfigFolder(agentRoot), "*", SearchOption.AllDirectories).Any(), "the fish writes nothing into Claude Code's config folder");
            check(Directory.EnumerateFiles(codex.ConfigFolder(agentRoot), "*", SearchOption.AllDirectories).SequenceEqual([codex.SettingsFile(agentRoot)])
                && Directory.EnumerateFiles(gemini.ConfigFolder(agentRoot), "*", SearchOption.AllDirectories).SequenceEqual([gemini.SettingsFile(agentRoot)]),
                "the fish writes only config.toml into Codex CLI's folder and .gemini/settings.json into Gemini CLI's");
            string log = DiagnosticLog.ExportText();
            check(!log.Contains("sk-ant") && !log.Contains("sk-proj") && !log.Contains("AIza") && !log.Contains("FAKE") && log.Contains("claude-api") && log.Contains("codex-api") && log.Contains("gemini-api"),
                "the diagnostic log names the profiles, never a key");
            check(!(host.Status + host.Detail).Contains("sk-ant"), "the status never shows the key");
            DiagnosticLog.ObserveSettings(new { AgentClaudeApiKey = false }); DiagnosticLog.ObserveSettings(new { AgentClaudeApiKey = true });
            DiagnosticLog.ObserveSettings(new { AgentOpenAiApiKey = false }); DiagnosticLog.ObserveSettings(new { AgentOpenAiApiKey = true });
            DiagnosticLog.ObserveSettings(new { AgentGeminiApiKey = false }); DiagnosticLog.ObserveSettings(new { AgentGeminiApiKey = true });
            string observed = DiagnosticLog.ExportText();
            check(observed.Contains("changed=AgentClaudeApiKey") && observed.Contains("changed=AgentOpenAiApiKey") && observed.Contains("changed=AgentGeminiApiKey"),
                "the diagnostic log names a change of each switch by name only");
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
