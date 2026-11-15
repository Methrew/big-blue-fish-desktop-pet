using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using BigBlueFish.Setup;
using BigBlueFish.Deployment;
using CheckBox = System.Windows.Controls.CheckBox;

namespace BigBlueFish.Uninstaller;

internal static class UninstallApplication
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? destination, int flags);

    internal static Window CreateUninstallForm(string target)
    {
        var window = new ModernWindow("解除安裝" + BigBlueFish.Windows.CompanionEdition.DisplayName, "和魚魚暫時說再見", "勾選下方選項，可以保留模型和設定，方便以後重新安裝。", 590, 350);
        var retain = new CheckBox { Content = "保留模型和設定", IsChecked = false };
        window.Body.Children.Add(retain);
        window.Actions.Children.Add(ModernUI.Button("取消", () => window.Close()));
        window.Actions.Children.Add(ModernUI.Button("解除安裝", () =>
        {
            try
            {
                var temporary = Path.Combine(Path.GetTempPath(), "BigBlueFish-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, temporary);
                var start = new ProcessStartInfo(temporary) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
                start.ArgumentList.Add("--uninstall-worker"); start.ArgumentList.Add(target);
                if (retain.IsChecked != true) start.ArgumentList.Add("--remove-data");
                Process.Start(start); window.Close();
            }
            catch (Exception error) { ModernUI.Error(window, "解除安裝未完成", error); }
        }, true));
        return window;
    }

    internal static int Run(string[] args)
    {
        ModernUI.EnsureApplication();
        try
        {
            bool worker = args.Length > 0 && args[0] == "--uninstall-worker";
            if (args.Length > 0 && (args.Length < 2 || args[0] is not ("--uninstall" or "--uninstall-worker")))
                throw new ArgumentException("未提供安裝資料夾。");
            var target = InstallationLifecycle.ValidateInstallation(args.Length == 0 ? Path.GetDirectoryName(Environment.ProcessPath!)! : args[1]);
            if (worker)
            {
                InstallationLifecycle.Uninstall(target, args.Contains("--remove-data"));
                MoveFileEx(Environment.ProcessPath!, null, 4);
                ModernUI.Choice(null, BigBlueFish.Windows.CompanionEdition.DisplayName, BigBlueFish.Windows.CompanionEdition.DisplayName + "已解除安裝。", "確定");
                return 0;
            }
            CreateUninstallForm(target).ShowDialog(); return 0;
        }
        catch (Exception error) { ModernUI.Error(null, BigBlueFish.Windows.CompanionEdition.DisplayName + "解除安裝未完成", error); return 1; }
    }
}
