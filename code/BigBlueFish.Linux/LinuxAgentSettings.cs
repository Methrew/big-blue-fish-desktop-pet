// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
#if !COMPANION_SAFE
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using BigBlueFish.Agent;
using BigBlueFish.Control;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    void BuildAgentSettings(StackPanel body)
    {
        var enabled = new CheckBox { Content = "連線本機相容的 Agent 命令列工具", IsChecked = Preferences.AgentEnabled };
        body.Children.Add(enabled); body.Children.Add(Paragraph("透過已安裝的 Agent 與魚魚對話，並控制魚魚的行為。"));
        var extra = new StackPanel { IsVisible = Preferences.AgentEnabled, Spacing = 8 }; body.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "選擇 Agent" });
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool refreshing = false;
        void Detect()
        {
            refreshing = true;
            select.ItemsSource = HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null)
                .Select(h => new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? h.Id == "antigravity" ? " · 連線時準備官方元件" : " · 未偵測到" : " · 已偵測到"), Tag = h.Id }).ToArray();
            select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string?)x.Tag == Preferences.AgentHarnessId);
            refreshing = false;
        }
        Detect(); extra.Children.Add(select);
#if COMPANION_MAC
        // CLIs that npm, Homebrew or a version manager installed are found only once the login shell's PATH is in (MacLoginPath).
        if (!MacLoginPath.Ready.IsCompleted)
            _ = MacLoginPath.Ready.ContinueWith(_ => Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(select) != null) Detect(); }), TaskScheduler.Default);
#endif
        var buttons = new WrapPanel(); buttons.Children.Add(ActionButton("重新偵測", Detect));
        buttons.Children.Add(ActionButton("安裝與登入說明", () => OpenUrl(HarnessCatalog.Get(Preferences.AgentHarnessId).Website)));
        extra.Children.Add(buttons);
        // Claude Code only: the Claude Code the fish starts runs on the cloud AI service's Claude API Key (AgentClaudeApiKey).
        var claudeApi = new StackPanel { Spacing = 8, IsVisible = Preferences.AgentHarnessId == ClaudeApiKeyLaunch.Harness };
        var apiKey = new CheckBox { Content = AgentClaudeApiKeyLabel, IsChecked = Preferences.AgentClaudeApiKey };
        string claudeSettings = ClaudeApiKeyLaunch.SettingsFile(Path.Combine(Environment.CurrentDirectory, "agent"));
        claudeApi.Children.Add(apiKey); claudeApi.Children.Add(Paragraph(AgentClaudeApiKeyDescription)); claudeApi.Children.Add(Paragraph(AgentClaudeModelHint(claudeSettings)));
        var apiKeyState = Paragraph(""); claudeApi.Children.Add(apiKeyState);
        claudeApi.Children.Add(ActionButton("前往雲端 AI 服務", () => showModelPage?.Invoke()));
        extra.Children.Add(claudeApi);
        var state = Paragraph(""); extra.Children.Add(state);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("重新連線", () => agent?.Reconnect()));
        actions.Children.Add(ActionButton("開始新對話", async () => { if (await Confirm("開始新對話", "將建立新的魚魚對話，之後的訊息使用新的上下文。")) agent?.NewConversation(); }));
        actions.Children.Add(ActionButton("取消目前的請求", () => agent?.Cancel())); extra.Children.Add(actions);
        var refreshWorkspace = BuildAgentWorkspaceSettings(extra);
        var refreshConfiguration = BuildAgentConfigurationSettings(extra);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = Preferences.AgentDailySpeech }; extra.Children.Add(daily);
        extra.Children.Add(Paragraph(ControlTools.DailySpeechDescription));
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet);
        extra.Children.Add(Paragraph(ControlTools.BehaviorControlDescription));
        var wake = Paragraph(""); extra.Children.Add(wake);
        extra.Children.Add(ActionButton("編輯人設", () => {
            if (agent == null) return;
            var skills = agent.Skills; skills.Install();
            var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 320 };
            var content = new StackPanel { Spacing = 15 }; content.Children.Add(Paragraph("儲存後，從 Agent 的下一輪訊息開始套用；可以修改或清空。")); content.Children.Add(editor);
            var window = Dialog("魚魚人設", content, 700, 560);
            content.Children.Add(ActionButton("儲存", () => { AtomicText.Write(skills.PersonaPath, editor.Text ?? ""); window.Close(); })); window.Show(settings!);
        }));
        var details = Paragraph(""); extra.Children.Add(new Expander { Header = "連線詳情", Content = details });
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            if (select.SelectedItem is ComboBoxItem { Tag: "antigravity" } selected)
                selected.Content = "Antigravity CLI" + (adapters.FindExecutable(HarnessCatalog.Get("antigravity")) == null ? " · 連線時準備官方元件" : " · 已偵測到");
            claudeApi.IsVisible = Preferences.AgentHarnessId == ClaudeApiKeyLaunch.Harness;
            if (apiKey.IsEnabled) apiKey.IsChecked = Preferences.AgentClaudeApiKey;
            apiKeyState.Text = !Preferences.AgentClaudeApiKey ? "目前使用 Claude Code 自己的登入（例如訂閱）。"
#if COMPANION_MAC
                : !macSecretsLoaded ? "正在讀取 macOS 鑰匙圈…"
                // In the order AgentLaunchFor (LinuxMcp) decides: a keychain that did not answer is not a missing key.
                : Preferences.ClaudeApiKey.Length == 0 ? SecretNote(MacKeychain.ClaudeAccount, "Claude API Key") ?? AgentClaudeKeyMissingHint
#endif
                : Preferences.ClaudeApiKey.Length > 0 ? "已儲存 Claude API Key，魚魚啟動的 Claude Code 會用它連線。" : AgentClaudeKeyMissingHint;
            state.Text = (agent?.Status ?? "尚未連線") + "\n" + (agent?.Detail ?? "選擇後連線，雙擊魚魚即可對話。");
            string signIn = agent?.LaunchProfile == ClaudeApiKeyLaunch.Profile ? "Claude API Key（依用量計費）" : "Agent 自己的登入";
            details.Text = $"程式：{agent?.ExecutablePath}\n版本：{agent?.Version}\n登入：{signIn}\n模型：{agent?.CurrentModel}\n對話編號：{agent?.SessionId}\n內部工具連線：{mcpServer?.Connections ?? 0}\n點選回應：{(Preferences.AgentRespondToClick ? "開" : "關")}；拖曳回應：{(Preferences.AgentRespondToDrag ? "開" : "關")}（可由 Agent 調整）";
            wake.Text = Preferences.PuppetMode && Preferences.AgentDailySpeech ? "下一次日常關心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : Preferences.PuppetMode ? "Agent 安排日常關心的時機，由模型編寫要說的話。下一次關心：" + (agent?.NextWake ?? "等待 Agent 預約") : Preferences.AgentDailySpeech ? "魚魚決定何時關心主人，Agent 編寫關心的話。" : "魚魚按自己的節奏關心主人，日常說話使用魚魚自己的模型。";
            if (Preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未連線，魚魚保持待機；關閉第二個開關可恢復魚魚自己的安排。";
        }
        select.SelectionChanged += (_, _) => { if (!refreshing && select.SelectedItem is ComboBoxItem { Tag: string id }) { Preferences.AgentHarnessId = id; ConfigureAgent(); Save(); } };
        daily.Click += (_, _) => { Preferences.AgentDailySpeech = daily.IsChecked == true; ApplySettings(); Refresh(); };
        apiKey.Click += async (_, _) => {
            apiKey.IsEnabled = false;
            try
            {
                bool on = apiKey.IsChecked == true;
                if (on && !await Confirm(AgentClaudeApiKeyLabel, AgentClaudeApiKeyWarning(claudeSettings), "瞭解並開啟")) { apiKey.IsChecked = false; return; }
                // A new Claude Code process starts with the key, or again with its own login.
                Preferences.AgentClaudeApiKey = on; Save(); AgentLaunchChanged();
            }
            finally { apiKey.IsEnabled = true; Refresh(); }
        };
        puppet.Click += async (_, _) => {
            bool on = puppet.IsChecked == true;
            if (on && !await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning + (Preferences.McpEnabled ? "\n\n" + PuppetSharedWithMcp : ""), "瞭解並開啟")) { puppet.IsChecked = false; return; }
            SetPuppet(on); Save(); Refresh();
        };
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("連線本機 Agent", ControlTools.PrivacyWarning, "瞭解並開啟")) { enabled.IsChecked = false; return; }
                // Puppet mode is shared with the MCP interface and ends here only when that is off too.
                if (!on && !Preferences.McpEnabled) SetPuppet(false); Preferences.AgentEnabled = on;
                if (on) InitializeMcp(); else StopMcp();
                extra.IsVisible = on; puppet.IsChecked = Preferences.PuppetMode; Save(); Refresh();
            }
            finally { enabled.IsEnabled = true; }
        };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        clock.Tick += (_, _) => Refresh(); extra.DetachedFromVisualTree += (_, _) => clock.Stop(); Refresh(); clock.Start();
        BuildMcpInterfaceSettings(body);
    }

    internal const string AgentClaudeApiKeyLabel = "Claude Code 改用 Claude API Key（依用量計費）";
    const string AgentClaudeApiKeyDescription = "開啟後，魚魚啟動的 Claude Code 會改用「設定 › 模型與資訊」雲端 AI 服務裡儲存的 Claude API Key，由 Anthropic 依用量計費；"
        + "你自己開的 Claude Code 照樣使用訂閱登入，不受影響。Claude Code 每一輪都會送出很長的上下文，用量比魚魚自己說一句話多很多，"
        + "建議先在 Claude Console 設定支出上限。"
        + "改用 API Key 後是另一段魚魚對話，關閉後會回到原本用訂閱登入的那段；工作區的 .claude/settings.local.json 照樣適用。";
    // A model picked under 模型與權限 lasts for the current conversation only (claude-agent-acp's set_config_option changes
    // the live session); settings.json in Claude Code's own folder holds one for every conversation, the first turn included.
    static string AgentClaudeModelHint(string settingsFile) => "想一直用比較省錢的 Sonnet 或 Haiku，請在 " + settingsFile + " 寫入 {\"model\": \"sonnet\"}（或 \"haiku\"）；"
        + "已經開啟這個選項時，寫好後按「重新連線」就會套用。這個檔案只影響魚魚用 API Key 啟動的 Claude Code，第一輪和之後每一段對話都會用這個模型。"
        + "下方「模型與權限」選的模型只套用到目前這段對話，按「開始新對話」後會回到這個檔案指定的模型（沒有這個檔案時是 Claude Code 預設的模型）。";
    static string AgentClaudeApiKeyWarning(string settingsFile) => "開啟後，魚魚啟動的 Claude Code 會用你在雲端 AI 服務儲存的 Claude API Key，費用由 Anthropic 依用量向這把 API Key 的帳戶收取。"
        + "Claude Code 每一輪都會送出很長的上下文（對話、讀到的檔案、工具結果），用量比魚魚自己說一句話多很多。\n\n"
        + "建議先到 Claude Console 替這個帳戶設定支出上限。Claude Code 會用它預設的模型；想一直用比較省錢的 Sonnet 或 Haiku，請先在 " + settingsFile
        + " 寫入 {\"model\": \"sonnet\"}（或 \"haiku\"）再開啟，第一輪就會用它。「模型與權限」選的模型只套用到目前這段對話。\n\n"
        + "API Key 只交給魚魚啟動的這個 Claude Code，放在它的環境變數裡，所以它和它執行的指令也可能讀得到；工作區 .claude 資料夾裡設定檔的 env 和 hooks 也會生效，"
        + "可能把 Key 送到別的主機，所以不要把來源不明的專案當作工作區。最好另外建立一把只給魚魚用的 API Key。你自己開的 Claude Code 不受影響，照樣使用訂閱登入。";
    const string AgentClaudeKeyMissingHint = "還沒有儲存 Claude API Key，Claude Code 會先暫停（不會改用訂閱登入）。請按「前往雲端 AI 服務」，模型來源選雲端 AI 服務、服務選 Claude，"
        + "貼上 API Key 後按「儲存並測試」；之後魚魚自己說話要改回本機模型也可以，API Key 會留著。";

    // The MCP interface for programs outside the fish, such as the Hermes bridge (LinuxMcpInterface.cs), under the Agent connection.
    void BuildMcpInterfaceSettings(StackPanel body)
    {
        body.Children.Add(new TextBlock { Text = "MCP 介面（外部 Agent，例如 Hermes 橋接程式）", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 28, 0, 4) });
        var enabled = new CheckBox { Content = "開啟 MCP 介面", IsChecked = Preferences.McpEnabled };
        body.Children.Add(enabled);
        body.Children.Add(Paragraph("開啟後，這台電腦上以你的帳號執行的其他程式（例如 Hermes 橋接程式、Claude Desktop、Cursor）可以透過 BigBlueFish.Mcp 控制魚魚。上面的 Agent 連線不需要開啟這裡。"));
        var extra = new StackPanel { IsVisible = Preferences.McpEnabled, Spacing = 8 }; body.Children.Add(extra);
        // 連線其他 MCP 軟體: pick the client, see what will be copied and where it goes (LinuxMcpInterface.McpClientSetups).
        extra.Children.Add(new TextBlock { Text = "連線其他 MCP 軟體" });
        var setups = McpClientSetups();
        var client = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = setups.Select(c => c.Name).ToArray(), SelectedIndex = 0 };
        Avalonia.Automation.AutomationProperties.SetName(client, "連線其他 MCP 軟體");
        extra.Children.Add(client);
        var snippet = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontFamily = LinuxFonts.Log, FontSize = 12 };
        Avalonia.Automation.AutomationProperties.SetName(snippet, "要複製的設定");
        var copySetup = ActionButton("複製設定", () => { });
        var where = Paragraph("");
        void ShowSetup() { var setup = setups[Math.Max(0, client.SelectedIndex)]; snippet.Text = setup.Text; where.Text = setup.Where; copySetup.Content = "複製設定"; }
        client.SelectionChanged += (_, _) => ShowSetup();
        copySetup.Click += async (_, _) => {
            if (TopLevel.GetTopLevel(copySetup)?.Clipboard is not { } clipboard) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, snippet.Text ?? "");
            copySetup.Content = "已複製";
        };
        extra.Children.Add(snippet); extra.Children.Add(copySetup); extra.Children.Add(where);
        extra.Children.Add(Paragraph("設定檔裡已經有其他 MCP 伺服器時，只把 big-blue-fish（Hermes 是 big_blue_fish）這一項加進去。除了 Hermes 橋接程式，這些軟體只會呼叫工具：在它們自己的視窗輸入，就能讓魚魚說話、做動作或讀取畫面，但收不到雙擊魚魚的輸入。"));
        ShowSetup();
        var puppet = new CheckBox { Content = "提線木偶模式", IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet);
        extra.Children.Add(Paragraph("關閉魚魚自己的說話和動作切換，完全交給外部 Agent。這和上面 Agent 連線的「" + ControlTools.BehaviorControlLabel + "」是同一個開關。"));
        var chat = new CheckBox { Content = "雙擊輸入交給 MCP 介面（Hermes 橋接程式）", IsChecked = Preferences.McpChatEnabled }; extra.Children.Add(chat);
        extra.Children.Add(Paragraph("開啟且 MCP 介面有接收輸入的客戶端（例如執行中的 Hermes 橋接程式）時，雙擊魚魚輸入的文字只會傳給 MCP 介面，不會再送給上面的 Agent 連線，避免兩邊各回答一次；沒有這樣的客戶端時，仍交給 Agent 連線。只呼叫工具的客戶端（例如 Hermes 自己的 big_blue_fish 連線）不算。"));
        var briefing = new CheckBox { Content = "每天第一次坐下時講今日簡報", IsChecked = Preferences.DailyBriefing }; extra.Children.Add(briefing);
        string Away(int minutes) => minutes == 0 ? "關閉" : minutes < 60 ? $"離開 {minutes} 分鐘以上" : $"離開 {minutes / 60} 小時以上";
        string Ahead(int minutes) => minutes == 0 ? "不提醒" : $"開始前 {minutes} 分鐘";
        extra.Children.Add(new TextBlock { Text = "離開很久回來時說期間發生的事" });
        var welcome = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = CompanionPreferences.WelcomeBackChoices.Select(Away).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(CompanionPreferences.WelcomeBackChoices, Preferences.WelcomeBackMinutes)) };
        extra.Children.Add(welcome);
        extra.Children.Add(new TextBlock { Text = "Google 日曆行程開始前提醒" });
        var ahead = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = CompanionPreferences.EventReminderChoices.Select(Ahead).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(CompanionPreferences.EventReminderChoices, Preferences.EventReminderMinutes)) };
        extra.Children.Add(ahead);
        extra.Children.Add(Paragraph("簡報、回來時的摘要和行程提醒都由 Hermes 橋接程式說，沒有執行橋接程式時不會說；行程提醒需要先替 Hermes 設定好 Google 日曆。"));
        var state = Paragraph(""); extra.Children.Add(state);
        void Refresh() => state.Text = Preferences.McpEnabled ? $"已連線 {mcpInterface?.Connections ?? 0} 個 MCP 客戶端" + (McpChatAvailable ? "，雙擊輸入目前交給 MCP 介面。" : Preferences.AgentEnabled ? "，雙擊輸入目前交給 Agent 連線。" : "。") : "";
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("開啟 MCP 介面", ControlTools.McpPrivacyWarning + "\n\n" + McpInterfaceRisk, "瞭解並開啟")) { enabled.IsChecked = false; return; }
                // Puppet mode is shared with the Agent connection and ends here only when that is off too.
                if (!on && !Preferences.AgentEnabled) SetPuppet(false);
                Preferences.McpEnabled = on;
                if (on) InitializeMcp(); else StopMcp();
                extra.IsVisible = on; Save(); refreshLinuxSettings?.Invoke();
            }
            finally { enabled.IsEnabled = true; }
        };
        puppet.Click += async (_, _) => {
            bool on = puppet.IsChecked == true;
            if (on && !await Confirm(ControlTools.McpPuppetTitle, ControlTools.McpPuppetWarning + (Preferences.AgentEnabled ? "\n\n" + PuppetSharedWithAgent : ""), "瞭解並開啟")) { puppet.IsChecked = false; return; }
            SetPuppet(on); Save(); refreshLinuxSettings?.Invoke();
        };
        chat.Click += (_, _) => { Preferences.McpChatEnabled = chat.IsChecked == true; if (!McpInputEnabled) mcpInput?.Close(); Save(); Refresh(); };
        briefing.Click += (_, _) => { Preferences.DailyBriefing = briefing.IsChecked == true; Save(); };
        welcome.SelectionChanged += (_, _) => { if (welcome.SelectedIndex >= 0) { Preferences.WelcomeBackMinutes = CompanionPreferences.WelcomeBackChoices[welcome.SelectedIndex]; Save(); } };
        ahead.SelectionChanged += (_, _) => { if (ahead.SelectedIndex >= 0) { Preferences.EventReminderMinutes = CompanionPreferences.EventReminderChoices[ahead.SelectedIndex]; Save(); } };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => Refresh(); state.DetachedFromVisualTree += (_, _) => clock.Stop(); Refresh(); clock.Start();
    }
    // What the MCP interface means beyond the consent shared with Windows: Hermes runs commands without asking.
    const string McpInterfaceRisk = "連線的程式可以擷取螢幕畫面、修改魚魚的設定，並收到你在魚魚輸入框打的字。Hermes 橋接程式會把這些文字交給 Hermes，而 Hermes 可能不經詢問就以你的身分執行終端機指令、修改檔案。只在自己的電腦上開啟，並只連線你信任的程式。";
    const string PuppetSharedWithAgent = "內建的 Agent 連線也開著：提線木偶模式和上面 Agent 連線的接管是同一個開關，開啟後內建的 Agent 也會安排日常關心與動作。";
    const string PuppetSharedWithMcp = "MCP 介面也開著：這個開關和「連線 Agent」頁下方 MCP 介面的「提線木偶模式」是同一個，開啟後 MCP 介面的外部 Agent 也能接管魚魚。";

    async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            if (closed || !Preferences.AgentEnabled || token.IsCancellationRequested) return null;
            var content = new StackPanel { Spacing = 12 };
            // The title and the buttons come from the Agent: shown in Taiwan Traditional Chinese where AgentText knows them;
            // a shell command stays exactly as it will run.
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 要求執行操作" : AgentText.LocalizeTitle(request.Title, request.Kind);
            content.Children.Add(new TextBlock { Text = heading, FontSize = 22, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(new ScrollViewer { Content = Paragraph(request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail), MaxHeight = 280 });
            var window = Dialog(request.Kind == "authentication" ? "Agent 登入驗證" : "Agent 要求權限", content, 700, 500); var choices = new WrapPanel(); content.Children.Add(choices);
            string? selected = null;
            var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options) choices.Children.Add(ActionButton(AgentText.Localize(option.Name), () => { selected = option.Id; window.Close(); }));
            choices.Children.Add(ActionButton("取消", window.Close));
            window.Closed += (_, _) => complete.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (settings?.IsVisible == true) window.Show(settings); else window.Show();
            return await complete.Task;
        });
    }
}
#endif
