// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BigBlueFish.Windows;

// The secrets the settings collect (the CWA authorization code, the remote model's API key, the API keys of the cloud AI
// services) live in the user's login keychain as generic passwords: service CompanionEdition.KeychainService, accounts
// CwaAccount, RemoteAccount, ClaudeAccount, OpenAiAccount and GeminiAccount.
// They go through Apple's /usr/bin/security, so the items trust that tool and this ad-hoc signed app, whose signature
// changes with every update, never has to ask for keychain access again (a Security.framework item would trust the
// app's own signature). Every call blocks for up to Timeout, so callers stay off the UI thread. A failure throws and
// nothing is ever written anywhere else instead; secrets stay out of argv (writes go through stdin), logs and messages.
internal static partial class MacKeychain
{
    internal const string CwaAccount = "CwaApiKey", RemoteAccount = "RemoteApiKey";
    // The same names as CompanionCloud.Providers' KeychainAccount.
    internal const string ClaudeAccount = "ClaudeApiKey", OpenAiAccount = "OpenAiApiKey", GeminiAccount = "GeminiApiKey";
    // security's exit status for errSecItemNotFound.
    const int NotFound = 44;
    // Tests point these at a stand-in for the tool.
    internal static string Tool { get; set; } = "/usr/bin/security";
    internal static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    // What a secret may hold: printable ASCII without spaces, quotes or backslashes (keys, tokens, Base64, CWA's
    // CWA-XXXXXXXX-…), so that security's command line in interactive mode reads it as one word.
    internal static bool IsValid(string secret) => SecretPattern().IsMatch(secret);

    // The stored secret, or "" when there is none.
    internal static string Read(string account)
    {
        var (exit, output) = Run(["find-generic-password", "-s", CompanionEdition.KeychainService, "-a", Account(account), "-w"], null);
        if (exit == NotFound) return "";
        if (exit != 0) throw new IOException($"無法讀取 macOS 鑰匙圈（security 結束代碼 {exit}）。");
        return output.TrimEnd('\r', '\n');
    }

    // Stores the secret (an empty one removes the item) and reads it back: interactive mode does not always report a
    // refused command in its exit status.
    internal static void Write(string account, string secret)
    {
        if (secret.Length == 0) { Delete(account); return; }
        if (!IsValid(secret)) throw new ArgumentException("內容只能包含英文字母、數字和常見符號，不能有空白、引號或反斜線。");
        var (exit, _) = Run(["-i"], $"add-generic-password -U -s {CompanionEdition.KeychainService} -a {Account(account)} -w {secret}\n");
        if (exit != 0) throw new IOException($"無法寫入 macOS 鑰匙圈（security 結束代碼 {exit}）。");
        if (Read(account) != secret) throw new IOException("macOS 鑰匙圈讀回的內容與寫入的不同。");
    }

    internal static void Delete(string account)
    {
        var (exit, _) = Run(["delete-generic-password", "-s", CompanionEdition.KeychainService, "-a", Account(account)], null);
        if (exit != 0 && exit != NotFound) throw new IOException($"無法從 macOS 鑰匙圈移除（security 結束代碼 {exit}）。");
    }

    static string Account(string account) => account is CwaAccount or RemoteAccount or ClaudeAccount or OpenAiAccount or GeminiAccount
        ? account : throw new ArgumentOutOfRangeException(nameof(account));

    static (int Exit, string Output) Run(string[] arguments, string? input)
    {
        var start = new ProcessStartInfo(Tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("無法啟動 macOS 鑰匙圈工具。");
        var output = process.StandardOutput.ReadToEndAsync();
        // Read and dropped: error text is not shown or logged.
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (input != null) process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException) { }
        if (!process.WaitForExit(Timeout))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            // A locked keychain asks for the login password in a dialog; the app does not wait on it.
            throw new IOException("macOS 鑰匙圈沒有回應，可能尚未解鎖。");
        }
        if (!output.Wait(Timeout) || !error.Wait(Timeout)) throw new IOException("macOS 鑰匙圈沒有回應。");
        return (process.ExitCode, output.Result);
    }

    [GeneratedRegex(@"^[A-Za-z0-9._~+/=:@-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}
