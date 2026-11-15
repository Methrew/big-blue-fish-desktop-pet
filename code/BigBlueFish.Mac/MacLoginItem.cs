// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BigBlueFish.Windows;

// 登入時啟動 on macOS: a per-user LaunchAgent labelled with this edition's own bundle identifier, so the Standard and
// Safe editions and the original app each keep their own login item. Only this edition's plist is ever written;
// an older plist is removed only when it starts this very app.
internal static class MacLoginItem
{
#if COMPANION_SAFE
    internal const string Label = CompanionEdition.MacSafeBundleId;
#else
    internal const string Label = CompanionEdition.MacBundleId;
#endif
    // Labels used before this edition had its own identity: the original's (shared by both of its editions) and the
    // earlier Taiwan Mac port's.
    static readonly string[] LegacyLabels = ["io.github.bai-cute.bigbluefish", "io.github.bigbluefish.pet", "io.github.bigbluefish.pet.safe"];
    const int WriteAccess = 2, ReadOnlyFileSystem = 30; // W_OK, EROFS

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents");
    internal static string PlistPath => Path.Combine(Folder, Label + ".plist");
    static string Executable => Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mac");
    // The .app this process runs from, or null for a build folder outside a bundle.
    static string? Bundle
    {
        get
        {
            var macOS = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            return macOS.Name == "MacOS" && macOS.Parent?.Name == "Contents" && macOS.Parent.Parent is { } app
                && app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? app.FullName : null;
        }
    }

    internal static bool Enabled => ProgramPath(PlistPath) is string program && Same(program, Executable);

    // Why a login item cannot point at the place this copy runs from, or null when it can.
    internal static string? Unsupported(string? location = null)
    {
        location ??= AppContext.BaseDirectory;
        if (location.Contains("/AppTranslocation/", StringComparison.Ordinal))
            return "macOS 目前是從暫時的位置執行" + CompanionEdition.DisplayName + "（App Translocation），登入時會找不到它。請先結束" + CompanionEdition.DisplayName
                + "，把它拖到「應用程式」檔案夾，再從「應用程式」重新開啟後設定。";
        if (location.StartsWith("/Volumes/", StringComparison.Ordinal) && ReadOnly(location))
            return CompanionEdition.DisplayName + "目前是直接從磁碟映像（DMG）執行，退出映像後登入時會找不到它。請先把它拖到「應用程式」檔案夾，再從「應用程式」重新開啟後設定。";
        return null;
    }

    internal static void Set(bool on)
    {
        if (!on) { File.Delete(PlistPath); return; }
        if (Unsupported() is string reason) throw new InvalidOperationException(reason);
        Write();
    }

    // At startup: take over an older login item that starts this same app, and repoint this edition's item when the
    // app it names is gone (the app was moved). Never blocks startup.
    internal static void Refresh()
    {
        try
        {
            if (Bundle is not string bundle || Unsupported() != null) return;
            bool wanted = false;
            foreach (string label in LegacyLabels)
            {
                string legacy = Path.Combine(Folder, label + ".plist");
                if (ProgramPath(legacy) is string program && Inside(program, bundle)) { File.Delete(legacy); wanted = true; }
            }
            if (ProgramPath(PlistPath) is string current && !File.Exists(current)) wanted = true;
            if (wanted && !Enabled) Write();
        }
        catch (Exception error) { Console.Error.WriteLine("登入項目檢查略過：" + error.Message); }
    }

    static void Write()
    {
        Directory.CreateDirectory(Folder);
        string temporary = PlistPath + ".tmp";
        File.WriteAllText(temporary, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\"><dict><key>Label</key><string>" + Label + "</string><key>ProgramArguments</key><array><string>"
            + SecurityElement.Escape(Executable) + "</string></array><key>RunAtLoad</key><true/><key>ProcessType</key><string>Interactive</string></dict></plist>\n");
        File.Move(temporary, PlistPath, true);
    }

    // The first ProgramArguments entry (or Program) of a LaunchAgent plist, XML or binary; null when absent or unreadable.
    internal static string? ProgramPath(string plist)
    {
        if (!File.Exists(plist)) return null;
        // Plists carry Apple's DOCTYPE; it is skipped, never fetched.
        static XDocument Read(TextReader text)
        {
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            return XDocument.Load(reader);
        }
        XDocument document;
        try { using var file = File.OpenText(plist); document = Read(file); }
        catch (Exception)
        {
            // A binary plist (edited with defaults or plutil): let plutil turn it into XML.
            try
            {
                var start = new ProcessStartInfo("/usr/bin/plutil") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string item in new[] { "-convert", "xml1", "-o", "-", plist }) start.ArgumentList.Add(item);
                using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000)) { process.Kill(); return null; }
                if (process.ExitCode != 0) return null;
                document = Read(new StringReader(output.Result));
            }
            catch (Exception) { return null; }
        }
        var entries = document.Root?.Element("dict")?.Elements().ToArray() ?? [];
        for (int i = 0; i + 1 < entries.Length; i++)
        {
            if (entries[i].Name != "key") continue;
            if ((string)entries[i] == "ProgramArguments" && entries[i + 1].Name == "array") return entries[i + 1].Elements("string").Select(x => (string)x).FirstOrDefault();
            if ((string)entries[i] == "Program" && entries[i + 1].Name == "string") return (string)entries[i + 1];
        }
        return null;
    }

    static string Normalize(string path) => Path.GetFullPath(path).Normalize(NormalizationForm.FormC);
    // APFS volumes are case-insensitive by default; Finder and the kernel can hand out differently normalized names.
    static bool Same(string left, string right) => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    static bool Inside(string path, string folder) => Normalize(path).StartsWith(Path.TrimEndingDirectorySeparator(Normalize(folder)) + "/", StringComparison.OrdinalIgnoreCase);

    // A mounted DMG is read-only; an app copied to a writable external disk under /Volumes is a real install.
    internal static bool ReadOnly(string path) => access(path, WriteAccess) != 0 && Marshal.GetLastPInvokeError() == ReadOnlyFileSystem;
    [DllImport("/usr/lib/libSystem.dylib", SetLastError = true)] static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
