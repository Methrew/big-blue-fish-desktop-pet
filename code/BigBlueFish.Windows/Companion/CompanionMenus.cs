// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private const string BatteryPauseNotice = "主人，目前設定為筆電拔掉電源時暫停模型，可以在設定裡調整喔。";

    private void BuildCompanionContextMenu()
    {
        companionContextMenu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        companionContextMenu.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/BigBlueFish;component/Companion/CompanionMenuStyles.xaml", UriKind.Relative) });
        companionContextMenu.Style = (Style)companionContextMenu.FindResource(typeof(ContextMenu));
        companionMenuDismissal = new CompanionMenuDismissal(companionContextMenu);
        TextBlock Icon(string glyph) => new()
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 96, 152)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var settings = new MenuItem { Header = "設定" };
        settings.Icon = Icon("\uE713");
        settings.Click += (_, _) => { companionContextMenu.IsOpen = false; OpenCompanionSettings(); };
        var weather = new MenuItem { Header = "天氣" };
        weather.Icon = Icon("\uE706");
        weather.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionWeather(); };
        companionContextMenu.Items.Add(settings);
        companionContextMenu.Items.Add(weather);
        var test = new MenuItem { Header = "說話測試" };
        test.Icon = Icon("\uE8F2");
        test.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionTest(); };
        companionContextMenu.Items.Add(test);
        var hide = new MenuItem { Header = "隱藏桌寵" };
        hide.Icon = Icon("\uE8A7");
        hide.Click += (_, _) => { companionContextMenu.IsOpen = false; HideCompanion(); };
        companionContextMenu.Items.Add(hide);
        // Weather and the speech test stay available while an agent decides when the fish cares (since 1.1.0).
        companionContextMenu.Opened += (_, _) => { weather.IsEnabled = true; test.IsEnabled = true; };
    }

    private async Task SpeakCompanionWeather()
    {
        if (weatherRequestPending || companionClosed || exitAnimation) return;
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (await TryManualAgentSpeech(true)) return;
        if (!brain.ModelAvailable) { OpenModelInstaller(); return; }
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (!preferences.ModelEnabled)
        {
            ShowCompanionNotice("主人，請先在設定裡開啟「啟用模型說話」，再來檢視天氣吧～", manual: true);
            return;
        }
        if (!brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice(BatteryPauseNotice, true, true);
            return;
        }
        InvalidateSpeechRequests();
        weatherRequestPending = true;
        long version = speechRequestVersion;
        string regionCode = preferences.WeatherRegionCode;
        try
        {
            ShowCompanionNotice("主人，正在檢視" + CompanionRegions.FullName(CompanionRegions.District(regionCode)) + "的天氣～", manual: true);
            // Let an in-flight warm-up or utterance finish; automatic speech yields to this request.
            var deadline = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < deadline && !companionClosed && brain.CanRunOnCurrentPower)
                await Task.Delay(100);
            if (version != speechRequestVersion || companionClosed || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower || regionCode != preferences.WeatherRegionCode)
                return;
            if (brain.Busy) { ShowCompanionNotice("主人，模型暫時還在忙，請稍後再試一下～", manual: true); return; }
            BeginModelSpeech();
            string text;
            try
            {
                text = await brain.GenerateWeather(partial => ShowCompanionPartial(partial, version, agentTriggered: true));
            }
            finally
            {
                if (version == speechRequestVersion) EndModelSpeech();
            }
            if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled
                && brain.CanRunOnCurrentPower && regionCode == preferences.WeatherRegionCode)
            {
                if (text.Length > 0) { FinishCompanionPartial(text, agentTriggered: true); }
                else ShowCompanionNotice("主人，" + brain.Status + "。", manual: true);
            }
        }
        finally
        {
            if (version == speechRequestVersion) {
                weatherRequestPending = false;
                nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            }
        }
    }

    private async Task SpeakCompanionTest()
    {
        if (companionClosed || exitAnimation) return;
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (await TryManualAgentSpeech(false)) return;
        if (!preferences.ModelEnabled)
        {
            ShowCompanionNotice("主人，目前關閉了「啟用模型說話」，請到設定裡開啟，並確認供電設定後再測試～", manual: true);
            return;
        }
        if (!brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice(BatteryPauseNotice, true, true);
            return;
        }
        // A test directly enters the same single handler used when the ordinary
        // speech countdown reaches zero.
        speechForegroundOverride = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        await GenerateLocalSpeechAsync(true);
    }

    private async Task<bool> TryManualAgentSpeech(bool weather)
    {
#if COMPANION_SAFE
        await Task.CompletedTask; return false;
#else
        if (!AgentDailyActive) return false;
        DiagnosticLog.Write(DiagnosticEvent.SpeechRequested, kind: weather ? "weather" : "test", state: "agent");
        try
        {
            if (weather) { weatherRequestPending = true; BeginModelSpeech(); }
            else ShowCompanionNotice("主人，正在請 Agent 回應這次說話測試～", true, true);
            string context = weather ? await brain.GetWeatherContext(System.Threading.CancellationToken.None) : "";
            if (companionClosed || exitAnimation) return true;
            bool accepted = agent?.ManualSpeech(weather, context) == true;
            DiagnosticLog.Write(DiagnosticEvent.AgentMaterials, kind: weather ? "weather" : "test", state: accepted ? "success" : "blocked");
            if (!accepted && !weather) ShowCompanionNotice("主人，Agent 還沒準備好，或是要求太多排不進去了，請看看連線狀態再試一次～", true, true);
        }
        catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.AgentFailed, kind: weather ? "weather" : "test", error: error); if (!weather) ShowCompanionNotice("主人，" + error.Message, true, true); }
        finally { if (weather) { weatherRequestPending = false; if (agent?.AwaitingSpeech != true) EndModelSpeech(); } }
        return true;
#endif
    }

    private const string OpenMeteoWeatherSource = "天氣來自 Open-Meteo（Weather data by Open-Meteo.com），依所選地點中心附近估算；美國地點使用華氏溫度和英里／小時。海外城市資料來自 GeoNames（CC BY 4.0）。";

    private void AddCompanionRegionSelectors(Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = "天氣地點", Margin = new Thickness(0, 10, 0, 4) });
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        (ComboBox Combo, TextBlock Caption, StackPanel Column) Selector(string name, string caption, int column)
        {
            var columnPanel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0) };
            var label = new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            columnPanel.Children.Add(label);
            // The lists keep their data files' Simplified names, codes and order; pickers show and search the Traditional
            // script form (CompanionRegions.DisplayName), as on macOS and Linux.
            var combo = new ComboBox { Name = name, DisplayMemberPath = "Display", SelectedValuePath = "Code", MinHeight = 30 };
            TextSearch.SetTextPath(combo, "Display");
            System.Windows.Automation.AutomationProperties.SetName(combo, caption);
            columnPanel.Children.Add(combo);
            Grid.SetColumn(columnPanel, column);
            grid.Children.Add(columnPanel);
            return (combo, label, columnPanel);
        }
        // Country first (China first, then by English name with 臺灣 Taiwan among them, as in the original), then that
        // country's levels: 臺灣 縣市 → 鄉鎮市區, 中國 three levels, the US and the rest two. The captions below are
        // placeholders; Configure shows the chosen country's own.
        var countrySelector = Selector("WeatherCountry", "國家", 0);
        var country = countrySelector.Combo;
        Grid.SetColumnSpan(countrySelector.Column, 3);
        countrySelector.Column.Margin = new Thickness(0, 0, 0, 8);
        var levels = new[]
        {
            Selector("WeatherProvince", "縣市", 0),
            Selector("WeatherCity", "鄉鎮市區", 1),
            Selector("WeatherDistrict", "區", 2),
        };
        foreach (var level in levels) Grid.SetRow(level.Column, 1);
        // The CWA authorization code only serves Taiwan locations, so it shows with 臺灣.
        var cwa = new StackPanel();
        cwa.Children.Add(new TextBlock { Text = "中央氣象署授權碼（以目前 Windows 帳戶加密儲存，不提供給 MCP）", Margin = new Thickness(0, 12, 0, 4) });
        var cwaKey = new PasswordBox { Password = preferences.CwaApiKey, Padding = new Thickness(6, 4, 6, 4) };
        System.Windows.Automation.AutomationProperties.SetName(cwaKey, "中央氣象署授權碼");
        // The weather uses the code once typing pauses (or the box loses focus): a code applied keystroke by keystroke
        // would send the CWA a request with every half-typed code.
        var cwaPause = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void ApplyCwaKey() { cwaPause.Stop(); brain.CwaApiKey = preferences.CwaApiKey; }
        cwaPause.Tick += (_, _) => ApplyCwaKey();
        cwaKey.PasswordChanged += (_, _) =>
        {
            preferences.CwaApiKey = cwaKey.Password;
            SaveCompanion();
            cwaPause.Stop(); cwaPause.Start();
        };
        cwaKey.LostKeyboardFocus += (_, _) => { if (cwaPause.IsEnabled) ApplyCwaKey(); };
        cwa.Children.Add(cwaKey);
        cwa.Children.Add(new TextBlock { Text = "填入授權碼後，臺灣地點使用中央氣象署的鄉鎮天氣預報；沒有授權碼時，改用 Open-Meteo 估計鄉鎮市區中心附近的天氣，只是概略的參考。授權碼可在氣象資料開放平臺免費申請。", FontSize = 11,
            Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var source = new TextBlock { FontSize = 11, Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) };
        bool changing = false;
        CompanionCountry current = CompanionRegions.China;
        void Configure(CompanionCountry target)
        {
            current = target;
            for (int i = 0; i < levels.Length; i++)
            {
                var visible = i <= target.LeafLevel;
                levels[i].Column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                levels[i].Caption.Text = visible ? target.Captions[i] : "";
                System.Windows.Automation.AutomationProperties.SetName(levels[i].Combo, levels[i].Caption.Text);
            }
            // The leaf column absorbs the space of hidden levels.
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions[i].Width = new GridLength(i <= target.LeafLevel ? 1 : 0, GridUnitType.Star);
            bool taiwan = target == CompanionRegions.Taiwan;
            cwa.Visibility = taiwan ? Visibility.Visible : Visibility.Collapsed;
            source.Text = taiwan
                ? $"天氣資料：中央氣象署 {DateTime.Now.Year} 鄉鎮天氣預報（F-D0047）。此開放資料依政府資料開放授權條款 (Open Government Data License) 進行公眾釋出，使用者於遵守本條款各項規定之前提下，得利用之。政府資料開放授權條款：https://data.gov.tw/license 。"
                    + "沒有授權碼時的估計值來自 Open-Meteo（Weather data by Open-Meteo.com），鄉鎮市區中心依內政部國土測繪中心 2025 鄉(鎮、市、區)界線（政府資料開放授權條款）計算。"
                : OpenMeteoWeatherSource;
        }
        // Fill level `from` and below; keep the matching chain entry when present, else the first item.
        // China's own 台湾省 stays listed (city-level Open-Meteo points); 臺灣 is the country with township forecasts.
        void Fill(int from, string parentCode, CompanionRegion[] keep)
        {
            for (int i = from; i <= current.LeafLevel; i++)
            {
                var items = CompanionRegions.Children(parentCode);
                levels[i].Combo.ItemsSource = items;
                var index = Array.FindIndex(items, x => i < keep.Length && x.Code == keep[i].Code);
                levels[i].Combo.SelectedIndex = items.Length == 0 ? -1 : Math.Max(0, index);
                if (levels[i].Combo.SelectedItem is not CompanionRegion chosen) break;
                parentCode = chosen.Code;
            }
            for (int i = current.LeafLevel + 1; i < levels.Length; i++) levels[i].Combo.ItemsSource = Array.Empty<CompanionRegion>();
        }
        void SaveRegion()
        {
            if (levels[current.LeafLevel].Combo.SelectedItem is not CompanionRegion region) return;
            if (preferences.WeatherRegionCode != region.Code) InvalidateSpeechRequests();
            preferences.WeatherRegionCode = region.Code;
            brain.WeatherRegionCode = region.Code;
            SaveCompanion();
        }
        // An older choice stays saved until the user picks another location here.
        var selected = CompanionRegions.District(preferences.WeatherRegionCode);
        country.ItemsSource = CompanionRegions.Countries;
        country.SelectedItem = CompanionRegions.Country(selected);
        Configure(CompanionRegions.Country(selected));
        Fill(0, current.Root, CompanionRegions.Chain(selected));
        country.SelectionChanged += (_, _) =>
        {
            if (changing || country.SelectedItem is not CompanionCountry target || target == current) return;
            changing = true;
            Configure(target);
            // 臺灣 starts at 臺北市中正區, 中國 at 合肥市蜀山區, other countries at their first entries.
            Fill(0, target.Root, CompanionRegions.DefaultChain(target));
            changing = false;
            SaveRegion();
        };
        for (int level = 0; level < levels.Length; level++)
        {
            var at = level;
            levels[at].Combo.SelectionChanged += (_, _) =>
            {
                if (changing || levels[at].Combo.SelectedItem is not CompanionRegion region) return;
                changing = true;
                if (at < current.LeafLevel) Fill(at + 1, region.Code, []);
                changing = false;
                SaveRegion();
            };
        }
        panel.Children.Add(grid);
        panel.Children.Add(cwa);
        panel.Children.Add(source);
    }
}
