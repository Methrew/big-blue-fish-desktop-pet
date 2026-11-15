// Modified by acer1204 on 2026-10-30 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Diagnostics;
using System.Text.Json;
using BigBlueFish.Control;
using BigBlueFish.Diagnostics;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
// macOS keeps a relative pipe under the long $TMPDIR, past the 104-byte socket path limit; the pet's own pipes live in /tmp there too.
var pipe = OperatingSystem.IsMacOS() ? "/tmp/bbf-verify-" + Guid.NewGuid().ToString("N")[..12] : "BigBlueFish.Verification." + Guid.NewGuid().ToString("N");
using var server = new ControlServer((name, args, token) => Task.FromResult(ControlTools.Text(new { name, args })), pipe);
var bridge = Path.GetFullPath(args[0]);
Process Start() => Process.Start(new ProcessStartInfo(bridge) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
    ArgumentList = { "--pipe", pipe } })!;
using var process = Start();
var notices = new List<JsonElement>();
async Task<JsonElement> Request(int id, string method, object? parameters = null)
{
    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }));
    using var timeout = new CancellationTokenSource(10000);
    while (true)
    {
        var line = await process.StandardOutput.ReadLineAsync(timeout.Token) ?? throw new IOException("Bridge exited: " + await process.StandardError.ReadToEndAsync());
        using var doc = JsonDocument.Parse(line); var value = doc.RootElement.Clone();
        if (value.TryGetProperty("id", out var found) && found.ValueKind == JsonValueKind.Number && found.GetInt32() == id) return value;
        notices.Add(value);
    }
}
var response = await Request(1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "verification", version = "1" } });
Check(response.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "Protocol handshake");
var instructions = response.GetProperty("result").GetProperty("instructions").GetString()!;
Check(instructions.Contains("user_message"), "Handshake explains input event handling");
Check(instructions.Contains("user_returned"), "Handshake names the user_returned event");
Check(instructions.Contains("say") && instructions.Contains("get_operation_guide"), "Handshake explains tool-only speech and the operation guide");
// Linux has no reader of the screen, so neither find_on_screen nor walk_to nor their coordinates are offered there.
bool screenTools = !OperatingSystem.IsLinux();
Check(instructions.Contains("attachments") && instructions.Contains("space=capture") == screenTools && instructions.Contains("台灣繁體中文"), "Handshake covers attachments, walk_to coordinates and Taiwan Traditional output");
await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
response = await Request(2, "tools/list");
var tools = response.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
// Typed input goes only to a client that listens for the events, never to one that only calls tools (Hermes' own MCP session).
Check(server.Connections == 1 && server.Listeners == 0, "A client that has only initialized does not take typed input");
Check(tools.Length == (screenTools ? 17 : 15), "Seventeen tools: the fourteen of the Agent connection plus get_context, walk_to and find_on_screen (Linux without the last two)");
foreach (var tool in tools) Check(tool.GetProperty("inputSchema").GetProperty("type").GetString() == "object", "Object schema");
var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();
Check(new[] { "get_settings", "get_context", "update_settings", "list_actions", "capture_foreground", "capture_screen", "play_action", "interrupt_action", "walk_to", "find_on_screen",
    "say", "say_next", "request_local_speech", "get_operation_guide", "get_wakeups", "schedule_wakeup", "cancel_wakeup" }.Where(n => screenTools || n is not ("walk_to" or "find_on_screen")).SequenceEqual(toolNames),
    "Merged tool list in its documented order");
var wakeup = tools.Single(t => t.GetProperty("name").GetString() == "schedule_wakeup").GetProperty("inputSchema");
Check(wakeup.GetProperty("required").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "purpose", "delay_seconds" })
    && wakeup.GetProperty("properties").GetProperty("repeat_seconds").GetProperty("type").EnumerateArray().Any(t => t.GetString() == "null"), "schedule_wakeup needs a purpose and a delay and may repeat");
if (screenTools)
{
    Check(toolNames.IndexOf("walk_to") == toolNames.IndexOf("interrupt_action") + 1, "walk_to listed right after interrupt_action");
    var walkSchema = tools.Single(t => t.GetProperty("name").GetString() == "walk_to").GetProperty("inputSchema");
    var walkProperties = walkSchema.GetProperty("properties");
    Check(walkSchema.GetProperty("required").GetArrayLength() == 0 && !walkSchema.GetProperty("additionalProperties").GetBoolean()
        && walkProperties.GetProperty("space").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "screen", "capture" })
        && new[] { "x", "y", "width", "height", "capture_id", "image_width", "image_height", "desktop_item", "side", "arrive_action", "say", "wait", "stops", "pause_seconds" }.All(p => walkProperties.TryGetProperty(p, out _)),
        "walk_to documents screen and capture coordinates, desktop items, stops and its options");
    var stopItems = walkProperties.GetProperty("stops").GetProperty("items");
    Check(walkProperties.GetProperty("stops").GetProperty("maxItems").GetInt32() == 20 && !stopItems.GetProperty("additionalProperties").GetBoolean()
        && new[] { "x", "y", "width", "height", "desktop_item", "label" }.All(p => stopItems.GetProperty("properties").TryGetProperty(p, out _)), "walk_to stops take a target and a label each");
    var find = tools.Single(t => t.GetProperty("name").GetString() == "find_on_screen");
    Check(toolNames.IndexOf("find_on_screen") == toolNames.IndexOf("walk_to") + 1 && find.GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "query" })
        && find.GetProperty("inputSchema").GetProperty("properties").GetProperty("where").GetProperty("enum").GetArrayLength() == 3
        && find.GetProperty("description").GetString()!.Contains("walk_to") && find.GetProperty("description").GetString()!.Contains("openFolder"),
        "find_on_screen follows walk_to, needs a query, hands its matches to walk_to and names Explorer folders");
    // macOS reads the screen through Accessibility and gives global points.
    if (OperatingSystem.IsMacOS())
        Check(find.GetProperty("description").GetString()!.Contains("輔助使用") && walkProperties.GetProperty("space").GetProperty("description").GetString()!.Contains("macOS全域座標點")
            && instructions.Contains("全域座標點"), "macOS describes its Accessibility permission and global points");
}
var context = tools.Single(t => t.GetProperty("name").GetString() == "get_context");
Check(toolNames.IndexOf("get_context") == toolNames.IndexOf("get_settings") + 1 && context.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Count() == 0
    && new[] { "summary", "idleSeconds", "activeMinutes", "fullscreen", "batteryPercent", "foreground", "weather", "openFolders", "lastReminder", "SitReminderMinutes" }.All(context.GetProperty("description").GetString()!.Contains),
    "get_context follows get_settings, takes no arguments and names what it reports and where its reminders are switched");
Check(ControlTools.PrivacyWarning.Contains("閒置時間") && ControlTools.PrivacyWarning.Contains("視窗標題"), "The consent warning mentions the usage state get_context reads");
Check(ControlTools.McpPrivacyWarning.Contains("閒置時間") && ControlTools.McpPrivacyWarning.Contains("視窗標題") && ControlTools.McpPrivacyWarning.Contains("MCP 介面"),
    "The MCP interface consent mentions the same usage state");
foreach (var capture in new[] { "capture_foreground", "capture_screen" })
    Check(tools.Single(t => t.GetProperty("name").GetString() == capture).GetProperty("description").GetString()!.Contains("captureId") == screenTools, capture + " describes its coordinate metadata where walk_to takes it");
// ControlServer checks only tool names, so walk_to's optional arguments are validated by these helpers.
var sample = JsonSerializer.SerializeToElement(new { x = 12.5, wait = false, space = "capture", word = "text", big = 1e9, empty = (string?)null });
Check(ControlTools.OptionalNumber(sample, "x", -10, 100) == 12.5 && ControlTools.OptionalNumber(sample, "y", 0, 1) == null && ControlTools.OptionalNumber(sample, "empty", 0, 1) == null
    && !ControlTools.OptionalBool(sample, "wait", true) && ControlTools.OptionalBool(sample, "missing", true)
    && ControlTools.OptionalChoice(sample, "space", "screen", "capture") == "capture" && ControlTools.OptionalChoice(sample, "side", "auto", "left") == "auto"
    && ControlTools.OptionalText(sample, "word", 10) == "text" && ControlTools.OptionalText(sample, "empty") == null, "Optional arguments parse with their defaults");
bool Rejected(Action parse) { try { parse(); return false; } catch (ArgumentException) { return true; } }
string Refusal(Action parse) { try { parse(); return ""; } catch (ArgumentException error) { return error.Message; } }
var texts = JsonSerializer.SerializeToElement(new { blank = "  \n ", empty = "", long_ = new string('魚', 11) });
Check(Refusal(() => ControlTools.RequiredText(texts, "blank")) == "blank 不能只有空白。" && Refusal(() => ControlTools.RequiredText(texts, "long_", 10)) == "long_ 最多 10 個字元。"
    && Refusal(() => ControlTools.RequiredText(texts, "empty", 10)) == "empty 必須是 1–10 字元的文字。" && Refusal(() => ControlTools.RequiredText(texts, "missing")).Contains("必須是"),
    "A refused text says whether it was blank, too long or missing");
// update_settings names why a field it does not take is refused, the same on every platform.
Check(ControlTools.RefusedSetting("ModelSource", false) == "模型來源與遠端模型只能由使用者在設定中修改。" && ControlTools.RefusedSetting("RemoteUrl", false).Contains("只能由使用者")
    && ControlTools.RefusedSetting("AgentHarnessId", false).StartsWith("Agent 連線只能由使用者") && ControlTools.RefusedSetting("McpEnabled", false).StartsWith("MCP 介面只能由使用者")
    && ControlTools.RefusedSetting("CwaApiKeyProtected", true).StartsWith("API Key 與授權碼") && ControlTools.RefusedSetting("Nonsense", false) == "未知設定：Nonsense"
    && ControlTools.RefusedSetting("ShowOnAllSpaces", false).Contains("macOS"), "Read-only, private and unknown settings are refused with their reason");
Check(ControlTools.SettingNames.Contains("ShowOnAllSpaces") == OperatingSystem.IsMacOS(), "ShowOnAllSpaces is writable on macOS only");
Check(Rejected(() => ControlTools.OptionalNumber(sample, "word", 0, 1)) && Rejected(() => ControlTools.OptionalNumber(sample, "big", 0, 100))
    && Rejected(() => ControlTools.OptionalBool(sample, "x", true)) && Rejected(() => ControlTools.OptionalChoice(sample, "space", "screen"))
    && Rejected(() => ControlTools.OptionalText(sample, "word", 2)), "Invalid optional arguments are rejected");
// The Hermes bridge marks its progress lines so the pet keeps them out of the chat history; plain say calls stay as they were.
var sayInput = tools.Single(t => t.GetProperty("name").GetString() == "say").GetProperty("inputSchema");
Check(sayInput.GetProperty("properties").GetProperty("transient").GetProperty("type").GetString() == "boolean"
    && sayInput.GetProperty("required").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "text" }), "say takes an optional transient flag");
var settingSchema = tools.Single(t => t.GetProperty("name").GetString() == "update_settings").GetProperty("inputSchema").GetProperty("properties").GetProperty("settings");
Check(!settingSchema.GetProperty("additionalProperties").GetBoolean(), "Settings schema rejects undocumented fields");
var settingProperties = settingSchema.GetProperty("properties");
Check(settingProperties.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(ControlTools.SettingNames.Order()), "Every writable setting is documented exactly once");
Check(!settingProperties.TryGetProperty("McpEnabled", out _), "User-only MCP switch absent from writable schema");
foreach (var userOnly in new[] { "AgentEnabled", "AgentHarnessId", "ModelSource", "RemoteUrl", "RemoteModel", "ScreenImages", "RemoteApiKeyProtected", "CwaApiKeyProtected" })
    Check(!settingProperties.TryGetProperty(userOnly, out _), "User-only setting absent from writable schema: " + userOnly);
Check(new[] { "AgentDailySpeech", "AgentRespondToClick", "AgentRespondToDrag", "McpChatEnabled", "DailyBriefing", "WelcomeBackMinutes", "SitReminderMinutes", "LateReminder" }
    .All(name => settingProperties.TryGetProperty(name, out _)), "Agent, chat, briefing and reminder settings are writable");
var region = settingProperties.GetProperty("WeatherRegionCode");
var regionPattern = new System.Text.RegularExpressions.Regex(region.GetProperty("pattern").GetString()!);
Check(new[] { "TW-061-01", "340104", "US-06037", "JP-1850147" }.All(code => regionPattern.IsMatch(code) && region.GetProperty("description").GetString()!.Contains(code))
    && !regionPattern.IsMatch("臺北市中正區") && !regionPattern.IsMatch("TW-061-1"), "Region codes of Taiwan, China, the US and other countries fit the pattern, each with an example");
foreach (var property in settingProperties.EnumerateObject()) Check(!string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()), "Setting description present: " + property.Name);
var interval = settingProperties.GetProperty("AnimationIntervalSeconds");
Check(interval.GetProperty("type").GetString()=="integer" && interval.GetProperty("minimum").GetInt32()==30 && interval.GetProperty("maximum").GetInt32()==300, "Animation interval schema matches runtime bounds");
Check(settingProperties.GetProperty("SpeechMinMinutes").GetProperty("enum").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{2,3,5,10,15,20}), "Speech range enum documented");
// The Hermes bridge reads this one from get_settings, so its choices are a contract with it.
Check(settingProperties.GetProperty("EventReminderMinutes").GetProperty("enum").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{0,5,10,15,30}), "Event reminder choices documented");
foreach (var name in new[]{"Left","Top","LastBrushDate"}) Check(settingProperties.GetProperty(name).GetProperty("type").EnumerateArray().Any(t=>t.GetString()=="null"),"Nullable setting accurately documented: " + name);
response = await Request(3, "tools/call", new { name = "say", arguments = new { text = "主人好~原文含有英文、換行\n和標點。" } });
Check(response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains("say"), "Tool result structure");
await Request(4, "resources/subscribe", new { uri = "bigbluefish://interactions" });
Check(server.Listeners == 1, "Subscribing to the interactions makes a listener");
server.Publish("click", new { button = "left", x = 20, y = 30 });
response = await Request(5, "resources/read", new { uri = "bigbluefish://interactions" });
Check(response.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!.Contains("click"), "Readable interaction history");
Check(notices.Any(n => n.TryGetProperty("method", out var m) && m.GetString() == "notifications/message"), "Interaction pushed without polling");
Check(notices.Any(n => n.TryGetProperty("method", out var m) && m.GetString() == "notifications/resources/updated"), "Subscription pushed");
response = await Request(6, "tools/call", new { name = "unknown", arguments = new { } });
Check(response.GetProperty("result").GetProperty("isError").GetBoolean(), "Unknown tool returns tool error");
// A pet without some tools (macOS and Linux) leaves them out of tools/list and refuses them, as if they did not exist.
ControlTools.Unavailable.UnionWith(["walk_to", "find_on_screen"]);
response = await Request(6001, "tools/list");
var offered = response.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
Check(offered.Count == 15 && !offered.Contains("walk_to") && !offered.Contains("find_on_screen") && offered.Contains("get_context"), "Unavailable tools are left out of tools/list");
response = await Request(6002, "tools/call", new { name = "walk_to", arguments = new { x = 1, y = 1 } });
Check(response.GetProperty("result").GetProperty("isError").GetBoolean(), "An unavailable tool is refused like an unknown one");
ControlTools.Unavailable.Clear();
response = await Request(6003, "tools/list");
Check(response.GetProperty("result").GetProperty("tools").GetArrayLength() == 17, "The full list returns once nothing is unavailable");
response = await Request(7, "resources/read", new { uri = "other://wrong" });
Check(response.GetProperty("error").GetProperty("code").GetInt32() == -32602, "Unknown resource rejected");
response = await Request(8, "logging/setLevel", new { level = "not-a-level" });
Check(response.TryGetProperty("error", out _), "Invalid log level rejected");
response = await Request(9, "ping"); Check(response.TryGetProperty("result", out _), "Ping after errors");
await Request(10,"logging/setLevel",new{level="warning"}); int beforeNotices=notices.Count;
server.Publish("user_message",new{text="原文\n第二行<> &"});
response=await Request(11,"resources/read",new{uri="bigbluefish://interactions"});
var records=JsonDocument.Parse(response.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!).RootElement;
Check(records[records.GetArrayLength()-1].GetProperty("detail").GetProperty("text").GetString()=="原文\n第二行<> &","Input resource contains literal submitted text");
Check(!notices.Skip(beforeNotices).Any(n=>n.TryGetProperty("method",out var m)&&m.GetString()=="notifications/message"),"Warning log level filters info interaction notifications");
Check(notices.Skip(beforeNotices).Any(n=>n.TryGetProperty("method",out var m)&&m.GetString()=="notifications/resources/updated"),"Resource subscription still works with info logs filtered");
await Request(1001, "resources/unsubscribe", new { uri = "bigbluefish://interactions" });
Check(server.Listeners == 0 && server.Connections == 1, "Unsubscribed with warning logs: no longer a listener");
await Request(1002, "logging/setLevel", new { level = "info" });
Check(server.Listeners == 1, "logging/setLevel info makes a listener, as the Hermes bridge does");
// Attachments travel as local paths and metadata only, so the event line stays small however large the files are.
var attachments=new[]{
    new{path=@"C:\Users\主人\AppData\Local\BigBlueFish\attachments\paste-20261026-101500-123-0a1b.png",name="paste-20261026-101500-123-0a1b.png",kind="image",mimeType="image/png",size=8_294_400L,source="clipboard"},
    new{path=@"D:\影片\海邊 & 夕陽.mp4",name="海邊 & 夕陽.mp4",kind="video",mimeType="video/mp4",size=1_073_741_824L,source="drop"},
    new{path=@"C:\Users\主人\Documents\報告.pdf",name="報告.pdf",kind="file",mimeType="application/pdf",size=52_431L,source="picker"}};
server.Publish("user_message",new{text="",attachments});
response=await Request(12,"resources/read",new{uri="bigbluefish://interactions"});
records=JsonDocument.Parse(response.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!).RootElement;
var withFiles=records[records.GetArrayLength()-1].GetProperty("detail");
Check(withFiles.GetProperty("text").GetString()=="","Attachment-only message keeps an empty text field");
Check(withFiles.GetProperty("attachments").EnumerateArray().Select(a=>(a.GetProperty("path").GetString(),a.GetProperty("name").GetString(),a.GetProperty("kind").GetString(),a.GetProperty("mimeType").GetString(),a.GetProperty("size").GetInt64(),a.GetProperty("source").GetString()))
    .SequenceEqual(attachments.Select(a=>((string?)a.path,(string?)a.name,(string?)a.kind,(string?)a.mimeType,a.size,(string?)a.source))),"Attachments round-trip through the interaction resource unchanged");
Check(System.Text.Encoding.UTF8.GetByteCount(records[records.GetArrayLength()-1].GetRawText())<4096,"Attachment event stays far below 64 KB");
Check(!records[records.GetArrayLength()-2].GetProperty("detail").TryGetProperty("attachments",out _),"Plain text user_message has no attachments field");
Check(server.Connections == 1, "Connection tracked");
const string privateLine = "PRIVATE-AGENT-SAY-BODY-923784";
response = await Request(12001, "tools/call", new { name = "say", arguments = new { text = privateLine } });
Check(response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains(privateLine), "MCP tool body reaches its intended receiver");
Check(!DiagnosticLog.ExportText().Contains(privateLine) && DiagnosticLog.Snapshot().Any(x => x.Code == "McpTool" && x.Details.Contains("kind=say")), "MCP diagnostics retain tool metadata without argument or result body");
process.StandardInput.Close();
await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(process.ExitCode == 0, "Bridge exits at stdin EOF");
for (int i = 0; i < 50 && server.Connections != 0; i++) await Task.Delay(20);
Check(server.Connections == 0, "Connection released");
using var second = Start();
await second.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\"}}");
var secondLine = await second.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(secondLine!.Contains("2025-03-26"), "Reconnect and older protocol negotiation");
server.Dispose();
await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(second.HasExited, "Disabling service disconnects bridge");
Console.WriteLine($"MCP protocol: {checks} checks passed.");
