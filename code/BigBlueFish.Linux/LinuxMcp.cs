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
        if (!Preferences.ForegroundEnabled) return new(material.Text + "\n本轮未启用读取前台内容，按已有情境自然关心主人。");
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
                    + "\n当前 Agent 未启用图片输入，已改用本地文字识别提供前台内容。", null, "ocr");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: "ocr", error: error);
                return new(material.Text + "\n本轮未取得可用读屏内容，按已有情境自然关心主人。");
            }
        }
        try
        {
            var captured = await CompanionCaptureFallback.ReadAsync<CompanionWindowImage>(
                async () => await LinuxCapture.CaptureAsync(handle, 2560, false, token),
                async () => await LinuxCapture.CaptureAsync(handle, 2560, true, token), token);
            var image = captured.Image;
            token.ThrowIfCancellationRequested();
            if (image == null) return new(material.Text + "\n本轮未取得有效截图，按已有情境自然关心主人。");
            return new(material.Text + "\n" + (captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : "本轮附图是按桌宠前台规则取得的当前界面，用于理解主人眼前的活动。"), AgentPromptImage.FromDataUrl(image.DataUrl), "image");
        }
        catch (IOException) { return new(material.Text + "\n本轮未取得有效前台截图，按已有情境自然关心主人。"); }
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
        if (everything) { mcpInput?.Close(); externalActions.Clear(); nextExternalText = null; }
        else if (!McpInputEnabled) mcpInput?.Close();
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
        Preferences.PuppetMode = enabled; speechVersion++; if (AgentDailyActive) brain.Stop(); else brain.CancelGeneration(); loading.IsVisible = false; moveRule = null;
        if (!externalSpeech) { typing = false; bubbleWindow.Hide(); }
        ConfigureAgent(); Idle();
        if (!enabled) { nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue; }
    }
    async Task RequestAgentLocalSpeech(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Preferences.AgentEnabled || AgentDailyActive) throw new InvalidOperationException("本地吐字需要连接 Agent 并关闭 AgentDailySpeech。");
        if (!Preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower) throw new InvalidOperationException("本地模型未启用、尚未安装或已按供电设置暂停。");
        if (generating || closed || phase == "Exit" || modelInstalling || hidden || fullscreenHidden) throw new InvalidOperationException("鱼鱼当前忙碌或隐藏，请稍后再触发本地吐字。");
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
            if (closed || phase == "Exit" || !McpChannelOpen(agentChannel)) throw new InvalidOperationException(agentChannel ? "Agent 連線已關閉或桌寵正在退出。" : "MCP 介面已關閉或桌寵正在退出。");
            switch (name)
            {
                case "get_settings": return ControlTools.Text(new { settings = CurrentSettings(), state = new { visible = IsVisible, hiddenByUser = hidden, fullscreenHidden, action = currentAction, animationPhase = phase, queuedActions = externalActions.ToArray(), queuedSpeech = nextExternalText, connections = McpConnections, mcpClients = mcpInterface?.Connections ?? 0, inputOpen = mcpInput?.IsVisible == true, inputAvailable = McpInputAvailable, agentConnected = agent?.Connected == true, agentStatus = agent?.Status, agentSession = agent?.SessionId, nextWake = agent?.NextWake, bubbleVisible = bubbleWindow.IsVisible, externalBubble = externalSpeech, speechPending = generating || agentWeatherPending, walkBubble = false, fullscreen = CompanionNative.IsFullscreen(), idleSeconds = Math.Round(CompanionNative.IdleSeconds), platform = OperatingSystem.IsMacOS() ? "macos" : "linux", modelAvailable = brain.ModelAvailable, modelBusy = brain.Busy, modelStatus = brain.Status } });
                case "update_settings": await UpdateSettings(args, token, agentChannel); return ControlTools.Text(new { settings = CurrentSettings() });
                case "request_local_speech": await RequestAgentLocalSpeech(token); return ControlTools.Text(new { generated = modelReply && bubbleWindow.IsVisible });
                case "list_actions": return ControlTools.Text(policy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground": case "capture_screen":
                    var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部显示器组成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await LinuxCapture.CaptureAsync(handle, limit, name == "capture_screen", token);
                    token.ThrowIfCancellationRequested();
                    if (!McpChannelOpen(agentChannel)) throw new OperationCanceledException();
                    return ControlTools.Image(image.DataUrl, new { target = name, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow });
                case "play_action": case "interrupt_action":
                    string action = ControlTools.RequiredText(args, "action", 200);
                    if (!policy.Rules.Any(r => r.Id == action)) throw new ArgumentException("动作不存在。");
                    if (name == "interrupt_action") { externalActions.Clear(); moveRule = null; Play(policy.Find(action), "External"); }
                    else { if (externalActions.Count >= 100) throw new InvalidOperationException("待播放动作队列已满。"); externalActions.Enqueue(action); }
                    return ControlTools.Text(new { action, started = name == "interrupt_action", queued = externalActions.Count });
                case "say": case "say_next":
                    string text = ControlTools.RequiredText(args, "text");
                    bool transient = name == "say" && ControlTools.OptionalBool(args, "transient", false);
                    if (name == "say") SayFromMcp(text, agentChannel, transient); else { if (agentChannel) agent?.MarkSpoken(); nextExternalText = text; }
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                case "walk_to": case "find_on_screen": case "get_context": throw new NotSupportedException($"Linux 版尚不支援 {name}。"); // The tool list is shared with Windows.
                case "get_operation_guide": case "get_wakeups": case "schedule_wakeup": case "cancel_wakeup":
                    // The built-in Agent's skill, persona and care reservations are its own, never the MCP interface's.
                    if (!agentChannel) throw new InvalidOperationException("這個工具只供內建的 Agent 連線使用。");
                    return ControlTools.Text(agent?.ExecuteTool(name, args) ?? throw new InvalidOperationException("Agent 尚未连接。"));
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        });
    }
    async Task UpdateSettings(JsonElement args, CancellationToken token, bool agentChannel)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必须为对象。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException(property.Name == "McpEnabled" ? "MCP 接口只能由用户在设置中开启或关闭。" : "未知设置：" + property.Name);
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
                // Taiwan townships too: without a CWA authorization code (Linux has none) they get the Open-Meteo estimate.
                "WeatherRegionCode" => item.Value is string region && CompanionRegions.District(region).Code == region,
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000), _ => item.Value is bool };
            if (!valid) throw new ArgumentException("设置值无效：" + item.Key);
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
        const string hint = "通过鱼鱼和已连接的Agent说话";
        var text = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxLength = 4000,
            Height = 56, Padding = new Thickness(0, 2, 22, 0), FontSize = 14, Foreground = Hair,
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), PlaceholderText = hint };
        Avalonia.Automation.AutomationProperties.SetName(text, hint);
        var editor = new Grid(); editor.Children.Add(text);
        const string ready = "Ctrl+Enter 傳送", sentToAgent = "已傳送給 Agent", sentToInterface = "已傳送給 MCP 介面（Hermes 橋接程式）";
        var status = new TextBlock { Text = ready, Foreground = Avalonia.Media.Brushes.SlateGray, FontSize = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(editor);
        var frame = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 251, 244)),
            BorderBrush = Hair, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = panel };
        var dialog = new Window { Title = "蓝色大肥鱼 · 输入", Width = 354, Height = 120,
            WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Avalonia.Media.Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = frame };
        var close = new Button { Content = "×", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Foreground = Hair, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        Avalonia.Controls.ToolTip.SetTip(close, "收起输入框"); close.Click += (_, _) => dialog.Close(); editor.Children.Add(close);
        // Exactly one side gets the message: the MCP interface while the input is handed to it and a client is there, else the Agent.
        void Send()
        {
            if (string.IsNullOrWhiteSpace(text.Text)) return;
            bool toInterface = McpChatAvailable;
            if (!toInterface && (agent?.Connected != true || !agent.SendUser(text.Text))) return;
            PublishUserMessage(new { text = text.Text }, toInterface);
            text.Text = ""; status.Text = toInterface ? sentToInterface : sentToAgent; text.Focus();
        }
        bool CanSend() => (McpChatAvailable || agent?.Connected == true) && !string.IsNullOrWhiteSpace(text.Text);
        var send = ActionButton("发送", Send); send.MinWidth = 58; send.Padding = new Thickness(12, 5); send.FontSize = 12;
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; actions.Children.Add(status); actions.Children.Add(send); Grid.SetColumn(send, 1); panel.Children.Add(actions);
        text.TextChanged += (_,_) => send.IsEnabled = CanSend();
        dialog.AddHandler(InputElement.KeyDownEvent, (_,e) => {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); }
            else if(e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; Send(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        bool positioning = false;
        void Follow()
        {
            if (positioning || mcpInput != dialog) return;
            positioning = true;
            try {
                if (!IsVisible) { dialog.Hide(); return; }
                double scale = DesktopScaling;
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
                dialog.Width = Math.Min(354, Math.Max(160, area.Width / scale - 16));
                int width = (int)Math.Ceiling(dialog.Width * scale), height = (int)Math.Ceiling(dialog.Height * scale), gap = (int)(8 * scale);
                int x = Position.X + (int)(SpriteSize * 208 / 360 * scale) - width - gap;
                if (x < area.X + gap) x = Position.X + (int)(SpriteSize * 428 / 360 * scale) + gap;
                int y = Position.Y + (int)(SpriteSize * 336 / 360 * scale) - height;
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
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_,_) => {
            if (!McpInputEnabled) { dialog.Close(); return; }
            Follow();
            send.IsEnabled = CanSend();
            if (McpChatAvailable) { if (status.Text != sentToInterface) status.Text = ready; }
            else if (agent?.Connected != true) status.Text = Preferences.AgentEnabled ? agent?.Status ?? "Agent 尚未連線" : "MCP 介面目前沒有客戶端連線";
            else if (status.Text != sentToAgent) status.Text = agent.Busy ? "Agent 正在處理…" : ready;
        };
        mcpInput = dialog; dialog.Closed += (_,_) => { clock.Stop(); PositionChanged -= moved; PropertyChanged -= changed; mcpInput = null; }; dialog.Opened += (_,_) => { text.Focus(); send.IsEnabled = false; };
        clock.Start(); Follow(); dialog.Activate(); return true;
    }
#endif
}
