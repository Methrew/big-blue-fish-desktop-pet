// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
#if COMPANION_MAC && !COMPANION_SAFE
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BigBlueFish.Control;

namespace BigBlueFish.Windows;

// walk_to on macOS, the Avalonia port of the Windows CompanionWalk.cs (taiwan-edition by acer1204): the fish runs (or
// floats) to a point on the screen, stops beside it without covering it and stays there. Targets are macOS global
// points (top-left of the main display is the origin), as find_on_screen, CGWindowList and Accessibility give them.
// The window's Position is in the same points times DesktopScaling (1 on macOS), so the path is a straight line there.
// The Linux app does not offer walk_to; only the hooks the shared pet code calls are kept for it (end of this file).
internal sealed partial class LinuxPetWindow
{
    const string RunClip = "原地左转奔跑", FloatClip = "原地漂浮踏步";
    // Run clip: 1.4-2.0 s turns from the front to the left, 2.000-3.958 s loops seamlessly, 7.5-10 s stops and turns
    // back to the front with a smile (the trip and fall in between is skipped). The float clip steps in place facing front.
    const double RunTurn = 1.4, RunLoop = 2.0, RunLoopEnd = 3.958, RunStop = 7.5, RunStopEnd = 10, FloatLoop = .5, FloatLoopEnd = 9.42;
    const double WalkGap = 8, WalkRamp = .15;
    const int MaxStops = 20, TourLimitSeconds = 280;

    sealed class WalkPlan
    {
        internal Point From, To, Last;      // window Position; Last is where the previous frame left it
        internal ScreenBox Target;          // the target in Position units, for planning and for the input to keep off
        internal ScreenBox Points;          // the target in global points, as asked and reported
        internal string? DesktopItem, ArriveAction, Say, Cancelled;
        internal string Motion = "none", Side = "right";
        internal bool Overlaps, Mirrored, FaceMirrored, Arrived, Puppet;
        internal double Distance, Lead, Duration, Rate = 1, Scale;
        internal Avalonia.Controls.Window? Settings;
        internal readonly Stopwatch Clock = new();
        internal readonly TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    sealed record WalkTarget(ScreenBox Points, PixelRect Work, string? DesktopItem);
    sealed record WalkStop(ScreenBox Points, string? Item, string? Label);
    // walk_to with stops: one walk after another, with a pause at each stop.
    sealed class WalkTour(int count)
    {
        internal readonly int Count = count;
        internal int Reached;
        internal string? Cancelled;
        internal bool Pressed;           // held by the mouse between two stops; the release names the reason
        internal readonly List<object> Legs = new();
        internal readonly TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    readonly record struct McpCapture(int Id, ScreenBox Source, int Width, int Height);

    WalkPlan? walk;
    WalkTour? tour;
    // Stopped by a mouse press; the release (click) or the first drag movement names the reason.
    WalkPlan? walkPressed;
    object? lastWalk;
    // The bubble a stop of a tour (or walk_to's say) put up; get_settings reports it as walkBubble while it shows.
    string? walkBubbleText;
    DispatcherTimer? walkTimer;
    bool walkHooked;
    // Where the last walk left the fish; the double-click input keeps off that target while the fish stays put.
    (ScreenBox Target, PixelPoint At, double Scale)? walkAvoid;
    readonly List<McpCapture> mcpCaptures = new();
    int mcpCaptureCount;
    bool Walking => walk != null;
    bool Touring => tour != null;
    bool WalkBubbleShown => walkBubbleText != null && bubbleWindow.IsVisible && fullText == walkBubbleText;

    async Task<object> WalkToMcp(JsonElement args, CancellationToken token)
    {
        double? seenWidth = ControlTools.OptionalNumber(args, "image_width", 1, 100_000), seenHeight = ControlTools.OptionalNumber(args, "image_height", 1, 100_000);
        var captureId = ControlTools.OptionalNumber(args, "capture_id", 1, int.MaxValue);
        var space = ControlTools.OptionalChoice(args, "space", "screen", "capture");
        // Screenshot details mean screenshot pixels even with space left out. Next to an explicit space=screen they
        // show the two were mixed up, which would otherwise send the fish somewhere else without a word.
        if (captureId != null || seenWidth != null || seenHeight != null)
        {
            if (space == "screen" && args.TryGetProperty("space", out var given) && given.ValueKind == JsonValueKind.String)
                throw new ArgumentException("capture_id、image_width、image_height 只用在截圖上的座標，這時 space 要填 \"capture\"；螢幕座標點（例如 find_on_screen 的結果）不要填這三個。");
            space = "capture";
        }
        var side = ControlTools.OptionalChoice(args, "side", "auto", "left", "right", "above", "below");
        var arriveAction = ControlTools.OptionalText(args, "arrive_action", 200);
        var say = ControlTools.OptionalText(args, "say");
        // A label next to stops instead of inside each one is taken as meant for every stop.
        var label = ControlTools.OptionalText(args, "label", 80);
        bool wait = ControlTools.OptionalBool(args, "wait", true);
        double pause = ControlTools.OptionalNumber(args, "pause_seconds", 0, 10) ?? 2;
        if (captureId is { } wanted && wanted != Math.Floor(wanted)) throw new ArgumentException("capture_id 必須是整數。");
        if (arriveAction != null) arriveAction = CompanionActionNames.Resolve(policy.Rules.Select(r => r.Id), arriveAction)
            ?? throw new ArgumentException(CompanionActionNames.Unknown(policy.Rules.Select(r => r.Id), "arrive_action", arriveAction)); // Traditional names mean the same clip
        var stops = new List<WalkStop>();
        bool touring = args.TryGetProperty("stops", out var list) && list.ValueKind != JsonValueKind.Null;
        if (touring)
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaxStops) throw new ArgumentException($"stops 必須是 1–{MaxStops} 個目標的陣列。");
            foreach (var stop in list.EnumerateArray())
                try
                {
                    if (stop.ValueKind != JsonValueKind.Object) throw new ArgumentException("每一站必須是物件。");
                    stops.Add(ReadStop(stop, space, captureId, seenWidth, seenHeight, ControlTools.OptionalText(stop, "label", 80) ?? label));
                }
                catch (ArgumentException error) { throw new ArgumentException($"stops 第 {stops.Count + 1} 站：{error.Message}"); }
        }
        else stops.Add(ReadStop(args, space, captureId, seenWidth, seenHeight, null));
        EnsureWalkable();
        var targets = await ResolveWalkTargets(stops, token);
        token.ThrowIfCancellationRequested();
        if (!Preferences.AnyAgentControl) throw new OperationCanceledException();
        // A leg takes at most about 8.5 s; past Hermes' 300 s tool timeout the agent would see a failure mid-tour.
        if (touring && wait && targets.Count * 8.5 + (targets.Count - 1) * pause > TourLimitSeconds)
            throw new ArgumentException($"這趟最久可能要 {targets.Count * 8.5 + (targets.Count - 1) * pause:0} 秒，超過 {TourLimitSeconds} 秒。請減少站數或縮短 pause_seconds，或分成幾趟。");
        EnsureWalkable(); // The pet may have been hidden or grabbed while the targets were resolved.
        if (!touring)
        {
            var plan = StartWalk(targets[0], side, arriveAction, say ?? label);
            if (plan.Done.Task.IsCompleted) return await plan.Done.Task; // Already standing there.
            if (!wait) return WalkReport(plan, true);
            using (token.Register(() => Dispatcher.UIThread.Post(() => { if (walk == plan || walkPressed == plan) CancelWalk("request_cancelled"); })))
                return await plan.Done.Task;
        }
        StopTour("replaced");
        var trip = new WalkTour(targets.Count);
        tour = trip;
        _ = RunTour(trip, targets, stops.Select(s => s.Label).ToArray(), side, arriveAction, pause);
        if (!wait) return TourReport(trip, true);
        using (token.Register(() => Dispatcher.UIThread.Post(() => { if (tour == trip) CancelWalk("request_cancelled"); })))
            return await trip.Done.Task;
    }

    // A target from walk_to's own fields or from one of its stops; space, capture and the seen size are shared.
    WalkStop ReadStop(JsonElement at, string space, double? captureId, double? seenWidth, double? seenHeight, string? label)
    {
        var item = ControlTools.OptionalText(at, "desktop_item", 260)?.Trim();
        if (!string.IsNullOrEmpty(item)) return new(default, item, label);
        double? x = ControlTools.OptionalNumber(at, "x", -1_000_000, 1_000_000), y = ControlTools.OptionalNumber(at, "y", -1_000_000, 1_000_000);
        double width = ControlTools.OptionalNumber(at, "width", 0, 100_000) ?? 0, height = ControlTools.OptionalNumber(at, "height", 0, 100_000) ?? 0;
        if (x == null || y == null) throw new ArgumentException("請提供目標中心 x 與 y，或 desktop_item。");
        if (space == "screen") return new(new(x.Value - width / 2, y.Value - height / 2, width, height), null, label);
        var capture = captureId is { } id ? mcpCaptures.LastOrDefault(c => c.Id == (int)id) : mcpCaptures.LastOrDefault();
        if (capture.Id == 0) throw new ArgumentException(captureId == null ? "還沒有截圖可換算，請先呼叫 capture_screen 或 capture_foreground。" : "找不到這個 capture_id，只保留最近 8 次截圖。");
        return new(CapturePoints(capture, seenWidth, seenHeight, x.Value, y.Value, width, height), null, label);
    }

    // An agent may have looked at a smaller copy (Hermes shrinks screenshots to its vision budget): its pixels first go
    // back onto the image this capture returned, one side alone keeping the aspect, then onto the rectangle in global
    // points that image showed.
    static ScreenBox CapturePoints(McpCapture capture, double? seenWidth, double? seenHeight, double x, double y, double width, double height)
    {
        double fx = seenWidth is { } vw ? capture.Width / vw : seenHeight is { } vh0 ? capture.Height / vh0 : 1;
        double fy = seenHeight is { } vh ? capture.Height / vh : fx;
        double imageWidth = capture.Width / fx, imageHeight = capture.Height / fy;
        // A point off the image it was read from has usually been multiplied back to full size by hand as well.
        if (x < -1 || y < -1 || x > imageWidth + 1 || y > imageHeight + 1)
            throw new ArgumentException($"座標 ({x:0.#}, {y:0.#}) 不在 {imageWidth:0.#}x{imageHeight:0.#} 的圖內。x、y 填你看到的那張圖上的位置，不要自己乘回原尺寸；看到的是縮小的圖時，image_width、image_height 填縮小後的寬高。");
        x *= fx; y *= fy; width *= fx; height *= fy;
        double sx = capture.Source.Width / capture.Width, sy = capture.Source.Height / capture.Height;
        return new(capture.Source.X + (x - width / 2) * sx, capture.Source.Y + (y - height / 2) * sy, width * sx, height * sy);
    }

    void EnsureWalkable()
    {
        if (closed || phase == "Exit" || !IsVisible || hidden || fullscreenHidden || dragging)
            throw new InvalidOperationException("魚魚目前隱藏、拖曳中或正在關閉，無法移動。");
    }

    // Every stop is checked before the fish moves, and the desktop icons are read once for all of them. Icons that
    // windows cover are found too.
    async Task<List<WalkTarget>> ResolveWalkTargets(IReadOnlyList<WalkStop> stops, CancellationToken token)
    {
        MacScreenSnapshot? desktop = null;
        var targets = new List<WalkTarget>();
        foreach (var stop in stops)
            try
            {
                var (points, item) = (stop.Points, stop.Item);
                if (item != null)
                {
                    desktop ??= await MacScreenHelper.FindAsync("desktop", token);
                    if (!desktop.Trusted) throw new InvalidOperationException(MacScreenHelper.AccessibilityNeeded("用 desktop_item 找桌面圖示"));
                    var icons = desktop.Desktop?.Icons ?? [];
                    // With every icon's file known, an icon shown without its extension is that file only if it is there.
                    IReadOnlyCollection<string>? files = icons.Count > 0 && icons.All(i => i.Path != null)
                        ? icons.Where(i => !i.Folder).Select(i => Path.GetFileName(i.Path!.TrimEnd('/'))).ToArray() : null;
                    (points, item) = CompanionScreenMatching.MatchDesktopItem(icons.Select(i => (i.Name, i.Bounds)).ToList(), item, files);
                }
                var screen = ScreenAt(points.CenterX, points.CenterY)
                    ?? throw new ArgumentException("目標不在任何顯示器上，請確認座標是 macOS 全域座標點（主螢幕左上角為原點，與 find_on_screen 相同）。");
                targets.Add(new(points, screen.WorkingArea, item));
            }
            catch (ArgumentException error) when (stops.Count > 1) { throw new ArgumentException($"stops 第 {targets.Count + 1} 站：{error.Message}"); }
        return targets;
    }

    Avalonia.Platform.Screen? ScreenAt(double x, double y)
    {
        double s = DesktopScaling;
        var at = new PixelPoint((int)Math.Floor(x * s), (int)Math.Floor(y * s));
        return Screens.All.FirstOrDefault(screen => screen.Bounds.Contains(at));
    }

    // Runs on the dispatcher. Whatever stops a walk (drag, click, interrupt_action, a new walk_to, hiding, settings)
    // ends the whole tour where the fish is, between two stops as well.
    async Task RunTour(WalkTour trip, IReadOnlyList<WalkTarget> targets, IReadOnlyList<string?> labels, string side, string? arriveAction, double pause)
    {
        try
        {
            for (int i = 0; i < targets.Count && tour == trip; i++)
            {
                if (i > 0)
                {
                    var until = DateTime.UtcNow.AddSeconds(pause);
                    while (tour == trip && (trip.Pressed || DateTime.UtcNow < until)) await Task.Delay(100);
                    if (tour != trip) break;
                    if (!Preferences.AnyAgentControl) { StopTour("mcp_disabled"); break; }
                    EnsureWalkable();
                }
                var say = $"第 {i + 1} 個（共 {targets.Count} 個）" + (labels[i] is { } label ? "：" + label : "");
                var plan = StartWalk(targets[i], side, i == targets.Count - 1 ? arriveAction : null, say, trip);
                await plan.Done.Task;
                if (plan.Arrived) trip.Reached++;
                trip.Legs.Add(new { n = i + 1, label = labels[i], arrived = plan.Arrived, cancelled = plan.Cancelled, side = plan.Side, target = PointBox(plan.Points), desktopItem = plan.DesktopItem });
                if (plan.Cancelled != null) trip.Cancelled ??= plan.Cancelled;
            }
        }
        catch (InvalidOperationException) { trip.Cancelled ??= "hidden"; } // Hidden or grabbed during a pause.
        finally
        {
            if (tour == trip) tour = null;
            var report = TourReport(trip);
            // The walk that replaced this tour reports for itself, possibly already.
            if (trip.Cancelled != "replaced") lastWalk = report;
            trip.Done.TrySetResult(report);
            WriteWalkTestStatus();
            // play_action clips queued during the tour waited for its end (PlayQueued); a last run leg still turning
            // round plays them when its stop clip ends.
            if (tour == null && walk == null && phase is "Idle" or "PuppetIdle" && !dragging && !hidden && !fullscreenHidden && !closed)
                PlayQueued();
        }
    }

    // The tour (if any) ends; its current walk, if one is under way, is stopped by the caller.
    void StopTour(string reason)
    {
        if (tour is not { } trip) return;
        tour = null;
        trip.Cancelled ??= reason;
    }

    // note: a model that saw cancelled=click carried on with the rest, and one that saw hidden switched HideOnFullscreen off.
    object TourReport(WalkTour trip, bool started = false) => new {
        stops = trip.Count, reached = trip.Reached, arrived = !started && trip.Cancelled == null && trip.Reached == trip.Count, cancelled = trip.Cancelled, started,
        note = trip.Cancelled switch {
            "click" or "drag" => "使用者點了或拖了魚魚，表示要她停下來：不要再跑剩下的，直接回答跑到第幾個、還剩哪些。",
            "hidden" => "魚魚被藏起來了（使用者隱藏，或有程式全螢幕）：不要改魚魚的設定繞過，直接回答跑到第幾個、還剩哪些。",
            _ => null },
        legs = trip.Legs.ToArray(), Left = Position.X, Top = Position.Y, petBounds = PetPointBounds() };

    object? TourSnapshot() => tour is { } trip ? new { stops = trip.Count, reached = trip.Reached } : null;

    // The visible body relative to the window's Position, in Position units: the rectangle Clamp, the bubble and the
    // double-click input use too.
    ScreenBox BodyBox()
    {
        double s = DesktopScaling;
        return new(SpriteSize * 208 / 360 * s, SpriteSize * 58 / 360 * s, SpriteSize * 220 / 360 * s, SpriteSize * 278 / 360 * s);
    }

    WalkPlan StartWalk(WalkTarget target, string side, string? arriveAction, string? say, WalkTour? trip = null)
    {
        HookWalk(); PreloadWalk();
        if (trip == null) StopTour("replaced");
        CancelWalk("replaced"); moveRule = null;
        double s = DesktopScaling;
        var body = BodyBox();
        var area = new ScreenBox(target.Work.X, target.Work.Y, target.Work.Width, target.Work.Height);
        var aim = new ScreenBox(target.Points.X * s, target.Points.Y * s, target.Points.Width * s, target.Points.Height * s);
        // A bare point stands for about one desktop icon (a 64-point icon with its name under it).
        if (target.Points.Width < 1) aim = aim with { X = aim.CenterX - 40 * s, Width = 80 * s };
        if (target.Points.Height < 1) aim = aim with { Y = aim.CenterY - 40 * s, Height = 80 * s };
        var plan = new WalkPlan { From = new(Position.X, Position.Y), Last = new(Position.X, Position.Y), Target = aim, Points = target.Points, DesktopItem = target.DesktopItem,
            ArriveAction = arriveAction, Say = say, Scale = Preferences.Scale, Puppet = Puppet, Settings = settings };
        (plan.To, plan.Side, plan.Overlaps) = PlanWalk(aim, area, body, side);
        double dx = plan.To.X - plan.From.X, dy = plan.To.Y - plan.From.Y;
        plan.Distance = Math.Sqrt(dx * dx + dy * dy);
        plan.Motion = plan.Distance < 12 * s ? "none" : Math.Abs(dx) >= .5 * Math.Abs(dy) ? "run" : "float";
        plan.Mirrored = plan.Motion == "run" && dx > 0; // The clips face left.
        // The user wants the double-click input gone while the fish runs (not when she stays put).
        if (plan.Motion != "none" && !mcpInputPicking) mcpInput?.Close();
        double bodyCenter = plan.To.X + body.CenterX, aimCenter = aim.CenterX;
        plan.FaceMirrored = Math.Abs(aimCenter - bodyCenter) < body.Width / 2 ? plan.Mirrored : aimCenter > bodyCenter;
        walkAvoid = null;
        walk = plan;
        plan.Clock.Start();
        if (plan.Motion == "none") { Arrive(plan); return plan; }
        // About one and a half body lengths a second; a long way speeds up rather than taking more than 8 s,
        // and the stride follows the speed.
        double speed = 260 * Preferences.Scale * s;
        plan.Duration = Math.Clamp(plan.Distance / speed, .6, 8);
        plan.Rate = 1.25 * Math.Clamp(plan.Distance / plan.Duration / speed, .8, 1.6);
        // Moving starts once the run clip has turned round.
        plan.Lead = plan.Motion == "run" ? (RunLoop - RunTurn) / plan.Rate : 0;
        PlayWalkSegment(plan, plan.Motion == "run" ? "WalkStart" : "WalkLoop");
        // Movement runs on its own clock, so the frame timing never changes the speed.
        walkTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => WalkFrame());
        walkTimer.Start();
        WriteWalkTestStatus();
        return plan;
    }

    // Beside the target, inside its work area, never covering it: left or right (whichever is nearer), else below, else above.
    // A requested side is used when it fits. No Clamp at the end: snapping to the work area could push the body onto the target.
    (Point To, string Side, bool Overlaps) PlanWalk(ScreenBox aim, ScreenBox area, ScreenBox body, string side)
    {
        double gap = WalkGap * DesktopScaling;
        double FitLeft(double v) => Math.Clamp(v, area.X - body.X, Math.Max(area.X - body.X, area.Right - body.Right));
        double FitTop(double v) => Math.Clamp(v, area.Y - body.Y, Math.Max(area.Y - body.Y, area.Bottom - body.Bottom));
        double beside = FitTop(aim.CenterY - body.Y - body.Height / 2), level = FitLeft(aim.CenterX - body.X - body.Width / 2);
        var places = new (string Side, Point At)[] {
            ("left", new(aim.X - gap - body.Right, beside)), ("right", new(aim.Right + gap - body.X, beside)),
            ("below", new(level, aim.Bottom + gap - body.Y)), ("above", new(level, aim.Y - gap - body.Bottom)) };
        ScreenBox Body(Point at) => new(at.X + body.X, at.Y + body.Y, body.Width, body.Height);
        bool Inside(ScreenBox r) => r.X >= area.X - .5 && r.Y >= area.Y - .5 && r.Right <= area.Right + .5 && r.Bottom <= area.Bottom + .5;
        double Covered(ScreenBox r)
        {
            double width = Math.Min(r.Right, aim.Right) - Math.Max(r.X, aim.X), height = Math.Min(r.Bottom, aim.Bottom) - Math.Max(r.Y, aim.Y);
            return width > 0 && height > 0 ? width * height : 0;
        }
        bool Free(Point at) => Inside(Body(at)) && Covered(Body(at)) < .01;
        double Away(Point at) => Math.Sqrt((at.X - Position.X) * (at.X - Position.X) + (at.Y - Position.Y) * (at.Y - Position.Y));
        var chosen = places.FirstOrDefault(p => p.Side == side && Free(p.At));
        if (chosen.Side == null) chosen = places.Take(2).Where(p => Free(p.At)).OrderBy(p => Away(p.At)).FirstOrDefault();
        if (chosen.Side == null) chosen = places.Skip(2).FirstOrDefault(p => Free(p.At));
        if (chosen.Side != null) return (chosen.At, chosen.Side, false);
        // The target nearly fills its work area: cover as little of it as possible.
        var squeezed = places.Select(p => (p.Side, At: new Point(FitLeft(p.At.X), FitTop(p.At.Y))))
            .OrderBy(p => Covered(Body(p.At))).ThenBy(p => Away(p.At)).First();
        return (squeezed.At, squeezed.Side, Covered(Body(squeezed.At)) >= .01);
    }

    void PlayWalkSegment(WalkPlan plan, string segment)
    {
        if (closed) return;
        // A walk during the greeting ends it; the greeting's end would have started the event timer.
        if (phase == "Startup") { EndWelcome(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); }
        bool run = plan.Motion != "float";
        var rule = policy.Find(run ? RunClip : FloatClip);
        var (start, end, mirror, rate) = segment switch {
            "WalkStart" => (RunTurn, RunLoop, plan.Mirrored, plan.Rate),
            "WalkLoop" => run ? (RunLoop, RunLoopEnd, plan.Mirrored, plan.Rate) : (FloatLoop, FloatLoopEnd, false, plan.Rate),
            _ => (RunStop, RunStopEnd, plan.FaceMirrored, 1.25) };
        DiagnosticLog.Write(DiagnosticEvent.AnimationStarted, kind: segment, action: rule.Id);
        currentAction = rule.Id; phase = segment;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), mirror, WalkSegmentFinished, start, end, rate);
    }

    // The walk picks its own next clip; queued play_action clips wait for the arrival. Anything else goes on as after any clip.
    void WalkSegmentFinished()
    {
        string ended = phase, clip = currentAction;
        if (!closed && phase != "Exit" && ContinueWalk()) { DiagnosticLog.Write(DiagnosticEvent.AnimationCompleted, kind: ended, action: clip); return; }
        Finished();
    }

    bool ContinueWalk()
    {
        if (walk is not { } plan || phase is not ("WalkStart" or "WalkLoop" or "WalkStop")) return false;
        if (phase != "WalkStop" && !plan.Arrived) { PlayWalkSegment(plan, "WalkLoop"); return true; }
        if (phase != "WalkStop" && plan.Motion == "run") { PlayWalkSegment(plan, "WalkStop"); return true; }
        return EndWalk(plan);
    }

    void WalkFrame()
    {
        if (walk is not { Arrived: false } plan) { StopWalkFrames(); return; }
        if (closed || phase == "Exit") { CancelWalk("exit"); return; }
        if (!IsVisible || hidden || fullscreenHidden) { CancelWalk("hidden"); return; }
        if (plan.Puppet != Puppet) { CancelWalk("puppet_mode"); return; }
        if (!Preferences.AnyAgentControl) { CancelWalk("mcp_disabled"); return; }
        if (settings != null && settings != plan.Settings) { CancelWalk("settings"); return; }
        // Another clip took over (interrupt_action, a drag or click clip): the fish stays where it is.
        if (phase is not ("WalkStart" or "WalkLoop")) { CancelWalk(phase switch { "External" => "interrupt", "Drag" => "drag", "Click" => "click", _ => "cancelled" }); return; }
        // Something else moved or resized the fish (the settings window, update_settings): stop where it is now.
        if (Math.Abs(Preferences.Scale - plan.Scale) > 1e-9 || Math.Abs(Position.X - plan.Last.X) > 1 || Math.Abs(Position.Y - plan.Last.Y) > 1) { CancelWalk("settings"); return; }
        double t = plan.Clock.Elapsed.TotalSeconds - plan.Lead;
        if (t >= plan.Duration) { Arrive(plan); return; }
        double f = Travelled(t, plan.Duration);
        var next = new PixelPoint((int)Math.Round(plan.From.X + (plan.To.X - plan.From.X) * f), (int)Math.Round(plan.From.Y + (plan.To.Y - plan.From.Y) * f));
        if (next != Position) Position = next;
        plan.Last = new(Position.X, Position.Y);
    }

    // Share of the way covered after t seconds: speed rises and falls along a smoothstep over the first and last .15 s.
    static double Travelled(double t, double duration)
    {
        static double Ramp(double u) => u * u * u - u * u * u * u / 2; // integral of 3u²-2u³
        if (t <= 0) return 0;
        if (t >= duration) return 1;
        double peak = 1 / (duration - WalkRamp);
        if (t < WalkRamp) return peak * WalkRamp * Ramp(t / WalkRamp);
        if (t > duration - WalkRamp) return 1 - peak * WalkRamp * Ramp((duration - t) / WalkRamp);
        return peak * (t - WalkRamp / 2);
    }

    void Arrive(WalkPlan plan)
    {
        plan.Arrived = true; plan.Clock.Stop(); StopWalkFrames();
        Position = new PixelPoint((int)Math.Round(plan.To.X), (int)Math.Round(plan.To.Y));
        Save();
        // A random movement event right away would carry the fish off again.
        nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds);
        walkAvoid = (plan.Target, Position, Preferences.Scale);
        ReportWalk(plan, null);
        if (plan.Say != null) { SayExternal(plan.Say); walkBubbleText = fullText; }
        if (plan.Motion == "run") PlayWalkSegment(plan, "WalkStop");
        else if (!EndWalk(plan) && plan.Motion == "float") ResumeAfterWalk();
    }

    // The requested action, facing the target; false leaves the next clip to the ordinary flow.
    bool EndWalk(WalkPlan plan)
    {
        if (walk == plan) walk = null;
        if (plan.ArriveAction == null || closed || phase == "Exit") return false;
        Play(policy.Find(plan.ArriveAction), "External", plan.FaceMirrored);
        return true;
    }

    void ResumeAfterWalk()
    {
        if (!dragging && !hidden && !fullscreenHidden && PlayQueued()) return;
        Idle();
    }

    // Always stops where the fish is now; it never runs back. Only a tour's own next walk replaces a walk
    // without ending the tour (StartWalk ends it for any other).
    void CancelWalk(string reason)
    {
        if (reason != "replaced") StopTour(reason);
        if (walkPressed is { } pressed) { walkPressed = null; ReportWalk(pressed, reason); }
        if (walk is not { } plan) return;
        walk = null; plan.Clock.Stop(); StopWalkFrames();
        if (plan.Arrived) return;
        Save();
        ReportWalk(plan, reason);
        // Drag, click, interrupt_action, the next walk, puppet mode and the farewell play their own clip next.
        if (reason is "request_cancelled" or "settings" or "display_changed" or "mcp_disabled" or "hidden"
            && phase is "WalkStart" or "WalkLoop" or "WalkStop" && !closed && phase != "Exit")
            ResumeAfterWalk();
    }

    void PressWalk()
    {
        if (tour is { } trip) trip.Pressed = true; // The next stop waits for the release, which ends the tour.
        if (walk is not { } plan) return;
        walk = null; plan.Clock.Stop(); StopWalkFrames();
        if (!plan.Arrived) walkPressed = plan;
    }

    void StopWalkFrames() => walkTimer?.Stop();

    // The pointer, the window and the displays stop a walk. Hooked once, on the first walk, beside the pet's own
    // handlers: the press and the release tunnel in before them (they still see whether this was a drag), and the
    // move, the capture loss and the closing run after them.
    // Decoded openings of the walk segments (LinuxVideoOpenings), as Windows keeps them, so a turn, a replayed stride or the
    // stop shows without waiting for a new pipeline: about 22 MB more, within the player's preload budget. Preload ignores repeats.
    void PreloadWalk()
    {
        var run = Path.Combine(AppContext.BaseDirectory, "assets/fish", policy.Find(RunClip).File);
        foreach (var start in new[] { RunTurn, RunLoop, RunStop }) player.Preload(run, start);
        player.Preload(Path.Combine(AppContext.BaseDirectory, "assets/fish", policy.Find(FloatClip).File), FloatLoop);
    }
    void HookWalk()
    {
        if (walkHooked) return;
        walkHooked = true;
        AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) PressWalk(); }, RoutingStrategies.Tunnel, true);
        PointerMoved += (_, _) => { if (dragging && dragged && (walkPressed != null || tour != null)) CancelWalk("drag"); };
        AddHandler(PointerReleasedEvent, (_, _) => { if (walkPressed != null || tour is { Pressed: true }) CancelWalk(dragging && dragged ? "drag" : "click"); }, RoutingStrategies.Tunnel, true);
        PointerCaptureLost += (_, _) => { if (walkPressed != null || tour is { Pressed: true }) CancelWalk(dragged ? "drag" : "click"); };
        Closing += (_, _) => CancelWalk("exit");
        Screens.Changed += (_, _) => CancelWalk("display_changed"); // Its target rectangle belonged to the old layout.
    }

    void ReportWalk(WalkPlan plan, string? cancelled)
    {
        plan.Cancelled = cancelled;
        var result = WalkReport(plan);
        lastWalk = result;
        plan.Done.TrySetResult(result);
        WriteWalkTestStatus();
    }

    object? WalkSnapshot() => walk is { } plan ? WalkReport(plan) : null;

    // from, to, Left and Top are the window's Position (get_settings' Left and Top); target and petBounds are global points.
    object WalkReport(WalkPlan plan, bool started = false)
    {
        double elapsed = plan.Clock.Elapsed.TotalSeconds;
        return new {
            arrived = plan.Arrived, cancelled = plan.Cancelled, started, phase = walk == plan && phase.StartsWith("Walk", StringComparison.Ordinal) ? phase : null,
            motion = plan.Motion, side = plan.Side, overlapsTarget = plan.Overlaps, target = PointBox(plan.Points), desktopItem = plan.DesktopItem, arriveAction = plan.ArriveAction,
            from = new { Left = Math.Round(plan.From.X, 2), Top = Math.Round(plan.From.Y, 2) }, to = new { Left = Math.Round(plan.To.X, 2), Top = Math.Round(plan.To.Y, 2) },
            Left = Position.X, Top = Position.Y, petBounds = PetPointBounds(),
            distance = Math.Round(plan.Distance, 1), durationSeconds = Math.Round(plan.Lead + plan.Duration, 2), elapsedSeconds = Math.Round(elapsed, 2),
            etaSeconds = plan.Arrived || plan.Cancelled != null ? 0 : Math.Round(Math.Max(0, plan.Lead + plan.Duration - elapsed), 2) };
    }

    static object PointBox(ScreenBox r) => new { x = (int)Math.Round(r.X), y = (int)Math.Round(r.Y), width = (int)Math.Round(r.Width), height = (int)Math.Round(r.Height) };

    // The visible body in global points: what the fish covers in a screenshot.
    object? PetPointBounds()
    {
        if (!IsVisible) return null;
        double s = DesktopScaling;
        var body = BodyBox();
        return PointBox(new((Position.X + body.X) / s, (Position.Y + body.Y) / s, body.Width / s, body.Height / s));
    }

    // capture_* metadata: where the image lies in global points, so its pixels can be turned into screen points (walk_to).
    object CaptureMetadata(string target, string identity, CompanionWindowImage image, IntPtr handle)
    {
        var source = CompanionNative.CaptureSource(handle, target == "capture_screen");
        mcpCaptures.Add(new(++mcpCaptureCount, source, image.Width, image.Height));
        if (mcpCaptures.Count > 8) mcpCaptures.RemoveAt(0);
        return new { target, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow, captureId = mcpCaptureCount,
            screen = new { x = Math.Round(source.X, 2), y = Math.Round(source.Y, 2), width = Math.Round(source.Width, 2), height = Math.Round(source.Height, 2) },
            scale = source.Width > 0 ? Math.Round(image.Width / source.Width, 6) : 1, coordinateSpace = "macos-points", pet = PetPointBounds() };
    }

    // The input usually opens to the left of the body, right on a target the fish just ran up to from the right.
    (int X, int Y) AvoidWalkTarget(int x, int y, int width, int height)
    {
        if (walkAvoid is not { } avoid || Math.Abs(avoid.At.X - Position.X) + Math.Abs(avoid.At.Y - Position.Y) > 1 || Math.Abs(avoid.Scale - Preferences.Scale) > 1e-9) return (x, y);
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        double gap = 8 * DesktopScaling;
        var body = BodyBox();
        ScreenBox Placed(double px, double py) => new(Math.Clamp(px, area.X + gap, Math.Max(area.X + gap, area.Right - width - gap)),
            Math.Clamp(py, area.Y + gap, Math.Max(area.Y + gap, area.Bottom - height - gap)), width, height);
        double middle = Position.X + body.CenterX - width / 2.0;
        foreach (var (px, py) in new[] { ((double)x, (double)y), (Position.X + body.Right + gap, y), (Position.X + body.X - width - gap, y),
            (middle, Position.Y + body.Bottom + gap), (middle, Position.Y + body.Y - height - gap) })
            if (!Placed(px, py).IntersectsWith(avoid.Target)) return ((int)Math.Round(px), (int)Math.Round(py));
        return (x, y);
    }

    // --test-mode commands (test-command.txt): walk:x,y[,width,height] runs to a point in global points,
    // walk-item:name to a desktop icon and find:query writes find-status.json; walk-status.json follows every walk.
    bool ScreenTestCommand(string command)
    {
        if (command.StartsWith("walk:", StringComparison.Ordinal))
        {
            var numbers = command[5..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var args = JsonSerializer.SerializeToElement(new { x = numbers[0], y = numbers[1], width = numbers.Length > 2 ? numbers[2] : 0, height = numbers.Length > 3 ? numbers[3] : 0, wait = false });
            _ = TestWalk(args);
            return true;
        }
        if (command.StartsWith("walk-item:", StringComparison.Ordinal)) { _ = TestWalk(JsonSerializer.SerializeToElement(new { desktop_item = command[10..], wait = false })); return true; }
        if (command.StartsWith("find:", StringComparison.Ordinal)) { _ = TestFind(command[5..]); return true; }
        return false;
    }

    async Task TestWalk(JsonElement args)
    {
        try { File.WriteAllText("walk-start.json", JsonSerializer.Serialize(await WalkToMcp(args, CancellationToken.None))); }
        catch (Exception error) { File.WriteAllText("walk-start.json", JsonSerializer.Serialize(new { error = error.Message })); }
    }

    async Task TestFind(string query)
    {
        try { File.WriteAllText("find-status.json", JsonSerializer.Serialize(await FindOnScreenMcp(JsonSerializer.SerializeToElement(new { query }), CancellationToken.None))); }
        catch (Exception error) { File.WriteAllText("find-status.json", JsonSerializer.Serialize(new { error = error.Message })); }
    }

    void WriteWalkTestStatus()
    {
        if (!App.Args.Contains("--test-mode")) return;
        try { File.WriteAllText("walk-status.json", JsonSerializer.Serialize(new { walk = WalkSnapshot(), tour = TourSnapshot(), lastWalk, phase, Position.X, Position.Y, DesktopScaling, petBounds = PetPointBounds() })); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
#else
namespace BigBlueFish.Windows;

// walk_to moves the fish on macOS only; the Linux and Safe editions keep the hooks the shared pet code calls.
internal sealed partial class LinuxPetWindow
{
    bool Walking => false;
    bool Touring => false;
    bool ScreenTestCommand(string command) => false;
#if !COMPANION_SAFE
    object? lastWalk => null;
    bool WalkBubbleShown => false;
    object? WalkSnapshot() => null;
    object? TourSnapshot() => null;
    void CancelWalk(string reason) { }
    (int X, int Y) AvoidWalkTarget(int x, int y, int width, int height) => (x, y);
    object CaptureMetadata(string target, string identity, CompanionWindowImage image, IntPtr handle) =>
        new { target, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow };
#endif
}
#endif
