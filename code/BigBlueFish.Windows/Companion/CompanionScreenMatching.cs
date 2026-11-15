// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigBlueFish.Windows;

// A rectangle on the screen, in the unit the platform's screen search reports: physical pixels of the Windows virtual
// desktop, global points of macOS (top-left of the main display is the origin).
internal readonly record struct ScreenBox(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal double CenterX => X + Width / 2;
    internal double CenterY => Y + Height / 2;
    internal ScreenBox Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);
    internal bool Contains(ScreenBox other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
    internal bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
    internal bool IntersectsWith(ScreenBox other) => other.X < Right && X < other.Right && other.Y < Bottom && Y < other.Bottom;
}

// The text and layout rules of find_on_screen and walk_to's desktop_item, taken out of the taiwan-edition's
// CompanionScreenSearch.cs and CompanionWalk.cs (acer1204) so the Windows and macOS apps find and name things alike.
// Nothing here touches the screen: callers hand in the names and rectangles they read.
internal static class CompanionScreenMatching
{
    internal static bool Matches(IReadOnlyList<string> terms, params string?[] texts) =>
        terms.Any(term => texts.Any(text => text != null && Contains(text, term)));

    // Ignoring case. A term's letters start and end on a word's edge, so tar is not found in Star nor doc in Docker
    // (a camelCase hump counts: Report in MyReportFinal), and its digits start on a number's edge (2026 finds
    // 20260911, 005 does not find PC2005). Chinese has no word edges and matches anywhere.
    internal static bool Contains(string text, string term)
    {
        for (int at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(term, at + 1, StringComparison.OrdinalIgnoreCase))
            if (Edge(text, at, term[0], true) && Edge(text, at + term.Length, term[^1], false)) return true;
        return false;
    }

    private static bool Edge(string text, int at, char side, bool start)
    {
        if (at == 0 || at == text.Length || !char.IsAsciiLetterOrDigit(side)) return true;
        char before = text[at - 1], after = text[at];
        if (char.IsAsciiDigit(side)) return !start || !char.IsAsciiDigit(before);
        return !char.IsAsciiLetter(before) || !char.IsAsciiLetter(after) || char.IsAsciiLetterLower(before) && char.IsAsciiLetterUpper(after);
    }

    // Nested matches name one thing once: an Explorer or Finder item and its label, a window and its title bar or page.
    // Only within one window (source): an item in a window in front is not the title of a larger one behind it.
    internal static List<T> Distinct<T>(IEnumerable<T> matches, Func<T, ScreenBox> bounds, Func<T, long> source, Func<T, string> name)
    {
        var kept = new List<T>();
        foreach (var match in matches.OrderByDescending(m => bounds(m).Width * bounds(m).Height))
            if (!kept.Any(k => source(k) == source(match) && bounds(k).Inflate(2, 2).Contains(bounds(match)) && name(k).Contains(name(match), StringComparison.OrdinalIgnoreCase)))
                kept.Add(match);
        return kept;
    }

    // Top to bottom in rows, left to right within a row: the order the fish visits them and the answer lists them.
    internal static List<T> ReadingOrder<T>(IEnumerable<T> matches, Func<T, ScreenBox> bounds)
    {
        var rows = new List<List<T>>();
        foreach (var match in matches.OrderBy(m => bounds(m).CenterY))
        {
            if (rows.Count > 0)
            {
                var first = bounds(rows[^1][0]);
                var box = bounds(match);
                if (box.CenterY - first.CenterY <= Math.Max(4, Math.Min(first.Height, box.Height) / 2)) { rows[^1].Add(match); continue; }
            }
            rows.Add([match]);
        }
        return rows.SelectMany(row => row.OrderBy(m => bounds(m).CenterX)).ToList();
    }

    // Names a slip or two away from a term, for an answer that found nothing (資源回收桶 for 資源回收筒).
    internal static List<string> Similar(IEnumerable<string> names, IReadOnlyList<string> terms)
    {
        static int Reach(string term) => Math.Max(1, term.Length / 3);
        return names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => (Name: n, Over: terms.Where(t => Math.Abs(n.Length - t.Length) <= Reach(t)).Select(t => EditDistance(n, t) - Reach(t)).DefaultIfEmpty(1).Min()))
            .Where(n => n.Over <= 0).OrderBy(n => n.Over).Take(8).Select(n => n.Name).ToList();
    }

    // Without the extension Explorer or Finder hides: a short tail of letters and digits after the last dot, with a
    // letter in it (docx, mp4, 7z). "Python 3.12" keeps its version number.
    internal static string Stem(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || name.Length - dot - 1 is < 1 or > 5) return name;
        var tail = name[(dot + 1)..];
        return tail.All(char.IsAsciiLetterOrDigit) && tail.Any(char.IsAsciiLetter) ? name[..dot] : name;
    }

    internal static int EditDistance(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int diagonal = row[0]; row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int above = row[j];
                row[j] = Math.Min(Math.Min(above + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    // walk_to's desktop_item. Ignoring case: the exact name, then the same name with or without its extension, then
    // the one name containing the query, then a one-character slip (資源回收桶 for 資源回收筒) that no other name
    // matches as well, so a typo costs no second call. Several names at one step are listed instead of picking
    // whichever the system happens to give first (報告1 against 報告10.docx and 報告1.pdf).
    // files: the desktop's file names, when known, so an icon shown without its extension is taken for 報告1.docx
    // only if that file is there (not 報告1.pdf, nor a folder 報告1).
    internal static (ScreenBox Bounds, string Name) MatchDesktopItem(IReadOnlyList<(string Name, ScreenBox Bounds)> items, string query, IReadOnlyCollection<string>? files)
    {
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        (ScreenBox, string)? One(List<(string Name, ScreenBox Bounds)> found, string several)
        {
            if (found.Count == 0) return null;
            var names = found.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length > 1) throw new ArgumentException($"有 {names.Length} 個桌面圖示的名稱{several}「{query}」，請用完整名稱指定其中一個：{string.Join("、", names.Take(12))}");
            // Extensions hidden: 報告.docx and 報告.pdf both show as 報告, and nothing on the screen tells them apart.
            if (found.Count > 1)
                throw new ArgumentException($"桌面上有 {found.Count} 個圖示都叫「{names[0]}」（副檔名被隱藏），從名稱分不出是哪一個。它們在 "
                    + string.Join("、", found.Select(f => $"x={f.Bounds.CenterX:0}, y={f.Bounds.CenterY:0}, width={f.Bounds.Width:0}, height={f.Bounds.Height:0}"))
                    + "。可以用 stops 每個都跑，或用 capture_screen 從圖示分辨。");
            return (found[0].Bounds, found[0].Name);
        }
        // The extension one way only: 報告1 finds 報告1.docx and 報告1.docx finds an icon shown as 報告1, but 報告1.pdf never
        // stands in for 報告1.docx, and a version number is no extension ("Python 3" is not "Python 3.12").
        var bare = Stem(query);
        bool hiddenThere = bare == query || files == null || files.Contains(query, StringComparer.OrdinalIgnoreCase);
        if (One(items.Where(i => Same(i.Name, query)).ToList(), "是") is { } exact) return exact;
        if (One(items.Where(i => Same(Stem(i.Name), query) || hiddenThere && Same(i.Name, bare)).ToList(), "去掉副檔名後都是") is { } stem) return stem;
        // A number in the query is a whole number in the name: 報告1 is not part of 報告10.docx.
        if (One(items.Where(i => WholeNumbers(i.Name, query)).ToList(), "包含") is { } part) return part;
        // Two characters or fewer, one wrong one is half the name: too much of a guess. Nor is a different
        // number a slip: 報告2 is another file than 報告1.
        if (query.EnumerateRunes().Count() >= 3
            && items.Where(i => EditDistance(i.Name, query) == 1 && Digits(i.Name) == Digits(query)).ToList() is { Count: > 0 } close
            && close.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return One(close, "接近")!.Value;
        if (items.Count == 0) throw new ArgumentException("桌面上沒有可辨識的圖示。");
        // Only names close enough to be what was meant; listing unrelated ones invites a wrong guess.
        int reach = Math.Max(2, query.Length / 3);
        var near = items.Select(i => i.Name).Distinct().Select(n => (Name: n, Distance: EditDistance(n, query))).Where(n => n.Distance <= reach)
            .OrderBy(n => n.Distance).Take(8).Select(n => n.Name).ToArray();
        throw new ArgumentException(near.Length == 0 ? $"桌面上沒有名為「{query}」或相近名稱的圖示。" : $"找不到名為「{query}」的桌面圖示。相近的名稱：{string.Join("、", near)}");
    }

    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

    private static bool WholeNumbers(string name, string query)
    {
        for (int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0; at = name.IndexOf(query, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            int end = at + query.Length;
            if (!(char.IsDigit(query[0]) && at > 0 && char.IsDigit(name[at - 1])) && !(char.IsDigit(query[^1]) && end < name.Length && char.IsDigit(name[end]))) return true;
        }
        return false;
    }
}
