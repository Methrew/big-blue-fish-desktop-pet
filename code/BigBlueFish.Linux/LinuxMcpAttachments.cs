// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
#if !COMPANION_SAFE
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BigBlueFish.Agent;
using SkiaSharp;
using Shape = Avalonia.Controls.Shapes.Path;

namespace BigBlueFish.Windows;

// Attachments of the double-click input on macOS and Linux, as the Windows pet has them (CompanionMcpAttachments.cs) and with
// the rules both share (CompanionAttachmentRules.cs): the paperclip, dragging files in, and pasting files or a picture add up
// to ten files. user_message names them by their local paths only; picked, dropped and copied files are never copied, and
// a pasted picture is saved as PNG in the data folder's attachments/.
internal sealed partial class LinuxPetWindow
{
    // While the file panel is open the input box is neither closed nor hidden under it.
    bool mcpInputPicking;
    McpAttachmentTray? mcpTray;
    static string? mcpAttachmentFolder;
    static DateTime mcpAttachmentSweep;
    static readonly IBrush TileBorder = new SolidColorBrush(Color.FromRgb(220, 225, 237)), TileBackground = new SolidColorBrush(Color.FromRgb(238, 241, 248)),
        DropHighlight = new SolidColorBrush(Color.FromRgb(64, 140, 230)), RemoveBackground = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255));
    const string FileGlyph = "M4,1.5 H9.5 L13,5 V14.5 H4 Z M9.5,1.5 V5 H13";
    const string ImageGlyph = "M2,3 H14 V13 H2 Z M2,11 L6,7 L9,10 L11,8 L14,11 M11.5,5.5 A1,1 0 1 0 9.5,5.5 A1,1 0 1 0 11.5,5.5";
    const string VideoGlyph = "M2,4 H11 V12 H2 Z M11,7 L14,5 V11 L11,9";
    const string ClipGlyph = "M11,5 V11.5 A3,3 0 0 1 5,11.5 V4 A2,2 0 0 1 9,4 V11.5 A1,1 0 0 1 7,11.5 V5.5";

    // The data folder (the current directory, set once at startup), never inside the app: updates replace that.
    static string McpAttachmentFolder => mcpAttachmentFolder ??= Path.Combine(Environment.CurrentDirectory, "attachments");
    // The send shortcut as each platform writes it; Ctrl+Enter sends on macOS too.
    static string McpSendHint => OperatingSystem.IsMacOS() ? "⌘↩ 傳送" : "Ctrl+Enter 傳送";
    static bool IsMcpSendKey(KeyEventArgs e) => e.Key == Key.Enter && (e.KeyModifiers == KeyModifiers.Control || OperatingSystem.IsMacOS() && e.KeyModifiers == KeyModifiers.Meta);

    // Files by path into the open input box: drops, the test commands.
    int AddMcpAttachments(IEnumerable<string> paths, string source) => mcpTray?.Add(paths, source) ?? 0;

    // The attachment row of a new input box, added to `panel` under the editor; the paste and drop handlers on the window.
    McpAttachmentTray OpenMcpAttachments(Window dialog, Border frame, Panel panel, TextBox text, TextBlock status, string ready)
    {
        SweepMcpAttachments();
        // One line: the window has fixed heights, so a long notice is shortened and shown whole as its tooltip.
        status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) ToolTip.SetTip(status, status.Text); };
        var tray = new McpAttachmentTray(dialog, notice => status.Text = notice, ready);
        panel.Children.Add(tray.View);
        AcceptMcpPaste(dialog, text, tray);
        AcceptMcpDrops(dialog, frame, text, tray);
        return tray;
    }

    // The paperclip beside the status line. Not focusable, so a click leaves the caret in the editor.
    Button McpAttachButton(Window dialog, TextBox text, McpAttachmentTray tray)
    {
        var clip = new Shape { Data = Geometry.Parse(ClipGlyph), Stroke = Hair, StrokeThickness = 1.3, StrokeLineCap = PenLineCap.Round,
            Width = 16, Height = 16, Stretch = Stretch.None, RenderTransform = new RotateTransform(40) };
        var button = new Button { Content = clip, Width = 28, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
            Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, "附加圖片、影片或檔案（也可以 " + (OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V") + " 貼上或拖曳進來）");
        AutomationProperties.SetName(button, "附加檔案");
        button.Click += async (_, _) => {
            if (mcpInputPicking) return;
            mcpInputPicking = true;
            IReadOnlyList<IStorageFile> picked = [];
            try { picked = await PickMcpAttachments(dialog); }
            catch (Exception error) when (error is not OutOfMemoryException) { tray.Notice("無法開啟選擇檔案的視窗：" + error.Message); }
            finally { mcpInputPicking = false; }
            var paths = picked.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            foreach (var file in picked) file.Dispose();
            if (mcpInput != dialog) return;
            if (paths.Length > 0) tray.Add(paths, "picker");
            dialog.Activate(); text.Focus();
        };
        return button;
    }

    static Task<IReadOnlyList<IStorageFile>> PickMcpAttachments(Window owner)
    {
        static FilePickerFileType Type(string name, params string[] kinds) =>
            new(name) { Patterns = kinds.SelectMany(CompanionAttachmentRules.Extensions).Select(extension => "*" + extension).ToArray() };
        var all = new FilePickerFileType("所有檔案") { Patterns = FilePickerFileTypes.All.Patterns, MimeTypes = FilePickerFileTypes.All.MimeTypes,
            AppleUniformTypeIdentifiers = FilePickerFileTypes.All.AppleUniformTypeIdentifiers };
        var options = new FilePickerOpenOptions { Title = "附加圖片、影片或檔案", AllowMultiple = true,
            FileTypeFilter = [Type("圖片與影片", "image", "video"), Type("圖片", "image"), Type("影片", "video"), all] };
        return OperatingSystem.IsMacOS() && FloatingFilePicker(options) is { } floating ? floating : owner.StorageProvider.OpenFilePickerAsync(options);
    }

    // Avalonia.Native shows the NSOpenPanel as a sheet of the window it is asked from, and a sheet would hang off this small
    // borderless box. Asked without a window, it floats as a panel of its own. Avalonia 12.1 has no public way to ask that,
    // hence the reflection on its internals (AvaloniaLocator → IStorageProviderFactory → OpenFileDialog(null, options)); if
    // they change, null sends the caller back to the sheet.
    static Task<IReadOnlyList<IStorageFile>>? FloatingFilePicker(FilePickerOpenOptions options)
    {
        try
        {
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
            var locator = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator");
            var factoryType = typeof(Window).Assembly.GetType("Avalonia.Controls.Platform.IStorageProviderFactory");
            var current = locator?.GetProperty("Current", any | BindingFlags.Static)?.GetValue(null);
            var factory = factoryType == null ? null
                : current?.GetType().GetMethod("GetService", any | BindingFlags.Instance, [typeof(Type)])?.Invoke(current, [factoryType]);
            var open = factory?.GetType().GetMethod("OpenFileDialog", any | BindingFlags.Instance);
            if (open?.GetParameters() is not [{ ParameterType.IsValueType: false }, { } second] || second.ParameterType != typeof(FilePickerOpenOptions)
                || !typeof(Task).IsAssignableFrom(open.ReturnType) || open.Invoke(factory, [null, options]) is not Task task) return null;
            return Files(task);
        }
        catch (Exception error) when (error is TargetInvocationException or MemberAccessException or ArgumentException or InvalidCastException or NotSupportedException)
        {
            return null;
        }

        static async Task<IReadOnlyList<IStorageFile>> Files(Task task)
        {
            await task;
            // (IReadOnlyList<IStorageFile> files, FilePickerFileType? selectedType)
            var result = task.GetType().GetProperty("Result")?.GetValue(task);
            return result?.GetType().GetField("Item1")?.GetValue(result) as IReadOnlyList<IStorageFile> ?? [];
        }
    }

    // At most every six hours, in the background (CompanionAttachmentRules.Sweep).
    static void SweepMcpAttachments()
    {
        if (DateTime.UtcNow - mcpAttachmentSweep < TimeSpan.FromHours(6)) return;
        mcpAttachmentSweep = DateTime.UtcNow;
        var folder = McpAttachmentFolder;
        _ = Task.Run(() => CompanionAttachmentRules.Sweep(folder));
    }

    // ⌘V or Ctrl+V, Shift+Insert and the editor's own menu all paste through the TextBox: copied files keep their paths, a
    // picture alone becomes a PNG, and anything with text pastes as text.
    static void AcceptMcpPaste(Window dialog, TextBox text, McpAttachmentTray tray)
    {
        bool asText = false;
        text.PastingFromClipboard += (_, e) => {
            if (asText || TopLevel.GetTopLevel(text)?.Clipboard is not { } clipboard) return;
            e.Handled = true;
            _ = tray.PasteAsync(clipboard, () => { asText = true; try { text.Paste(); } finally { asText = false; } });
        };
        // With the focus on a button, the shortcut still pastes into the editor.
        dialog.AddHandler(InputElement.KeyDownEvent, (_, e) => {
            if (text.IsFocused || Application.Current?.PlatformSettings?.HotkeyConfiguration.Paste is not { } gestures || !gestures.Any(g => g.Matches(e))) return;
            e.Handled = true; text.Focus(); text.Paste();
        }, RoutingStrategies.Tunnel);
    }

    // Tunnel handlers, so the editor does not get the drag first.
    static void AcceptMcpDrops(Window dialog, Border frame, TextBox text, McpAttachmentTray tray)
    {
        var normal = frame.BorderBrush;
        DragDrop.SetAllowDrop(dialog, true);
        void Over(object? sender, DragEventArgs e)
        {
            if (!e.DataTransfer.Contains(DataFormat.File)) return;
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; frame.BorderBrush = DropHighlight;
        }
        dialog.AddHandler(DragDrop.DragEnterEvent, Over, RoutingStrategies.Tunnel);
        dialog.AddHandler(DragDrop.DragOverEvent, Over, RoutingStrategies.Tunnel);
        dialog.AddHandler(DragDrop.DragLeaveEvent, (_, _) => frame.BorderBrush = normal, RoutingStrategies.Tunnel);
        dialog.AddHandler(DragDrop.DropEvent, (_, e) => {
            frame.BorderBrush = normal;
            var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths is not { Length: > 0 }) return;
            e.DragEffects = DragDropEffects.Copy; e.Handled = true;
            // The file checks run after the drop returns, so the Finder is not kept waiting.
            Dispatcher.UIThread.Post(() => { tray.Add(paths, "drop"); dialog.Activate(); text.Focus(); });
        }, RoutingStrategies.Tunnel);
    }

    // AgentCoordinator.AttachmentImage: an upright picture no longer than AgentUserAttachment.ImageEdge. A small upright PNG,
    // JPEG, GIF or WebP goes as it is; anything else becomes a JPEG (a PNG while it has transparency and fits). HEIC and TIFF
    // are read through sips on macOS. Null leaves the Agent the link alone.
    static Task<AgentPromptImage?> AttachmentPromptImage(AgentUserAttachment file, CancellationToken token) => Task.Run(() => {
        token.ThrowIfCancellationRequested();
        if (FitsAsItIs(file.Path)) return AgentUserAttachment.ReadAsIs(file.Path);
        using var bitmap = DecodeAttachmentImage(file.Path, AgentUserAttachment.ImageEdge, false);
        if (bitmap == null) return null;
        token.ThrowIfCancellationRequested();
        if (bitmap.AlphaType != SKAlphaType.Opaque)
        {
            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            if (png != null && png.Size <= AgentUserAttachment.ImageBytes) return new AgentPromptImage("image/png", Convert.ToBase64String(png.AsSpan()));
        }
        // JPEG has no transparency: transparent parts become white rather than black.
        using var flat = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(flat)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(bitmap, 0, 0); }
        using var flatImage = SKImage.FromBitmap(flat);
        foreach (int quality in new[] { 85, 70, 55 })
        {
            using var jpeg = flatImage.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (jpeg != null && jpeg.Size <= AgentUserAttachment.ImageBytes) return new AgentPromptImage("image/jpeg", Convert.ToBase64String(jpeg.AsSpan()));
        }
        return null;
    }, token);

    static bool FitsAsItIs(string path)
    {
        try
        {
            var head = new byte[12];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                if (stream.Length > AgentUserAttachment.ImageBytes || stream.Read(head) < 4 || AgentUserAttachment.ImageMime(head) == null) return false;
            using var codec = SKCodec.Create(path);
            return codec != null && Math.Max(codec.Info.Width, codec.Info.Height) <= AgentUserAttachment.ImageEdge && codec.EncodedOrigin == SKEncodedOrigin.TopLeft;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    // An attached picture, upright (its EXIF orientation applied) and scaled down until its shorter side (cover, for the tiles)
    // or its longer side fits `edge`. Types Skia cannot read, such as HEIC, go through sips on macOS first. Null when it is
    // not a picture this computer can read.
    static SKBitmap? DecodeAttachmentImage(string path, int edge, bool cover)
    {
        var decoded = DecodeWithSkia(path, edge, cover);
        if (decoded != null || !OperatingSystem.IsMacOS() || !File.Exists(Sips)) return decoded;
        DirectoryInfo? folder = null;
        try
        {
            // A folder only this user can read (0700), removed whatever happens.
            folder = Directory.CreateTempSubdirectory("bbf-attachment");
            string output = Path.Combine(folder.FullName, "picture.jpg");
            int bound = cover ? edge * 4 : edge;
            return RunSips(["-s", "format", "jpeg", "-s", "formatOptions", "90", "-Z", bound.ToString(CultureInfo.InvariantCulture), path, "--out", output])
                && File.Exists(output) ? DecodeWithSkia(output, edge, cover) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception) { return null; }
        finally
        {
            try { folder?.Delete(true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    static SKBitmap? DecodeWithSkia(string path, int edge, bool cover)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 150_000_000) return null;
        float scale = Math.Min(1f, edge / (float)(cover ? Math.Min(info.Width, info.Height) : Math.Max(info.Width, info.Height)));
        int width = Math.Max(1, (int)Math.Round(info.Width * scale)), height = Math.Max(1, (int)Math.Round(info.Height * scale));
        // JPEG decodes straight at a fraction of its size; the other formats come whole and are resized below.
        var size = codec.GetScaledDimensions(scale);
        if (size.Width < width || size.Height < height) size = info.Size;
        var target = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        var bitmap = new SKBitmap(target);
        if (codec.GetPixels(target, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) { bitmap.Dispose(); return null; }
        if (bitmap.Width > width || bitmap.Height > height)
        {
            var resized = bitmap.Resize(new SKImageInfo(width, height, target.ColorType, target.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
            bitmap.Dispose();
            if (resized == null) return null;
            bitmap = resized;
        }
        return Orient(bitmap, codec.EncodedOrigin);
    }

    // Turns the decoded pixels the way the camera meant them (EXIF orientation 1–8); takes ownership of `source`.
    static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source;
        bool swap = origin >= SKEncodedOrigin.LeftTop;
        var result = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType));
        int w = result.Width, h = result.Height;
        using (var canvas = new SKCanvas(result))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(w, h); canvas.RotateDegrees(270); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(source, 0, 0);
        }
        source.Dispose();
        return result;
    }

    const string Sips = "/usr/bin/sips";
    // No shell: every argument goes as it is. False when sips failed or took longer than 30 seconds.
    static bool RunSips(string[] arguments)
    {
        var start = new ProcessStartInfo(Sips) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process == null) return false;
        _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { try { process.Kill(true); } catch (InvalidOperationException) { } return false; }
        return process.ExitCode == 0;
    }

    sealed class McpAttachment(string path, string kind, string mimeType, long size, string source, bool owned)
    {
        public readonly string Path = path, Name = System.IO.Path.GetFileName(path), Kind = kind, MimeType = mimeType, Source = source;
        public readonly bool Owned = owned; // a pasted picture in the attachments folder, deleted if it is never sent
        public long Size = size;
        public Avalonia.Controls.Control? Tile;
    }

    sealed class McpAttachmentTray
    {
        internal readonly List<McpAttachment> Items = [];
        internal readonly ScrollViewer View;
        internal Action? Changed;
        internal int Saving { get; private set; }
        readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        readonly Window owner;
        readonly Action<string> notice;
        readonly string ready;
        bool closed;

        internal McpAttachmentTray(Window owner, Action<string> notice, string ready)
        {
            this.owner = owner; this.notice = notice; this.ready = ready;
            // A scroll bar would not fit under the 52-point tiles; the wheel scrolls the row sideways instead.
            View = new ScrollViewer { Content = strip, Height = 56, Margin = new Thickness(0, 2, 0, 0), IsVisible = false, Focusable = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            View.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => {
                double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
                double end = Math.Max(0, View.Extent.Width - View.Viewport.Width);
                View.Offset = View.Offset.WithX(Math.Clamp(View.Offset.X - delta * 40, 0, end)); e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        internal void Notice(string text) { if (!closed) notice(text); }

        // Picked, dropped and copied files are referenced by their original paths and never copied.
        internal int Add(IEnumerable<string> paths, string source, bool owned = false)
        {
            int added = 0; string? problem = null;
            foreach (var raw in paths)
            {
                if (closed) break;
                string path;
                try { path = System.IO.Path.GetFullPath(raw); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { problem = "無法辨識的檔案路徑"; continue; }
                var name = System.IO.Path.GetFileName(path);
                if (Directory.Exists(path)) { problem = "不支援資料夾：" + name; continue; }
                // The Agent reads the paths as lines of text; a control character in one could forge a line.
                if (path.Any(char.IsControl)) { problem = "檔名含有控制字元，無法附加：" + name.Trim(); continue; }
                if (Items.Any(i => string.Equals(i.Path, path, CompanionAttachmentRules.PathComparison))) { problem = "已經附加過：" + name; continue; }
                if (Items.Count >= CompanionAttachmentRules.Limit) { problem = $"最多只能附加 {CompanionAttachmentRules.Limit} 個檔案"; break; }
                long size;
                try { size = new FileInfo(path).Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { problem = "找不到或無法讀取：" + name; continue; }
                var (kind, mime) = CompanionAttachmentRules.Type(path);
                var item = new McpAttachment(path, kind, mime, size, source, owned);
                Items.Add(item); strip.Children.Add(item.Tile = Tile(item)); added++;
                if (kind == "image" && size > CompanionAttachmentRules.LargeImage) problem = "圖片超過 20 MB，Agent 可能無法直接檢視：" + name;
            }
            if (added > 0)
            {
                View.IsVisible = true; Changed?.Invoke();
                Dispatcher.UIThread.Post(() => View.Offset = View.Offset.WithX(Math.Max(0, View.Extent.Width - View.Viewport.Width)), DispatcherPriority.Background);
            }
            if (problem != null || added > 0) Notice(problem ?? $"已附加 {Items.Count} 個檔案");
            return added;
        }

        internal void Remove(McpAttachment item)
        {
            if (!Items.Remove(item)) return;
            if (item.Tile != null) strip.Children.Remove(item.Tile);
            if (item.Owned) Delete(item.Path);
            if (Items.Count == 0) View.IsVisible = false;
            Notice(Items.Count == 0 ? ready : $"已附加 {Items.Count} 個檔案");
            Changed?.Invoke();
        }

        // The user_message entries. Files that disappeared since they were added are left out; nothing is removed, so a
        // refused message keeps its files.
        internal AgentUserAttachment[] Peek(out int missing)
        {
            var sent = new List<AgentUserAttachment>(); missing = 0;
            foreach (var item in Items)
            {
                try { item.Size = new FileInfo(item.Path).Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { missing++; continue; }
                sent.Add(new(item.Path, item.Name, item.Kind, item.MimeType, item.Size, item.Source));
            }
            return [.. sent];
        }

        // After sending: pasted pictures stay on disk for the agent until the sweep.
        internal void Clear()
        {
            Items.Clear(); strip.Children.Clear(); View.IsVisible = false; Changed?.Invoke();
        }

        // The input box closed: unsent pasted pictures belong to this draft only.
        internal void Discard()
        {
            closed = true;
            foreach (var item in Items) if (item.Owned) Delete(item.Path);
            Items.Clear();
        }

        // Copied files keep their paths (the Finder also offers the file name as text and its icon as a picture); a picture
        // alone becomes a PNG; anything else with text pastes as text, through `pasteText`.
        internal async Task PasteAsync(IClipboard clipboard, Action pasteText)
        {
            try
            {
                var files = await clipboard.TryGetFilesAsync();
                var paths = files?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
                foreach (var file in files ?? []) file.Dispose();
                if (closed) return;
                if (paths.Length > 0) { Add(paths, "clipboard-files"); return; }
                if (!string.IsNullOrEmpty(await clipboard.TryGetTextAsync())) { if (!closed) pasteText(); return; }
                using var bitmap = await clipboard.TryGetBitmapAsync();
                if (bitmap == null || closed) return;
                if (Items.Count >= CompanionAttachmentRules.Limit) { Notice($"最多只能附加 {CompanionAttachmentRules.Limit} 個檔案"); return; }
                await SavePaste(bitmap);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { Notice("無法讀取剪貼簿的內容，請再試一次"); }
        }

        // Encoding a 5K screenshot takes a few hundred milliseconds, too long for the animation's UI thread.
        async Task SavePaste(Bitmap bitmap)
        {
            Saving++; Changed?.Invoke(); Notice("正在儲存貼上的圖片…");
            string? path = null;
            try { path = await Task.Run(() => WritePaste(bitmap)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException) { Notice("無法儲存貼上的圖片：" + e.Message); }
            finally { Saving--; if (!closed) Changed?.Invoke(); } // the input may have closed while the file was written
            if (path != null && (closed || Add([path], "clipboard", true) == 0)) Delete(path);
        }

        static string WritePaste(Bitmap bitmap)
        {
            var folder = McpAttachmentFolder; Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, CompanionAttachmentRules.PasteName(DateTime.Now));
            // The bridge may open the file as soon as the event arrives, so only a complete file gets the final name.
            using (var file = File.Create(path + ".tmp")) bitmap.Save(file, PngBitmapEncoderOptions.Default);
            File.Move(path + ".tmp", path);
            return path;
        }

        static void Delete(string path)
        {
            if (!CompanionAttachmentRules.Owns(McpAttachmentFolder, path)) return;
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        Avalonia.Controls.Control Tile(McpAttachment item)
        {
            var glyph = new Shape { Data = Geometry.Parse(item.Kind switch { "image" => ImageGlyph, "video" => VideoGlyph, _ => FileGlyph }), Stroke = Hair,
                StrokeThickness = 1.2, StrokeJoin = PenLineJoin.Round, Width = 16, Height = 16, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = item.Name, FontSize = 10, Foreground = Brushes.SlateGray, TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(3, 3, 3, 0) };
            var placeholder = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { glyph, label } };
            var picture = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
            var remove = new Button { Content = "×", Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 2, 2, 0),
                FontSize = 11, CornerRadius = new CornerRadius(8), Focusable = false, BorderThickness = new Thickness(0), Background = RemoveBackground, Foreground = Hair,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(remove, "移除附件");
            AutomationProperties.SetName(remove, "移除附件 " + item.Name);
            remove.Click += (_, _) => Remove(item);
            var tile = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = TileBorder,
                Background = TileBackground, ClipToBounds = true, Child = new Grid { Children = { placeholder, picture, remove } } };
            ToolTip.SetTip(tile, item.Path + "\n" + CompanionAttachmentRules.FormatSize(item.Size));
            AutomationProperties.SetName(tile, item.Name);
            if (item.Kind == "image") _ = ShowThumbnail(item, picture, placeholder);
            return tile;
        }

        async Task ShowThumbnail(McpAttachment item, Image picture, Avalonia.Controls.Control placeholder)
        {
            int pixels = (int)Math.Ceiling(52 * owner.RenderScaling);
            Bitmap? image = null;
            try { image = await Task.Run(() => Thumbnail(item.Path, pixels)); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
            // HEIC without sips, or a file that is not really a picture, keeps the generic icon.
            if (image == null || !Items.Contains(item)) return;
            picture.Source = image; picture.IsVisible = true; placeholder.IsVisible = false;
        }

        static Bitmap? Thumbnail(string path, int pixels)
        {
            using var bitmap = DecodeAttachmentImage(path, pixels, true);
            if (bitmap == null) return null;
            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = png.AsStream();
            return new Bitmap(stream);
        }
    }
}
#endif
