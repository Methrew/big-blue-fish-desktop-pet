// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private ModelInstallWindow? modelInstaller;
    private bool checkingUpdates;
    private UpdateWindow? updateWindow;
    private AvailableUpdate? availableUpdate;
    private string updateStatus = "";
    private void OpenModelInstaller()
    {
        if (exitAnimation || companionClosed) return;
        if (modelInstaller != null) { modelInstaller.Activate(); return; }
        InvalidateSpeechRequests(true);
        var dialog = modelInstaller = new ModelInstallWindow(Environment.CurrentDirectory);
        if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
        dialog.Closed += (_, _) => {
            if (modelInstaller == dialog) modelInstaller = null;
            if (companionClosed || exitAnimation || exitReady) return;
            brain.RefreshModelAvailability();
            nextWarmup = DateTime.MinValue;
            nextSpeech = DateTime.UtcNow.AddSeconds(20);
            if (dialog.CloudChosen) AdoptCloudRequest(true);
        };
        dialog.Show();
    }

    // 改用雲端 AI 服務 in the 安裝本機模型 window, here or in the installer before this start (which leaves
    // ModelInstallWindow.CloudRequestName in the data folder): 設定 › 模型與供電 opens with 模型來源 turned to the cloud
    // AI service, which asks its confirmation first. A model source already on the cloud only gets the page.
    private void AdoptCloudRequest(bool chosen = false)
    {
        try
        {
            if (File.Exists(ModelInstallWindow.CloudRequestName)) { File.Delete(ModelInstallWindow.CloudRequestName); chosen = true; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write(DiagnosticEvent.RunnerFailed, kind: "cloud", state: "read", error: error);
        }
        if (chosen) OpenCloudSettings();
    }

    private async Task CheckForUpdates(bool interactive)
    {
        if (checkingUpdates || companionClosed || exitAnimation) return;
        checkingUpdates = true; updateStatus = "正在檢查更新…";
        try
        {
            // The InformationalVersion keeps the -tw.N revision that the numeric assembly version drops.
            var version = ReleaseVersion.Current(typeof(MainWindow).Assembly).ToString();
            availableUpdate = await UpdateService.Check(version);
            if (companionClosed || exitAnimation) return;
            updateStatus = availableUpdate == null ? "目前已是最新版本" : "發現新版本 " + availableUpdate.Version;
            if (availableUpdate != null)
            {
                if (interactive) OpenUpdateWindow();
                else notifyIcon.ShowBalloonTip(8000, "藍色大肥魚有新版本啦", "點選下載並更新到 " + availableUpdate.Version, System.Windows.Forms.ToolTipIcon.Info);
            }
            else if (interactive) ModernUI.Choice(companionSettings, "檢查更新", updateStatus, "確定");
        }
        catch (Exception error)
        {
            updateStatus = "暫時無法檢查更新，可以稍後重試";
            if (interactive) ModernUI.Error(companionSettings, "檢查更新未完成", error);
        }
        finally { checkingUpdates = false; }
    }

    private void OpenUpdateWindow()
    {
        if (availableUpdate == null || companionClosed || exitAnimation) return;
        if (updateWindow != null) { updateWindow.Activate(); return; }
        var dialog = updateWindow = new UpdateWindow(availableUpdate, AppContext.BaseDirectory, Environment.CurrentDirectory);
        if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
        dialog.Closed += (_, _) => {
            if (updateWindow == dialog) updateWindow = null;
            if (dialog.InstallerStarted && !companionClosed && !exitAnimation && !exitReady) base.Close();
        };
        dialog.Show();
    }
}
