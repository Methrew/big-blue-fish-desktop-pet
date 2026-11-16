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
        // Claude Code, Codex CLI and Gemini CLI only: the CLI the fish starts runs on the cloud AI service's API Key of its
        // company (ApiKeyLaunch; AgentClaudeApiKey, AgentOpenAiApiKey, AgentGeminiApiKey), shown for the selected harness.
        string agentRoot = Path.Combine(Environment.CurrentDirectory, "agent");
        var apiPanel = new StackPanel { Spacing = 8 };
        var apiKey = new CheckBox();
        var apiDescription = Paragraph(""); var apiKeyState = Paragraph("");
        // The model hint can be selected and copied: pasted, its example keeps the straight quotes JSON needs.
        var apiModel = new SelectableTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 4, 0, 12), Foreground = Avalonia.Media.Brushes.SlateGray };
        // Claude Code and Gemini CLI: the CLI's own settings file (a model, for one), shown in Finder or the file manager.
        var openSettings = ActionButton(ApiKeyLaunch.OpenSettingsLabel, () => {
            if (ApiKeyLaunch.For(Preferences.AgentHarnessId) is not { EditableSettings: true } api) return;
            try { RevealFile(api.EnsureSettingsFile(agentRoot)); }
            catch (Exception error) { AgentError("無法開啟設定檔資料夾", error); }
        });
        apiPanel.Children.Add(apiKey); apiPanel.Children.Add(apiDescription); apiPanel.Children.Add(apiModel); apiPanel.Children.Add(openSettings); apiPanel.Children.Add(apiKeyState);
        apiPanel.Children.Add(ActionButton("前往雲端 AI 服務", () => showModelPage?.Invoke()));
        extra.Children.Add(apiPanel);
        ApiKeyLaunch? shownApi = null;
        void RefreshApiKey()
        {
            var api = ApiKeyLaunch.For(Preferences.AgentHarnessId);
            apiPanel.IsVisible = api != null;
            if (api == null) return;
            if (api != shownApi) { shownApi = api; apiKey.Content = api.Label; apiDescription.Text = api.Description(ModelPageName); apiModel.Text = api.ModelHint(agentRoot); openSettings.IsVisible = api.EditableSettings; }
            if (apiKey.IsEnabled) apiKey.IsChecked = Preferences.AgentUsesApiKey(api.Harness);
            string provider = CompanionPreferences.AgentApiKeyProvider(api.Harness)!;
            string missing = api.MissingHint(CompanionCloud.Info(provider).DisplayName);
            apiKeyState.Text = !Preferences.AgentUsesApiKey(api.Harness) ? api.OwnState
#if COMPANION_MAC
                : !macSecretsLoaded ? "正在讀取 macOS 鑰匙圈…"
                // In the order AgentLaunchFor (LinuxMcp) decides: a keychain that did not answer is not a missing key.
                : Preferences.CloudApiKey(provider).Length == 0 ? SecretNote(CompanionPreferences.CloudAccount(provider), api.KeyName) ?? missing
#endif
                : Preferences.CloudApiKey(provider).Length > 0 ? api.KeySaved : missing;
        }
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
            RefreshApiKey();
            state.Text = (agent?.Status ?? "尚未連線") + "\n" + (agent?.Detail ?? "選擇後連線，雙擊魚魚即可對話。");
            string signIn = ApiKeyLaunch.ForProfile(agent?.LaunchProfile)?.SignInDetail ?? "Agent 自己的登入";
            details.Text = $"程式：{agent?.ExecutablePath}\n版本：{agent?.Version}\n登入：{signIn}\n模型：{agent?.CurrentModel}\n對話編號：{agent?.SessionId}\n內部工具連線：{mcpServer?.Connections ?? 0}\n點選回應：{(Preferences.AgentRespondToClick ? "開" : "關")}；拖曳回應：{(Preferences.AgentRespondToDrag ? "開" : "關")}（可由 Agent 調整）";
            wake.Text = Preferences.PuppetMode && Preferences.AgentDailySpeech ? "下一次日常關心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : Preferences.PuppetMode ? "Agent 安排日常關心的時機，由模型編寫要說的話。下一次關心：" + (agent?.NextWake ?? "等待 Agent 預約") : Preferences.AgentDailySpeech ? "魚魚決定何時關心主人，Agent 編寫關心的話。" : "魚魚按自己的節奏關心主人，日常說話使用魚魚自己的模型。";
            if (Preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未連線，魚魚保持待機；關閉第二個開關可恢復魚魚自己的安排。";
        }
        select.SelectionChanged += (_, _) => { if (!refreshing && select.SelectedItem is ComboBoxItem { Tag: string id }) { Preferences.AgentHarnessId = id; ConfigureAgent(); Save(); RefreshApiKey(); } };
        daily.Click += (_, _) => { Preferences.AgentDailySpeech = daily.IsChecked == true; ApplySettings(); Refresh(); };
        apiKey.Click += async (_, _) => {
            // The harness shown when the switch was clicked, even if another one is selected while the warning is open.
            if (ApiKeyLaunch.For(Preferences.AgentHarnessId) is not { } api) return;
            apiKey.IsEnabled = false;
            try
            {
                bool on = apiKey.IsChecked == true;
                if (on && !await Confirm(api.Label, api.Warning(agentRoot), "瞭解並開啟")) { apiKey.IsChecked = false; return; }
                // A new process of that CLI starts with the key, or again with its own sign-in.
                Preferences.SetAgentUsesApiKey(api.Harness, on); Save(); AgentLaunchChanged();
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
