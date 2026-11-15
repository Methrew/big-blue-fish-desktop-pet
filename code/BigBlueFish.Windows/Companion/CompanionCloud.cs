// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using AnthropicBeta = Anthropic.Models.Beta.AnthropicBeta;
using AnthropicModels = Anthropic.Models.Models;
using B = Anthropic.Models.Beta.Messages;
using M = Anthropic.Models.Messages;

namespace BigBlueFish.Windows;

// The cloud model source (ModelSource "Cloud"): the fish's own speech written by Claude (the Anthropic API, through the
// official Anthropic C# SDK), ChatGPT (OpenAI Chat Completions) or Gemini (Gemini API generateContent), always at the
// provider's official endpoint, which only test code can redirect (to a server on this computer). The API key goes only
// there, in the provider's own header; it never reaches the diagnostic log, an exception message, the usage file or MCP,
// and neither do raw response bodies. Requests carry no sampling parameters and none of llama.cpp's fields.

internal enum CloudFailure
{
    NoKey, InvalidKey, Forbidden, MissingScope, Billing, RateLimited, Overloaded, ModelNotFound, UnsupportedEffort, BadRequest,
    Refused, Truncated, Interrupted, Network, Timeout, Server, DailyLimit
}

// A failure already put into words for the model status line (Taiwan Traditional Chinese, never the key or a response body).
internal sealed class CloudException(CloudFailure kind, string message, TimeSpan? retryAfter = null) : IOException(message)
{
    internal CloudFailure Kind { get; } = kind;
    // What the provider asked for (Retry-After, Gemini's RetryInfo), or the pause a failure itself calls for.
    internal TimeSpan? RetryAfter { get; } = retryAfter;
    // The diagnostic log's word for the kind.
    internal string Token => Kind switch
    {
        CloudFailure.NoKey => "no_key", CloudFailure.InvalidKey => "invalid_key", CloudFailure.Forbidden => "forbidden",
        CloudFailure.MissingScope => "missing_scope", CloudFailure.Billing => "billing", CloudFailure.RateLimited => "rate_limited", CloudFailure.Overloaded => "overloaded",
        CloudFailure.ModelNotFound => "model_not_found", CloudFailure.UnsupportedEffort => "unsupported",
        CloudFailure.BadRequest => "bad_request", CloudFailure.Refused => "refused", CloudFailure.Truncated => "truncated",
        CloudFailure.Interrupted => "interrupted", CloudFailure.Network => "network", CloudFailure.Timeout => "timeout",
        CloudFailure.DailyLimit => "daily_limit", _ => "server"
    };
}

internal sealed record CloudProviderInfo(string Id, string Name, string DisplayName, string Endpoint, string KeyPage,
    string KeychainAccount, string[] PreferredModels);
internal sealed record CloudModelInfo(string Id, string DisplayName);
// One line of speech: the persona as the system prompt, this turn's material as the one user message, an optional image.
internal sealed record CloudRequest(string Model, string System, string Text, string? ImageDataUrl);
// Problem: the reply ended without a line to say (declined, cut off, out of output tokens); its usage still counts.
internal sealed record CloudReply(string Text, long InputTokens, long OutputTokens, string? Model, CloudException? Problem = null);

internal static class CompanionCloud
{
    internal const string Claude = "Claude", OpenAI = "OpenAI", Gemini = "Gemini";
    // The model status after a successful test or line starts with this.
    internal const string ReadyPrefix = "雲端 AI 服務就緒：";
    // Every provider counts thinking (reasoning) tokens in this limit, so it stays far above the short line asked for.
    internal const int MaxOutputTokens = 4096;
    // A stream that sends nothing for this long is given up (the SDK's timeout covers only the response headers).
    internal static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    internal static readonly CloudProviderInfo[] Providers =
    [
        new(Claude, "Claude", "Claude（Anthropic）", "https://api.anthropic.com", "https://platform.claude.com/settings/keys",
            "ClaudeApiKey", ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"]),
        new(OpenAI, "OpenAI", "ChatGPT（OpenAI）", "https://api.openai.com/v1/", "https://platform.openai.com/api-keys",
            "OpenAiApiKey", ["gpt-6-luna", "gpt-5.6-luna", "gpt-6.1-terra"]),
        new(Gemini, "Gemini", "Gemini（Google）", "https://generativelanguage.googleapis.com/v1beta/", "https://aistudio.google.com/apikey",
            "GeminiApiKey", ["gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-2.5-flash-lite"]),
    ];

    internal static bool IsProvider(string? provider) => Providers.Any(p => p.Id == provider);
    internal static CloudProviderInfo Info(string? provider) => Providers.FirstOrDefault(p => p.Id == provider) ?? Providers[0];
    internal static string DefaultModel(string? provider) => Info(provider).PreferredModels[0];
    // The company that bills the service and whose terms apply: Anthropic, OpenAI, Google.
    internal static string Company(string? provider) => Info(provider).Id switch { OpenAI => "OpenAI", Gemini => "Google", _ => "Anthropic" };

    // A pasted key names its service: sk-ant- is Anthropic's; AIza (API keys) and AQ. (the auth keys AI Studio creates
    // since 2026-05-28) are Google's; any other sk- (sk-proj-, sk-svcacct-, ...) is OpenAI's. Null: no known prefix.
    internal static string? ProviderForKey(string? key)
    {
        key = key?.Trim() ?? "";
        if (key.StartsWith("sk-ant-", StringComparison.Ordinal)) return Claude;
        if (key.StartsWith("AIza", StringComparison.Ordinal) || key.StartsWith("AQ.", StringComparison.Ordinal)) return Gemini;
        if (key.StartsWith("sk-", StringComparison.Ordinal)) return OpenAI;
        return null;
    }

    // The model to select after a test: the one already chosen when the key can use it, else the provider's recommended
    // one (Claude Opus 5.5; the low-cost GPT-6 Luna and Gemini 3.5 Flash-Lite), else the first suitable listed model.
    internal static string Preselect(string? provider, IReadOnlyList<CloudModelInfo> models, string? current = null)
    {
        current = current?.Trim() ?? "";
        if (current.Length > 0 && models.Any(m => m.Id == current)) return current;
        var info = Info(provider);
        return info.PreferredModels.FirstOrDefault(p => models.Any(m => m.Id == p))
            ?? (info.Id == Gemini ? models.FirstOrDefault(m => m.Id.Contains("flash-lite", StringComparison.Ordinal))?.Id
                ?? models.FirstOrDefault(m => m.Id.Contains("flash", StringComparison.Ordinal))?.Id : null)
            ?? models.FirstOrDefault()?.Id ?? info.PreferredModels[0];
    }

    // Rough cost of one line with a window screenshot (about 2,000 input and a few hundred output tokens, thinking
    // included), from the providers' November 2026 prices; null for a model without an estimate.
    internal static string? CostHint(string? provider, string model) => (Info(provider).Id, model) switch
    {
        (Claude, "claude-opus-5-5") => "約 US$0.02",
        (Claude, "claude-sonnet-5-5") => "約 US$0.01",
        (Claude, "claude-haiku-5-5") => "約 US$0.0005",
        (OpenAI, "gpt-6-luna") => "約 US$0.0004",
        (OpenAI, "gpt-5.6-luna") => "約 US$0.0008",
        (OpenAI, "gpt-6.1-terra") => "約 US$0.0035",
        (Gemini, "gemini-3.5-flash-lite") => "約 US$0.001",
        _ => null
    };

    // Test code only: a provider's endpoint replaced by a server on this computer. Nothing else sets it (no environment
    // variable, no preference); while one is set, that provider's requests may go nowhere else.
    static readonly Dictionary<string, Uri> testEndpoints = new(StringComparer.Ordinal);
    internal static void RedirectForTests(string provider, string baseUrl)
    {
        var target = new Uri(baseUrl, UriKind.Absolute);
        if (!target.IsLoopback || !IsProvider(provider)) throw new ArgumentException("Only a loopback server may stand in for a provider.", nameof(baseUrl));
        lock (testEndpoints) testEndpoints[provider] = target;
    }
    internal static Uri Endpoint(string? provider)
    {
        var info = Info(provider);
        lock (testEndpoints) if (testEndpoints.TryGetValue(info.Id, out var target)) return target;
        return new Uri(info.Endpoint, UriKind.Absolute);
    }

    internal static CloudService Create(string? provider, string key) => Info(provider).Id switch
    {
        OpenAI => new OpenAiService(key),
        Gemini => new GeminiService(key),
        _ => new ClaudeService(key),
    };

    // HttpRequestException.Message is the system's English text; the status line names the kind of failure instead.
    internal static CloudException NetworkFailure(string name, HttpRequestException error) => error.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => new(CloudFailure.Network, $"找不到 {name} 的伺服器，請確認網路連線（或 DNS 設定）後再試。"),
        HttpRequestError.SecureConnectionError => new(CloudFailure.Network, $"無法和 {name} 建立 https 安全連線，請確認網路沒有攔截加密連線。"),
        HttpRequestError.ConnectionError => new(CloudFailure.Network, $"連不上 {name}，請確認網路連線後再試。"),
        _ => new(CloudFailure.Network, $"無法連線 {name}，請確認網路連線後再試。")
    };

    internal static (string Mime, string Data)? ParseDataUrl(string? url)
    {
        if (url == null || !url.StartsWith("data:", StringComparison.Ordinal)) return null;
        int at = url.IndexOf(";base64,", StringComparison.Ordinal);
        return at <= 5 ? null : (url[5..at], url[(at + 8)..]);
    }

    internal static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter is not { } after ? null
        : after.Delta ?? (after.Date is { } date ? date - DateTimeOffset.UtcNow : null);
}

// Lets a request through only to the provider's endpoint (the test server while a test redirects it). The SDK and
// HttpClient never leave it on their own; this keeps a stray base URL from ever carrying the key elsewhere.
internal sealed class CloudGuard(string provider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var allowed = CompanionCloud.Endpoint(provider);
        if (request.RequestUri is not { } target
            || Uri.Compare(target, allowed, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
            throw new HttpRequestException("The request was not addressed to the provider's endpoint.");
        return base.SendAsync(request, token);
    }
}

internal abstract class CloudService(string provider) : IDisposable
{
    internal string Provider { get; } = provider;
    internal string Name => CompanionCloud.Info(Provider).Name;
    // Lists the models this key may use: proves the key without spending tokens.
    internal abstract Task<List<CloudModelInfo>> ListModels(CancellationToken token);
    internal abstract Task<CloudReply> Complete(CloudRequest request, Action<string>? onPartial, CancellationToken token);
    public virtual void Dispose() { }

    protected CloudException Refused() => new(CloudFailure.Refused, $"{Name} 婉拒了這次的請求，這次魚魚先不說話。");
    protected CloudException Truncated() => new(CloudFailure.Truncated, $"{Name} 還沒說出內容就用完了輸出上限，這次魚魚先不說話。");
    protected CloudException Interrupted() => new(CloudFailure.Interrupted, $"{Name} 的回覆中途中斷了，這次魚魚先不說話。");
    protected CloudException ModelNotFound(string model) => new(CloudFailure.ModelNotFound,
        model.Length == 0 ? $"這把 {Name} API Key 找不到可用的模型，請確認 Key 的權限。" : $"這把 {Name} API Key 不能使用模型 {model}，請按「儲存並測試」重新選擇模型。");
    protected CloudException Overloaded(TimeSpan? retryAfter) => new(CloudFailure.Overloaded, $"{Name} 目前忙碌中。", retryAfter);
    protected CloudException RateLimited(TimeSpan? retryAfter) => new(CloudFailure.RateLimited, $"{Name} 的請求太頻繁了。", retryAfter);
    protected CloudException Server(int status) => new(CloudFailure.Server,
        status > 0 ? $"{Name} 服務暫時出錯（HTTP {status}），稍後再試。" : $"{Name} 服務暫時出錯，稍後再試。");
    protected CloudException BadRequest(int status) => new(CloudFailure.BadRequest, $"{Name} 無法處理這次的請求（HTTP {status}）。");

    // Failures below the provider's API in words; the caller's own cancellation is not a failure and passes through.
    protected CloudException Transport(Exception error) => error switch
    {
        CloudException cloud => cloud,
        HttpRequestException http => CompanionCloud.NetworkFailure(Name, http),
        OperationCanceledException => new(CloudFailure.Timeout, $"{Name} 回應逾時，稍後再試。"),
        JsonException => new(CloudFailure.Server, $"{Name} 的回覆格式無法辨識，稍後再試。"),
        IOException => new(CloudFailure.Network, $"和 {Name} 的連線中斷了，稍後再試。"),
        _ => new(CloudFailure.Server, $"無法使用 {Name}，稍後再試。")
    };

    // HttpClient for the REST providers: no redirects (the key stays with the endpoint), headers within 60 seconds.
    protected HttpClient NewHttpClient() => new(new CloudGuard(Provider) { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    protected static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    protected static long Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}

// Claude through the official Anthropic SDK (NuGet Anthropic), Messages API streaming. The key goes as the SDK sends it
// (Authorization: Bearer); retries are the brain's pauses, not the SDK's.
internal sealed class ClaudeService : CloudService
{
    // Models that take the server-side fallbacks beta: a request one of them declines runs again on Anthropic's
    // recommended fallback model in the same call instead of ending in a refusal.
    internal static readonly HashSet<string> FallbackModels = new(StringComparer.Ordinal)
        { "claude-fable-5-2", "claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5" };
    readonly AnthropicClient client;

    internal ClaudeService(string key) : base(CompanionCloud.Claude)
    {
        client = new AnthropicClient
        {
            ApiKey = key,
            // Explicit, or ANTHROPIC_BASE_URL or a profile's base_url would decide where the key goes.
            BaseUrl = CompanionCloud.Endpoint(Provider).GetLeftPart(UriPartial.Authority),
            MaxRetries = 0,
            Timeout = TimeSpan.FromSeconds(60),
            OpenTelemetry = false,
            // New instances for every client; the last one is next to the network.
            Handlers = [new ClaudeHeaders(), new CloudGuard(Provider)],
        };
    }

    // Effort came with Claude Opus 4.5; the older models still served answer it with a 400, which is also retried without it.
    internal static bool SupportsEffort(string model) => !(model.StartsWith("claude-haiku-4-5", StringComparison.Ordinal)
        || model.StartsWith("claude-sonnet-4-5", StringComparison.Ordinal) || model.StartsWith("claude-3", StringComparison.Ordinal));

    internal override async Task<List<CloudModelInfo>> ListModels(CancellationToken token)
    {
        var models = new List<CloudModelInfo>();
        ClaudeHeaders.Begin();
        try
        {
            var page = await client.Models.List(new AnthropicModels.ModelListParams { Limit = 100 }, token);
            while (true)
            {
                foreach (var model in page.Items)
                    if (model.ID.StartsWith("claude-", StringComparison.Ordinal) && models.All(m => m.Id != model.ID))
                        models.Add(new(model.ID, string.IsNullOrWhiteSpace(model.DisplayName) ? model.ID : model.DisplayName));
                if (!page.HasNext()) break;
                page = await page.Next(token);
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Map(error, ""); }
        return models;
    }

    internal override async Task<CloudReply> Complete(CloudRequest request, Action<string>? onPartial, CancellationToken token)
    {
        bool effort = SupportsEffort(request.Model);
        while (true)
        {
            try { return await Stream(request, effort, onPartial, token); }
            catch (CloudException error) when (error.Kind == CloudFailure.UnsupportedEffort && effort) { effort = false; }
        }
    }

    async Task<CloudReply> Stream(CloudRequest request, bool effort, Action<string>? onPartial, CancellationToken token)
    {
        var text = new StringBuilder();
        string? stop = null, model = null;
        long input = 0, output = 0;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(CompanionCloud.IdleTimeout);
        // In this method, so the Retry-After the handler sees below is read in the catch.
        ClaudeHeaders.Begin();
        try
        {
            if (FallbackModels.Contains(request.Model))
            {
                await foreach (B.BetaRawMessageStreamEvent item in client.Beta.Messages.CreateStreaming(Beta(request, effort), idle.Token))
                {
                    idle.CancelAfter(CompanionCloud.IdleTimeout);
                    if (item.TryPickStart(out B.BetaRawMessageStartEvent? start))
                    {
                        model = start.Message.Model;
                        input = start.Message.Usage.InputTokens;
                        output = start.Message.Usage.OutputTokens;
                    }
                    else if (item.TryPickContentBlockDelta(out B.BetaRawContentBlockDeltaEvent? delta) && delta.Delta.TryPickText(out B.BetaTextDelta? piece))
                    {
                        text.Append(piece.Text);
                        onPartial?.Invoke(text.ToString());
                    }
                    else if (item.TryPickDelta(out B.BetaRawMessageDeltaEvent? end))
                    {
                        stop = end.Delta.StopReason?.Raw();
                        output = end.Usage.OutputTokens;
                        if (end.Usage.InputTokens is long all) input = all;
                        if (Billed(end.Usage.Iterations) is { } billed) (input, output) = billed;
                    }
                }
            }
            else
            {
                await foreach (M.RawMessageStreamEvent item in client.Messages.CreateStreaming(Plain(request, effort), idle.Token))
                {
                    idle.CancelAfter(CompanionCloud.IdleTimeout);
                    if (item.TryPickStart(out M.RawMessageStartEvent? start))
                    {
                        model = start.Message.Model;
                        input = start.Message.Usage.InputTokens;
                        output = start.Message.Usage.OutputTokens;
                    }
                    else if (item.TryPickContentBlockDelta(out M.RawContentBlockDeltaEvent? delta) && delta.Delta.TryPickText(out M.TextDelta? piece))
                    {
                        text.Append(piece.Text);
                        onPartial?.Invoke(text.ToString());
                    }
                    else if (item.TryPickDelta(out M.RawMessageDeltaEvent? end))
                    {
                        stop = end.Delta.StopReason?.Raw();
                        output = end.Usage.OutputTokens;
                        if (end.Usage.InputTokens is long all) input = all;
                    }
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Map(error, request.Model); }
        // A stream cut before message_delta ends without an exception from the SDK; a refusal (also after a partial line,
        // which is then not said) is a normal HTTP 200.
        return new(text.ToString(), input, output, model, stop switch
        {
            null => Interrupted(),
            "refusal" => Refused(),
            "max_tokens" when text.ToString().Trim().Length == 0 => Truncated(),
            _ => null
        });
    }

    // After a fallback the top-level usage covers only the attempt that wrote the reply; usage.iterations lists every
    // attempt. Billed: the fallback that answered, and a declined attempt that had already written something (a decline
    // before any output is mostly not billed). Null without a fallback: the top-level usage stands.
    static (long Input, long Output)? Billed(IEnumerable<B.Iteration>? iterations)
    {
        if (iterations == null) return null;
        long input = 0, output = 0;
        bool served = false;
        foreach (var hop in iterations)
        {
            if (hop.TryPickBetaFallbackMessageIterationUsage(out var fallback))
            {
                served = true;
                input += fallback.InputTokens;
                output += fallback.OutputTokens;
            }
            else if (hop.TryPickBetaMessageIterationUsage(out var declined) && declined.OutputTokens > 0)
            {
                input += declined.InputTokens;
                output += declined.OutputTokens;
            }
        }
        return served ? (input, output) : null;
    }

    static M.MessageCreateParams Plain(CloudRequest request, bool effort)
    {
        var content = new List<M.ContentBlockParam>();
        if (CompanionCloud.ParseDataUrl(request.ImageDataUrl) is { } image)
            content.Add(new M.ImageBlockParam
            {
                Source = new M.Base64ImageSource
                {
                    Data = image.Data,
                    MediaType = image.Mime switch { "image/png" => M.MediaType.ImagePng, "image/gif" => M.MediaType.ImageGif, "image/webp" => M.MediaType.ImageWebP, _ => M.MediaType.ImageJpeg }
                }
            });
        content.Add(new M.TextBlockParam { Text = request.Text });
        // No temperature or top_p (current models reject them) and no thinking field (adaptive where it is on).
        return effort
            ? new M.MessageCreateParams { Model = request.Model, MaxTokens = CompanionCloud.MaxOutputTokens, System = request.System,
                OutputConfig = new M.OutputConfig { Effort = M.Effort.Low }, Messages = [new M.UserMessageParam { Content = content }] }
            : new M.MessageCreateParams { Model = request.Model, MaxTokens = CompanionCloud.MaxOutputTokens, System = request.System,
                Messages = [new M.UserMessageParam { Content = content }] };
    }

    static B.MessageCreateParams Beta(CloudRequest request, bool effort)
    {
        var content = new List<B.BetaContentBlockParam>();
        if (CompanionCloud.ParseDataUrl(request.ImageDataUrl) is { } image)
            content.Add(new B.BetaImageBlockParam
            {
                Source = new B.BetaBase64ImageSource
                {
                    Data = image.Data,
                    MediaType = image.Mime switch { "image/png" => B.MediaType.ImagePng, "image/gif" => B.MediaType.ImageGif, "image/webp" => B.MediaType.ImageWebP, _ => B.MediaType.ImageJpeg }
                }
            });
        content.Add(new B.BetaTextBlockParam { Text = request.Text });
        // fallbacks "default": Anthropic picks the fallback model by the kind of refusal.
        return effort
            ? new B.MessageCreateParams { Model = request.Model, MaxTokens = CompanionCloud.MaxOutputTokens, System = request.System,
                OutputConfig = new B.BetaOutputConfig { Effort = B.Effort.Low }, Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new B.Default(), Messages = [new B.BetaUserMessageParam { Content = content }] }
            : new B.MessageCreateParams { Model = request.Model, MaxTokens = CompanionCloud.MaxOutputTokens, System = request.System,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new B.Default(), Messages = [new B.BetaUserMessageParam { Content = content }] };
    }

    // The SDK's exceptions carry the raw response in Message; only the status, the error type and the API's own message
    // (matched, never shown) are used here.
    CloudException Map(Exception error, string model)
    {
        var retry = ClaudeHeaders.RetryAfter;
        string message = error switch { AnthropicApiException api => api.ApiMessage ?? "", AnthropicSseException sse => sse.ApiMessage ?? "", _ => "" };
        bool Says(string words) => message.Contains(words, StringComparison.OrdinalIgnoreCase);
        int status = error is AnthropicApiException response ? (int)response.StatusCode : 0;
        string type = error switch { AnthropicApiException api => api.Type?.Raw() ?? "", AnthropicSseException sse => sse.Type?.Raw() ?? "", _ => "" };
        if (status == 401 || type == "authentication_error")
            return new(CloudFailure.InvalidKey, "Anthropic 不接受這把 API Key，請確認金鑰正確、沒有被停用，再按「儲存並測試」。");
        if (status == 402 || type == "billing_error" || Says("credit balance"))
            return new(CloudFailure.Billing, "Anthropic 帳戶的額度不足，請到 platform.claude.com 儲值，再按「儲存並測試」。");
        // The spend limit the user set in the Console (organization or workspace) answers with a 400, not a billing error:
        // "You have reached your specified (workspace) API usage limits. You will regain access on ...".
        if (status == 400 && Says("API usage limits"))
            return new(CloudFailure.Billing, "已達到你在 platform.claude.com 設定的 API 花費上限，請到 Claude Console 調高或移除上限，再按「儲存並測試」。");
        if (status == 403 || type == "permission_error")
            return new(CloudFailure.Forbidden, "這把 Claude API Key 沒有使用這個模型的權限（可能是組織或地區限制），請到 platform.claude.com 確認。");
        if (status == 404 || type == "not_found_error") return ModelNotFound(model);
        if (status == 429 || type == "rate_limit_error") return RateLimited(retry);
        if (status == 400 && Says("effort")) return new(CloudFailure.UnsupportedEffort, "這個 Claude 模型不支援思考強度設定。");
        if (status is 400 or 413) return BadRequest(status);
        if (status is 503 or 529 || type == "overloaded_error") return Overloaded(retry);
        if (status >= 500 || error is AnthropicSseException) return Server(status);
        if (status > 0) return BadRequest(status);
        if (error is AnthropicIOException { InnerException: HttpRequestException http }) return CompanionCloud.NetworkFailure(Name, http);
        if (error is AnthropicIOException) return new(CloudFailure.Network, "無法連線 Claude，請確認網路連線後再試。");
        if (error is AnthropicException) return new(CloudFailure.Server, "Claude 的回覆格式無法辨識，稍後再試。");
        return Transport(error);
    }

    public override void Dispose() => (client as IDisposable)?.Dispose();
}

// Around every Claude request: drops headers the SDK did not set itself (ANTHROPIC_CUSTOM_HEADERS adds them to every
// request otherwise), and keeps the Retry-After of a 429, 503 or 529, which the SDK's exceptions do not carry.
internal sealed class ClaudeHeaders : DelegatingHandler
{
    sealed class Box { internal TimeSpan? RetryAfter; }
    static readonly AsyncLocal<Box?> current = new();
    // Called in the method that catches the request's failure, before the request.
    internal static void Begin() => current.Value = new();
    internal static TimeSpan? RetryAfter => current.Value?.RetryAfter;

    static bool Known(string name) => name.StartsWith("x-stainless-", StringComparison.OrdinalIgnoreCase)
        || new[] { "Authorization", "anthropic-version", "anthropic-beta", "User-Agent", "Accept", "Accept-Encoding" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        foreach (var name in request.Headers.Select(header => header.Key).Where(name => !Known(name)).ToList()) request.Headers.Remove(name);
        var response = await base.SendAsync(request, token);
        if ((int)response.StatusCode is 429 or 503 or 529 && current.Value is { } box) box.RetryAfter = CompanionCloud.RetryAfter(response);
        return response;
    }
}

// ChatGPT through OpenAI's Chat Completions (current models support it for text and image input without tools):
// a developer message with the persona, one user message, max_completion_tokens (reasoning included), low reasoning
// effort, store false; no temperature or top_p.
internal sealed class OpenAiService : CloudService
{
    static readonly string[] NotChat = ["embedding", "moderation", "image", "dall-e", "audio", "realtime", "transcribe", "tts", "whisper", "live",
        "search", "codex", "deep-research", "-pro", "cyber", "daybreak", "oss", "computer-use", "instruct", "babbage", "davinci", "sora", "rosalind"];
    readonly HttpClient http;
    readonly string key;

    internal OpenAiService(string key) : base(CompanionCloud.OpenAI)
    {
        this.key = key;
        http = NewHttpClient();
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(CompanionCloud.Endpoint(Provider), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    // GET /models reports no capabilities, so the chat models are told apart by their names.
    internal static bool IsChatModel(string id)
    {
        id = id.ToLowerInvariant();
        return (id.StartsWith("gpt-", StringComparison.Ordinal) || id.StartsWith("o1", StringComparison.Ordinal) || id.StartsWith("o3", StringComparison.Ordinal)
            || id.StartsWith("o4", StringComparison.Ordinal) || id == "chat-latest") && !NotChat.Any(word => id.Contains(word, StringComparison.Ordinal));
    }

    internal override async Task<List<CloudModelInfo>> ListModels(CancellationToken token)
    {
        try
        {
            using var request = Request(HttpMethod.Get, "models");
            using var response = await http.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) throw Failure(response, body, "");
            using var document = JsonDocument.Parse(body);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var preferred = CompanionCloud.Info(Provider).PreferredModels;
            var data = document.RootElement.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToArray() : [];
            return data
                .Where(m => !(DateOnly.TryParse(Text(m, "shutdown_date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var shutdown) && shutdown <= today))
                .Select(m => (Id: Text(m, "id"), Created: Number(m, "created")))
                .Where(m => IsChatModel(m.Id))
                .OrderBy(m => Array.IndexOf(preferred, m.Id) is var at && at >= 0 ? at : int.MaxValue).ThenByDescending(m => m.Created)
                .Select(m => m.Id).Distinct().Select(id => new CloudModelInfo(id, id)).ToList();
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Transport(error); }
    }

    internal override async Task<CloudReply> Complete(CloudRequest request, Action<string>? onPartial, CancellationToken token)
    {
        try { return await Stream(request, true, onPartial, token); }
        // A model without reasoning answers reasoning_effort with a 400 naming it; once more without it.
        catch (CloudException error) when (error.Kind == CloudFailure.UnsupportedEffort) { return await Stream(request, false, onPartial, token); }
    }

    internal static JsonObject Body(CloudRequest request, bool effort)
    {
        JsonNode content = CompanionCloud.ParseDataUrl(request.ImageDataUrl) == null ? JsonValue.Create(request.Text) : new JsonArray(
            new JsonObject { ["type"] = "text", ["text"] = request.Text },
            // detail low: the window's text goes along as text, and the image costs about 200 tokens instead of 1,800.
            new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = request.ImageDataUrl, ["detail"] = "low" } });
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "developer", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = content }),
            ["max_completion_tokens"] = CompanionCloud.MaxOutputTokens,
            ["store"] = false,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true }
        };
        // low is accepted by every current reasoning model (minimal and none are not).
        if (effort) body["reasoning_effort"] = "low";
        return body;
    }

    async Task<CloudReply> Stream(CloudRequest request, bool effort, Action<string>? onPartial, CancellationToken token)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(CompanionCloud.IdleTimeout);
        var text = new StringBuilder();
        bool refused = false;
        string? finish = null, model = null;
        long input = 0, output = 0;
        try
        {
            using var message = Request(HttpMethod.Post, "chat/completions");
            message.Content = new StringContent(Body(request, effort).ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!response.IsSuccessStatusCode) throw Failure(response, await response.Content.ReadAsStringAsync(idle.Token), request.Model);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(idle.Token), Encoding.UTF8);
            while (await reader.ReadLineAsync(idle.Token) is { } line)
            {
                idle.CancelAfter(CompanionCloud.IdleTimeout);
                // Blank lines and ": comment" keep-alives carry nothing.
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                using var chunk = JsonDocument.Parse(data);
                var root = chunk.RootElement;
                // An error after the stream began arrives as an event, not as an HTTP status.
                if (root.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.Object)
                    throw Map(0, Text(failure, "type"), Text(failure, "code"), Text(failure, "param"), Text(failure, "message"), null, request.Model);
                if (Text(root, "model") is { Length: > 0 } named) model = named;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    input = Number(usage, "prompt_tokens");
                    output = Number(usage, "completion_tokens");
                }
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                {
                    if (Text(delta, "content") is { Length: > 0 } piece) { text.Append(piece); onPartial?.Invoke(text.ToString()); }
                    if (Text(delta, "refusal").Length > 0) refused = true;
                }
                if (Text(choice, "finish_reason") is { Length: > 0 } reason) finish = reason;
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Transport(error); }
        return new(text.ToString(), input, output, model,
            refused || finish == "content_filter" ? Refused()
            : finish == null ? Interrupted()
            : finish == "length" && text.ToString().Trim().Length == 0 ? Truncated() : null);
    }

    CloudException Failure(HttpResponseMessage response, string body, string model)
    {
        string type = "", code = "", param = "", message = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                (type, code, param, message) = (Text(error, "type"), Text(error, "code"), Text(error, "param"), Text(error, "message"));
        }
        catch (JsonException) { }
        return Map((int)response.StatusCode, type, code, param, message, CompanionCloud.RetryAfter(response), model);
    }

    // The message is only matched, never shown or logged (a 401's message repeats part of the key).
    CloudException Map(int status, string type, string code, string param, string message, TimeSpan? retryAfter, string model)
    {
        bool Mentions(string words) => param.Contains(words, StringComparison.OrdinalIgnoreCase) || message.Contains(words, StringComparison.OrdinalIgnoreCase);
        bool billing = type == "insufficient_quota" || code is "insufficient_quota" or "credit_balance_exhausted" or "usage_limit_exceeded"
            || code.EndsWith("_spend_limit_exceeded", StringComparison.Ordinal) || code.EndsWith("_usage_limit_exceeded", StringComparison.Ordinal);
        // A restricted key without a permission this call needs answers 401 (not 403): "You have insufficient permissions
        // for this operation. Missing scopes: ...". The key itself is valid.
        if (code == "missing_scope" || Mentions("Missing scopes") || Mentions("insufficient permissions"))
            return new(CloudFailure.MissingScope, "這把 OpenAI API Key 是限制權限的 Key，缺少魚魚需要的權限，請到 OpenAI 平台開啟模型的使用權限（或改用全部權限的 Key），再按「儲存並測試」。");
        // An account without an active payment method (429, type billing_not_active): no retry helps until the billing is fixed.
        if (type == "billing_not_active" || code == "billing_not_active")
            return new(CloudFailure.Billing, "OpenAI 帳戶目前沒有啟用，請到 OpenAI 平台確認付款方式與帳單，再按「儲存並測試」。");
        if (status == 401 || code == "invalid_api_key" || type == "authentication_error")
            return new(CloudFailure.InvalidKey, "OpenAI 不接受這把 API Key，請確認金鑰正確、沒有被撤銷，再按「儲存並測試」。");
        if (code == "model_not_found" || status == 404) return ModelNotFound(model);
        if (status == 403) return new(CloudFailure.Forbidden, "這把 OpenAI API Key 沒有使用這項服務的權限（專案權限或地區限制），請到 OpenAI 平台確認。");
        if (billing) return new(CloudFailure.Billing, "OpenAI 帳戶的額度或花費上限已用完，請到 OpenAI 平台儲值或調整上限，再按「儲存並測試」。");
        if (status == 429 || type == "rate_limit_error" || code is "rate_limit_exceeded" or "slow_down") return RateLimited(retryAfter);
        if (status == 400 && (Mentions("reasoning_effort") || Mentions("reasoning.effort")))
            return new(CloudFailure.UnsupportedEffort, "這個 OpenAI 模型不支援推論強度設定。");
        if (status == 503 || code is "server_is_overloaded" or "server_overloaded") return Overloaded(retryAfter);
        if (status >= 500 || status == 0 && type is "server_error" or "" || code == "server_error") return Server(status);
        return BadRequest(status == 0 ? 400 : status);
    }

    public override void Dispose() => http.Dispose();
}

// Gemini through generateContent streaming (Server-Sent Events): systemInstruction, the image before the text,
// maxOutputTokens (thinking included) and a low thinking setting for the model family; no sampling parameters.
internal sealed class GeminiService : CloudService
{
    static readonly string[] NotChat = ["embedding", "tts", "image", "live", "audio", "robotics", "computer-use", "transcribe", "translate", "omni",
        "banana", "deep-research", "antigravity", "decision", "aqa", "imagen", "veo", "lyria"];
    readonly HttpClient http;
    readonly string key;

    internal GeminiService(string key) : base(CompanionCloud.Gemini)
    {
        this.key = key;
        http = NewHttpClient();
    }

    // The key only in x-goog-api-key: never in the address, and no second credential beside it.
    HttpRequestMessage Request(HttpMethod method, string relative)
    {
        var request = new HttpRequestMessage(method, new Uri(CompanionCloud.Endpoint(Provider), relative));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        return request;
    }

    internal static bool IsChatModel(string name, IEnumerable<string> methods) => name.StartsWith("models/gemini-", StringComparison.Ordinal)
        && !NotChat.Any(word => name.Contains(word, StringComparison.Ordinal)) && methods.Contains("generateContent");

    internal override async Task<List<CloudModelInfo>> ListModels(CancellationToken token)
    {
        var models = new List<CloudModelInfo>();
        try
        {
            string? page = null;
            do
            {
                using var request = Request(HttpMethod.Get, "models?pageSize=1000" + (page == null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
                using var response = await http.SendAsync(request, token);
                var body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode) throw Failure(response, body, "");
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var model in list.EnumerateArray())
                    {
                        var name = Text(model, "name");
                        var methods = model.TryGetProperty("supportedGenerationMethods", out var supported) && supported.ValueKind == JsonValueKind.Array
                            ? supported.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString() ?? "") : [];
                        if (!IsChatModel(name, methods)) continue;
                        var id = name["models/".Length..];
                        if (models.Any(m => m.Id == id)) continue;
                        models.Add(new(id, Text(model, "displayName") is { Length: > 0 } display ? display : id));
                    }
                page = Text(root, "nextPageToken") is { Length: > 0 } next ? next : null;
            } while (page != null);
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Transport(error); }
        return models;
    }

    // As little thinking as each family allows: Gemini 2.5 takes a budget (Pro cannot turn it off), Gemini 3 Flash and
    // Flash-Lite (but 3.8) a minimal level, Gemini 4, 3.8 Flash, the Pro models and unknown names low.
    internal static JsonObject? Thinking(string model)
    {
        var name = model.ToLowerInvariant();
        if (name.StartsWith("gemini-2.5-pro", StringComparison.Ordinal)) return new() { ["thinkingBudget"] = 128 };
        if (name.StartsWith("gemini-2.5", StringComparison.Ordinal)) return new() { ["thinkingBudget"] = 0 };
        if (name.StartsWith("gemini-2", StringComparison.Ordinal) || name.StartsWith("gemini-1", StringComparison.Ordinal)) return null;
        bool minimal = name.StartsWith("gemini-3", StringComparison.Ordinal) && !name.StartsWith("gemini-3.8", StringComparison.Ordinal) && !name.Contains("-pro", StringComparison.Ordinal);
        return new() { ["thinkingLevel"] = minimal ? "minimal" : "low" };
    }

    internal static JsonObject Body(CloudRequest request, JsonObject? thinking)
    {
        var parts = new JsonArray();
        if (CompanionCloud.ParseDataUrl(request.ImageDataUrl) is { } image)
            parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = image.Mime, ["data"] = image.Data } });
        parts.Add(new JsonObject { ["text"] = request.Text });
        var config = new JsonObject { ["maxOutputTokens"] = CompanionCloud.MaxOutputTokens };
        if (thinking != null) config["thinkingConfig"] = thinking;
        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generationConfig"] = config
        };
    }

    internal override async Task<CloudReply> Complete(CloudRequest request, Action<string>? onPartial, CancellationToken token)
    {
        var thinking = Thinking(request.Model);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(CompanionCloud.IdleTimeout);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                using var message = Request(HttpMethod.Post, "models/" + Uri.EscapeDataString(request.Model) + ":streamGenerateContent?alt=sse");
                message.Content = new StringContent(Body(request, thinking).ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, idle.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(idle.Token);
                    // A model that does not take this thinking setting says so in a 400: once more without it.
                    if (attempt == 0 && thinking != null && response.StatusCode == HttpStatusCode.BadRequest && body.Contains("thinking", StringComparison.OrdinalIgnoreCase))
                    {
                        thinking = null;
                        continue;
                    }
                    throw Failure(response, body, request.Model);
                }
                return await Read(response, request.Model, onPartial, idle);
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception error) { throw Transport(error); }
    }

    // Each event is one "data:" line of a GenerateContentResponse; the stream ends when the connection closes.
    async Task<CloudReply> Read(HttpResponseMessage response, string requested, Action<string>? onPartial, CancellationTokenSource idle)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(idle.Token), Encoding.UTF8);
        var text = new StringBuilder();
        var data = new StringBuilder();
        string? finish = null, blocked = null, model = null;
        long input = 0, output = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(idle.Token);
            idle.CancelAfter(CompanionCloud.IdleTimeout);
            if (line == null || line.Length == 0)
            {
                if (data.Length > 0) Event(data.ToString());
                data.Clear();
                if (line == null) break;
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(line.AsSpan(5).TrimStart(' '));
        }
        void Event(string json)
        {
            using var chunk = JsonDocument.Parse(json);
            var root = chunk.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("error", out _)) throw Failure(response, json, requested);
            if (Text(root, "modelVersion") is { Length: > 0 } version) model = version;
            if (root.TryGetProperty("promptFeedback", out var feedback) && Text(feedback, "blockReason") is { Length: > 0 } reason) blocked = reason;
            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                input = Number(usage, "promptTokenCount");
                output = Number(usage, "candidatesTokenCount") + Number(usage, "thoughtsTokenCount");
            }
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return;
            var candidate = candidates[0];
            if (Text(candidate, "finishReason") is { Length: > 0 } ended) finish = ended;
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) return;
            bool grew = false;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (Text(part, "text") is { Length: > 0 } piece) { text.Append(piece); grew = true; }
            }
            if (grew) onPartial?.Invoke(text.ToString());
        }
        bool empty = text.ToString().Trim().Length == 0;
        CloudException? problem = blocked != null || finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "RECITATION" or "IMAGE_SAFETY" or "LANGUAGE"
            ? Refused()
            : finish == null ? Interrupted()
            // The account itself is limited or disabled under Google's Prohibited Use Policy: a pause, as for a key problem.
            : finish == "PUP_LIMITED_DISABLED" ? new(CloudFailure.Forbidden, "Google 依使用政策限制或停用了這個帳號使用 Gemini，請到 Google AI Studio 確認帳號狀態。")
            : !empty || finish == "STOP" ? null
            : finish == "MAX_TOKENS" ? Truncated()
            // OTHER, MALFORMED_RESPONSE, MISSING_THOUGHT_SIGNATURE, UNEXPECTED_TOOL_CALL, ...: nothing to say this time.
            : new(CloudFailure.Server, "Gemini 這次沒有產生內容，稍後再試。");
        return new(text.ToString(), input, output, model, problem);
    }

    CloudException Failure(HttpResponseMessage response, string body, string model)
    {
        int code = (int)response.StatusCode;
        string status = "", message = "", reason = "", quota = "";
        var retry = CompanionCloud.RetryAfter(response);
        try
        {
            using var document = JsonDocument.Parse(body);
            // Without alt=sse the endpoint answers errors as an array.
            var root = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0 ? document.RootElement[0] : document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var inner) && inner.ValueKind == JsonValueKind.Number && inner.TryGetInt32(out var number)) code = number;
                status = Text(error, "status");
                message = Text(error, "message");
                if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                    foreach (var detail in details.EnumerateArray())
                    {
                        var type = Text(detail, "@type");
                        if (type.EndsWith("google.rpc.ErrorInfo", StringComparison.Ordinal)) reason = Text(detail, "reason");
                        else if (type.EndsWith("google.rpc.RetryInfo", StringComparison.Ordinal) && Duration(Text(detail, "retryDelay")) is { } delay) retry = delay;
                        else if (type.EndsWith("google.rpc.QuotaFailure", StringComparison.Ordinal) && detail.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                            foreach (var violation in violations.EnumerateArray()) quota += Text(violation, "quotaId") + " ";
                    }
            }
        }
        catch (JsonException) { }
        // The message is only matched, never shown or logged.
        bool Says(string words) => message.Contains(words, StringComparison.OrdinalIgnoreCase) || reason.Contains(words, StringComparison.OrdinalIgnoreCase);
        return code switch
        {
            400 when reason == "API_KEY_INVALID" || Says("API key") => new(CloudFailure.InvalidKey, "Gemini 不接受這把 API Key（無效或已過期），請到 Google AI Studio 確認或重新建立。"),
            400 when status == "FAILED_PRECONDITION" => new(CloudFailure.Billing, "這個 Google 專案目前不能使用 Gemini 的免費額度，請在 Google AI Studio 為專案開啟付費。"),
            400 => BadRequest(400),
            401 => new(CloudFailure.InvalidKey, "Gemini 不接受這把 API Key，請確認後重新貼上，再按「儲存並測試」。"),
            402 => new(CloudFailure.Billing, "Gemini 的預付額度已用完，請到 Google AI Studio 儲值或開啟自動儲值。"),
            403 when Says("leaked") => new(CloudFailure.InvalidKey, "Google 判定這把 Gemini API Key 已外洩並停用了，請建立新的 Key。"),
            403 when Says("billing") || Says("balance") || Says("payment") => new(CloudFailure.Billing, "Google 帳單有未付的款項或付款問題，請到 Google AI Studio 的帳單頁面處理。"),
            403 when Says("verif") => new(CloudFailure.Billing, "這個 Google 專案要先完成驗證，請到 Google AI Studio 依指示處理。"),
            403 => new(CloudFailure.InvalidKey, "Gemini 拒絕這把 API Key，請確認 Key 仍有效，而且允許使用 Gemini API。"),
            404 => ModelNotFound(model),
            // RetryInfo can say a second for a quota that resets only at midnight Pacific time.
            429 when quota.Contains("PerDay", StringComparison.Ordinal) => new(CloudFailure.RateLimited, "Gemini 今天的請求額度用完了（每天台灣時間下午三點或四點重置）。", TimeSpan.FromMinutes(30)),
            429 => RateLimited(retry),
            503 => Overloaded(retry),
            504 or 408 or 499 => new(CloudFailure.Timeout, "Gemini 回應逾時，稍後再試。"),
            >= 500 => Server(code),
            _ => BadRequest(code)
        };
    }

    static TimeSpan? Duration(string value) => value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        ? TimeSpan.FromSeconds(seconds) : null;

    public override void Dispose() => http.Dispose();
}

// What a failure means for the next requests: a rate limit waits as long as the provider asks (5 minutes when it does
// not say, at most 30), an overloaded service 2 minutes; a key, permission, model or billing problem 30 minutes, or until
// another service, key or model is chosen or a test succeeds. While paused, a request ends at once with the same words, and nothing is sent.
internal sealed class CloudPause
{
    DateTime until;
    CloudException? reason;
    internal void Clear() { until = DateTime.MinValue; reason = null; }
    internal DateTime? Until => reason != null && DateTime.UtcNow < until ? until : null;
    internal CloudException? Active(DateTime utcNow) => reason != null && utcNow < until ? new CloudException(reason.Kind, reason.Message, reason.RetryAfter) : null;

    internal CloudException Apply(CloudException error, DateTime utcNow)
    {
        TimeSpan? pause = error.Kind switch
        {
            CloudFailure.RateLimited => Clamp(error.RetryAfter ?? TimeSpan.FromMinutes(5)),
            CloudFailure.Overloaded => Clamp(error.RetryAfter is { } asked && asked > TimeSpan.FromMinutes(2) ? asked : TimeSpan.FromMinutes(2)),
            CloudFailure.InvalidKey or CloudFailure.Forbidden or CloudFailure.MissingScope or CloudFailure.Billing or CloudFailure.ModelNotFound => TimeSpan.FromMinutes(30),
            _ => null
        };
        if (pause is not { } span) return error;
        until = utcNow + span;
        reason = error.Kind is CloudFailure.RateLimited or CloudFailure.Overloaded
            ? new CloudException(error.Kind, error.Message.TrimEnd('。') + "，魚魚先安靜到 "
                + until.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + "。", span)
            : error;
        return reason;
    }

    static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : value > TimeSpan.FromMinutes(30) ? TimeSpan.FromMinutes(30) : value;
}

// cloud-usage.json in the data folder: requests and tokens per local day (kept about three months), never a key, a
// prompt or a reply. The daily limit counts requests sent today; the settings show today and this month.
internal sealed class CloudUsage(string path, Func<DateTime>? clock = null)
{
    internal sealed record Totals(int Requests, long InputTokens, long OutputTokens);
    sealed class Day
    {
        public int Requests { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
    }
    sealed class Store
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Day> Days { get; set; } = new();
    }
    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    readonly Func<DateTime> now = clock ?? (() => DateTime.Now);
    readonly object gate = new();
    Store? store;
    internal string Path => path;

    Store Load()
    {
        if (store != null) return store;
        try { store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Options); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        store ??= new();
        store.Days ??= new();
        return store;
    }

    static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static Totals Of(IEnumerable<Day> days) => new(days.Sum(d => d.Requests), days.Sum(d => d.InputTokens), days.Sum(d => d.OutputTokens));

    internal Totals Today { get { lock (gate) return Of(Load().Days.Where(d => d.Key == Key(now())).Select(d => d.Value)); } }
    internal Totals ThisMonth
    {
        get
        {
            lock (gate)
            {
                var month = now().ToString("yyyy-MM-", CultureInfo.InvariantCulture);
                return Of(Load().Days.Where(d => d.Key.StartsWith(month, StringComparison.Ordinal)).Select(d => d.Value));
            }
        }
    }
    internal bool Reached(int limit) => limit > 0 && Today.Requests >= limit;
    internal void Count() => Update(day => day.Requests++);
    internal void AddTokens(long input, long output)
    {
        if (input <= 0 && output <= 0) return;
        Update(day => { day.InputTokens += Math.Max(0, input); day.OutputTokens += Math.Max(0, output); });
    }

    void Update(Action<Day> change)
    {
        lock (gate)
        {
            var current = Load();
            var today = now();
            if (!current.Days.TryGetValue(Key(today), out var day)) current.Days[Key(today)] = day = new();
            change(day);
            var oldest = Key(today.AddDays(-100));
            foreach (var old in current.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList()) current.Days.Remove(old);
            try
            {
                var full = System.IO.Path.GetFullPath(path);
                if (System.IO.Path.GetDirectoryName(full) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
                var temporary = full + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(current, Options));
                File.Move(temporary, full, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The count stays in memory for this run; the log gets the kind of failure only.
                DiagnosticLog.Write(DiagnosticEvent.RunnerFailed, kind: "cloud", state: "write", error: error);
            }
        }
    }

    // For the settings page: 今天 3 次（上限 200 次），輸入 4,567、輸出 890 個 token；本月 20 次，輸入 …
    internal string Describe(int limit)
    {
        var (today, month) = (Today, ThisMonth);
        return string.Format(CultureInfo.InvariantCulture, "今天 {0:N0} 次（{1}），輸入 {2:N0}、輸出 {3:N0} 個 token；本月 {4:N0} 次，輸入 {5:N0}、輸出 {6:N0} 個 token。",
            today.Requests, limit > 0 ? $"上限 {limit} 次" : "不限制", today.InputTokens, today.OutputTokens, month.Requests, month.InputTokens, month.OutputTokens);
    }
}
