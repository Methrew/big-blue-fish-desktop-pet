// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BigBlueFish.Windows;

// This edition keeps its own data folder (CompanionEdition.MacDataFolder): the original resets a Taiwan weather code
// such as TW-061-01 and drops this edition's own settings when it saves. On the first launch, when that folder does
// not exist yet, settings, the model choice and Agent data are copied once from the original's folder. The import only
// reads the original: nothing there is moved, changed or deleted. Models are APFS clones (no extra disk space) or
// are left out, never copied in full. The result is written to import-from-original.txt in the new folder.
internal static class MacDataImport
{
    internal const string NoteName = "import-from-original.txt";
    const uint CloneNoFollow = 1; // CLONE_NOFOLLOW

    internal static void Run(string target, string source)
    {
        if (Directory.Exists(target) || File.Exists(target) || !Directory.Exists(source) || Same(target, source)) return;
        // Built beside the target and renamed into place last, so an interrupted import is redone from the start.
        string parent = Path.GetDirectoryName(Path.GetFullPath(target))!, staging = Path.Combine(parent, "." + Path.GetFileName(target) + ".importing");
        // Two copies started together (login item and a click) both get here before the single-instance check: the
        // second waits for the first to finish instead of creating an empty folder under it.
        string gatePath = Path.Combine(parent, "." + Path.GetFileName(target) + ".import-lock");
        FileStream? gate = null;
        for (var limit = DateTime.UtcNow.AddMinutes(2); gate == null && DateTime.UtcNow < limit;)
            try { gate = new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(200); }
        if (gate == null) return;
        try
        {
            if (Directory.Exists(target)) return;
            Discard(staging);
            Directory.CreateDirectory(staging);
            var note = new StringBuilder();
            note.AppendLine(CompanionEdition.DisplayName + "在 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " 第一次啟動時，從原版的資料夾複製了下列資料。原版的資料沒有被移動、修改或刪除。");
            note.AppendLine("來源：" + source).AppendLine();
            foreach (string name in new[] { "preferences.json", "preferences.json.bak" })
                if (File.Exists(Path.Combine(source, name)) && !IsLink(Path.Combine(source, name)))
                {
                    File.Copy(Path.Combine(source, name), Path.Combine(staging, name));
                    if (name == "preferences.json") note.AppendLine("- 設定（preferences.json）：已複製。");
                }
            if (Directory.Exists(Path.Combine(source, "agent")) && !IsLink(Path.Combine(source, "agent")))
            {
                if (!Clone(Path.Combine(source, "agent"), Path.Combine(staging, "agent"))) CopyTree(Path.Combine(source, "agent"), Path.Combine(staging, "agent"));
                note.AppendLine("- Agent 資料（agent/）：已複製。");
            }
            bool modelsReady = true;
            string models = Path.Combine(source, "models");
            if (Directory.Exists(models) && !IsLink(models))
            {
                modelsReady = Clone(models, Path.Combine(staging, "models"));
                note.AppendLine(modelsReady ? "- 模型（models/）：已用 APFS 複本複製，不會額外佔用磁碟空間。"
                    : "- 模型（models/）：沒有複製。無法建立 APFS 複本（可能位在不同的磁碟），為了不佔用好幾 GB 的空間就略過了。"
                      + "想沿用原版的模型，可以在設定的模型頁面安裝模型時選「手動安裝」，再選擇 " + models + " 裡的 .gguf 檔案。");
            }
            // The model choice only comes along with the models it names; otherwise the first launch offers to install one.
            if (File.Exists(Path.Combine(source, "model-selection.json")) && !IsLink(Path.Combine(source, "model-selection.json")))
            {
                if (modelsReady) { File.Copy(Path.Combine(source, "model-selection.json"), Path.Combine(staging, "model-selection.json")); note.AppendLine("- 模型選擇（model-selection.json）：已複製。"); }
                else note.AppendLine("- 模型選擇（model-selection.json）：因為模型沒有複製，也一併略過。");
            }
            note.AppendLine().AppendLine("天氣、新聞等快取沒有複製，會重新取得。之後兩個版本各自儲存設定，互不影響。");
            File.WriteAllText(Path.Combine(staging, NoteName), note.ToString(), new UTF8Encoding(false));
            Directory.Move(staging, target);
        }
        catch (Exception error)
        {
            // Never blocks startup: the app starts with an empty folder, and the original's data stays as it was.
            Console.Error.WriteLine("沒有從原版匯入資料：" + error.Message);
            try { Discard(staging); } catch (Exception) { }
        }
        finally
        {
            try { File.Delete(gatePath); } catch (Exception) { }
            gate.Dispose();
        }
    }

    // APFS clone of a file or a whole folder (clonefile copies a folder's tree); false when the volume cannot clone.
    static bool Clone(string source, string target) => clonefile(source, target, CloneNoFollow) == 0;

    // Fallback for small folders when cloning is unavailable: files are copied, symbolic links are recreated as links.
    static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            string destination = Path.Combine(target, entry.Name);
            if (entry.LinkTarget is string link) { if (entry is DirectoryInfo) Directory.CreateSymbolicLink(destination, link); else File.CreateSymbolicLink(destination, link); }
            else if (entry is DirectoryInfo) CopyTree(entry.FullName, destination);
            else File.Copy(entry.FullName, destination);
        }
    }

    // Removes this import's own staging folder; a link in its place is removed as a link, never followed.
    static void Discard(string staging)
    {
        if (IsLink(staging)) File.Delete(staging);
        else if (Directory.Exists(staging)) Directory.Delete(staging, true);
    }
    static bool IsLink(string path) => new FileInfo(path).LinkTarget != null;
    static bool Same(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd('/'), Path.GetFullPath(right).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    [DllImport("/usr/lib/libSystem.dylib", SetLastError = true)]
    static extern int clonefile([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);
}
