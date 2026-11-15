using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // Entries of the remote panel that are not saved yet. They survive a rebuild of the settings page (after MCP
    // update_settings, or when the keychain answers at startup) and are dropped with the settings window.
    string? remoteUrlDraft, remoteKeyDraft, remoteModelDraft;
    Window? remoteDraftWindow;
    // Settings of the local runner only, switched off while a remote server writes the speech.
    readonly List<Avalonia.Controls.Control> localModelOptions = [];
    readonly SemaphoreSlim modelSourceSaving = new(1, 1);

    // Hands the saved model source to the brain: a remote server replaces the local runner (nothing is loaded on this
    // computer then), null returns to it. Runs at startup before anything asks whether a local model is installed.
    void ApplyModelSource()
    {
        brain.UseRemoteModel(Preferences.UseRemoteModel ? Preferences.RemoteUrl : null, Preferences.RemoteApiKey, Preferences.RemoteModel);
        brain.ScreenImages = Preferences.ScreenImages;
        brain.RefreshModelAvailability();
        nextWarmup = DateTime.MinValue;
    }

    T LocalModelOnly<T>(T control) where T : Avalonia.Controls.Control
    {
        localModelOptions.Add(control);
        control.IsEnabled = !brain.UseRemote;
        return control;
    }

    // 模型與資訊: what 啟用模型說話 covers, 模型來源, and for a remote llama.cpp server its address, API key, model name and
    // the window image option, with 儲存並測試連線. The API key lives in the login keychain on macOS (never in
    // preferences.json, never in MCP get_settings); Linux keeps it in the 0600 preferences file as Base64.
    void BuildModelSourceSettings(StackPanel body)
    {
        localModelOptions.Clear();
        if (!ReferenceEquals(remoteDraftWindow, settings)) { remoteUrlDraft = remoteKeyDraft = remoteModelDraft = null; remoteDraftWindow = settings; }
        body.Children.Add(Paragraph("魚魚用模型自己說話和播報天氣的總開關。使用遠端模型時也要開著，這時不會在這台電腦上載入模型。"));
        body.Children.Add(new TextBlock { Text = "模型來源", Margin = new Thickness(0, 6, 0, 4) });
        var source = new ComboBox { ItemsSource = new[] { "本機內建模型", "遠端 llama.cpp 伺服器（OpenAI 相容介面）" }, SelectedIndex = Preferences.ModelSource == "Remote" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(source);
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 8), IsVisible = source.SelectedIndex == 1 };
        body.Children.Add(panel);
        TextBox Field(string label, string value, string placeholder, Action<string> draft, bool secret = false)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 0) });
            var box = new TextBox { Text = value, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (secret) box.PasswordChar = '•';
            box.TextChanged += (_, _) => draft(box.Text ?? "");
            panel.Children.Add(box);
            return box;
        }
        var url = Field("伺服器網址", remoteUrlDraft ?? Preferences.RemoteUrl, "http://192.168.1.10:8080 或 https://example.com", text => remoteUrlDraft = text);
#if COMPANION_MAC
        const string keyLabel = "API Key（存在 macOS 鑰匙圈，不提供給 MCP；伺服器沒有設定金鑰時留空）";
#else
        const string keyLabel = "API Key（以 Base64 存在本機設定檔，沒有加密，不提供給 MCP；伺服器沒有設定金鑰時留空）";
#endif
        var key = Field(keyLabel, remoteKeyDraft ?? Preferences.RemoteApiKey, "llama-server 的 --api-key", text => remoteKeyDraft = text, secret: true);
        var reveal = new CheckBox { Content = "顯示 API Key" };
        reveal.IsCheckedChanged += (_, _) => key.RevealPassword = reveal.IsChecked == true;
        panel.Children.Add(reveal);
        var model = Field("模型名稱", remoteModelDraft ?? Preferences.RemoteModel, "留空則使用伺服器提供的第一個模型", text => remoteModelDraft = text);
        var images = new CheckBox { Content = "模型支援圖片時，直接傳送視窗截圖（取代 OCR 文字）", IsChecked = Preferences.ScreenImages, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(images);
#if COMPANION_MAC
        const string identity = "前景 App 名稱與視窗標題";
#else
        const string identity = "前景程式名稱與視窗標題";
#endif
        panel.Children.Add(Paragraph("使用遠端模型時，會傳送到上面這台伺服器的內容：魚魚的說話設定、目前時間、" + identity
            + "、前景視窗的文字（在這台電腦上 OCR 辨識的結果），或在勾選上一項、且伺服器支援圖片時，改傳前景視窗的截圖（JPEG，長邊最多 1568 像素）；"
            + "開啟天氣和新聞時，還有天氣地點與預報、新聞內容。只會讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗；最前面是桌面時，只告訴模型目前在桌面。"
#if COMPANION_MAC
            + "連線到區域網路內的伺服器時，macOS 會詢問是否允許尋找區域網路上的裝置，請選「允許」（之後可在「系統設定 › 隱私權與安全性 › 區域網路」更改）。"
#endif
            ));
        var result = Paragraph(
#if COMPANION_MAC
            !macSecretsLoaded ? "正在讀取 macOS 鑰匙圈…" :
#endif
            "填好後按「儲存並測試連線」。");
        var state = Paragraph("");
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void UpdateState() => state.Text = "模型狀態：" + (brain.UseRemote ? brain.Status : Preferences.ModelSource == "Remote" ? "網址無效，目前使用本機模型" : "使用本機模型");
        clock.Tick += (_, _) => UpdateState();
        state.AttachedToVisualTree += (_, _) => { UpdateState(); clock.Start(); };
        state.DetachedFromVisualTree += (_, _) => clock.Stop();

        // The key changes in the keychain first (off the UI thread, up to a few seconds); when that fails nothing is saved.
        async Task<bool> SaveModelSource()
        {
            await modelSourceSaving.WaitAsync();
            try
            {
                string keyText = key.Text?.Trim() ?? "";
                if (keyText != Preferences.RemoteApiKey)
                {
#if COMPANION_MAC
                    if (!macSecretsLoaded) { result.Text = "正在讀取 macOS 鑰匙圈，請稍候再儲存。"; return false; }
                    result.Text = "正在存入 macOS 鑰匙圈…";
                    try { await Task.Run(() => Preferences.RemoteApiKey = keyText); }
                    catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // The log gets the kind of failure only, never the key.
                        DiagnosticLog.Write(DiagnosticEvent.RunnerFailed, kind: "remote", state: "keychain", error: error);
                        result.Text = "無法存入 macOS 鑰匙圈，設定沒有變更：" + error.Message;
                        return false;
                    }
#else
                    Preferences.RemoteApiKey = keyText;
#endif
                }
                Preferences.ModelSource = source.SelectedIndex == 1 ? "Remote" : "Local";
                Preferences.RemoteUrl = url.Text?.Trim() ?? "";
                Preferences.RemoteModel = model.Text?.Trim() ?? "";
                Preferences.ScreenImages = images.IsChecked == true;
                remoteUrlDraft = remoteKeyDraft = remoteModelDraft = null;
                // A reply in progress is dropped, as with every other switch of the model.
                speechVersion++; loading.IsVisible = false;
                ApplyModelSource(); Save();
                foreach (var option in localModelOptions) option.IsEnabled = !brain.UseRemote;
                UpdateState();
                return true;
            }
            finally { modelSourceSaving.Release(); }
        }
        Button? test = null;
        test = ActionButton("儲存並測試連線", async () =>
        {
            test!.IsEnabled = false;
            try
            {
                if (!await SaveModelSource()) return;
                if (!brain.UseRemote) { result.Text = "網址無效：請填寫 http:// 或 https:// 開頭的網址，例如 http://192.168.1.10:8080。目前仍使用本機模型。"; return; }
                result.Text = "正在測試連線…";
                // Saving cancelled whatever the model was doing; it lets go within moments.
                for (int i = 0; i < 30 && brain.Busy; i++) await Task.Delay(100);
                result.Text = "連線測試：" + await brain.TestRemote();
            }
            finally { test.IsEnabled = true; }
        });
#if COMPANION_MAC
        // Until the keychain has answered at startup, a save could be overwritten by the value read before it.
        test.IsEnabled = macSecretsLoaded;
#endif
        panel.Children.Add(test);
        panel.Children.Add(result);
        panel.Children.Add(state);
        source.SelectionChanged += async (_, _) =>
        {
            if (source.SelectedIndex < 0) return;
            panel.IsVisible = source.SelectedIndex == 1;
            if (await SaveModelSource() && source.SelectedIndex == 1 && !brain.UseRemote) result.Text = "填好伺服器網址後，按「儲存並測試連線」。";
        };
        images.IsCheckedChanged += async (_, _) => await SaveModelSource();
    }
}
