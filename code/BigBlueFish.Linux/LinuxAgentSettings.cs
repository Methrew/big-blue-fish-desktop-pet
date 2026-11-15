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
        var enabled = new CheckBox { Content = "连接本机兼容的 CLI Harness", IsChecked = Preferences.AgentEnabled };
        body.Children.Add(enabled); body.Children.Add(Paragraph("通过已安装的 Agent 与鱼鱼对话，并控制鱼鱼的行为。"));
        var extra = new StackPanel { IsVisible = Preferences.AgentEnabled, Spacing = 8 }; body.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "选择 Harness" });
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool refreshing = false;
        void Detect()
        {
            refreshing = true;
            select.ItemsSource = HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null)
                .Select(h => new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? h.Id == "antigravity" ? " · 连接时准备官方组件" : " · 未检测到" : " · 已检测到"), Tag = h.Id }).ToArray();
            select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string?)x.Tag == Preferences.AgentHarnessId);
            refreshing = false;
        }
        Detect(); extra.Children.Add(select);
        var buttons = new WrapPanel(); buttons.Children.Add(ActionButton("重新检测", Detect));
        buttons.Children.Add(ActionButton("安装与登录说明", () => OpenUrl(HarnessCatalog.Get(Preferences.AgentHarnessId).Website)));
        extra.Children.Add(buttons);
        var state = Paragraph(""); extra.Children.Add(state);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("重新连接", () => agent?.Reconnect()));
        actions.Children.Add(ActionButton("开始新对话", async () => { if (await Confirm("开始新对话", "将建立新的鱼鱼对话，后续使用新的上下文。")) agent?.NewConversation(); }));
        actions.Children.Add(ActionButton("取消当前请求", () => agent?.Cancel())); extra.Children.Add(actions);
        var refreshWorkspace = BuildAgentWorkspaceSettings(extra);
        var refreshConfiguration = BuildAgentConfigurationSettings(extra);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = Preferences.AgentDailySpeech }; extra.Children.Add(daily);
        extra.Children.Add(Paragraph(ControlTools.DailySpeechDescription));
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet);
        extra.Children.Add(Paragraph(ControlTools.BehaviorControlDescription));
        var wake = Paragraph(""); extra.Children.Add(wake);
        extra.Children.Add(ActionButton("编辑人设", () => {
            if (agent == null) return;
            var skills = agent.Skills; skills.Install();
            var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 320 };
            var content = new StackPanel { Spacing = 15 }; content.Children.Add(Paragraph("保存后在下一轮消息中应用，可修改或清空。")); content.Children.Add(editor);
            var window = Dialog("鱼鱼人设", content, 700, 560);
            content.Children.Add(ActionButton("保存", () => { AtomicText.Write(skills.PersonaPath, editor.Text ?? ""); window.Close(); })); window.Show(settings!);
        }));
        var details = Paragraph(""); extra.Children.Add(new Expander { Header = "连接详情", Content = details });
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            if (select.SelectedItem is ComboBoxItem { Tag: "antigravity" } selected)
                selected.Content = "Antigravity CLI" + (adapters.FindExecutable(HarnessCatalog.Get("antigravity")) == null ? " · 连接时准备官方组件" : " · 已检测到");
            state.Text = (agent?.Status ?? "尚未连接") + "\n" + (agent?.Detail ?? "选择后连接，双击鱼鱼即可对话。");
            details.Text = $"程序：{agent?.ExecutablePath}\n版本：{agent?.Version}\n模型：{agent?.CurrentModel}\n对话编号：{agent?.SessionId}\n内部工具连接：{mcpServer?.Connections ?? 0}\n点击响应：{Preferences.AgentRespondToClick}；拖动响应：{Preferences.AgentRespondToDrag}（可由 Agent 调整）";
            wake.Text = Preferences.PuppetMode && Preferences.AgentDailySpeech ? "下一次日常关心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : Preferences.PuppetMode ? "Agent 安排日常关心的时机，本地模型负责吐字。下一次关心：" + (agent?.NextWake ?? "等待 Agent 预约") : Preferences.AgentDailySpeech ? "鱼鱼决定何时关心主人，Agent 编写关心的话。" : "鱼鱼按自己的节奏关心主人，日常说话使用内置模型。";
            if (Preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未连接，鱼鱼保持待机；关闭第二个开关可恢复鱼鱼自己的安排。";
        }
        select.SelectionChanged += (_, _) => { if (!refreshing && select.SelectedItem is ComboBoxItem { Tag: string id }) { Preferences.AgentHarnessId = id; ConfigureAgent(); Save(); } };
        daily.Click += (_, _) => { Preferences.AgentDailySpeech = daily.IsChecked == true; ApplySettings(); Refresh(); };
        puppet.Click += async (_, _) => { bool on = puppet.IsChecked == true; if (on && !await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, "了解并开启")) { puppet.IsChecked = false; return; } SetPuppet(on); Save(); Refresh(); };
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("连接本机 Agent", ControlTools.PrivacyWarning, "了解并开启")) { enabled.IsChecked = false; return; }
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
        body.Children.Add(Paragraph("開啟後，這台電腦上以你的帳號執行的其他程式（例如 Hermes 橋接程式）可以透過 BigBlueFish.Mcp 控制魚魚。上面的 Agent 連線不需要開啟這裡。"));
        var extra = new StackPanel { IsVisible = Preferences.McpEnabled, Spacing = 8 }; body.Children.Add(extra);
        var puppet = new CheckBox { Content = "提線木偶模式", IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet);
        extra.Children.Add(Paragraph("關閉魚魚自己的吐字和動作切換，完全交給外部 Agent。這和上面 Agent 連線的「" + ControlTools.BehaviorControlLabel + "」是同一個開關。"));
        var chat = new CheckBox { Content = "雙擊輸入交給 MCP 介面（Hermes 橋接程式）", IsChecked = Preferences.McpChatEnabled }; extra.Children.Add(chat);
        extra.Children.Add(Paragraph("開啟且 MCP 介面有客戶端連線時，雙擊魚魚輸入的文字只會傳給 MCP 介面，不會再送給上面的 Agent 連線，避免兩邊各回答一次；沒有客戶端連線時，仍交給 Agent 連線。"));
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
        var copy = ActionButton("複製 Hermes 的 MCP 設定", () => { });
        copy.Click += async (_, _) => {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is not { } clipboard) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, HermesMcpConfig());
            copy.Content = "已複製，請貼到 ~/.hermes/config.yaml";
        };
        extra.Children.Add(copy);
        extra.Children.Add(Paragraph("在 Hermes 設定檔的 mcp_servers 底下加入這段；名稱必須是 big_blue_fish，橋接程式才叫得到魚魚的工具。"));
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
    const string McpInterfaceRisk = "連線的程式可以截取螢幕畫面、修改魚魚的設定，並收到你在魚魚輸入框打的字。Hermes 橋接程式會把這些文字交給 Hermes，而 Hermes 可能不經詢問就以你的身分執行終端機指令、修改檔案。只在自己的電腦上開啟，並只連線你信任的程式。";
    const string PuppetSharedWithAgent = "內建的 Agent 連線也開著：提線木偶模式和上面 Agent 連線的接管是同一個開關，開啟後內建的 Agent 也會安排日常關心與動作。";

    async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            if (closed || !Preferences.AgentEnabled || token.IsCancellationRequested) return null;
            var content = new StackPanel { Spacing = 12 };
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 请求执行操作" : request.Title;
            content.Children.Add(new TextBlock { Text = heading, FontSize = 22, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(new ScrollViewer { Content = Paragraph(request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail), MaxHeight = 280 });
            var window = Dialog(request.Kind == "authentication" ? "Agent 登录认证" : "Agent 请求权限", content, 700, 500); var choices = new WrapPanel(); content.Children.Add(choices);
            string? selected = null;
            var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options) choices.Children.Add(ActionButton(option.Name, () => { selected = option.Id; window.Close(); }));
            choices.Children.Add(ActionButton("取消", window.Close));
            window.Closed += (_, _) => complete.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (settings?.IsVisible == true) window.Show(settings); else window.Show();
            return await complete.Task;
        });
    }
}
#endif
