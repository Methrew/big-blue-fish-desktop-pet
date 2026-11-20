// Modified by acer1204 on 2026-10-30 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Control;
using BigBlueFish.Agent;
using BigBlueFish.Windows;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Brushes = System.Windows.Media.Brushes;

internal static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks, id;
    static MainWindow pet = null!;
    static object Read(string field) => (pet.GetType().GetField(field, Access)?.GetValue(pet) ?? pet.GetType().GetProperty(field, Access)!.GetValue(pet))!;
    static void Field(string field, object value) => pet.GetType().GetField(field, Access)!.SetValue(pet, value);
    static object? Invoke(string method, params object?[] values) => pet.GetType().GetMethod(method, Access)!.Invoke(pet, values);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point point);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr WindowStyle(IntPtr window, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    static IntPtr Handle(Window window) => new System.Windows.Interop.WindowInteropHelper(window).Handle;
    static System.Windows.Point PetPointerTarget()
    {
        Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames",Access)!.GetValue(Read("videoPlayer"))! > 3 && pet.IsVisible,8,"Visible decoded frame ready for physical pointer test");
        var bitmap=(BitmapSource)Read("videoPlayer").GetType().GetField("bitmap",Access)!.GetValue(Read("videoPlayer"))!;
        int bw=bitmap.PixelWidth,bh=bitmap.PixelHeight,step=Math.Max(1,bw/200);
        var pixels=new byte[bw*bh*4];bitmap.CopyPixels(pixels,bw*4,0);
        var candidates=from y in Enumerable.Range(bh*100/360,bh*170/360) from x in Enumerable.Range(bw*240/640,bw*160/640)
                       where pixels[(y*bw+x)*4+3]>220 && pixels[((y+step)*bw+x+step)*4+3]>220 && pixels[((y-step)*bw+x-step)*4+3]>220
                       orderby Math.Abs(x-bw/2)+Math.Abs(y-bh*190/360) select new System.Windows.Point(x,y);
        var pixel=candidates.First();var image=(System.Windows.Controls.Image)Read("videoImage");
        return image.PointToScreen(new System.Windows.Point(pixel.X*image.ActualWidth/bw,pixel.Y*image.ActualHeight/bh));
    }
    static void ClickFixture(IntPtr handle)
    {
        GetCursorPos(out var old);
        try {
            SetForegroundWindow(handle); Pump(60);
            if (!GetWindowRect(handle, out var bounds)) throw new Exception("Fixture window missing");
            Console.WriteLine($"CLICK fixture bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}, visible={IsWindowVisible(handle)}, minimized={IsIconic(handle)}");
            SetCursorPos(bounds.Left + 70, bounds.Top + 65); Pump(30);
            mouse_event(2,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(100);
        } finally { SetCursorPos(old.X, old.Y); }
        Check(GetForegroundWindow() == handle, $"Actual click focuses an independent external process (expected={handle}, actual={GetForegroundWindow()})");
    }
    static void OpenSettingsByMouse()
    {
        GetCursorPos(out var old);
        try {
            var point = PetPointerTarget();
            SetCursorPos((int)point.X,(int)point.Y); Pump(30);
            mouse_event(8,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(16,0,0,0,UIntPtr.Zero); Pump(150);
            var menu = (ContextMenu)Read("companionContextMenu");
            Check(menu.IsOpen, "Real right-click opens pet menu");
            var item = menu.Items.Cast<MenuItem>().Single(i => i.Header?.ToString() == "設定");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(200);
            Check(!menu.IsOpen, "Settings menu command closes the menu");
        } finally { SetCursorPos(old.X,old.Y); }
    }
    static void VerifySettingsFocus(Client client)
    {
        using var other = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "--focus-fixture" } })!;
        try {
            Wait(() => { var found=FindWindow(null,"MCP 設定焦點 · 獨立應用"); return found!=IntPtr.Zero && IsWindowVisible(found) && !IsIconic(found) && GetWindowRect(found,out var rect) && rect.Right-rect.Left>200; }, 10, "Independent ordinary window running");
            var external = FindWindow(null,"MCP 設定焦點 · 獨立應用");
            Pump(300);
            ClickFixture(external);
            client.Data("update_settings", new { settings = new { AnimationIntervalSeconds = 60 } }); Pump(100);
            Check(pet.GetType().GetField("companionSettings", Access)!.GetValue(pet) == null && GetForegroundWindow() == external, "MCP updates while settings closed create no settings window and keep foreground");
            Invoke("OpenCompanionSettings"); Pump(150);
            var window = (Window)Read("companionSettings"); var handle = Handle(window);
            Descendants<Button>(window).Single(b => b.Content?.ToString() == "說話與天氣").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
            var scroll = Descendants<ScrollViewer>(window).First(v => v.Visibility == Visibility.Visible && v.Content is StackPanel);
            scroll.ScrollToVerticalOffset(70); Pump(100); double offset = scroll.VerticalOffset;
            window.Left = Math.Max(SystemParameters.WorkArea.Left + 400, window.Left); double left = window.Left, top = window.Top;
            ClickFixture(external);
            for (int i=0;i<12;i++) {
                client.Data("update_settings", new { settings = new { Scale = .85 + .01 * i, BubbleScale = 1.2, AnimationIntervalSeconds = 60 + i } }); Pump(80);
                Check(ReferenceEquals(Read("companionSettings"), window) && Handle(window) == handle && GetForegroundWindow() == external && !window.IsActive,
                    "Repeated MCP update keeps the original settings window in background " + i);
                Check(!window.Topmost && (WindowStyle(handle,-20).ToInt64() & 8) == 0 && window.IsEnabled,
                    "Native settings window remains non-topmost and enabled " + i);
            }
            var currentScroll = Descendants<ScrollViewer>(window).First(v => v.Visibility == Visibility.Visible && v.Content is StackPanel);
            Check(currentScroll.Content is StackPanel panel && panel.Children.OfType<TextBlock>().First().Text == "說話與天氣" && Math.Abs(currentScroll.VerticalOffset-offset)<1,
                "Remote refresh keeps selected page and scroll position");
            Check(window.Left == left && window.Top == top, "Remote refresh keeps window location");
            Check(Descendants<Slider>(window).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "文字氣泡大小").Value == 1.2,
                "Remote value appears in existing controls");
            window.WindowState = WindowState.Minimized; Pump(100); ClickFixture(external);
            client.Data("update_settings", new { settings = new { SpeechMinMinutes = 5 } }); Pump(120);
            Check(window.WindowState == WindowState.Minimized && GetForegroundWindow() == external && Handle(window) == handle, "Remote update does not restore minimized settings");
            window.WindowState = WindowState.Maximized; Pump(100); ClickFixture(external);
            client.Data("update_settings", new { settings = new { AnimationIntervalSeconds = 70 } }); Pump(120);
            Check(window.WindowState == WindowState.Maximized && GetForegroundWindow() == external && Handle(window) == handle, "Remote update preserves maximized settings without activating");
            ClickFixture(external); Check((WindowStyle(handle,-20).ToInt64() & 8) == 0, "Other application remains usable after repeated updates");
            window.WindowState = WindowState.Minimized; Pump(); OpenSettingsByMouse();
            Check(window.WindowState == WindowState.Maximized && GetForegroundWindow() == handle, "Explicit menu opening restores the previously maximized settings and activates it");
            window.WindowState = WindowState.Normal; window.WindowState = WindowState.Minimized; Pump(); OpenSettingsByMouse();
            Check(window.WindowState == WindowState.Normal && GetForegroundWindow() == handle, "Explicit menu opening restores previously normal settings and activates it");
            Check(pet.Topmost && !pet.ShowActivated && (WindowStyle(Handle(pet),-20).ToInt64() & 0x08000008) == 0x08000008, "Pet keeps its own topmost non-activating policy");
            window.Close();
        } finally { if (!other.HasExited) { other.CloseMainWindow(); if (!other.WaitForExit(2000)) other.Kill(); } }
    }
    static void DoubleClickPet()
    {
        GetCursorPos(out var old);
        try {
            var point = PetPointerTarget();
            SetCursorPos((int)point.X, (int)point.Y); Pump(50);
            for (int i=0;i<2;i++) { mouse_event(2,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(80); }
        } finally { SetCursorPos(old.X, old.Y); }
    }
    static void DragPetByMouse()
    {
        GetCursorPos(out var old); double left=pet.Left, top=pet.Top;
        try {
            var start=PetPointerTarget();
            SetCursorPos((int)start.X,(int)start.Y); Pump(50); mouse_event(2,0,0,0,UIntPtr.Zero); Pump(50);
            SetCursorPos((int)start.X-50,(int)start.Y-25); Pump(150); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(120);
            Check(Math.Abs(pet.Left-left)+Math.Abs(pet.Top-top)>5,"Real mouse drag moves pet while consent is pending");
        } finally { mouse_event(4,0,0,0,UIntPtr.Zero); SetCursorPos(old.X,old.Y); }
    }
    // Presses right where the moving fish is drawn and drags at once, before it runs out from under the pointer.
    static void DragWalkingPet()
    {
        GetCursorPos(out var old);
        try {
            var start = PetPointerTarget();
            SetCursorPos((int)start.X, (int)start.Y); mouse_event(2,0,0,0,UIntPtr.Zero); Pump(60);
            SetCursorPos((int)start.X-60, (int)start.Y+30); Pump(150); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(150);
        } finally { mouse_event(4,0,0,0,UIntPtr.Zero); SetCursorPos(old.X, old.Y); }
    }
    // walk_to on the real pet: it runs beside a target given in physical pixels without covering it, stays there,
    // closes the double-click input on starting, and a real drag stops it where it is.
    static void VerifyWalk(Client client)
    {
        client.Data("update_settings", new { settings = new { Scale = 1d, McpChatEnabled = true } }); Pump(100);
        var area = CompanionEdgeLayout.WorkArea(pet); var body = CompanionEdgeLayout.PetBounds(pet);
        Rect Body() => new(pet.Left + body.Left, pet.Top + body.Top, body.Width, body.Height);
        string Phase() => (string)Read("animationPhase");
        JsonElement State() => client.Data("get_settings").GetProperty("state");
        bool InputOpen() => pet.GetType().GetField("mcpInput", Access)!.GetValue(pet) != null;
        // One DPI in this test: WPF screen points of the pet window are the physical pixels walk_to expects.
        Rect Physical(Rect dip) => new(pet.PointToScreen(new System.Windows.Point(dip.Left - pet.Left, dip.Top - pet.Top)), pet.PointToScreen(new System.Windows.Point(dip.Right - pet.Left, dip.Bottom - pet.Top)));
        object Aim(Rect dip, bool wait) { var r = Physical(dip); return new { x = r.X + r.Width / 2, y = r.Y + r.Height / 2, width = r.Width, height = r.Height, wait }; }
        var inside = Rect.Inflate(area, 1, 1);
        client.Data("update_settings", new { settings = new { Left = area.Right - body.Right - 40, Top = area.Bottom - body.Bottom - 40 } }); Pump(150);
        Check((bool)Invoke("TryOpenMcpInput")! && InputOpen(), "Double-click input is open before walking"); Pump(150);
        var target = new Rect(area.Left + 120, Body().Top + body.Height / 2 - 40, 80, 80);
        double startLeft = pet.Left;
        var started = client.Data("walk_to", Aim(target, false));
        Check(started.GetProperty("started").GetBoolean() && started.GetProperty("motion").GetString() == "run" && started.GetProperty("side").GetString() == "right" && !started.GetProperty("overlapsTarget").GetBoolean(),
            "walk_to plans a horizontal run to the free side of the target and returns at once");
        Check(!InputOpen(), "Starting a walk closes the double-click input");
        Wait(() => Phase() == "WalkLoop", 5, "Run turns round and reaches its looping stride");
        Wait(() => pet.Left < startLeft - 20, 3, "Fish actually moves toward the target");
        var during = State();
        Check(during.GetProperty("animationPhase").GetString()!.StartsWith("Walk") && during.GetProperty("walk").GetProperty("motion").GetString() == "run" && during.GetProperty("walk").GetProperty("etaSeconds").GetDouble() > 0,
            "get_settings reports the walk under way");
        Wait(() => Phase() == "WalkStop", 12, "Walk arrives and plays the stop segment");
        var to = started.GetProperty("to");
        Check(Math.Abs(pet.Left - to.GetProperty("Left").GetDouble()) < .6 && Math.Abs(pet.Top - to.GetProperty("Top").GetDouble()) < .6, "Fish stops exactly where it planned");
        Check(!Body().IntersectsWith(target) && Body().Left >= target.Right && inside.Contains(Body()), "Body stands beside the target without covering it, inside the work area");
        var prefs = Read("preferences");
        Check(Math.Abs((double)prefs.GetType().GetProperty("Left")!.GetValue(prefs)! - pet.Left) < .01, "Arrival position is saved");
        Check((bool)Invoke("TryOpenMcpInput")!, "Input reopens after arriving"); Pump(150);
        var reopened = (Window)Read("mcpInput");
        Check(!new Rect(reopened.Left, reopened.Top, reopened.Width, reopened.Height).IntersectsWith(target), "Reopened input keeps off the target the fish ran to");
        reopened.Close(); Pump(50);
        Wait(() => !Phase().StartsWith("Walk"), 6, "Stop segment hands back to the ordinary animation");
        var after = State();
        Check(after.GetProperty("walk").ValueKind == JsonValueKind.Null && after.GetProperty("lastWalk").GetProperty("arrived").GetBoolean(), "Finished walk clears state.walk and records the arrival");
        Pump(500); Check(Math.Abs(pet.Left - to.GetProperty("Left").GetDouble()) < .6, "Fish stays at the target instead of running back");
        // Capture pixels: the screenshot metadata maps the image back onto the physical desktop.
        var shot = client.Call("capture_screen", new { max_dimension = 1280 });
        var meta = JsonDocument.Parse(shot.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
        var screen = meta.GetProperty("screen"); double scale = meta.GetProperty("scale").GetDouble(); var petBox = meta.GetProperty("pet"); var petNow = Physical(Body());
        Check(meta.GetProperty("coordinateSpace").GetString() == "physical" && Math.Abs(scale - meta.GetProperty("Width").GetInt32() / (double)screen.GetProperty("width").GetInt32()) < .001
            && Math.Abs(petBox.GetProperty("x").GetInt32() - petNow.X) <= 2 && Math.Abs(petBox.GetProperty("width").GetInt32() - petNow.Width) <= 2, "Screenshot metadata gives its scale and the fish in physical pixels");
        var high = new Rect(Body().Left + body.Width / 2 - 40, area.Top + 40, 80, 80); var p = Physical(high);
        var floated = client.Data("walk_to", new { space = "capture", capture_id = meta.GetProperty("captureId").GetInt32(),
            x = (p.X + p.Width / 2 - screen.GetProperty("x").GetInt32()) * scale, y = (p.Y + p.Height / 2 - screen.GetProperty("y").GetInt32()) * scale, width = p.Width * scale, height = p.Height * scale });
        Check(floated.GetProperty("arrived").GetBoolean() && floated.GetProperty("cancelled").ValueKind == JsonValueKind.Null && floated.GetProperty("motion").GetString() == "float",
            "walk_to in screenshot pixels floats up and waits for the arrival");
        Check(!Body().IntersectsWith(high) && inside.Contains(Body()), "Screenshot-space target is not covered either");
        Wait(() => !Phase().StartsWith("Walk"), 3, "Float walk ends without a stop segment");
        // An agent that looked at a half-size copy gives that copy's pixels and size; walk_to scales them back.
        // The capture details alone stand for space=capture.
        var halved = client.Data("walk_to", new { capture_id = meta.GetProperty("captureId").GetInt32(),
            x = (p.X + p.Width / 2 - screen.GetProperty("x").GetInt32()) * scale / 2, y = (p.Y + p.Height / 2 - screen.GetProperty("y").GetInt32()) * scale / 2,
            width = p.Width * scale / 2, height = p.Height * scale / 2, image_width = meta.GetProperty("Width").GetInt32() / 2.0, image_height = meta.GetProperty("Height").GetInt32() / 2.0 });
        var aimed = halved.GetProperty("target");
        Check(Math.Abs(aimed.GetProperty("x").GetDouble() - p.X) <= 3 && Math.Abs(aimed.GetProperty("y").GetDouble() - p.Y) <= 3 && Math.Abs(aimed.GetProperty("width").GetDouble() - p.Width) <= 3,
            "walk_to maps a downscaled copy's pixels back through image_width and image_height");
        int seenWidth = meta.GetProperty("Width").GetInt32() / 2, seenHeight = meta.GetProperty("Height").GetInt32() / 2;
        var multiplied = client.Call("walk_to", new { capture_id = meta.GetProperty("captureId").GetInt32(), x = seenWidth * 1.5, y = 10, image_width = seenWidth, image_height = seenHeight });
        Check(multiplied.GetProperty("isError").GetBoolean() && multiplied.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("不要自己乘回原尺寸"),
            "A point off the seen image (multiplied back by hand) is an error, not a run elsewhere");
        var mixed = client.Call("walk_to", new { space = "screen", capture_id = meta.GetProperty("captureId").GetInt32(), x = 10, y = 10 });
        Check(mixed.GetProperty("isError").GetBoolean() && mixed.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("capture"), "Capture details next to space=screen are an error");
        Wait(() => !Phase().StartsWith("Walk"), 3, "Walk to the same place ends at once");
        // A real drag during a run stops it on the spot.
        var far = new Rect(area.Right - 200, Body().Top + body.Height / 2 - 40, 80, 80);
        client.Data("walk_to", Aim(far, false));
        Wait(() => Phase() == "WalkLoop", 5, "Second run is under way");
        Pump(400); DragWalkingPet();
        var stopped = State();
        Check(stopped.GetProperty("walk").ValueKind == JsonValueKind.Null && stopped.GetProperty("lastWalk").GetProperty("cancelled").GetString() == "drag" && !stopped.GetProperty("lastWalk").GetProperty("arrived").GetBoolean(),
            "Real drag stops the walk and reports drag");
        double dropped = pet.Left; Pump(600);
        Check(Math.Abs(pet.Left - dropped) < .6 && !Phase().StartsWith("Walk"), "Dragged fish stays where it was dropped and leaves the walk clips");
        client.Data("walk_to", Aim(far, false));
        Wait(() => Phase() is "WalkStart" or "WalkLoop", 3, "Third run starts");
        client.Data("interrupt_action", new { action = "点击回应-元气挥手" }); double interrupted = pet.Left; Pump(300);
        Check(State().GetProperty("lastWalk").GetProperty("cancelled").GetString() == "interrupt" && (string)Read("currentAction") == "点击回应-元气挥手" && Math.Abs(pet.Left - interrupted) < .6,
            "interrupt_action stops the walk in place and plays its action");
        var missing = client.Call("walk_to", new { desktop_item = "不存在的圖示" + Guid.NewGuid().ToString("N")[..8] });
        Check(missing.GetProperty("isError").GetBoolean() && missing.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("桌面"), "Unknown desktop icon is reported as an error");
        // Name matching on its own, away from the real desktop: a one-character slip still finds the icon.
        var match = typeof(MainWindow).GetMethod("MatchDesktopItem", BindingFlags.Static | BindingFlags.NonPublic)!;
        var icons = new List<(string Name, Rect Bounds)> { ("資源回收筒", new Rect(10, 10, 80, 80)), ("本機", new Rect(10, 100, 80, 80)), ("報告A.docx", new Rect(10, 200, 80, 80)), ("報告B.docx", new Rect(10, 300, 80, 80)) };
        string[]? files = null;
        string? Matched(string query)
        {
            try { return (((Rect, string))match.Invoke(null, [icons, query, files])!).Item2; }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { return null; }
        }
        Check(Matched("資源回收桶") == "資源回收筒" && Matched("資源回收筒") == "資源回收筒" && Matched("回收") == "資源回收筒" && Matched("報告A.DOCX") == "報告A.docx",
            "Desktop icon names match exactly, by part, ignoring case, or one character off");
        Check(Matched("報告C.docx") == null && Matched("本基") == null, "A slip that fits two icons, or one in a two-character name, is not guessed");
        Check(Matched("報告") == null, "A part of several names is not guessed either");
        icons = [("報告10.docx", new Rect(10, 10, 80, 80)), ("報告1.docx", new Rect(10, 100, 80, 80)), ("Steam", new Rect(10, 200, 80, 80))];
        Check(Matched("報告1") == "報告1.docx" && Matched("報告1.DOCX") == "報告1.docx", "The name without its extension wins over a longer name containing it");
        icons = [("報告1.docx", new Rect(10, 10, 80, 80)), ("Steam", new Rect(10, 100, 80, 80))];
        Check(Matched("報告2.docx") == null, "A different number is another file, not a slip");
        string Failure(string query) { try { match.Invoke(null, [icons, query, files]); return ""; } catch (TargetInvocationException error) { return error.InnerException!.Message; } }
        Check(!Failure("資源回收筒").Contains("Steam") && Failure("報告2.docx").Contains("報告1.docx"), "Only names that are really close are suggested");
        icons = [("Python 3.12", new Rect(10, 10, 80, 80)), ("Python 3.11", new Rect(10, 100, 80, 80))];
        Check(Matched("Python 3") == null && Failure("Python 3").Contains("Python 3.12") && Failure("Python 3").Contains("Python 3.11"), "A version number is no extension: Python 3 is listed, not taken for 3.12");
        icons = [("報告1.docx", new Rect(10, 10, 80, 80)), ("報告1.pdf", new Rect(10, 100, 80, 80)), ("報告", new Rect(10, 200, 80, 80)), ("報告", new Rect(10, 300, 80, 80)), ("合約", new Rect(10, 400, 80, 80))];
        Check(Matched("報告1") == null && Failure("報告1").Contains("報告1.pdf"), "Two files sharing a name without their extensions are listed, not guessed");
        Check(Matched("報告1.docx") == "報告1.docx" && Matched("合約.docx") == "合約" && Matched("報告1.pptx") == null, "An extension shown or hidden still finds the icon, another extension does not");
        Check(Failure("報告").Contains("stops") && Failure("報告").Contains("x="), "Icons showing the same name are listed with their places, not guessed");
        // With the desktop's files known, an icon shown without its extension is that file only if it is there.
        icons = [("報告1", new Rect(10, 10, 80, 80)), ("報告10.docx", new Rect(10, 100, 80, 80)), ("回收站", new Rect(10, 200, 80, 80))];
        files = ["報告1.pdf", "報告10.docx"];
        Check(Matched("報告1.docx") == null && Matched("報告1.pdf") == "報告1", "A hidden extension must be the file's own");
        Check(Matched("報告1") == "報告1" && Matched("告1") == "報告1" && Matched("報告10") == "報告10.docx", "A number in the query is a whole number in the name");
        files = null;
        // find_on_screen's own rules, away from the real screen.
        var search = typeof(MainWindow).Assembly.GetType("BigBlueFish.Windows.CompanionScreenSearch")!;
        object? Static(string name, params object?[] values) => search.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values);
        Check((string)Static("Stem", "報告.docx")! == "報告" && (string)Static("Stem", "壓縮檔.7z")! == "壓縮檔" && (string)Static("Stem", "Python 3.12")! == "Python 3.12"
            && (string)Static("Stem", ".gitignore")! == ".gitignore" && (string)Static("Stem", "封面.final draft")! == "封面.final draft", "Only extension-like tails are taken off");
        bool Has(string text, string term) => (bool)Static("Contains", text, term)!;
        Check(!Has("Star (4.5k)", "tar") && !Has("User avatar", "tar") && !Has("Docker Desktop", "doc") && !Has("報告.docx", "doc") && !Has("PC2005", "005"),
            "Letters and digits do not match inside another word or number");
        Check(Has("報告.DOCX", "docx") && Has("MyReportFinal", "Report") && Has("20260911.zip", "2026") && Has("資源回收筒", "回收") && Has("Scada with Agent.zip", "zip")
            && Has("Microsoft Word 文件", "word 文件"), "Whole words, camelCase humps, number starts and Chinese anywhere match");
        var matchType = search.GetNestedType("Match", BindingFlags.NonPublic)!;
        Array Matches(params (string Kind, string Name, Rect Bounds, int Source)[] rows)
        {
            var array = Array.CreateInstance(matchType, rows.Length);
            for (int i = 0; i < rows.Length; i++)
                array.SetValue(Activator.CreateInstance(matchType, rows[i].Kind, rows[i].Name, rows[i].Bounds, null, false, null, null, null, true, new IntPtr(rows[i].Source), null, null, null, null), i);
            return array;
        }
        string[] Names(object list) => ((System.Collections.IEnumerable)list).Cast<object>().Select(m => (string)matchType.GetProperty("Name")!.GetValue(m)!).ToArray();
        Check(Names(Static("ReadingOrder", Matches(("element", "右上", new Rect(300, 12, 80, 30), 1), ("element", "左下", new Rect(10, 200, 80, 30), 1), ("element", "左上", new Rect(10, 10, 80, 34), 1)))!)
            .SequenceEqual(["左上", "右上", "左下"]), "Matches come in reading order, a row at a time");
        Check(Names(Static("Distinct", Matches(("element", "報告1.docx", new Rect(10, 10, 100, 120), 1), ("element", "報告1.docx", new Rect(12, 100, 90, 20), 1), ("window", "報告1.docx - Word", new Rect(400, 0, 800, 600), 2),
            ("element", "報告1.docx", new Rect(420, 5, 200, 20), 2), ("element", "報告1.docx", new Rect(10, 300, 100, 120), 1)))!).Order().SequenceEqual(["報告1.docx", "報告1.docx", "報告1.docx - Word"]),
            "An item and its label, or a window and its title bar, count once; two separate items both count");
        Check(Names(Static("Distinct", Matches(("window", "報告1 - Word", new Rect(0, 0, 3000, 2000), 2), ("element", "報告1", new Rect(400, 300, 200, 40), 3)))!).Length == 2,
            "An item in a window in front is not swallowed by the title of a larger window behind it");
        Check(((List<string>)Static("Similar", new[] { "資源回收筒", "本機", "Steam" }, new[] { "資源回收桶" })!).SequenceEqual(["資源回收筒"]), "Similar names suggest the slip only");
        Check(!client.Data("find_on_screen", new { query = "資料夾", where = "desktop" }).TryGetProperty("unfinished", out _), "The desktop icons are read within the budget");
        VerifyFindAndTour(client, Body, State, Phase);
        Check(client.Call("walk_to", new { }).GetProperty("isError").GetBoolean() && client.Call("walk_to", new { x = 10, y = 10, space = "image" }).GetProperty("isError").GetBoolean()
            && client.Call("walk_to", new { x = 10, y = 10, space = "capture", capture_id = 999999 }).GetProperty("isError").GetBoolean(), "walk_to rejects a missing target, an unknown space and an unknown capture");
        var hiddenAim = Aim(target, true); Invoke("HideCompanion"); Pump(100);
        Check(client.Call("walk_to", hiddenAim).GetProperty("isError").GetBoolean(), "A hidden fish does not walk");
        Invoke("RestoreCompanion"); Pump(200);
    }
    // find_on_screen on the real screen: a window of another process and its buttons are found with their physical
    // bounds in reading order, the fish's own bubble is not, and walk_to's stops run through them one by one.
    static void VerifyFindAndTour(Client client, Func<Rect> body, Func<JsonElement> state, Func<string> phase)
    {
        string tag = "找測" + Guid.NewGuid().ToString("N")[..6], title = "找畫面 " + tag;
        using var fixture = Process.Start(Environment.ProcessPath!, ["--find-fixture", tag])!;
        try
        {
            Wait(() => FindWindow(null, title) != IntPtr.Zero, 15, "Find fixture window opens");
            var handle = FindWindow(null, title); Pump(1200);
            var found = client.Data("find_on_screen", new { query = tag });
            var matches = found.GetProperty("matches").EnumerateArray().ToArray();
            string[] names = matches.Select(m => m.GetProperty("name").GetString()!).ToArray();
            var window = matches.Single(m => m.GetProperty("kind").GetString() == "window");
            GetWindowRect(handle, out var r);
            Check(window.GetProperty("name").GetString() == title && Math.Abs(window.GetProperty("x").GetInt32() - (r.Left + r.Right) / 2) <= 12 && Math.Abs(window.GetProperty("y").GetInt32() - (r.Top + r.Bottom) / 2) <= 12,
                "find_on_screen finds the other process's window by its title, centred on its physical rectangle");
            var buttons = matches.Where(m => m.GetProperty("kind").GetString() == "element").ToArray();
            Check(buttons.Select(m => m.GetProperty("name").GetString()).SequenceEqual([tag + " 甲", tag + " 乙", tag + " 丙"]) && found.GetProperty("total").GetInt32() == 4,
                "Its buttons come once each, in reading order: " + string.Join("、", names));
            Check(buttons.All(m => m.GetProperty("x").GetInt32() > r.Left && m.GetProperty("x").GetInt32() < r.Right && m.GetProperty("y").GetInt32() > r.Top && m.GetProperty("y").GetInt32() < r.Bottom
                && m.GetProperty("width").GetInt32() > 20 && m.GetProperty("window").GetString() == title), "Element bounds lie inside their window and name it");
            var stops = buttons.Select(m => new { x = m.GetProperty("x").GetInt32(), y = m.GetProperty("y").GetInt32(), width = m.GetProperty("width").GetInt32(), height = m.GetProperty("height").GetInt32(), label = m.GetProperty("name").GetString() }).ToArray();
            var toured = client.Data("walk_to", new { stops, pause_seconds = .3 });
            var legs = toured.GetProperty("legs").EnumerateArray().ToArray();
            Check(toured.GetProperty("arrived").GetBoolean() && toured.GetProperty("reached").GetInt32() == 3 && legs.Length == 3 && legs.All(l => l.GetProperty("arrived").GetBoolean())
                && legs.Select(l => l.GetProperty("label").GetString()).SequenceEqual(stops.Select(s => s.label)), "walk_to stops runs to every match in order and reports each leg");
            Wait(() => ((TextBlock)Read("bubbleText")).Text == $"第 3 個（共 3 個）：{tag} 丙", 6, "The last stop's bubble says which one it is");
            var after = state();
            Check(after.GetProperty("walkBubble").GetBoolean() && after.GetProperty("tour").ValueKind == JsonValueKind.Null && after.GetProperty("lastWalk").GetProperty("reached").GetInt32() == 3,
                "After the tour the bubble counts as the walk's own and lastWalk holds the tour");
            var last = legs[2].GetProperty("target");
            var lastPixels = new Rect(last.GetProperty("x").GetInt32(), last.GetProperty("y").GetInt32(), last.GetProperty("width").GetInt32(), last.GetProperty("height").GetInt32());
            var dip = new Rect(pet.PointFromScreen(lastPixels.TopLeft) + new Vector(pet.Left, pet.Top), pet.PointFromScreen(lastPixels.BottomRight) + new Vector(pet.Left, pet.Top));
            Check(!body().IntersectsWith(dip), "The fish ends beside the last match without covering it");
            var again = client.Data("find_on_screen", new { query = tag });
            Check(again.GetProperty("total").GetInt32() == 4 && !again.GetProperty("matches").EnumerateArray().Any(m => m.GetProperty("name").GetString()!.StartsWith("第 ")),
                "The fish's own bubble showing the name is not a match");
            // Between two stops an interrupt ends the tour where the fish stands.
            var started = client.Data("walk_to", new { stops = stops.Take(2).ToArray(), pause_seconds = 8, wait = false });
            Check(started.GetProperty("started").GetBoolean() && started.GetProperty("stops").GetInt32() == 2, "A tour started without waiting reports its stops at once");
            Wait(() => state().GetProperty("tour") is { ValueKind: JsonValueKind.Object } t && t.GetProperty("reached").GetInt32() == 1, 15, "The first stop of the second tour is reached");
            client.Data("interrupt_action", new { action = "点击回应-元气挥手" }); double stood = pet.Left; Pump(400);
            var stopped = state();
            Check(stopped.GetProperty("tour").ValueKind == JsonValueKind.Null && stopped.GetProperty("lastWalk").GetProperty("cancelled").GetString() == "interrupt"
                && stopped.GetProperty("lastWalk").GetProperty("reached").GetInt32() == 1, "interrupt_action between two stops ends the tour after the first");
            Pump(1500); Check(Math.Abs(pet.Left - stood) < .6 && !phase().StartsWith("Walk"), "The fish stays put instead of running to the next stop");
            Check(client.Call("walk_to", new { stops = Array.Empty<object>() }).GetProperty("isError").GetBoolean()
                && client.Call("walk_to", new { stops = new[] { new { x = 10 } } }).GetProperty("content")[0].GetProperty("text").GetString()!.Contains("第 1 站"),
                "walk_to rejects empty stops and names the stop that is wrong");
            Check(client.Call("find_on_screen", new { query = tag, limit = 0 }).GetProperty("isError").GetBoolean() && client.Call("find_on_screen", new { query = " | " }).GetProperty("isError").GetBoolean(),
                "find_on_screen rejects a bad limit and an empty query");
            Check(client.Data("find_on_screen", new { query = tag, where = "desktop" }).GetProperty("total").GetInt32() == 0, "where=desktop leaves windows out");
            ShowWindow(handle, 6 /* SW_MINIMIZE */); Pump(600);
            Check(client.Data("find_on_screen", new { query = tag }).GetProperty("total").GetInt32() == 0, "A minimized window is not on the screen");
        }
        finally { if (!fixture.HasExited) fixture.Kill(); }
        VerifyExplorerFolder(client);
    }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    // A File Explorer window found by find_on_screen names the folder it shows, so an agent works on that folder instead
    // of guessing one from the title; desktop files carry their full paths.
    static void VerifyExplorerFolder(Client client)
    {
        var desktopFiles = (System.Collections.IEnumerable)typeof(MainWindow).Assembly.GetType("BigBlueFish.Windows.CompanionScreenSearch")!
            .GetMethod("DesktopFiles", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Check(desktopFiles.Cast<object>().All(f => f.GetType().GetProperty("FullName")!.GetValue(f) is string full && Path.IsPathRooted(full)
            && full.EndsWith((string)f.GetType().GetProperty("Name")!.GetValue(f)!, StringComparison.Ordinal)), "Desktop files are read with their full paths");
        string name = "找測資料夾" + Guid.NewGuid().ToString("N")[..6], folder = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(folder);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic Tab()
        {
            dynamic list = shell.Windows();
            for (int i = 0; i < (int)list.Count; i++)
                try { dynamic tab = list.Item(i); if (tab != null && ((string)(tab.LocationURL ?? "")).StartsWith("file:") && string.Equals(new Uri((string)tab.LocationURL).LocalPath.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return tab; }
                catch (COMException) { }
            return null;
        }
        var before = new HashSet<long>();
        { dynamic list = shell.Windows(); for (int i = 0; i < (int)list.Count; i++) try { before.Add((long)list.Item(i).HWND); } catch (COMException) { } }
        Process.Start("explorer.exe", "/n,\"" + folder + "\"")?.Dispose();
        long handle = 0;
        try
        {
            Wait(() => Tab() != null, 15, "A File Explorer window opens on a temporary folder");
            handle = (long)Tab()!.HWND; Pump(1500);
            var found = client.Data("find_on_screen", new { query = name, where = "windows" });
            var window = found.GetProperty("matches").EnumerateArray().FirstOrDefault(m => m.GetProperty("kind").GetString() == "window");
            Check(window.ValueKind == JsonValueKind.Object && window.TryGetProperty("openFolder", out var open)
                && string.Equals(open.GetString()!.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), "find_on_screen gives the folder a File Explorer window shows: " + found);
            Check(found.GetProperty("matches").EnumerateArray().Where(m => m.GetProperty("kind").GetString() == "element").All(m => m.TryGetProperty("openFolder", out _)),
                "Elements inside that window carry the same folder");
            var shown = client.Data("get_context").GetProperty("openFolders");
            Check(shown.EnumerateArray().Any(f => string.Equals(f.GetProperty("folder").GetString()!.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)),
                "get_context lists the folder that File Explorer window shows: " + shown);
        }
        finally
        {
            // Only a window this test opened is closed; a tab added to a window of the user's stays open.
            if (handle != 0 && !before.Contains(handle)) PostMessage(new IntPtr(handle), 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            Pump(800);
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Marshal.FinalReleaseComObject(shell);
        }
    }
    static readonly byte[] PngMagic = [137, 80, 78, 71, 13, 10, 26, 10];
    static void Keys(params byte[] keys)
    { foreach (var key in keys) { keybd_event(key,0,0,UIntPtr.Zero); Pump(15); } foreach (var key in Enumerable.Reverse(keys)) keybd_event(key,0,2,UIntPtr.Zero); Pump(100); }
    static void Paste(Window window, TextBox input) { window.Activate(); input.Focus(); Pump(50); Keys(0x11, 0x56); }
    static Button[] RemoveButtons(Window window) => Descendants<Button>(window).Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("移除附件 ")).ToArray();
    static void Remove(Window window, string name) => RemoveButtons(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "移除附件 " + name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static BitmapSource Solid(int width, int height, byte alpha)
    { var pixels = new byte[width*height*4]; for (int i=0;i<pixels.Length;i+=4) { pixels[i]=40; pixels[i+1]=120; pixels[i+2]=220; pixels[i+3]=alpha; } return BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4); }
    static byte[] Encode(BitmapEncoder encoder, BitmapSource image) { encoder.Frames.Add(BitmapFrame.Create(image)); using var buffer = new MemoryStream(); encoder.Save(buffer); return buffer.ToArray(); }
    static JsonElement[] UserMessages(Client client, int skip = 0) => client.Events.Skip(skip).Where(e => e.TryGetProperty("method", out var method) && method.GetString() == "notifications/message")
        .Select(e => e.GetProperty("params").GetProperty("data")).Where(d => d.GetProperty("kind").GetString() == "user_message").Select(d => d.GetProperty("detail")).ToArray();
    // The test pastes through the real clipboard; keep whatever the user had on it.
    static System.Windows.DataObject? SnapshotClipboard()
    {
        try {
            var current = System.Windows.Clipboard.GetDataObject(); if (current == null) return null;
            var copy = new System.Windows.DataObject();
            foreach (var format in current.GetFormats(false)) try { var value = current.GetData(format, false); if (value != null) copy.SetData(format, value, false); } catch (Exception) { }
            return copy;
        } catch (Exception) { return null; }
    }
    static void RestoreClipboard(System.Windows.DataObject? saved)
    { try { if (saved != null) System.Windows.Clipboard.SetDataObject(saved, true); else System.Windows.Clipboard.Clear(); } catch (Exception) { } }
    // Returns an unsent pasted screenshot left in the input, so the caller can verify closing deletes it.
    static string VerifyAttachments(Client client, Window window, TextBox input)
    {
        var area = CompanionEdgeLayout.WorkArea(pet);
        bool InArea() { area = CompanionEdgeLayout.WorkArea(pet); return window.Left >= area.Left && window.Top >= area.Top && window.Left+window.Width <= area.Right+1 && window.Top+window.Height <= area.Bottom+1; }
        string folder = Path.GetFullPath("attachments"), media = Path.GetFullPath("attachment-fixtures");
        string[] All() => Directory.Exists(folder) ? Directory.GetFiles(folder, "paste-*.png") : [];
        var earlier = All(); // sent screenshots of a previous run in the same sandbox stay for a week
        string[] Pasted() => All().Except(earlier).ToArray();
        Check(UserMessages(client).Length > 0 && UserMessages(client).All(d => !d.TryGetProperty("attachments", out _)), "Text-only user_message keeps the exact old shape without attachments");
        var attach = Descendants<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "附加檔案");
        Check(!attach.Focusable && attach.ToolTip is string tip && tip.Contains("Ctrl+V") && window.AllowDrop, "Attach button is named and unfocusable; the input accepts file drops");
        Check(window.Height <= 120 && RemoveButtons(window).Length == 0, "Input stays compact without attachments");
        input.Text = "附件說明";
        var png = Encode(new PngBitmapEncoder(), Solid(40, 30, 255));
        var data = new System.Windows.DataObject(); data.SetData("PNG", new MemoryStream(png)); System.Windows.Clipboard.SetDataObject(data, true);
        Paste(window, input);
        Wait(() => RemoveButtons(window).Length == 1, 5, "Ctrl+V of an application PNG adds one attachment");
        var first = Pasted().Single();
        Check(File.ReadAllBytes(first).SequenceEqual(png) && input.Text == "附件說明" && input.IsKeyboardFocused, "Clipboard PNG bytes saved unchanged in the sandbox; editor text and focus untouched");
        Check(window.Height > 120 && InArea(), "Attachment row grows the input inside the work area");
        Check(RemoveButtons(window).All(b => !b.Focusable), "Attachment remove buttons are unfocusable");
        System.Windows.Clipboard.SetImage(Solid(64, 48, 0));
        Paste(window, input);
        Wait(() => RemoveButtons(window).Length == 2, 5, "Ctrl+V of a bitmap adds an attachment");
        var second = Pasted().Single(p => p != first); var bytes = File.ReadAllBytes(second);
        var decoded = new FormatConvertedBitmap(BitmapFrame.Create(new MemoryStream(bytes)), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[decoded.PixelWidth*decoded.PixelHeight*4]; decoded.CopyPixels(pixels, decoded.PixelWidth*4, 0);
        Check(bytes.AsSpan().StartsWith(PngMagic) && decoded.PixelWidth == 64 && Enumerable.Range(0, pixels.Length/4).All(i => pixels[i*4+3] == 255), "Zero-alpha clipboard bitmap is saved as an opaque PNG");
        Directory.CreateDirectory(media);
        string jpg = Path.Combine(media, "海邊照片.jpg"), mp4 = Path.Combine(media, "clip.mp4");
        File.WriteAllBytes(jpg, Encode(new JpegBitmapEncoder(), Solid(120, 80, 255))); File.WriteAllBytes(mp4, Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray());
        var videoHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(mp4));
        System.Windows.Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { jpg, mp4 });
        Paste(window, input);
        Wait(() => RemoveButtons(window).Length == 4, 5, "Ctrl+V of files copied in Explorer adds both files");
        Check(Descendants<TextBlock>(window).Any(t => t.Text == "clip.mp4") && input.Text == "附件說明", "Video attachment shows its file name; editor text untouched");
        Wait(() => Descendants<Border>(window).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "海邊照片.jpg" && b.Background is ImageBrush { ImageSource.IsFrozen: true }), 5, "Image attachment shows a frozen thumbnail");
        System.Windows.Clipboard.SetText("貼上的文字"); input.Text = ""; Paste(window, input);
        Check(input.Text == "貼上的文字" && RemoveButtons(window).Length == 4, "Text-only clipboard still pastes into the editor");
        var mixed = new System.Windows.DataObject(); mixed.SetText("表格文字"); mixed.SetImage(Solid(20, 20, 255)); System.Windows.Clipboard.SetDataObject(mixed, true);
        input.Text = ""; Paste(window, input);
        Check(input.Text == "表格文字" && RemoveButtons(window).Length == 4, "Clipboard with text and an image pastes the text without an attachment");
        Check((int)Invoke("AddMcpAttachments", new[] { media }, "drop")! == 0 && (int)Invoke("AddMcpAttachments", new[] { jpg.ToUpperInvariant() }, "drop")! == 0, "Folders and duplicate paths (any case) are refused");
        var docs = Enumerable.Range(1, 7).Select(i => Path.Combine(media, $"筆記{i}.txt")).ToArray(); foreach (var doc in docs) File.WriteAllText(doc, "附件內容 " + doc);
        Check((int)Invoke("AddMcpAttachments", docs.Take(6).ToArray(), "drop")! == 6 && RemoveButtons(window).Length == 10, "Dropped files are added up to ten");
        Check((int)Invoke("AddMcpAttachments", docs.Skip(6).ToArray(), "drop")! == 0 && RemoveButtons(window).Length == 10, "The eleventh attachment is refused");
        Check(window.Height > 120 && window.Height <= 184 && InArea(), "Ten attachments keep one row inside the work area");
        Render((Visual)window.Content, "mcp-input-attachments.png", (int)window.ActualWidth, (int)window.ActualHeight);
        Remove(window, Path.GetFileName(first)); Remove(window, "筆記6.txt"); Pump(50);
        Check(!File.Exists(first) && File.Exists(docs[5]) && RemoveButtons(window).Length == 8, "Removing deletes only the unsent pasted file, never the user's original");
        input.Text = ""; int before = client.Events.Count; window.Activate(); input.Focus(); Pump(50);
        Keys(0x11, 0x0D); client.Data("get_settings");
        var sent = UserMessages(client, before);
        Check(sent.Length == 1 && sent[0].GetProperty("text").GetString() == "" && sent[0].GetProperty("attachments").GetArrayLength() == 8, "Ctrl+Enter sends attachments alone with empty text");
        var items = sent[0].GetProperty("attachments").EnumerateArray().ToArray();
        JsonElement Item(string name) => items.Single(i => i.GetProperty("name").GetString() == name);
        string Text(JsonElement item, string field) => item.GetProperty(field).GetString()!;
        var photo = Item("海邊照片.jpg"); var video = Item("clip.mp4"); var note = Item("筆記1.txt"); var shot = Item(Path.GetFileName(second));
        Check(Text(photo,"path") == jpg && Text(photo,"kind") == "image" && Text(photo,"mimeType") == "image/jpeg" && Text(photo,"source") == "clipboard-files" && photo.GetProperty("size").GetInt64() == new FileInfo(jpg).Length,
            "Copied image is sent by its original path with metadata");
        Check(Text(video,"path") == mp4 && Text(video,"kind") == "video" && Text(video,"mimeType") == "video/mp4" && System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Text(video,"path"))).SequenceEqual(videoHash),
            "Video is sent by its original, unchanged path");
        Check(Text(note,"path") == docs[0] && Text(note,"kind") == "file" && Text(note,"mimeType") == "text/plain" && Text(note,"source") == "drop", "Dropped document is sent as a file");
        Check(Text(shot,"path") == second && Path.GetDirectoryName(second) == folder && File.Exists(second) && Text(shot,"source") == "clipboard" && Text(shot,"mimeType") == "image/png" && Text(shot,"kind") == "image",
            "Pasted screenshot is a complete PNG inside the sandbox attachments folder");
        Check(RemoveButtons(window).Length == 0 && window.Height <= 120 && input.Text.Length == 0 && InArea() && Descendants<TextBlock>(window).Any(t => t.Text == "已傳送給 Agent（含 8 個附件）"),
            "Sending clears the attachment row and restores the compact input");
        data = new System.Windows.DataObject(); data.SetData("PNG", new MemoryStream(png)); System.Windows.Clipboard.SetDataObject(data, true);
        Paste(window, input);
        Wait(() => RemoveButtons(window).Length == 1, 5, "Another pasted screenshot waits unsent");
        return Pasted().Single(p => p != second);
    }
    // The fish's own reminders on the real pet, with a shifted clock and a fixed idle time: get_context reports the
    // moment; a reminder shows once the bubble is free, and waits while an agent has just spoken, the settings
    // window is open, the user is away or a program is full screen, then still comes; its settings sit on the
    // 說話與天氣 page and are writable over MCP within their choices.
    static void VerifyReminders(Client client)
    {
        void Test(string command) => Invoke("ReminderTestCommand", command);
        string At(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        bool Showing(string text) => ((TextBlock)Read("bubbleText")).Text == text && ((Border)Read("bubble")).Visibility == Visibility.Visible;
        string? Last() => client.Data("get_context").GetProperty("lastReminder") is { ValueKind: JsonValueKind.Object } last ? last.GetProperty("text").GetString() : null;
        // Reads the pipe now and then: the pet gives up on a client that leaves an event (user_returned, sent on every
        // clock move here) unread for ten seconds.
        void WaitFor(Func<bool> condition, int seconds, string label)
        {
            var clock = Stopwatch.StartNew(); var read = Stopwatch.StartNew();
            while (!condition() && clock.Elapsed.TotalSeconds < seconds) { Pump(); if (read.Elapsed.TotalSeconds >= 2) { client.Data("get_settings"); read.Restart(); } }
            Check(condition(), label);
        }
        void Hold(int seconds)
        {
            var clock = Stopwatch.StartNew(); var read = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds) { Pump(); if (read.Elapsed.TotalSeconds >= 2) { client.Data("get_settings"); read.Restart(); } }
        }
        var native = typeof(MainWindow).Assembly.GetType("BigBlueFish.Windows.CompanionNative")!;
        void Power(bool? onAC) => native.GetField("TestPowerOverride", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, onAC);
        client.Data("update_settings", new { settings = new { BubbleDurationIndex = 0, HideOnFullscreen = false, AnimationIntervalSeconds = 300 } });
        // A game the user is playing meanwhile would hold every reminder back; full screen is tested on its own below.
        Test("fullscreen:off");
        var context = client.Data("get_context");
        Check(DateTimeOffset.TryParse(context.GetProperty("localTime").GetString(), out var local) && Math.Abs((local - DateTimeOffset.Now).TotalSeconds) < 5
            && context.GetProperty("weekday").GetString()!.StartsWith("星期") && context.GetProperty("idleSeconds").GetDouble() >= 0
            && context.GetProperty("power").GetProperty("onAC").ValueKind is JsonValueKind.True or JsonValueKind.False && context.GetProperty("fullscreen").ValueKind == JsonValueKind.False
            && context.GetProperty("weather").ValueKind == JsonValueKind.Null && context.GetProperty("lastReminder").ValueKind == JsonValueKind.Null,
            "get_context reports the time, idle time and power, no weather with public info off and no reminder yet: " + context);
        client.Data("update_settings", new { settings = new { ForegroundEnabled = false } });
        Check(client.Data("get_context").GetProperty("foreground").ValueKind == JsonValueKind.Null, "get_context leaves the program in front out when the user switched that off");
        client.Data("update_settings", new { settings = new { ForegroundEnabled = true } });
        var presence = client.Data("get_settings").GetProperty("state");
        Check(presence.GetProperty("idleSeconds").ValueKind == JsonValueKind.Number && presence.TryGetProperty("activeMinutes", out _) && presence.TryGetProperty("lastBreakMinutes", out _)
            && presence.GetProperty("fullscreen").ValueKind is JsonValueKind.True or JsonValueKind.False,
            "get_settings tells how long the keyboard and mouse were still, cheaply: " + presence);
        // Away for a few minutes, then back: the agents hear user_returned with the break.
        Test("reminder-idle:400"); Hold(2); Test("reminder-idle:3"); Hold(2);
        client.Data("get_settings");
        var returned = client.Events.Where(e => e.TryGetProperty("method", out var m) && m.GetString() == "notifications/message"
            && e.GetProperty("params").GetProperty("data").GetProperty("kind").GetString() == "user_returned").Select(e => e.GetProperty("params").GetProperty("data").GetProperty("detail")).ToList();
        Check(returned.Count == 1 && returned[0].GetProperty("breakMinutes").GetInt32() is >= 6 and <= 8 && DateTimeOffset.TryParse(returned[0].GetProperty("since").GetString(), out _)
            && DateTimeOffset.TryParse(returned[0].GetProperty("at").GetString(), out _) && returned[0].GetProperty("watching").ValueKind is JsonValueKind.True or JsonValueKind.False,
            "Coming back after about 7 minutes is announced once as user_returned: " + string.Join(" | ", returned));
        var today = DateTime.Today;
        try
        {
            Test("reminder-idle:3");
            Test("reminder-clock:" + At(today.AddHours(12).AddMinutes(5)));
            WaitFor(() => Showing("中午 12 點了，該吃午餐囉～"), 12, "Lunch is mentioned at 12:05 while the user is at the computer");
            WaitFor(() => (string)Read("currentAction") == "吃午餐", 3, "The fish eats its lunch along with it");
            var told = client.Data("get_context");
            Check(told.GetProperty("lastReminder").GetProperty("kind").GetString() == "meal" && told.GetProperty("activeMinutes").GetInt32() <= 1,
                "get_context tells the last reminder and the stretch of use: " + told);
            Check(JsonDocument.Parse(File.ReadAllText(Path.Combine("cache", "reminders.json"))).RootElement.EnumerateObject().Any(p => p.Name.EndsWith(":午餐")), "What was said is remembered on disk");

            client.Data("say", new { text = "Agent 正在說話" });
            Test("reminder-clock:" + At(today.AddHours(18).AddMinutes(5)));
            Hold(10);
            Check(Last() == "中午 12 點了，該吃午餐囉～", "Dinner waits while an agent has just spoken");
            pet.GetType().GetProperty("externalBubbleAt", Access)!.SetValue(pet, DateTime.UtcNow.AddMinutes(-2));
            WaitFor(() => Showing("晚上 6 點了，該吃晚餐囉～"), 15, "Dinner comes once the agent has been quiet for a minute and its bubble is gone");

            Test("reminder-clock:" + At(today.AddDays(1).AddHours(8).AddMinutes(10)));
            Invoke("OpenCompanionSettings"); Pump(300);
            var settings = (Window)Read("companionSettings");
            Descendants<Button>(settings).Single(b => b.Content?.ToString() == "說話與天氣").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(300);
            var sit = Descendants<ComboBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "久坐提醒");
            var late = Descendants<ComboBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "深夜提醒");
            Check(sit.Items.Count == 6 && (string)sit.SelectedItem == "久坐提醒：連續使用 60 分鐘後" && late.Items.Count == 6 && (string)late.SelectedItem == "深夜提醒：00:00 到早上 5 點",
                "The 說話與天氣 page offers the break and late-night choices, showing the defaults");
            Check(Descendants<CheckBox>(settings).Count(c => c.Content is string text && text.Contains("提醒") && c.IsChecked == true) == 3, "Meal, battery and rain reminders are switched on there");
            var upcoming = Descendants<ComboBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "行程開始前提醒");
            Check(upcoming.Items.Count == 5 && (string)upcoming.SelectedItem == "行程開始前提醒：10 分鐘前"
                && Descendants<TextBlock>(settings).Any(t => t.Text.Contains("Google 日曆") && t.Text.Contains("Hermes 橋接程式")),
                "The event reminder sits with them, 10 minutes before by default, noting it needs Google 日曆 and the Hermes bridge");
            Descendants<Button>(settings).Single(b => b.Content?.ToString() == "Agent 與 MCP").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(300);
            var briefing = Descendants<CheckBox>(settings).Single(c => c.Content?.ToString() == "每天第一次坐下時講今日簡報");
            var welcomeBack = Descendants<ComboBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "回來時說期間發生的事");
            Check(briefing.IsChecked == true && welcomeBack.Items.Count == 5 && (string)welcomeBack.SelectedItem == "離開很久回來時說期間發生的事：離開 1 小時以上",
                "The Agent page offers the daily briefing and the welcome back, on by default");
            welcomeBack.SelectedIndex = 3; Pump(50);
            Check(client.Data("get_settings").GetProperty("settings").GetProperty("WelcomeBackMinutes").GetInt32() == 120, "Choosing 2 hours there changes the setting");
            welcomeBack.SelectedIndex = 2; Pump(50);
            var view = Descendants<ScrollViewer>(settings).First(v => v.Visibility == Visibility.Visible); view.ScrollToEnd(); Pump(300);
            Render((Visual)settings.Content, "reminder-settings.png", (int)settings.ActualWidth, (int)settings.ActualHeight);
            sit.SelectedIndex = 2; Pump(50);
            Check(client.Data("get_settings").GetProperty("settings").GetProperty("SitReminderMinutes").GetInt32() == 45, "Choosing 45 minutes on the page changes the setting");
            upcoming.SelectedIndex = 4; Pump(50);
            Check(client.Data("get_settings").GetProperty("settings").GetProperty("EventReminderMinutes").GetInt32() == 30, "Choosing 30 minutes before changes the setting the bridge reads");
            upcoming.SelectedIndex = 2; Pump(50);
            Hold(9);
            Check(Last() == "晚上 6 點了，該吃晚餐囉～", "Breakfast waits while the settings window is open");
            settings.Close(); Pump(100);
            WaitFor(() => Showing("早上 8 點了，該吃早餐囉～"), 12, "Breakfast comes once the settings window is closed");

            Test("reminder-idle:400");
            Test("reminder-clock:" + At(today.AddDays(1).AddHours(12).AddMinutes(10)));
            Hold(9);
            Check(Last()!.Contains("早餐"), "Nothing is said while the user is away");
            Test("reminder-idle:3");
            WaitFor(() => Showing("中午 12 點了，該吃午餐囉～"), 12, "Lunch comes when the user is back");

            Test("fullscreen:on");
            Test("reminder-clock:" + At(today.AddDays(1).AddHours(18).AddMinutes(10)));
            Hold(9);
            Check(Last()!.Contains("午餐") && pet.IsVisible, "With a program full screen nothing is said, even with the fish left on screen");
            Test("fullscreen:off");
            WaitFor(() => Showing("晚上 6 點了，該吃晚餐囉～"), 12, "Dinner comes after the full-screen program");

            Power(false);
            Test("battery-percent:15");
            Test("reminder-clock:" + At(today.AddDays(1).AddHours(14)));
            WaitFor(() => Showing("電量剩 15%，記得接上電源喔。"), 12, "A low battery off the charger is mentioned");
            Check(client.Data("get_context").GetProperty("power").GetProperty("batteryPercent").GetInt32() == 15, "get_context reports the battery level");
            Power(null);
            Test("battery-percent:real");

            Test("reminder-clock:" + At(today.AddDays(2).AddMinutes(20)));
            WaitFor(() => Showing("已經 00:20 了，早點休息吧～明天再繼續也不遲。"), 12, "A late night is mentioned after midnight");
            WaitFor(() => (string)Read("currentAction") == "哈欠连天", 3, "The fish yawns along with it");
        }
        finally
        {
            Test("reminder-clock:real"); Test("reminder-idle:real"); Test("fullscreen:real"); Test("battery-percent:real");
            Power(null);
        }
        var played = client.Data("play_action", new { action = "超大伸懶腰" });
        Check(played.GetProperty("action").GetString() == "超大伸懒腰", "An action named in Traditional Chinese queues the clip of that name");
        var wrong = client.Call("play_action", new { action = "超級伸懶腰" });
        Check(wrong.GetProperty("isError").GetBoolean() && wrong.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("超大伸懒腰"), "A misspelt action is answered with the closest names: " + wrong);
        client.Data("interrupt_action", new { action = "待机呼吸休闲" });
        client.Data("update_settings", new { settings = new { SitReminderMinutes = 90, LateReminderHour = 23, LateReminder = false, MealReminder = false } });
        var saved = client.Data("get_settings").GetProperty("settings");
        Check(saved.GetProperty("SitReminderMinutes").GetInt32() == 90 && saved.GetProperty("LateReminderHour").GetInt32() == 23 && !saved.GetProperty("LateReminder").GetBoolean() && !saved.GetProperty("MealReminder").GetBoolean(),
            "An agent can change the reminder settings");
        Check(client.Call("update_settings", new { settings = new { SitReminderMinutes = 50 } }).GetProperty("isError").GetBoolean()
            && client.Call("update_settings", new { settings = new { LateReminderHour = 20 } }).GetProperty("isError").GetBoolean()
            && client.Call("update_settings", new { settings = new { LateReminderHour = -1 } }).GetProperty("isError").GetBoolean()
            && client.Call("update_settings", new { settings = new { WelcomeBackMinutes = 45 } }).GetProperty("isError").GetBoolean()
            && client.Call("update_settings", new { settings = new { EventReminderMinutes = 20 } }).GetProperty("isError").GetBoolean(), "Values outside the choices are refused");
        client.Data("update_settings", new { settings = new { DailyBriefing = false, WelcomeBackMinutes = 240, EventReminderMinutes = 0 } });
        saved = client.Data("get_settings").GetProperty("settings");
        Check(!saved.GetProperty("DailyBriefing").GetBoolean() && saved.GetProperty("WelcomeBackMinutes").GetInt32() == 240 && saved.GetProperty("EventReminderMinutes").GetInt32() == 0,
            "An agent can switch the briefings and the event reminder too");
        client.Data("update_settings", new { settings = new { DailyBriefing = true, WelcomeBackMinutes = 60, EventReminderMinutes = 10 } });
        client.Data("update_settings", new { settings = new { SitReminderMinutes = 60, LateReminderHour = 0, LateReminder = true, MealReminder = true, BubbleDurationIndex = 3, HideOnFullscreen = true, AnimationIntervalSeconds = 40 } });
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine($"PASS {checks}: {message}"); }
    static void Pump(int milliseconds = 30)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static T Complete<T>(Task<T> task, int seconds = 15)
    {
        var clock = Stopwatch.StartNew(); while (!task.IsCompleted && clock.Elapsed.TotalSeconds < seconds) Pump();
        if (!task.IsCompleted) throw new TimeoutException(); return task.GetAwaiter().GetResult();
    }
    static void Wait(Func<bool> condition, int seconds, string label)
    { var clock = Stopwatch.StartNew(); while (!condition() && clock.Elapsed.TotalSeconds < seconds) Pump(); Check(condition(), label); }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    static void Render(Visual root, string path, int width, int height)
    { var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    // The Agent connection pages may read in Simplified Chinese or already in Taiwan Traditional: labels are compared
    // after the fish's own Simplified-to-Traditional filter, so either wording of the same text matches.
    static readonly MethodInfo TraditionalText = typeof(MainWindow).Assembly.GetType("BigBlueFish.Windows.CompanionTraditional")!
        .GetMethod("Convert", BindingFlags.Static | BindingFlags.NonPublic)!;
    static string Traditional(string text) => (string)TraditionalText.Invoke(null, [text])!;
    static bool Same(object? label, string expected) => label?.ToString() is string text && Traditional(text) == Traditional(expected);
    static void VerifyAgentWorkspaceSettings(Window settings)
    {
        var coordinator = (AgentCoordinator)Read("agent"); string original = coordinator.Workspaces.Current;
        Check(Descendants<Button>(settings).Single(b => Same(b.Content, "新增工作區")).IsVisible, "workspace controls belong to visible enabled Agent page");
        Render((Visual)settings.Content, "agent-workspace-top.png", (int)settings.ActualWidth, (int)settings.ActualHeight);
        Wait(() => Descendants<ComboBox>(settings).Any(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "accurate")), 3, "actual settings show Agent-advertised model selector");
        var selector = Descendants<ComboBox>(settings).Single(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "accurate"));
        selector.BringIntoView(); Pump(150);
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "accurate");
        Wait(() => coordinator.CurrentModel == "accurate" && !coordinator.Busy, 5, "settings model selection reaches ACP and receives ACK"); Pump(700);
        Check(Descendants<TextBlock>(settings).Any(t => Traditional(t.Text).Contains(Traditional("思考深度"))), "settings render supported reasoning selector");
        Render((Visual)settings.Content, "agent-workspace-settings.png", (int)settings.ActualWidth, (int)settings.ActualHeight);
        Descendants<Button>(settings).Single(b => Same(b.Content, "新增工作區")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
        var dialog = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => Same(w.Title, "新增工作區"));
        string name = "ui-workspace-" + Guid.NewGuid().ToString("N")[..8]; Descendants<TextBox>(dialog).Single().Text = name;
        Descendants<Button>(dialog).Single(b => Same(b.Content, "建立並開啟")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => coordinator.Connected && Path.GetFileName(coordinator.Workspaces.Current) == name && !dialog.IsVisible, 8, "new workspace dialog creates folder and reconnects");
        string file = Path.Combine(coordinator.Workspaces.Current, "review.txt"); File.WriteAllText(file, "retain on cancellation");
        DispatcherTimer Answer(string button)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => { var confirmation = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => Same(w.Title, "清空工作區")); if (confirmation == null) return; timer.Stop(); Descendants<Button>(confirmation).Single(b => Same(b.Content, button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            timer.Start(); return timer;
        }
        var answer = Answer("取消"); Descendants<Button>(settings).Single(b => Same(b.Content, "清空工作區")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => !(bool)Read("mcpConsentOpen"), 5, "clear workspace cancel closes confirmation"); answer.Stop();
        Check(File.Exists(file), "cancelling clear preserves actual workspace files"); Pump(650);
        string before = coordinator.SessionId; answer = Answer("清空");
        Descendants<Button>(settings).Single(b => Same(b.Content, "清空工作區")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => !File.Exists(file) && coordinator.Connected && coordinator.SessionId != before, 8, "confirmed clear removes files and starts fresh ACP conversation"); answer.Stop();
        Complete(coordinator.OpenWorkspace(original).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
        Wait(() => coordinator.Connected && coordinator.Workspaces.Current == original, 8, "original workspace reopens after clear test");
    }
    // A client of either server: by default the MCP interface's fixed pipe, as BigBlueFish.Mcp without arguments (the
    // Hermes bridge) reaches it; with the Agent connection's private pipe, as the harness's own bridge does.
    sealed class Client : IDisposable
    {
        readonly NamedPipeClientStream pipe;
        readonly StreamReader reader; readonly StreamWriter writer;
        readonly System.Threading.Channels.Channel<string> incoming = System.Threading.Channels.Channel.CreateUnbounded<string>();
        internal readonly List<JsonElement> Events = [];
        internal Client(string? pipeName = null)
        {
            pipe = new(".", pipeName ?? ControlTools.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Complete(pipe.ConnectAsync(5000).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            reader = new(pipe, new UTF8Encoding(false)); writer = new(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            _ = Task.Run(async () => {
                try { while (await reader.ReadLineAsync() is string line) await incoming.Writer.WriteAsync(line); incoming.Writer.TryComplete(); }
                catch (Exception error) { incoming.Writer.TryComplete(error); }
            });
            Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "WPF verification", version = "1" } });
            Send("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        }
        void Send(string line) => Complete(Task.Run(() => { writer.WriteLine(line); return true; }));
        internal JsonElement Request(string method, object args)
        {
            int request = ++id;
            Send(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = request, method, @params = args }));
            while (true)
            {
                var line = Complete(incoming.Reader.ReadAsync().AsTask());
                using var doc = JsonDocument.Parse(line); var result = doc.RootElement.Clone();
                if (result.TryGetProperty("id", out var found) && found.GetInt32() == request) return result.GetProperty("result");
                Events.Add(result);
            }
        }
        internal JsonElement Call(string name, object? args = null) => Request("tools/call", new { name, arguments = args ?? new { } });
        internal JsonElement Data(string name, object? args = null)
        {
            var result = Call(name, args);
            if (result.TryGetProperty("isError", out var error) && error.GetBoolean()) throw new Exception(result.ToString());
            return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
        }
        public void Dispose() { pipe.Dispose(); }
    }
    static void VerifySpeechRoutingSettings(Window settings)
    {
        var prefs = Read("preferences");
        void Set(string key, object value) => prefs.GetType().GetProperty(key)!.SetValue(prefs, value);
        Descendants<Button>(settings).Single(b => b.Content?.ToString() == "說話與天氣").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
        var frequency = Descendants<System.Windows.Controls.ComboBox>(settings).Single(c => c.Items.Count > 0 && c.Items[0]?.ToString()?.StartsWith("說話間隔：") == true);
        Check(settings.Icon != null && settings.ShowInTaskbar && !settings.Topmost, "Settings uses explicit fish icon and ordinary taskbar window policy");
        Check(SendMessage(Handle(settings), 0x7F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero, "Native settings HWND has large taskbar icon");
        Set("AgentDailySpeech", false); Invoke("SetPuppetMode", true); Pump(1200);
        Check(!frequency.IsEnabled, "Agent timing disables fish speech-interval selector even with local writer");
        Invoke("RefreshMcpMenuAvailability");
        var contextMenu = (ContextMenu)Read("companionContextMenu");
        Check(((MenuItem)contextMenu.Items[1]).IsEnabled && ((MenuItem)contextMenu.Items[2]).IsEnabled, "Pet weather and speech-test entries stay enabled with Agent timing");
        var tray = Read("notifyIcon"); var trayMenu = (System.Windows.Forms.ContextMenuStrip)tray.GetType().GetProperty("ContextMenuStrip", Access)!.GetValue(tray)!;
        Check(trayMenu.Items[2].Enabled && trayMenu.Items[3].Enabled, "Tray weather and speech-test entries stay enabled with Agent timing");
        var manualTest = (Task)Invoke("SpeakCompanionTest")!; Wait(() => manualTest.IsCompleted, 5, "Manual test entry completes disabled-model notice"); manualTest.GetAwaiter().GetResult();
        Check(((TextBlock)Read("bubbleText")).Text.Contains("關閉了「啟用模型說話」"), "Manual local test passes takeover guard and reports disabled model rather than silently returning");
        Set("AgentDailySpeech", true); Invoke("ConfigureAgent");
        var manualAgentTest = (Task)Invoke("SpeakCompanionTest")!; Wait(() => manualAgentTest.IsCompleted, 5, "Manual Agent test enqueued without local model"); manualAgentTest.GetAwaiter().GetResult();
        Wait(() => ((TextBlock)Read("bubbleText")).Text == "tool-only response", 10, "Manual menu test routes through actual ACP fixture and MCP say with Agent timing enabled");
        Set("AgentDailySpeech", false); Invoke("ConfigureAgent"); Pump(1200);
        Check(Descendants<System.Windows.Controls.TextBlock>(settings).Any(t => t.Text.Contains("此時說話間隔不可調整")), "Disabled interval explains Agent timing ownership");
        Descendants<Button>(settings).Single(b => b.Content?.ToString() == "模型與供電").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
        Check(Descendants<System.Windows.Controls.TextBlock>(settings).Any(t => t.Text.Contains("Agent 決定日常關心的時機，由模型編寫要說的話")), "Model page describes the fish's own model writing while Agent owns timing");
        Invoke("SetPuppetMode", false); Set("AgentDailySpeech", true); Invoke("ConfigureAgent"); Pump(1200);
        Check(frequency.IsEnabled, "Agent writer alone keeps fish interval adjustable");
        Set("AgentDailySpeech", false); Invoke("ConfigureAgent"); Pump(1200);
        Check(frequency.IsEnabled, "Fish timing and local writer keep interval adjustable");
        Set("AgentEnabled", false); Set("PuppetMode", true); Invoke("ConfigureAgent"); Pump(1200);
        Check(frequency.IsEnabled, "Disabled Agent connection ignores stored takeover preference");
    }

    // Two flows, one per way of handing the fish to an agent. By default the MCP interface (開啟 MCP 介面): a client on
    // the fixed pipe, as the Hermes bridge connects, with chat, attachments, walk_to and the reminders. With --agent
    // (implied by --speech-routing-settings, --workspace-settings and --agent-notices) the Agent connection through the ACP fixture,
    // with a client on that connection's private pipe, as the harness's own MCP bridge connects.
    [STAThread] static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--focus-fixture") {
            var fixtureApp = new System.Windows.Application();
            fixtureApp.Run(new Window { Title = "MCP 設定焦點 · 獨立應用", Left = SystemParameters.WorkArea.Left+15, Top = SystemParameters.WorkArea.Top+15, Width = 340, Height = 180,
                Content = new TextBlock { Text = "普通應用：檢查設定視窗不會搶佔前景", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) } }); return 0;
        }
        // Another process for find_on_screen, which leaves the pet's own process out: two buttons in a row, one below.
        if (args.Length == 2 && args[0] == "--find-fixture") {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
            foreach (var (name, row, column) in new[] { ("甲", 0, 0), ("乙", 0, 1), ("丙", 1, 0) }) {
                var button = new Button { Content = args[1] + " " + name, Margin = new Thickness(16), Height = 44 };
                Grid.SetRow(button, row); Grid.SetColumn(button, column); grid.Children.Add(button);
            }
            // Off the taskbar: an uncombined taskbar button carries the title too.
            new System.Windows.Application().Run(new Window { Title = "找畫面 " + args[1], Topmost = true, ShowInTaskbar = false, Left = SystemParameters.WorkArea.Left + 160, Top = SystemParameters.WorkArea.Top + 120,
                Width = 520, Height = 240, Content = grid }); return 0;
        }
        string sandbox = Path.GetFullPath(args[0]); Directory.CreateDirectory(sandbox); Environment.CurrentDirectory = sandbox;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), Path.Combine(sandbox, "BigBlueFish.ico"), true);
        // StopModelOnBattery off: on a laptop off its charger the battery pause would hold the Agent these flows drive
        // (CompanionPreferences.AiPausedOnBattery); AnimationUiVerification --ai-power tests that pause.
        File.WriteAllText("preferences.json", "{\"ModelEnabled\":false,\"CheckUpdatesOnStartup\":false,\"PublicInfo\":false,\"StopModelOnBattery\":false}");
        var remembered = Path.Combine("cache", "reminders.json"); if (File.Exists(remembered)) File.Delete(remembered); // what an earlier run's reminders said today
        if (File.Exists("chat-log.json")) File.Delete("chat-log.json"); // an earlier run's lines would repeat the owner message the history checks find once
        App.Args = ["--test-mode"]; var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Environment.CurrentDirectory = sandbox;
        bool agentFlow = args.Contains("--agent") || args.Contains("--speech-routing-settings") || args.Contains("--workspace-settings") || args.Contains("--agent-notices");
        try
        {
            pet = new MainWindow(); pet.Show(); Wait(() => (bool)Read("companionReady"), 20, "Actual video initialized");
            Invoke("OpenCompanionSettings"); Pump(150);
            var settings = (Window)Read("companionSettings");
            return agentFlow ? VerifyAgentConnection(app, settings, args, sandbox) : VerifyMcpInterface(app, settings, args, sandbox);
        }
        catch (Exception error) { Console.Error.WriteLine(error); if (pet != null) { Field("exitReady", true); pet.Close(); } return 1; }
    }
    static object? Member(string name) => pet.GetType().GetField(name, Access)!.GetValue(pet);
    static int Finish(string label) { Field("exitReady", true); pet.Close(); Console.WriteLine($"{label}: {checks} checks passed."); return 0; }
    static void SendWithCtrlEnter() { keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x0D,0,0,UIntPtr.Zero); Pump(30); keybd_event(0x0D,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero); Pump(100); }
    static bool UserMessageSent(Client client, string text) => client.Events.Any(e => e.TryGetProperty("method", out var method) && method.GetString() == "notifications/message"
        && e.GetProperty("params").GetProperty("data").GetProperty("kind").GetString() == "user_message" && e.GetProperty("params").GetProperty("data").GetProperty("detail").GetProperty("text").GetString() == text);
    const string OwnerMessage = "主人輸入的原文\n第二行：ABC <> &，都原樣傳遞~";
    // The double-click input on the real pet, the same for both flows: frameless and attached, inside the work area at
    // every scale and corner, following a real drag, hidden and restored with the pet, Ctrl+Enter sending the literal text.
    static (Window Window, TextBox Input, TextBlock Placeholder) VerifyAttachedInput(Client client, string opened, Action hide, Action show)
    {
        DoubleClickPet();
        Wait(() => Member("mcpInput") != null, 4, opened);
        var inputWindow = (Window)Read("mcpInput"); Pump(150);
        var input = Descendants<TextBox>(inputWindow).Single();
        const string hint = "透過魚魚和已連線的 Agent 說話";
        var placeholder = Descendants<TextBlock>(inputWindow).Single(t => t.Text == hint);
        Check(inputWindow.WindowStyle == System.Windows.WindowStyle.None && !inputWindow.ShowInTaskbar && inputWindow.Owner == pet && inputWindow.Topmost, "Input is a frameless pet attachment, without independent title bar or taskbar button");
        Check(inputWindow.Width <= 354 && inputWindow.Height <= 120 && placeholder.IsVisible && placeholder.Foreground == Brushes.Gray, "Compact input shows the exact gray placeholder while empty");
        Check(GetForegroundWindow() == Handle(inputWindow) && input.IsKeyboardFocused, "Explicit double-click gives only the editor keyboard focus");
        Check(Descendants<Button>(inputWindow).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "附加檔案"), "The input offers the attachment button");
        Render((Visual)inputWindow.Content, "mcp-input-empty.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
        var area = CompanionEdgeLayout.WorkArea(pet);
        foreach (var scale in new[] { .5, 1d, 2d })
            foreach (var corner in new[] { (area.Left, area.Top), (area.Right, area.Top), (area.Left, area.Bottom), (area.Right, area.Bottom) }) {
                client.Data("update_settings", new { settings = new { Scale = scale, Left = corner.Item1, Top = corner.Item2 } }); Pump(100);
                area = CompanionEdgeLayout.WorkArea(pet);
                Check(inputWindow.Left >= area.Left && inputWindow.Top >= area.Top && inputWindow.Left+inputWindow.Width <= area.Right+1 && inputWindow.Top+inputWindow.Height <= area.Bottom+1, "Attached input fits work area at scale=" + scale + " corner=" + corner);
            }
        client.Data("update_settings", new { settings = new { Scale = 1d, Left = area.Right-800, Top = area.Bottom-450 } }); Pump(100);
        double beforeX = inputWindow.Left, beforeY = inputWindow.Top;
        DragPetByMouse(); Pump(100);
        Check(Math.Abs(inputWindow.Left-beforeX)+Math.Abs(inputWindow.Top-beforeY)>5, "Input follows the actual dragged pet");
        input.Text = "暫存草稿"; hide(); Pump(100);
        Check(!inputWindow.IsVisible && input.Text == "暫存草稿", "Pet hiding hides the attached input and retains draft");
        show(); Pump(150); inputWindow.Activate(); input.Focus();
        Check(inputWindow.IsVisible && input.Text == "暫存草稿", "Showing the pet restores the same attached input");
        const string submitted = OwnerMessage;
        input.Text = submitted;
        Pump(50);
        Check(!placeholder.IsVisible, "Placeholder disappears while typing");
        Render((Visual)inputWindow.Content, "mcp-input.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
        SendWithCtrlEnter();
        client.Data("get_settings");
        Check(UserMessageSent(client, submitted), "User message pushed through actual MCP unchanged");
        Check(input.Text.Length == 0 && placeholder.IsVisible, "Actual Ctrl+Enter sends, clears input and restores placeholder");
        Check(pet.Topmost && !pet.ShowActivated && (WindowStyle(Handle(pet),-20).ToInt64() & 0x08000008) == 0x08000008, "Typing preserves pet topmost non-activating policy");
        return (inputWindow, input, placeholder);
    }
    // The chat history above the input, after the Agent answered OwnerMessage: a real wheel-up opens it with the editor
    // kept in place, the owner's line in a light blue bubble beside the reply, later idle lines only with ChatLogShowAll,
    // and a wheel-down past its end folds it back to the compact input.
    static void VerifyChatHistory(Client client, Window inputWindow, TextBox input)
    {
        Wait(() => Member("agent") is AgentCoordinator { Busy: false } && ((TextBlock)Read("bubbleText")).Text == "tool-only response", 10,
            "Submitted owner message receives its real fixture tool reply before checking history");
        const string idle = "魚魚之後隨口說的話";
        client.Data("say", new { text = idle }); Pump(100);
        // The attachment row is a horizontal StackPanel in a ScrollViewer too.
        var historyView = Descendants<ScrollViewer>(inputWindow).First(s => s.Content is StackPanel { Orientation: System.Windows.Controls.Orientation.Vertical });
        string HistoryText() => string.Concat(Descendants<TextBlock>((StackPanel)historyView.Content).Select(t => t.Text));
        void Wheel(int delta) { GetCursorPos(out var old); try { FrameworkElement target = delta < 0 && historyView.IsVisible ? historyView : input; var at = target.PointToScreen(new System.Windows.Point(target.ActualWidth / 2, target.ActualHeight / 2)); SetCursorPos((int)at.X, (int)at.Y); Pump(50); mouse_event(0x0800, 0, 0, unchecked((uint)delta), UIntPtr.Zero); Pump(200); } finally { SetCursorPos(old.X, old.Y); } }
        double collapsedHeight = inputWindow.Height, collapsedBottom = inputWindow.Top + inputWindow.Height;
        Check(!historyView.IsVisible, "Chat history stays hidden until the owner scrolls up");
        Wheel(120);
        Check(historyView.IsVisible && inputWindow.Height > collapsedHeight && Math.Abs(inputWindow.Top + inputWindow.Height - collapsedBottom) < 1, "Real wheel-up opens history above the editor, keeping the editor in place");
        Check(HistoryText().Contains(OwnerMessage) && HistoryText().Contains("tool-only response") && !HistoryText().Contains(idle), "Default history keeps the owner's message and the pet reply, not later idle lines");
        Check(Descendants<TextBlock>((StackPanel)historyView.Content).Single(t => t.Text == OwnerMessage).Parent is Border { Background: SolidColorBrush { Color: { R: 0xBF, G: 0xDB, B: 0xFE } } }, "Owner line sits in a light blue bubble");
        Render((Visual)inputWindow.Content, "mcp-input-history.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
        var prefs = Read("preferences");
        prefs.GetType().GetProperty("ChatLogShowAll")!.SetValue(prefs, true); Pump(400);
        Check(HistoryText().Contains(idle), "Show-all setting lists every pet line in the open history");
        Render((Visual)inputWindow.Content, "mcp-input-history-all.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
        prefs.GetType().GetProperty("ChatLogShowAll")!.SetValue(prefs, false);
        Wheel(-120); Wheel(-120);
        Check(!historyView.IsVisible && inputWindow.Height == collapsedHeight, "Wheel-down past the end folds history back to the compact input");
    }
    // Puppet mode, actions, speech and screenshots through either server, ending in puppet mode with an unsent draft.
    static void VerifyPuppetAndTools(Client client, string puppetTitle, string puppetWarning)
    {
        const string raw = "主人，MCP傳來的原文：新衣服、標點 & <>，都應原樣顯示~";
        client.Data("say", new { text = raw });
        Wait(() => ((TextBlock)Read("bubbleText")).Text == raw, 5, "External text displayed exactly without model");
        Check((bool)Read("externalBubble"), "External bubble is separate from model result");
        Render((Visual)Read("companionLayout"), "mcp-pet.png", (int)pet.Width, (int)pet.Height);
        Check(client.Data("get_settings").GetProperty("state").GetProperty("modelBusy").GetBoolean() == false, "No local inference for external text");
        var answer = AnswerPuppet(false, true, puppetTitle, puppetWarning);
        var unconfirmed = client.Call("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
        Check(unconfirmed.GetProperty("isError").GetBoolean(), "Agent-enabled puppet shows warning; cancellation leaves mode off");
        answer = AnswerPuppet(true, false, puppetTitle, puppetWarning); client.Data("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
        Check((string)Read("currentAction") == "待机呼吸休闲", "Puppet starts fixed idle");
        Field("speechRequestPending", true); Field("weatherRequestPending", true);
        Field("dragging", true); Field("lastClickAt", DateTime.UtcNow);
        var beforeRecovery = (long)Read("speechRequestVersion");
        Invoke("RecoverCompanionSession"); Pump(100);
        Check(!(bool)Read("speechRequestPending") && !(bool)Read("weatherRequestPending") && !(bool)Read("dragging") && !pet.IsMouseCaptured,
            "Resume recovery clears abandoned speech, weather and drag state");
        Check((long)Read("speechRequestVersion") > beforeRecovery && (DateTime)Read("lastClickAt") == DateTime.MinValue,
            "Resume recovery invalidates stale completions and double-click state");
        Check((string)Read("currentAction") == "待机呼吸休闲" && pet.IsVisible,
            "Resume recovery preserves fixed puppet idle and visible pet");
        DragPetByMouse(); Check((string)Read("currentAction") == "待机呼吸休闲","Actual drag preserves puppet idle");
        Invoke("ClickAnimation"); Check((string)Read("currentAction") == "待机呼吸休闲", "Click cannot switch puppet animation");
        var context = (ContextMenu)Read("companionContextMenu");
        Check(((MenuItem)context.Items[1]).IsEnabled && ((MenuItem)context.Items[2]).IsEnabled, "Manual weather and speech-test remain enabled in puppet mode");
        client.Data("say_next", new { text = "下次節奏原文" }); Field("nextSpeech", DateTime.UtcNow.AddMinutes(-1)); Pump(300);
        Check(((TextBlock)Read("bubbleText")).Text == raw, "Puppet never consumes scheduled speech");
        client.Data("interrupt_action", new { action = "点击回应-元气挥手" }); Pump(250);
        Check((string)Read("currentAction") == "点击回应-元气挥手", "Immediate action starts");
        Invoke("FinishDragAnimation");
        Check((string)Read("currentAction") == "点击回应-元气挥手", "Puppet drag release does not truncate externally commanded action");
        client.Data("play_action", new { action = "吃白飯" });
        Check((string)Read("currentAction") == "点击回应-元气挥手", "Queued action, named in Traditional Chinese, does not truncate active clip");
        Wait(() => (string)Read("currentAction") == "吃白饭", 13, "Queued action starts at actual decoder completion");
        Wait(() => (string)Read("currentAction") == "待机呼吸休闲", 13, "Puppet returns to fixed idle after command");
        Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))! >= 20, 3, "Puppet idle produces real frames");
        long frames = (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))!;
        Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))! < frames, 13, "Puppet idle loops at full clip boundary");
        Check((string)Read("currentAction") == "待机呼吸休闲", "Loop never chooses another idle or event");
        Invoke("PublishMcpInteraction", "click", new { button = "left" });
        Invoke("PublishMcpInteraction", "drag_start", new { Left = pet.Left });
        Invoke("PublishMcpInteraction", "drag_end", new { Left = pet.Left });
        client.Data("get_settings"); Check(client.Events.Count(e => e.TryGetProperty("method", out var method) && method.GetString() == "notifications/message") >= 3, "Click/drag automatically pushed on MCP connection");
        // With the Agent connection, the capture tools below still return images after the ACP peer declares no prompt
        // image support; only the host's own care turns fall back to text.
        if (Member("agent") is AgentCoordinator coordinator)
        {
            File.WriteAllText(Path.Combine(coordinator.Workspaces.Current, "fixture-no-images"), "");
            Check(coordinator.Reconnect(), "Reconnect a text-only ACP fixture");
            Wait(() => coordinator.Connected && !coordinator.Busy && !coordinator.SupportsImages, 15, "ACP peer declares no prompt image support");
        }
        using var fixture = new Fixture(); fixture.Window.Show(); fixture.Window.Activate(); Pump(200);
        Field("lastExternalForeground", new System.Windows.Interop.WindowInteropHelper(fixture.Window).Handle);
        foreach (var name in new[] { "capture_foreground", "capture_screen" })
        {
            var capture = client.Call(name, new { max_dimension = 1280 });
            Check(capture.GetProperty("content")[1].GetProperty("mimeType").GetString() == "image/png", name + " returns image content");
            var bytes = Convert.FromBase64String(capture.GetProperty("content")[1].GetProperty("data").GetString()!);
            Check(bytes.Length > 1000 && bytes[0] == 137, name + " real PNG bytes");
            File.WriteAllBytes(name + ".png", bytes);
            var meta = JsonDocument.Parse(capture.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone(); var source = meta.GetProperty("screen");
            Check(meta.GetProperty("coordinateSpace").GetString() == "physical" && meta.GetProperty("captureId").GetInt32() > 0 && source.GetProperty("width").GetInt32() >= meta.GetProperty("Width").GetInt32()
                && Math.Abs(meta.GetProperty("scale").GetDouble() - meta.GetProperty("Width").GetInt32() / (double)source.GetProperty("width").GetInt32()) < .001 && meta.GetProperty("pet").ValueKind == JsonValueKind.Object,
                name + " metadata maps image pixels onto the physical desktop");
        }
        client.Data("update_settings", new { settings = new { PuppetMode = false } });
        Check(((MenuItem)context.Items[1]).IsEnabled && ((MenuItem)context.Items[2]).IsEnabled, "Normal menu restored");
        Check((bool)Invoke("ConsumeMcpSpeech")!, "Queued original text available on return to cadence");
        Wait(() => ((TextBlock)Read("bubbleText")).Text == "下次節奏原文", 3, "Scheduled MCP text exact");
        answer = AnswerPuppet(true, false, puppetTitle, puppetWarning); client.Data("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
        Invoke("TryOpenMcpInput"); Pump(100);
        Descendants<TextBox>((Window)Read("mcpInput")).Single().Text = "斷線時保留這條草稿";
    }
    // The MCP interface for clients outside the fish, as the Hermes bridge uses it.
    static int VerifyMcpInterface(System.Windows.Application app, Window settings, string[] args, string sandbox)
    {
        Descendants<Button>(settings).Single(b => b.Content?.ToString() == "Agent 與 MCP").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var toggle = Descendants<CheckBox>(settings).Single(c => c.Content?.ToString() == "開啟 MCP 介面");
        bool sawWarning = false;
        var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        dialogTimer.Tick += (_, _) => {
            var dialog = app.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "開啟 MCP 介面"); if (dialog == null) return;
            sawWarning = Descendants<TextBlock>(dialog).Any(t => t.Text == ControlTools.McpPrivacyWarning);
            Render((Visual)dialog.Content, "mcp-warning.png", (int)dialog.ActualWidth, (int)dialog.ActualHeight);
            Descendants<Button>(dialog).Single(b => b.Content?.ToString() == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        dialogTimer.Start(); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Wait(() => !(bool)Read("mcpConsentOpen"),5,"Asynchronous privacy confirmation finishes after cancellation"); dialogTimer.Stop();
        Check(sawWarning && toggle.IsChecked == false, "Privacy warning shown; cancellation leaves MCP off");
        var prefs = Read("preferences"); prefs.GetType().GetProperty("McpEnabled")!.SetValue(prefs, true); Invoke("InitializeMcp");
        Check(Member("mcpInterface") != null && Member("mcpServer") == null && Member("agent") == null, "The MCP interface alone starts no Agent connection");
        prefs.GetType().GetProperty("McpChatEnabled")!.SetValue(prefs, true);
        Check(!(bool)Invoke("TryOpenMcpInput")!, "Chat input cannot open without a real initialized client");
        prefs.GetType().GetProperty("McpChatEnabled")!.SetValue(prefs, false);
        settings.Close(); Pump();
        using (var client = new Client())
        {
            var state = client.Data("get_settings");
            Check(state.GetProperty("settings").GetProperty("McpEnabled").GetBoolean() && !state.GetProperty("settings").GetProperty("AgentEnabled").GetBoolean(), "MCP settings state");
            Check(!state.GetProperty("settings").TryGetProperty("RemoteApiKeyProtected", out _) && !state.GetProperty("settings").TryGetProperty("CwaApiKeyProtected", out _)
                && !state.GetProperty("settings").TryGetProperty("McpWakeEnabled", out _), "get_settings leaves out the protected keys and the old aliases");
            Check(state.GetProperty("state").GetProperty("mcpClients").GetInt32() == 1 && state.GetProperty("state").GetProperty("connections").GetInt32() == 1
                && !state.GetProperty("state").GetProperty("agentConnected").GetBoolean(), "State counts the MCP interface client apart from the Agent connection");
            Check(client.Data("list_actions").GetArrayLength() == 106, "All 106 actions exposed");
            if (args.Contains("--settings-focus-only")) { VerifySettingsFocus(client); return Finish("MCP settings focus"); }
            if (args.Contains("--walk-only")) { VerifyWalk(client); return Finish("MCP walk_to"); }
            if (args.Contains("--reminders-only")) { VerifyReminders(client); return Finish("Reminders"); }
            Check(client.Call("get_operation_guide").GetProperty("isError").GetBoolean() && client.Call("schedule_wakeup", new { purpose = "關心", delay_seconds = 60 }).GetProperty("isError").GetBoolean(),
                "Tools of the Agent connection answer an error on the MCP interface");
            Check(!(bool)Invoke("TryOpenMcpInput")!, "Chat feature is off by default even when MCP is connected");
            client.Data("update_settings", new { settings = new { McpChatEnabled = true } });
            var (inputWindow, input, _) = VerifyAttachedInput(client, "Actual double-click opens input with connected enabled MCP chat", pet.Hide, pet.Show);
            var savedClipboard = SnapshotClipboard(); string unsent;
            try { unsent = VerifyAttachments(client, inputWindow, input); } finally { RestoreClipboard(savedClipboard); }
            inputWindow.Activate(); input.Focus(); Pump(50);
            keybd_event(0x1B,0,0,UIntPtr.Zero); keybd_event(0x1B,0,2,UIntPtr.Zero); Pump(100);
            Check(Member("mcpInput") == null, "Escape closes the attached editor");
            Check(!File.Exists(unsent) && Member("mcpTray") == null, "Closing the input deletes its unsent pasted screenshot");
            if (args.Contains("--input-only")) return Finish("Input UI");
            VerifyWalk(client);
            VerifyReminders(client);
            var denied = client.Call("update_settings", new { settings = new { McpEnabled = false, Scale = 2 } });
            Check(denied.GetProperty("isError").GetBoolean(), "Agent cannot disable MCP");
            Check(client.Data("get_settings").GetProperty("settings").GetProperty("Scale").GetDouble() == 1, "Denied patch applied no fields");
            Check(client.Call("update_settings", new { settings = new { AgentEnabled = true } }).GetProperty("isError").GetBoolean(), "The MCP interface cannot turn on the Agent connection");
            var secret = client.Call("update_settings", new { settings = new { RemoteApiKeyProtected = "ECHO-CHECK-731" } });
            Check(secret.GetProperty("isError").GetBoolean() && !secret.ToString().Contains("ECHO-CHECK-731"), "A protected key cannot be written over MCP and is never echoed");
            client.Data("update_settings", new { settings = new { BubbleScale = 1.3, Scale = .85, AnimationIntervalSeconds = 70 } });
            Check(File.ReadAllText("preferences.json").Contains("1.3"), "Settings persisted");
            VerifyPuppetAndTools(client, ControlTools.McpPuppetTitle, ControlTools.McpPuppetWarning);
            Check((int)Invoke("AddMcpAttachments", new[] { Path.GetFullPath(Path.Combine("attachment-fixtures", "clip.mp4")) }, "drop")! == 1, "Attachment added before disconnect");
        }
        Pump(600);
        var disconnectedInput = (Window)Read("mcpInput");
        Check(!Descendants<Button>(disconnectedInput).Single(b => b.Content?.ToString() == "傳送").IsEnabled, "Disconnect disables sending in the already-open input");
        Check(Descendants<TextBox>(disconnectedInput).Single().Text == "斷線時保留這條草稿", "Disconnect preserves unsent draft");
        Check(RemoveButtons(disconnectedInput).Length == 1, "Disconnect preserves unsent attachments");
        Check(Descendants<TextBlock>(disconnectedInput).Any(t => t.Text == "Agent 已中斷連線，重新連線後可傳送。"), "The input says the client has gone");
        Check(!(bool)Invoke("TryOpenMcpInput")!, "Disconnected double-click cannot open chat");
        Check((bool)pet.GetType().GetProperty("PuppetActive", Access)!.GetValue(pet)! && (string)Read("currentAction") == "待机呼吸休闲", "Disconnect keeps puppet mode and fixed idle");
        using (new Client())
        {
            Wait(() => (bool)Invoke("TryOpenMcpInput")!, 5, "Reconnected client allows opening chat again");
            Pump(300);
            Check(ReferenceEquals(Read("mcpInput"), disconnectedInput) && RemoveButtons(disconnectedInput).Length == 1 && Descendants<TextBox>(disconnectedInput).Single().Text == "斷線時保留這條草稿"
                && Descendants<Button>(disconnectedInput).Single(b => b.Content?.ToString() == "傳送").IsEnabled, "Reopening reuses the same input with draft, attachments and sending enabled");
        }
        Invoke("SetPuppetMode", false);
        Check((string)Read("animationPhase") == "Idle", "Explicitly leaving puppet restores ordinary idle selection");
        Read("preferences").GetType().GetProperty("McpEnabled")!.SetValue(Read("preferences"), false); Invoke("StopMcp");
        Check(Member("mcpInterface") == null && Member("mcpInput") == null, "Switching the MCP interface off stops its server and closes the input");
        return Finish($"MCP interface actual WPF integration ({sandbox})");
    }
    // A turn that ends without say, one answered only with say_next, and a dropped connection: the host's own notice
    // types out in the ordinary bubble, stays in the chat history and never counts as the Agent's speech (1.1.5).
    static void VerifyAgentNotices()
    {
        var coordinator = (AgentCoordinator)Member("agent")!;
        string fixtureMode = Path.Combine(coordinator.Workspaces.Current, "fixture-notice-mode");
        var text = (TextBlock)Read("bubbleText");
        var bubble = (Border)Read("bubble");
        long Spoken() => (long)coordinator.GetType().GetField("spoken", Access)!.GetValue(coordinator)!;
        long spoken = Spoken();
        File.WriteAllText(fixtureMode, "silent"); coordinator.SendUser("silent task");
        Wait(() => !coordinator.Busy && text.Text == "主人，Agent 結束工作了，但沒有回覆。", 15, "Stopped silent Agent produces the actual typed bubble");
        Check(Spoken() == spoken, "Host notice does not count as Agent speech");
        Check(bubble.Visibility == Visibility.Visible, "Notice is visible through the normal bubble path");
        Render(bubble, "agent-missing-reply.png", (int)bubble.ActualWidth, (int)bubble.ActualHeight);
        Check(File.ReadAllText("chat-log.json").Contains("Agent"), "Notice is recorded in persistent fish chat history");
        File.WriteAllText(fixtureMode, "pending"); coordinator.SendUser("pending only");
        Wait(() => !coordinator.Busy && coordinator.Detail.Contains("沒有呼叫說話工具"), 15, "Delayed-only answer is not counted as an immediate reply");
        Check(Spoken() == spoken, "say_next does not advance immediate speech counter");
        File.WriteAllText(fixtureMode, "disconnect"); coordinator.SendUser("disconnect task");
        Wait(() => !coordinator.Busy && text.Text == "主人，Agent 的連線中斷了。", 15, "Actual process disconnect displays the distinct typed bubble");
        Render(bubble, "agent-disconnected.png", (int)bubble.ActualWidth, (int)bubble.ActualHeight);
        File.Delete(fixtureMode);
    }
    // The Agent connection through the ACP fixture; its MCP client sits on the connection's private pipe.
    static int VerifyAgentConnection(System.Windows.Application app, Window settings, string[] args, string sandbox)
    {
        Descendants<Button>(settings).Single(b => b.Content?.ToString() == "連線 Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var toggle = Descendants<CheckBox>(settings).Single(c => c.Content?.ToString()?.Contains("Agent 命令列工具") == true);
        // Found by its notice, whichever wording its title has.
        Window? Consent() => app.Windows.Cast<Window>().FirstOrDefault(w => w != settings && w != pet && Descendants<TextBlock>(w).Any(t => t.Text == ControlTools.PrivacyWarning));
        bool sawWarning = false;
        var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        dialogTimer.Tick += (_, _) => {
            var dialog = Consent(); if (dialog == null) return;
            sawWarning = true;
            Render((Visual)dialog.Content, "agent-warning.png", (int)dialog.ActualWidth, (int)dialog.ActualHeight);
            Descendants<Button>(dialog).Single(b => b.Content?.ToString() == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        dialogTimer.Start(); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Wait(() => !(bool)Read("mcpConsentOpen"),5,"Asynchronous privacy confirmation finishes after cancellation"); dialogTimer.Stop();
        Check(sawWarning && toggle.IsChecked == false, "Privacy warning shown; cancellation leaves the Agent connection off");
        var prefs = Read("preferences");
        prefs.GetType().GetProperty("AgentHarnessId")!.SetValue(prefs, "opencode");
        var harnessSelector = Descendants<ComboBox>(settings).Single(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "opencode"));
        harnessSelector.SelectedItem = harnessSelector.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "opencode");
        string bridge = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe");
        if (!File.Exists(bridge)) File.WriteAllText(bridge, "ACP fixture reads its configured pipe directly");
        var acceptPrivacy = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        acceptPrivacy.Tick += (_, _) => { var confirmation = Consent(); if (confirmation == null) return; acceptPrivacy.Stop(); Descendants<Button>(confirmation).Single(b => b.Content?.ToString() == "瞭解並開啟").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        acceptPrivacy.Start(); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Wait(() => Member("agent") is AgentCoordinator { Connected: true }, 15, "Actual fixture ACP connection ready");
        Check(Member("mcpInterface") == null, "The Agent connection opens no MCP interface for other clients");
        if (args.Contains("--agent-notices")) { VerifyAgentNotices(); return Finish("Agent notice actual WPF"); }
        if (args.Contains("--speech-routing-settings")) { VerifySpeechRoutingSettings(settings); return Finish("Speech routing/settings actual WPF"); }
        if (args.Contains("--workspace-settings")) { VerifyAgentWorkspaceSettings(settings); return Finish("Agent workspace/settings actual WPF"); }
        settings.Close(); Pump();
        using (var client = new Client((string)Read("agentPipe")))
        {
            var state = client.Data("get_settings"); Check(state.GetProperty("settings").GetProperty("AgentEnabled").GetBoolean() && state.GetProperty("state").GetProperty("agentConnected").GetBoolean(), "MCP settings state");
            Check(client.Data("list_actions").GetArrayLength() == 106, "All 106 actions exposed");
            if (args.Contains("--settings-focus-only")) { VerifySettingsFocus(client); return Finish("MCP settings focus"); }
            var (inputWindow, input, _) = VerifyAttachedInput(client, "Actual double-click opens input with the Agent connected",
                () => Invoke("HideCompanion"), () => Invoke("RestoreCompanion"));
            VerifyChatHistory(client, inputWindow, input);
            // The ACP turn gets the attachment paths as text; the MCP event carries them as for any client.
            string note = Path.GetFullPath("agent-attachment.txt"); File.WriteAllText(note, "附件內容");
            Check((int)Invoke("AddMcpAttachments", new[] { note }, "drop")! == 1, "The Agent input takes attachments too");
            int before = client.Events.Count; input.Text = "看看這個檔案"; inputWindow.Activate(); input.Focus(); Pump(50);
            SendWithCtrlEnter(); client.Data("get_settings");
            var sent = UserMessages(client, before);
            Check(sent.Length == 1 && sent[0].GetProperty("text").GetString() == "看看這個檔案" && sent[0].GetProperty("attachments")[0].GetProperty("path").GetString() == note
                && RemoveButtons(inputWindow).Length == 0 && Descendants<TextBlock>(inputWindow).Any(t => t.Text == "已傳送給 Agent（含 1 個附件）"),
                "Sending through the Agent publishes user_message with its attachment and clears the input");
            // The fixture answers each message with say; let those turns end before the bubble checks below.
            Pump(500); Wait(() => Member("agent") is AgentCoordinator { Busy: false }, 30, "The Agent finishes the turns the messages started");
            // With the MCP interface on but 雙擊輸入交給 MCP 介面 off, what the user types for the Agent reaches no client of the interface.
            var interfacePrefs = Read("preferences");
            interfacePrefs.GetType().GetProperty("McpEnabled")!.SetValue(interfacePrefs, true); interfacePrefs.GetType().GetProperty("McpChatEnabled")!.SetValue(interfacePrefs, false);
            Invoke("InitializeMcp");
            using (var outsider = new Client())
            {
                int agentBefore = client.Events.Count, outsiderBefore = outsider.Events.Count;
                input.Text = "只給內建 Agent 的話"; inputWindow.Activate(); input.Focus(); Pump(50);
                SendWithCtrlEnter(); client.Data("get_settings"); outsider.Data("get_settings");
                Check(UserMessages(client, agentBefore).Length == 1 && UserMessages(outsider, outsiderBefore).Length == 0,
                    "Without 雙擊輸入交給 MCP 介面 the Agent's input stays off the MCP interface");
                Pump(500); Wait(() => Member("agent") is AgentCoordinator { Busy: false }, 30, "The Agent finishes that turn too");
            }
            // With it on and a client of the interface there, the input goes to that client only: the Agent gets no turn and
            // its private pipe no user_message, so the two never both answer.
            interfacePrefs.GetType().GetProperty("McpChatEnabled")!.SetValue(interfacePrefs, true);
            using (var outsider = new Client())
            {
                outsider.Data("get_settings"); // initialized before the message goes out
                int agentBefore = client.Events.Count, outsiderBefore = outsider.Events.Count;
                input.Text = "只給 MCP 介面的話"; inputWindow.Activate(); input.Focus(); Pump(50);
                SendWithCtrlEnter(); client.Data("get_settings"); outsider.Data("get_settings");
                Check(UserMessages(client, agentBefore).Length == 0 && UserMessages(outsider, outsiderBefore).Length == 1
                    && Member("agent") is AgentCoordinator { Busy: false } && Descendants<TextBlock>(inputWindow).Any(t => t.Text == "已傳送給 MCP 介面"),
                    "With 雙擊輸入交給 MCP 介面 on the input reaches only the MCP interface");
            }
            interfacePrefs.GetType().GetProperty("McpChatEnabled")!.SetValue(interfacePrefs, false);
            interfacePrefs.GetType().GetProperty("McpEnabled")!.SetValue(interfacePrefs, false); Invoke("StopMcp");
            Check(Member("mcpInterface") == null && Member("agent") is AgentCoordinator { Connected: true }, "Switching the MCP interface off again keeps the Agent connected");
            keybd_event(0x1B,0,0,UIntPtr.Zero); keybd_event(0x1B,0,2,UIntPtr.Zero); Pump(100);
            Check(Member("mcpInput") == null, "Escape closes the attached editor");
            if (args.Contains("--input-only")) return Finish("Input UI");
            var denied = client.Call("update_settings", new { settings = new { AgentEnabled = false, Scale = 2 } });
            Check(denied.GetProperty("isError").GetBoolean(), "Agent cannot disable MCP");
            Check(client.Data("get_settings").GetProperty("settings").GetProperty("Scale").GetDouble() == 1, "Denied patch applied no fields");
            Check(client.Call("update_settings", new { settings = new { McpEnabled = true } }).GetProperty("isError").GetBoolean(), "The Agent cannot open the MCP interface for other clients");
            client.Data("update_settings", new { settings = new { BubbleScale = 1.3, Scale = .85, AnimationIntervalSeconds = 70 } });
            Check(File.ReadAllText("preferences.json").Contains("1.3"), "Settings persisted");
            VerifyPuppetAndTools(client, ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning);
        }
        ((IDisposable)Read("agent")).Dispose();
        Pump(600);
        var disconnectedInput = (Window)Read("mcpInput");
        Check(!Descendants<Button>(disconnectedInput).Single(b => b.Content?.ToString() == "傳送").IsEnabled, "Disconnect disables sending in the already-open input");
        Check(Descendants<TextBox>(disconnectedInput).Single().Text == "斷線時保留這條草稿", "Disconnect preserves unsent draft");
        Check((bool)Invoke("TryOpenMcpInput")!, "Disconnected input can show connection feedback");
        Check((bool)pet.GetType().GetProperty("PuppetActive", Access)!.GetValue(pet)! && (string)Read("currentAction") == "待机呼吸休闲", "Disconnect keeps puppet mode and fixed idle");
        Invoke("SetPuppetMode", false);
        Check((string)Read("animationPhase") == "Idle", "Explicitly leaving puppet restores ordinary idle selection");
        Read("preferences").GetType().GetProperty("AgentEnabled")!.SetValue(Read("preferences"), false); Invoke("StopMcp");
        Check(Member("agent") == null && Member("mcpServer") == null && Member("mcpInput") == null, "Switching the Agent connection off stops it and closes the input");
        return Finish($"Agent connection actual WPF integration ({sandbox})");
    }
    static DispatcherTimer AnswerPuppet(bool accept, bool verifyDrag, string title, string warning)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => {
            var dialog = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == title);
            if (dialog == null) return;
            Check(Descendants<TextBlock>(dialog).Any(t => t.Text == warning), "Actual puppet warning displayed");
            Render((Visual)dialog.Content, "puppet-warning.png", (int)dialog.ActualWidth, (int)dialog.ActualHeight);
            timer.Stop();
            Check(IsWindowEnabled(Handle(pet)) && IsWindowEnabled(Handle(dialog)),"Pending consent keeps native pet and notice windows enabled");
            if (verifyDrag) DragPetByMouse();
            Descendants<Button>(dialog).Single(b => b.Content?.ToString() == (accept ? "瞭解並開啟" : "取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start(); return timer;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Window Window = new() { Title = "MCP前景截圖驗證 · 1001", Width = 640, Height = 420, Background = Brushes.LimeGreen, Content = new TextBlock { Text = "藍色大肥魚 MCP 截圖驗證\n前景內容編號：1001", FontSize = 30, Margin = new Thickness(30) } };
        public void Dispose() => Window.Close();
    }
}
