// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
#if !COMPANION_SAFE
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BigBlueFish.Agent;
using BigBlueFish.Control;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    // The 連線 Agent page: a local CLI harness over ACP, with its workspace, options, daily care and persona.
    private Action AddAgentConnectionSettings(StackPanel panel, Window owner)
    {
        var enabled = new CheckBox { Content = "連線本機相容的 Agent 命令列工具", IsChecked = preferences.AgentEnabled, Margin = new(0, 12, 0, 5) };
        panel.Children.Add(enabled);
        panel.Children.Add(ModernUI.Text("透過已安裝的 Agent 與魚魚對話，並控制魚魚的行為。"));
        var extra = new StackPanel { Visibility = preferences.AgentEnabled ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "選擇 Agent", FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 8) });
        var select = new ComboBox { MinHeight = 38, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool updating = false;
        void Detect()
        {
            updating = true; select.Items.Clear();
            foreach (var h in HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null))
            {
                var item = new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? h.Id == "antigravity" ? " · 連線時準備官方元件" : " · 未偵測到" : " · 已偵測到"), Tag = h.Id };
                select.Items.Add(item); if (h.Id == preferences.AgentHarnessId) select.SelectedItem = item;
            }
            updating = false;
        }
        Detect(); extra.Children.Add(select);
        var detectRow = new WrapPanel();
        detectRow.Children.Add(ModernUI.Button("重新偵測", Detect));
        detectRow.Children.Add(ModernUI.Button("安裝與登入說明", () => {
            Process.Start(new ProcessStartInfo(HarnessCatalog.Get(preferences.AgentHarnessId).Website) { UseShellExecute = true });
        }));
        extra.Children.Add(detectRow);
        // Claude Code, Codex CLI and Gemini CLI only: the CLI the fish starts runs on the cloud AI service's API Key of its
        // company (ApiKeyLaunch; AgentClaudeApiKey, AgentOpenAiApiKey, AgentGeminiApiKey), shown for the selected harness.
        string agentRoot = Path.Combine(Environment.CurrentDirectory, "agent");
        var apiPanel = new StackPanel();
        var apiKey = new CheckBox { Margin = new(0, 12, 0, 5) };
        var apiDescription = ModernUI.Text(""); var apiModel = ModernUI.Text(""); var apiKeyState = ModernUI.Text("");
        apiPanel.Children.Add(apiKey); apiPanel.Children.Add(apiDescription); apiPanel.Children.Add(apiModel); apiPanel.Children.Add(apiKeyState);
        apiPanel.Children.Add(ModernUI.Button("前往雲端 AI 服務", () => { if (companionSettings is { } open) BuildCompanionSettingsContent(open, ModelSettingsPage); }));
        extra.Children.Add(apiPanel);
        var state = new TextBlock { Foreground = ModernUI.Blue, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 5) };
        var description = ModernUI.Text(""); extra.Children.Add(state); extra.Children.Add(description);
        var actions = new WrapPanel();
        actions.Children.Add(ModernUI.Button("重新連線", () => agent?.Reconnect()));
        actions.Children.Add(ModernUI.Button("開始新對話", async () => {
            if (await ConfirmMcp(owner, "開始新對話", "將建立新的魚魚對話，之後的訊息使用新的上下文。原對話由 Agent 自行保留。", accept: "開始新對話")) agent?.NewConversation();
        }));
        actions.Children.Add(ModernUI.Button("取消目前的請求", () => agent?.Cancel()));
        extra.Children.Add(actions);
        var refreshWorkspace = AddAgentWorkspaceSettings(extra, owner);
        var refreshConfiguration = AddAgentConfigurationSettings(extra, owner);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = preferences.AgentDailySpeech, Margin = new(0, 18, 0, 5) };
        extra.Children.Add(daily); extra.Children.Add(ModernUI.Text(ControlTools.DailySpeechDescription));
        // The same PuppetMode as 提線木偶模式 on the Agent 與 MCP page.
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = preferences.PuppetMode, Margin = new(0, 10, 0, 5) };
        extra.Children.Add(puppet); extra.Children.Add(ModernUI.Text(ControlTools.BehaviorControlDescription));
        var wake = ModernUI.Text(""); extra.Children.Add(wake);
        var chatLogAll = new CheckBox { Content = "聊天記錄顯示魚魚說過的所有話", IsChecked = preferences.ChatLogShowAll, Margin = new(0, 10, 0, 5) };
        extra.Children.Add(chatLogAll); extra.Children.Add(ModernUI.Text("在輸入框上往上捲動就能看到聊天記錄。預設只顯示主人說的話，以及前後相鄰的魚魚那一句。"));
        chatLogAll.Click += (_, _) => { preferences.ChatLogShowAll = chatLogAll.IsChecked == true; SaveCompanion(); };
        extra.Children.Add(ModernUI.Button("編輯人設", () => EditAgentPersona(owner)));
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        extra.Children.Add(new Expander { Header = "連線詳情", Content = details, Margin = new(0, 10, 0, 8) });
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        static string OnOff(bool value) => value ? "開" : "關";
        ApiKeyLaunch? shownApi = null; string keyProvider = ""; string? keySeen = null; bool keyUsable = false;
        void RefreshApiKey()
        {
            var api = ApiKeyLaunch.For(preferences.AgentHarnessId);
            apiPanel.Visibility = api != null ? Visibility.Visible : Visibility.Collapsed;
            if (api == null) return;
            string provider = CompanionPreferences.AgentApiKeyProvider(api.Harness)!;
            if (api != shownApi) { shownApi = api; apiKey.Content = api.Label; apiDescription.Text = api.Description(ModelPageName); apiModel.Text = api.ModelHint(agentRoot); }
            if (apiKey.IsEnabled) apiKey.IsChecked = preferences.AgentUsesApiKey(api.Harness);
            // Whether the stored key can be used here (AgentLaunchFor gets "" for one this Windows account cannot decrypt):
            // decrypted only when the stored value changes, not every half second.
            string? stored = preferences.CloudApiKeyStored(provider);
            if (keyProvider != provider || keySeen != stored) { keyProvider = provider; keySeen = stored; keyUsable = preferences.CloudApiKey(provider).Length > 0; }
            apiKeyState.Text = !preferences.AgentUsesApiKey(api.Harness) ? api.OwnState
                : keyUsable ? api.KeySaved
                : !string.IsNullOrEmpty(keySeen) ? AgentKeyUnreadableHint(api) : api.MissingHint(CompanionCloud.Info(provider).DisplayName);
        }
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            if (select.SelectedItem is ComboBoxItem { Tag: "antigravity" } selected)
                selected.Content = "Antigravity CLI" + (adapters.FindExecutable(HarnessCatalog.Get("antigravity")) == null ? " · 連線時準備官方元件" : " · 已偵測到");
            // The other page or MCP may have changed it meanwhile; a click still being confirmed keeps its own state.
            if (puppet.IsEnabled) puppet.IsChecked = preferences.PuppetMode;
            RefreshApiKey();
            state.Text = agent?.Status ?? "尚未連線";
            description.Text = agent?.Detail ?? "選擇已安裝的 Agent，連線後雙擊魚魚即可對話。";
            string signIn = ApiKeyLaunch.ForProfile(agent?.LaunchProfile)?.SignInDetail ?? "Agent 自己的登入";
            details.Text = $"程式：{agent?.ExecutablePath ?? "尚未啟動"}\nAgent 版本：{agent?.Version}\n登入：{signIn}\n模型：{agent?.CurrentModel}\n對話編號：{agent?.SessionId}\n內部工具連線：{mcpServer?.Connections ?? 0}\n輸入會發起對話；點選回應：{OnOff(preferences.AgentRespondToClick)}；拖曳回應：{OnOff(preferences.AgentRespondToDrag)}（可由 Agent 調整）";
            wake.Text = preferences.PuppetMode && preferences.AgentDailySpeech
                ? "下一次日常關心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : preferences.PuppetMode ? "Agent 安排日常關心的時機，由模型編寫要說的話。下一次關心：" + (agent?.NextWake ?? "等待 Agent 預約")
                : preferences.AgentDailySpeech ? "魚魚決定何時關心主人，Agent 編寫關心的話。" : "魚魚按自己的節奏關心主人，日常說話使用「模型與供電」頁的模型。";
            if (preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未連線，魚魚保持待機；關閉第二個開關可恢復魚魚自己的安排。";
        }
        select.SelectionChanged += (_, _) => {
            if (updating || select.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            preferences.AgentHarnessId = id; ConfigureAgent(); SaveCompanion(); Refresh();
        };
        apiKey.Click += async (_, _) => {
            // The harness shown when the switch was clicked, even if another one is selected while the warning is open.
            if (ApiKeyLaunch.For(preferences.AgentHarnessId) is not { } api) return;
            apiKey.IsEnabled = false;
            try
            {
                bool on = apiKey.IsChecked == true;
                if (on && !await ConfirmMcp(owner, api.Label, api.Warning(agentRoot))) { apiKey.IsChecked = false; return; }
                // A new process of that CLI starts with the key, or again with its own sign-in.
                preferences.SetAgentUsesApiKey(api.Harness, on); SaveCompanion(); AgentLaunchChanged();
            }
            catch (Exception e) { apiKey.IsChecked = preferences.AgentUsesApiKey(api.Harness); ModernUI.Error(owner, "連線設定失敗", e); }
            finally { apiKey.IsEnabled = true; Refresh(); }
        };
        daily.Click += (_, _) => {
            preferences.AgentDailySpeech = daily.IsChecked == true;
            InvalidateSpeechRequests(true); ConfigureAgent(); nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom)); SaveCompanion(); Refresh();
        };
        puppet.Click += async (_, _) => {
            puppet.IsEnabled = false;
            try
            {
                bool on = puppet.IsChecked == true;
                if (on && !await ConfirmMcp(owner, ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, note: preferences.McpEnabled ? PuppetSharedWithMcp : null)) { puppet.IsChecked = false; return; }
                SetPuppetMode(on); SaveCompanion();
            }
            catch (Exception e) { puppet.IsChecked = preferences.PuppetMode; ModernUI.Error(owner, "接管設定失敗", e); }
            finally { puppet.IsEnabled = true; Refresh(); }
        };
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await ConfirmMcp(owner, "連線本機 Agent", ControlTools.PrivacyWarning)) { enabled.IsChecked = false; return; }
                // 提線木偶模式, turned on for the MCP interface, is this page's takeover too: the Agent about to connect
                // would take the care and the actions over at once, which needs the same consent as the switch below.
                if (on && preferences.PuppetMode && !await ConfirmMcp(owner, ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, note: PuppetAlreadyOn))
                { enabled.IsChecked = false; return; }
                // The fish gets its own schedule back once no agent can drive it any more.
                if (!on && !preferences.McpEnabled) SetPuppetMode(false);
                preferences.AgentEnabled = on;
                // InitializeMcp starts whatever is switched on; StopMcp stops only what is switched off, so the MCP
                // interface on the Agent 與 MCP page keeps running.
                if (on) InitializeMcp(); else StopMcp();
                extra.Visibility = on ? Visibility.Visible : Visibility.Collapsed; puppet.IsChecked = preferences.PuppetMode;
                SaveCompanion(); Refresh();
            }
            catch (Exception e) { ModernUI.Error(owner, "連線設定失敗", e); }
            finally { enabled.IsEnabled = true; }
        };
        clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }

    // Under the switch when the stored key cannot be decrypted by this Windows account (a settings file copied from another
    // computer or account).
    private static string AgentKeyUnreadableHint(ApiKeyLaunch api) => "已儲存的 " + api.KeyName + " 無法在這個 Windows 帳戶解密（設定檔可能是從別的電腦或帳戶複製過來的），"
        + api.Product + " 會先暫停（不會改用" + api.Fallback + "）。請按「前往雲端 AI 服務」重新貼上 API Key，再按「儲存並測試」。";

    private void EditAgentPersona(Window owner)
    {
        if (agent == null) return;
        var skills = agent.Skills; skills.Install();
        var dialog = new ModernWindow("魚魚人設", "魚魚人設", "儲存後，從 Agent 的下一輪訊息開始套用；可以修改或清空。", 700, 620) { Owner = owner };
        var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Body.Children.Add(editor);
        dialog.Actions.Children.Add(ModernUI.Button("儲存", () => { AtomicText.Write(skills.PersonaPath, editor.Text); dialog.Close(); }, true));
        dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close)); dialog.Show();
    }
    private async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.InvokeAsync(async () => {
            if (companionClosed || !preferences.AgentEnabled || token.IsCancellationRequested) return null;
            // The title and the buttons come from the Agent: shown in Taiwan Traditional Chinese where AgentText knows them;
            // a shell command stays exactly as it will run.
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 要求執行操作" : AgentText.LocalizeTitle(request.Title, request.Kind);
            var dialog = new ModernWindow(request.Kind == "authentication" ? "Agent 登入驗證" : "Agent 要求權限", heading,
                request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail, 700, 540);
            if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
            string? selected = null;
            var finished = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options)
                dialog.Actions.Children.Add(ModernUI.Button(AgentText.Localize(option.Name), () => { selected = option.Id; dialog.Close(); }));
            dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close));
            dialog.Closed += (_, _) => finished.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.BeginInvoke(() => dialog.Close()));
            dialog.Show(); return await finished.Task;
        }).Task.Unwrap();
    }
}
#endif
