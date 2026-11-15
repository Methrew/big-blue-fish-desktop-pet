// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // Set once the login keychain has answered at startup; the settings page waits for it before writing a secret.
    bool macSecretsLoaded;
    // A read is under way (at startup or a retry); the settings page does not write a secret meanwhile.
    bool macSecretsReading;
    // Automatic retries after a failed read: each one may show the keychain's unlock prompt, which disappears when
    // security is stopped after MacKeychain.Timeout, so they stay few and far apart. Unlocking the screen and opening
    // the settings also try again, and the settings page has a button for it.
    static readonly TimeSpan[] SecretRetryDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];
    int secretRetry;
    DispatcherTimer? secretRetryTimer;
    bool screenWasLocked;
    long nextLockCheck;

    // Reads the CWA authorization code (and the remote and cloud API keys) from the login keychain off the UI thread, then
    // hands the code to the weather. Until then, and while the keychain cannot be read, 臺灣 townships get the Open-Meteo estimate.
    partial void LoadPlatformSecrets() => ReadSecrets();

    // Reads whatever is not loaded yet; with nothing left to read it only refreshes the settings page.
    void ReadSecrets()
    {
        if (macSecretsReading || closed) return;
        macSecretsReading = true;
        _ = Task.Run(Preferences.LoadSecrets).ContinueWith(loaded => Dispatcher.UIThread.Post(() =>
        {
            macSecretsReading = false;
            var (moved, error) = loaded.IsCompletedSuccessfully ? loaded.Result : (false, loaded.Exception);
            // The log gets the kind of failure only, never a secret or a message.
            if (error != null) DiagnosticLog.Write(DiagnosticEvent.WeatherFailed, kind: "read", error: error);
            macSecretsLoaded = true;
            if (closed) return;
            // Twice: the first save turns the old file, which still held the Base64 value, into preferences.json.bak.
            if (moved) { Save(); Save(); }
            brain.CwaApiKey = Preferences.CwaApiKey;
            // A remote model server or a cloud AI service gets its API key only now.
            if (Preferences.UseRemoteModel || Preferences.CloudSelected) ApplyModelSource();
            // So does an Agent CLI on an API Key (Claude Code, Codex CLI, Gemini CLI), which waits for it (the Agent
            // restarts only when its key changed).
            AgentLaunchChanged();
            refreshLinuxSettings?.Invoke();
            ScheduleSecretRetry();
        }), TaskScheduler.Default);
    }

    void ScheduleSecretRetry()
    {
        secretRetryTimer?.Stop(); secretRetryTimer = null;
        if (!Preferences.SecretsPending) { secretRetry = 0; return; }
        if (secretRetry >= SecretRetryDelays.Length) return;
        secretRetryTimer = new DispatcherTimer { Interval = SecretRetryDelays[secretRetry++] };
        secretRetryTimer.Tick += (_, _) => { secretRetryTimer?.Stop(); secretRetryTimer = null; ReadSecrets(); };
        secretRetryTimer.Start();
    }

    // From the settings: opening them, or the page's 「重新讀取鑰匙圈」 button.
    void RetrySecrets() { if (Preferences.SecretsPending) ReadSecrets(); }

    // Every tick (MacPetWindow.TickPlatform): a failed read is tried again right after the screen is unlocked, when the
    // login keychain usually unlocks too. The lock state is looked at every two seconds.
    void WatchSecretUnlock()
    {
        if (!Preferences.SecretsPending) { screenWasLocked = false; return; }
        long now = Environment.TickCount64;
        if (now < nextLockCheck) return;
        nextLockCheck = now + 2000;
        bool locked = CompanionNative.Locked;
        if (screenWasLocked && !locked) ReadSecrets();
        screenWasLocked = locked;
    }

    partial void OpenedSettingsPlatform() => RetrySecrets();

    // Under a key field of the settings: why the stored secret is not in use, or null when it is.
    string? SecretNote(string account, string what)
    {
        bool inUse = Preferences.SecretValue(account).Length > 0;
        return Preferences.StateOf(account) switch
        {
            CompanionPreferences.SecretState.Failed => "無法讀取 macOS 鑰匙圈：" + Preferences.SecretProblem(account)
                + "鑰匙圈裡的" + what + "暫時沒有使用；解鎖螢幕後會自動再讀一次，也可以按「重新讀取鑰匙圈」。",
            CompanionPreferences.SecretState.Legacy => "舊版存在設定檔的" + what + "還沒移進 macOS 鑰匙圈：" + Preferences.SecretProblem(account)
                + "這次執行仍會使用它，之後會再試一次。",
            CompanionPreferences.SecretState.Unsaved => "舊版存在設定檔的" + what + "無法存進 macOS 鑰匙圈：" + Preferences.SecretProblem(account)
                + "已從設定檔移除" + (inUse ? "，這次執行仍會使用它；請重新填寫鑰匙圈接受的內容並儲存，否則下次啟動就不會再有。" : "；請重新填寫並儲存。"),
            _ => null
        };
    }
}
