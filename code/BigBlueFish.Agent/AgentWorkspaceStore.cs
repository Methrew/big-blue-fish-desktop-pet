// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record WorkspaceState(string Current, string[] Recent);

/// <summary>Persistent user files are shared across harnesses; conversation records remain outside them.</summary>
public sealed class AgentWorkspaceStore
{
    private readonly string root, statePath;
    private WorkspaceState state;
    private readonly object gate = new();
    public string Current { get { lock (gate) return state.Current; } }
    public string[] Recent { get { lock (gate) return state.Recent.ToArray(); } }
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public AgentWorkspaceStore(string root, string? defaultWorkspace = null)
    {
        this.root = Path.GetFullPath(root); statePath = Path.Combine(this.root, "workspace-state.json");
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        // Worked out only when nothing is saved: DefaultWorkspace looks into Documents, which macOS guards.
        WorkspaceState Initial() { string fallback = Path.GetFullPath(defaultWorkspace ?? DefaultWorkspace(documents)); return new(fallback, [fallback]); }
        try
        {
            var saved = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(statePath));
            state = saved == null ? Initial() : new(Normalize(saved.Current), (saved.Recent ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p)).Select(Normalize).ToArray());
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { state = Initial(); }
    }
    // New installs get this edition's folder; the original edition's folder keeps being used where it already exists
    // and this one does not. A workspace the user chose is in workspace-state.json and never comes through here.
    public static string DefaultWorkspace(string documents)
    {
        string current = Path.Combine(documents, "藍色大肥魚工作區"), original = Path.Combine(documents, "蓝色大肥鱼工作区");
        return !Directory.Exists(current) && Directory.Exists(original) ? original : current;
    }
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("請選擇完整的工作區路徑。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public void EnsureCurrent() => Directory.CreateDirectory(Current);
    public void Open(string path)
    {
        path = Normalize(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("工作區資料夾不存在：" + path);
        lock (gate)
        {
            var recent = new[] { path }.Concat(state.Recent.Where(p => !p.Equals(path, PathComparison))).Take(30).ToArray();
            var updated = new WorkspaceState(path, recent);
            AtomicText.Write(statePath, JsonSerializer.Serialize(updated)); state = updated;
        }
    }
    public void Create(string parent, string name)
    {
        parent = Normalize(parent);
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')) throw new ArgumentException("請填寫有效的工作區資料夾名稱。");
        string path = Path.Combine(parent, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("這個名稱已經存在，請使用「開啟工作區」或換一個名稱。");
        Directory.CreateDirectory(path); Open(path);
    }
    public string SessionDirectory(string? workspace = null)
    {
        string path = Normalize(workspace ?? Current);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return Path.Combine(root, "sessions", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))));
    }
    // One saved conversation per harness and launch profile (AgentLaunch.Profile): claude.json for the Agent's own setup,
    // claude.claude-api.json for Claude Code on the Claude API Key, whose conversations Claude Code keeps in another folder.
    public string SessionPath(string harness, string? workspace = null, string profile = "") => Path.Combine(SessionDirectory(workspace),
        HarnessCatalog.Get(harness).Id + (profile.Length > 0 ? "." + profile : "") + ".json");
    // The harness a saved conversation's file name belongs to (the part before the profile).
    public static string HarnessOfRecord(string file) => Path.GetFileNameWithoutExtension(file).Split('.')[0];
    public void ValidateClear(string expected)
    {
        string path = Normalize(expected);
        if (!path.Equals(Normalize(Current), PathComparison)) throw new IOException("目前的工作區已經改變，請重新確認。");
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("工作區已經不存在。");
        string[] protectedRoots = [Path.GetPathRoot(path)!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppContext.BaseDirectory];
        if (protectedRoots.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => path.Equals(Normalize(p), PathComparison))
            || root.Equals(path, PathComparison) || root.StartsWith(path + Path.DirectorySeparatorChar, PathComparison))
            throw new IOException("這個資料夾是使用者、系統或程式的資料夾，不能清空。請使用專門的工作資料夾。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("目前的路徑是資料夾連結，請開啟實際的資料夾後再清空。");
    }
    public void Clear(string expected)
    {
        lock (gate)
        {
            ValidateClear(expected);
            string sessions = SessionDirectory();
            if (Directory.Exists(sessions)) foreach (var file in Directory.EnumerateFiles(sessions, "*.json")) File.Delete(file);
            // Delete only direct children of the confirmed directory. Recursive deletion does not follow directory links.
            foreach (var child in new DirectoryInfo(Current).EnumerateFileSystemInfos())
                if (child is DirectoryInfo directory) directory.Delete(true); else child.Delete();
        }
    }
}
