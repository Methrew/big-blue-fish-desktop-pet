// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.IO.Pipes;
using BigBlueFish.Control;

// With --pipe the bridge belongs to the fish's own Agent connection; without arguments it reaches the MCP interface
// that the user switches on for other MCP clients, such as the Hermes bridge.
bool internalPipe = args.Length == 2 && args[0] == "--pipe";
try
{
    var pipeName = internalPipe ? args[1] : ControlTools.PipeName;
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000);
    using var cancel = new CancellationTokenSource();
    var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
    var toPet = input.CopyToAsync(pipe, cancel.Token); var toAgent = pipe.CopyToAsync(output, cancel.Token);
    await Task.WhenAny(toPet, toAgent);
    cancel.Cancel(); pipe.Dispose();
    try { await toAgent; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
}
catch (Exception e)
{
    // Linux has no MCP interface for other clients: there the bridge only serves the Agent connection's private pipe.
    await Console.Error.WriteLineAsync((internalPipe
        ? "藍色大肥魚的內部工具未連線：請在桌寵設定中重新連線 Agent。"
        : OperatingSystem.IsWindows()
            ? "藍色大肥魚的 MCP 介面未連線：請確認桌寵正在執行，並已在設定中開啟「開啟 MCP 介面」。"
            : "藍色大肥魚的 Linux 版沒有給其他 MCP 客戶端用的 MCP 介面：本程式只供桌寵設定裡的 Agent 連線使用，由它以 --pipe 啟動。") + e.Message);
    Environment.ExitCode = 1;
}
