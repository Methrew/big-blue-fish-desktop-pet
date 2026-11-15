// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
#if !COMPANION_SAFE
using BigBlueFish.Control;
#endif

namespace BigBlueFish.Windows;

// The MCP interface for programs outside the fish on macOS and Linux, as the Windows pet has it (CompanionMcp.cs): BigBlueFish.Mcp
// without arguments reaches it on the fixed per-user pipe (ControlTools.PipeName, a short /tmp socket on macOS), for clients such
// as the Hermes bridge. It runs next to the Agent connection's private pipe (LinuxMcp.cs), off until the user switches it on.
// Typed text goes to exactly one of them; every other event goes to both.
internal sealed partial class LinuxPetWindow
{
    // When an MCP client last said something. An agent's bubble is not talked over by the fish's own speech: while it is typed
    // out, while it has a deadline of its own, and for a while after the agent last refreshed a bubble kept until the next
    // sentence (the Hermes bridge refreshes its progress bubble at least every 15 seconds).
#if COMPANION_SAFE
    static bool ExternalSpeechHeld(DateTime now) => false;
#else
    DateTime externalSpeechAt;
    static readonly TimeSpan ExternalSpeechHold = TimeSpan.FromSeconds(20);
    bool ExternalSpeechHeld(DateTime now) => externalSpeech && bubbleWindow.IsVisible
        && (typing || bubbleUntil != DateTime.MaxValue || now - externalSpeechAt < ExternalSpeechHold);
    ControlServer? mcpInterface;
    // Tools of the shared list this edition does not have yet: left out of tools/list and refused (ControlTools.Unavailable),
    // so neither the Agent connection nor an MCP client keeps calling them. A port of one of them takes its name out here.
    // get_context runs on both; walk_to and find_on_screen need the macOS screen reader (ControlTools.Definitions also leaves them out on Linux).
    static readonly string[] MissingMcpTools = OperatingSystem.IsMacOS() ? [] : ["walk_to", "find_on_screen"];
    // agentChannel: a call through the Agent connection's private pipe rather than the MCP interface.
    bool McpChannelOpen(bool agentChannel) => agentChannel ? Preferences.AgentEnabled : Preferences.McpEnabled;
    bool McpChatOn => Preferences.McpEnabled && Preferences.McpChatEnabled;
    // Typed text goes to the MCP interface while the user hands the input to it and a client there listens for the events
    // (the Hermes bridge); otherwise to the Agent connection. A client that only calls tools, such as Hermes' own
    // big_blue_fish MCP session, still connected after the bridge stopped, never takes the input.
    bool McpChatAvailable => McpChatOn && (mcpInterface?.Listeners ?? 0) > 0;
    bool McpInputEnabled => Preferences.AgentEnabled || McpChatOn;
    int McpConnections => (mcpServer?.Connections ?? 0) + (mcpInterface?.Connections ?? 0);

    // Starts or stops the fixed pipe to match the switch: from InitializeMcp, StopMcp and the settings page.
    void RefreshMcpInterface()
    {
        ControlTools.Unavailable.UnionWith(MissingMcpTools);
        if (Preferences.McpEnabled && !closed) mcpInterface ??= new ControlServer((name, args, token) => ExecuteMcp(name, args, token, false), ControlTools.PipeName);
        else { mcpInterface?.Dispose(); mcpInterface = null; }
    }

    // What the user typed, to one side only: both answering it would mean two replies and two agents at work.
    void PublishUserMessage(object detail, bool toInterface)
    {
        DiagnosticLog.Write(DiagnosticEvent.Interaction, kind: "user_message");
        if (toInterface) mcpInterface?.Publish("user_message", detail);
        else mcpServer?.Publish("user_message", detail);
    }

    // MCP say. The Agent connection's turn counts as spoken only when the Agent itself speaks, not an MCP client meanwhile.
    void SayFromMcp(string text, bool agentChannel, bool transient)
    {
        if (agentChannel) agent?.MarkSpoken();
        SayExternal(text);
        externalSpeechAt = DateTime.UtcNow;
        if (!transient) LogExternalSpeech(text);
    }

    // The chat history of the input box, once the macOS and Linux input box keeps one (CompanionChatLog on Windows):
    // progress lines (say with transient=true) stay out of it.
    partial void LogExternalSpeech(string text);

    // The Hermes server entry: Hermes names the pet's tools mcp__big_blue_fish__*, which the bridge's instructions rely on.
    static string HermesMcpConfig()
    {
        var command = System.Text.Json.JsonSerializer.Serialize(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp"),
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return "mcp_servers:\n  big_blue_fish:\n    command: " + command + "\n    args: []\n";
    }
#endif
}
