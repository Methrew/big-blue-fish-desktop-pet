// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Text.Json;

namespace BigBlueFish.Windows;

// What runtime/BigBlueFish.MacScreen (mac-screen.swift) saw, in macOS global points: the on-screen windows front to
// back, Finder's desktop icons, and the named elements of the windows that show (with the Dock as one more window).
internal sealed record MacScreenSnapshot(bool Trusted, bool ScreenRecording, IReadOnlyList<ScreenBox> Displays, IReadOnlyList<MacScreenWindow> Windows,
    MacScreenDesktop? Desktop, IReadOnlyList<MacScreenScan> Scanned, IReadOnlyList<long> UnfinishedWindows);
internal sealed record MacScreenWindow(long Id, int Pid, int Layer, double Alpha, string Process, string? BundleId, string Title, ScreenBox Bounds);
internal sealed record MacScreenIcon(string Name, ScreenBox Bounds, string? Path, bool Folder);
internal sealed record MacScreenDesktop(IReadOnlyList<MacScreenIcon> Icons, bool Finished, bool FinderMissing);
internal sealed record MacScreenElement(string Role, string? Type, string Name, ScreenBox Bounds);
internal sealed record MacScreenScan(long Window, bool Finished, string? Title, string? Document, string? Label, IReadOnlyList<MacScreenElement> Elements);

// find_on_screen on macOS, apart from the screen itself so it can be checked with made-up snapshots: which of the
// things the helper saw match the query and can be seen now. Whatever another window covers, the fish's own windows
// (the helper leaves them out) and elements scrolled out of view are left out, as on Windows.
internal static class MacScreenSearch
{
    // Finder calls a folder 檔案夾; a search for 資料夾 or folder finds them too.
    internal const string FolderWords = "檔案夾 資料夾 folder";
    internal const string FinderBundle = "com.apple.finder";

    // Source: the window a match was found in (0 for the desktop). File and Path: the file behind a desktop icon, from
    // the URL Finder gives (the name shown may lack its extension). OpenFolder: the folder a Finder window shows, on
    // the window and on everything in it.
    internal sealed record Match(string Kind, string Name, ScreenBox Bounds, long Source, string? File = null, bool Folder = false, string? Type = null,
        string? Window = null, string? Process = null, string? Path = null, string? OpenFolder = null);
    internal sealed record Result(IReadOnlyList<Match> Matches, int CoveredIcons, int CoveredInWindows, IReadOnlyList<string> Unfinished, IReadOnlyList<string> Similar);

    internal static MacScreenSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new IOException("讀取畫面內容的輔助程式沒有傳回有效的資料。");
        if (Text(root, "error") is { } error) throw new IOException(error == "timeout" ? "讀取畫面內容逾時，請稍後再試。" : "讀取畫面內容失敗：" + error);
        var displays = Items(root, "displays").Select(Box).ToList();
        var windows = Items(root, "windows").Select(w => new MacScreenWindow((long)Number(w, "id"), (int)Number(w, "pid"), (int)Number(w, "layer"),
            w.TryGetProperty("alpha", out _) ? Number(w, "alpha") : 1, Text(w, "process") ?? "", Text(w, "bundleId"), Text(w, "title") ?? "", Box(w))).ToList();
        MacScreenDesktop? desktop = null;
        if (root.TryGetProperty("desktop", out var area) && area.ValueKind == JsonValueKind.Object)
            desktop = new(Items(area, "icons").Select(i => new MacScreenIcon(Text(i, "name") ?? "", Box(i), Text(i, "path"), Flag(i, "folder"))).Where(i => i.Name.Length > 0).ToList(),
                !area.TryGetProperty("finished", out _) || Flag(area, "finished"), Flag(area, "finderMissing"));
        var scanned = Items(root, "scanned").Select(s => new MacScreenScan((long)Number(s, "window"), !s.TryGetProperty("finished", out _) || Flag(s, "finished"), Text(s, "title"),
            Text(s, "document"), Text(s, "label"), Items(s, "elements").Select(e => new MacScreenElement(Text(e, "role") ?? "", Text(e, "type"), Normalize(Text(e, "name")), Box(e)))
                .Where(e => e.Name.Length > 0).ToList())).ToList();
        var unfinished = Items(root, "unfinishedWindows").Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt64()).ToList();
        return new(Flag(root, "trusted"), Flag(root, "screenRecording"), displays, windows, desktop, scanned, unfinished);
    }

    // A window above the normal level spanning a whole display (a lock screen shield, a dimming overlay) or a fully
    // transparent one hides nothing under it; mac-screen.swift reads windows by the same rule.
    internal static bool Hides(MacScreenWindow window, IReadOnlyList<ScreenBox> displays) =>
        window.Alpha > .01 && !(window.Layer != 0 && displays.Any(d => window.Bounds.Contains(d)));

    // The window showing at a point, 0 where the desktop (or nothing of another program) shows.
    internal static long At(MacScreenSnapshot snapshot, double x, double y) =>
        snapshot.Windows.FirstOrDefault(w => Hides(w, snapshot.Displays) && w.Bounds.Contains(x, y))?.Id ?? 0;

    internal static bool ShowsAt(MacScreenSnapshot snapshot, long window, double x, double y) =>
        snapshot.Displays.Any(d => d.Contains(x, y)) && At(snapshot, x, y) == window;

    // Five by five points across a window plus a ring just inside its edges, so a strip left showing (a tab row
    // under a window in front) still counts: one point showing is enough.
    internal static IEnumerable<(double X, double Y)> Samples(ScreenBox bounds)
    {
        static IEnumerable<double> Across(double start, double size) =>
            new[] { Math.Min(16, size / 2) }.Concat(Enumerable.Range(0, 5).Select(i => size * (i + .5) / 5)).Append(Math.Max(size - 16, size / 2)).Select(d => start + d);
        foreach (var y in Across(bounds.Y, bounds.Height))
            foreach (var x in Across(bounds.X, bounds.Width))
                yield return (x, y);
    }

    // The normal and floating windows of other programs that show at least in part, front to back.
    internal static IEnumerable<MacScreenWindow> ShownWindows(MacScreenSnapshot snapshot) =>
        snapshot.Windows.Where(w => w.Layer is >= 0 and < 20 && Hides(w, snapshot.Displays) && Samples(w.Bounds).Any(p => ShowsAt(snapshot, w.Id, p.X, p.Y)));

    internal static string Title(MacScreenSnapshot snapshot, MacScreenWindow window) =>
        Normalize(window.Title.Length > 0 ? window.Title : snapshot.Scanned.FirstOrDefault(s => s.Window == window.Id)?.Title);

    internal static string Label(MacScreenSnapshot snapshot, MacScreenWindow? window, MacScreenScan? scan = null)
    {
        if (scan?.Label is { Length: > 0 } label) return label;
        if (window == null) return "未知的視窗";
        var title = Title(snapshot, window);
        return title.Length > 0 ? Short(title, 60) : window.Process.Length > 0 ? window.Process : "未知的 App";
    }

    // The folder a Finder window shows: from the window's own document URL, else from Finder's list of its windows
    // (get_context's openFolders) by the window title.
    internal static string? FolderOf(MacScreenSnapshot snapshot, MacScreenWindow? window, IReadOnlyList<(string Title, string Folder)>? finderFolders)
    {
        if (window?.BundleId != FinderBundle) return null;
        var document = snapshot.Scanned.FirstOrDefault(s => s.Window == window.Id)?.Document;
        if (document != null && Uri.TryCreate(document, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var path = uri.LocalPath;
            return path.Length > 1 ? path.TrimEnd('/') : path;
        }
        var title = Title(snapshot, window);
        var named = finderFolders?.Where(f => f.Title == title).Select(f => f.Folder).Distinct().ToList();
        return named is { Count: 1 } ? named[0] : null;
    }

    internal static Result Find(MacScreenSnapshot snapshot, IReadOnlyList<string> terms, string where, IReadOnlyList<(string Title, string Folder)>? finderFolders = null)
    {
        var matches = new List<Match>();
        var names = new List<string>();
        var unfinished = new List<string>();
        int coveredIcons = 0, coveredInWindows = 0;
        if (where != "windows" && snapshot.Desktop is { } desktop)
        {
            if (!desktop.Finished) unfinished.Add("桌面圖示");
            foreach (var icon in desktop.Icons)
            {
                names.Add(icon.Name);
                var file = icon.Path is { Length: > 1 } path ? System.IO.Path.GetFileName(path.TrimEnd('/')) : null;
                if (!CompanionScreenMatching.Matches(terms, icon.Name, icon.Folder ? FolderWords : null, file)) continue;
                if (!ShowsAt(snapshot, 0, icon.Bounds.CenterX, icon.Bounds.CenterY)) { coveredIcons++; continue; }
                matches.Add(new("desktop_icon", icon.Name, icon.Bounds, 0, file, icon.Folder, icon.Folder ? "檔案夾" : null, Path: icon.Path is { Length: > 1 } p ? p.TrimEnd('/') : icon.Path));
            }
        }
        if (where != "desktop")
        {
            var byId = snapshot.Windows.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var window in ShownWindows(snapshot))
            {
                var title = Title(snapshot, window);
                if (title.Length == 0) continue;
                names.Add(title);
                if (CompanionScreenMatching.Matches(terms, title))
                    matches.Add(new("window", title, window.Bounds, window.Id, Process: window.Process, OpenFolder: FolderOf(snapshot, window, finderFolders)));
            }
            foreach (var scan in snapshot.Scanned)
            {
                byId.TryGetValue(scan.Window, out var window);
                var label = Label(snapshot, window, scan);
                if (!scan.Finished) unfinished.Add(label);
                var folder = FolderOf(snapshot, window, finderFolders);
                foreach (var element in scan.Elements)
                {
                    if (element.Name.Length <= 60) names.Add(element.Name);
                    if (!CompanionScreenMatching.Matches(terms, element.Name)) continue;
                    if (ShowsAt(snapshot, scan.Window, element.Bounds.CenterX, element.Bounds.CenterY))
                        matches.Add(new("element", element.Name, element.Bounds, scan.Window, Type: element.Type, Window: label, OpenFolder: folder));
                    else coveredInWindows++;
                }
            }
            foreach (var id in snapshot.UnfinishedWindows.Distinct())
                if (byId.TryGetValue(id, out var window)) unfinished.Add(Label(snapshot, window));
        }
        var kept = CompanionScreenMatching.ReadingOrder(CompanionScreenMatching.Distinct(matches, m => m.Bounds, m => m.Source, m => m.Name), m => m.Bounds);
        // A name that matches was only covered; suggesting it again as a similar one would send the agent round in a circle.
        return new(kept, coveredIcons, coveredInWindows, unfinished.Distinct().ToList(),
            kept.Count == 0 ? CompanionScreenMatching.Similar(names.Where(n => !CompanionScreenMatching.Matches(terms, n)), terms) : []);
    }

    static IEnumerable<JsonElement> Items(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    static double Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : 0;
    static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static bool Flag(JsonElement owner, string name) => owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    static ScreenBox Box(JsonElement owner) => new(Number(owner, "x"), Number(owner, "y"), Number(owner, "width"), Number(owner, "height"));
    static string Normalize(string? text) => string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    static string Short(string text, int limit) => text.Length <= limit ? text : text[..limit] + "…";
}
