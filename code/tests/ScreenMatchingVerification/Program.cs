// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// The name rules find_on_screen and walk_to's desktop_item share on Windows and macOS (CompanionScreenMatching, the
// same cases McpUiVerification checks through the Windows wrappers), then the macOS search over made-up screens:
// what is covered, what shows, which folder a Finder window has open, what a missing permission leaves. With the path
// of a built BigBlueFish.MacScreen as the argument it also reads this Mac's real screen and checks the JSON shape.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BigBlueFish.Windows;

Console.OutputEncoding = Encoding.UTF8;
int failures = 0, passes = 0;
void Check(bool condition, string name)
{
    if (condition) passes++; else failures++;
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
}

// Desktop item names
var icons = new List<(string Name, ScreenBox Bounds)> { ("資源回收筒", new(10, 10, 80, 80)), ("本機", new(10, 100, 80, 80)), ("報告A.docx", new(10, 200, 80, 80)), ("報告B.docx", new(10, 300, 80, 80)) };
string[]? files = null;
string? Matched(string query) { try { return CompanionScreenMatching.MatchDesktopItem(icons, query, files).Name; } catch (ArgumentException) { return null; } }
string Failure(string query) { try { CompanionScreenMatching.MatchDesktopItem(icons, query, files); return ""; } catch (ArgumentException error) { return error.Message; } }
Check(Matched("資源回收桶") == "資源回收筒" && Matched("資源回收筒") == "資源回收筒" && Matched("回收") == "資源回收筒" && Matched("報告A.DOCX") == "報告A.docx",
    "Desktop icon names match exactly, by part, ignoring case, or one character off");
Check(Matched("報告C.docx") == null && Matched("本基") == null, "A slip that fits two icons, or one in a two-character name, is not guessed");
Check(Matched("報告") == null, "A part of several names is not guessed either");
icons = [("報告10.docx", new(10, 10, 80, 80)), ("報告1.docx", new(10, 100, 80, 80)), ("Steam", new(10, 200, 80, 80))];
Check(Matched("報告1") == "報告1.docx" && Matched("報告1.DOCX") == "報告1.docx", "The name without its extension wins over a longer name containing it");
icons = [("報告1.docx", new(10, 10, 80, 80)), ("Steam", new(10, 100, 80, 80))];
Check(Matched("報告2.docx") == null, "A different number is another file, not a slip");
Check(!Failure("資源回收筒").Contains("Steam") && Failure("報告2.docx").Contains("報告1.docx"), "Only names that are really close are suggested");
icons = [("Python 3.12", new(10, 10, 80, 80)), ("Python 3.11", new(10, 100, 80, 80))];
Check(Matched("Python 3") == null && Failure("Python 3").Contains("Python 3.12") && Failure("Python 3").Contains("Python 3.11"), "A version number is no extension");
icons = [("報告1.docx", new(10, 10, 80, 80)), ("報告1.pdf", new(10, 100, 80, 80)), ("報告", new(10, 200, 80, 80)), ("報告", new(10, 300, 80, 80)), ("合約", new(10, 400, 80, 80))];
Check(Matched("報告1") == null && Failure("報告1").Contains("報告1.pdf"), "Two files sharing a name without their extensions are listed, not guessed");
Check(Matched("報告1.docx") == "報告1.docx" && Matched("合約.docx") == "合約" && Matched("報告1.pptx") == null, "An extension shown or hidden still finds the icon, another extension does not");
Check(Failure("報告").Contains("stops") && Failure("報告").Contains("x=50") && Failure("報告").Contains("y=240"), "Icons showing the same name are listed with their centres");
icons = [("報告1", new(10, 10, 80, 80)), ("報告10.docx", new(10, 100, 80, 80)), ("回收站", new(10, 200, 80, 80))];
files = ["報告1.pdf", "報告10.docx"];
Check(Matched("報告1.docx") == null && Matched("報告1.pdf") == "報告1", "A hidden extension must be the file's own");
Check(Matched("報告1") == "報告1" && Matched("告1") == "報告1" && Matched("報告10") == "報告10.docx", "A number in the query is a whole number in the name");
Check(CompanionScreenMatching.MatchDesktopItem(icons, "回收站", files).Bounds == new ScreenBox(10, 200, 80, 80), "The icon's own rectangle comes back");

// Text rules
Check(CompanionScreenMatching.Stem("報告.docx") == "報告" && CompanionScreenMatching.Stem("壓縮檔.7z") == "壓縮檔" && CompanionScreenMatching.Stem("Python 3.12") == "Python 3.12"
    && CompanionScreenMatching.Stem(".gitignore") == ".gitignore" && CompanionScreenMatching.Stem("封面.final draft") == "封面.final draft", "Only extension-like tails are taken off");
bool Has(string text, string term) => CompanionScreenMatching.Contains(text, term);
Check(!Has("Star (4.5k)", "tar") && !Has("User avatar", "tar") && !Has("Docker Desktop", "doc") && !Has("報告.docx", "doc") && !Has("PC2005", "005"),
    "Letters and digits do not match inside another word or number");
Check(Has("報告.DOCX", "docx") && Has("MyReportFinal", "Report") && Has("20260911.zip", "2026") && Has("資源回收筒", "回收") && Has("Scada with Agent.zip", "zip")
    && Has("Microsoft Word 文件", "word 文件"), "Whole words, camelCase humps, number starts and Chinese anywhere match");
var rows = new[] { ("右上", new ScreenBox(300, 12, 80, 30)), ("左下", new ScreenBox(10, 200, 80, 30)), ("左上", new ScreenBox(10, 10, 80, 34)) };
Check(CompanionScreenMatching.ReadingOrder(rows, r => r.Item2).Select(r => r.Item1).SequenceEqual(["左上", "右上", "左下"]), "Matches come in reading order, a row at a time");
var nested = new[] { ("報告1.docx", new ScreenBox(10, 10, 100, 120), 1L), ("報告1.docx", new ScreenBox(12, 100, 90, 20), 1L), ("報告1.docx - Word", new ScreenBox(400, 0, 800, 600), 2L),
    ("報告1.docx", new ScreenBox(420, 5, 200, 20), 2L), ("報告1.docx", new ScreenBox(10, 300, 100, 120), 1L) };
Check(CompanionScreenMatching.Distinct(nested, m => m.Item2, m => m.Item3, m => m.Item1).Select(m => m.Item1).Order().SequenceEqual(["報告1.docx", "報告1.docx", "報告1.docx - Word"]),
    "An item and its label, or a window and its title bar, count once; two separate items both count");
var behind = new[] { ("報告1 - Word", new ScreenBox(0, 0, 3000, 2000), 2L), ("報告1", new ScreenBox(400, 300, 200, 40), 3L) };
Check(CompanionScreenMatching.Distinct(behind, m => m.Item2, m => m.Item3, m => m.Item1).Count == 2, "An item in a window in front is not swallowed by the title of a larger window behind it");
Check(CompanionScreenMatching.Similar(["資源回收筒", "本機", "Steam"], ["資源回收桶"]).SequenceEqual(["資源回收筒"]), "Similar names suggest the slip only");

// macOS: a made-up screen. One 1440x900 display; front to back: a menu bar, a dimming overlay over the whole display
// (layer 1000, ignored), Safari over the left half, a Finder window behind it on the right, a transparent window.
string Snapshot(bool trusted = true, string? document = "file:///Users/me/Documents/%E5%A0%B1%E5%91%8A/", bool finished = true) => JsonSerializer.Serialize(new
{
    trusted, screenRecording = true,
    displays = new[] { new { x = 0, y = 0, width = 1440, height = 900, main = true } },
    windows = new object[] {
        new { id = 1, pid = 100, layer = 24, alpha = 1.0, process = "Window Server", title = "Menubar", x = 0, y = 0, width = 1440, height = 30 },
        new { id = 2, pid = 101, layer = 1000, alpha = 1.0, process = "Dimmer", title = "", x = 0, y = 0, width = 1440, height = 900 },
        new { id = 10, pid = 200, layer = 0, alpha = 1.0, process = "Safari", bundleId = "com.apple.Safari", title = "報告 - 新聞", x = 0, y = 30, width = 720, height = 600 },
        new { id = 20, pid = 300, layer = 0, alpha = 1.0, process = "Finder", bundleId = "com.apple.finder", title = "報告", x = 600, y = 100, width = 700, height = 500 },
        new { id = 30, pid = 400, layer = 0, alpha = 0.0, process = "Ghost", title = "報告 overlay", x = 0, y = 0, width = 1440, height = 900 },
        new { id = 40, pid = 500, layer = 20, alpha = 1.0, process = "Dock", bundleId = "com.apple.dock", title = "Dock", x = 400, y = 820, width = 640, height = 80 } },
    desktop = trusted ? new { icons = new object[] {
        new { name = "報告.pdf", x = 1340, y = 40, width = 80, height = 90, path = "/Users/me/Desktop/報告.pdf", folder = false },
        new { name = "報告草稿", x = 1340, y = 140, width = 80, height = 90, path = "/Users/me/Desktop/報告草稿.docx", folder = false },
        new { name = "舊報告", x = 1340, y = 240, width = 80, height = 90, path = "/Users/me/Desktop/舊報告/", folder = true },
        new { name = "報告備份", x = 100, y = 200, width = 80, height = 90, path = "/Users/me/Desktop/報告備份.zip", folder = false } }, finished = true } : null,
    scanned = trusted ? new object[] {
        new { window = 10, finished, elements = new object[] {
            new { role = "AXButton", type = "按鈕", name = "下載報告", x = 100, y = 60, width = 80, height = 24 },
            new { role = "AXStaticText", type = "文字", name = "報告  第二段", x = 100, y = 300, width = 200, height = 20 } } },
        new { window = 20, finished = true, title = "報告", document, elements = new object[] {
            new { role = "AXTextField", type = "文字欄位", name = "報告1.docx", x = 650, y = 200, width = 120, height = 20 },
            new { role = "AXTextField", type = "文字欄位", name = "報告2.docx", x = 1000, y = 200, width = 120, height = 20 } } },
        new { window = 40, finished = true, label = "Dock", elements = new object[] { new { role = "AXDockItem", type = "Dock 項目", name = "報告產生器", x = 420, y = 830, width = 60, height = 60 } } } } : Array.Empty<object>(),
    unfinishedWindows = new[] { 20 },
});
var screen = MacScreenSearch.Parse(Snapshot());
Check(screen.Windows.Count == 6 && screen.Displays.Single() == new ScreenBox(0, 0, 1440, 900) && screen.Desktop!.Icons.Count == 4 && screen.Scanned.Count == 3, "The helper's JSON is read in full");
Check(MacScreenSearch.At(screen, 300, 300) == 10 && MacScreenSearch.At(screen, 1000, 300) == 20 && MacScreenSearch.At(screen, 1380, 200) == 0 && MacScreenSearch.At(screen, 700, 10) == 1,
    "The window showing at a point skips the full-display overlay and the transparent window");
Check(MacScreenSearch.ShownWindows(screen).Select(w => w.Id).SequenceEqual([10L, 20L]), "Only normal windows that show are searched by title");
var all = MacScreenSearch.Find(screen, ["報告"], "all");
string[] Names(MacScreenSearch.Result result) => result.Matches.Select(m => m.Name).ToArray();
Check(Names(all).SequenceEqual(["下載報告", "報告.pdf", "報告草稿", "報告2.docx", "舊報告", "報告 第二段", "報告 - 新聞", "報告", "報告產生器"]),
    "Matches come in reading order, row by row: " + string.Join("、", Names(all)));
Check(!Names(all).Contains("報告1.docx") && all.CoveredInWindows == 1, "An element of the Finder window under Safari is covered, not found");
Check(!Names(all).Contains("報告備份") && all.CoveredIcons == 1, "The desktop icon under Safari is covered; the ones beside the windows show through the overlay");
var draft = all.Matches.Single(m => m.Name == "報告草稿");
Check(draft.Kind == "desktop_icon" && draft.File == "報告草稿.docx" && draft.Path == "/Users/me/Desktop/報告草稿.docx" && !draft.Folder, "A desktop icon carries the file behind it, extension and all");
var folder = all.Matches.Single(m => m.Name == "舊報告");
Check(folder.Folder && folder.Type == "檔案夾" && folder.Path == "/Users/me/Desktop/舊報告", "A folder icon says it is a folder, with its path");
Check(MacScreenSearch.Find(screen, ["資料夾"], "desktop").Matches.Select(m => m.Name).SequenceEqual(["舊報告"]), "A search for 資料夾 finds folder icons");
var finder = all.Matches.Single(m => m.Kind == "window" && m.Name == "報告");
Check(finder.OpenFolder == "/Users/me/Documents/報告" && finder.Process == "Finder", "A Finder window names the folder it shows, from its document URL");
Check(all.Matches.Single(m => m.Name == "報告2.docx").OpenFolder == "/Users/me/Documents/報告" && all.Matches.Single(m => m.Name == "報告2.docx").Window == "報告", "So do the items in it");
var noDocument = MacScreenSearch.Parse(Snapshot(document: null));
Check(MacScreenSearch.Find(noDocument, ["報告"], "windows", [("報告", "/Users/me/Documents/報告"), ("下載項目", "/Users/me/Downloads")]).Matches.Single(m => m.Kind == "window" && m.Name == "報告").OpenFolder == "/Users/me/Documents/報告",
    "Without a document URL, Finder's own list gives the folder by the window title");
Check(MacScreenSearch.Find(noDocument, ["報告"], "windows").Matches.Single(m => m.Kind == "window" && m.Name == "報告").OpenFolder == null, "Without either, no folder is guessed");
var dock = all.Matches.Single(m => m.Name == "報告產生器");
Check(dock.Kind == "element" && dock.Window == "Dock" && dock.Type == "Dock 項目", "Dock items are found as elements of the Dock");
Check(all.Matches.Single(m => m.Name == "下載報告").Window == "報告 - 新聞" && all.Matches.Single(m => m.Name == "下載報告").Type == "按鈕", "Elements name their window and their role");
Check(all.Unfinished.SequenceEqual(["報告"]), "A window the helper could not finish is reported by its title");
Check(MacScreenSearch.Find(MacScreenSearch.Parse(Snapshot(finished: false)), ["報告"], "all").Unfinished.Order().SequenceEqual(["報告", "報告 - 新聞"]), "A window cut short is unfinished too");
Check(MacScreenSearch.Find(screen, ["報告"], "desktop").Matches.All(m => m.Kind == "desktop_icon") && MacScreenSearch.Find(screen, ["報告"], "windows").Matches.All(m => m.Kind != "desktop_icon"),
    "where limits the search to the desktop or to the windows");
var none = MacScreenSearch.Find(screen, ["報吿草稿"], "all");
Check(none.Matches.Count == 0 && none.Similar.Contains("報告草稿"), "A slip finds nothing but suggests the close name");
var untrusted = MacScreenSearch.Parse(Snapshot(trusted: false));
var titles = MacScreenSearch.Find(untrusted, ["報告"], "all");
Check(!untrusted.Trusted && untrusted.Desktop == null && untrusted.Scanned.Count == 0 && titles.Matches.All(m => m.Kind == "window") && titles.Matches.Count == 2,
    "Without Accessibility only the window titles are searched");
bool Throws(string json) { try { MacScreenSearch.Parse(json); return false; } catch (IOException) { return true; } }
Check(Throws("{\"error\":\"timeout\"}") && Throws("[]") && !Throws("{\"trusted\":false}"), "Helper errors become IOException; a bare answer is read with defaults");

// The real helper, when given: the JSON shape on this Mac.
if (args.Length > 0 && File.Exists(args[0]))
{
    string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(args[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException(string.Join(" ", arguments)); }
        return output;
    }
    using var trusted = JsonDocument.Parse(Run("trusted"));
    bool granted = trusted.RootElement.GetProperty("trusted").GetBoolean();
    Console.WriteLine($"     helper: trusted={granted} screenRecording={trusted.RootElement.GetProperty("screenRecording").GetBoolean()}");
    var clock = Stopwatch.StartNew();
    var live = MacScreenSearch.Parse(Run("find", "windows", "--exclude-pid", Environment.ProcessId.ToString()));
    Check(live.Displays.Count > 0 && live.Windows.Count > 0 && live.Trusted == granted && clock.Elapsed < TimeSpan.FromSeconds(15),
        $"The real helper lists {live.Displays.Count} display(s) and {live.Windows.Count} window(s) in {clock.ElapsedMilliseconds} ms");
    Check(granted ? live.Desktop == null : live.Scanned.Count == 0, "find windows reads no desktop; without Accessibility it reads no elements");
    var desktop = MacScreenSearch.Parse(Run("find", "desktop"));
    Check(granted ? desktop.Desktop != null : desktop.Desktop == null, "find desktop reads Finder's desktop when Accessibility is granted");
    Console.WriteLine("     shown windows: " + string.Join("、", MacScreenSearch.ShownWindows(live).Select(w => MacScreenSearch.Label(live, w))));
}

Console.WriteLine($"{passes} passed, {failures} failed");
return failures == 0 ? 0 : 1;
