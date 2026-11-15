// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace BigBlueFish.Windows;

public partial class App : Application
{
    public static string[] Args { get; set; } = [];

    public App()
    {
#if !DEBUG
        DispatcherUnhandledException += (_, e) => { e.Handled = true; RecordException(e.Exception, false); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordException((Exception)e.ExceptionObject, true);
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try { Environment.CurrentDirectory = BigBlueFish.Deployment.ApplicationData.Prepare(AppContext.BaseDirectory); }
        catch (Exception error)
        {
            MessageBox.Show("無法準備大肥魚的資料夾：" + error.Message, "藍色大肥魚", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1); return;
        }
        // In-process verifications set --test-mode before WPF raises Startup with the test program's own
        // arguments; keep it, or test files such as pasted screenshots would land in the user's real folders.
        Args = Args.Contains("--test-mode") && !e.Args.Contains("--test-mode") ? [.. e.Args, "--test-mode"] : e.Args;
        base.OnStartup(e);
    }

    private void RecordException(Exception exception, bool fatal)
    {
        try { DiagnosticLog.Write(DiagnosticEvent.AppFailed, state: fatal ? "exit" : "failed", error: exception); }
        catch { }
        if (fatal) Shutdown(1);
    }
}
