// Modified by acer1204 on 2026-10-30 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;
public partial class MainWindow
{
    private const string AgentConnectionPage = "連線 Agent", McpInterfacePage = "Agent 與 MCP", LogConsolePage = "記錄主控台";
    private Action? refreshCompanionSettings;
    private Action? disposeCompanionSettingsContent;
    // 模型與供電, in both editions.
    private const int ModelSettingsPage = 2;
    // The cloud panel's typed keys and models per service, until a save of the model source; dropped with the window.
    private readonly Dictionary<string, string> cloudKeyDrafts = new(), cloudModelDrafts = new();
    private Window? cloudDraftWindow;
    // The next build of the settings turns 模型來源 to the cloud AI service (after its confirmation).
    private bool cloudSettingsRequested;
#if COMPANION_OCR
    private const string CloudScreenshotLabel = "傳送前景視窗截圖（取代 OCR 文字）";
#else
    private const string CloudScreenshotLabel = "傳送前景視窗截圖";
#endif
    private void OpenCompanionSettings(int page = 0)
    {
        if (exitAnimation || companionClosed || exitReady) return;
        if (companionSettings != null)
        {
            ActivateCompanionSettings(companionSettings);
            return;
        }

        CancelWalk("settings"); CancelMovement();
        var w = new Window
        {
            Title = "大肥魚設定",
            Width = Math.Min(860, SystemParameters.WorkArea.Width - 32),
            Height = Math.Min(690, SystemParameters.WorkArea.Height - 32),
            MinWidth = Math.Min(680, SystemParameters.WorkArea.Width - 32),
            MinHeight = Math.Min(450, SystemParameters.WorkArea.Height - 32),
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 252)),
            FontFamily = new FontFamily("Microsoft JhengHei UI"),
            FontSize = 14,
            ShowInTaskbar = true,
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"))),
            ShowActivated = true,
            Topmost = false
        };
        w.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/BigBlueFish;component/Companion/CompanionSettingsStyles.xaml", UriKind.Relative) });
        companionSettings = w;
        DiagnosticLog.Write(DiagnosticEvent.SettingsOpened);
        BuildCompanionSettingsContent(w, page);
        w.Closed += (_, _) =>
        {
            disposeCompanionSettingsContent?.Invoke();
            disposeCompanionSettingsContent = refreshCompanionSettings = null;
            cloudKeyDrafts.Clear();
            cloudModelDrafts.Clear();
            cloudDraftWindow = null;
            companionSettings = null;
            DiagnosticLog.Write(DiagnosticEvent.SettingsClosed);
            SaveCompanion();
        };
        ActivateCompanionSettings(w);
    }

    private void BuildCompanionSettingsContent(Window w, int selectedPage = 0, double[]? scrollOffsets = null)
    {
        disposeCompanionSettingsContent?.Invoke();
        var shell = new Grid { Background = w.Background };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        w.Content = shell;
        var sidebar = new StackPanel { Margin = new Thickness(18, 24, 18, 18) };
        var sidebarFrame = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar };
        shell.Children.Add(sidebarFrame);
        sidebar.Children.Add(new TextBlock { Text = "藍色大肥魚", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(53, 76, 129)), Margin = new Thickness(5, 0, 0, 8) });
        var version = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
#if COMPANION_SAFE
        version += " · 無MCP版";
#endif
        sidebar.Children.Add(new TextBlock { Text = version + " · 陪伴設定", Foreground = Brushes.SlateGray, Margin = new Thickness(5, 0, 0, 28) });
#if COMPANION_SAFE
        var titles = new[] { "陪伴與外觀", "說話與天氣", "模型與供電", LogConsolePage };
#else
        // 連線 Agent is the ACP connection to a local CLI harness; Agent 與 MCP is the MCP interface for external
        // clients such as the Hermes bridge. Both can be on at once.
        var titles = new[] { "陪伴與外觀", "說話與天氣", "模型與供電", AgentConnectionPage, McpInterfacePage, LogConsolePage };
#endif
        var pages = new StackPanel[titles.Length];
        var views = new ScrollViewer[titles.Length];
        var navigation = new Button[titles.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = new StackPanel { Margin = new Thickness(26, 24, 26, 24) };
            pages[i].Children.Add(new TextBlock { Text = titles[i], FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(42, 56, 83)), Margin = new Thickness(0, 0, 0, 20) });
            views[i] = new ScrollViewer { Content = pages[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = i == selectedPage ? Visibility.Visible : Visibility.Collapsed };
            Grid.SetColumn(views[i], 1); shell.Children.Add(views[i]);
            int selected = i;
            var button = navigation[i] = new Button { Content = titles[i], HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = i == selectedPage ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent };
            button.Click += (_, _) =>
            {
                selectedPage = selected;
                for (int n = 0; n < views.Length; n++)
                { views[n].Visibility = n == selected ? Visibility.Visible : Visibility.Collapsed; navigation[n].Background = n == selected ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent; }
            };
            sidebar.Children.Add(button);
        }
        void Card(StackPanel panel)
        {
            var children = panel.Children.Cast<UIElement>().Skip(1).ToArray();
            foreach (var child in children) panel.Children.Remove(child);
            var inner = new StackPanel { Margin = new Thickness(20) };
            foreach (var child in children) inner.Children.Add(child);
            panel.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = inner });
        }
        var p = pages[2];
        void Toggle(string label, bool value, Action<bool> change)
        {
            var c = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 7, 0, 7)
            };
            c.Click += (_, _) =>
            {
                change(c.IsChecked == true);
                SaveCompanion();
            };
            p.Children.Add(c);
        }

        Toggle("啟用模型說話", preferences.ModelEnabled, v =>
        {
            preferences.ModelEnabled = v;
            if (!v)
                InvalidateSpeechRequests(true);
            else
            {
                nextWarmup = DateTime.MinValue;
                nextSpeech = DateTime.UtcNow.AddSeconds(20);
            }
        });
        Toggle("拔掉電源時暫停模型並釋放記憶體", preferences.StopModelOnBattery, v =>
        {
            preferences.StopModelOnBattery = v;
            ApplyCompanionPowerPolicy();
        });
        Toggle("使用顯示卡加速（關閉則使用 CPU）", preferences.GpuEnabled, v =>
        {
            preferences.GpuEnabled = v;
            InvalidateSpeechRequests(true);
            brain.UseGpu = v;
            nextWarmup = DateTime.MinValue;
        });
        p = pages[0];
        Toggle("全螢幕時自動隱藏桌寵", preferences.HideOnFullscreen, v =>
        {
            preferences.HideOnFullscreen = v;
            UpdateCompanionVisibility();
        });
        Toggle("開機時啟動", StartupEnabled(), SetCompanionStartup);
        Toggle("啟動時檢查更新", preferences.CheckUpdatesOnStartup, v => preferences.CheckUpdatesOnStartup = v);
        p = pages[1];
        Toggle("自動取得天氣和科技新聞", preferences.PublicInfo, v => preferences.PublicInfo = v);
        Toggle("感知目前使用的程式（本機模型只在本機處理）", preferences.ForegroundEnabled, v => preferences.ForegroundEnabled = v);
#if !COMPANION_SAFE
        p.Children.Add(new TextBlock { Text = "本機模型在本機讀取螢幕；由 Agent 編寫日常關心時，會提供前景視窗的截圖給它。", FontSize = 11,
            Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
#endif
        AddCompanionRegionSelectors(p);
        p = pages[0];
        var label = new TextBlock
        {
            Text = $"縮放：{preferences.Scale:P0}",
            Margin = new Thickness(0, 10, 0, 4)
        };
        p.Children.Add(label);
        var scale = new Slider
        {
            Minimum = .5,
            Maximum = 2.0,
            Value = preferences.Scale,
            TickFrequency = .05,
            IsSnapToTickEnabled = true
        };
        scale.ValueChanged += (_, _) =>
        {
            preferences.Scale = scale.Value;
            label.Text = $"縮放：{scale.Value:P0}";
            ApplyCompanionScale();
        };
        p.Children.Add(scale);
        p = pages[1];
        var bubbleScaleLabel = new TextBlock
        {
            Text = $"文字氣泡大小：{preferences.BubbleScale:P0}",
            Margin = new Thickness(0, 18, 0, 4)
        };
        p.Children.Add(bubbleScaleLabel);
        var bubbleScale = new Slider
        {
            Minimum = .5, Maximum = 2.0, Value = preferences.BubbleScale,
            TickFrequency = .05, IsSnapToTickEnabled = true
        };
        System.Windows.Automation.AutomationProperties.SetName(bubbleScale, "文字氣泡大小");
        bubbleScale.ValueChanged += (_, _) =>
        {
            preferences.BubbleScale = bubbleScale.Value;
            bubbleScaleLabel.Text = $"文字氣泡大小：{bubbleScale.Value:P0}";
            ApplyCompanionBubbleScale();
        };
        p.Children.Add(bubbleScale);
        p = pages[0];
        var intervalLabel = new TextBlock { Text = $"動作觸發間隔：{preferences.AnimationIntervalSeconds} 秒", Margin = new Thickness(0, 18, 0, 4) };
        p.Children.Add(intervalLabel);
        var interval = new Slider { Minimum = 30, Maximum = 300, Value = preferences.AnimationIntervalSeconds, TickFrequency = 10, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(interval, "動作觸發間隔");
        interval.ValueChanged += (_, _) =>
        {
            preferences.AnimationIntervalSeconds = (int)Math.Round(interval.Value);
            intervalLabel.Text = $"動作觸發間隔：{preferences.AnimationIntervalSeconds} 秒";
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            SaveCompanion();
        };
        p.Children.Add(interval);
        var meals = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        void MealTime(string labelText, int initialHour, Action<int> change)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 14, 0), Width = 110 };
            column.Children.Add(new TextBlock { Text = labelText, Margin = new Thickness(0, 0, 0, 6) });
            var selector = new ComboBox();
            for (int hour = 0; hour < 24; hour++) selector.Items.Add($"{hour:00}:00");
            selector.SelectedIndex = initialHour;
            selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) { change(selector.SelectedIndex); SaveCompanion(); } };
            column.Children.Add(selector); meals.Children.Add(column);
        }
        MealTime("早餐時間", preferences.BreakfastHour, h => preferences.BreakfastHour = h);
        MealTime("午餐時間", preferences.LunchHour, h => preferences.LunchHour = h);
        MealTime("晚餐時間", preferences.DinnerHour, h => preferences.DinnerHour = h);
        p.Children.Add(meals);
        p = pages[1];
        var freq = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 8)
        };
        foreach (var range in CompanionPreferences.SpeechRanges)
            freq.Items.Add($"說話間隔：{range.Min}–{range.Max} 分鐘");
        freq.SelectedIndex = preferences.SpeechRangeIndex;
        freq.SelectionChanged += (_, _) =>
        {
            if (freq.SelectedIndex < 0) return;
            preferences.SpeechMinMinutes = CompanionPreferences.SpeechRanges[freq.SelectedIndex].Min;
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            SaveCompanion();
        };
        p.Children.Add(freq);
        var frequencyHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(frequencyHint);
        var dwellLabel = new TextBlock
        {
            Text = "氣泡駐留時間：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex],
            Margin = new Thickness(0, 8, 0, 4)
        };
        p.Children.Add(dwellLabel);
        var dwell = new Slider
        {
            Minimum = 0,
            Maximum = 7,
            Value = preferences.BubbleDurationIndex,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 1,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            ToolTip = dwellLabel.Text
        };
        System.Windows.Automation.AutomationProperties.SetName(dwell, "氣泡駐留時間");
        dwell.ValueChanged += (_, _) =>
        {
            preferences.BubbleDurationIndex = (int)Math.Round(dwell.Value);
            dwellLabel.Text = "氣泡駐留時間：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex];
            dwell.ToolTip = dwellLabel.Text;
            if (bubble.Visibility == Visibility.Visible)
                ResetBubbleDeadline();
            SaveCompanion();
        };
        p.Children.Add(dwell);
        p.Children.Add(new TextBlock { Text = "主動提醒", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 2) });
        p.Children.Add(new TextBlock { Text = "魚魚自己會提醒，不需要模型或 Agent。你不在電腦前、全螢幕、拖曳魚魚或 Agent 正在說話時，提醒會先等等。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 4) });
        void Choice(string name, int[] values, Func<int, string> text, int current, Action<int> change)
        {
            var selector = new ComboBox { Margin = new Thickness(0, 6, 0, 6) };
            foreach (var value in values) selector.Items.Add(name + "：" + text(value));
            selector.SelectedIndex = Math.Max(0, Array.IndexOf(values, current));
            System.Windows.Automation.AutomationProperties.SetName(selector, name);
            selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) { change(values[selector.SelectedIndex]); SaveCompanion(); } };
            p.Children.Add(selector);
        }
        Choice("久坐提醒", CompanionPreferences.SitReminderChoices, minutes => minutes == 0 ? "關閉" : $"連續使用 {minutes} 分鐘後", preferences.SitReminderMinutes, v => preferences.SitReminderMinutes = v);
        Toggle("用餐提醒（三餐時間在「陪伴與外觀」設定）", preferences.MealReminder, v => preferences.MealReminder = v);
        // One list for the switch and the hour: 關閉 turns LateReminder off and keeps the hour.
        Choice("深夜提醒", [-1, .. CompanionPreferences.LateReminderChoices], hour => hour < 0 ? "關閉" : $"{hour:00}:00 到早上 5 點", preferences.LateReminder ? preferences.LateReminderHour : -1,
            v => { preferences.LateReminder = v >= 0; if (v >= 0) preferences.LateReminderHour = v; });
        Toggle("低電量提醒（沒插電且電量剩 20%、10% 時）", preferences.BatteryReminder, v => preferences.BatteryReminder = v);
        Toggle("下雨提醒（需開啟天氣，6 小時內降雨機率 60% 以上）", preferences.RainReminder, v => preferences.RainReminder = v);
        // Kept here with the other reminders, but said by the Hermes bridge from Google Calendar; the fish only stores it.
        Choice("行程開始前提醒", CompanionPreferences.EventReminderChoices, minutes => minutes == 0 ? "關閉" : $"{minutes} 分鐘前", preferences.EventReminderMinutes, v => preferences.EventReminderMinutes = v);
        p.Children.Add(new TextBlock { Text = "需要先替 Hermes 設定好 Google 日曆，由 Hermes 橋接程式在你在電腦前時說（沒有執行橋接程式時不會說）；整天的行程和已回覆不參加的邀請不提醒。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 4) });
        p = pages[2];
        var installModel = new Button { Content = "安裝模型", Margin = new Thickness(0, 8, 0, 8) };
        installModel.Click += (_, _) => OpenModelInstaller();
        p.Children.Add(installModel);
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray,
            Margin = new Thickness(0, 8, 0, 8)
        };
        p.Children.Add(status);
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        void UpdateStatus()
        {
            // The speech interval does not apply while an agent decides when the fish cares.
            freq.IsEnabled = !PuppetActive;
            frequencyHint.Text = PuppetActive ? "日常關心的觸發時機已交給 Agent，此時說話間隔不可調整。關閉接管後會恢復使用此設定。"
                : AgentDailyActive ? "魚魚仍按此間隔觸發日常關心，由 Agent 編寫要說的話。" : "魚魚按此間隔觸發日常關心，由模型編寫要說的話。";
            bool available = brain.ModelAvailable;
            installModel.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            // A remote model keeps running on battery; only the local runner pauses.
            status.Text = (CompanionNative.OnAC ? "已插電" :
                preferences.StopModelOnBattery && !brain.UseRemote ? "使用電池 · 已按設定暫停模型" : "使用電池 · 允許模型執行")
                + "\n" + (AgentDailyActive ? "日常說話由 Agent 編寫，模型不會自動載入。\n" : PuppetActive ? "Agent 決定日常關心的時機，由模型編寫要說的話。\n" : "")
                + (available ? brain.Status : "尚未安裝模型")
                + "\n推論裝置：" + brain.ExecutionDevice;
        }
        timer.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        timer.Start();
        p.Children.Add(new TextBlock { Text = "模型來源", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        var source = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        source.Items.Add("本機內建模型");
        source.Items.Add("遠端 llama.cpp 伺服器（OpenAI 相容介面）");
        source.Items.Add("雲端 AI 服務（Claude、ChatGPT、Gemini）");
        // The saved source; a switch to the cloud AI service counts only after its confirmation.
        int confirmedSource = preferences.ModelSource switch { "Remote" => 1, "Cloud" => 2, _ => 0 };
        source.SelectedIndex = confirmedSource;
        System.Windows.Automation.AutomationProperties.SetName(source, "模型來源");
        p.Children.Add(source);
        var remotePanel = new StackPanel { Visibility = source.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed };
        TextBox RemoteField(string labelText, string value)
        {
            remotePanel.Children.Add(new TextBlock { Text = labelText, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = value, Padding = new Thickness(6, 4, 6, 4) };
            System.Windows.Automation.AutomationProperties.SetName(box, labelText);
            remotePanel.Children.Add(box);
            return box;
        }
        var remoteUrl = RemoteField("伺服器網址（例如 https://example.com）", preferences.RemoteUrl);
        remotePanel.Children.Add(new TextBlock { Text = "API Key（以目前 Windows 帳戶加密儲存，不提供給 MCP）", Margin = new Thickness(0, 8, 0, 4) });
        var remoteKey = new PasswordBox { Password = preferences.RemoteApiKey, Padding = new Thickness(6, 4, 6, 4) };
        System.Windows.Automation.AutomationProperties.SetName(remoteKey, "遠端 API Key");
        remotePanel.Children.Add(remoteKey);
        var remoteModel = RemoteField("模型名稱（留空則使用伺服器提供的第一個模型）", preferences.RemoteModel);
        var screenImages = new CheckBox { Content = "模型支援圖片時，直接傳送視窗截圖（取代 OCR 文字）", IsChecked = preferences.ScreenImages, Margin = new Thickness(0, 12, 0, 4) };
        remotePanel.Children.Add(screenImages);
        remotePanel.Children.Add(new TextBlock { Text = "使用遠端模型時，視窗文字（OCR 辨識結果）或視窗截圖、前景視窗的程式名稱、標題、視窗類別與介面元件名稱、目前時間，以及開啟天氣和新聞時的天氣地點與預報、新聞內容，都會傳送到上面的伺服器。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 4) });
        var testRemote = new Button { Content = "儲存並測試連線", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 4) };
        remotePanel.Children.Add(testRemote);
        p.Children.Add(remotePanel);

        // 雲端 AI 服務 (CompanionCloud.cs): 服務, 取得 API Key, the API key of that service (masked, 顯示 API Key), 模型 (typed,
        // or picked from the list a test fills), 每天最多, the window screenshot option, what is sent where, 儲存並測試, the
        // model state and today's and this month's use. Keys and models typed here wait in cloudKeyDrafts/cloudModelDrafts
        // (one per service, kept over a rebuild of this window) until something saves the model source.
        if (!ReferenceEquals(cloudDraftWindow, w)) { cloudKeyDrafts.Clear(); cloudModelDrafts.Clear(); cloudDraftWindow = w; }
        var cloudPanel = new StackPanel { Visibility = confirmedSource == 2 ? Visibility.Visible : Visibility.Collapsed };
        string cloudProvider = CompanionCloud.Info(preferences.CloudProvider).Id;
        TextBlock CloudLabel(string text)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 4) };
            cloudPanel.Children.Add(t);
            return t;
        }
        TextBlock CloudNote(string text = "")
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 4) };
            cloudPanel.Children.Add(t);
            return t;
        }
        CloudLabel("服務");
        var cloudService = new ComboBox();
        foreach (var info in CompanionCloud.Providers) cloudService.Items.Add(info.DisplayName);
        cloudService.SelectedIndex = Array.FindIndex(CompanionCloud.Providers, info => info.Id == cloudProvider);
        System.Windows.Automation.AutomationProperties.SetName(cloudService, "服務");
        cloudPanel.Children.Add(cloudService);
        var getKey = new Button { Content = "取得 API Key", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        cloudPanel.Children.Add(getKey);
#if COMPANION_SAFE
        CloudLabel("API Key（以目前 Windows 帳戶加密儲存）");
#else
        CloudLabel("API Key（以目前 Windows 帳戶加密儲存，不提供給 MCP 和 Agent）");
#endif
        var cloudKey = new PasswordBox { Padding = new Thickness(6, 4, 6, 4) };
        var cloudKeyShown = new TextBox { Padding = new Thickness(6, 4, 6, 4), Visibility = Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetName(cloudKey, "雲端 API Key");
        System.Windows.Automation.AutomationProperties.SetName(cloudKeyShown, "雲端 API Key");
        cloudPanel.Children.Add(cloudKey);
        cloudPanel.Children.Add(cloudKeyShown);
        var revealKey = new CheckBox { Content = "顯示 API Key", Margin = new Thickness(0, 4, 0, 0) };
        cloudPanel.Children.Add(revealKey);
        CloudNote("貼上 API Key 時，會依開頭自動選擇服務：sk-ant- 是 Claude，AIza 和 AQ. 是 Gemini，其他 sk- 是 ChatGPT。");
        CloudLabel("模型");
        var cloudModel = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
        System.Windows.Automation.AutomationProperties.SetName(cloudModel, "模型");
        cloudPanel.Children.Add(cloudModel);
        var cloudModels = new ComboBox { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetName(cloudModels, "從清單選擇模型");
        cloudPanel.Children.Add(cloudModels);
        var modelNote = CloudNote();
        var costNote = CloudNote();
        CloudLabel("每天最多");
        var dailyLimit = new ComboBox();
        foreach (var limit in CompanionPreferences.CloudDailyLimitChoices) dailyLimit.Items.Add(limit == 0 ? "不限制" : $"{limit} 次");
        dailyLimit.SelectedIndex = Math.Max(0, Array.IndexOf(CompanionPreferences.CloudDailyLimitChoices, preferences.CloudDailyLimit));
        System.Windows.Automation.AutomationProperties.SetName(dailyLimit, "每天最多");
        cloudPanel.Children.Add(dailyLimit);
        CloudNote("魚魚每請雲端服務寫一句話（包括天氣和新聞）算一次；到了上限，當天就不再送出，隔天自動恢復。");
        var cloudImages = new CheckBox { Content = CloudScreenshotLabel, IsChecked = preferences.ScreenImages, Margin = new Thickness(0, 12, 0, 4) };
        cloudPanel.Children.Add(cloudImages);
        var cloudPrivacy = CloudNote();
        var testCloud = new Button { Content = "儲存並測試", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 4) };
        cloudPanel.Children.Add(testCloud);
        var cloudResult = CloudNote("填好 API Key 後按「儲存並測試」：只會讀取這把 API Key 可用的模型清單，不會用掉 token。");
        var cloudState = CloudNote();
        var cloudUsage = CloudNote();
        p.Children.Add(cloudPanel);

        bool loadingCloud = false;
        int lastKeyLength = 0;
        string CloudKeyText() => (revealKey.IsChecked == true ? cloudKeyShown.Text : cloudKey.Password).Trim();
        string CloudModelText() => cloudModel.Text.Trim();
        void SelectListedModel()
        {
            var listed = cloudModels.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == CloudModelText());
            if (ReferenceEquals(cloudModels.SelectedItem, listed)) return;
            loadingCloud = true;
            try { cloudModels.SelectedItem = listed; }
            finally { loadingCloud = false; }
        }
        // The models the last successful test listed, for the service shown.
        void FillCloudModels()
        {
            IReadOnlyList<CloudModelInfo> models = brain.UseCloud && brain.CloudProvider == cloudProvider ? brain.CloudModels : Array.Empty<CloudModelInfo>();
            loadingCloud = true;
            try
            {
                cloudModels.Items.Clear();
                foreach (var model in models)
                    cloudModels.Items.Add(new ComboBoxItem { Content = model.DisplayName.Length == 0 || model.DisplayName == model.Id ? model.Id : $"{model.DisplayName}（{model.Id}）", Tag = model.Id });
            }
            finally { loadingCloud = false; }
            cloudModels.Visibility = models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectListedModel();
        }
        void UpdateCloudCost()
        {
            // The company bills the service: Anthropic, OpenAI, Google.
            var name = CompanionCloud.Company(cloudProvider);
            var model = CloudModelText().Length > 0 ? CloudModelText() : CompanionCloud.DefaultModel(cloudProvider);
            var text = CompanionCloud.CostHint(cloudProvider, model) is { } hint
                ? $"費用估計：{model} 每說一句{hint}（以附上前景視窗截圖估計，實際費用以 {name} 的帳單為準）。"
                : $"{model} 沒有費用估計，價格請見 {name} 的網站。";
            if (cloudProvider == CompanionCloud.Claude)
                text += $"\nClaude 各模型每說一句大約：Opus 5.5 {CompanionCloud.CostHint(CompanionCloud.Claude, "claude-opus-5-5")}、Sonnet 5.5 {CompanionCloud.CostHint(CompanionCloud.Claude, "claude-sonnet-5-5")}、Haiku 5.5 {CompanionCloud.CostHint(CompanionCloud.Claude, "claude-haiku-5-5")}。";
            costNote.Text = text;
        }
        // The service shown: its key and model (typed and not saved yet, or saved), its model list, notes and privacy text.
        void ShowCloudProvider()
        {
            loadingCloud = true;
            try
            {
                var key = cloudKeyDrafts.TryGetValue(cloudProvider, out var typedKey) ? typedKey : preferences.CloudApiKey(cloudProvider);
                cloudKey.Password = key;
                cloudKeyShown.Text = key;
                lastKeyLength = key.Length;
                cloudModel.Text = cloudModelDrafts.TryGetValue(cloudProvider, out var typedModel) ? typedModel : preferences.CloudModel(cloudProvider);
            }
            finally { loadingCloud = false; }
            FillCloudModels();
            modelNote.Text = $"按「儲存並測試」後，下面會列出這把 API Key 可用的模型；也可以直接輸入模型名稱。留空則使用建議的 {CompanionCloud.DefaultModel(cloudProvider)}。";
            cloudPrivacy.Text = CloudPrivacyText(cloudProvider);
            UpdateCloudCost();
        }
        void UpdateCloudState()
        {
            if (cloudPanel.Visibility != Visibility.Visible) return;
            cloudState.Text = "模型狀態：" + (brain.UseCloud ? brain.Status : "尚未使用雲端 AI 服務");
            var usage = "用量：" + brain.CloudUsage.Describe(preferences.CloudDailyLimit);
            if (brain.CloudPausedUntil is { } until)
                usage += $"\n雲端請求暫停中，{until.ToLocalTime():HH:mm} 自動恢復；換用其他服務、API Key 或模型，或按「儲存並測試」成功後也會解除。";
            cloudUsage.Text = usage;
        }
        // Turns the panel to another service. Nothing is saved: 儲存並測試 (or picking a model from the tested list) does that.
        void SelectCloudProvider(string provider)
        {
            int index = Array.FindIndex(CompanionCloud.Providers, info => info.Id == provider);
            if (index < 0 || provider == cloudProvider) return;
            cloudProvider = provider;
            loadingCloud = true;
            try { cloudService.SelectedIndex = index; }
            finally { loadingCloud = false; }
            ShowCloudProvider();
        }
        ShowCloudProvider();
        UpdateCloudState();
        timer.Tick += (_, _) => UpdateCloudState();

        // cloud: the service shown on the cloud panel and its typed keys and models become the saved ones (儲存並測試, the
        // confirmed switch to the cloud source, a model picked from the tested list). Every other save (the image option,
        // another source, 測試遠端模型) leaves them as drafts, as the panel says.
        void SaveModelSource(bool cloud = false)
        {
            preferences.ModelSource = confirmedSource switch { 1 => "Remote", 2 => "Cloud", _ => "Local" };
            preferences.RemoteUrl = remoteUrl.Text.Trim();
            preferences.RemoteModel = remoteModel.Text.Trim();
            preferences.RemoteApiKey = remoteKey.Password;
            if (cloud)
            {
                preferences.CloudProvider = cloudProvider;
                // A key typed for one service that belongs to another is not saved for it; it stays a draft.
                foreach (var (provider, key) in cloudKeyDrafts.ToList())
                    if (CompanionCloud.ProviderForKey(key) is not { } owner || owner == provider)
                    {
                        preferences.SetCloudApiKey(provider, key.Trim());
                        cloudKeyDrafts.Remove(provider);
                        // Claude Code on the Claude API Key starts again with the new key (or stops for want of one).
                        if (provider == CompanionCloud.Claude) AgentLaunchChanged();
                    }
                foreach (var (provider, model) in cloudModelDrafts) preferences.SetCloudModel(provider, model);
                cloudModelDrafts.Clear();
            }
            preferences.ScreenImages = screenImages.IsChecked == true;
            // Drops pending speech and weather requests too, as every other switch of the model does since 1.0.5.
            InvalidateSpeechRequests();
            ClearCompanionModelReply();
            ApplyCompanionModelSource();
            SaveCompanion();
            UpdateStatus();
            UpdateCloudState();
        }
        void ShowSourcePanels()
        {
            remotePanel.Visibility = confirmedSource == 1 ? Visibility.Visible : Visibility.Collapsed;
            cloudPanel.Visibility = confirmedSource == 2 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCloudState();
        }
        bool revertingSource = false;
        source.SelectionChanged += (_, _) =>
        {
            if (revertingSource || source.SelectedIndex < 0 || source.SelectedIndex == confirmedSource) return;
            if (source.SelectedIndex != 2)
            {
                confirmedSource = source.SelectedIndex;
                ShowSourcePanels();
                SaveModelSource();
                return;
            }
            // Once the selection has settled: the confirmation names the service and what is sent; 取消 keeps the source.
            remotePanel.Visibility = Visibility.Collapsed;
            w.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
            {
                if (companionSettings != w || w.Content != shell || source.SelectedIndex != 2 || confirmedSource == 2) return;
                if (ConfirmCloudSource(w, cloudProvider))
                {
                    confirmedSource = 2;
                    ShowSourcePanels();
                    // The confirmation named the service shown: it is the one used.
                    SaveModelSource(cloud: true);
                    // Rebuilt while the confirmation was open (MCP update_settings): the new page shows the saved source.
                    if (companionSettings == w && w.Content != shell) { refreshCompanionSettings?.Invoke(); return; }
                    if (!preferences.UseCloudModel)
                        cloudResult.Text = $"請貼上 {CompanionCloud.Info(cloudProvider).Name} 的 API Key，再按「儲存並測試」。還沒有 API Key 的話，按「取得 API Key」開啟建立 API Key 的網頁。";
                    w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() => cloudPanel.BringIntoView()));
                }
                else
                {
                    revertingSource = true;
                    try { source.SelectedIndex = confirmedSource; }
                    finally { revertingSource = false; }
                    ShowSourcePanels();
                }
            }));
        };
        screenImages.Click += (_, _) =>
        {
            cloudImages.IsChecked = screenImages.IsChecked;
            SaveModelSource();
        };
        cloudImages.Click += (_, _) =>
        {
            screenImages.IsChecked = cloudImages.IsChecked;
            SaveModelSource();
        };
        testRemote.Click += async (_, _) =>
        {
            SaveModelSource();
            testRemote.IsEnabled = false;
            status.Text = "正在測試遠端模型……";
            await brain.TestRemote();
            testRemote.IsEnabled = true;
            UpdateStatus();
        };
        cloudService.SelectionChanged += (_, _) =>
        {
            if (loadingCloud || cloudService.SelectedIndex < 0) return;
            var provider = CompanionCloud.Providers[cloudService.SelectedIndex].Id;
            if (provider == cloudProvider) return;
            cloudProvider = provider;
            ShowCloudProvider();
            cloudResult.Text = provider == preferences.CloudProvider && preferences.CloudSelected ? ""
                : $"已改選 {CompanionCloud.Info(provider).DisplayName}，按「儲存並測試」後才會改用這個服務。";
        };
        getKey.Click += (_, _) =>
        {
            var page = CompanionCloud.Info(cloudProvider).KeyPage;
            try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                cloudResult.Text = "無法開啟瀏覽器，請自行前往 " + page;
            }
        };
        void CloudKeyChanged(string text)
        {
            if (loadingCloud) return;
            loadingCloud = true;
            try
            {
                if (revealKey.IsChecked == true) cloudKey.Password = text;
                else cloudKeyShown.Text = text;
            }
            finally { loadingCloud = false; }
            // A pasted key (not a keystroke) names its service: the panel turns to it, and the service shown before keeps
            // its own key.
            bool pasted = Math.Abs(text.Length - lastKeyLength) > 1;
            lastKeyLength = text.Length;
            if (pasted && CompanionCloud.ProviderForKey(text) is { } detected && detected != cloudProvider)
            {
                cloudKeyDrafts.Remove(cloudProvider);
                cloudKeyDrafts[detected] = text;
                SelectCloudProvider(detected);
                cloudResult.Text = $"依 API Key 的開頭，已改選 {CompanionCloud.Info(detected).DisplayName}；按「儲存並測試」後才會改用。";
                return;
            }
            cloudKeyDrafts[cloudProvider] = text;
        }
        cloudKey.PasswordChanged += (_, _) => CloudKeyChanged(cloudKey.Password);
        cloudKeyShown.TextChanged += (_, _) => CloudKeyChanged(cloudKeyShown.Text);
        revealKey.Click += (_, _) =>
        {
            bool shown = revealKey.IsChecked == true;
            cloudKeyShown.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            cloudKey.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        };
        cloudModel.TextChanged += (_, _) =>
        {
            if (loadingCloud) return;
            cloudModelDrafts[cloudProvider] = cloudModel.Text;
            UpdateCloudCost();
            SelectListedModel();
        };
        // A model picked from the list the test filled is used at once.
        cloudModels.SelectionChanged += (_, _) =>
        {
            if (loadingCloud || cloudModels.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            loadingCloud = true;
            try { cloudModel.Text = id; }
            finally { loadingCloud = false; }
            cloudModelDrafts[cloudProvider] = id;
            UpdateCloudCost();
            SaveModelSource(cloud: true);
        };
        dailyLimit.SelectionChanged += (_, _) =>
        {
            if (dailyLimit.SelectedIndex < 0) return;
            preferences.CloudDailyLimit = CompanionPreferences.CloudDailyLimitChoices[dailyLimit.SelectedIndex];
            brain.CloudDailyLimit = preferences.CloudDailyLimit;
            SaveCompanion();
            UpdateCloudState();
        };
        testCloud.Click += async (_, _) =>
        {
            // A key of another service turns the panel to that service first.
            if (CompanionCloud.ProviderForKey(CloudKeyText()) is { } detected && detected != cloudProvider)
            {
                var typed = CloudKeyText();
                cloudKeyDrafts.Remove(cloudProvider);
                cloudKeyDrafts[detected] = typed;
                SelectCloudProvider(detected);
            }
            SaveModelSource(cloud: true);
            var provider = cloudProvider;
            var name = CompanionCloud.Info(provider).Name;
            if (!preferences.UseCloudModel)
            {
                cloudResult.Text = $"請先貼上 {name} 的 API Key。還沒有 API Key 的話，按「取得 API Key」開啟建立 API Key 的網頁。";
                return;
            }
            testCloud.IsEnabled = false;
            cloudResult.Text = $"正在測試 {name} 的 API Key…";
            try
            {
                // Saving cancelled whatever the model was doing; it lets go within moments.
                for (int i = 0; i < 30 && brain.Busy; i++) await Task.Delay(100);
                var reply = await brain.TestRemote();
                if (companionSettings != w || w.Content != shell) return;
                bool ready = reply.StartsWith(CompanionCloud.ReadyPrefix, StringComparison.Ordinal);
                // No model chosen yet (or the recommended one is not listed for this key): the preselected one is used.
                if (ready && brain.UseCloud && brain.CloudProvider == provider && cloudProvider == provider && brain.CloudModels.Count > 0)
                {
                    var current = preferences.CloudModel(provider);
                    var pick = CompanionCloud.Preselect(provider, brain.CloudModels, current);
                    if (pick != current)
                    {
                        loadingCloud = true;
                        try { cloudModel.Text = pick; }
                        finally { loadingCloud = false; }
                        cloudModelDrafts[provider] = pick;
                        SaveModelSource(cloud: true);
                    }
                }
                if (cloudProvider == provider) { FillCloudModels(); UpdateCloudCost(); }
                // A valid key whose list lacks the model typed says so itself; it is not a failed test.
                cloudResult.Text = ready
                    ? $"測試成功：{name} 的 API Key 有效，找到 {brain.CloudModels.Count} 個可用模型，目前使用 {brain.CloudModel}。"
                    : reply.Contains("API Key 有效", StringComparison.Ordinal) ? reply : "測試沒有成功：" + reply;
            }
            finally { testCloud.IsEnabled = true; }
            UpdateCloudState();
        };
        void Button(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 4)
            };
            b.Click += (_, _) => action();
            p.Children.Add(b);
        }

        p = pages[0];
        Button("檢查更新", async () => await CheckForUpdates(true));
        var updates = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        p.Children.Add(updates);
        timer.Tick += (_, _) => updates.Text = updateStatus;
        Button("隱藏到系統匣", HideCompanion);
        Button("回到右下角", () =>
        {
            var a = SystemParameters.WorkArea;
            Left = a.Right - Width - 8;
            Top = a.Bottom - Height + 12;
            ClampCompanion();
            anchor = new Point(Left, Top);
            SaveCompanion();
        });
        Button("關閉桌寵", () =>
        {
            w.Close();
            base.Close();
        });
#if COMPANION_SAFE
        Action? disposeAgentSettings = null, disposeMcpSettings = null;
#else
        Action? disposeAgentSettings = AddAgentConnectionSettings(pages[Array.IndexOf(titles, AgentConnectionPage)], w);
        Action? disposeMcpSettings = AddMcpInterfaceSettings(pages[Array.IndexOf(titles, McpInterfacePage)], w);
#endif
        Action disposeLog = BuildLogConsole(pages[^1], w, () => views[^1].Visibility == Visibility.Visible);
        foreach (var panel in pages) Card(panel);
        disposeCompanionSettingsContent = () => { timer.Stop(); disposeAgentSettings?.Invoke(); disposeMcpSettings?.Invoke(); disposeLog(); };
        refreshCompanionSettings = () => BuildCompanionSettingsContent(w, selectedPage, views.Select(v => v.VerticalOffset).ToArray());
        if (scrollOffsets != null) w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
        {
            if (companionSettings != w || w.Content != shell) return;
            for (int i = 0; i < views.Length; i++) views[i].ScrollToVerticalOffset(scrollOffsets[i]);
        }));
        if (cloudSettingsRequested)
        {
            cloudSettingsRequested = false;
            w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                if (companionSettings != w || w.Content != shell) return;
                source.BringIntoView();
                // Asks its confirmation; a source already on the cloud stays as it is.
                if (source.SelectedIndex != 2) source.SelectedIndex = 2;
            }));
        }
    }

    // 改用雲端 AI 服務 from the 安裝本機模型 window: 設定 › 模型與供電 with 模型來源 turned to the cloud AI service.
    private void OpenCloudSettings()
    {
        if (exitAnimation || companionClosed || exitReady) return;
        cloudSettingsRequested = true;
        if (companionSettings is { } open)
        {
            BuildCompanionSettingsContent(open, ModelSettingsPage);
            ActivateCompanionSettings(open);
        }
        else OpenCompanionSettings(ModelSettingsPage);
    }

    // What the cloud panel says is sent, and to which service.
    private static string CloudPrivacyText(string provider) => "使用雲端 AI 服務時，會傳送到 " + CompanionCloud.Info(provider).DisplayName
        + " 的內容：魚魚的說話設定、目前時間、前景視窗的程式名稱、標題、視窗類別與介面元件名稱"
#if COMPANION_OCR
        + "、在這台電腦上 OCR 辨識的前景視窗文字，或在開啟「傳送前景視窗截圖」時改傳前景視窗的截圖"
#else
        + "，以及在開啟「傳送前景視窗截圖」時，前景視窗的截圖"
#endif
        + "（JPEG，長邊最多 1568 像素）；開啟天氣和新聞時，還有天氣地點與預報、新聞內容。只會讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗。"
#if COMPANION_SAFE
        + "API Key 以目前 Windows 帳戶加密儲存在這台電腦。"
#else
        + "API Key 以目前 Windows 帳戶加密儲存在這台電腦，不提供給 MCP 和 Agent。"
#endif
        + "各服務依用量收費，建議在服務的主控台設定花費上限，並為魚魚建立一把專用的 API Key。";

    // Before 模型來源 turns to the cloud AI service: which service gets what, where the key stays, and how to keep the
    // spending in hand. 取消 (or closing the window) keeps the source as it was.
    private bool ConfirmCloudSource(Window owner, string provider)
    {
        var info = CompanionCloud.Info(provider);
        var dialog = new ModernWindow("改用雲端 AI 服務", "改用雲端 AI 服務？",
            $"之後魚魚說話時，會把下列內容傳送到 {info.DisplayName}，由它寫出魚魚要說的話：", 620, 600) { ShowInTaskbar = false };
        if (owner.IsVisible) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        string screenshot = preferences.ScreenImages ? "目前已開啟" : "目前關閉";
        dialog.Body.Children.Add(ModernUI.Text(string.Join("\n",
            "• 魚魚的說話設定和目前時間",
            "• 前景視窗的程式名稱、標題、視窗類別與介面元件名稱",
#if COMPANION_OCR
            $"• 在這台電腦上 OCR 辨識的前景視窗文字；開啟「傳送前景視窗截圖」時改傳截圖（{screenshot}）",
#else
            $"• 開啟「傳送前景視窗截圖」時，前景視窗的截圖（{screenshot}）",
#endif
            "• 開啟天氣和新聞時，天氣地點與預報、新聞內容")));
#if COMPANION_SAFE
        dialog.Body.Children.Add(ModernUI.Text("之後在「服務」換成其他服務時，就改傳到那個服務。API Key 以目前 Windows 帳戶加密儲存在這台電腦。"));
#else
        dialog.Body.Children.Add(ModernUI.Text("之後在「服務」換成其他服務時，就改傳到那個服務。API Key 以目前 Windows 帳戶加密儲存在這台電腦，不提供給 MCP 和 Agent。"));
#endif
        dialog.Body.Children.Add(ModernUI.Text("各服務依用量收費。建議先在服務的主控台設定花費上限，並為魚魚建立一把專用的 API Key；設定裡的「每天最多」也能限制每天的請求次數。"));
        bool accepted = false;
        dialog.Actions.Children.Add(ModernUI.Button("改用雲端 AI 服務", () => { accepted = true; dialog.Close(); }, true));
        dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close));
        dialog.ShowDialog();
        return accepted;
    }

    private void ActivateCompanionSettings(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Topmost = false;
        window.Show();
        window.Activate();
        // Pet/tray menus finish restoring their previous foreground window after
        // the click handler. Activate once that input/menu work has completed.
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)(() =>
        {
            if (companionSettings == window && window.IsVisible && !companionClosed && !exitAnimation)
            {
                // This is only reached by an explicit user request. Keep the
                // ordinary window band even if a native topmost flag was left over.
                window.Topmost = false;
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                CompanionNative.SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
                window.Activate();
            }
        }));
    }

}
