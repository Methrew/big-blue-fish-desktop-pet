// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // Entries of the remote and cloud panels that are not saved yet. They survive a rebuild of the settings page (after MCP
    // update_settings, or when the keychain answers at startup) and are dropped with the settings window. The cloud panel
    // keeps a key and a model per service, so switching 服務 back and forth loses nothing typed.
    string? remoteUrlDraft, remoteKeyDraft, remoteModelDraft, cloudProviderDraft;
    readonly Dictionary<string, string> cloudKeyDrafts = new(StringComparer.Ordinal), cloudModelDrafts = new(StringComparer.Ordinal);
    // The models 儲存並測試 listed for each service in this settings window.
    readonly Dictionary<string, IReadOnlyList<CloudModelInfo>> cloudListed = new(StringComparer.Ordinal);
    Window? remoteDraftWindow;
    // --test-mode only (cloud-models): the cloud panel takes a model list as if 儲存並測試 had listed it; no key, nothing sent.
    Action<IReadOnlyList<CloudModelInfo>>? cloudModelsListed;
    // Settings of the local runner only, switched off while a remote server or a cloud AI service writes the speech.
    readonly List<Avalonia.Controls.Control> localModelOptions = [];
    readonly SemaphoreSlim modelSourceSaving = new(1, 1);

    static readonly string[] ModelSourceLabels = ["本機內建模型", "遠端 llama.cpp 伺服器（OpenAI 相容介面）", "雲端 AI 服務（Claude、ChatGPT、Gemini）"];
    static int ModelSourceIndex(string source) => source switch { "Remote" => 1, "Cloud" => 2, _ => 0 };
    static string ModelSourceOf(int index) => index switch { 1 => "Remote", 2 => "Cloud", _ => "Local" };
#if COMPANION_MAC
    const string ForegroundIdentity = "前景 App 名稱與視窗標題", OnThisComputer = "在這台 Mac 上";
#else
    const string ForegroundIdentity = "前景程式名稱與視窗標題", OnThisComputer = "在這台電腦上";
#endif
    // Claude, ChatGPT, Gemini: the service without its company.
    static string CloudShortName(CloudProviderInfo info) => info.DisplayName.Split('（')[0];
    static string CloudCompany(string provider) => CompanionCloud.Company(provider);
    static string CloudConsole(string provider) => provider switch { CompanionCloud.OpenAI => "OpenAI Platform", CompanionCloud.Gemini => "Google AI Studio", _ => "Claude Console" };
    static string CloudKeyPrefix(string provider) => provider switch { CompanionCloud.OpenAI => "sk-", CompanionCloud.Gemini => "AIza 或 AQ.", _ => "sk-ant-" };

    // The confirmation before the cloud source is chosen (in the settings, or from the first-run 安裝本機模型 window): which
    // service writes the speech, what each line sends (whether the screenshot option, shared with the remote source, is on
    // now), where the key stays, and that every line is billed.
    string CloudConsent(string provider)
    {
        var info = CompanionCloud.Info(provider);
        string others = string.Join(" 或 ", CompanionCloud.Providers.Where(p => p.Id != info.Id).Select(CloudShortName));
        return "改用後，魚魚要說的話會由 " + info.DisplayName + "產生，之後也可以在「服務」改選 " + others + "。每次說話會傳送：魚魚的說話設定、目前時間、"
            + ForegroundIdentity + "、" + OnThisComputer + "辨識的前景視窗文字（勾選「傳送前景視窗截圖」時改傳截圖，" + (Preferences.ScreenImages ? "目前已開啟" : "目前關閉")
            + "）；開啟天氣和新聞時，還有天氣地點與預報、新聞內容。\n\n"
#if COMPANION_MAC && COMPANION_SAFE
            + "API Key 存在這台 Mac 的鑰匙圈，不會寫進設定檔。"
#elif COMPANION_MAC
            + "API Key 存在這台 Mac 的鑰匙圈，不會寫進設定檔，也不提供給 MCP 和 Agent。"
#elif COMPANION_SAFE
            + "API Key 以 Base64 存在本機設定檔（沒有加密）。"
#else
            + "API Key 以 Base64 存在本機設定檔（沒有加密），不提供給 MCP 和 Agent。"
#endif
            + "每次說話都會依用量計費，建議在服務的主控台設定花費上限，並建立一把只給魚魚用的 API Key。";
    }

    // Hands the saved model source to the brain: a cloud AI service or a remote server replaces the local runner (nothing
    // is loaded on this computer then), null returns to it. Runs at startup before anything asks whether a local model is
    // installed; with the cloud source chosen the brain takes it even before the key is read, so none is asked for.
    void ApplyModelSource()
    {
        // The limit first: the cloud status says when today's limit is already reached.
        brain.CloudDailyLimit = Preferences.CloudDailyLimit;
        if (Preferences.CloudSelected)
            brain.UseCloudModel(Preferences.CloudProvider, Preferences.CloudApiKey(Preferences.CloudProvider), Preferences.CloudModel(Preferences.CloudProvider));
        else brain.UseRemoteModel(Preferences.UseRemoteModel ? Preferences.RemoteUrl : null, Preferences.RemoteApiKey, Preferences.RemoteModel);
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
    // the window image option, with 儲存並測試連線; for a cloud AI service (Claude, ChatGPT, Gemini) the service, its API key,
    // the model, the daily limit, the usage and the window image option, with 儲存並測試. The API keys live in the login
    // keychain on macOS (never in preferences.json, never in MCP get_settings); Linux keeps them in the 0600 preferences
    // file as Base64.
    void BuildModelSourceSettings(StackPanel body)
    {
        localModelOptions.Clear();
        if (!ReferenceEquals(remoteDraftWindow, settings))
        {
            remoteUrlDraft = remoteKeyDraft = remoteModelDraft = cloudProviderDraft = null;
            cloudKeyDrafts.Clear(); cloudModelDrafts.Clear(); cloudListed.Clear();
            remoteDraftWindow = settings;
        }
        body.Children.Add(Paragraph("魚魚用模型自己說話和播報天氣的總開關。使用遠端模型或雲端 AI 服務時也要開著，這時不會在這台電腦上載入模型。"));
        body.Children.Add(new TextBlock { Text = "模型來源", Margin = new Thickness(0, 6, 0, 4) });
        var source = new ComboBox { ItemsSource = ModelSourceLabels, SelectedIndex = ModelSourceIndex(Preferences.ModelSource), HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(source);
        // The source in use (confirmed): the box shows the cloud source while its confirmation is open, and a save
        // meanwhile must not choose it.
        int shownSource = source.SelectedIndex;
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 8), IsVisible = source.SelectedIndex == 1 };
        body.Children.Add(panel);
        var cloudPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 8), IsVisible = source.SelectedIndex == 2 };
        body.Children.Add(cloudPanel);
        static void Label(StackPanel target, string text) => target.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        TextBox Field(string label, string value, string placeholder, Action<string> draft, bool secret = false)
        {
            Label(panel, label);
            var box = new TextBox { Text = value, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (secret) box.PasswordChar = '•';
            // PropertyChanged, not TextChanged: Avalonia raises TextChanged later (asynchronously), so the value set above
            // would come back as a draft and stand for the saved one when the page is rebuilt.
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) draft(box.Text ?? ""); };
            panel.Children.Add(box);
            return box;
        }
        var url = Field("伺服器網址", remoteUrlDraft ?? Preferences.RemoteUrl, "http://192.168.1.10:8080 或 https://example.com", text => remoteUrlDraft = text);
#if COMPANION_MAC && COMPANION_SAFE
        const string keyLabel = "API Key（存在 macOS 鑰匙圈；伺服器沒有設定金鑰時留空）";
        const string cloudKeyLabel = "API Key（存在 macOS 鑰匙圈）";
#elif COMPANION_MAC
        const string keyLabel = "API Key（存在 macOS 鑰匙圈，不提供給 MCP；伺服器沒有設定金鑰時留空）";
        const string cloudKeyLabel = "API Key（存在 macOS 鑰匙圈，不提供給 MCP）";
#elif COMPANION_SAFE
        const string keyLabel = "API Key（以 Base64 存在本機設定檔，沒有加密；伺服器沒有設定金鑰時留空）";
        const string cloudKeyLabel = "API Key（以 Base64 存在本機設定檔，沒有加密）";
#else
        const string keyLabel = "API Key（以 Base64 存在本機設定檔，沒有加密，不提供給 MCP；伺服器沒有設定金鑰時留空）";
        const string cloudKeyLabel = "API Key（以 Base64 存在本機設定檔，沒有加密，不提供給 MCP）";
#endif
        var key = Field(keyLabel, remoteKeyDraft ?? Preferences.RemoteApiKey, "llama-server 的 --api-key", text => remoteKeyDraft = text, secret: true);
        var reveal = new CheckBox { Content = "顯示 API Key" };
        reveal.IsCheckedChanged += (_, _) => key.RevealPassword = reveal.IsChecked == true;
        panel.Children.Add(reveal);
#if COMPANION_MAC
        bool secretsReady = macSecretsLoaded && !macSecretsReading;
        if (secretsReady && SecretNote(MacKeychain.RemoteAccount, "API Key") is { } keyNote)
        {
            panel.Children.Add(Paragraph(keyNote));
            if (Preferences.StateOf(MacKeychain.RemoteAccount) == CompanionPreferences.SecretState.Failed)
                panel.Children.Add(ActionButton("重新讀取鑰匙圈", ReadSecrets));
        }
#endif
        var model = Field("模型名稱", remoteModelDraft ?? Preferences.RemoteModel, "留空則使用伺服器提供的第一個模型", text => remoteModelDraft = text);
        var images = new CheckBox { Content = "模型支援圖片時，直接傳送視窗截圖（取代 OCR 文字）", IsChecked = Preferences.ScreenImages, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(images);
        panel.Children.Add(Paragraph("使用遠端模型時，會傳送到上面這台伺服器的內容：魚魚的說話設定、目前時間、" + ForegroundIdentity
            + "、前景視窗的文字（在這台電腦上 OCR 辨識的結果），或在勾選上一項、且伺服器支援圖片時，改傳前景視窗的截圖（JPEG，長邊最多 1568 像素）；"
            + "開啟天氣和新聞時，還有天氣地點與預報、新聞內容。只會讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗；最前面是桌面時，只告訴模型目前在桌面。"
#if COMPANION_MAC
            + "連線到區域網路內的伺服器時，macOS 會詢問是否允許尋找區域網路上的裝置，請選「允許」（之後可在「系統設定 › 隱私權與安全性 › 區域網路」更改）。"
#endif
            ));
        var result = Paragraph(
#if COMPANION_MAC
            !secretsReady ? "正在讀取 macOS 鑰匙圈…" :
#endif
            "填好後按「儲存並測試連線」。");
        var state = Paragraph("");

        // The cloud panel. The service, key and model shown are drafts until 儲存並測試, which alone writes a key; the daily
        // limit and the image option are saved as they change.
        string provider = CompanionCloud.Info(cloudProviderDraft ?? Preferences.CloudProvider).Id;
        bool filling = false;
        Label(cloudPanel, "服務");
        var service = new ComboBox { ItemsSource = CompanionCloud.Providers.Select(p => p.DisplayName).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = Array.FindIndex(CompanionCloud.Providers, p => p.Id == provider) };
        cloudPanel.Children.Add(service);
        var keyHint = Paragraph("");
        keyHint.Margin = new Thickness(0, 8, 0, 0);
        cloudPanel.Children.Add(keyHint);
        cloudPanel.Children.Add(ActionButton("取得 API Key", () => OpenUrl(CompanionCloud.Info(provider).KeyPage)));
        Label(cloudPanel, cloudKeyLabel);
        var cloudKey = new TextBox { PasswordChar = '•', HorizontalAlignment = HorizontalAlignment.Stretch };
        Avalonia.Automation.AutomationProperties.SetName(cloudKey, "API Key");
        cloudPanel.Children.Add(cloudKey);
        var cloudReveal = new CheckBox { Content = "顯示 API Key" };
        cloudReveal.IsCheckedChanged += (_, _) => cloudKey.RevealPassword = cloudReveal.IsChecked == true;
        cloudPanel.Children.Add(cloudReveal);
#if COMPANION_MAC
        var cloudKeyNote = Paragraph("");
        var cloudRetry = ActionButton("重新讀取鑰匙圈", ReadSecrets);
        cloudPanel.Children.Add(cloudKeyNote);
        cloudPanel.Children.Add(cloudRetry);
#endif
        Label(cloudPanel, "模型");
        var cloudModel = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        cloudPanel.Children.Add(cloudModel);
        var modelHint = Paragraph("");
        modelHint.Margin = new Thickness(0, 4, 0, 0);
        var costHint = Paragraph("");
        cloudPanel.Children.Add(modelHint);
        cloudPanel.Children.Add(costHint);
        Label(cloudPanel, "每天最多");
        var limits = CompanionPreferences.CloudDailyLimitChoices;
        var limit = new ComboBox { ItemsSource = limits.Select(n => n > 0 ? n + " 次" : "不限制").ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = Math.Max(0, Array.IndexOf(limits, Preferences.CloudDailyLimit)) };
        cloudPanel.Children.Add(limit);
        var limitHint = Paragraph("每次說話算一次請求；到了上限魚魚會先安靜，隔天自動恢復。");
        limitHint.Margin = new Thickness(0, 4, 0, 0);
        cloudPanel.Children.Add(limitHint);
        var cloudImages = new CheckBox { Content = "傳送前景視窗截圖（取代 OCR 文字；每句話用掉的 token 較多）", IsChecked = Preferences.ScreenImages, Margin = new Thickness(0, 8, 0, 0) };
        cloudPanel.Children.Add(cloudImages);
        var privacy = Paragraph("");
        cloudPanel.Children.Add(privacy);
        var cloudResult = Paragraph("");
        var usage = Paragraph("");
        var cloudState = Paragraph("");

        string ModelText() => (cloudModel.Text ?? cloudModel.SelectedItem as string ?? "").Trim();
        IReadOnlyList<CloudModelInfo> Listed(string p) => cloudListed.TryGetValue(p, out var listed) ? listed : brain.CloudProvider == p ? brain.CloudModels : [];
        void UpdateModelHints()
        {
            var listed = Listed(provider);
            string chosen = ModelText() is { Length: > 0 } typed ? typed : CompanionCloud.DefaultModel(provider);
            modelHint.Text = listed.Count > 0 ? "已列出這把 API Key 可用的 " + listed.Count + " 個模型，也可以直接輸入其他模型名稱。"
                : "按「儲存並測試」後會列出這把 API Key 可用的模型；也可以直接輸入模型名稱，清空時使用建議的 " + CompanionCloud.DefaultModel(provider) + "。";
            string named = listed.FirstOrDefault(m => m.Id == chosen) is { } info && info.DisplayName.Length > 0 && info.DisplayName != chosen ? info.DisplayName + "：" : "";
            costHint.Text = CompanionCloud.CostHint(provider, chosen) is { } cost
                ? named + "每句話" + cost + "（粗估，以附上視窗截圖的一句話計算；實際以 " + CloudCompany(provider) + " 的帳單為準）。"
                    + (provider == CompanionCloud.Claude && chosen == "claude-opus-5-5"
                        ? "想省一點可以改選 claude-sonnet-5-5（" + CompanionCloud.CostHint(provider, "claude-sonnet-5-5") + "）或 claude-haiku-5-5（" + CompanionCloud.CostHint(provider, "claude-haiku-5-5") + "）。" : "")
                : named + "這個模型沒有費用估計，請參考 " + CloudCompany(provider) + " 的價格說明。";
        }
        void FillModels(string text)
        {
            var listed = Listed(provider);
            cloudModel.ItemsSource = listed.Count > 0 ? listed.Select(m => m.Id).ToArray() : CompanionCloud.Info(provider).PreferredModels.ToArray();
            cloudModel.Text = text;
            UpdateModelHints();
        }
        string NoKeyYet() => "貼上 " + CompanionCloud.Info(provider).Name + " 的 API Key 後按「儲存並測試」。";
        // Shows a service's key, model, hints and notes; nothing is saved.
        void ShowProvider(string id)
        {
            filling = true;
            try
            {
                provider = CompanionCloud.Info(id).Id;
                var info = CompanionCloud.Info(provider);
                service.SelectedIndex = Array.FindIndex(CompanionCloud.Providers, p => p.Id == provider);
                keyHint.Text = "到 " + CloudConsole(provider) + " 建立 API Key，複製後貼到下面。建議另外建立一把只給魚魚用的 API Key，並在主控台設定花費上限。";
                cloudKey.Text = cloudKeyDrafts.TryGetValue(provider, out var typed) ? typed : Preferences.CloudApiKey(provider);
                cloudKey.PlaceholderText = "貼上 " + info.Name + " 的 API Key（" + CloudKeyPrefix(provider) + " 開頭）";
                string saved = Preferences.CloudModel(provider);
                FillModels(cloudModelDrafts.TryGetValue(provider, out var drafted) ? drafted : saved.Length > 0 ? saved : CompanionCloud.DefaultModel(provider));
#if COMPANION_MAC
                string account = CompanionPreferences.CloudAccount(provider);
                var note = secretsReady ? SecretNote(account, "API Key") : null;
                cloudKeyNote.Text = note ?? "";
                cloudKeyNote.IsVisible = note != null;
                cloudRetry.IsVisible = secretsReady && Preferences.StateOf(account) == CompanionPreferences.SecretState.Failed;
#endif
                privacy.Text = "使用雲端 AI 服務時，每次說話會傳送到 " + info.DisplayName + "的內容：魚魚的說話設定、目前時間、" + ForegroundIdentity
                    + "、前景視窗的文字（" + OnThisComputer + " OCR 辨識的結果），或在勾選上一項時，改傳前景視窗的截圖（JPEG，長邊最多 1568 像素）；"
                    + "開啟天氣和新聞時，還有天氣地點與預報、新聞內容。只會讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗；最前面是桌面時，只告訴模型目前在桌面。"
                    + "傳送的內容依 " + CloudCompany(provider) + " 的 API 條款處理。";
            }
            finally { filling = false; }
        }
        ShowProvider(provider);
        cloudResult.Text =
#if COMPANION_MAC
            !secretsReady ? "正在讀取 macOS 鑰匙圈…" :
#endif
            provider != Preferences.CloudProvider ? "按「儲存並測試」後改用 " + CompanionCloud.Info(provider).DisplayName + "。"
            : cloudKey.Text?.Trim().Length > 0 ? "按「儲存並測試」確認 API Key 並列出可用的模型，不會用掉 token。" : NoKeyYet();

        // One clock for both panels' state lines and the usage; both panels stay in the page (hidden or not), so the
        // cloud state line's attachment stands for the page's.
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        // The cloud source without a key (none entered, or the keychain not read): the brain waits quietly, and the line says why.
        string ModelStatus()
        {
            string id = Preferences.CloudProvider;
            if (!brain.UseCloud || Preferences.CloudApiKey(id).Length > 0) return brain.Status;
            string name = CompanionCloud.Info(id).Name;
#if COMPANION_MAC
            if (!macSecretsLoaded || macSecretsReading) return "正在讀取 macOS 鑰匙圈";
            if (Preferences.StateOf(CompanionPreferences.CloudAccount(id)) == CompanionPreferences.SecretState.Failed) return "無法讀取 macOS 鑰匙圈裡 " + name + " 的 API Key，魚魚先不說話";
#endif
            return "尚未填寫 " + name + " 的 API Key，魚魚先不說話";
        }
        void UpdateState()
        {
            state.Text = cloudState.Text = "模型狀態：" + (brain.UseRemote ? ModelStatus() : Preferences.ModelSource != "Remote" ? "使用本機模型"
                : Preferences.RemoteUrl.Trim().Length == 0 ? "尚未填寫伺服器網址，目前使用本機模型" : "網址無效，目前使用本機模型");
            usage.Text = "雲端用量：" + brain.CloudUsage.Describe(Preferences.CloudDailyLimit);
            if (brain.CloudPausedUntil is { } until)
                usage.Text += "\n雲端請求暫停中，" + until.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    + " 自動恢復；換用其他服務、API Key 或模型，或按「儲存並測試」成功後也會解除。";
        }
        clock.Tick += (_, _) => UpdateState();
        cloudState.AttachedToVisualTree += (_, _) => { UpdateState(); clock.Start(); };
        cloudState.DetachedFromVisualTree += (_, _) => clock.Stop();

        // The key changes in the keychain first (off the UI thread, up to a few seconds); when that fails nothing is saved.
        // explicitSave: 儲存並測試連線. A switch of the source or the image option leaves a key that is not loaded as it is:
        // the field is empty then because the keychain did not answer, not because the user emptied it. The cloud panel's
        // drafts are left to its own 儲存並測試.
        async Task<bool> SaveModelSource(bool explicitSave)
        {
            await modelSourceSaving.WaitAsync();
            try
            {
                string keyText = key.Text?.Trim() ?? "";
#if COMPANION_MAC
                var keyState = Preferences.StateOf(MacKeychain.RemoteAccount);
                bool writeKey = keyState == CompanionPreferences.SecretState.Loaded ? keyText != Preferences.RemoteApiKey
                    // Failed: only a key the user typed replaces the unread one. Legacy and Unsaved: an explicit save writes it,
                    // which moves it into the keychain or says why the keychain refuses it.
                    : explicitSave && (keyState != CompanionPreferences.SecretState.Failed || keyText.Length > 0);
#else
                bool writeKey = keyText != Preferences.RemoteApiKey;
#endif
                if (writeKey)
                {
#if COMPANION_MAC
                    if (!macSecretsLoaded || macSecretsReading) { result.Text = "正在讀取 macOS 鑰匙圈，請稍候再儲存。"; return false; }
                    result.Text = keyText.Length == 0 ? "正在從 macOS 鑰匙圈移除 API Key…" : "正在存入 macOS 鑰匙圈…";
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
                bool wasRemote = brain.UseRemote;
                Preferences.ModelSource = ModelSourceOf(shownSource);
                Preferences.RemoteUrl = url.Text?.Trim() ?? "";
                Preferences.RemoteModel = model.Text?.Trim() ?? "";
                Preferences.ScreenImages = images.IsChecked == true;
                remoteUrlDraft = remoteKeyDraft = remoteModelDraft = null;
                // Still the local model, as before (the remote source picked without a valid address yet): the resident
                // local model stays loaded. Any other switch drops a reply in progress, as with every other switch of the model.
                if (!wasRemote && !Preferences.UseRemoteModel && !Preferences.CloudSelected && !writeKey) { brain.ScreenImages = Preferences.ScreenImages; Save(); UpdateState(); return true; }
                speechVersion++; loading.IsVisible = false;
                ApplyModelSource(); Save();
                foreach (var option in localModelOptions) option.IsEnabled = !brain.UseRemote;
                UpdateState();
                return true;
            }
            finally { modelSourceSaving.Release(); }
        }

        // 儲存並測試 of the cloud panel: the shown service's key (into the keychain on macOS, off the UI thread), the service
        // and its model become the cloud source. A key that belongs to another service is not saved.
        async Task<bool> SaveCloud()
        {
            await modelSourceSaving.WaitAsync();
            try
            {
                string id = provider, keyText = cloudKey.Text?.Trim() ?? "";
                if (keyText.Length > 0 && CompanionCloud.ProviderForKey(keyText) is { } owner && owner != id)
                {
                    cloudResult.Text = "這把 API Key 是 " + CompanionCloud.Info(owner).DisplayName + "的，請把「服務」改成 " + CloudShortName(CompanionCloud.Info(owner))
                        + "，或貼上 " + CompanionCloud.Info(id).Name + " 的 API Key。設定沒有變更。";
                    return false;
                }
#if COMPANION_MAC
                if (!macSecretsLoaded || macSecretsReading) { cloudResult.Text = "正在讀取 macOS 鑰匙圈，請稍候再儲存。"; return false; }
                var keyState = Preferences.StateOf(CompanionPreferences.CloudAccount(id));
                // As for the remote key: Failed is replaced only by a typed key; Legacy and Unsaved are written (moved) now.
                bool writeKey = keyState == CompanionPreferences.SecretState.Loaded ? keyText != Preferences.CloudApiKey(id)
                    : keyState != CompanionPreferences.SecretState.Failed || keyText.Length > 0;
#else
                bool writeKey = keyText != Preferences.CloudApiKey(id);
#endif
                if (writeKey)
                {
#if COMPANION_MAC
                    cloudResult.Text = keyText.Length == 0 ? "正在從 macOS 鑰匙圈移除 API Key…" : "正在存入 macOS 鑰匙圈…";
                    try { await Task.Run(() => Preferences.SetCloudApiKey(id, keyText)); }
                    catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // The log gets the kind of failure only, never the key.
                        DiagnosticLog.Write(DiagnosticEvent.RunnerFailed, kind: "cloud", state: "write", error: error);
                        cloudResult.Text = "無法存入 macOS 鑰匙圈，設定沒有變更：" + error.Message;
                        return false;
                    }
#else
                    Preferences.SetCloudApiKey(id, keyText);
#endif
                    // Another key may list other models.
                    cloudListed.Remove(id);
                }
                // The recommended model shown and never chosen stays unchosen (empty), so it follows the recommendation.
                string chosen = ModelText();
                if (chosen == CompanionCloud.DefaultModel(id) && Preferences.CloudModel(id).Length == 0 && !cloudModelDrafts.ContainsKey(id)) chosen = "";
                Preferences.ModelSource = "Cloud";
                Preferences.CloudProvider = id;
                Preferences.SetCloudModel(id, chosen);
                Preferences.ScreenImages = cloudImages.IsChecked == true;
                cloudKeyDrafts.Remove(id); cloudModelDrafts.Remove(id); cloudProviderDraft = null;
                speechVersion++; loading.IsVisible = false;
                ApplyModelSource(); Save();
                foreach (var option in localModelOptions) option.IsEnabled = !brain.UseRemote;
                UpdateState();
                return true;
            }
            finally { modelSourceSaving.Release(); }
        }

        // After a listing (儲存並測試, or --test-mode's cloud-models): the list fills the model box. A model never chosen
        // becomes the recommended one this key can use; one the user chose or typed stays (the status says when it is not listed).
        void ModelsListed(IReadOnlyList<CloudModelInfo> listed)
        {
            if (listed.Count == 0) return;
            string id = provider;
            cloudListed[id] = listed;
            string current = ModelText();
            bool chosen = Preferences.CloudModel(id).Length > 0 || cloudModelDrafts.ContainsKey(id);
            string pick = CompanionCloud.Preselect(id, listed, chosen ? current : null);
            filling = true;
            try { FillModels(chosen ? current : pick); }
            finally { filling = false; }
            if (chosen || Preferences.CloudProvider != id || !Preferences.CloudSelected || pick == CompanionCloud.DefaultModel(id)) return;
            // The recommended model is not on this key's list: the first suitable one is used instead.
            Preferences.SetCloudModel(id, pick);
            ApplyModelSource(); Save(); UpdateState();
            cloudResult.Text += "這把 API Key 不能使用 " + CompanionCloud.DefaultModel(id) + "，已改選 " + pick + "。";
        }
        cloudModelsListed = listed => { cloudResult.Text = ""; ModelsListed(listed); };

        Button? test = null;
        test = ActionButton("儲存並測試連線", async () =>
        {
            test!.IsEnabled = false;
            try
            {
                if (!await SaveModelSource(true)) return;
                if (!brain.UseRemote) { result.Text = "網址無效：請填寫 http:// 或 https:// 開頭的網址，例如 http://192.168.1.10:8080。目前仍使用本機模型。"; return; }
                result.Text = "正在測試連線…";
                // Saving cancelled whatever the model was doing; it lets go within moments.
                for (int i = 0; i < 30 && brain.Busy; i++) await Task.Delay(100);
                // The reason is also the model status on the line below; it is not repeated here.
                var reply = await brain.TestRemote();
                result.Text = reply != brain.Status ? "連線測試：" + reply
                    : reply.StartsWith("遠端模型就緒", StringComparison.Ordinal) ? "連線測試成功，詳情見下方的模型狀態。" : "連線測試沒有成功，原因見下方的模型狀態。";
                UpdateState();
            }
            finally { test.IsEnabled = true; }
        });
        Button? cloudTest = null;
        cloudTest = ActionButton("儲存並測試", async () =>
        {
            cloudTest!.IsEnabled = false;
            try
            {
                if (!await SaveCloud()) return;
                if (!brain.UseCloud) return;
                cloudResult.Text = "正在確認 " + CompanionCloud.Info(provider).Name + " 的 API Key…";
                for (int i = 0; i < 30 && brain.Busy; i++) await Task.Delay(100);
                // Listing the models proves the key without spending tokens; the reason of a failure is the model status below.
                var reply = await brain.TestRemote();
                bool ready = reply.StartsWith(CompanionCloud.ReadyPrefix, StringComparison.Ordinal);
                cloudResult.Text = Preferences.CloudApiKey(provider).Length == 0 ? NoKeyYet() : reply != brain.Status ? "測試：" + reply
                    : ready ? "API Key 有效。這個測試只讀取模型清單，沒有用掉 token。" : "測試沒有成功，原因見下方的模型狀態。";
                if (brain.CloudProvider == provider) ModelsListed(brain.CloudModels);
                UpdateState();
            }
            finally { cloudTest.IsEnabled = true; }
        });
#if COMPANION_MAC
        // Until the keychain has answered, a save could be overwritten by the value read before it.
        test.IsEnabled = secretsReady;
        cloudTest.IsEnabled = secretsReady;
#endif
        panel.Children.Add(test);
        panel.Children.Add(result);
        panel.Children.Add(state);
        cloudPanel.Children.Add(cloudTest);
        cloudPanel.Children.Add(cloudResult);
        cloudPanel.Children.Add(usage);
        cloudPanel.Children.Add(cloudState);

        service.SelectionChanged += (_, _) =>
        {
            if (filling || service.SelectedIndex < 0) return;
            ShowProvider(CompanionCloud.Providers[service.SelectedIndex].Id);
            cloudProviderDraft = provider;
            cloudResult.Text = provider != Preferences.CloudProvider || !Preferences.CloudSelected ? "按「儲存並測試」後改用 " + CompanionCloud.Info(provider).DisplayName + "。"
                : cloudKey.Text?.Trim().Length > 0 ? "" : NoKeyYet();
        };
        // A pasted key names its service (sk-ant-, AIza or AQ., another sk-): the panel switches to it, and the key goes along.
        // Only from eight characters on, so typing sk-ant- by hand does not flip between ChatGPT and Claude. PropertyChanged
        // (synchronous), not TextChanged (raised later, after filling has ended): a key ShowProvider fills in is no draft, or
        // an empty field filled before the keychain answered would later stand for the key read and erase it on a save.
        cloudKey.PropertyChanged += (_, e) =>
        {
            if (filling || e.Property != TextBox.TextProperty) return;
            string text = cloudKey.Text ?? "";
            cloudKeyDrafts[provider] = text;
            if (text.Trim().Length < 8 || CompanionCloud.ProviderForKey(text) is not { } owner || owner == provider) return;
            cloudKeyDrafts.Remove(provider);
            cloudKeyDrafts[owner] = text;
            cloudProviderDraft = owner;
            ShowProvider(owner);
            cloudResult.Text = "已依 API Key 的開頭改選 " + CompanionCloud.Info(owner).DisplayName + "。按「儲存並測試」存起來並確認這把 API Key。";
        };
        cloudModel.PropertyChanged += (_, e) =>
        {
            if (filling || e.Property != ComboBox.TextProperty) return;
            cloudModelDrafts[provider] = cloudModel.Text ?? "";
            UpdateModelHints();
        };
        cloudModel.SelectionChanged += (_, _) =>
        {
            if (filling || cloudModel.SelectedItem is not string picked) return;
            cloudModelDrafts[provider] = picked;
            UpdateModelHints();
        };
        limit.SelectionChanged += (_, _) =>
        {
            if (limit.SelectedIndex < 0) return;
            Preferences.CloudDailyLimit = limits[limit.SelectedIndex];
            brain.CloudDailyLimit = Preferences.CloudDailyLimit;
            Save(); UpdateState();
        };

        // The source switch. The cloud source is chosen only after its confirmation; declined, the box returns to the source
        // in use. A page rebuilt meanwhile (the keychain answered) is rebuilt again with the new source.
        bool reverting = false;
        source.SelectionChanged += async (_, _) =>
        {
            if (source.SelectedIndex < 0 || reverting) return;
            if (source.SelectedIndex == 2 && shownSource != 2)
            {
                // The confirmation names the saved service, the one the switch uses; a service picked on the panel and
                // not saved yet is put aside (its typed key and model stay drafts).
                string saved = CompanionCloud.Info(Preferences.CloudProvider).Id;
                if (provider != saved)
                {
                    cloudProviderDraft = null;
                    ShowProvider(saved);
                    cloudResult.Text = "";
                }
                // The confirmation does not block the settings window: until it is answered, the source box and the
                // remote panel wait, so nothing saves or tests with a source the user has not accepted.
                source.IsEnabled = false; panel.IsEnabled = false;
                bool accepted;
                try { accepted = await Confirm("改用雲端 AI 服務？", CloudConsent(saved), "改用雲端 AI 服務", reject: "取消"); }
                catch (InvalidOperationException) { accepted = false; }
                finally { source.IsEnabled = true; panel.IsEnabled = true; }
                if (!accepted) { reverting = true; source.SelectedIndex = shownSource; reverting = false; return; }
            }
            shownSource = source.SelectedIndex;
            panel.IsVisible = shownSource == 1;
            cloudPanel.IsVisible = shownSource == 2;
            if (!await SaveModelSource(false)) return;
            if (TopLevel.GetTopLevel(source) == null) { refreshLinuxSettings?.Invoke(); return; }
            if (shownSource == 1 && !brain.UseRemote) result.Text = "填好伺服器網址後，按「儲存並測試連線」。";
            if (shownSource == 2 && Preferences.CloudApiKey(Preferences.CloudProvider).Length == 0 && provider == Preferences.CloudProvider) cloudResult.Text = NoKeyYet();
        };
        // The two image options are one setting.
        bool syncing = false;
        void ImagesChanged(CheckBox changed, CheckBox other)
        {
            if (syncing) return;
            syncing = true; other.IsChecked = changed.IsChecked; syncing = false;
            _ = SaveModelSource(false);
        }
        images.IsCheckedChanged += (_, _) => ImagesChanged(images, cloudImages);
        cloudImages.IsCheckedChanged += (_, _) => ImagesChanged(cloudImages, images);
    }
}
