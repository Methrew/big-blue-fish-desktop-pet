// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
#if !COMPANION_SAFE
using BigBlueFish.Control;
using BigBlueFish.Agent;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
#if COMPANION_SAFE
    static bool McpAvailable => false;
    static string? TestAgentPipe => null;
    bool AgentDailyActive => false;
    void TickAgent(DateTime now) { }
    void ConfigureAgent() { }
    bool DispatchAgentDaily() => false;
    void InitializeMcp() { Preferences.McpEnabled = false; Preferences.PuppetMode = false; Preferences.McpWakeEnabled = false; }
    void StopMcp() { }
    void PublishInteraction(string kind, object detail) { DiagnosticLog.Write(DiagnosticEvent.Interaction, kind: kind); }
    bool TryOpenMcpInput() => false;
#else
    static bool McpAvailable => true;
    ControlServer? mcpServer;
    AgentCoordinator? agent;
    readonly string agentPipe = ControlTools.PipeName + "." + Guid.NewGuid().ToString("N");
    // Linux MCP is only the Agent connection's own pipe; --test-mode reports it so LinuxVerification can attach a bridge.
    string? TestAgentPipe => mcpServer == null ? null : agentPipe;
    bool AgentDailyActive => Preferences.AgentEnabled && Preferences.AgentDailySpeech;
    async Task<AgentSpeechBrief> BuildAgentSpeechBrief(string kind, bool allowImages, CancellationToken token)
    {
        bool publicInfo = Preferences.PublicInfo;
        var material = await brain.AgentSpeechBrief(kind == "weather_request", publicInfo, token);
        if (!material.ObserveScreen) return new(material.Text);
        if (!Preferences.ForegroundEnabled) return new(material.Text + "\n本輪未啟用讀取前景內容，請按已有情境自然關心主人。");
        var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
        if (!allowImages)
        {
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
        }
        try
        {
            var captured = await CompanionCaptureFallback.ReadAsync<CompanionWindowImage>(
                async () => await LinuxCapture.CaptureAsync(handle, 2560, false, token),
                async () => await LinuxCapture.CaptureAsync(handle, 2560, true, token), token);
            var image = captured.Image;
            token.ThrowIfCancellationRequested();
            if (image == null) return new(material.Text + "\n本輪未取得有效的截圖，請按已有情境自然關心主人。");
            return new(material.Text + "\n" + (captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : "本輪附圖是按桌寵前景規則取得的目前畫面，用來理解主人眼前的活動。"), AgentPromptImage.FromDataUrl(image.DataUrl), "image");
        }
        catch (IOException) { return new(material.Text + "\n本輪未取得有效的前景截圖，請按已有情境自然關心主人。"); }
    }

    void ConfigureAgent() { agent?.Configure(Preferences.AgentHarnessId, new(Preferences.AgentEnabled, Preferences.PuppetMode, Preferences.AgentDailySpeech)); if (!AgentDailyActive) nextWarmup = DateTime.MinValue; }
    void TickAgent(DateTime now)
    {
        agent?.Tick(new DateTimeOffset(now), !hidden && !fullscreenHidden && !dragging && !closed && phase != "Exit");
        if (!generating && !agentWeatherPending) loading.IsVisible = agent?.AwaitingSpeech == true && !hidden && !fullscreenHidden;
    }
    bool DispatchAgentDaily()
    {
        if (!AgentDailyActive) return false;
        nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); agent?.DailyCare(); return true;
    }
    Window? mcpInput;
    bool McpInputAvailable => Preferences.AgentEnabled || McpChatAvailable;
    // Starts whatever is switched on and is not running yet: the MCP interface and the Agent connection each by its own switch.
    void InitializeMcp()
    {
        RefreshMcpInterface();
        if (!Preferences.AgentEnabled) return;
        mcpServer ??= new ControlServer((name, args, token) => ExecuteMcp(name, args, token, true), agentPipe);
        agent ??= new AgentCoordinator(Path.Combine(Environment.CurrentDirectory, "agent"), Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp"), agentPipe, RequestAgentPermission,
            App.Args.Contains("--test-mode") ? Path.Combine(Environment.CurrentDirectory, "agent-test-workspace") : null);
        if (!HarnessCatalog.All.Any(x => x.Id == Preferences.AgentHarnessId)) Preferences.AgentHarnessId = "codex";
        agent.LocalSpeechRequested = token => Dispatcher.UIThread.InvokeAsync(() => RequestAgentLocalSpeech(token));
        agent.SpeechBrief = (kind, allowImages, token) => Dispatcher.UIThread.InvokeAsync(() => BuildAgentSpeechBrief(kind, allowImages, token));
        agent.AttachmentImage = AttachmentPromptImage;
        ConfigureAgent();
        if (AgentDailyActive) brain.Stop();
    }
    // Stops whatever has been switched off, and everything when both are off or the fish is closing.
    void StopMcp()
    {
        bool everything = closed || !Preferences.AgentEnabled && !Preferences.McpEnabled;
        consentWindow?.Close();
        if (everything || !Preferences.AgentEnabled) { agent?.Dispose(); agent = null; mcpServer?.Dispose(); mcpServer = null; }
        if (everything || !Preferences.McpEnabled) { mcpInterface?.Dispose(); mcpInterface = null; }
        if (everything) { CancelWalk("mcp_disabled"); mcpInput?.Close(); externalActions.Clear(); nextExternalText = null; }
        else if (!McpInputEnabled && !mcpInputPicking) mcpInput?.Close();
    }
    void PublishInteraction(string kind, object detail)
    {
        if (kind == "user_message") { PublishUserMessage(detail, McpChatAvailable); return; }
        DiagnosticLog.Write(DiagnosticEvent.Interaction, kind: kind);
        mcpServer?.Publish(kind, detail); mcpInterface?.Publish(kind, detail);
        if (kind == "click" && Preferences.AgentRespondToClick || kind == "drag_end" && Preferences.AgentRespondToDrag) agent?.Interaction(kind, detail);
    }
    void SetPuppet(bool enabled)
    {
        if (Preferences.PuppetMode == enabled) return;
        CancelWalk("puppet_mode");
        Preferences.PuppetMode = enabled; speechVersion++; if (AgentDailyActive) brain.Stop(); else brain.CancelGeneration(); loading.IsVisible = false; moveRule = null;
        if (!externalSpeech) { typing = false; bubbleWindow.Hide(); }
        ConfigureAgent(); Idle();
        if (!enabled) { nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue; }
    }
    async Task RequestAgentLocalSpeech(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Preferences.AgentEnabled || AgentDailyActive) throw new InvalidOperationException("讓魚魚自己的模型說話需要連線 Agent，並關閉 AgentDailySpeech。");
        if (!Preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower) throw new InvalidOperationException("「啟用模型說話」未開啟、尚未安裝本機模型，或已依供電設定暫停。");
        if (generating || closed || phase == "Exit" || modelInstalling || hidden || fullscreenHidden) throw new InvalidOperationException("魚魚目前正在忙或已隱藏，請稍後再讓魚魚自己的模型說話。");
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { speechVersion++; brain.CancelGeneration(); }));
        await Speak(false, true); token.ThrowIfCancellationRequested();
    }
    Dictionary<string, object?> CurrentSettings()
    {
        var result = typeof(CompanionPreferences).GetProperties()
            .Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)) && !CompanionPreferences.PrivateSettings.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(Preferences));
        result["StartupEnabled"] = StartupEnabled; return result;
    }
    async Task<object> ExecuteMcp(string name, JsonElement args, CancellationToken token, bool agentChannel)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            token.ThrowIfCancellationRequested();
            if (closed || phase == "Exit" || !McpChannelOpen(agentChannel)) throw new InvalidOperationException(agentChannel ? "Agent 連線已關閉或桌寵正在關閉。" : "MCP 介面已關閉或桌寵正在關閉。");
            switch (name)
            {
                case "get_settings": return ControlTools.Text(new { settings = CurrentSettings(), state = new { visible = IsVisible, hiddenByUser = hidden, fullscreenHidden, action = currentAction, animationPhase = phase, queuedActions = externalActions.ToArray(), queuedSpeech = nextExternalText, connections = McpConnections, mcpClients = mcpInterface?.Connections ?? 0, inputOpen = mcpInput?.IsVisible == true, inputAvailable = McpInputAvailable, agentConnected = agent?.Connected == true, agentStatus = agent?.Status, agentSession = agent?.SessionId, nextWake = agent?.NextWake, bubbleVisible = bubbleWindow.IsVisible, externalBubble = externalSpeech, speechPending = generating || agentWeatherPending, platform = OperatingSystem.IsMacOS() ? "macos" : "linux", modelAvailable = brain.ModelAvailable, modelBusy = brain.Busy, modelStatus = brain.Status, fullscreen = CompanionNative.IsFullscreen(), idleSeconds = Math.Round(ReminderIdle), activeMinutes = ReminderActiveMinutes, lastBreakMinutes = ReminderLastBreakMinutes, walk = WalkSnapshot(), tour = TourSnapshot(), lastWalk, walkBubble = WalkBubbleShown } });
                case "update_settings": await UpdateSettings(args, token, agentChannel); return ControlTools.Text(new { settings = CurrentSettings() });
                case "request_local_speech": await RequestAgentLocalSpeech(token); return ControlTools.Text(new { generated = modelReply && bubbleWindow.IsVisible });
                case "list_actions": return ControlTools.Text(policy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground": case "capture_screen":
                    var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部顯示器組成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await LinuxCapture.CaptureAsync(handle, limit, name == "capture_screen", token);
                    token.ThrowIfCancellationRequested();
                    if (!McpChannelOpen(agentChannel)) throw new OperationCanceledException();
                    return ControlTools.Image(image.DataUrl, CaptureMetadata(name, identity, image, handle));
                case "play_action": case "interrupt_action":
                    string asked = ControlTools.RequiredText(args, "action", 200), action = CompanionActionNames.Resolve(policy.Rules.Select(r => r.Id), asked)
                        ?? throw new ArgumentException(CompanionActionNames.Unknown(policy.Rules.Select(r => r.Id), "action", asked)); // Traditional names mean the same clip
                    if (name == "interrupt_action") { externalActions.Clear(); moveRule = null; CancelWalk("interrupt"); Play(policy.Find(action), "External"); }
                    else { if (externalActions.Count >= 100) throw new InvalidOperationException("待播放動作佇列已滿。"); externalActions.Enqueue(action); }
                    return ControlTools.Text(new { action, started = name == "interrupt_action", queued = externalActions.Count });
                case "say": case "say_next":
                    string text = ControlTools.RequiredText(args, "text");
                    bool transient = name == "say" && ControlTools.OptionalBool(args, "transient", false);
                    if (name == "say") SayFromMcp(text, agentChannel, transient); else { if (agentChannel) agent?.MarkSpoken(); nextExternalText = text; }
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                case "get_context":
                    // Asking Finder (macOS) for its windows takes a moment; the UI thread stays free meanwhile.
                    var folders = await CompanionNative.OpenFoldersAsync(token);
                    token.ThrowIfCancellationRequested();
                    return ControlTools.Text(CurrentContext(folders.Folders, folders.Note));
#if COMPANION_MAC
                case "walk_to":
                    if (agentChannel && args.ValueKind == JsonValueKind.Object && args.TryGetProperty("say", out var arrival) && arrival.ValueKind == JsonValueKind.String) agent?.MarkSpoken();
                    // Awaiting the arrival leaves the dispatcher free, so get_settings and say keep working meanwhile.
                    return ControlTools.Text(await WalkToMcp(args, token));
                case "find_on_screen": return ControlTools.Text(await FindOnScreenMcp(args, token));
#else
                case "walk_to": case "find_on_screen": throw new NotSupportedException($"Linux 版不支援 {name}。"); // Not offered on Linux (ControlTools.Definitions).
#endif
                case "get_operation_guide": case "get_wakeups": case "schedule_wakeup": case "cancel_wakeup":
                    // The built-in Agent's skill, persona and care reservations are its own, never the MCP interface's.
                    if (!agentChannel) throw new InvalidOperationException("這個工具只供內建的 Agent 連線使用。");
                    return ControlTools.Text(agent?.ExecuteTool(name, args) ?? throw new InvalidOperationException("Agent 尚未連線。"));
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        });
    }
    async Task UpdateSettings(JsonElement args, CancellationToken token, bool agentChannel)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必須為物件。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            // A refused field is named, never echoed with its value.
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException(ControlTools.RefusedSetting(property.Name, CompanionPreferences.PrivateSettings.Contains(property.Name)));
            var type = property.Name == "StartupEnabled" ? typeof(bool) : typeof(CompanionPreferences).GetProperty(property.Name)!.PropertyType;
            values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), type);
        }
        foreach (var item in values)
        {
            bool Range(double low, double high) => item.Value is IConvertible c && double.IsFinite(c.ToDouble(null)) && c.ToDouble(null) >= low && c.ToDouble(null) <= high;
            bool valid = item.Key switch {
                "Scale" or "BubbleScale" => Range(.5, 2), "AnimationIntervalSeconds" => Range(30, 300),
                "BreakfastHour" or "LunchHour" or "DinnerHour" => Range(0, 23), "BubbleDurationIndex" => Range(0, 7),
                "SpeechMinMinutes" => item.Value is int minutes && CompanionPreferences.SpeechRanges.Any(r => r.Min == minutes),
                "SitReminderMinutes" => item.Value is int sit && CompanionPreferences.SitReminderChoices.Contains(sit),
                "LateReminderHour" => item.Value is int late && CompanionPreferences.LateReminderChoices.Contains(late),
                "WelcomeBackMinutes" => item.Value is int away && CompanionPreferences.WelcomeBackChoices.Contains(away),
                "EventReminderMinutes" => item.Value is int ahead && CompanionPreferences.EventReminderChoices.Contains(ahead),
                // 臺灣 townships and China's 台湾省 alike; a 臺灣 township uses CWA when macOS has an authorization code, else the estimate.
                "WeatherRegionCode" => item.Value is string region && CompanionRegions.District(region).Code == region,
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000), _ => item.Value is bool };
            if (!valid) throw new ArgumentException("設定值無效：" + item.Key);
        }
        // One switch, named as each side names it: the Agent connection hands the care and actions to the Agent, the MCP interface
        // calls it 提線木偶模式.
        if (values.TryGetValue("PuppetMode", out var puppet) && puppet is true && !Preferences.PuppetMode
            && !await Confirm(agentChannel ? ControlTools.BehaviorControlTitle : ControlTools.McpPuppetTitle, agentChannel ? ControlTools.PuppetWarning : ControlTools.McpPuppetWarning, "瞭解並開啟", token))
            throw new OperationCanceledException(agentChannel ? "使用者取消讓 Agent 安排日常關心與動作。" : "使用者取消開啟提線木偶模式。");
        token.ThrowIfCancellationRequested(); if (!McpChannelOpen(agentChannel)) throw new OperationCanceledException();
        foreach (var item in values)
            if (item.Key == "StartupEnabled") SetStartup((bool)item.Value!);
            else if (item.Key != "PuppetMode") typeof(CompanionPreferences).GetProperty(item.Key)!.SetValue(Preferences, item.Value);
        if (values.TryGetValue("PuppetMode", out puppet)) SetPuppet((bool)puppet!);
        var position = Position;
        if (values.TryGetValue("Left", out var left) && left is double x) position = new((int)x, position.Y);
        if (values.TryGetValue("Top", out var top) && top is double y) position = new(position.X, (int)y);
        ApplySettings(position);
        ConfigureAgent();
        if (!McpInputEnabled) mcpInput?.Close();
        refreshLinuxSettings?.Invoke();
    }
    bool TryOpenMcpInput()
    {
        if (!McpInputAvailable || closed || phase == "Exit") return false;
        if (mcpInput != null) { mcpInput.Show(this); mcpInput.Activate(); return true; }
        const string hint = "透過魚魚和已連線的Agent說話";
        var text = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxLength = 4000,
            Height = 56, Padding = new Thickness(0, 2, 22, 0), FontSize = 14, Foreground = Hair,
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), PlaceholderText = hint };
        Avalonia.Automation.AutomationProperties.SetName(text, hint);
        var editor = new Grid(); editor.Children.Add(text);
        string ready = McpSendHint;
        const string sentToAgent = "已傳送給 Agent", sentToInterface = "已傳送給 MCP 介面（Hermes 橋接程式）";
        var status = new TextBlock { Text = ready, Foreground = Avalonia.Media.Brushes.SlateGray, FontSize = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(editor);
        var frame = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 251, 244)),
            BorderBrush = Hair, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = panel };
        var dialog = new Window { Title = CompanionEdition.AppName + " · 輸入", Width = 354, Height = 120,
            WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Avalonia.Media.Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = frame };
        var close = new Button { Content = "×", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Foreground = Hair, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        Avalonia.Controls.ToolTip.SetTip(close, "收起輸入框"); close.Click += (_, _) => dialog.Close(); editor.Children.Add(close);
        var tray = OpenMcpAttachments(dialog, frame, panel, text, status, ready);
        // Exactly one side gets the message: the MCP interface while the input is handed to it and a client is there, else the Agent.
        void Send()
        {
            if (!CanSend()) return;
            var attachments = tray.Peek(out int missing);
            string typed = text.Text ?? "";
            if (attachments.Length == 0 && string.IsNullOrWhiteSpace(typed)) { tray.Clear(); status.Text = "附件已不存在，沒有傳送。"; return; }
            bool toInterface = McpChatAvailable;
            // A refused message stays in the box with its attachments.
            if (!toInterface && (agent?.Connected != true || !agent.SendUser(typed, attachments))) { status.Text = "Agent 目前無法接收，請稍後再試。"; return; }
            // Without attachments the event stays exactly { text }, as older clients expect.
            PublishUserMessage(attachments.Length == 0 ? new { text = typed } : new { text = typed, attachments }, toInterface);
            tray.Clear(); text.Text = "";
            status.Text = (toInterface ? sentToInterface : sentToAgent) + (attachments.Length == 0 ? "" : $"（含 {attachments.Length} 個附件）")
                + (missing > 0 ? $"，{missing} 個檔案已不存在" : ""); text.Focus();
        }
        bool CanSend() => (McpChatAvailable || agent?.Connected == true) && tray.Saving == 0 && (!string.IsNullOrWhiteSpace(text.Text) || tray.Items.Count > 0);
        var send = ActionButton("傳送", Send); send.MinWidth = 58; send.Padding = new Thickness(12, 5); send.FontSize = 12;
        var attach = McpAttachButton(dialog, text, tray);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; actions.Children.Add(attach); actions.Children.Add(status); actions.Children.Add(send); Grid.SetColumn(status, 1); Grid.SetColumn(send, 2); panel.Children.Add(actions);
        text.TextChanged += (_,_) => send.IsEnabled = CanSend();
        dialog.AddHandler(InputElement.KeyDownEvent, (_,e) => {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); }
            else if (IsMcpSendKey(e)) { e.Handled = true; Send(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        bool positioning = false;
        void Follow()
        {
            if (positioning || mcpInput != dialog) return;
            positioning = true;
            try {
                if (!IsVisible) { if (!mcpInputPicking) dialog.Hide(); return; }
                double scale = DesktopScaling;
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
                dialog.Width = Math.Min(354, Math.Max(160, area.Width / scale - 16));
                int width = (int)Math.Ceiling(dialog.Width * scale), height = (int)Math.Ceiling(dialog.Height * scale), gap = (int)(8 * scale);
                int x = Position.X + (int)(SpriteSize * 208 / 360 * scale) - width - gap;
                if (x < area.X + gap) x = Position.X + (int)(SpriteSize * 428 / 360 * scale) + gap;
                int y = Position.Y + (int)(SpriteSize * 336 / 360 * scale) - height;
                (x, y) = AvoidWalkTarget(x, y, width, height);
                dialog.Position = new(Math.Clamp(x, area.X + gap, Math.Max(area.X + gap, area.Right - width - gap)),
                    Math.Clamp(y, area.Y + gap, Math.Max(area.Y + gap, area.Bottom - height - gap)));
                if (!dialog.IsVisible) dialog.Show(this);
            } finally { positioning = false; }
        }
        EventHandler<PixelPointEventArgs> moved = (_, _) => Follow();
        EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, e) => {
            if (e.Property == IsVisibleProperty || e.Property == WidthProperty || e.Property == HeightProperty) Follow();
        };
        PositionChanged += moved; PropertyChanged += changed;
        // The attachment row adds its own height under the editor.
        tray.Changed = () => { dialog.Height = tray.Items.Count > 0 ? 184 : 120; send.IsEnabled = CanSend(); Follow(); };
        // What the clock itself last wrote: attachment notices and send results stay until the connection state changes.
        string? shown = null;
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_,_) => {
            if (!McpInputEnabled) { if (!mcpInputPicking) dialog.Close(); return; }
            Follow();
            send.IsEnabled = CanSend();
            // Nothing can take a message: say why, keeping the draft and its attachments for later.
            bool connected = agent?.Connected == true;
            string? state = McpChatAvailable ? null : connected ? (agent!.Busy ? "Agent 正在處理…" : null)
                : Preferences.AgentEnabled ? agent?.Status ?? "Agent 尚未連線" : "MCP 介面目前沒有客戶端連線";
            bool blocked = !connected && !McpChatAvailable;
            if (state != null && (blocked || status.Text == shown || status.Text == ready)) { status.Text = state; shown = state; }
            else if (state == null && shown != null) { if (status.Text == shown) status.Text = ready; shown = null; }
        };
        mcpInput = dialog; mcpTray = tray;
        dialog.Closed += (_,_) => { clock.Stop(); PositionChanged -= moved; PropertyChanged -= changed; tray.Discard(); if (mcpTray == tray) mcpTray = null; mcpInput = null; };
        dialog.Opened += (_,_) => { text.Focus(); send.IsEnabled = CanSend(); };
        dialog.Opened += (_, _) => ConfigurePlatformWindow(dialog);
        clock.Start(); Follow(); dialog.Activate(); return true;
    }
#endif
}
