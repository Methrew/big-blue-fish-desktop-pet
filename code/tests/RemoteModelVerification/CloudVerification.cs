// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BigBlueFish.Windows;
using SkiaSharp;

// The cloud model source (CompanionCloud.cs) as macOS runs it, against fake Claude, OpenAI and Gemini servers on this
// computer (CompanionCloud.RedirectForTests): the requests each provider gets (system prompt, one user message, the window
// image, no sampling or llama.cpp fields, low effort and its retry), streamed text, model lists and preselection, the key
// prefixes, every error in Traditional Chinese, the pauses after them, the daily limit and the usage file, and that no key
// or response body reaches the diagnostic log, a status line or the usage file. No real service or key is used.
internal static class CloudVerification
{
    const string ClaudeKey = "sk-ant-api03-FAKE-cloud-test-0001", OpenAiKey = "sk-proj-FAKE-cloud-test-0002", GeminiKey = "AIzaFAKE-cloud-test-0003";
    const string Reply = "主人，記得喝水喔～";
    static int checks;
    static string work = "";
    static int brains;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

    static CompanionBrain NewBrain(string provider, string key, string model)
    {
        var brain = new CompanionBrain(_ => null, TimeSpan.FromSeconds(1), weatherClient: new HttpClient(), newsRoll: () => 1,
            cloudUsagePath: Path.Combine(work, $"cloud-usage-{++brains}.json"));
        brain.UseCloudModel(provider, key, model);
        brain.ScreenImages = true;
        return brain;
    }

    static async Task<(string Reply, string Status, List<string> Partials)> Speak(CompanionBrain brain)
    {
        var partials = new List<string>();
        var reply = await brain.Generate(false, true, CompanionNative.Foreground, partials.Add);
        return (reply, brain.Status, partials);
    }

    // Window captures and OCR reads so far (the stand-ins in Stubs.cs count them).
    static int ScreenReads() => LinuxCapture.Calls.Count + CompanionOcr.Calls.Count;

    // Seconds from now until the pause ends.
    static double PauseSeconds(CompanionBrain brain) => brain.CloudPausedUntil is { } until ? (until - DateTime.UtcNow).TotalSeconds : 0;

    internal static async Task<int> Run()
    {
        work = Directory.CreateTempSubdirectory("bbf-cloud-").FullName;
        // What the SDK would otherwise take from the environment must not change where the key goes or what is sent.
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "http://127.0.0.1:9/never");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-from-environment");
        Environment.SetEnvironmentVariable("ANTHROPIC_CUSTOM_HEADERS", "X-Leak: from-environment");
        using var claude = new FakeCloud();
        using var openai = new FakeCloud();
        using var gemini = new FakeCloud();
        try
        {
            Metadata();
            CompanionCloud.RedirectForTests(CompanionCloud.Claude, claude.Url);
            CompanionCloud.RedirectForTests(CompanionCloud.OpenAI, openai.Url + "/v1/");
            CompanionCloud.RedirectForTests(CompanionCloud.Gemini, gemini.Url + "/v1beta/");
            await Guard();
            await Claude(claude);
            await OpenAi(openai);
            await Gemini(gemini);
            await LimitAndUsage(claude);
            Secrets(claude, openai, gemini);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_CUSTOM_HEADERS", null);
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
        return checks;
    }

    static void Metadata()
    {
        Check(CompanionCloud.ProviderForKey("sk-ant-api03-abc") == "Claude" && CompanionCloud.ProviderForKey("  sk-ant-admin01-x ") == "Claude"
            && CompanionCloud.ProviderForKey("sk-proj-abc") == "OpenAI" && CompanionCloud.ProviderForKey("sk-svcacct-abc") == "OpenAI" && CompanionCloud.ProviderForKey("sk-abc") == "OpenAI"
            && CompanionCloud.ProviderForKey("AIzaSyAbc") == "Gemini" && CompanionCloud.ProviderForKey("AQ.Ab8RN6abc") == "Gemini"
            && CompanionCloud.ProviderForKey("hello") == null && CompanionCloud.ProviderForKey("") == null && CompanionCloud.ProviderForKey(null) == null,
            "a pasted key names its service: sk-ant- Claude, AIza and AQ. Gemini, other sk- OpenAI");
        var p = CompanionCloud.Providers;
        Check(p.Select(x => x.Id).SequenceEqual(["Claude", "OpenAI", "Gemini"]) && p.Select(x => x.DisplayName).SequenceEqual(["Claude API（Anthropic）", "OpenAI API", "Gemini API（Google）"])
            && p.Select(x => x.Endpoint).SequenceEqual(["https://api.anthropic.com", "https://api.openai.com/v1/", "https://generativelanguage.googleapis.com/v1beta/"])
            && p.Select(x => x.KeyPage).SequenceEqual(["https://platform.claude.com/settings/keys", "https://platform.openai.com/api-keys", "https://aistudio.google.com/apikey"])
            && p.Select(x => x.KeychainAccount).SequenceEqual(["ClaudeApiKey", "OpenAiApiKey", "GeminiApiKey"]),
            "providers: names, official endpoints, key pages and keychain accounts");
        Check(CompanionCloud.Endpoint("Claude").ToString() == "https://api.anthropic.com/" && CompanionCloud.Endpoint("OpenAI").Host == "api.openai.com"
            && CompanionCloud.Endpoint("Gemini").Host == "generativelanguage.googleapis.com", "without a test redirect every provider is its official endpoint");
        Check(CompanionCloud.DefaultModel("Claude") == "claude-opus-5-5" && CompanionCloud.DefaultModel("OpenAI") == "gpt-6-luna" && CompanionCloud.DefaultModel("Gemini") == "gemini-3.5-flash-lite"
            && CompanionCloud.DefaultModel("Nonsense") == "claude-opus-5-5", "recommended models");
        Check(CompanionCloud.CostHint("Claude", "claude-opus-5-5") == "約 US$0.02" && CompanionCloud.CostHint("Claude", "claude-sonnet-5-5") == "約 US$0.01"
            && CompanionCloud.CostHint("Claude", "claude-haiku-5-5") == "約 US$0.0005" && CompanionCloud.CostHint("Claude", "claude-fable-5-2") == null,
            "cost hints per line for Claude Opus 5.5, Sonnet 5.5 and Haiku 5.5");
        bool refused;
        try { CompanionCloud.RedirectForTests("Claude", "https://example.com"); refused = false; } catch (ArgumentException) { refused = true; }
        Check(refused && CompanionCloud.Endpoint("Claude").Host == "api.anthropic.com", "the test redirect takes only a server on this computer");
    }

    static async Task Guard()
    {
        // Behind the guard nothing may be sent at all.
        using var invoker = new HttpMessageInvoker(new CloudGuard("OpenAI") { InnerHandler = new Refuse() });
        bool blocked;
        try { await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/v1/models"), default); blocked = false; }
        catch (HttpRequestException) { blocked = true; }
        Check(blocked && Refuse.Calls == 0, "a request addressed anywhere but the provider's endpoint is stopped before it is sent");
    }

    sealed class Refuse : HttpMessageHandler
    {
        internal static int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; throw new InvalidOperationException(); }
    }

    // ------------------------------------------------------------------------------------------------ Claude
    static string Event(string name, object data) => $"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n";
    static Dictionary<string, object?> ClaudeUsage(long input, long output) => new() { ["input_tokens"] = input, ["cache_creation_input_tokens"] = 0, ["cache_read_input_tokens"] = 0, ["output_tokens"] = output };
    static string ClaudeStream(string model, string[] pieces, string? stop, bool fallback = false, bool thinking = true)
    {
        var events = new StringBuilder();
        events.Append(Event("message_start", new { type = "message_start", message = new Dictionary<string, object?> { ["id"] = "msg_fake", ["type"] = "message", ["role"] = "assistant",
            ["content"] = Array.Empty<object>(), ["model"] = fallback ? "claude-opus-5" : model, ["stop_reason"] = null, ["stop_sequence"] = null, ["usage"] = ClaudeUsage(1234, 1) } }));
        int index = 0;
        if (fallback)
        {
            events.Append(Event("content_block_start", new { type = "content_block_start", index, content_block = new Dictionary<string, object?> { ["type"] = "fallback", ["from"] = new { model }, ["to"] = new { model = "claude-opus-5" } } }));
            events.Append(Event("content_block_stop", new { type = "content_block_stop", index = index++ }));
        }
        if (thinking)
        {
            // Adaptive thinking with its text omitted: an empty block and a signature come first.
            events.Append(Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "thinking", thinking = "", signature = "" } }));
            events.Append(Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "signature_delta", signature = "EqQBfake" } }));
            events.Append(Event("content_block_stop", new { type = "content_block_stop", index = index++ }));
        }
        events.Append(Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } }));
        events.Append(Event("ping", new { type = "ping" }));
        foreach (var piece in pieces) events.Append(Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text = piece } }));
        events.Append(Event("content_block_stop", new { type = "content_block_stop", index }));
        if (stop == null) return events.ToString();   // cut before message_delta
        var usage = ClaudeUsage(1234, 42);
        if (fallback) usage["iterations"] = new object[]
        {
            new { type = "message", model, input_tokens = 1234, output_tokens = 0, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
            new { type = "fallback_message", model = "claude-opus-5", input_tokens = 1234, output_tokens = 42, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 }
        };
        var delta = new Dictionary<string, object?> { ["stop_reason"] = stop, ["stop_sequence"] = null };
        if (stop == "refusal") delta["stop_details"] = new Dictionary<string, object?> { ["type"] = "refusal", ["category"] = "cyber", ["explanation"] = null };
        events.Append(Event("message_delta", new { type = "message_delta", delta, usage }));
        events.Append(Event("message_stop", new { type = "message_stop" }));
        return events.ToString();
    }
    // A decline after Opus 5.5 had written a few tokens: its partial stays, a fallback block marks the switch, Claude Opus 5
    // continues. Top-level usage is the fallback attempt's; usage.iterations has both, and both are billed.
    static string ClaudeMidStreamFallback(string model, string partial, string rest)
    {
        var events = new StringBuilder();
        events.Append(Event("message_start", new { type = "message_start", message = new Dictionary<string, object?> { ["id"] = "msg_fake", ["type"] = "message", ["role"] = "assistant",
            ["content"] = Array.Empty<object>(), ["model"] = model, ["stop_reason"] = null, ["stop_sequence"] = null, ["usage"] = ClaudeUsage(1234, 1) } }));
        events.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }));
        events.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = partial } }));
        events.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        events.Append(Event("content_block_start", new { type = "content_block_start", index = 1, content_block = new Dictionary<string, object?> { ["type"] = "fallback", ["from"] = new { model }, ["to"] = new { model = "claude-opus-5" } } }));
        events.Append(Event("content_block_stop", new { type = "content_block_stop", index = 1 }));
        events.Append(Event("content_block_start", new { type = "content_block_start", index = 2, content_block = new { type = "text", text = "" } }));
        events.Append(Event("content_block_delta", new { type = "content_block_delta", index = 2, delta = new { type = "text_delta", text = rest } }));
        events.Append(Event("content_block_stop", new { type = "content_block_stop", index = 2 }));
        var usage = ClaudeUsage(1234, 42);
        usage["iterations"] = new object[]
        {
            new { type = "message", model, input_tokens = 1234, output_tokens = 7, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
            new { type = "fallback_message", model = "claude-opus-5", input_tokens = 1234, output_tokens = 42, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 }
        };
        events.Append(Event("message_delta", new { type = "message_delta", delta = new Dictionary<string, object?> { ["stop_reason"] = "end_turn", ["stop_sequence"] = null }, usage }));
        events.Append(Event("message_stop", new { type = "message_stop" }));
        return events.ToString();
    }
    static FakeCloud.Reply ClaudeError(int status, string type, string message, params (string, string)[] headers) =>
        new(status, JsonSerializer.Serialize(new { type = "error", error = new { type, message }, request_id = "req_fake" }), Headers: headers);
    static FakeCloud.Reply ClaudeModels(FakeCloud.Request request)
    {
        object Model(string id, string name) => new { type = "model", id, display_name = name, created_at = "2026-10-01T00:00:00Z", max_input_tokens = 1000000, max_tokens = 128000 };
        return request.Path.Contains("after_id=", StringComparison.Ordinal)
            ? new(200, JsonSerializer.Serialize(new { data = new[] { Model("claude-opus-5", "Claude Opus 5"), Model("not-claude-1", "Other") }, has_more = false, first_id = "claude-opus-5", last_id = "not-claude-1" }))
            : new(200, JsonSerializer.Serialize(new { data = new[] { Model("claude-opus-5-5", "Claude Opus 5.5"), Model("claude-sonnet-5-5", "Claude Sonnet 5.5"), Model("claude-haiku-5-5", "Claude Haiku 5.5") },
                has_more = true, first_id = "claude-opus-5-5", last_id = "claude-haiku-5-5" }));
    }
    static FakeCloud.Reply Sse(string body) => new(200, body, "text/event-stream");

    static async Task Claude(FakeCloud server)
    {
        // No key: the source stays (nothing local is asked for), nothing is sent, the status says why.
        using (var brain = NewBrain("Claude", "", ""))
        {
            Check(brain.UseCloud && brain.UseRemote && brain.ModelAvailable && brain.CanRunOnCurrentPower
                && brain.Status == "尚未填寫 Claude 的 API Key，請到設定 › 模型與資訊填寫後按「儲存並測試」。",
                "the cloud source without a key still replaces the local model, and its status says the key is missing from the start");
            int screen = ScreenReads();
            var (reply, status, _) = await Speak(brain);
            Check(reply == "" && status == "尚未填寫 Claude 的 API Key，請到設定 › 模型與資訊填寫後按「儲存並測試」。" && server.Take().Count == 0 && ScreenReads() == screen,
                "without a key nothing is sent, nothing of the screen is read, and the status says what to do");
            Check(await brain.TestRemote() == status, "testing without a key says the same");
        }

        using (var brain = NewBrain("Claude", ClaudeKey, ""))
        {
            server.Respond = ClaudeModels;
            var result = await brain.TestRemote();
            var requests = server.Take();
            Check(result == "雲端 AI 服務就緒：Claude（claude-opus-5-5）（API Key 有效，找到 4 個可用模型）" && brain.Status == result, "儲存並測試 lists the models: " + result);
            Check(brain.CloudModels.Select(m => m.Id).SequenceEqual(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5"])
                && brain.CloudModels[0].DisplayName == "Claude Opus 5.5", "both pages of the Models API, Claude models only");
            Check(CompanionCloud.Preselect("Claude", brain.CloudModels) == "claude-opus-5-5" && CompanionCloud.Preselect("Claude", brain.CloudModels, "claude-haiku-5-5") == "claude-haiku-5-5"
                && CompanionCloud.Preselect("Claude", brain.CloudModels, "claude-gone") == "claude-opus-5-5"
                && CompanionCloud.Preselect("Claude", [new("claude-sonnet-5-5", "S")]) == "claude-sonnet-5-5", "Claude preselects Opus 5.5, keeps a chosen listed model");
            Check(requests.Count == 2 && requests.All(r => r.Method == "GET") && requests[0].Path == "/v1/models?limit=100" && requests[1].Path.Contains("after_id=claude-haiku-5-5"),
                "GET /v1/models, then the next page");
            Check(requests.All(r => r.Header("Authorization") == "Bearer " + ClaudeKey && r.Header("x-api-key") == null && r.Header("X-Leak") == null),
                "the key from the settings (not ANTHROPIC_API_KEY), and no header from ANTHROPIC_CUSTOM_HEADERS");
            Check(brain.ExecutionDevice == "雲端 AI 服務：Claude API（Anthropic）", "the inference device names the service");

            // Opus 5.5: the beta endpoint with server-side fallbacks.
            server.Respond = _ => Sse(ClaudeStream("claude-opus-5-5", ["主人，", "記得喝水", "喔～"], "end_turn"));
            var (reply, status, partials) = await Speak(brain);
            var sent = server.Take().Single();
            var body = sent.Json!.AsObject();
            Check(reply == Reply && status == "雲端 AI 服務就緒：Claude（claude-opus-5-5）" && partials.Count >= 2 && partials[^1] == Reply, "the streamed line is said, partials on the way");
            Check(sent.Method == "POST" && sent.Path == "/v1/messages" && sent.Header("anthropic-beta") == "server-side-fallback-2026-07-01"
                && body["fallbacks"]?.GetValueKind() == JsonValueKind.String && body["fallbacks"]!.GetValue<string>() == "default" && !body.ContainsKey("betas"),
                "Opus 5.5 goes through the beta with fallbacks \"default\"");
            Check(body["model"]!.GetValue<string>() == "claude-opus-5-5" && body["max_tokens"]!.GetValue<int>() == 4096 && body["output_config"]!["effort"]!.GetValue<string>() == "low"
                && body["stream"]!.GetValue<bool>(), "model, max_tokens 4096, effort low, streaming");
            Check(body["system"]!.GetValue<string>().StartsWith(CompanionBrain.Persona, StringComparison.Ordinal), "the persona is the system prompt");
            Check(new[] { "temperature", "top_p", "top_k", "thinking", "cache_prompt", "chat_template_kwargs", "stream_options", "n_predict" }.All(k => !body.ContainsKey(k)),
                "no sampling parameters, no thinking field and no llama.cpp fields");
            var content = body["messages"]!.AsArray().Single()!["content"]!.AsArray();
            Check(body["messages"]![0]!["role"]!.GetValue<string>() == "user" && content.Count == 2 && content[0]!["type"]!.GetValue<string>() == "image"
                && content[0]!["source"]!["type"]!.GetValue<string>() == "base64" && content[0]!["source"]!["media_type"]!.GetValue<string>() == "image/jpeg", "one user message: the window image first");
            using (var jpeg = SKBitmap.Decode(Convert.FromBase64String(content[0]!["source"]!["data"]!.GetValue<string>())))
                Check(jpeg.Width == 1568 && jpeg.Height == 941, "the window image is the JPEG with a long side of 1568");
            var text = content[1]!["text"]!.GetValue<string>();
            Check(content[1]!["type"]!.GetValue<string>() == "text" && text.Contains("【本輪螢幕資料】") && text.Contains("備忘錄") && text.Contains("購物清單") && !text.Contains(CompanionOcr.WindowText),
                "then the turn's text: time, app and window title, no OCR text");
            Check(sent.Header("Authorization") == "Bearer " + ClaudeKey && sent.Header("X-Leak") == null, "the message request carries the key and nothing from the environment");
            Check(brain.CloudUsage.Today is { Requests: 1, InputTokens: 1234, OutputTokens: 42 }, "the request and its tokens are counted");

            // Fallback served: the fallback block is no text of its own.
            var before = brain.CloudUsage.Today;
            server.Respond = _ => Sse(ClaudeStream("claude-opus-5-5", [Reply], "end_turn", fallback: true));
            (reply, _, _) = await Speak(brain);
            var after = brain.CloudUsage.Today;
            Check(reply == Reply && server.Take().Count == 1, "a line a fallback model wrote is said like any other");
            Check(after.InputTokens - before.InputTokens == 1234 && after.OutputTokens - before.OutputTokens == 42,
                "a decline before any output is not billed: the tokens are the fallback attempt's");
            // Declined mid-line: the partial and its input are billed too (usage.iterations), and the line goes on.
            before = after;
            server.Respond = _ => Sse(ClaudeMidStreamFallback("claude-opus-5-5", "主人，", "記得喝水喔～"));
            (reply, _, _) = await Speak(brain);
            after = brain.CloudUsage.Today;
            Check(reply == Reply && server.Take().Count == 1 && after.Requests - before.Requests == 1
                && after.InputTokens - before.InputTokens == 2468 && after.OutputTokens - before.OutputTokens == 49,
                "a decline mid-line counts both attempts' tokens from usage.iterations");

            // Without image option: OCR text of the foreground window only.
            brain.ScreenImages = false;
            server.Respond = _ => Sse(ClaudeStream("claude-opus-5-5", [Reply], "end_turn"));
            await Speak(brain);
            content = server.Take().Single().Json!["messages"]![0]!["content"]!.AsArray();
            Check(content.Count == 1 && content[0]!["text"]!.GetValue<string>().Contains(CompanionOcr.WindowText), "without the screenshot option the window's OCR text goes as text");
            brain.ScreenImages = true;
        }

        // Haiku 5.5: the plain endpoint, no fallbacks (it has none), effort low.
        using (var brain = NewBrain("Claude", ClaudeKey, "claude-haiku-5-5"))
        {
            server.Respond = _ => Sse(ClaudeStream("claude-haiku-5-5", [Reply], "end_turn", thinking: false));
            var (reply, status, _) = await Speak(brain);
            var sent = server.Take().Single();
            Check(reply == Reply && status.EndsWith("（claude-haiku-5-5）") && sent.Header("anthropic-beta") == null && !sent.Json!.AsObject().ContainsKey("fallbacks")
                && sent.Json!["output_config"]!["effort"]!.GetValue<string>() == "low", "Haiku 5.5 goes to the plain Messages API with effort low");
        }
        // Haiku 4.5 has no effort; an unknown model that refuses it is asked again without it.
        using (var brain = NewBrain("Claude", ClaudeKey, "claude-haiku-4-5"))
        {
            server.Respond = _ => Sse(ClaudeStream("claude-haiku-4-5", [Reply], "end_turn", thinking: false));
            await Speak(brain);
            Check(!server.Take().Single().Json!.AsObject().ContainsKey("output_config"), "Claude Haiku 4.5 gets no effort");
        }
        using (var brain = NewBrain("Claude", ClaudeKey, "claude-test-noeffort"))
        {
            server.Respond = r => r.Json!.AsObject().ContainsKey("output_config")
                ? ClaudeError(400, "invalid_request_error", "output_config.effort: This model does not support the effort parameter.")
                : Sse(ClaudeStream("claude-test-noeffort", [Reply], "end_turn", thinking: false));
            var (reply, _, _) = await Speak(brain);
            var sent = server.Take();
            Check(reply == Reply && sent.Count == 2 && sent[0].Json!.AsObject().ContainsKey("output_config") && !sent[1].Json!.AsObject().ContainsKey("output_config")
                && brain.CloudUsage.Today.Requests == 1, "a 400 naming effort is retried once without it (one request counted)");
        }

        // Replies that end without a line.
        await ClaudeOutcome(server, _ => Sse(ClaudeStream("claude-opus-5-5", ["我"], "refusal")), "Claude 婉拒了這次的請求，這次魚魚先不說話。", 0, "a refusal (after a partial line) says nothing and tells why");
        await ClaudeOutcome(server, _ => Sse(ClaudeStream("claude-opus-5-5", ["半句"], null)), "Claude 的回覆中途中斷了，這次魚魚先不說話。", 0, "a stream cut before message_delta is not said");
        await ClaudeOutcome(server, _ => Sse(ClaudeStream("claude-opus-5-5", [], "max_tokens")), "Claude 還沒說出內容就用完了輸出上限，這次魚魚先不說話。", 0, "max_tokens without text");
        await ClaudeOutcome(server, _ => Sse(Event("message_start", new { type = "message_start", message = new Dictionary<string, object?> { ["id"] = "m", ["type"] = "message", ["role"] = "assistant",
                ["content"] = Array.Empty<object>(), ["model"] = "claude-opus-5-5", ["stop_reason"] = null, ["stop_sequence"] = null, ["usage"] = ClaudeUsage(10, 1) } })
                + Event("error", new { type = "error", error = new { type = "overloaded_error", message = "Overloaded" } })),
            "Claude 目前忙碌中，魚魚先安靜到 ", 120, "an error event in the stream (overloaded) pauses 2 minutes");

        // HTTP errors, each in Taiwan Traditional Chinese, never the API's own message.
        await ClaudeOutcome(server, _ => ClaudeError(401, "authentication_error", "invalid x-api-key"), "Anthropic 不接受這把 API Key，請確認金鑰正確、沒有被停用，再按「儲存並測試」。", 1800, "401: invalid key, 30 minutes");
        await ClaudeOutcome(server, _ => ClaudeError(400, "invalid_request_error", "Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."),
            "Anthropic 帳戶的額度不足，請到 platform.claude.com 儲值，再按「儲存並測試」。", 1800, "400 credit balance: billing");
        await ClaudeOutcome(server, _ => ClaudeError(402, "billing_error", "Your account has a billing problem."), "Anthropic 帳戶的額度不足", 1800, "402 billing_error: billing");
        // The spend limit set in the Console is a 400: billing (30 minutes), not a bad request that would send the line
        // again as text and turn the window image off.
        await ClaudeOutcome(server, _ => ClaudeError(400, "invalid_request_error", "You have reached your specified API usage limits. You will regain access on 2026-12-01 at 00:00 UTC."),
            "已達到你在 platform.claude.com 設定的 API 花費上限，請到 Claude Console 調高或移除上限，再按「儲存並測試」。", 1800, "400 spend limit you set: billing, 30 minutes, one request", requests: 1);
        await ClaudeOutcome(server, _ => ClaudeError(400, "invalid_request_error", "You have reached your specified workspace API usage limits. You will regain access on 2026-12-01 at 00:00 UTC."),
            "已達到你在 platform.claude.com 設定的 API 花費上限", 1800, "400 workspace spend limit: billing", requests: 1);
        await ClaudeOutcome(server, _ => ClaudeError(403, "permission_error", "forbidden"), "這把 Claude API Key 沒有使用這個模型的權限", 1800, "403: forbidden");
        await ClaudeOutcome(server, _ => ClaudeError(404, "not_found_error", "model: claude-opus-5-5"), "這把 Claude API Key 不能使用模型 claude-opus-5-5，請按「儲存並測試」重新選擇模型。", 1800, "404: model not found");
        await ClaudeOutcome(server, _ => ClaudeError(429, "rate_limit_error", "rate limited", ("retry-after", "120")), "Claude 的請求太頻繁了，魚魚先安靜到 ", 120, "429 pauses as long as Retry-After says");
        await ClaudeOutcome(server, _ => ClaudeError(429, "rate_limit_error", "rate limited"), "Claude 的請求太頻繁了，魚魚先安靜到 ", 300, "429 without Retry-After pauses 5 minutes");
        await ClaudeOutcome(server, _ => ClaudeError(429, "rate_limit_error", "rate limited", ("retry-after", "7200")), "Claude 的請求太頻繁了", 1800, "a long Retry-After is capped at 30 minutes");
        await ClaudeOutcome(server, _ => ClaudeError(529, "overloaded_error", "Overloaded"), "Claude 目前忙碌中，魚魚先安靜到 ", 120, "529: overloaded, 2 minutes");
        await ClaudeOutcome(server, _ => ClaudeError(503, "api_error", "unavailable"), "Claude 目前忙碌中", 120, "503: overloaded");
        await ClaudeOutcome(server, _ => ClaudeError(500, "api_error", "Internal"), "Claude 服務暫時出錯（HTTP 500），稍後再試。", 0, "500: server error, no pause");

        // A pause sends nothing until it ends, the settings change or a test succeeds.
        using (var brain = NewBrain("Claude", ClaudeKey, ""))
        {
            server.Respond = _ => ClaudeError(429, "rate_limit_error", "rate limited", ("retry-after", "60"));
            var (_, first, _) = await Speak(brain);
            server.Take();
            server.Respond = _ => Sse(ClaudeStream("claude-opus-5-5", [Reply], "end_turn"));
            int screen = ScreenReads();
            var (reply, again, _) = await Speak(brain);
            Check(reply == "" && again == first && server.Take().Count == 0 && ScreenReads() == screen, "while paused nothing is sent or read of the screen, and the status keeps the reason: " + first);
            brain.UseCloudModel("Claude", ClaudeKey, "");
            Check(brain.CloudPausedUntil != null && brain.Status == first, "applying the same settings again keeps the pause, and the status still says why");
            brain.UseCloudModel("Claude", ClaudeKey, "claude-sonnet-5-5");
            Check(brain.CloudPausedUntil == null, "choosing another model ends the pause");
            brain.UseCloudModel("Claude", ClaudeKey, "");
            server.Respond = _ => ClaudeError(429, "rate_limit_error", "rate limited");
            await Speak(brain);
            server.Respond = ClaudeModels;
            await brain.TestRemote();
            server.Take();
            Check(brain.CloudPausedUntil == null, "a successful 儲存並測試 ends the pause");
            server.Respond = _ => Sse(ClaudeStream("claude-opus-5-5", [Reply], "end_turn"));
            Check((await Speak(brain)).Reply == Reply, "and the next line is said");
            server.Take();
            server.Respond = _ => ClaudeError(401, "authentication_error", "invalid x-api-key");
            var test = await brain.TestRemote();
            server.Take();
            Check(test.StartsWith("Anthropic 不接受這把 API Key") && brain.CloudModels.Count > 0, "a refused key in the test says so");
            brain.UseCloudModel("Claude", ClaudeKey + "X", "");
            Check(brain.CloudPausedUntil == null && brain.CloudModels.Count == 0, "another key starts afresh");
        }

        // A 400 with the window image: once more as OCR text, then text for this session.
        using (var brain = NewBrain("Claude", ClaudeKey, ""))
        {
            server.Respond = r => r.Body.Contains("\"type\":\"image\"", StringComparison.Ordinal)
                ? ClaudeError(400, "invalid_request_error", "messages.0.content.0.image.source.base64: image exceeds 5 MB maximum")
                : Sse(ClaudeStream("claude-opus-5-5", [Reply], "end_turn"));
            var (reply, _, _) = await Speak(brain);
            var sent = server.Take();
            Check(reply == Reply && sent.Count == 2 && sent[1].Json!["messages"]![0]!["content"]!.AsArray().Single()!["text"]!.GetValue<string>().Contains(CompanionOcr.WindowText),
                "a 400 for a window image is answered once more with the window's OCR text");
            await Speak(brain);
            Check(server.Take().Single().Json!["messages"]![0]!["content"]!.AsArray().Count == 1 && !brain.VisionAvailable, "later turns send text until the settings change");
        }

        // A stream that stalls is given up; a server nobody answers is named in Chinese.
        var idle = CompanionCloud.IdleTimeout;
        CompanionCloud.IdleTimeout = TimeSpan.FromSeconds(1.5);
        try
        {
            await ClaudeOutcome(server, _ => Sse(ClaudeStream("claude-opus-5-5", ["主人", "，記得喝水喔～"], "end_turn")) with { Stall = TimeSpan.FromSeconds(5) },
                "Claude 回應逾時，稍後再試。", 0, "a stream silent for longer than the idle limit times out");
        }
        finally { CompanionCloud.IdleTimeout = idle; }
        using (var closed = new TcpListener(IPAddress.Loopback, 0))
        {
            closed.Start(); int unused = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
            CompanionCloud.RedirectForTests("Claude", $"http://127.0.0.1:{unused}");
            using var brain = NewBrain("Claude", ClaudeKey, "");
            var (reply, status, _) = await Speak(brain);
            CompanionCloud.RedirectForTests("Claude", server.Url);
            Check(reply == "" && status == "連不上 Claude，請確認網路連線後再試。", "a refused connection: " + status);
        }
    }

    // requests: how many requests the line may send (0: not checked).
    static async Task ClaudeOutcome(FakeCloud server, Func<FakeCloud.Request, FakeCloud.Reply> respond, string status, int pauseSeconds, string name, int requests = 0)
    {
        using var brain = NewBrain("Claude", ClaudeKey, "");
        server.Respond = respond;
        var (reply, shown, _) = await Speak(brain);
        var sent = server.Take();
        double paused = PauseSeconds(brain);
        Check(reply == "" && shown.StartsWith(status, StringComparison.Ordinal) && !shown.Contains(ClaudeKey) && (requests == 0 || sent.Count == requests)
            && (pauseSeconds == 0 ? brain.CloudPausedUntil == null : Math.Abs(paused - pauseSeconds) < 10), $"{name}: {shown}");
    }

    // ------------------------------------------------------------------------------------------------ OpenAI
    static string OpenAiChunk(object? delta, string? finish = null, object? usage = null) => "data: " + JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = "chatcmpl-fake", ["object"] = "chat.completion.chunk", ["created"] = 1, ["model"] = "gpt-6-luna", ["obfuscation"] = "x",
        ["choices"] = delta == null ? Array.Empty<object>() : new object[] { new Dictionary<string, object?> { ["index"] = 0, ["delta"] = delta, ["logprobs"] = null, ["finish_reason"] = finish } },
        ["usage"] = usage
    }) + "\n\n";
    static string OpenAiStream(params string[] pieces) => OpenAiChunk(new { role = "assistant", content = "" }) + ": keep-alive\n\n"
        + string.Concat(pieces.Select(p => OpenAiChunk(new { content = p }))) + OpenAiChunk(new { }, "stop")
        + OpenAiChunk(null, usage: new { prompt_tokens = 1200, completion_tokens = 150, total_tokens = 1350, completion_tokens_details = new { reasoning_tokens = 120 } }) + "data: [DONE]\n\n";
    static FakeCloud.Reply OpenAiError(int status, string type, string code, string message, string? param = null, params (string, string)[] headers) =>
        new(status, JsonSerializer.Serialize(new { error = new { message, type, param, code } }), Headers: headers);

    static async Task OpenAi(FakeCloud server)
    {
        const string Models = """{"object":"list","data":[{"id":"text-embedding-3-small","object":"model","created":1,"owned_by":"system"},{"id":"gpt-6.1-terra","object":"model","created":5,"owned_by":"system"},{"id":"gpt-image-2.6-flare","object":"model","created":6,"owned_by":"system"},{"id":"gpt-6-luna","object":"model","created":4,"owned_by":"system"},{"id":"gpt-5.5-pro","object":"model","created":3,"owned_by":"system"},{"id":"gpt-realtime-2.1-mini","object":"model","created":3,"owned_by":"system"},{"id":"omni-moderation-latest","object":"model","created":2,"owned_by":"system"},{"id":"gpt-5-nano","object":"model","created":2,"owned_by":"system","shutdown_date":"2026-01-01"},{"id":"gpt-6-astra","object":"model","created":7,"owned_by":"system","shutdown_date":null},{"id":"whisper-1","object":"model","created":1,"owned_by":"system"},{"id":"ft:gpt-4o:org::abc","object":"model","created":9,"owned_by":"org"},{"id":"o3-deep-research","object":"model","created":8,"owned_by":"system"}]}""";
        using (var brain = NewBrain("OpenAI", OpenAiKey, ""))
        {
            server.Respond = _ => new(200, Models);
            var result = await brain.TestRemote();
            var sent = server.Take().Single();
            Check(result == "雲端 AI 服務就緒：OpenAI（gpt-6-luna）（API Key 有效，找到 3 個可用模型）" && sent.Method == "GET" && sent.Path == "/v1/models"
                && sent.Header("Authorization") == "Bearer " + OpenAiKey, "OpenAI 儲存並測試 reads GET /v1/models with the key: " + result);
            Check(brain.CloudModels.Select(m => m.Id).SequenceEqual(["gpt-6-luna", "gpt-6.1-terra", "gpt-6-astra"]),
                "chat models only (no embeddings, images, realtime, pro, retired, fine-tuned or research models), the recommended first");
            Check(CompanionCloud.Preselect("OpenAI", brain.CloudModels) == "gpt-6-luna" && CompanionCloud.Preselect("OpenAI", [new("gpt-6.1-terra", "t"), new("gpt-5.6-luna", "l")]) == "gpt-5.6-luna"
                && CompanionCloud.Preselect("OpenAI", [new("gpt-6-astra", "a")]) == "gpt-6-astra", "OpenAI preselects GPT-6 Luna, then GPT-5.6 Luna, else the first chat model");

            server.Respond = _ => Sse(OpenAiStream("主人，", "記得喝水喔～"));
            var (reply, status, partials) = await Speak(brain);
            sent = server.Take().Single();
            var body = sent.Json!.AsObject();
            Check(reply == Reply && status == "雲端 AI 服務就緒：OpenAI（gpt-6-luna）" && partials[^1] == Reply, "the streamed OpenAI line is said");
            Check(sent.Method == "POST" && sent.Path == "/v1/chat/completions" && sent.Header("Authorization") == "Bearer " + OpenAiKey, "POST /v1/chat/completions with the key");
            Check(body["model"]!.GetValue<string>() == "gpt-6-luna" && body["max_completion_tokens"]!.GetValue<int>() == 4096 && body["reasoning_effort"]!.GetValue<string>() == "low"
                && body["store"]!.GetValue<bool>() == false && body["stream"]!.GetValue<bool>() && body["stream_options"]!["include_usage"]!.GetValue<bool>(),
                "max_completion_tokens 4096, reasoning effort low, store false, streaming with usage");
            Check(new[] { "max_tokens", "temperature", "top_p", "cache_prompt", "chat_template_kwargs" }.All(k => !body.ContainsKey(k)), "no max_tokens, sampling or llama.cpp fields");
            var messages = body["messages"]!.AsArray();
            var content = messages[1]!["content"]!.AsArray();
            Check(messages.Count == 2 && messages[0]!["role"]!.GetValue<string>() == "developer" && messages[0]!["content"]!.GetValue<string>().StartsWith(CompanionBrain.Persona, StringComparison.Ordinal)
                && messages[1]!["role"]!.GetValue<string>() == "user" && content[0]!["type"]!.GetValue<string>() == "text"
                && content[1]!["image_url"]!["url"]!.GetValue<string>().StartsWith("data:image/jpeg;base64,") && content[1]!["image_url"]!["detail"]!.GetValue<string>() == "low",
                "a developer message with the persona, one user message with the text and the window image (detail low)");
            Check(brain.CloudUsage.Today is { Requests: 1, InputTokens: 1200, OutputTokens: 150 }, "the usage chunk is counted");

            server.Respond = r => r.Json!.AsObject().ContainsKey("reasoning_effort")
                ? OpenAiError(400, "invalid_request_error", "unsupported_parameter", "Unsupported parameter: 'reasoning_effort' is not supported with this model.", "reasoning_effort")
                : Sse(OpenAiStream(Reply));
            (reply, _, _) = await Speak(brain);
            var both = server.Take();
            Check(reply == Reply && both.Count == 2 && !both[1].Json!.AsObject().ContainsKey("reasoning_effort"), "a 400 naming reasoning_effort is retried once without it");

            // A restricted key without the model-list permission answers 401 (not 403) and names the missing scope.
            server.Respond = _ => OpenAiError(401, "invalid_request_error", "", "You have insufficient permissions for this operation. Missing scopes: api.model.read. Check that you have the correct role in your organization (Reader, Writer, Owner) and project (Member, Owner), and if you're using a restricted API key, that it has the necessary scopes.");
            Check((await brain.TestRemote()).StartsWith("這把 OpenAI API Key 沒有讀取模型清單的權限") && brain.CloudPausedUntil == null, "a restricted key that may not list models is told it can still name one");
            server.Take();
            // A 403 is no restricted key (a region block, for example): no such hint, and a pause.
            server.Respond = _ => OpenAiError(403, "invalid_request_error", "unsupported_country_region_territory", "Country, region, or territory not supported");
            var blocked = await brain.TestRemote();
            server.Take();
            Check(blocked.StartsWith("這把 OpenAI API Key 沒有使用這項服務的權限") && brain.CloudPausedUntil != null, "a 403 while listing models is not taken for a restricted key: " + blocked);
        }

        await OpenAiOutcome(server, _ => OpenAiError(401, "invalid_request_error", "invalid_api_key", "Incorrect API key provided: sk-proj-****0002."), "OpenAI 不接受這把 API Key", 1800, "401: invalid key");
        await OpenAiOutcome(server, _ => OpenAiError(429, "insufficient_quota", "insufficient_quota", "You exceeded your current quota"), "OpenAI 帳戶的額度或花費上限已用完", 1800, "429 insufficient_quota: billing, no retry");
        await OpenAiOutcome(server, _ => OpenAiError(429, "insufficient_quota", "credit_balance_exhausted", "no credit"), "OpenAI 帳戶的額度或花費上限已用完", 1800, "429 credit_balance_exhausted: billing");
        await OpenAiOutcome(server, _ => OpenAiError(429, "insufficient_quota", "project_spend_limit_exceeded", "spend"), "OpenAI 帳戶的額度或花費上限已用完", 1800, "429 spend limit: billing");
        await OpenAiOutcome(server, _ => OpenAiError(429, "billing_not_active", "", "Your account is not active, please check your billing details on our website."),
            "OpenAI 帳戶目前沒有啟用，請到 OpenAI 平台確認付款方式與帳單，再按「儲存並測試」。", 1800, "429 billing_not_active: billing, not a rate limit");
        await OpenAiOutcome(server, _ => OpenAiError(401, "invalid_request_error", "", "You have insufficient permissions for this operation. Missing scopes: model.request."),
            "這把 OpenAI API Key 是限制權限的 Key，缺少魚魚需要的權限", 1800, "401 missing scope: a permission, not an invalid key");
        await OpenAiOutcome(server, _ => OpenAiError(429, "requests", "rate_limit_exceeded", "Rate limit reached for requests", null, ("Retry-After", "56")), "OpenAI 的請求太頻繁了，魚魚先安靜到 ", 56, "429 rate limit with Retry-After");
        await OpenAiOutcome(server, _ => OpenAiError(404, "invalid_request_error", "model_not_found", "The model `gpt-6-luna` does not exist or you do not have access to it."), "這把 OpenAI API Key 不能使用模型 gpt-6-luna", 1800, "404 model_not_found");
        await OpenAiOutcome(server, _ => OpenAiError(403, "invalid_request_error", "unsupported_country_region_territory", "Country, region, or territory not supported"), "這把 OpenAI API Key 沒有使用這項服務的權限", 1800, "403: forbidden");
        await OpenAiOutcome(server, _ => OpenAiError(503, "service_unavailable_error", "server_is_overloaded", "overloaded"), "OpenAI 目前忙碌中，魚魚先安靜到 ", 120, "503: overloaded");
        await OpenAiOutcome(server, _ => OpenAiError(500, "server_error", "server_error", "boom"), "OpenAI 服務暫時出錯（HTTP 500）", 0, "500: server error");
        await OpenAiOutcome(server, _ => Sse(OpenAiChunk(new { content = "主" }) + "data: {\"error\":{\"message\":\"boom\",\"type\":\"server_error\",\"param\":null,\"code\":null}}\n\n"), "OpenAI 服務暫時出錯", 0, "an error event in the stream");
        await OpenAiOutcome(server, _ => Sse(OpenAiChunk(new { refusal = "I can't help with that." }) + OpenAiChunk(new { }, "stop") + "data: [DONE]\n\n"), "OpenAI 婉拒了這次的請求", 0, "a refusal");
        await OpenAiOutcome(server, _ => Sse(OpenAiChunk(new { content = "主人" }) + OpenAiChunk(new { }, "content_filter") + "data: [DONE]\n\n"), "OpenAI 婉拒了這次的請求", 0, "content_filter");
        await OpenAiOutcome(server, _ => Sse(OpenAiChunk(new { }, "length") + "data: [DONE]\n\n"), "OpenAI 還沒說出內容就用完了輸出上限", 0, "length without text");
        await OpenAiOutcome(server, _ => Sse(OpenAiChunk(new { content = "主人" })), "OpenAI 的回覆中途中斷了", 0, "a stream cut before finish_reason");
        var idle = CompanionCloud.IdleTimeout;
        CompanionCloud.IdleTimeout = TimeSpan.FromSeconds(1.5);
        try { await OpenAiOutcome(server, _ => Sse(OpenAiStream("主人", "，記得喝水喔～")) with { Stall = TimeSpan.FromSeconds(5) }, "OpenAI 回應逾時，稍後再試。", 0, "a stalled stream times out"); }
        finally { CompanionCloud.IdleTimeout = idle; }
    }

    static async Task OpenAiOutcome(FakeCloud server, Func<FakeCloud.Request, FakeCloud.Reply> respond, string status, int pauseSeconds, string name)
    {
        using var brain = NewBrain("OpenAI", OpenAiKey, "");
        server.Respond = respond;
        var (reply, shown, _) = await Speak(brain);
        var sent = server.Take();
        double paused = PauseSeconds(brain);
        Check(reply == "" && sent.Count == 1 && shown.StartsWith(status, StringComparison.Ordinal) && !shown.Contains("****") && !shown.Contains(OpenAiKey)
            && (pauseSeconds == 0 ? brain.CloudPausedUntil == null : Math.Abs(paused - pauseSeconds) < 10), $"OpenAI {name}: {shown}");
    }

    // ------------------------------------------------------------------------------------------------ Gemini
    static string GeminiEvent(object data) => "data: " + JsonSerializer.Serialize(data) + "\r\n\r\n";
    static string GeminiStream(string[] pieces, string? finish = "STOP", string model = "gemini-3.5-flash-lite")
    {
        var events = new StringBuilder();
        for (int i = 0; i < pieces.Length; i++)
        {
            var candidate = new Dictionary<string, object?> { ["content"] = new { parts = new object[] { new { text = pieces[i] } }, role = "model" }, ["index"] = 0 };
            if (i == pieces.Length - 1 && finish != null) candidate["finishReason"] = finish;
            events.Append(GeminiEvent(new { candidates = new[] { candidate }, usageMetadata = new { promptTokenCount = 900, candidatesTokenCount = 8, thoughtsTokenCount = 12, totalTokenCount = 920 }, modelVersion = model }));
        }
        if (pieces.Length == 0 && finish != null)
            events.Append(GeminiEvent(new { candidates = new[] { new { finishReason = finish, index = 0 } }, usageMetadata = new { promptTokenCount = 900, thoughtsTokenCount = 4096 } }));
        return events.ToString();
    }
    static FakeCloud.Reply GeminiError(int code, string status, string message, params object[] details) =>
        new(code, JsonSerializer.Serialize(new { error = new { code, message, status, details } }));

    static async Task Gemini(FakeCloud server)
    {
        using (var brain = NewBrain("Gemini", GeminiKey, ""))
        {
            server.Respond = r => r.Path.Contains("pageToken=", StringComparison.Ordinal)
                ? new(200, """{"models":[{"name":"models/gemma-3-27b-it","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-4-hydrogen","displayName":"Gemini 4 Hydrogen","supportedGenerationMethods":["generateContent"],"thinking":true},{"name":"models/imagen-4.0-generate-001","supportedGenerationMethods":["predict"]}]}""")
                : new(200, """{"models":[{"name":"models/gemini-3.5-flash-lite","displayName":"Gemini 3.5 Flash-Lite","outputTokenLimit":65536,"supportedGenerationMethods":["generateContent","countTokens"],"thinking":true},{"name":"models/gemini-embedding-2","supportedGenerationMethods":["embedContent"]},{"name":"models/gemini-3.8-flash-tts","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-2.5-flash-image","supportedGenerationMethods":["generateContent"]}],"nextPageToken":"page 2"}""");
            var result = await brain.TestRemote();
            var sent = server.Take();
            Check(result == "雲端 AI 服務就緒：Gemini（gemini-3.5-flash-lite）（API Key 有效，找到 2 個可用模型）" && brain.CloudModels.Select(m => m.Id).SequenceEqual(["gemini-3.5-flash-lite", "gemini-4-hydrogen"])
                && brain.CloudModels[0].DisplayName == "Gemini 3.5 Flash-Lite", "Gemini lists generateContent models of the gemini family only: " + result);
            Check(sent.Count == 2 && sent[0].Path == "/v1beta/models?pageSize=1000" && sent[1].Path == "/v1beta/models?pageSize=1000&pageToken=page%202"
                && sent.All(r => r.Header("x-goog-api-key") == GeminiKey && r.Header("Authorization") == null && !r.Path.Contains("key=")), "both pages, the key only in x-goog-api-key");
            Check(CompanionCloud.Preselect("Gemini", brain.CloudModels) == "gemini-3.5-flash-lite" && CompanionCloud.Preselect("Gemini", [new("gemini-4-hydrogen", "h"), new("gemini-9-flash-lite", "l")]) == "gemini-9-flash-lite"
                && CompanionCloud.Preselect("Gemini", [new("gemini-4-hydrogen", "h"), new("gemini-9-flash", "f")]) == "gemini-9-flash", "Gemini preselects 3.5 Flash-Lite, else a Flash-Lite, else a Flash");

            server.Respond = _ => Sse(GeminiStream(["主人，", "記得喝水喔～"]));
            var (reply, status, partials) = await Speak(brain);
            var request = server.Take().Single();
            var body = request.Json!.AsObject();
            Check(reply == Reply && status == "雲端 AI 服務就緒：Gemini（gemini-3.5-flash-lite）" && partials[^1] == Reply, "the streamed Gemini line is said");
            Check(request.Method == "POST" && request.Path == "/v1beta/models/gemini-3.5-flash-lite:streamGenerateContent?alt=sse" && request.Header("x-goog-api-key") == GeminiKey
                && request.Header("Authorization") == null, "POST models/…:streamGenerateContent?alt=sse with x-goog-api-key");
            var parts = body["contents"]!.AsArray().Single()!["parts"]!.AsArray();
            Check(body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().StartsWith(CompanionBrain.Persona, StringComparison.Ordinal)
                && body["contents"]![0]!["role"]!.GetValue<string>() == "user" && parts[0]!["inlineData"]!["mimeType"]!.GetValue<string>() == "image/jpeg" && parts[1]!["text"]!.GetValue<string>().Contains("購物清單"),
                "systemInstruction with the persona, one user turn: the window image, then the text");
            var config = body["generationConfig"]!.AsObject();
            Check(config["maxOutputTokens"]!.GetValue<int>() == 4096 && config["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>() == "minimal"
                && new[] { "temperature", "topP", "topK" }.All(k => !config.ContainsKey(k)), "maxOutputTokens 4096, minimal thinking for 3.5 Flash-Lite, no sampling parameters");
            Check(brain.CloudUsage.Today is { Requests: 1, InputTokens: 900, OutputTokens: 20 }, "Gemini usage: output counts the thinking tokens too");
        }
        // A thought part (thought: true) is never said, not even as a partial.
        using (var brain = NewBrain("Gemini", GeminiKey, ""))
        {
            server.Respond = _ => Sse(GeminiEvent(new
            {
                candidates = new[] { new { content = new { parts = new object[] { new { text = "思考中", thought = true }, new { text = Reply } }, role = "model" }, finishReason = "STOP", index = 0 } },
                usageMetadata = new { promptTokenCount = 900, candidatesTokenCount = 8, thoughtsTokenCount = 12 }, modelVersion = "gemini-3.5-flash-lite"
            }));
            var (reply, _, partials) = await Speak(brain);
            server.Take();
            Check(reply == Reply && partials.All(p => !p.Contains("思考中")), "a Gemini thought part is not said");
        }
        Check(GeminiService.Thinking("gemini-2.5-pro")!.ToJsonString() == """{"thinkingBudget":128}""" && GeminiService.Thinking("gemini-2.5-flash")!.ToJsonString() == """{"thinkingBudget":0}"""
            && GeminiService.Thinking("gemini-3.8-flash")!["thinkingLevel"]!.GetValue<string>() == "low" && GeminiService.Thinking("gemini-4-hydrogen")!["thinkingLevel"]!.GetValue<string>() == "low"
            && GeminiService.Thinking("gemini-3.1-pro-preview")!["thinkingLevel"]!.GetValue<string>() == "low" && GeminiService.Thinking("gemini-3.1-flash-lite")!["thinkingLevel"]!.GetValue<string>() == "minimal"
            && GeminiService.Thinking("gemini-2.0-flash") == null, "the lowest thinking setting of each Gemini family");
        using (var brain = NewBrain("Gemini", GeminiKey, "gemini-2.5-flash"))
        {
            server.Respond = r => r.Body.Contains("thinkingConfig", StringComparison.Ordinal)
                ? GeminiError(400, "INVALID_ARGUMENT", "Thinking budget is not supported for this model.")
                : Sse(GeminiStream([Reply], model: "gemini-2.5-flash"));
            var (reply, _, _) = await Speak(brain);
            var sent = server.Take();
            Check(reply == Reply && sent.Count == 2 && sent[0].Json!["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]!.GetValue<int>() == 0
                && !sent[1].Json!["generationConfig"]!.AsObject().ContainsKey("thinkingConfig"), "a 400 about thinking is retried once without thinkingConfig");
        }

        object ErrorInfo(string reason) => new Dictionary<string, object> { ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo", ["reason"] = reason, ["domain"] = "googleapis.com" };
        object RetryInfo(string delay) => new Dictionary<string, object> { ["@type"] = "type.googleapis.com/google.rpc.RetryInfo", ["retryDelay"] = delay };
        object Quota(string id) => new Dictionary<string, object> { ["@type"] = "type.googleapis.com/google.rpc.QuotaFailure", ["violations"] = new[] { new { quotaId = id } } };
        await GeminiOutcome(server, _ => GeminiError(400, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key.", ErrorInfo("API_KEY_INVALID")), "Gemini 不接受這把 API Key（無效或已過期）", 1800, "400 API_KEY_INVALID");
        await GeminiOutcome(server, _ => GeminiError(403, "PERMISSION_DENIED", "Your API key was reported as leaked. Please use another API key."), "Google 判定這把 Gemini API Key 已外洩並停用了", 1800, "403 leaked key");
        await GeminiOutcome(server, _ => GeminiError(400, "FAILED_PRECONDITION", "Gemini API free tier is not available in your country. Please enable billing on your project in Google AI Studio."), "這個 Google 專案目前不能使用 Gemini 的免費額度", 1800, "400 FAILED_PRECONDITION: billing");
        await GeminiOutcome(server, _ => GeminiError(402, "RESOURCE_EXHAUSTED", "Your Prepay credit balance is depleted."), "Gemini 的預付額度已用完", 1800, "402: prepay credit");
        await GeminiOutcome(server, _ => GeminiError(429, "RESOURCE_EXHAUSTED", "You exceeded your current quota", Quota("GenerateRequestsPerMinutePerProjectPerModel"), RetryInfo("17.5s")), "Gemini 的請求太頻繁了，魚魚先安靜到 ", 17, "429 waits for RetryInfo");
        await GeminiOutcome(server, _ => GeminiError(429, "RESOURCE_EXHAUSTED", "quota", Quota("GenerateRequestsPerDayPerProjectPerModel-FreeTier"), RetryInfo("1s")), "Gemini 今天的請求額度用完了（每天台灣時間下午三點或四點重置），魚魚先安靜到 ", 1800, "429 of the daily quota waits 30 minutes");
        await GeminiOutcome(server, _ => GeminiError(404, "NOT_FOUND", "models/gemini-3.5-flash-lite is not found for API version v1beta"), "這把 Gemini API Key 不能使用模型 gemini-3.5-flash-lite", 1800, "404: model not found");
        await GeminiOutcome(server, _ => GeminiError(503, "UNAVAILABLE", "The model is overloaded."), "Gemini 目前忙碌中，魚魚先安靜到 ", 120, "503: overloaded");
        await GeminiOutcome(server, _ => GeminiError(500, "INTERNAL", "internal"), "Gemini 服務暫時出錯（HTTP 500）", 0, "500: server error");
        await GeminiOutcome(server, _ => Sse(GeminiEvent(new { promptFeedback = new { blockReason = "SAFETY" }, usageMetadata = new { promptTokenCount = 900 } })), "Gemini 婉拒了這次的請求", 0, "a blocked prompt");
        await GeminiOutcome(server, _ => Sse(GeminiStream(["主人"], "SAFETY")), "Gemini 婉拒了這次的請求", 0, "finishReason SAFETY");
        await GeminiOutcome(server, _ => Sse(GeminiStream([], "MAX_TOKENS")), "Gemini 還沒說出內容就用完了輸出上限", 0, "MAX_TOKENS while thinking");
        await GeminiOutcome(server, _ => Sse(GeminiStream(["主人"], null)), "Gemini 的回覆中途中斷了", 0, "a stream closed without finishReason");
        await GeminiOutcome(server, _ => Sse(GeminiStream([], "OTHER")), "Gemini 這次沒有產生內容，稍後再試。", 0, "finishReason OTHER without text");
        await GeminiOutcome(server, _ => Sse(GeminiStream([], "MALFORMED_RESPONSE")), "Gemini 這次沒有產生內容", 0, "finishReason MALFORMED_RESPONSE without text");
        await GeminiOutcome(server, _ => Sse(GeminiStream([], "PUP_LIMITED_DISABLED")), "Google 依使用政策限制或停用了這個帳號使用 Gemini", 1800, "finishReason PUP_LIMITED_DISABLED: the account is limited, 30 minutes");
        await GeminiOutcome(server, _ => Sse(GeminiStream(["主"], null) + GeminiEvent(new { error = new { code = 503, status = "UNAVAILABLE", message = "overloaded" } })),
            "Gemini 目前忙碌中，魚魚先安靜到 ", 120, "an error event in the stream");
        var idle = CompanionCloud.IdleTimeout;
        CompanionCloud.IdleTimeout = TimeSpan.FromSeconds(1.5);
        try { await GeminiOutcome(server, _ => Sse(GeminiStream(["主人", "，記得喝水喔～"])) with { Stall = TimeSpan.FromSeconds(5) }, "Gemini 回應逾時，稍後再試。", 0, "a stalled stream times out"); }
        finally { CompanionCloud.IdleTimeout = idle; }
    }

    static async Task GeminiOutcome(FakeCloud server, Func<FakeCloud.Request, FakeCloud.Reply> respond, string status, int pauseSeconds, string name)
    {
        using var brain = NewBrain("Gemini", GeminiKey, "");
        server.Respond = respond;
        var (reply, shown, _) = await Speak(brain);
        server.Take();
        double paused = PauseSeconds(brain);
        Check(reply == "" && shown.StartsWith(status, StringComparison.Ordinal) && !shown.Contains(GeminiKey) && !shown.Contains("API key not valid")
            && (pauseSeconds == 0 ? brain.CloudPausedUntil == null : Math.Abs(paused - pauseSeconds) < 10), $"Gemini {name}: {shown}");
    }

    // ------------------------------------------------------------------------------------------------ limit and usage
    static async Task LimitAndUsage(FakeCloud server)
    {
        using (var brain = NewBrain("Claude", ClaudeKey, "claude-haiku-5-5"))
        {
            brain.CloudDailyLimit = 2;
            server.Respond = _ => Sse(ClaudeStream("claude-haiku-5-5", [Reply], "end_turn", thinking: false));
            var first = await Speak(brain);
            var second = await Speak(brain);
            int screen = ScreenReads();
            var third = await Speak(brain);
            Check(first.Reply == Reply && second.Reply == Reply && third.Reply == "" && server.Take().Count == 2 && ScreenReads() == screen
                && third.Status == "今天的雲端請求已達上限（2 次），魚魚先休息，明天會自動恢復；可以在設定 › 模型與資訊調整每天的上限。", "the daily limit stops the third request before it is sent");
            Check(brain.CloudUsage.Today.Requests == 2 && brain.CloudUsage.ThisMonth.Requests == 2, "today and this month count the two requests");
            brain.UseCloudModel("Claude", ClaudeKey, "claude-haiku-5-5");
            Check(brain.Status == third.Status, "applying the settings again keeps saying the limit is reached");
            brain.CloudDailyLimit = 0;
            Check((await Speak(brain)).Reply == Reply && server.Take().Count == 1, "0 means no limit");
            var file = File.ReadAllText(brain.CloudUsage.Path);
            using var json = JsonDocument.Parse(file);
            var today = json.RootElement.GetProperty("days").GetProperty(DateTime.Now.ToString("yyyy-MM-dd"));
            Check(today.GetProperty("requests").GetInt32() == 3 && today.GetProperty("inputTokens").GetInt64() == 3 * 1234 && today.GetProperty("outputTokens").GetInt64() == 3 * 42,
                "cloud-usage.json keeps requests and tokens per day");
            Check(!file.Contains(ClaudeKey) && !file.Contains("sk-") && !file.Contains(Reply) && !file.Contains("主人"), "the usage file holds no key and no text");
            brain.CloudDailyLimit = 50;
            Check(brain.CloudUsage.Describe(50) == "今天 3 次（上限 50 次），輸入 3,702、輸出 126 個 token；本月 3 次，輸入 3,702、輸出 126 個 token。", "the usage line for the settings");
        }

        var clock = new DateTime(2026, 11, 30, 23, 0, 0);
        var path = Path.Combine(work, "usage-clock.json");
        var usage = new CloudUsage(path, () => clock);
        usage.Count(); usage.Count(); usage.AddTokens(1000, 100);
        Check(usage.Today == new CloudUsage.Totals(2, 1000, 100) && usage.Reached(2) && !usage.Reached(3) && !usage.Reached(0), "today's requests reach a limit; 0 never does");
        clock = clock.AddHours(2);
        usage.Count();
        Check(usage.Today == new CloudUsage.Totals(1, 0, 0) && usage.ThisMonth == new CloudUsage.Totals(1, 0, 0), "a new local day and month start from zero");
        var reread = new CloudUsage(path, () => new DateTime(2026, 11, 15));
        Check(reread.ThisMonth == new CloudUsage.Totals(2, 1000, 100) && reread.Today == new CloudUsage.Totals(0, 0, 0), "the file is read back by day and month");
        clock = clock.AddDays(120);
        usage.Count();
        Check(!File.ReadAllText(path).Contains("2026-11-30"), "days older than about three months leave the file");
        File.WriteAllText(path, "not json");
        Check(new CloudUsage(path, () => clock).Today.Requests == 0, "a damaged usage file reads as empty");
    }

    // ------------------------------------------------------------------------------------------------ secrets
    static void Secrets(params FakeCloud[] servers)
    {
        var log = string.Join("\n", DiagnosticLog.Snapshot().Select(e => e.ToString()));
        Check(log.Contains("kind=cloud") && log.Contains("state=rate_limited") && log.Contains("state=invalid_key") && log.Contains("state=refused"),
            "the diagnostic log records the cloud failures by kind");
        Check(new[] { ClaudeKey, OpenAiKey, GeminiKey, "sk-ant", "sk-proj", "AIza", "credit balance", "invalid x-api-key", "API key not valid", "****", "leaked", "usage limits", "Missing scopes", "billing details" }.All(s => !log.Contains(s, StringComparison.OrdinalIgnoreCase)),
            "no key, key fragment or response body reaches the diagnostic log");
        Check(servers.All(s => s.Unexpected == 0), "every request went to a fake server's known paths");
    }
}

// A provider's API as a server on this computer: every request is kept; Respond decides the answer.
sealed class FakeCloud : IDisposable
{
    internal sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body)
    {
        internal string? Header(string name) => Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        internal JsonNode? Json => Body.Length > 0 ? JsonNode.Parse(Body) : null;
    }
    // Stall: the body's first event is sent, then nothing for this long.
    internal sealed record Reply(int Status, string Body, string Type = "application/json", (string Name, string Value)[]? Headers = null, TimeSpan? Stall = null);
    readonly HttpListener listener = new();
    readonly List<Request> requests = [];
    internal string Url { get; }
    internal Func<Request, Reply> Respond = _ => new(500, "{}");
    internal int Unexpected;

    internal FakeCloud()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Url = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(Url + "/");
        listener.Start();
        _ = Task.Run(Serve);
    }

    internal List<Request> Take() { lock (requests) { var taken = requests.ToList(); requests.Clear(); return taken; } }

    async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    async Task Handle(HttpListenerContext context)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
            var headers = context.Request.Headers.AllKeys.Where(k => k != null).ToDictionary(k => k!, k => context.Request.Headers[k]!);
            var request = new Request(context.Request.HttpMethod, context.Request.Url!.PathAndQuery, headers, body);
            var path = context.Request.Url.AbsolutePath;
            if (!(path is "/v1/models" or "/v1/messages" or "/v1/chat/completions" or "/v1beta/models" || path.StartsWith("/v1beta/models/", StringComparison.Ordinal)))
                Interlocked.Increment(ref Unexpected);
            lock (requests) requests.Add(request);
            var reply = Respond(request);
            var response = context.Response;
            response.StatusCode = reply.Status;
            response.ContentType = reply.Type + "; charset=utf-8";
            foreach (var (name, value) in reply.Headers ?? []) response.Headers[name] = value;
            response.SendChunked = true;
            var bytes = Encoding.UTF8.GetBytes(reply.Body);
            // The first event ends at a blank line: "\n\n" (Claude, OpenAI) or "\r\n\r\n" (Gemini), whichever comes first.
            int lf = reply.Body.IndexOf("\n\n", StringComparison.Ordinal), crlf = reply.Body.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int end = crlf > 0 && (lf < 0 || crlf < lf) ? crlf + 4 : lf > 0 ? lf + 2 : -1;
            if (reply.Stall is { } stall && end > 0)
            {
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(reply.Body[..end]));
                await response.OutputStream.FlushAsync();
                await Task.Delay(stall);
                bytes = Encoding.UTF8.GetBytes(reply.Body[end..]);
            }
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch (Exception) { try { context.Response.Abort(); } catch { } }
    }

    public void Dispose() { listener.Stop(); listener.Close(); }
}
