// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#if !COMPANION_SAFE
using BigBlueFish.Control;
using BigBlueFish.Agent;
using BigBlueFish.Deployment;
#endif

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private bool externalBubble { get; set; }
    // When an agent last said something; it holds off the fish's own speech while it keeps saying more
    // (the Hermes bridge refreshes its progress bubble at least every 15 seconds).
    private DateTime externalBubbleAt { get; set; }
    private static readonly TimeSpan ExternalBubbleHold = TimeSpan.FromSeconds(20);
#if COMPANION_SAFE
    private bool AgentDailyActive => false;
    private void TickAgent(DateTime now) { }
    private bool DispatchAgentDaily() => false;
    private bool McpInputAvailable => false;
    private bool AgentAwaitingSpeech => false;
    private bool PuppetActive => false;
    private bool HasScheduledMcpSpeech => false;
    private void InitializeMcp() { preferences.AgentEnabled = false; preferences.McpEnabled = false; preferences.PuppetMode = false; }
    private void StopMcp() { }
    private bool PlayQueuedMcpAction() => false;
    private void PublishMcpInteraction(string kind, object detail) { DiagnosticLog.Write(DiagnosticEvent.Interaction, kind: kind); }
    private bool ConsumeMcpSpeech() => false;
    private bool TryOpenMcpInput() => false;
    private bool McpInputOpen => false;
#else
    // Two servers run the same tools. mcpServer is the built-in Agent's own, on a private pipe its ACP session is given
    // (BigBlueFish.Mcp --pipe); mcpInterface is the MCP interface for clients outside the fish, such as the Hermes
    // bridge, on the fixed pipe BigBlueFish.Mcp uses without arguments. Each answers only while its own switch is on.
    private ControlServer? mcpServer;
    private ControlServer? mcpInterface;
    private AgentCoordinator? agent;
    private readonly string agentPipe = ControlTools.PipeName + "." + Guid.NewGuid().ToString("N");
    private readonly Queue<string> mcpActions = new();
    private string? mcpNextSpeech;
    private bool mcpConsentOpen;
    private Window? mcpConsentWindow;
    private Window? mcpInput;
    // The input's file picker is open; closing or hiding its owner then would tear the dialog down.
    private bool mcpInputPicking;
    private bool McpChannelOpen(bool agentChannel) => agentChannel ? preferences.AgentEnabled : preferences.McpEnabled;
    private int McpConnections => (mcpServer?.Connections ?? 0) + (mcpInterface?.Connections ?? 0);
    // The double-click input: always with the Agent connection on (it shows the connection state and keeps the draft),
    // or for the MCP interface once the user allowed chat there and a real client has initialized.
    private bool McpChatOn => preferences.McpEnabled && preferences.McpChatEnabled;
    private bool McpChatAvailable => McpChatOn && (mcpInterface?.Connections ?? 0) > 0;
    private bool McpInputEnabled => preferences.AgentEnabled || McpChatOn;
    private bool McpInputAvailable => preferences.AgentEnabled || McpChatAvailable;
    private bool PuppetActive => preferences.AnyAgentControl && preferences.PuppetMode;
    private bool HasScheduledMcpSpeech => mcpNextSpeech != null;
    private bool McpInputOpen => mcpInput?.IsVisible == true;
    private bool AgentDailyActive => preferences.AgentEnabled && preferences.AgentDailySpeech;
    // The built-in Agent is preparing a turn the fish shows the loading ring for, and has not spoken in it yet.
    private bool AgentAwaitingSpeech => agent?.AwaitingSpeech == true;
    // PuppetMode is one switch for both sides; each confirmation says so when the other side is on too.
    private const string PuppetSharedWithAgent = "內建的 Agent 連線也開著：提線木偶模式和「連線 Agent」頁的接管是同一個開關，開啟後內建的 Agent 也會安排日常關心與動作。";
    private const string PuppetSharedWithMcp = "MCP 介面也開著：這個開關和「Agent 與 MCP」頁的提線木偶模式是同一個，開啟後 MCP 介面的外部 Agent 也能接管魚魚。";
    private const string PuppetAlreadyOn = "「Agent 與 MCP」頁的提線木偶模式目前已經開啟，它和這裡的接管是同一個開關，所以連線後 Agent 會立刻安排日常關心與動作。不想讓 Agent 接管的話，請按取消，先到「Agent 與 MCP」頁關閉提線木偶模式。";
    // allowImages: the connected Agent declared ACP image input; otherwise the screen is described as text.
    private async Task<AgentSpeechBrief> BuildAgentSpeechBrief(string kind, bool allowImages, CancellationToken token)
    {
        bool publicInfo = preferences.PublicInfo;
        var material = await brain.AgentSpeechBrief(kind == "weather_request", publicInfo, token);
        if (!material.ObserveScreen) return new(material.Text);
        if (!preferences.ForegroundEnabled) return new(material.Text + "\n本輪未啟用讀取前景內容，請按已有情境自然關心主人。");
        var handle = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        if (!allowImages)
        {
#if COMPANION_OCR
            DiagnosticLog.Write(DiagnosticEvent.ScreenReadStarted, kind: "ocr");
            try
            {
                var ocr = await CompanionOcr.ReadAsync(handle, token);
                token.ThrowIfCancellationRequested();
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadCompleted, kind: "ocr", count: ocr.WordCount,
                    elapsedMs: ocr.CaptureMilliseconds + ocr.RecognizeMilliseconds);
                return new(CompanionBrain.CompleteAgentScreenBrief(material, ocr.Context, true)
                    + "\n目前的 Agent 未啟用圖片輸入，已改用本機文字辨識提供前景內容。", null, "ocr");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: "ocr", error: error);
                return new(material.Text + "\n本輪未取得可用的螢幕內容，請按已有情境自然關心主人。");
            }
#else
            try
            {
                var context = await Task.Run(() => CompanionNative.ForegroundContext(handle), token);
                token.ThrowIfCancellationRequested();
                return new(CompanionBrain.CompleteAgentScreenBrief(material, context, false)
                    + "\n目前的 Agent 未啟用圖片輸入，已改用前景視窗的文字線索提供目前情境。");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: "text", error: error);
                return new(material.Text + "\n本輪未取得可用的螢幕內容，請按已有情境自然關心主人。");
            }
#endif
        }
        var captured = await Task.Run(() => CompanionCaptureFallback.ReadAsync(
            () => Task.FromResult(CompanionScreenCapture.Capture(handle, 2560, true)),
            () => Task.FromResult(CompanionScreenCapture.CaptureScreen(2560)), token), token);
        var image = captured.Image;
        token.ThrowIfCancellationRequested();
        return image == null ? new(material.Text + "\n本輪未取得有效的前景截圖，請按已有情境自然關心主人。")
            : new(material.Text + "\n" + (captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : "本輪附圖是按桌寵前景規則取得的目前畫面，用來理解主人眼前的活動。"), AgentPromptImage.FromDataUrl(image.DataUrl), "image");
    }

    private void ConfigureAgent()
    {
        agent?.Configure(preferences.AgentHarnessId, new(preferences.AgentEnabled, preferences.PuppetMode, preferences.AgentDailySpeech));
        if (!AgentDailyActive) nextWarmup = DateTime.MinValue;
    }
    private bool agentLoading;
    private void TickAgent(DateTime now)
    {
        agent?.Tick(new DateTimeOffset(now), !hiddenByUser && !fullscreenHidden && !dragging && !exitAnimation && !companionClosed);
        bool loading = agent?.AwaitingSpeech == true && !hiddenByUser && !fullscreenHidden;
        if (loading != agentLoading || loading && loadingIndicator.Visibility != Visibility.Visible)
        {
            agentLoading = loading;
            if (loading) BeginModelSpeech(); else if (!speechRequestPending && !weatherRequestPending) EndModelSpeech();
        }
    }
    private bool DispatchAgentDaily()
    {
        if (!AgentDailyActive) return false;
        nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        agent?.DailyCare(); return true;
    }
    // Starts whatever is switched on and is not running yet; called at startup and by both settings pages.
    private void InitializeMcp()
    {
        if (preferences.McpEnabled) mcpInterface ??= new ControlServer((name, args, token) => ExecuteMcp(name, args, token, false));
        if (preferences.AgentEnabled)
        {
            mcpServer ??= new ControlServer((name, args, token) => ExecuteMcp(name, args, token, true), agentPipe);
            agent ??= new AgentCoordinator(Path.Combine(Environment.CurrentDirectory, "agent"),
                Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe"), agentPipe, RequestAgentPermission,
                App.Args.Contains("--test-mode") ? Path.Combine(Environment.CurrentDirectory, "agent-test-workspace") : null);
            if (!HarnessCatalog.All.Any(x => x.Id == preferences.AgentHarnessId)) preferences.AgentHarnessId = "codex";
            agent.LocalSpeechRequested = token => Dispatcher.InvokeAsync(() => RequestAgentLocalSpeech(token)).Task.Unwrap();
            agent.SpeechBrief = (kind, allowImages, token) => Dispatcher.InvokeAsync(() => BuildAgentSpeechBrief(kind, allowImages, token)).Task.Unwrap();
            agent.AttachmentImage = McpAttachmentPicture;
        }
        ConfigureAgent();
        if (AgentDailyActive) brain.Stop();
        PreloadWalk();
    }
    // Stops whatever has been switched off, and everything when both are off or the fish is closing.
    private void StopMcp()
    {
        bool everything = companionClosed || !preferences.AgentEnabled && !preferences.McpEnabled;
        if (everything || !preferences.AgentEnabled)
        {
            // Switching the connection off ends this harness's care reservations, also while the MCP interface keeps
            // puppet mode on; closing the fish keeps them for the next start.
            if (!preferences.AgentEnabled && agent != null) agent.Wakeups.Clear(agent.HarnessId);
            agent?.Dispose(); agent = null; mcpServer?.Dispose(); mcpServer = null;
        }
        if (everything || !preferences.McpEnabled) { mcpInterface?.Dispose(); mcpInterface = null; }
        if (everything)
        {
            mcpConsentWindow?.Close(); mcpInput?.Close();
            mcpActions.Clear(); mcpNextSpeech = null;
            CancelWalk("mcp_disabled");
        }
        else if (!McpInputEnabled && !mcpInputPicking) mcpInput?.Close();
    }
    private void SetPuppetMode(bool enabled)
    {
        if (preferences.PuppetMode == enabled) return;
        preferences.PuppetMode = enabled;
        InvalidateSpeechRequests(AgentDailyActive); EndGreetingNotice();
        if (!externalBubble) { speech.Cancel(); bubble.Visibility = Visibility.Collapsed; }
        CancelWalk("puppet_mode"); CancelMovement(); ConfigureAgent(); RefreshMcpMenuAvailability();
        if (enabled && companionReady) PlayAnimation(animationPolicy.Find("待机呼吸休闲"), "PuppetIdle");
        if (!enabled)
        {
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            nextWarmup = DateTime.MinValue; PlayIdle();
        }
    }
    private async Task RequestAgentLocalSpeech(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Reached from the Agent's wakeups and from request_local_speech on either server.
        if (!preferences.AnyAgentControl || AgentDailyActive) throw new InvalidOperationException("目前日常吐字由 Agent 編寫；要讓魚魚自己的模型吐字，需要關閉「把日常關心的吐字交給 Agent」。");
        if (!preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower) throw new InvalidOperationException("魚魚自己的模型沒有啟用、還沒安裝，或依供電設定暫停了。");
        if (speechRequestPending || companionClosed || exitAnimation || modelInstaller != null || hiddenByUser || fullscreenHidden) throw new InvalidOperationException("魚魚目前正在忙或已隱藏，請稍後再讓魚魚自己的模型說話。");
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => InvalidateSpeechRequests()));
        await GenerateLocalSpeechAsync(true);
        token.ThrowIfCancellationRequested();
    }
    // toInterface: where a user_message goes (Send decides). What the user types reaches one side only: the clients of the MCP
    // interface while 雙擊輸入交給 MCP 介面 is on and one is connected, otherwise the built-in Agent (its ACP turn and its
    // private pipe), so the two never both answer it. Every other event reaches both.
    private void PublishMcpInteraction(string kind, object detail, bool toInterface = false)
    {
        DiagnosticLog.Write(DiagnosticEvent.Interaction, kind: kind);
        if (kind == "user_message")
        {
            if (toInterface) mcpInterface?.Publish(kind, detail); else mcpServer?.Publish(kind, detail);
            return; // User submission uses the bounded ACP queue and only clears accepted text.
        }
        mcpInterface?.Publish(kind, detail);
        mcpServer?.Publish(kind, detail);
        if (kind == "click" && preferences.AgentRespondToClick || kind == "drag_end" && preferences.AgentRespondToDrag)
            agent?.Interaction(kind, detail);
    }
    private bool PlayQueuedMcpAction()
    {
        // Between the stops of a walk_to tour the next leg would cut a clip off; it plays once the tour is over.
        if (mcpActions.Count == 0 || tour != null) return false;
        CancelMovement();
        PlayAnimation(animationPolicy.Find(mcpActions.Dequeue()), "External");
        return true;
    }
    private void RefreshMcpMenuAvailability()
    {
        if (companionContextMenu != null) foreach (int index in new[] { 1, 2 }) if (companionContextMenu.Items[index] is MenuItem item) item.IsEnabled = true;
        if (notifyIcon?.ContextMenuStrip != null) foreach (int index in new[] { 2, 3 }) notifyIcon.ContextMenuStrip.Items[index].Enabled = true;
    }
    // The Agent's turn counts as spoken when it calls say, say_next or walk_to with say (ExecuteMcp marks it), not
    // when a client of the MCP interface speaks meanwhile.
    // transient: a progress line (say with transient=true, as the Hermes bridge sends them), kept out of the chat history.
    private void ShowMcpSpeech(string text, bool transient = false)
    {
        InvalidateSpeechRequests(); EndGreetingNotice();
        externalBubble = true; walkBubble = false;
        externalBubbleAt = DateTime.UtcNow;
        bubbleIsModelReply = false;
        bubbleUntil = DateTime.MaxValue;
        speech.Show(text, true);
        // The history keeps the line as the bubble showed it, converted to Taiwan Traditional.
        if (!transient) chatLog.Add(false, speech.FullText);
    }
    private bool ConsumeMcpSpeech()
    {
        if (mcpNextSpeech == null) return false;
        var text = mcpNextSpeech; mcpNextSpeech = null;
        nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        ShowMcpSpeech(text);
        return true;
    }
    // agentChannel: the call came through the Agent connection's private pipe rather than the MCP interface.
    private async Task<object> ExecuteMcp(string name, JsonElement args, CancellationToken token, bool agentChannel)
    {
        // All mutations, scheduling and target selection run on the ordinary WPF dispatcher.
        return await Dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (!McpChannelOpen(agentChannel) || companionClosed || exitAnimation)
                throw new InvalidOperationException(agentChannel ? "Agent 連線已關閉或桌寵正在退出。" : "MCP 介面已關閉或桌寵正在退出。");
            switch (name)
            {
                case "get_settings":
                    return ControlTools.Text(new { settings = CurrentMcpSettings(), state = new {
                        visible = IsVisible, hiddenByUser, fullscreenHidden, action = currentAction, animationPhase,
                        queuedActions = mcpActions.ToArray(), queuedSpeech = mcpNextSpeech, connections = McpConnections, mcpClients = mcpInterface?.Connections ?? 0,
                        inputOpen = mcpInput?.IsVisible == true, inputAvailable = McpInputAvailable,
                        agentConnected = agent?.Connected == true, agentStatus = agent?.Status, agentSession = agent?.SessionId, nextWake = agent?.NextWake,
                        bubbleVisible = bubble.Visibility == Visibility.Visible, externalBubble, speechPending = speechRequestPending || weatherRequestPending,
                        modelAvailable = brain.ModelAvailable, modelBusy = brain.Busy, modelStatus = brain.Status, walk = WalkSnapshot(), tour = TourSnapshot(), lastWalk,
                        walkBubble = walkBubble && bubble.Visibility == Visibility.Visible, fullscreen = CompanionNative.IsFullscreen(), idleSeconds = Math.Round(ReminderIdle),
                        activeMinutes = Reminders.ActiveFor(ReminderNow) is { } active ? (int?)active.TotalMinutes : null,
                        lastBreakMinutes = Reminders.LastBreak is { } pause ? (int?)Math.Round(pause.TotalMinutes) : null } });
                case "get_context":
                    // Asking the Shell for the folders can take a moment; the dispatcher stays free meanwhile.
                    var folders = await Task.Run(CompanionScreenSearch.OpenFolders, token);
                    return ControlTools.Text(CurrentContext(folders));
                case "update_settings":
                    await UpdateMcpSettings(args, token, agentChannel); return ControlTools.Text(new { settings = CurrentMcpSettings() });
                case "request_local_speech":
                    await RequestAgentLocalSpeech(token); return ControlTools.Text(new { generated = bubbleIsModelReply && bubble.Visibility == Visibility.Visible });
                case "list_actions":
                    return ControlTools.Text(animationPolicy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground": case "capture_screen":
                    var handle = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部顯示器組成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await Task.Run(() => {
                        token.ThrowIfCancellationRequested();
                        return name == "capture_foreground" ? CompanionScreenCapture.Capture(handle, limit, true) : CompanionScreenCapture.CaptureScreen(limit);
                    }, token);
                    token.ThrowIfCancellationRequested();
                    if (!McpChannelOpen(agentChannel)) throw new OperationCanceledException();
                    if (image == null) throw new IOException("未能擷取螢幕。");
                    // Where the image lies on the physical virtual desktop, so its pixels can be turned into screen points (walk_to).
                    var source = image.Source;
                    return ControlTools.Image(image.DataUrl, new { target = name, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow,
                        captureId = RememberMcpCapture(image), screen = new { x = source.X, y = source.Y, width = source.Width, height = source.Height },
                        scale = source.Width > 0 ? Math.Round(image.Width / (double)source.Width, 6) : 1, coordinateSpace = "physical", pet = PetPhysicalBounds() });
                case "play_action": case "interrupt_action":
                    var asked = ControlTools.RequiredText(args, "action", 200);
                    if (!companionReady) throw new ArgumentException("動作播放器尚未就緒。");
                    var action = ResolveAction(asked) ?? throw new ArgumentException(UnknownAction("action", asked));
                    if (name == "interrupt_action")
                    {
                        mcpActions.Clear(); CancelWalk("interrupt"); CancelMovement();
                        PlayAnimation(animationPolicy.Find(action), "External");
                    }
                    else
                    {
                        if (mcpActions.Count >= 100) throw new InvalidOperationException("待播放動作佇列已滿。");
                        mcpActions.Enqueue(action);

                    }
                    return ControlTools.Text(new { action, started = name == "interrupt_action" || animationPhase == "External" && currentAction == action, queued = mcpActions.Count });
                case "say": case "say_next":
                    var text = ControlTools.RequiredText(args, "text");
                    bool transient = name == "say" && ControlTools.OptionalBool(args, "transient", false);
                    if (agentChannel) agent?.MarkSpoken();
                    if (name == "say") ShowMcpSpeech(text, transient); else mcpNextSpeech = text;
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                case "walk_to":
                    if (agentChannel && args.ValueKind == JsonValueKind.Object && args.TryGetProperty("say", out var arrival) && arrival.ValueKind == JsonValueKind.String) agent?.MarkSpoken();
                    // Awaiting the arrival leaves the dispatcher free, so get_settings and say keep working meanwhile.
                    return ControlTools.Text(await WalkToMcp(args, token));
                case "find_on_screen":
                    return ControlTools.Text(await FindOnScreenMcp(args, token));
                case "get_operation_guide": case "get_wakeups": case "schedule_wakeup": case "cancel_wakeup":
                    // The built-in Agent's skill, persona and care reservations are its own, never the MCP interface's.
                    if (!agentChannel) throw new InvalidOperationException("這個工具只供內建的 Agent 連線使用。");
                    return ControlTools.Text(agent?.ExecuteTool(name, args) ?? throw new InvalidOperationException("Agent 尚未連線。"));
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        }).Task.Unwrap();
    }
    // The clips are named in Simplified Chinese; an agent writing Traditional Chinese, as it is asked to, still means
    // the same clip. Null when no clip has the name either way.
    private string? ResolveAction(string name)
    {
        if (animationPolicy.Rules.Any(r => r.Id == name)) return name;
        string wanted = CompanionTraditional.Convert(name);
        return animationPolicy.Rules.FirstOrDefault(r => CompanionTraditional.Convert(r.Id) == wanted)?.Id;
    }
    // Names the closest clips, so a misspelt name is fixed in one step instead of a round of list_actions.
    private string UnknownAction(string field, string name)
    {
        string wanted = CompanionTraditional.Convert(name);
        var closest = animationPolicy.Rules.Select(r => r.Id).OrderBy(id => CompanionScreenSearch.EditDistance(CompanionTraditional.Convert(id), wanted)).Take(3);
        return $"{field} 沒有「{name}」這個動作。最接近的是：{string.Join("、", closest)}。請照 list_actions 的名稱原樣填，不要重複查 list_actions。";
    }
    // Neither the [JsonIgnore] aliases nor the protected API key and authorization code ever reach a client.
    private Dictionary<string, object?> CurrentMcpSettings()
    {
        var result = typeof(CompanionPreferences).GetProperties()
            .Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)) && !CompanionPreferences.PrivateSettings.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(preferences));
        result["StartupEnabled"] = StartupEnabled();
        return result;
    }
    private async Task UpdateMcpSettings(JsonElement args, CancellationToken token, bool agentChannel)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必須為物件。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            // A refused field is named, never echoed with its value.
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException(property.Name switch {
                "McpEnabled" => "MCP 介面只能由使用者在設定中開啟或關閉。",
                "AgentEnabled" or "AgentHarnessId" => "Agent 連線只能由使用者在設定中開啟、關閉或選擇。",
                "ModelSource" or "RemoteUrl" or "RemoteModel" or "ScreenImages" => "模型來源與遠端模型只能由使用者在設定中修改。",
                _ when CompanionPreferences.PrivateSettings.Contains(property.Name) => "API Key 與授權碼只能由使用者在設定中填寫。",
                _ => "未知設定：" + property.Name });
            if (property.Name == "StartupEnabled") { values[property.Name] = property.Value.GetBoolean(); continue; }
            var field = typeof(CompanionPreferences).GetProperty(property.Name)!;
            values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), field.PropertyType);
        }
        // Validate the entire patch before applying any part of it.
        foreach (var item in values)
        {
            bool Range(double low, double high) => item.Value is IConvertible c && double.IsFinite(c.ToDouble(null)) && c.ToDouble(null) >= low && c.ToDouble(null) <= high;
            bool valid = item.Key switch {
                "Scale" or "BubbleScale" => Range(.5, 2),
                "AnimationIntervalSeconds" => Range(30, 300),
                "BreakfastHour" or "LunchHour" or "DinnerHour" => Range(0, 23),
                "BubbleDurationIndex" => Range(0, 7),
                "SpeechMinMinutes" => item.Value is int minutes && CompanionPreferences.SpeechRanges.Any(r => r.Min == minutes),
                "SitReminderMinutes" => item.Value is int sit && CompanionPreferences.SitReminderChoices.Contains(sit),
                "LateReminderHour" => item.Value is int late && CompanionPreferences.LateReminderChoices.Contains(late),
                "WelcomeBackMinutes" => item.Value is int away && CompanionPreferences.WelcomeBackChoices.Contains(away),
                "EventReminderMinutes" => item.Value is int ahead && CompanionPreferences.EventReminderChoices.Contains(ahead),
                "WeatherRegionCode" => item.Value is string region && CompanionRegions.District(region).Code == region,
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000),
                _ => item.Value is bool };
            if (!valid) throw new ArgumentException("設定值無效：" + item.Key);
        }
        // One switch, named as each side names it: the Agent connection hands the care and actions to the Agent,
        // the MCP interface calls it 提線木偶模式.
        if (values.TryGetValue("PuppetMode", out var puppet) && puppet is true && !preferences.PuppetMode
            && !await ConfirmMcp(companionSettings, agentChannel ? ControlTools.BehaviorControlTitle : ControlTools.McpPuppetTitle,
                agentChannel ? ControlTools.PuppetWarning : ControlTools.McpPuppetWarning, token,
                note: agentChannel ? (preferences.McpEnabled ? PuppetSharedWithMcp : null) : (preferences.AgentEnabled ? PuppetSharedWithAgent : null)))
            throw new OperationCanceledException(agentChannel ? "使用者取消讓 Agent 安排日常關心與動作。" : "使用者取消開啟提線木偶模式。");
        token.ThrowIfCancellationRequested();
        if (!McpChannelOpen(agentChannel)) throw new OperationCanceledException();
        bool oldGpu = preferences.GpuEnabled, oldModel = preferences.ModelEnabled;
        string oldRegion = preferences.WeatherRegionCode;
        if (values.ContainsKey("Left") || values.ContainsKey("Top") || values.ContainsKey("Scale")) CancelWalk("settings");
        foreach (var item in values)
        {
            if (item.Key == "StartupEnabled") SetCompanionStartup((bool)item.Value!);
            else if (item.Key != "PuppetMode") typeof(CompanionPreferences).GetProperty(item.Key)!.SetValue(preferences, item.Value);
        }
        if (values.TryGetValue("PuppetMode", out puppet)) SetPuppetMode((bool)puppet!);
        if (oldGpu != preferences.GpuEnabled || oldModel && !preferences.ModelEnabled) InvalidateSpeechRequests(true);
        brain.UseGpu = preferences.GpuEnabled; brain.WeatherRegionCode = preferences.WeatherRegionCode;
        if (oldRegion != preferences.WeatherRegionCode) InvalidateSpeechRequests();
        ApplyCompanionPowerPolicy(); ApplyCompanionScale(false); ApplyCompanionBubbleScale(false);
        if (values.TryGetValue("Left", out var left) && left is double x) Left = x;
        if (values.TryGetValue("Top", out var top) && top is double y) Top = y;
        // A walk still under way keeps its path; the 14 DIP edge snap would read as an outside move and stop it.
        if (!Walking) { ClampCompanion(); anchor = new(Left, Top); }
        if (!preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hiddenByUser) Show(); }
        if (values.ContainsKey("SpeechMinMinutes")) nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        if (values.ContainsKey("AnimationIntervalSeconds")) nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
        if (values.ContainsKey("BubbleDurationIndex") && bubble.Visibility == Visibility.Visible) ResetBubbleDeadline();
        ConfigureAgent();
        if (!McpInputEnabled && !mcpInputPicking) mcpInput?.Close();
        UpdateCompanionVisibility();
        SaveCompanion();
        refreshCompanionSettings?.Invoke();
    }
    // The Agent 與 MCP page: the MCP interface for clients outside the fish, such as the Hermes bridge. The Agent
    // connection on its own page needs none of it.
    private Action AddMcpInterfaceSettings(StackPanel panel, Window owner)
    {
        var enabled = new CheckBox { Content = "開啟 MCP 介面", IsChecked = preferences.McpEnabled, Margin = new(0, 12, 0, 4) };
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "開啟後行為可被其他 Agent 控制，例如 Hermes 橋接程式。內建的 Agent 連線不需要開啟這裡。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var extra = new StackPanel { Visibility = preferences.McpEnabled ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 8, 0, 0) };
        var puppet = new CheckBox { Content = "提線木偶模式", IsChecked = preferences.PuppetMode, Margin = new(0, 7, 0, 4) };
        extra.Children.Add(puppet);
        extra.Children.Add(new TextBlock { Text = "關閉大肥魚自身的吐字和動作切換邏輯，完全由外部 Agent 接管", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var chat = new CheckBox { Content = "雙擊輸入交給 MCP 介面（Hermes 橋接程式）", IsChecked = preferences.McpChatEnabled, Margin = new(0, 16, 0, 4) };
        chat.Click += (_, _) => { preferences.McpChatEnabled = chat.IsChecked == true; if (!McpInputEnabled && !mcpInputPicking) mcpInput?.Close(); SaveCompanion(); };
        extra.Children.Add(chat);
        extra.Children.Add(new TextBlock { Text = "開啟且 MCP 介面有客戶端連線時，雙擊魚魚輸入的內容只會傳給 MCP 介面，不會再送給「連線 Agent」頁的 Agent，避免兩邊各回答一次；沒有客戶端連線時，仍交給 Agent 連線。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        // Said by the Hermes bridge (it hears user_returned and reads these two settings); without it they do nothing.
        var briefing = new CheckBox { Content = "每天第一次坐下時講今日簡報", IsChecked = preferences.DailyBriefing, Margin = new(0, 16, 0, 4) };
        briefing.Click += (_, _) => { preferences.DailyBriefing = briefing.IsChecked == true; SaveCompanion(); };
        extra.Children.Add(briefing);
        var welcome = new ComboBox { Margin = new(0, 8, 0, 4) };
        foreach (var minutes in CompanionPreferences.WelcomeBackChoices)
            welcome.Items.Add("離開很久回來時說期間發生的事：" + (minutes == 0 ? "關閉" : minutes < 60 ? $"離開 {minutes} 分鐘以上" : $"離開 {minutes / 60} 小時以上"));
        welcome.SelectedIndex = Math.Max(0, Array.IndexOf(CompanionPreferences.WelcomeBackChoices, preferences.WelcomeBackMinutes));
        System.Windows.Automation.AutomationProperties.SetName(welcome, "回來時說期間發生的事");
        welcome.SelectionChanged += (_, _) => { if (welcome.SelectedIndex >= 0) { preferences.WelcomeBackMinutes = CompanionPreferences.WelcomeBackChoices[welcome.SelectedIndex]; SaveCompanion(); } };
        extra.Children.Add(welcome);
        extra.Children.Add(new TextBlock { Text = "由 Hermes 橋接程式說（沒有執行橋接程式時不會說）：天氣、今天的 Google 日曆行程（已設定 Google 日曆時）和排程，以及你不在時的排程回報與其他對話；Hermes 暫時沒回應時，改說橋接程式自己整理的簡短版本。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var config = new Button { Content = "複製 MCP 連線配置", Margin = new(0, 12, 0, 8) };
        config.Click += (_, _) => {
            var command = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe");
            Clipboard.SetText(JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["big-blue-fish"] = new { command, args = Array.Empty<string>() } } }, new JsonSerializerOptions { WriteIndented = true }));
            config.Content = "連線配置已複製";
        };
        extra.Children.Add(config);
        var status = new TextBlock { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        extra.Children.Add(status);
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => {
            status.Text = $"已連線 {mcpInterface?.Connections ?? 0} 個 MCP 客戶端";
            // The Agent connection page and update_settings switch the same mode.
            if (puppet.IsEnabled && puppet.IsChecked != preferences.PuppetMode) puppet.IsChecked = preferences.PuppetMode;
        };
        clock.Start();
        panel.Children.Add(extra);
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try {
            bool on = enabled.IsChecked == true;
            if (on && !await ConfirmMcp(owner, "開啟 MCP 介面", ControlTools.McpPrivacyWarning)) { enabled.IsChecked = false; return; }
            if (on && !File.Exists(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe"))) {
                ModernUI.Choice(owner, "MCP 元件未就緒", "請使用包含 MCP 元件的完整標準版安裝程式。", "確定"); enabled.IsChecked = false; return;
            }
            // Puppet mode is shared with the Agent connection and ends here only when that is off too.
            if (!on && !preferences.AgentEnabled) SetPuppetMode(false);
            preferences.McpEnabled = on;
            if (on) InitializeMcp(); else StopMcp();
            extra.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            puppet.IsChecked = preferences.PuppetMode; SaveCompanion();
            } finally { enabled.IsEnabled = true; }
        };
        puppet.Click += async (_, _) => {
            puppet.IsEnabled = false;
            try {
            bool on = puppet.IsChecked == true;
            if (on && !await ConfirmMcp(owner, ControlTools.McpPuppetTitle, ControlTools.McpPuppetWarning, note: preferences.AgentEnabled ? PuppetSharedWithAgent : null)) { puppet.IsChecked = false; return; }
            SetPuppetMode(on); SaveCompanion();
            } finally { puppet.IsEnabled = true; }
        };
        return clock.Stop;
    }
    // note: a paragraph of its own under the message, which stays exactly as given.
    private async Task<bool> ConfirmMcp(Window? owner, string title, string message, CancellationToken token = default, string accept = "瞭解並開啟", string? note = null)
    {
        if (mcpConsentOpen) throw new InvalidOperationException("請先處理已開啟的 MCP 提示視窗。");
        mcpConsentOpen = true;
        try
        {
            var dialog = new ModernWindow(title, title, message, 640, 460);
            if (note != null) dialog.Body.Children.Add(ModernUI.Text(note));
            if (owner?.IsVisible == true) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            bool accepted = false;
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            mcpConsentWindow = dialog;
            dialog.Actions.Children.Add(ModernUI.Button(accept, () => { accepted = true; dialog.Close(); }, true));
            dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close));
            using var registration = token.Register(() => Dispatcher.BeginInvoke(() => { accepted = false; dialog.Close(); }));
            dialog.Show();
            return await finished.Task;
        }
        finally { mcpConsentWindow = null; mcpConsentOpen = false; }
    }
    private bool TryOpenMcpInput()
    {
        if (!McpInputAvailable || exitAnimation || companionClosed) return false;
        if (mcpInput != null) { mcpInput.Show(); mcpInput.Activate(); return true; }
        const string hint = "通過魚魚和已連線的Agent說話", ready = "Ctrl+Enter 傳送";
        var blue = new SolidColorBrush(global::BigBlueFish.Windows.CompanionBubble.HairBlue);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000,
            Height = 56, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = 14, Foreground = blue, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 2, 22, 0) };
        System.Windows.Automation.AutomationProperties.SetName(text, hint);
        var placeholder = new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 14,
            Margin = new Thickness(2, 4, 22, 0), IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap };
        var editor = new Grid(); editor.Children.Add(text); editor.Children.Add(placeholder);
        // One line: the window has explicit heights, so a long notice is shortened and shown whole as its tooltip.
        var status = new TextBlock { Text = ready, Foreground = Brushes.SlateGray, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0) };
        status.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { RelativeSource = System.Windows.Data.RelativeSource.Self });
        // Scrolling up over the input box reveals the chat log above it; scrolling down past its end hides it again.
        var history = new StackPanel { Margin = new Thickness(2, 0, 6, 0) };
        // Owner lines get light blue bubbles; black text is 14.8:1 on this blue, white only 1.4:1.
        var ownerBubble = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));
        var historyView = new ScrollViewer { Content = history, Height = 220, Margin = new Thickness(0, 0, 0, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        var content = new StackPanel(); content.Children.Add(historyView); content.Children.Add(editor);
        var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 251, 244)), BorderBrush = blue,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = content };
        var window = new Window { Title = "藍色大肥魚 · 輸入", Width = 354, Height = 120, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, Owner = this, Content = frame, FontFamily = new FontFamily("Microsoft JhengHei UI") };
        using (var theme = typeof(ModernWindow).Assembly.GetManifestResourceStream("BigBlueFish.ModernTheme.xaml")!)
        {
            var resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(theme);
            window.Resources[typeof(Button)] = resources[typeof(Button)];
        }
        var close = new Button { Content = "×", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 16,
            MinHeight = 0, Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Foreground = blue, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "收起輸入框" };
        close.Click += (_, _) => window.Close(); editor.Children.Add(close);
        SweepMcpAttachments();
        var tray = new McpAttachmentTray(window, blue, notice => status.Text = notice);
        content.Children.Add(tray.View);
        // To the clients of the MCP interface while the input is handed to them and one is connected; otherwise to the Agent's ACP session.
        bool AgentReady() => agent?.Connected == true;
        bool CanSend() => (AgentReady() || McpChatAvailable) && tray.Saving == 0 && (!string.IsNullOrWhiteSpace(text.Text) || tray.Items.Count > 0);
        void Send()
        {
            if (!CanSend()) return;
            var attachments = tray.Peek(out int missing);
            if (attachments.Length == 0 && string.IsNullOrWhiteSpace(text.Text)) { tray.Clear(); status.Text = "附件已不存在，沒有傳送。"; return; }
            bool toInterface = McpChatAvailable;
            // The ACP turn gets the files as resource_link blocks and their local paths in the event; the Agent opens them with
            // the user's own permissions. The 4000-character limit is the text's alone.
            // A refused message stays in the box with its attachments.
            if (!toInterface && agent is { Connected: true } link && !link.SendUser(text.Text, attachments))
            { status.Text = string.IsNullOrEmpty(link.Detail) ? link.Status : link.Detail; return; }
            // Without attachments the event stays exactly { text }, as older clients expect.
            PublishMcpInteraction("user_message", attachments.Length == 0 ? new { text = text.Text } : new { text = text.Text, attachments }, toInterface);
            // The chat history keeps what the owner typed, and names the files sent with it.
            chatLog.Add(true, attachments.Length == 0 ? text.Text
                : (string.IsNullOrWhiteSpace(text.Text) ? "" : text.Text.TrimEnd() + "\n") + "附件：" + string.Join("、", attachments.Select(a => a.Name)));
            tray.Clear(); text.Clear();
            string target = toInterface ? "MCP 介面" : "Agent";
            status.Text = attachments.Length == 0 ? $"已傳送給 {target}" : $"已傳送給 {target}（含 {attachments.Length} 個附件）";
            if (missing > 0) status.Text += $"，{missing} 個檔案已不存在";
            text.Focus();
        }
        var send = ModernUI.Button("傳送", Send, true); send.MinWidth = 58; send.MinHeight = 0; send.Margin = new Thickness(0); send.Padding = new Thickness(12, 5, 12, 5); send.FontSize = 12;
        // Not focusable, so a click keeps the caret in the editor.
        var attach = new Button { Content = "\uE723", FontFamily = McpGlyphs, FontSize = 15, Width = 28, Height = 26, MinHeight = 0, Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0), Foreground = blue, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false,
            ToolTip = "附加圖片或影片（也可 Ctrl+V 貼上或拖曳）" };
        System.Windows.Automation.AutomationProperties.SetName(attach, "附加檔案");
        attach.Click += (_, _) => {
            // Closing or hiding the owner would tear down the running dialog, so Follow, the clock and walk_to leave it alone meanwhile.
            mcpInputPicking = true;
            string[]? picked;
            try { picked = PickMcpAttachments(window); } finally { mcpInputPicking = false; }
            if (mcpInput != window) return;
            if (picked != null) tray.Add(picked, "picker");
            window.Activate(); text.Focus();
        };
        var actions = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.Children.Add(attach); actions.Children.Add(status); actions.Children.Add(send); Grid.SetColumn(status, 1); Grid.SetColumn(send, 2); content.Children.Add(actions);
        text.TextChanged += (_, _) => { placeholder.Visibility = text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; send.IsEnabled = CanSend(); };
        window.PreviewKeyDown += (_, e) => {
            if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); }
            else if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { e.Handled = true; Send(); }
        };
        AcceptMcpPaste(window, tray);
        AcceptMcpDrops(window, frame, tray, () => text.Focus());
        bool positioning = false;
        void Follow()
        {
            if (positioning || mcpInput != window) return;
            positioning = true;
            try {
                if (!IsVisible) { if (!mcpInputPicking) window.Hide(); return; }
                var area = CompanionEdgeLayout.WorkArea(this); var body = CompanionEdgeLayout.PetBounds(this);
                window.Width = Math.Min(354, Math.Max(160, area.Width - 16));
                double x = Left + body.Left - window.Width - 8;
                if (x < area.Left + 8) x = Left + body.Right + 8;
                double y = Top + body.Bottom - window.Height;
                (x, y) = AvoidWalkTarget(x, y, window.Width, window.Height, body, area);
                window.Left = Math.Clamp(x, area.Left + 8, Math.Max(area.Left + 8, area.Right - window.Width - 8));
                window.Top = Math.Clamp(y, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - window.Height - 8));
                if (!window.IsVisible) window.Show();
            } finally { positioning = false; }
        }
        // Explicit heights: SizeToContent can flip back to Manual or leave Height NaN, which Follow would clamp to NaN.
        // The open chat history adds its own height above the editor and the attachment row.
        void Resize() => window.Height = (tray.Items.Count > 0 ? 184 : 120)
            + (historyView.Visibility == Visibility.Visible ? historyView.Height + historyView.Margin.Bottom : 0);
        tray.Changed = () => { Resize(); send.IsEnabled = CanSend(); Follow(); };
        (int, bool)? historyShown = null;
        void FillHistory()
        {
            if (historyShown == (chatLog.Version, preferences.ChatLogShowAll)) return;
            historyShown = (chatLog.Version, preferences.ChatLogShowAll);
            history.Children.Clear();
            foreach (var (user, line) in chatLog.Lines(preferences.ChatLogShowAll))
            {
                var block = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = user ? Brushes.Black : blue, Margin = new Thickness(0, 4, 0, 4) };
                history.Children.Add(!user ? block : new Border { Child = block, Background = ownerBubble, CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(40, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right });
            }
            if (history.Children.Count == 0) history.Children.Add(new TextBlock { Text = "還沒有聊天記錄", FontSize = 13, Foreground = Brushes.Gray });
            historyView.ScrollToEnd();
        }
        void ShowHistory(bool show)
        {
            if ((historyView.Visibility == Visibility.Visible) == show) return;
            historyView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Resize();
            if (show) { historyShown = null; FillHistory(); }
            Follow();
        }
        // The attachment row turns the wheel into sideways scrolling, so the wheel opens the history everywhere else.
        window.PreviewMouseWheel += (_, e) => { if (e.Delta > 0 && historyView.Visibility != Visibility.Visible && !tray.View.IsMouseOver) { e.Handled = true; ShowHistory(true); } };
        historyView.PreviewMouseWheel += (_, e) => { if (e.Delta < 0 && historyView.VerticalOffset >= historyView.ScrollableHeight) { e.Handled = true; ShowHistory(false); } };
        EventHandler moved = (_, _) => Follow();
        SizeChangedEventHandler resized = (_, _) => Follow();
        DependencyPropertyChangedEventHandler visibility = (_, _) => Follow();
        DpiChangedEventHandler dpiChanged = (_, _) => Follow();
        LocationChanged += moved; SizeChanged += resized; IsVisibleChanged += visibility; DpiChanged += dpiChanged;
        // What the clock itself last wrote: attachment notices and send results stay until the connection state changes.
        string? shown = null;
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => {
            if (!McpInputEnabled) { if (!mcpInputPicking) window.Close(); return; }
            Follow();
            if (historyView.Visibility == Visibility.Visible) FillHistory();
            send.IsEnabled = CanSend();
            // Nothing can take a message: say why, keeping the draft and its attachments for later.
            var link = agent is { Connected: true } connected ? connected : null;
            string? state = McpChatAvailable ? null
                : link != null ? (link.Busy ? "Agent 正在處理…" : null)
                : preferences.AgentEnabled ? agent?.Status ?? "Agent 尚未連線"
                : "Agent 已斷開，重新連線後可傳送。";
            bool blocked = link == null && !McpChatAvailable;
            if (state != null && (blocked || status.Text == shown || status.Text == ready)) { status.Text = state; shown = state; }
            else if (state == null && shown != null) { if (status.Text == shown) status.Text = ready; shown = null; }
        };
        clock.Start(); mcpInput = window; mcpTray = tray;
        window.Closed += (_, _) => {
            clock.Stop(); LocationChanged -= moved; SizeChanged -= resized; IsVisibleChanged -= visibility; DpiChanged -= dpiChanged;
            tray.Discard(); if (mcpTray == tray) mcpTray = null; mcpInput = null;
        };
        window.Loaded += (_, _) => { text.Focus(); send.IsEnabled = CanSend(); };
        Follow(); window.Activate(); return true;
    }
#endif
}
