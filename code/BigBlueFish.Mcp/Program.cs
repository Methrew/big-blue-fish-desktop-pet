// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
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
    var message = (internalPipe
        ? "藍色大肥魚的內部工具未連線：請在桌寵設定中重新連線 Agent。"
        : OperatingSystem.IsWindows()
            ? "藍色大肥魚的 MCP 介面未連線：請確認桌寵正在執行，並已在設定中開啟「開啟 MCP 介面」。"
            : "藍色大肥魚的 MCP 介面未連線：請確認桌寵正在執行，並已在設定的「連線 Agent」頁開啟「開啟 MCP 介面」。")
        // The system's own text stays apart from the explanation.
        + (e is TimeoutException ? "（5 秒內沒有回應）" : "\n（" + e.Message + "）");
    // MCP clients read a redirected stderr as UTF-8, while .NET on Windows writes it in the system's code page (950 on a
    // Taiwan Windows 11, 2026-11-22: garbled in a UTF-8 reader, question marks on other systems). A console window keeps Console.Error, which
    // writes Unicode there; Console.OutputEncoding is left alone, since it would change the code page of that console.
    if (Console.IsErrorRedirected)
    {
        using var error = Console.OpenStandardError();
        await error.WriteAsync(new System.Text.UTF8Encoding(false).GetBytes(message + Environment.NewLine));
    }
    else await Console.Error.WriteLineAsync(message);
    Environment.ExitCode = 1;
}
