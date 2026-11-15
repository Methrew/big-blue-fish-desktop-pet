// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
#if COMPANION_MAC && !COMPANION_SAFE
using System.Text.Json;
using BigBlueFish.Control;

namespace BigBlueFish.Windows;

// find_on_screen on macOS, the counterpart of the Windows CompanionScreenSearch.cs (taiwan-edition by acer1204): the
// same arguments, result fields and notes, with macOS global points for x, y, width and height (walk_to takes them as
// they are). runtime/BigBlueFish.MacScreen reads the screen; MacScreenSearch decides what matches and what shows.
// The Linux app has no such reader and does not offer the tool.
internal sealed partial class LinuxPetWindow
{
    // More than this many matches are asked about before the fish runs to them.
    const int ManyMatches = 8;

    async Task<object> FindOnScreenMcp(JsonElement args, CancellationToken token)
    {
        var terms = ControlTools.RequiredText(args, "query", 200).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        if (terms.Length == 0) throw new ArgumentException("query 要寫出要找的名稱。");
        var where = ControlTools.OptionalChoice(args, "where", "all", "desktop", "windows");
        double limit = ControlTools.OptionalNumber(args, "limit", 1, 50) ?? 20;
        if (limit != Math.Floor(limit)) throw new ArgumentException("limit 必須是整數。");
        var snapshot = await MacScreenHelper.FindAsync(where, token);
        token.ThrowIfCancellationRequested();
        // Without the Accessibility permission only the window titles of CGWindowList are there to search.
        if (!snapshot.Trusted && where == "desktop") throw new InvalidOperationException(MacScreenHelper.AccessibilityNeeded("找桌面圖示"));
        // A Finder window that does not name its folder itself: Finder's own list (the Automation permission get_context also uses).
        IReadOnlyList<(string Title, string Folder)>? folders = null;
        if (where != "desktop" && MacScreenSearch.ShownWindows(snapshot).Any(w => w.BundleId == MacScreenSearch.FinderBundle && snapshot.Scanned.FirstOrDefault(s => s.Window == w.Id)?.Document == null))
            folders = (await CompanionNative.OpenFoldersAsync(token)).Folders;
        var found = MacScreenSearch.Find(snapshot, terms, where, folders);
        if (!snapshot.Trusted && found.Matches.Count == 0) throw new InvalidOperationException(MacScreenHelper.AccessibilityNeeded("找桌面圖示和視窗裡的按鈕、文字"));
        // Only the fields a match has, so a long list stays short; x and y are centres, as walk_to takes them.
        var matches = found.Matches.Take((int)limit).Select((m, i) =>
        {
            var row = new Dictionary<string, object?> { ["n"] = i + 1, ["kind"] = m.Kind, ["name"] = m.Name };
            if (m.File != null) row["file"] = m.File;
            if (m.Path != null) row["path"] = m.Path;
            if (m.OpenFolder != null) row["openFolder"] = m.OpenFolder;
            if (m.Folder) row["folder"] = true;
            if (!string.IsNullOrEmpty(m.Type)) row["type"] = m.Type;
            if (m.Window != null) row["window"] = m.Window;
            if (m.Process != null) row["process"] = m.Process;
            row["x"] = (int)Math.Round(m.Bounds.CenterX); row["y"] = (int)Math.Round(m.Bounds.CenterY);
            row["width"] = (int)Math.Round(m.Bounds.Width); row["height"] = (int)Math.Round(m.Bounds.Height);
            return row;
        }).ToArray();
        var result = new Dictionary<string, object?> { ["query"] = string.Join("|", terms), ["total"] = found.Matches.Count, ["returned"] = matches.Length, ["matches"] = matches,
            ["coordinateSpace"] = "macos-points" };
        if (found.CoveredIcons > 0) result["coveredDesktopIcons"] = found.CoveredIcons;
        if (found.CoveredInWindows > 0) result["coveredInWindows"] = found.CoveredInWindows;
        var unfinished = snapshot.Trusted ? found.Unfinished : [.. found.Unfinished, .. where == "all" ? new[] { "桌面圖示" } : [], "視窗裡的介面元素"];
        if (unfinished.Count > 0) result["unfinished"] = unfinished;
        if (found.Similar.Count > 0) result["similar"] = found.Similar;
        // Said where the agent decides: left to its instructions, a model ran a 35-stop tour and kept capturing a
        // desktop that windows covered.
        if (!snapshot.Trusted)
            result["note"] = "這次只比對了視窗標題。" + MacScreenHelper.AccessibilityNeeded("找桌面圖示和視窗裡的按鈕、文字") + "回答時告訴使用者這些還沒找。";
        else if (found.Matches.Count > ManyMatches)
            result["note"] = $"符合的有 {found.Matches.Count} 個，全部跑一遍要好幾分鐘。先告訴使用者總數和幾個名稱，問要跑哪些，等使用者回答再跑。";
        else if (found.Matches.Count == 0 && found.CoveredIcons > 0)
            result["note"] = $"有 {found.CoveredIcons} 個符合的桌面圖示被視窗擋住，現在看不到。告訴使用者，請他先顯示桌面（例如按 F11，筆電可能要按 fn＋F11，或在觸控式軌跡板上用拇指和三指張開）再找一次；不要改用截圖找。";
        else if (found.Matches.Count == 0 && found.CoveredInWindows > 0)
            result["note"] = $"有 {found.CoveredInWindows} 個符合的項目被前面的視窗擋住，現在看不到。告訴使用者是被擋住了，不要改用截圖找。";
        else if (found.Unfinished.Count > 0)
            result["note"] = $"這些還沒讀完：{string.Join("、", found.Unfinished)}。先再呼叫一次 find_on_screen（通常第二次就讀得到）；還是沒讀完，回答時告訴使用者這些沒有找。";
        return result;
    }
}
#endif
