// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    DateTime nextTestStatus;
    async Task TestCommands()
    {
        if (!App.Args.Contains("--test-mode")) return;
        if (DateTime.UtcNow >= nextTestStatus) {
            nextTestStatus = DateTime.UtcNow.AddSeconds(1);
            File.WriteAllText("test-status.json", JsonSerializer.Serialize(new { version = typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3), currentAction, phase, frames = player.Frames, animationSeconds=player.PositionSeconds, animationBackend=player.Backend, player.LastReadMs, player.LastUiMs, player.LastCopyMs, player.LastShapeMs, text = bubbleText.Text, bubbleVisible = bubbleWindow.IsVisible, Puppet, McpAvailable, agentPipe = TestAgentPipe, visible = IsVisible, hidden, fullscreenHidden, modelPid = brain.RunnerPid, modelBusy = brain.Busy, modelStatus = brain.Status, generating, settingsOpen = settings != null, Position.X, Position.Y }));
        }
        if (!File.Exists("test-command.txt")) return;
        string command = File.ReadAllText("test-command.txt").Trim(); File.Delete("test-command.txt");
        // <verb> [<argument>]; a render takes an optional .png name in the data folder after its verb.
        int space = command.IndexOf(' '); string verb = space < 0 ? command : command[..space], argument = space < 0 ? "" : command[(space + 1)..].Trim();
        string Png(string fallback) => argument.Length > 0 ? argument : fallback;
        try {
            switch (verb)
            {
                case "settings": OpenSettings(); break;
                case "close-settings": settings?.Close(); break;
                case "speech-test": await Speak(false); break;
                case "weather": await Speak(true); break;
                case "hide": hidden = true; Hide(); bubbleWindow.Hide(); break;
                case "show": RestorePet(); break;
                case "close": Close(); break;
                case "exit": CloseImmediately(); break;
                case "ocr":
                    var ocr = await CompanionOcr.ReadAsync(CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground), CancellationToken.None);
                    File.WriteAllText("ocr-status.json", JsonSerializer.Serialize(ocr)); break;
                case "render": RenderControl(this, Png("pet.png")); break;
                case "render-settings": RenderControl(settings ?? throw new InvalidOperationException("the settings are not open"), Png("settings.png")); break;
                // The current settings page from top to bottom, however far it scrolls (the navigation column left out).
                case "render-settings-full": RenderSettingsPage(Png("settings-full.png")); break;
                // A page by its navigation label (桌寵與顯示, 模型與資訊, 動作與作息, 連線 Agent, 關於與更新, 記錄主控台); opens the settings.
                case "settings-page": OpenSettings(); Click(argument, settings!); break;
                case "settings-size": // <width>x<height>
                    var wh = argument.Split('x'); OpenSettings(); settings!.Width = double.Parse(wh[0], CultureInfo.InvariantCulture); settings.Height = double.Parse(wh[1], CultureInfo.InvariantCulture); break;
                case "settings-scroll": // a vertical offset, or end
                    var scroll = SettingsScroll();
                    if (argument == "end") scroll.ScrollToEnd(); else scroll.Offset = new Vector(0, double.Parse(argument, CultureInfo.InvariantCulture)); break;
                // Every open window of the app (dialogs, the bubble, the model installer, confirmations), newest last.
                case "list-windows":
                    File.WriteAllText("windows.json", JsonSerializer.Serialize(OpenWindows().Reverse().Select(w => new {
                        title = w.Title, type = w.GetType().Name, width = w.Bounds.Width, height = w.Bounds.Height, visible = w.IsVisible,
                        active = w.IsActive, x = w.Position.X, y = w.Position.Y, owner = (w.Owner as Window)?.Title }), Readable)); break;
                // render-window <part of the title> <file.png>: the newest visible window whose title contains it.
                case "render-window": var (shown, png) = TitleAndFile(argument); RenderControl(FindWindow(shown), png); break;
                // texts <part of the title> <file.json>: the texts the window shows, in order, without OCR.
                case "texts":
                    var (title, json) = TitleAndFile(argument);
                    if (Path.GetFileName(json) != json || !json.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("a .json file name in the data folder expected: " + json);
                    File.WriteAllText(json, JsonSerializer.Serialize(FindWindow(title).GetLogicalDescendants().OfType<Avalonia.Controls.Control>().Where(c => c.IsEffectivelyVisible)
                        .Select(c => new { kind = c.GetType().Name, text = c switch { TextBlock b => b.Parent is ContentControl { Content: string } ? null : b.Text, ContentControl { Content: string s } => s,
                            TextBox b => string.IsNullOrEmpty(b.Text) ? b.PlaceholderText : b.Text, ComboBox b => b.SelectedItem?.ToString(), _ => null } })
                        .Where(c => !string.IsNullOrEmpty(c.text)), Readable)); break;
                case "close-window": FindWindow(argument).Close(); break;
                case "render-bubble": RenderControl(bubbleWindow, Png("bubble.png")); break;
                case "open-menu": OpenMenu(); break;
                case "render-menu": RenderControl(ContextMenu is { IsOpen: true } menu ? menu : throw new InvalidOperationException("the menu is not open"), Png("menu.png")); break;
                // The button or check box with this text in the newest window that has one, as a click would.
                case "click": Click(argument); break;
                // select <shown item>=<item>: the combo box now showing the first item switches to the second.
                case "select":
                    var (from, item) = Pair(argument);
                    var combo = OpenWindows().Where(w => w.IsVisible).SelectMany(w => w.GetLogicalDescendants().OfType<ComboBox>())
                        .FirstOrDefault(c => c.IsEffectivelyVisible && c.SelectedItem?.ToString() == from) ?? throw new InvalidOperationException("no combo box shows " + from);
                    int index = Enumerable.Range(0, combo.ItemCount).FirstOrDefault(i => combo.Items[i]?.ToString() == item, -1);
                    combo.SelectedIndex = index >= 0 ? index : throw new InvalidOperationException("no item " + item); break;
                // type <placeholder, name or text of the box>=<text>: test values only; a secret saved from here goes to the login keychain.
                case "type":
                    var (field, value) = Pair(argument);
                    (OpenWindows().Where(w => w.IsVisible).SelectMany(w => w.GetLogicalDescendants().OfType<TextBox>()).FirstOrDefault(t => t.IsEffectivelyVisible
                        && (t.PlaceholderText?.Contains(field) == true || Avalonia.Automation.AutomationProperties.GetName(t) == field || t.Text == field))
                        ?? throw new InvalidOperationException("no text box " + field)).Text = value; break;
                // The manual 檢查更新 path with its dialogs; a URL in place of this edition's latest-release API answers for GitHub.
                case "check-updates": Beside(CheckUpdates(true, argument.Length > 0 ? argument : null)); break;
                case "install-model": Beside(InstallModel()); break;
                // cloud-models <id>[,<id>...]: the open cloud panel takes this list as if 儲存並測試 had listed it (no key, nothing sent).
                case "cloud-models":
                    (cloudModelsListed ?? throw new InvalidOperationException("the model page is not open"))(argument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(id => new CloudModelInfo(id, id)).ToList()); break;
                case "bubble" when space > 0: ShowText(command[7..], true); break;
#if COMPANION_MAC
                // surface [input] <file.json>: the drawing surface of the bubble (or the input box) next to its window (pixels);
                // surface-shrink [input] halves the surface's height and surface-grow [input] makes it half as high again, as a
                // stale resize leaves it after the window grew or shrank; surface-check on|off switches the repair for both
                // (KeepSurfacePlatform in MacPetWindow.cs).
                case "surface":
                    var (surfaceOf, surfaceFile) = argument.StartsWith("input ", StringComparison.Ordinal) ? (SurfaceWindow("input"), argument[6..].Trim()) : (bubbleWindow, argument);
                    if (Path.GetFileName(surfaceFile) != surfaceFile || !surfaceFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("a .json file name in the data folder expected: " + surfaceFile);
                    var surface = CompanionNative.Surface(surfaceOf);
                    File.WriteAllText(surfaceFile, JsonSerializer.Serialize(new { visible = surfaceOf.IsVisible, found = surface != null,
                        drawableWidth = surface?.Drawable.X, drawableHeight = surface?.Drawable.Y, width = surface?.Width, height = surface?.Height,
                        windowWidth = surfaceOf.Width, windowHeight = surfaceOf.Height }, Readable)); break;
                case "surface-shrink": if (!CompanionNative.ResizeSurfaceForTest(SurfaceWindow(argument), .5)) throw new InvalidOperationException("the window has no Metal surface"); break;
                case "surface-grow": if (!CompanionNative.ResizeSurfaceForTest(SurfaceWindow(argument), 1.5)) throw new InvalidOperationException("the window has no Metal surface"); break;
                case "surface-check": surfaceCheck = argument != "off"; break;
                // native-drop <pet|input> <path>[|<path>...]: the calls AppKit makes when files dragged from Finder are dropped
                // in the middle of that window (MacTestDrag.cs; text:<words> in place of a path drags text); what it would have
                // got back goes to native-drop.json.
                case "native-drop":
                    int dropAt = argument.IndexOf(' ');
                    if (dropAt < 0) throw new ArgumentException("<pet|input> <path>[|<path>...] expected: " + argument);
                    Window dropInto = argument[..dropAt] switch {
                        "pet" => this,
#if !COMPANION_SAFE
                        "input" => mcpInput ?? throw new InvalidOperationException("the input is not open"),
#endif
                        var other => throw new ArgumentException("pet or input expected: " + other) };
                    var dropped = CompanionNative.DropForTest(dropInto, argument[(dropAt + 1)..].Split('|'));
                    File.WriteAllText("native-drop.json", JsonSerializer.Serialize(new { entered = dropped.Entered, updated = dropped.Updated,
                        prepared = dropped.Prepared, performed = dropped.Performed }, Readable)); break;
#endif
#if !COMPANION_SAFE
                case "render-input": RenderControl(mcpInput ?? throw new InvalidOperationException("the input is not open"), Png("input.png")); break;
                case "open-input": if (!TryOpenMcpInput()) throw new InvalidOperationException("the input needs the Agent or the MCP chat on"); break;
                case "attach" when space > 0: AddMcpAttachments(command[7..].Split('|'), "drop"); break;
                // drop-pet <path>[|<path>...]: what files dropped on the fish do once the drop has returned.
                case "drop-pet" when space > 0: TakeDroppedFiles(argument.Split('|')); break;
#endif
                default: if (ReminderTestCommand(command) || ScreenTestCommand(command) || await BubbleTestCommand(command)) break; throw new ArgumentException("unknown test command: " + command);
            }
        } catch (Exception error) { File.WriteAllText("test-error.txt", error.ToString()); }
        File.WriteAllText("test-done.txt", command); // the command ran (or failed into test-error.txt); dialogs it opened stay open
    }
#if COMPANION_MAC
    // The bubble, or the input box for "input".
    Window SurfaceWindow(string name) => name switch {
        "" => bubbleWindow,
#if !COMPANION_SAFE
        "input" => mcpInput ?? throw new InvalidOperationException("the input is not open"),
#endif
        _ => throw new ArgumentException("nothing or input expected: " + name) };
#endif
    // A dialog waits for its buttons: run it beside the command loop, so the next command can render or answer it.
    static async void Beside(Task task)
    {
        try { await task; } catch (Exception error) { File.WriteAllText("test-error.txt", error.ToString()); }
    }
    static readonly JsonSerializerOptions Readable = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static (string, string) TitleAndFile(string text) { int at = text.LastIndexOf(' '); return at > 0 ? (text[..at].Trim(), text[(at + 1)..]) : throw new ArgumentException("<title> <file> expected: " + text); }
    static (string, string) Pair(string text) { int at = text.IndexOf('='); return at > 0 ? (text[..at], text[(at + 1)..]) : throw new ArgumentException("<from>=<to> expected: " + text); }
    static IEnumerable<Window> OpenWindows() =>
        ((IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!).Windows.Distinct().Reverse();
    static Window FindWindow(string title) =>
        OpenWindows().OrderBy(w => w.IsVisible ? 0 : 1).FirstOrDefault(w => w.Title?.Contains(title) == true) ?? throw new InvalidOperationException("no window " + title);
    static void Click(string label, params Window[] scope)
    {
        static string? Text(Button b) => b.Content switch { string s => s, TextBlock t => t.Text, _ => null };
        var buttons = (scope.Length > 0 ? scope : OpenWindows().Where(w => w.IsVisible)).SelectMany(w => w.GetLogicalDescendants().OfType<Button>()).Where(b => b.IsEffectivelyVisible).ToList();
        var button = buttons.FirstOrDefault(b => Text(b) == label) ?? buttons.FirstOrDefault(b => Text(b)?.Contains(label) == true) ?? throw new InvalidOperationException("no button " + label);
        switch (ControlAutomationPeer.CreatePeerForElement(button)) { case IToggleProvider toggle: toggle.Toggle(); break; case IInvokeProvider invoke: invoke.Invoke(); break; default: throw new InvalidOperationException("cannot click " + label); }
    }
    ScrollViewer SettingsScroll() => settings?.GetLogicalDescendants().OfType<ScrollViewer>().First() ?? throw new InvalidOperationException("the settings are not open");
    // The page is rendered where it is scrolled to, so it goes back to the top for a moment, on the window's background.
    void RenderSettingsPage(string path)
    {
        var scroll = SettingsScroll(); var page = (Panel)scroll.Content!;
        var (offset, background) = (scroll.Offset, page.Background); scroll.Offset = default; page.Background = settings!.Background;
        try { RenderControl(page, path); } finally { scroll.Offset = offset; page.Background = background; }
    }
    // At 2x like a Retina screen, so the text stays readable.
    static void RenderControl(Avalonia.Controls.Control control, string path)
    {
        if (Path.GetFileName(path) != path || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("a .png file name in the data folder expected: " + path);
        control.UpdateLayout();
        var size = control.Bounds.Size; if (size.Width < 1 || size.Height < 1) throw new InvalidOperationException("nothing to render (not shown yet?): " + size);
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2)), new Vector(192, 192));
        image.Render(control); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}
