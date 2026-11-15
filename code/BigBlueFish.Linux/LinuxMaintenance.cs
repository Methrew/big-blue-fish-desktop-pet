// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool updateChecking;
    Window? modelDialog;
    CancellationTokenSource? modelWindowCancellation;
    void CloseModelInstaller() { modelWindowCancellation?.Cancel(); modelDialog?.Close(); }
    static void OpenUrl(string url) { var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "/usr/bin/xdg-open") { UseShellExecute = false }; start.ArgumentList.Add(url); Process.Start(start); }
    async Task InstallModel()
    {
        if (closed || phase == "Exit") return;
        if (modelInstalling) { modelDialog?.Activate(); return; }
        modelInstalling = true;
        // With a model in place the window manages it: it names the model, and the resident model keeps running until a
        // download or an import begins (v1.1.3 stopped it as the window opened and loaded it again once it closed).
        // With the remote model or a cloud AI service nothing local runs, so nothing is stopped and a reply from the server is
        // not cut off.
        string? installed = ModelStore.Resolve(Environment.CurrentDirectory);
        bool remote = brain.UseRemote, modelStopped = false, offerCloud = installed == null && !Preferences.CloudSelected;
        void StopModel() { if (modelStopped || remote) return; modelStopped = true; brain.Stop(); speechVersion++; }
        if (installed == null) StopModel();
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = installed == null ? "安裝本機模型" : "管理本機模型", FontSize = 24 });
        if (installed != null)
        {
            var selection = ModelStore.ReadSelection(Environment.CurrentDirectory);
            // An imported file that matches a known model is recorded under that source, like a download of it.
            string origin = selection?.Source switch
            {
                "huggingface" => "Hugging Face 版本，已驗證檢查碼",
                "modelscope" => "ModelScope 版本，已驗證檢查碼",
                "manual" => "手動匯入，不是本程式預期的模型",
                _ => "放在模型資料夾中",
            };
            panel.Children.Add(Paragraph(remote
                ? "本機模型：" + Path.GetFileName(installed) + "（" + origin + "）。目前由" + (brain.UseCloud ? "雲端 AI 服務（" + CompanionCloud.Info(brain.CloudProvider).Name + "）" : "遠端伺服器")
                    + "說話，切回「本機內建模型」時才會用到這個模型。"
                : "目前使用：" + Path.GetFileName(installed) + "（" + origin + "）。"));
            panel.Children.Add(Paragraph(remote ? "要換模型時，選擇下載來源或匯入已下載的 GGUF 模型。兩個來源的模型有所不同，但都適用於本程式。"
                : "要換模型時，選擇下載來源或匯入已下載的 GGUF 模型；開始下載或匯入時，目前的模型會先停止。兩個來源的模型有所不同，但都適用於本程式。"));
        }
        else panel.Children.Add(Paragraph("選擇下載來源，或匯入已下載的 GGUF 模型。兩個來源的模型有所不同，但都適用於本程式。"
            // The first run's other way: no model on this computer, the speech from Claude, ChatGPT or Gemini.
            + (offerCloud ? "\n不想在這台電腦上執行模型，也可以改用雲端 AI 服務（Claude、ChatGPT 或 Gemini）：需要該服務的 API Key，並依用量付費。" : "")));
        var dialog = Dialog(CompanionEdition.AppName + (installed == null ? " · 安裝模型" : " · 管理模型"), panel, 680, 550);
        var status = Paragraph("模型資料夾：" + Path.Combine(Environment.CurrentDirectory, "models")); panel.Children.Add(status);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8 }; panel.Children.Add(progress);
        var choices = new WrapPanel(); panel.Children.Add(choices); var retry = new WrapPanel { IsVisible = false }; panel.Children.Add(retry);
        using var cancel = new CancellationTokenSource(); modelWindowCancellation = cancel; modelDialog = dialog;
        CancellationTokenSource? transfer = null; bool busy = false, closeRequested = false, dialogClosed = false, abandonRequested = false, confirming = false;
        ModelSource selected = ModelStore.Sources.Where(s => File.Exists(Path.Combine(Environment.CurrentDirectory, "models", s.FileName) + ".partial"))
            .OrderByDescending(s => File.GetLastWriteTimeUtc(Path.Combine(Environment.CurrentDirectory, "models", s.FileName) + ".partial")).FirstOrDefault() ?? ModelStore.HuggingFace;
        var controls = new WrapPanel(); panel.Children.Add(controls);
        var abandon = ActionButton("放棄並轉為手動安裝", () => { });
        var resume = ActionButton("繼續下載", () => { }); controls.Children.Add(abandon); controls.Children.Add(resume);
        abandon.Click += async (_, _) => {
            var active = transfer;
            if (active == null || active.IsCancellationRequested || confirming) return;
            confirming = true; RefreshControls();
            try {
                bool accepted = await Confirm("放棄自動安裝？", "放棄自動安裝會刪除已下載的部分，之後可以手動安裝模型。", "放棄自動安裝", cancel.Token, "不要放棄");
                if (!accepted || dialogClosed || closeRequested || transfer != active) return;
                abandonRequested = true; status.Text = "正在停止自動安裝並清理未完成的下載…"; active.Cancel();
            } finally { confirming = false; RefreshControls(); }
        };
        resume.Click += async (_, _) => await Download(selected);
        void RefreshControls()
        {
            if (dialogClosed) return;
            abandon.IsVisible = transfer != null; abandon.IsEnabled = transfer?.IsCancellationRequested == false && !confirming && !abandonRequested;
            resume.IsVisible = !busy && File.Exists(Path.Combine(Environment.CurrentDirectory, "models", selected.FileName) + ".partial");
            choices.IsEnabled = retry.IsEnabled = !busy && !confirming;
        }
        dialog.Closing += (_, e) => {
            closeRequested = true; cancel.Cancel(); transfer?.Cancel();
            if (busy) { e.Cancel = true; status.Text = "正在停止並保留進度…"; }
        };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => { dialogClosed = true; cancel.Cancel(); transfer?.Cancel(); finished.TrySetResult(); };
        async Task Manual()
        {
            if (busy || dialogClosed || closeRequested) return;
            OpenUrl(selected.Page);
            status.Text = "請將模型放入：" + Path.Combine(Environment.CurrentDirectory, "models") + "\n然後選擇已下載的 GGUF 模型檔案。";
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "選擇 GGUF 模型", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("GGUF 模型") { Patterns = ["*.gguf"] }] });
            if (files.Count == 0 || dialogClosed || closeRequested) return;
            string? path = files[0].TryGetLocalPath();
            if (path == null || !ModelStore.IsGguf(path)) { status.Text = "請選擇有效的 GGUF 模型檔案。"; return; }
            busy = true; StopModel(); RefreshControls();
            try {
                status.Text = "正在檢查模型檔案…"; string hash = await ModelStore.Hash(path, cancel.Token);
                if (!ModelStore.Expected(hash) && !await Confirm("使用此模型", "這不是本程式預期的模型，你可以試用，但不保證穩定。", "繼續使用", cancel.Token)) return;
                await ModelStore.Import(Environment.CurrentDirectory, path, hash, cancel.Token); brain.RefreshModelAvailability(); dialog.Close();
            } catch (OperationCanceledException) { } catch (Exception error) { status.Text = error.Message; }
            finally { busy = false; RefreshControls(); if (closeRequested && !dialogClosed) dialog.Close(); }
        }
        async Task Download(ModelSource source)
        {
            if (busy || dialogClosed || closeRequested) return;
            busy = true; StopModel(); selected = source; abandonRequested = false; retry.IsVisible = false; transfer = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token); RefreshControls();
            try {
                string target = Path.Combine(Environment.CurrentDirectory, "models", source.FileName);
                await ArtifactDownload.Download(source.Url, target, source.Sha256, new Progress<ArtifactDownload.Progress>(p => { if (dialogClosed || closeRequested || transfer?.IsCancellationRequested != false) return; progress.Value = p.Percent; status.Text = p.ToString(); }), transfer.Token);
                transfer.Token.ThrowIfCancellationRequested();
                if (!ModelStore.IsGguf(target)) throw new IOException("下載檔案不是有效的 GGUF 模型。");
                ModelStore.Save(Environment.CurrentDirectory, new(source.Id, source.FileName, source.Sha256)); brain.RefreshModelAvailability(); dialog.Close();
            } catch (OperationCanceledException) { if (!closeRequested && !dialogClosed && !abandonRequested) status.Text = "下載已停止，已有進度已保留。"; }
            catch (Exception error) { if (!closeRequested && !dialogClosed && !abandonRequested) { status.Text = "下載未完成：" + error.Message + (installed == null ? "\n可以在網路穩定後重試、手動安裝模型，或無模型啟動。" : "\n可以在網路穩定後重試、手動安裝模型，或保留目前的模型。"); retry.IsVisible = true; } }
            finally {
                transfer.Dispose(); transfer = null;
                bool switchToManual = abandonRequested; abandonRequested = false;
                if (switchToManual) {
                    try { ModelStore.DiscardPartial(Environment.CurrentDirectory, source); }
                    catch (Exception error) { switchToManual = false; if (!dialogClosed && !closeRequested) status.Text = "未能清理未完成的下載：" + error.Message; }
                }
                busy = false; RefreshControls();
                if (closeRequested && !dialogClosed) dialog.Close();
                else if (switchToManual && !dialogClosed) { progress.Value = 0; await Manual(); }
            }
        }
        void Without() { ModelStore.Save(Environment.CurrentDirectory, new("none", null, null)); brain.RefreshModelAvailability(); dialog.Close(); }
        // 改用雲端 AI 服務: after the same confirmation as in the settings, the cloud source is saved (no model-selection.json:
        // the cloud source alone keeps the next start from asking for a local model) and 設定 › 模型與資訊 opens on its panel.
        async Task UseCloud()
        {
            if (busy || dialogClosed || closeRequested || confirming) return;
            confirming = true; RefreshControls();
            bool accepted;
            try { accepted = await Confirm("改用雲端 AI 服務？", CloudConsent(Preferences.CloudProvider), "改用雲端 AI 服務", cancel.Token); }
            catch (InvalidOperationException) { accepted = false; }
            finally { confirming = false; RefreshControls(); }
            if (!accepted || dialogClosed || closeRequested || closed) return;
            Preferences.ModelSource = "Cloud";
            speechVersion++; ApplyModelSource(); Save();
            dialog.Close();
            OpenModelSettings();
        }
        foreach (var source in ModelStore.Sources) choices.Children.Add(ActionButton(source.Label, async () => await Download(source)));
        // With a model in place, leaving keeps it: 關閉 and 保留目前的模型 stand where 無模型啟動 would drop it.
        choices.Children.Add(ActionButton("手動安裝", async () => await Manual()));
        if (offerCloud) choices.Children.Add(ActionButton("改用雲端 AI 服務", async () => await UseCloud()));
        choices.Children.Add(installed == null ? ActionButton("無模型啟動", Without) : ActionButton("關閉", () => dialog.Close()));
        retry.Children.Add(ActionButton("重試", async () => await Download(selected))); retry.Children.Add(ActionButton("手動安裝模型", async () => await Manual()));
        retry.Children.Add(installed == null ? ActionButton("無模型啟動", Without) : ActionButton("保留目前的模型", () => dialog.Close()));
        RefreshControls();
        try { dialog.Show(settings ?? this); await finished.Task; }
        finally { modelWindowCancellation = null; modelDialog = null; modelInstalling = false; if (!closed && phase != "Exit") nextWarmup = DateTime.MinValue; }
    }
    // The settings page 關於與更新: edition, version, the credits to the original author and the Taiwan edition, and updates.
    void BuildAbout(StackPanel body)
    {
        string name = CompanionEdition.AppName;
        string version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(LinuxPetWindow).Assembly)?.InformationalVersion.Split('+')[0]
            ?? ReleaseVersion.Current(typeof(LinuxPetWindow).Assembly).ToString();
        body.Children.Add(new TextBlock { Text = name + " " + version, FontSize = 19, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        body.Children.Add(Paragraph("這是非官方的台灣繁體中文版，不是原作者發布的版本；使用上的問題請回報到本版的專案頁面。"
#if COMPANION_MAC
            + "本版有自己的 App 名稱、套件識別碼和資料夾，可以和原版同時安裝，但同一時間只會執行其中一隻魚。"
#endif
        ));
        body.Children.Add(Paragraph(CompanionEdition.Credits));
        var links = new WrapPanel();
        links.Children.Add(ActionButton("本版專案頁面", () => OpenUrl(CompanionEdition.Homepage)));
        links.Children.Add(ActionButton("原作者 Bai-Cute 的專案", () => OpenUrl("https://github.com/" + CompanionEdition.OriginalRepository)));
        links.Children.Add(ActionButton("acer1204 的台灣版", () => OpenUrl("https://github.com/" + CompanionEdition.TaiwanEditionRepository)));
        string notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY.md");
        if (File.Exists(notices)) links.Children.Add(ActionButton("授權與第三方元件", () => OpenUrl(notices)));
        body.Children.Add(links);
#if COMPANION_SAFE
        body.Children.Add(Paragraph("設定和模型存放在：" + Environment.CurrentDirectory));
#else
        body.Children.Add(Paragraph("設定、模型和 Agent 資料存放在：" + Environment.CurrentDirectory));
#endif
#if COMPANION_MAC
        if (File.Exists(MacDataImport.NoteName))
            body.Children.Add(Paragraph("第一次啟動時已經從原版的資料夾匯入設定，原版的資料沒有變動；匯入了哪些項目，記在這個資料夾的 " + MacDataImport.NoteName + "。"));
#endif
        body.Children.Add(ActionButton("開啟資料夾", () => OpenUrl(Environment.CurrentDirectory)));
        body.Children.Add(new TextBlock { Text = "更新", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 4) });
        body.Children.Add(Paragraph("只會從本版的發布頁面檢查更新，不會換成原作者的版本。"
#if COMPANION_MAC
            + "新版以 DMG 發布：下載後把新版拖到「應用程式」檔案夾，取代舊版即可。"
#endif
        ));
        var automatic = new CheckBox { Content = "每次啟動時自動檢查更新", IsChecked = Preferences.CheckUpdatesOnStartup, Margin = new Thickness(0, 4) };
        automatic.IsCheckedChanged += (_, _) => { Preferences.CheckUpdatesOnStartup = automatic.IsChecked == true; Save(); };
        body.Children.Add(automatic);
        body.Children.Add(ActionButton("檢查更新", async () => await CheckUpdates(true)));
    }
    // Updates come only from this edition's own releases (CompanionEdition), never from the original's, and compare
    // (x.y.z, -tw.N). With no release published yet (404) an automatic check stays silent and a manual one says so.
    // latestApi: --test-mode only (check-updates <url>), a stand-in for GitHub to reach the dialogs of a newer release.
    async Task CheckUpdates(bool manual, string? latestApi = null)
    {
        if (updateChecking) return; updateChecking = true;
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BigBlueFish/" + typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            async Task<HttpResponseMessage> Latest()
            {
                try { return await client.GetAsync(latestApi ?? CompanionEdition.LatestReleaseApi); }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { throw new IOException("無法連線到 GitHub，請確認網路連線後再試一次。", error); }
            }
            using var response = await Latest();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { if (manual) await Inform("檢查更新", "本版目前還沒有發布任何正式版本，之後再來看看。"); return; }
            if (!response.IsSuccessStatusCode) throw new IOException("GitHub 暫時無法回應（HTTP " + (int)response.StatusCode + "），請稍後再試一次。");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement; string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!ReleaseVersion.TryParse(tag, out var latest) || !(latest > ReleaseVersion.Current(typeof(LinuxPetWindow).Assembly))) { if (manual) await Inform("檢查更新", "目前已經是最新版本。"); return; }
            var assets = root.GetProperty("assets").EnumerateArray().ToArray();
            string? Url(string name) => assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name) is { ValueKind: JsonValueKind.Object } found
                && found.TryGetProperty("browser_download_url", out var value) ? value.GetString() : null;
#if COMPANION_SAFE
            string edition = "-Safe";
#else
            string edition = "";
#endif
            string app = CompanionEdition.AppName; // the app the user runs and has to quit
#if COMPANION_MAC
            // A DMG release: the app is never replaced from inside; the user downloads the DMG and replaces the app.
            string asset = CompanionEdition.AssetPrefix + "-v" + latest + edition + "-macOS-arm64.dmg";
            if (Url(asset) == null) { if (manual) await Inform("發現新版本", app + " " + latest + " 已經發布，但 macOS 安裝檔還沒準備好，請稍後再檢查一次。"); return; }
            if (await Confirm("發現新版本", app + " " + latest + " 已經發布。請在發布頁面下載「" + asset + "」，結束目前的" + app
                + "，打開 DMG 後把新版拖到「應用程式」檔案夾，取代舊版。設定和模型存放在資料夾「" + Environment.CurrentDirectory + "」，不會受影響；取代後如果讀不到畫面，請到「系統設定 › 隱私權與安全性 › " + CompanionNative.ScreenRecordingPane + "」重新允許。", "開啟發布頁面", reject: "稍後再說"))
                OpenUrl(root.GetProperty("html_url").GetString() ?? CompanionEdition.ReleasesPage);
#else
            string asset = CompanionEdition.AssetPrefix + "-v" + latest + edition + "-Linux-x64.deb";
            string? url = Url(asset), checksum = Url(asset + ".sha256"); if (url == null || checksum == null) throw new IOException("這個版本的 Linux 安裝套件還沒準備好，請稍後再試一次。");
            if (!await Confirm("發現新版本", app + " " + latest + " 已經發布，要下載並安裝更新嗎？", "下載更新", reject: "稍後再說")) return;
            string hash = (await client.GetStringAsync(checksum)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new IOException("更新的檢查碼資訊無效。");
            string package = Path.Combine(Environment.CurrentDirectory, "cache/updates", asset);
            await ArtifactDownload.Download(url, package, hash);
            var start = new ProcessStartInfo("/usr/bin/pkexec") { UseShellExecute = false };
            foreach (string item in new[] { "/usr/bin/apt-get", "install", "-y", package }) start.ArgumentList.Add(item);
            using var installer = Process.Start(start) ?? throw new IOException("無法啟動系統安裝程式。");
            await installer.WaitForExitAsync(); if (installer.ExitCode != 0) throw new IOException("更新沒有安裝完成，可以再檢查一次更新後重試。");
            var restart = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux")) { UseShellExecute = false };
            restart.ArgumentList.Add("--wait-for-exit"); restart.ArgumentList.Add(Environment.ProcessId.ToString()); Process.Start(restart); CloseImmediately();
#endif
        } catch (Exception error) { if (manual && !closed && !consentOpen) await Inform("檢查更新", "這次沒有完成檢查更新：" + error.Message); }
        finally { updateChecking = false; }
    }
}

