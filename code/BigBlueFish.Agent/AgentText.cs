// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Text.RegularExpressions;

namespace BigBlueFish.Agent;

/// <summary>
/// Taiwan Traditional Chinese for the English an Agent sends for people to read. Only what is shown goes through it;
/// ids and values sent back to the Agent, and the raw titles that recognize the pet's own tools, stay as they are.
/// Proper nouns (model, product and user-named agent names), shell commands and anything not listed stay as sent.
/// The entries follow what claude-agent-acp (Claude Code) sends, plus the common words other Agents use for the same.
/// Three entry points: <see cref="LocalizeOption"/> for session option names and descriptions (fixed texts only),
/// <see cref="Localize"/> for permission buttons and sign-in methods, and <see cref="LocalizeTitle"/> for the title of
/// a permission request, which depends on the tool kind: a shell command or a sub-agent's own description stays as is.
/// </summary>
public static class AgentText
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        // Permission mode (claude-agent-acp session-mode.js)
        ["Mode"] = "權限／工作模式", ["Session permission mode"] = "這次對話的權限模式",
        ["Manual"] = "手動確認", ["Always ask before making changes"] = "修改前都先詢問",
        ["Accept edits"] = "自動接受編輯", ["Automatically accept all file edits"] = "自動接受所有檔案編輯",
        ["Plan"] = "先規劃", ["Create a plan before making changes"] = "修改前先擬定計畫",
        ["Auto"] = "自動", ["Claude handles permission decisions"] = "由 Claude 代你決定是否允許操作",
        ["Bypass permissions"] = "略過權限確認", ["Accepts all permissions"] = "接受所有權限要求",
        // Model, effort, Fast mode, Ultracode and the Agent picker
        ["Model"] = "模型", ["AI model to use"] = "要使用的 AI 模型", ["Default (recommended)"] = "預設（建議）",
        ["Effort"] = "思考深度", ["Reasoning effort"] = "思考深度", ["Thought level"] = "思考深度",
        ["Available effort levels for this model"] = "這個模型可用的思考深度", ["Current reasoning effort"] = "目前的思考深度",
        ["Default"] = "預設", ["None"] = "無", ["Minimal"] = "最低", ["Low"] = "低", ["Medium"] = "中", ["High"] = "高",
        ["Xhigh"] = "很高", ["Max"] = "最高",
        ["Fast mode"] = "快速模式", ["Faster responses on supported models"] = "支援的模型會回應得更快",
        ["not available on the free plan"] = "免費方案不提供",
        ["requires extra usage to be enabled for this account"] = "這個帳號需要先啟用額外用量",
        ["not available for the selected model"] = "所選模型不支援",
        ["not available on this API provider"] = "這個 API 供應商不支援",
        ["disabled by environment configuration"] = "已被環境設定停用",
        ["eligibility could not be verified (network error)"] = "網路錯誤，無法確認使用資格",
        ["On"] = "開啟", ["Off"] = "關閉",
        ["Dynamic workflows on every task, at any effort level"] = "每項工作都使用動態工作流程，不限思考深度",
        ["Main-thread agent persona"] = "主對話使用的 Agent 角色", ["Subagent type"] = "子 Agent 類型",
        ["Standard Claude Code agent"] = "標準的 Claude Code Agent",
        // Permission buttons
        ["Yes"] = "允許", ["No"] = "拒絕", ["Allow"] = "允許", ["Allow once"] = "允許一次",
        ["Allow always"] = "一律允許", ["Always allow"] = "一律允許", ["Reject"] = "拒絕", ["Deny"] = "拒絕",
        ["Reject once"] = "拒絕一次", ["Reject always"] = "一律拒絕", ["Always reject"] = "一律拒絕", ["Always deny"] = "一律拒絕",
        ["Cancel"] = "取消",
        ["Yes, during this session"] = "允許，這次對話中讀取檔案都不再詢問",
        ["Yes, allow all edits during this session"] = "允許，這次對話中的編輯都不再詢問",
        ["Yes, and allow Claude to edit its own settings for this session"] = "允許，這次對話中也讓 Claude 修改自己的設定",
        ["Yes, enter plan mode"] = "好，進入規劃模式", ["No, start implementing now"] = "不要，現在就開始實作",
        ["Yes, and use auto mode"] = "好，並改用自動模式", ["Yes, and bypass permissions"] = "好，並略過權限確認",
        ["Yes, auto-accept edits"] = "好，自動接受編輯", ["Yes, manually approve edits"] = "好，編輯時逐一確認",
        ["No, keep planning"] = "不要，繼續規劃",
        // Sign-in methods
        ["Log in with Claude"] = "使用 Claude 帳號登入", ["Run `claude /login` in the terminal"] = "在終端機執行 `claude /login`",
        ["Claude Subscription"] = "Claude 訂閱方案", ["Use Claude subscription"] = "使用 Claude 訂閱方案",
        ["Use Anthropic Console (API usage billing)"] = "使用 Anthropic Console（依 API 用量計費）",
        ["Custom model gateway"] = "自訂模型閘道", ["Use a custom gateway to authenticate and access models"] = "透過自訂閘道驗證並使用模型",
    };

    // Fixed tool titles, for any tool kind but a shell command or a sub-agent (see LocalizeTitle).
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["Preparing file…"] = "正在準備檔案…", ["Edit"] = "編輯", ["Terminal"] = "終端機", ["Fetch"] = "擷取網頁",
        ["Web search"] = "網頁搜尋", ["Approve Plan"] = "核准計畫", ["Exited Plan Mode"] = "已離開規劃模式",
        ["EnterPlanMode"] = "進入規劃模式", ["Asking for your input"] = "需要你的回答", ["Load skill"] = "載入技能",
        ["Edit notebook"] = "編輯 Notebook", ["Unknown Tool"] = "不明的工具",
    };
    // Sub-agent and task tools (kind "think"): only these titles are fixed texts; anything else is a model's own words.
    private static readonly Dictionary<string, string> TaskTitles = new(StringComparer.Ordinal)
    {
        ["Task"] = "工作", ["Create task"] = "建立工作", ["Update task"] = "更新工作", ["List tasks"] = "列出工作",
        ["Get task"] = "檢視工作", ["Update TODOs"] = "更新待辦清單",
    };

    // Slots: {1} keeps a group as it is, {F1} reads it as one folder, {D1} as claude-agent-acp's list of folders
    // (displayPaths), {C1} as its list of commands (displayList), {P1} as the context use of a plan ("42" → （已用 42%）).
    private static readonly (Regex Pattern, string Template)[] ButtonPatterns =
    [
        (new(@"^Yes, allow all edits in (.+) during this session$"), "允許，這次對話中 {F1} 裡的編輯都不再詢問"),
        (new(@"^Yes, allow reading from (.+) during this session$"), "允許，這次對話中可以讀取 {F1}"),
        (new(@"^Yes, allow reading from (.+) from this project$"), "允許，這個專案之後都可以讀取 {D1}"),
        (new(@"^Yes, and always allow access to (.+) from this project$"), "允許，這個專案之後都可以存取 {D1}"),
        (new(@"^Yes, and don't ask again for (.+) commands$"), "允許，之後 {C1} 指令都不再詢問"),
        (new(@"^Yes, and don't ask again for (.+)$"), "允許，之後 {1} 都不再詢問"),
        (new(@"^Yes, and allow access to (.+[/\\](?: and \d+ more)?|similar) and (.+) commands$"), "允許，之後也可以存取 {D1}、執行 {C2} 指令"),
        (new(@"^Yes, and allow (.+) access and (.+) commands$"), "允許，之後也可以存取 {D1}、執行 {C2} 指令"),
        (new(@"^Yes, clear context(?: \((\d+)% used\))? and use auto mode$"), "好，清除上下文{P1}並改用自動模式"),
        (new(@"^Yes, clear context(?: \((\d+)% used\))? and bypass permissions$"), "好，清除上下文{P1}並略過權限確認"),
        (new(@"^Yes, clear context(?: \((\d+)% used\))? and auto-accept edits$"), "好，清除上下文{P1}並自動接受編輯"),
    ];
    // Tool titles that name a file, a URL or a search; only for the tool kinds that send them.
    private static readonly (Regex Pattern, string Template, string[] Kinds)[] TitlePatterns =
    [
        (new(@"^Write (.+)$"), "寫入 {1}", ["edit"]),
        (new(@"^Edit (.+)$"), "編輯 {1}", ["edit"]),
        (new(@"^Read (.+) \(from line (\d+)\)$"), "讀取 {1}（從第 {2} 行起）", ["read"]),
        (new(@"^Read (.+)$"), "讀取 {1}", ["read"]),
        (new(@"^Fetch (.+) \(from char (\d+)\)$"), "擷取網頁 {1}（從第 {2} 個字元起）", ["fetch"]),
        (new(@"^Fetch (.+)$"), "擷取網頁 {1}", ["fetch"]),
        (new(@"^Search ""(.+)""$"), "搜尋「{1}」", ["fetch", "search"]),
        (new(@"^Find (`.+`)$"), "尋找檔案 {1}", ["search"]),
        (new(@"^Load skill: (.+)$"), "載入技能：{1}", ["other"]),
        (new(@"^Create task: (.+)$"), "建立工作：{1}", ["think"]),
        (new(@"^Update task: (.+)$"), "更新工作：{1}", ["think"]),
        (new(@"^Update TODOs: (.+)$"), "更新待辦清單：{1}", ["think"]),
    ];

    /// <summary>A session option's name, description, choice or group: translated when it is a known fixed text.</summary>
    public static string LocalizeOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        return Known(text.Trim()) ?? text;
    }

    /// <summary>A permission button or a sign-in method: translated when it is a known text or one of its forms.</summary>
    public static string Localize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        string trimmed = text.Trim();
        if (Known(trimmed) is { } known) return known;
        foreach (var (pattern, template) in ButtonPatterns)
            if (pattern.Match(trimmed) is { Success: true } match) return Fill(template, match);
        return text;
    }

    /// <summary>
    /// The title of a permission request, by the ACP tool kind: a shell command (kind "execute") is shown exactly as it
    /// will run, a sub-agent's description (kind "think") stays in its own words, and an unknown kind is left alone.
    /// </summary>
    public static string LocalizeTitle(string? title, string? kind)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? "";
        string trimmed = title.Trim();
        switch (kind)
        {
            case "think":
                if (TaskTitles.TryGetValue(trimmed, out var task)) return task;
                break;
            case "read" or "edit" or "delete" or "move" or "search" or "fetch" or "switch_mode" or "other":
                if (Titles.TryGetValue(trimmed, out var fixedTitle)) return fixedTitle;
                break;
            default:
                return title;
        }
        foreach (var (pattern, template, kinds) in TitlePatterns)
            if (kinds.Contains(kind) && pattern.Match(trimmed) is { Success: true } match) return Fill(template, match);
        return title;
    }

    // A fixed text, or one with its reason after a dash when both parts are fixed texts:
    // "Faster responses on supported models — not available on the free plan" → 支援的模型會回應得更快（免費方案不提供）.
    private static string? Known(string text)
    {
        if (Exact.TryGetValue(text, out var known)) return known;
        int dash = text.IndexOf(" — ", StringComparison.Ordinal);
        return dash > 0 && Exact.TryGetValue(text[..dash], out var head) && Exact.TryGetValue(text[(dash + 3)..], out var tail)
            ? head + "（" + tail + "）" : null;
    }

    private const char Joined = '\u0001';
    private static string Fill(string template, Match match)
    {
        string filled = Regex.Replace(template, @"\{([DCPF]?)(\d)\}", slot =>
        {
            var group = match.Groups[int.Parse(slot.Groups[2].Value)];
            return slot.Groups[1].Value switch
            {
                "D" => Folders(group.Value),
                "C" => Commands(group.Value),
                "F" => Folder(group.Value),
                "P" => group.Success ? "（已用 " + group.Value + "%）" : "",
                _ => group.Value,
            };
        });
        // A Chinese word in a slot ("類似的路徑") is written without the spaces kept around English names.
        return Regex.Replace(filled, " ?" + Joined + " ?", "");
    }

    private static string Chinese(string text) => Joined + text + Joined;

    // One folder (filesystem.js fileSessionLabel): its name ends with a path separator; the root is "this directory/".
    private static string Folder(string value) => value is "this directory/" or "this directory\\" ? Chinese("這個資料夾") : value;

    // displayPaths: one or two names that end with a separator, "a/, b/ and N more", or "similar" past 50 characters.
    private static string Folders(string value)
    {
        if (value == "similar") return Chinese("類似的路徑");
        var more = Regex.Match(value, @"^(.+[/\\]), (.+[/\\]) and (\d+) more$");
        if (more.Success) return Folder(more.Groups[1].Value) + "、" + Folder(more.Groups[2].Value) + " 和其他 " + more.Groups[3].Value + " 個路徑";
        return string.Join("、", Regex.Split(value, @"(?<=[/\\])(?:, and |, | and )").Select(Folder));
    }

    // displayList of command prefixes: "git", "git and npm", "a, b, and c", or "similar". A list with capital letters is
    // an MCP tool's display name ("Find and Replace") and stays as written.
    private static string Commands(string value)
    {
        if (value == "similar") return Chinese("類似的");
        if (value.Any(char.IsUpper)) return value;
        return string.Join("、", Regex.Split(value, @", and |, | and "));
    }
}
