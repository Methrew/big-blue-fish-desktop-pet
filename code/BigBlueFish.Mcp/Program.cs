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
    // The MCP interface for other clients is the pet's fixed per-user pipe on every platform (a /tmp socket on macOS),
    // there only while the user has it switched on.
    await Console.Error.WriteLineAsync((internalPipe
        ? "藍色大肥魚的內部工具未連線：請在桌寵設定中重新連線 Agent。"
        : OperatingSystem.IsWindows()
            ? "藍色大肥魚的 MCP 介面未連線：請確認桌寵正在執行，並已在設定中開啟「開啟 MCP 介面」。"
            : "藍色大肥魚的 MCP 介面未連線：請確認桌寵正在執行，並已在設定的「連線 Agent」頁開啟「開啟 MCP 介面」。") + e.Message);
    Environment.ExitCode = 1;
}
