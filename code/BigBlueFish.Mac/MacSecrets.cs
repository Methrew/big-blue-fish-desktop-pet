using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // Set once the login keychain has answered at startup; the settings page waits for it before writing a secret.
    bool macSecretsLoaded;

    // Reads the CWA authorization code (and the remote API key) from the login keychain off the UI thread, then hands the
    // code to the weather. Until then, and when the keychain cannot be read, 臺灣 townships get the Open-Meteo estimate.
    partial void LoadPlatformSecrets() =>
        _ = Task.Run(Preferences.LoadSecrets).ContinueWith(loaded => Dispatcher.UIThread.Post(() =>
        {
            var (moved, error) = loaded.IsCompletedSuccessfully ? loaded.Result : (false, loaded.Exception);
            // The log gets the kind of failure only, never a secret or a message.
            if (error != null) DiagnosticLog.Write(DiagnosticEvent.WeatherFailed, kind: "read", error: error);
            macSecretsLoaded = true;
            if (closed) return;
            if (moved) Save();
            brain.CwaApiKey = Preferences.CwaApiKey;
            refreshLinuxSettings?.Invoke();
        }), TaskScheduler.Default);
}
