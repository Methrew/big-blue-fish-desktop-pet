// Modified by Methrew on 2026-11-21 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.5, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BigBlueFish.Windows;

internal static partial class Program
{
    // The Taiwan edition's battery pause: StopModelOnBattery pauses the model on this computer (a remote or cloud model
    // keeps talking) and the Agent the fish started, with its channel's tools that would make the fish speak or act. The
    // MCP interface for other clients (the Hermes bridge) is not paused, and the double-click input stays available.
    static void VerifyAiPower(MainWindow window)
    {
        var native = typeof(App).Assembly.GetType("BigBlueFish.Windows.CompanionNative")!;
        var power = native.GetField("TestPowerOverride", Access)!;
        var preferences = Read(window, "preferences");
        var timer = Read(window, "companionTimer"); Invoke(timer, "Stop");
        Task<object> Mcp(string tool, string json, bool agentChannel)
        {
            using var args = JsonDocument.Parse(json);
            return (Task<object>)Invoke(window, "ExecuteMcp", tool, args.RootElement.Clone(), CancellationToken.None, agentChannel)!;
        }
        try
        {
            Set(preferences, "StopModelOnBattery", true);
            Set(preferences, "AgentEnabled", true); Set(preferences, "AgentDailySpeech", true);
            power.SetValue(null, false); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(Property<bool>(preferences, "AiPausedOnBattery"), "battery policy pauses the Agent the fish started");
            Check(Property<bool>(window, "McpInputAvailable") && Property<bool>(window, "AgentInputPaused"),
                "battery pause keeps the double-click input, with sending to the paused Agent held");
            foreach (string method in new[] { "SpeakCompanionTest", "SpeakCompanionWeather" })
            {
                var task = (Task)Invoke(window, method)!;
                Wait(() => task.IsCompleted, 3, "manual battery request finishes without Agent work"); task.GetAwaiter().GetResult();
                var speech = Read(window, "speech");
                Check(Property<string>(speech, "FullText").Contains("Agent 已依供電設定暫停"), "manual Agent test/weather explains the Agent's battery pause");
                Check(!Property<bool>(Read(window, "brain"), "Busy"), "manual Agent request does not start local inference");
            }
            var stale = Mcp("say", "{\"text\":\"stale Agent reply\"}", true);
            Wait(() => stale.IsCompleted, 3, "stale tool request returns during power pause");
            Check(stale.IsFaulted && stale.Exception!.GetBaseException() is InvalidOperationException, "late Agent reply cannot bypass battery pause");
            var read = Mcp("get_settings", "{}", true);
            Wait(() => read.IsCompleted, 3, "read-only Agent tool returns during power pause");
            Check(read.Status == TaskStatus.RanToCompletion, "the Agent's channel still reads settings during the battery pause");
            Set(preferences, "McpEnabled", true);
            var outside = Mcp("say", "{\"text\":\"MCP 介面照常說話\"}", false);
            Wait(() => outside.IsCompleted, 3, "MCP interface say returns during power pause");
            Check(outside.Status == TaskStatus.RanToCompletion && Property<string>(Read(window, "speech"), "FullText").Contains("照常說話"),
                "the MCP interface is not paused on battery");
            Set(preferences, "McpEnabled", false);
            Check(Property<bool>(preferences, "AgentEnabled") && Property<bool>(preferences, "AgentDailySpeech"), "power pause preserves user Agent choices");
            // Without the Agent writing the care, the fish's own model answers the speech test with its own notice.
            Set(preferences, "AgentDailySpeech", false); Set(preferences, "ModelEnabled", true);
            var local = (Task)Invoke(window, "SpeakCompanionTest")!;
            Wait(() => local.IsCompleted, 3, "local model battery request finishes"); local.GetAwaiter().GetResult();
            Check(Property<string>(Read(window, "speech"), "FullText").Contains("暫停本機模型"), "the local model keeps its own battery notice");
            Set(preferences, "ModelEnabled", false); Set(preferences, "AgentDailySpeech", true);
            power.SetValue(null, true); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(!Property<bool>(preferences, "AiPausedOnBattery") && Property<bool>(window, "McpInputAvailable") && !Property<bool>(window, "AgentInputPaused"),
                "plugging back in restores sending to the Agent");
            power.SetValue(null, false); Set(preferences, "StopModelOnBattery", false); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(!Property<bool>(preferences, "AiPausedOnBattery") && Property<bool>(window, "McpInputAvailable") && !Property<bool>(window, "AgentInputPaused"),
                "disabling the power switch permits the Agent on battery");
        }
        finally
        {
            power.SetValue(null, null);
            Set(preferences, "AgentEnabled", false); Set(preferences, "AgentDailySpeech", false);
            Set(preferences, "McpEnabled", false); Set(preferences, "ModelEnabled", false);
        }
        Console.WriteLine($"AI power: {checks} actual WPF/power-gate checks passed.");
    }
}
