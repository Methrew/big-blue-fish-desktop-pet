// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
#if !COMPANION_SAFE
using BigBlueFish.Control;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool consentOpen;
    Window? consentWindow;
    Action? refreshLinuxSettings;
    Action? disposeLogConsole;
#if COMPANION_MAC
    // A LaunchAgent labelled with this edition's bundle identifier (MacLoginItem). Refused while the app runs from a
    // translocated or read-only location; the switch then shows the real state again.
    const string StartupLabel = "登入時自動開啟";
    bool StartupEnabled => MacLoginItem.Enabled;
    void SetStartup(bool on)
    {
        try { MacLoginItem.Set(on); }
        catch (Exception error) {
            if (!consentOpen) _ = Inform(StartupLabel, error.Message);
            Dispatcher.UIThread.Post(() => refreshLinuxSettings?.Invoke());
        }
    }
#else
    const string StartupLabel = "開機時啟動";
    static string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart/bigbluefish.desktop");
    bool StartupEnabled => File.Exists(AutostartFile);
    void SetStartup(bool on)
    {
        if (!on) { File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, "[Desktop Entry]\nType=Application\nName=" + CompanionEdition.AppName + "\nExec=\"" + Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\nTerminal=false\n");
    }
#endif
    void ApplySettings(PixelPoint? requestedPosition = null)
    {
        speechVersion++; brain.Stop(); brain.UseGpu = Preferences.GpuEnabled; brain.StopOnBattery = Preferences.StopModelOnBattery; brain.WeatherRegionCode = Preferences.WeatherRegionCode;
        ConfigureAgent();
        Rescale(); var appliedPosition = Clamp(requestedPosition); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue;
        if (!Preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hidden) Show(); }
        ApplyPlatformSettings();
        if (bubbleWindow.IsVisible) ResetDwell();
        Preferences.Left = appliedPosition.X; Preferences.Top = appliedPosition.Y; Preferences.Save(); DiagnosticLog.ObserveSettings(Preferences);
    }
    static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(18, 9), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 10, 4), Background = Brushes.White, Foreground = Hair, BorderBrush = new SolidColorBrush(Color.FromRgb(222,226,237)), BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); return button;
    }
    static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12), Foreground = Brushes.SlateGray };
    static Window Dialog(string title, Avalonia.Controls.Control content, double width = 630, double height = 460) => new() {
        Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 360,
        FontFamily = LinuxFonts.Interface, FontSize = 14,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
        Content = new Border { Padding = new Thickness(28), Child = content } };
    // reject null: a notice with its one button (Inform).
    async Task<bool> Confirm(string title, string message, string accept = "確定", CancellationToken token = default, string? reject = "取消")
    {
        if (consentOpen) throw new InvalidOperationException("請先處理已開啟的提示視窗。");
        consentOpen = true; bool accepted = false;
        try {
            var panel = new StackPanel { Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold }); panel.Children.Add(Paragraph(message));
            var dialog = Dialog(title, panel, reject == null ? 480 : 630); var buttons = new WrapPanel();
            // As tall as the message: a short one leaves no empty half window under its buttons.
            dialog.MinHeight = 0; dialog.Height = double.NaN; dialog.SizeToContent = SizeToContent.Height;
            buttons.Children.Add(ActionButton(accept, () => { accepted = true; dialog.Close(); }));
            if (reject != null) buttons.Children.Add(ActionButton(reject, dialog.Close));
            panel.Children.Add(buttons);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { accepted = false; dialog.Close(); }));
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            consentWindow = dialog;
            if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
            return await finished.Task;
        } finally { consentWindow = null; consentOpen = false; }
    }
    // A notice that only tells something: one button, as tall as its text.
    Task Inform(string title, string message, string accept = "知道了") => Confirm(title, message, accept, reject: null);
    void OpenSettings()
    {
        if (settings != null) { if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal; settings.Activate(); return; }
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        var navigation = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 20, 0) };
        // The right margin keeps the overlay scroll bar off the last characters and the right edge of the fields.
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 16, 0) }; var scroll = new ScrollViewer { Content = body }; Grid.SetColumn(scroll, 1); root.Children.Add(navigation); root.Children.Add(scroll);
        settings = Dialog(CompanionEdition.AppName + " · 設定", root, 880, 700); settings.MinWidth = 780; settings.MinHeight = 600;
        settings.Closed += (_, _) => { disposeLogConsole?.Invoke(); disposeLogConsole = null; settings = null; refreshLinuxSettings = null; DiagnosticLog.Write(DiagnosticEvent.SettingsClosed); Save(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); };
        Action selectedPage = General;
        void Navigate(Action page) { selectedPage = page; page(); }
        void Title(string text) { disposeLogConsole?.Invoke(); disposeLogConsole = null; body.Children.Clear(); body.Children.Add(new TextBlock { Text = text, FontSize = 25, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 16) }); }
        CheckBox Toggle(string text, bool value, Action<bool> set) { var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4) }; box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Save(); }; body.Children.Add(box); return box; }
        void Slider(string text, double value, double low, double high, Action<double> set, double tick = 1) {
            var label = new TextBlock { Text = text + "：" + value.ToString("0.##"), Margin = new Thickness(0, 12, 0, 0) };
            var slider = new Avalonia.Controls.Slider { Minimum = low, Maximum = high, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { label.Text = text + "：" + slider.Value.ToString("0.##"); set(slider.Value); Save(); }; body.Children.Add(label); body.Children.Add(slider);
        }
        ComboBox Select(string label, string[] choices, int selected, Action<int> set) {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var combo = new ComboBox { ItemsSource = choices, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Save(); } }; body.Children.Add(combo); return combo;
        }
        void General() {
            Title("桌寵與顯示"); Slider("桌寵大小（%）", Preferences.Scale * 100, 50, 200, value => { Preferences.Scale = value / 100; Rescale(); Clamp(); }, 5);
            Slider("文字氣泡大小（%）", Preferences.BubbleScale * 100, 50, 200, value => { Preferences.BubbleScale = value / 100; MoveBubble(); }, 5);
            Toggle(StartupLabel, StartupEnabled, SetStartup); Toggle("全螢幕時自動隱藏桌寵", Preferences.HideOnFullscreen, value => { Preferences.HideOnFullscreen = value; ApplySettings(); });
            Select("氣泡駐留時間", CompanionPreferences.BubbleDurationLabels, Preferences.BubbleDurationIndex, value => { Preferences.BubbleDurationIndex = value; ResetDwell(); });
            AddPlatformGeneralOptions(body);
        }
        void Model() {
            Title("模型與資訊"); Toggle("啟用模型說話", Preferences.ModelEnabled, value => { Preferences.ModelEnabled = value; ApplySettings(); });
            BuildModelSourceSettings(body);
            LocalModelOnly(Toggle("使用 GPU 加速（只對本機模型有效）", Preferences.GpuEnabled, value => { Preferences.GpuEnabled = value; ApplySettings(); }));
            // Added only after the handlers: the open window attaches it at once, and a later handler would never run.
            var deviceStatus = Paragraph("");
            var deviceClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            // "GPU：Apple M4（MTL0）" reads with one colon here: 推論裝置：GPU Apple M4（MTL0）.
            void UpdateDevice() => deviceStatus.Text = "推論裝置：" + (brain.ExecutionDevice is var device && device.StartsWith("GPU：", StringComparison.Ordinal) ? "GPU " + device[4..] : device);
            deviceClock.Tick += (_, _) => UpdateDevice();
            deviceStatus.AttachedToVisualTree += (_, _) => { UpdateDevice(); deviceClock.Start(); };
            deviceStatus.DetachedFromVisualTree += (_, _) => deviceClock.Stop();
            body.Children.Add(deviceStatus);
            LocalModelOnly(Toggle("筆電拔掉電源時暫停模型（只對本機模型有效）", Preferences.StopModelOnBattery, value => { Preferences.StopModelOnBattery = value; ApplySettings(); }));
            Toggle("連網取得天氣和科技新聞", Preferences.PublicInfo, value => Preferences.PublicInfo = value);
            Toggle(OperatingSystem.IsMacOS() ? "感知目前使用的 App" : "感知目前使用的程式", Preferences.ForegroundEnabled, value => Preferences.ForegroundEnabled = value);
#if !COMPANION_SAFE
            body.Children.Add(Paragraph("使用本機模型時，螢幕內容只在這台電腦上辨識；使用遠端模型時，會傳送到上面填寫的伺服器。由 Agent 編寫日常關心時，會提供前景視窗截圖給 Agent。"));
#endif
            body.Children.Add(ActionButton(brain.ModelAvailable ? "管理本機模型" : "安裝模型", async () => await InstallModel()));
            var frequency = Select("自動說話間隔", CompanionPreferences.SpeechRanges.Select(r => $"{r.Min}–{r.Max} 分鐘").ToArray(), Preferences.SpeechRangeIndex, value => { Preferences.SpeechMinMinutes = CompanionPreferences.SpeechRanges[value].Min; nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); });
            frequency.IsEnabled = !Puppet;
            body.Children.Add(Paragraph(Puppet ? "日常關心的觸發時機已交給 Agent，此時說話間隔不可調整。關閉接管後會恢復使用此設定。" : AgentDailyActive ? "魚魚按此間隔觸發日常關心，由 Agent 編寫要說的話。" : "魚魚按此間隔觸發日常關心，由模型編寫要說的話。"));
            BuildWeatherSettings(body);
        }
        void Animation() {
            Title("動作與作息"); Slider("動作觸發間隔（秒）", Preferences.AnimationIntervalSeconds, 30, 300, value => { Preferences.AnimationIntervalSeconds = (int)value; nextEvent = DateTime.UtcNow.AddSeconds(value); });
            foreach (var meal in new[] { ("早餐", "BreakfastHour"), ("午餐", "LunchHour"), ("晚餐", "DinnerHour") }) {
                var property = typeof(CompanionPreferences).GetProperty(meal.Item2)!;
                Slider(meal.Item1 + "時間（時）", (int)property.GetValue(Preferences)!, 0, 23, value => property.SetValue(Preferences, (int)value));
            }
            BuildReminderSettings(body);
        }
        void About() { Title("關於與更新"); BuildAbout(body); }
        navigation.Children.Add(ActionButton("桌寵與顯示", () => Navigate(General))); navigation.Children.Add(ActionButton("模型與資訊", () => Navigate(Model))); navigation.Children.Add(ActionButton("動作與作息", () => Navigate(Animation)));
#if !COMPANION_SAFE
        void Agent() { Title("連線 Agent"); BuildAgentSettings(body); }
        navigation.Children.Add(ActionButton("連線 Agent", () => Navigate(Agent)));
#endif
        navigation.Children.Add(ActionButton("關於與更新", () => Navigate(About)));
        refreshLinuxSettings = () => { var offset = scroll.Offset; selectedPage(); scroll.Offset = offset; };
        void Logs() { Title("記錄主控台"); disposeLogConsole = BuildLogConsole(body); }
        navigation.Children.Add(ActionButton("記錄主控台", () => Navigate(Logs)));
        DiagnosticLog.Write(DiagnosticEvent.SettingsOpened);
        General(); settings.Show(); settings.Activate();
        OpenedSettingsPlatform();
    }
}


