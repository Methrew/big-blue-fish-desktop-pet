// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BigBlueFish.Windows;
public partial class MainWindow
{
    private Action BuildLogConsole(StackPanel panel, Window owner, Func<bool> visible)
    {
        panel.Children.Add(new TextBlock { Text = $"這次執行的疑難排解記錄，保留最近 {DiagnosticLog.Capacity} 筆。記錄操作、狀態、耗時和錯誤碼；不包含截圖、OCR、聊天或提示詞的內容，資料夾路徑已匿名化。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        var follow = new CheckBox { Content = "自動捲動", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var save = new Button { Content = "儲存成 TXT…", Padding = new Thickness(14, 7, 14, 7) };
        toolbar.Children.Add(save); toolbar.Children.Add(follow); panel.Children.Add(toolbar);
        var count = new TextBlock { Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(count);
        var console = new TextBox { Name = "LogConsoleText", IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Microsoft JhengHei UI"), FontSize = 12,
            Height = 410, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(console, "疑難排解記錄"); panel.Children.Add(console);
        long revision = -1;
        void Refresh()
        {
            if (!visible() || revision == DiagnosticLog.Revision) return;
            revision = DiagnosticLog.Revision;
            var snapshot = DiagnosticLog.Snapshot(); var offset = console.VerticalOffset;
            int selection = console.SelectionStart, length = console.SelectionLength;
            console.Text = string.Join(Environment.NewLine, snapshot.Select(x => x.ToString()));
            if (follow.IsChecked == true && length == 0) console.ScrollToEnd();
            else { console.Select(Math.Min(selection, console.Text.Length), Math.Min(length, Math.Max(0, console.Text.Length - selection))); console.ScrollToVerticalOffset(offset); }
            count.Text = $"{snapshot.Length} / {DiagnosticLog.Capacity} 筆 · INFO / WARN · 自動重新整理";
        }
        save.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Title = "儲存疑難排解記錄", DefaultExt = ".txt", Filter = "文字檔 (*.txt)|*.txt", FileName = $"BigBlueFish-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dialog.ShowDialog(owner) != true) return;
            try { DiagnosticLog.SaveText(dialog.FileName); DiagnosticLog.Write(DiagnosticEvent.LogExported, directory: System.IO.Path.GetDirectoryName(dialog.FileName)); }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.LogExportFailed, error: error); BigBlueFish.Deployment.ModernUI.Error(owner, "儲存記錄失敗", error); }
        };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }
}
