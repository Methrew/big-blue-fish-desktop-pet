using System;
using System.Collections.Generic;
using System.Linq;

namespace BigBlueFish.Windows;

// MCP action names on every platform (play_action, interrupt_action, walk_to's arrive_action). The 106 clips are
// named in Simplified Chinese, their .webm file names. An Agent writing Taiwan Traditional, as its persona asks,
// still means the same clip, whether it changed only the characters (點擊回應) or also the Taiwan wording that
// CompanionTraditional writes (點選回應). The original Simplified id is what gets played and reported.
internal static class CompanionActionNames
{
    // Null when no clip has the name in either script.
    internal static string? Resolve(IEnumerable<string> ids, string name)
    {
        var all = ids as IReadOnlyCollection<string> ?? ids.ToArray();
        if (all.Contains(name, StringComparer.Ordinal)) return name;
        string converted = CompanionTraditional.Convert(name), folded = CompanionTraditional.Fold(name);
        return all.FirstOrDefault(id => CompanionTraditional.Convert(id) == converted) ?? all.FirstOrDefault(id => CompanionTraditional.Fold(id) == folded);
    }

    // Names the three closest clips, so a misspelt name is fixed in one step instead of another list_actions round.
    // Names are compared both folded to Simplified and in CompanionTraditional's wording (被滑鼠拖拽 is part of
    // 被鼠标拖拽悬空反馈 only in the latter); clips containing the name, or contained in it, come first.
    internal static string Unknown(IEnumerable<string> ids, string field, string name)
    {
        string folded = CompanionTraditional.Fold(name), converted = CompanionTraditional.Convert(name);
        static bool Overlaps(string a, string b) => a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
        var closest = ids.Select(id => (Id: id, Folded: CompanionTraditional.Fold(id), Converted: CompanionTraditional.Convert(id)))
            .OrderBy(x => Overlaps(x.Folded, folded) || Overlaps(x.Converted, converted) ? 0 : 1)
            .ThenBy(x => Math.Min(EditDistance(x.Folded, folded), EditDistance(x.Converted, converted)))
            .ThenBy(x => x.Id, StringComparer.Ordinal).Take(3).Select(x => x.Id);
        return $"{field} 沒有「{name}」這個動作。最接近的是：{string.Join("、", closest)}。請照 list_actions 的名稱原樣填，不要重複查 list_actions。";
    }

    // Same Levenshtein distance as CompanionScreenSearch.EditDistance, which the Linux and macOS builds do not compile.
    private static int EditDistance(string a, string b)
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
}
