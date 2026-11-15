using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.RegularExpressions;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // macOS reads the secrets kept in the login keychain at startup (BigBlueFish.Mac/MacSecrets.cs); Linux has nothing to load.
    partial void LoadPlatformSecrets();

    const string OpenMeteoWeatherSource = "天氣來自 Open-Meteo（Weather data by Open-Meteo.com），依所選地點中心附近估算；美國地點使用華氏溫度和英里／小時。海外城市資料來自 GeoNames（CC BY 4.0）。";
    const string TownshipCentreSource = "臺灣鄉鎮市區中心座標由內政部國土測繪中心「鄉(鎮、市、區)界線(TWD97經緯度)」計算；此開放資料依政府資料開放授權條款 (Open Government Data License) 進行公眾釋出，使用者於遵守本條款各項規定之前提下，得利用之。政府資料開放授權條款：https://data.gov.tw/license";

    // 天氣地點 on the settings page: the country (China first, then by English name with 臺灣 Taiwan among them, as
    // CompanionRegions.Countries lists them), then its levels: 臺灣 縣市 → 鄉鎮市區, China three levels, the rest two.
    // Names show in Traditional script (CompanionRegions.DisplayName); codes and data stay as they are. On macOS, 臺灣
    // also takes the CWA authorization code, which lives in the login keychain.
    void BuildWeatherSettings(StackPanel body)
    {
        body.Children.Add(new TextBlock { Text = "天氣地點", Margin = new Thickness(0, 12, 0, 4) });
        var regions = new StackPanel { Spacing = 8 }; body.Children.Add(regions);
        var country = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = CompanionRegions.Countries.Select(CompanionRegions.DisplayName).ToArray() };
        regions.Children.Add(country);
        var levels = Enumerable.Range(0, 3).Select(_ => new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }).ToArray();
        foreach (var level in levels) regions.Children.Add(level);
        var options = new CompanionRegion[levels.Length][];
        var province = Paragraph("中國清單裡的臺灣省沿用原版資料：天氣以縣市為單位估算，時間為北京時間。要看鄉鎮的天氣，請在國家選「臺灣 Taiwan」。");
        regions.Children.Add(province);
#if COMPANION_MAC
        var cwa = CwaKeySettings(); regions.Children.Add(cwa);
#endif
        var source = Paragraph(""); body.Children.Add(source);
        // An older choice stays selected until the user picks another place here.
        var selected = CompanionRegions.District(Preferences.WeatherRegionCode); var current = CompanionRegions.Country(selected); bool changing = true;
        void Describe()
        {
            bool taiwan = current == CompanionRegions.Taiwan;
            province.IsVisible = current == CompanionRegions.China && levels[0].SelectedIndex >= 0 && options[0][levels[0].SelectedIndex].Code == "71";
#if COMPANION_MAC
            cwa.IsVisible = taiwan;
            source.Text = taiwan ? $"填了授權碼時，臺灣鄉鎮使用中央氣象署 {DateTime.Now.Year} 鄉鎮天氣預報（F-D0047）。此開放資料依政府資料開放授權條款 (Open Government Data License) 進行公眾釋出，使用者於遵守本條款各項規定之前提下，得利用之。"
                + "沒有授權碼時改用 Open-Meteo（Weather data by Open-Meteo.com）估計鄉鎮市區中心附近的天氣，只是概略參考。" + TownshipCentreSource : OpenMeteoWeatherSource;
#else
            source.Text = taiwan ? "臺灣鄉鎮的天氣來自 Open-Meteo（Weather data by Open-Meteo.com），估計鄉鎮市區中心附近的天氣，只是概略參考（Linux 版不使用中央氣象署授權碼）。" + TownshipCentreSource : OpenMeteoWeatherSource;
#endif
        }
        void Fill(int from, string parentCode, CompanionRegion[] keep) {
            for (int i = from; i < levels.Length; i++) {
                levels[i].IsVisible = i <= current.LeafLevel;
                options[i] = i <= current.LeafLevel ? CompanionRegions.Children(parentCode) : [];
                levels[i].ItemsSource = options[i].Select(CompanionRegions.DisplayName).ToArray();
                levels[i].SelectedIndex = options[i].Length == 0 ? -1 : Math.Max(0, Array.FindIndex(options[i], r => i < keep.Length && r.Code == keep[i].Code));
                if (levels[i].SelectedIndex >= 0) parentCode = options[i][levels[i].SelectedIndex].Code;
            }
            Describe();
        }
        country.SelectedIndex = Array.IndexOf(CompanionRegions.Countries, current); Fill(0, current.Root, CompanionRegions.Chain(selected)); changing = false;
        void RegionChanged() { var leaf = levels[current.LeafLevel]; if (leaf.SelectedIndex >= 0 && leaf.SelectedIndex < options[current.LeafLevel].Length) { Preferences.WeatherRegionCode = options[current.LeafLevel][leaf.SelectedIndex].Code; ApplySettings(); } }
        // 臺灣 starts at 臺北市中正區, China at 合肥市蜀山區, other countries at their first entries.
        country.SelectionChanged += (_,_) => { if (changing || country.SelectedIndex < 0 || CompanionRegions.Countries[country.SelectedIndex] == current) return; changing = true; current = CompanionRegions.Countries[country.SelectedIndex]; Fill(0, current.Root, CompanionRegions.DefaultChain(current)); changing = false; RegionChanged(); };
        for (int level = 0; level < levels.Length; level++) { var at = level; levels[at].SelectionChanged += (_,_) => { if (changing || levels[at].SelectedIndex < 0) return; changing = true; if (at < current.LeafLevel) Fill(at + 1, options[at][levels[at].SelectedIndex].Code, []); changing = false; RegionChanged(); }; }
    }

#if COMPANION_MAC
    // The CWA authorization code: saved to the login keychain (never to preferences.json, never to MCP) and tried at once
    // on the township the weather uses, or 臺北市中正區 when the weather is elsewhere.
    StackPanel CwaKeySettings()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "中央氣象署授權碼（存在 macOS 鑰匙圈，不提供給 MCP）" });
        var key = new TextBox { Text = Preferences.CwaApiKey, PasswordChar = '•', PlaceholderText = "CWA-XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX", HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(key);
        var reveal = new CheckBox { Content = "顯示授權碼" };
        reveal.IsCheckedChanged += (_, _) => key.RevealPassword = reveal.IsChecked == true;
        panel.Children.Add(reveal);
        var status = Paragraph(macSecretsLoaded ? "填入授權碼後，臺灣鄉鎮使用中央氣象署的鄉鎮天氣預報；沒有授權碼時，改用 Open-Meteo 估計鄉鎮市區中心附近的天氣。授權碼可在中央氣象署氣象資料開放平臺免費申請。"
            : "正在讀取 macOS 鑰匙圈…");
        var buttons = new WrapPanel();
        Button? save = null, remove = null;
        async Task Store(string value, bool test)
        {
            if (value.Length > 0 && !CwaKeyPattern().IsMatch(value)) { status.Text = "授權碼格式不正確：只能有英文字母、數字和連字號，例如 CWA-1234ABCD-…。"; return; }
            save!.IsEnabled = remove!.IsEnabled = false;
            try
            {
                if (value != Preferences.CwaApiKey)
                {
                    status.Text = "正在存入 macOS 鑰匙圈…";
                    try { await Task.Run(() => Preferences.CwaApiKey = value); }
                    catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // The weather keeps the code it had; nothing is written to preferences.json instead.
                        DiagnosticLog.Write(DiagnosticEvent.WeatherFailed, kind: "write", error: error);
                        status.Text = "無法存入 macOS 鑰匙圈，授權碼沒有變更：" + error.Message;
                        return;
                    }
                    Save(); brain.CwaApiKey = value;
                }
                if (value.Length == 0) { key.Text = ""; status.Text = "已從鑰匙圈移除授權碼，臺灣鄉鎮改用 Open-Meteo 估計。"; return; }
                status.Text = test ? "已存入鑰匙圈，正在向中央氣象署查詢…" : "已存入鑰匙圈。";
                if (!test) return;
                var district = CompanionRegions.District(Preferences.WeatherRegionCode);
                if (!CompanionRegions.IsTaiwan(district)) district = CompanionRegions.District(CompanionRegions.DefaultCode);
                try
                {
                    using var web = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    using var probe = new CompanionWeather(web) { CwaApiKey = value };
                    await probe.Context(district.Code, CancellationToken.None);
                    status.Text = $"授權碼可以使用：已取得{CompanionRegions.FullName(district)}的中央氣象署鄉鎮預報。";
                }
                catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException)
                {
                    status.Text = "授權碼已存入，但測試沒有成功：" + error.Message;
                }
            }
            finally { save.IsEnabled = remove.IsEnabled = true; }
        }
        save = ActionButton("儲存並測試", async () => await Store(key.Text?.Trim() ?? "", true));
        remove = ActionButton("移除授權碼", async () => await Store("", false));
        // Until the keychain has answered at startup, a save could be overwritten by the value read before it.
        save.IsEnabled = remove.IsEnabled = macSecretsLoaded;
        buttons.Children.Add(save); buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        panel.Children.Add(status);
        return panel;
    }

    [GeneratedRegex(@"^[A-Za-z0-9-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CwaKeyPattern();
#endif
}
