// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace BigBlueFish.Windows;
internal sealed partial class LinuxPetWindow
{
    Action BuildLogConsole(StackPanel panel)
    {
        panel.Children.Add(Paragraph($"這次執行的疑難排解記錄，保留最近 {DiagnosticLog.Capacity} 筆。記錄操作、狀態、耗時和錯誤碼；不包含截圖、OCR、聊天或提示詞的內容，資料夾路徑已匿名化。"));
        var follow = new CheckBox { Content = "自動捲動", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        toolbar.Children.Add(ActionButton("儲存成 TXT…", async () => {
            if (settings == null) return;
            try
            {
                var file = await settings.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "儲存疑難排解記錄", SuggestedFileName = $"BigBlueFish-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt", DefaultExtension = "txt", FileTypeChoices = [new FilePickerFileType("文字檔") { Patterns = ["*.txt"] }] });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true)); await writer.WriteAsync(DiagnosticLog.ExportText()); await writer.FlushAsync();
                DiagnosticLog.Write(DiagnosticEvent.LogExported);
            }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.LogExportFailed, error: error); }
        }));
        toolbar.Children.Add(follow); panel.Children.Add(toolbar);
        var count = Paragraph(""); panel.Children.Add(count);
        var console = new TextBox { Name = "LogConsoleText", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = LinuxFonts.Log, FontSize = 12, Height = 400 };
        ScrollViewer.SetVerticalScrollBarVisibility(console, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(console, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto); panel.Children.Add(console);
        long revision = -1;
        var shownIds = new Dictionary<string, string>(StringComparer.Ordinal);
        void Refresh()
        {
            if (revision == DiagnosticLog.Revision) return;
            revision = DiagnosticLog.Revision; var snapshot = DiagnosticLog.Snapshot();
            // Animation ids are the clips' Simplified file names: shown in Traditional here, kept as they are in the saved TXT.
            console.Text = string.Join(Environment.NewLine, snapshot.Select(x => ActionId().Replace(x.ToString(),
                m => "action=" + (shownIds.TryGetValue(m.Groups[1].Value, out var shown) ? shown : shownIds[m.Groups[1].Value] = CompanionTraditional.Convert(m.Groups[1].Value)))));
            // The caret at the start of the last line: the box scrolls to the bottom but stays at the first column, where
            // each line's time, level and event are (the end of a long last line would scroll it sideways).
            if (follow.IsChecked == true) console.CaretIndex = console.Text.LastIndexOf('\n') + 1;
            count.Text = $"{snapshot.Length} / {DiagnosticLog.Capacity} 筆 · INFO / WARN · 自動重新整理";
        }
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"action=([^;]+)")]
    private static partial System.Text.RegularExpressions.Regex ActionId();
}
