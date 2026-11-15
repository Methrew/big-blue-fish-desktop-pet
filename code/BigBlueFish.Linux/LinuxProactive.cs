// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BigBlueFish.Windows;

// The Avalonia port of the Windows CompanionProactive.cs (taiwan-edition by acer1204), shared by the Linux and
// macOS apps: CompanionReminders hears what the pet sees each second and speaks when the bubble is free, the user's
// return after a break goes to MCP clients as user_returned, and get_context reads the same picture. Every timer
// here runs on CompanionNative.SteadyMilliseconds, which keeps counting while the computer sleeps.
internal sealed partial class LinuxPetWindow
{
    const string ReminderMemoryPath = "cache/reminders.json";
    // An agent that spoke this recently may still be in a conversation (the Hermes bridge refreshes its
    // progress bubble at least every 15 seconds, and a reply wants reading).
    static readonly TimeSpan AgentQuiet = TimeSpan.FromSeconds(60);
    CompanionReminders? reminders;
    long nextObservation, nextReminder, nextMemorySave, observedAt;
    bool reminderMemoryHooked;
    // SayExternal's last bubble (UTC).
    DateTime externalSaidAt = DateTime.MinValue;
    // --test-mode only: a shifted wall clock and a fixed idle time, so a test can visit lunchtime or midnight.
    TimeSpan reminderClockShift;
    double? reminderIdleOverride;
    DateTime ReminderNow => DateTime.Now + reminderClockShift;
    double ReminderIdle => reminderIdleOverride ?? CompanionNative.IdleSeconds;
    CompanionReminders Reminders => reminders ??= new(ReadReminderMemory());
    CompanionReminders.Settings ReminderSettings => new(Preferences.SitReminderMinutes, Preferences.MealReminder,
        Preferences.BreakfastHour, Preferences.LunchHour, Preferences.DinnerHour, Preferences.LateReminder ? Preferences.LateReminderHour : -1, Preferences.BatteryReminder, Preferences.RainReminder);

    // Every tick, hidden or not: a stretch of use goes on behind a full-screen game too.
    void ObserveActivity()
    {
        if (!reminderMemoryHooked) { reminderMemoryHooked = true; Closed += (_, _) => SaveReminderMemory(); }
        long ticks = CompanionNative.SteadyMilliseconds;
        if (ticks < nextObservation) return;
        nextObservation = ticks + 1000;
        double idle = ReminderIdle;
        // A full-screen film, or a full-screen game played with a controller: still keyboard and mouse then mean
        // watching, not away. Not while the screen is locked.
        bool watching = idle >= 60 && CompanionNative.IsFullscreen() && !CompanionNative.Locked;
        Reminders.Observe(ReminderNow, idle, observedAt == 0 ? null : TimeSpan.FromMilliseconds(ticks - observedAt), watching);
        observedAt = ticks;
        // MCP clients hear when the user is back after a break (the Hermes bridge greets and tells what happened
        // meanwhile). Only MCP: the built-in Agent is not woken up for it, a return is no reason to pay for a turn.
        if (Reminders.TakeReturn() is { Break: { } pause } back)
            PublishInteraction("user_returned", new { breakMinutes = (int)Math.Round(pause.TotalMinutes), since = ReminderIso(back.At - pause), at = ReminderIso(back.At), watching = back.Watching });
        if (App.Args.Contains("--test-mode")) WriteReminderTestStatus(idle);
        // The stretch under way survives a restart of the fish (an update, a crash) shorter than a break.
        if (ticks < nextMemorySave) return;
        nextMemorySave = ticks + 60_000;
        SaveReminderMemory();
    }

    // From Tick once the fish is on screen. A reminder waits while the fish prepares or still shows something of
    // its own, while the built-in Agent prepares a turn it has not spoken in yet, while an agent spoke within the
    // last minute, during a drag or a click, with the settings, a confirmation or the input window open, and while
    // any program is full screen, even one the fish stays visible over. True when a reminder took the bubble.
    bool Remind(DateTime utc)
    {
        long ticks = CompanionNative.SteadyMilliseconds;
        if (ticks < nextReminder) return false;
        nextReminder = ticks + 5000;
        if (Puppet || settings != null || consentOpen || modelInstalling || ReminderInputOpen || dragging || Walking || Touring || generating || agentWeatherPending || ReminderAgentPreparing
            || phase is "Drag" or "Click" or "Startup" or "Exit"
            || utc >= externalSaidAt && utc - externalSaidAt < AgentQuiet || bubbleWindow.IsVisible && (typing || bubbleUntil != DateTime.MaxValue)
            || CompanionNative.IsFullscreen()) return false;
        var now = ReminderNow;
        var reminder = Reminders.Next(new(now, ReminderIdle, CompanionNative.OnAC, CompanionNative.BatteryPercent,
            Preferences.PublicInfo ? brain.CachedWeather : ""), ReminderSettings, observe: false);
        if (reminder == null) return false;
        ShowText(reminder.Text, false);
        Reminders.Said(reminder, now);
        SaveReminderMemory();
        // The fish's own chatter does not replace the reminder right away.
        var quiet = utc.AddMinutes(2);
        if (nextSpeech < quiet) nextSpeech = quiet;
        if (reminder.Action != null && phase == "Idle" && moveRule == null && policy.Rules.FirstOrDefault(r => r.Id == reminder.Action) is { } rule)
            Event(rule);
        return true;
    }

#if COMPANION_SAFE
    bool ReminderInputOpen => false;
    bool ReminderAgentPreparing => false;
#else
    bool ReminderInputOpen => mcpInput?.IsVisible == true;
    bool ReminderAgentPreparing => agent?.AwaitingSpeech == true;
#endif

    static string ReminderIso(DateTime local) => new DateTimeOffset(local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    static Dictionary<string, DateTime>? ReadReminderMemory()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(ReminderMemoryPath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    void SaveReminderMemory()
    {
        if (reminders == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReminderMemoryPath)!);
            File.WriteAllText(ReminderMemoryPath + ".tmp", JsonSerializer.Serialize(reminders.Remembered));
            File.Move(ReminderMemoryPath + ".tmp", ReminderMemoryPath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // --test-mode commands (test-command.txt): reminder-clock:2026-10-28T12:05:00, reminder-idle:30,
    // battery-percent:15, fullscreen:on (or off) and power:battery (or ac), each undone with the value real.
    bool ReminderTestCommand(string command)
    {
        string? Value(string prefix) => command.StartsWith(prefix, StringComparison.Ordinal) ? command[prefix.Length..] : null;
        if (Value("reminder-clock:") is { } clock)
        {
            if (clock == "real") reminderClockShift = TimeSpan.Zero;
            else if (DateTime.TryParse(clock, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) reminderClockShift = at - DateTime.Now;
            nextReminder = 0;
            // A test visiting another hour moves the clock on purpose: a gap in time, not on the steady clock.
            observedAt = 0;
            return true;
        }
        if (Value("reminder-idle:") is { } idle)
        {
            reminderIdleOverride = double.TryParse(idle, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : null;
            nextReminder = 0;
            return true;
        }
        if (Value("battery-percent:") is { } percent)
        {
            CompanionNative.TestBatteryOverride = int.TryParse(percent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? Math.Clamp(level, 0, 100) : null;
            nextReminder = 0;
            return true;
        }
        if (Value("fullscreen:") is { } full)
        {
            CompanionNative.TestFullscreenOverride = full switch { "on" => true, "off" => false, _ => null };
            return true;
        }
        if (Value("power:") is { } power)
        {
            CompanionNative.TestPowerOverride = power switch { "ac" => true, "battery" => false, _ => null };
            nextReminder = 0;
            return true;
        }
        return false;
    }

    // reminder-status.json, beside test-status.json, once a second in --test-mode.
    void WriteReminderTestStatus(double idle)
    {
        var now = ReminderNow;
        try
        {
            File.WriteAllText("reminder-status.json", JsonSerializer.Serialize(new
            {
                localTime = ReminderIso(now), idleSeconds = Math.Round(idle, 1), systemIdleSeconds = Math.Round(CompanionNative.IdleSeconds, 1),
                activeMinutes = ReminderActiveMinutes, lastBreakMinutes = ReminderLastBreakMinutes,
                onAC = CompanionNative.OnAC, battery = CompanionNative.BatteryPercent, locked = CompanionNative.Locked, fullscreen = CompanionNative.IsFullscreen(),
                steadyMilliseconds = CompanionNative.SteadyMilliseconds, tickCount = Environment.TickCount64,
                lastReminder = Reminders.Last is { } last ? new { kind = last.Reminder.Kind, text = last.Reminder.Text, at = ReminderIso(last.At) } : null
            }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

#if !COMPANION_SAFE
    // get_context: what the fish knows about the moment, without a screenshot or a network request.
    object CurrentContext(IReadOnlyList<(string Title, string Folder)> folders, string? foldersNote)
    {
        var now = ReminderNow;
        double idle = ReminderIdle;
        var active = Reminders.ActiveFor(now);
        bool onAC = CompanionNative.OnAC;
        int? battery = CompanionNative.BatteryPercent;
        string weather = Preferences.PublicInfo ? brain.CachedWeather : "";
        var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
        return new
        {
            summary = CompanionReminders.Summary(now, idle, active, onAC, battery),
            localTime = ReminderIso(now),
            weekday = CompanionReminders.Weekday(now),
            idleSeconds = Math.Round(idle),
            activeMinutes = active is { } stretch ? (int?)stretch.TotalMinutes : null,
            lastBreakMinutes = ReminderLastBreakMinutes,
            fullscreen = CompanionNative.IsFullscreen(),
            power = new { onAC, batteryPercent = battery },
            foreground = Preferences.ForegroundEnabled ? CompanionNative.ForegroundApp(handle) : null,
            weather = weather.Length > 0 ? weather : null,
            openFolders = folders.Select((folder, i) => new { folder = folder.Folder, window = folder.Title, front = i == 0 }).ToArray(),
            openFoldersNote = foldersNote,
            lastReminder = Reminders.Last is { } last ? new { kind = last.Reminder.Kind, text = last.Reminder.Text, at = ReminderIso(last.At) } : null
        };
    }

#endif
    // This stretch of use and the last break in whole minutes, as get_context and get_settings' state report them.
    int? ReminderActiveMinutes => Reminders.ActiveFor(ReminderNow) is { } active ? (int)active.TotalMinutes : null;
    int? ReminderLastBreakMinutes => Reminders.LastBreak is { } pause ? (int)Math.Round(pause.TotalMinutes) : null;

    // The 「主動提醒」 block of the 「動作與作息」 settings page, under the meal times.
    void BuildReminderSettings(StackPanel body)
    {
        body.Children.Add(new TextBlock { Text = "主動提醒", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 20, 0, 0) });
#if COMPANION_SAFE
        body.Children.Add(Paragraph("魚魚自己會提醒，不需要模型。你不在電腦前、有程式全螢幕或正在拖曳魚魚時，提醒會先等一下。"));
#else
        body.Children.Add(Paragraph("魚魚自己會提醒，不需要模型或 Agent。你不在電腦前、有程式全螢幕、正在拖曳魚魚或 Agent 剛說過話時，提醒會先等一下。"));
#endif
        void Choice(string label, string[] choices, int selected, Action<int> set)
        {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) });
            var combo = new ComboBox { ItemsSource = choices, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Save(); } };
            body.Children.Add(combo);
        }
        void Switch(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4) };
            box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Save(); };
            body.Children.Add(box);
        }
        var sit = CompanionPreferences.SitReminderChoices;
        Choice("久坐提醒", sit.Select(minutes => minutes == 0 ? "關閉" : $"連續使用 {minutes} 分鐘後").ToArray(), Math.Max(0, Array.IndexOf(sit, Preferences.SitReminderMinutes)),
            index => Preferences.SitReminderMinutes = sit[index]);
        Switch("用餐提醒（三餐時間在上方設定）", Preferences.MealReminder, value => Preferences.MealReminder = value);
        var late = CompanionPreferences.LateReminderChoices;
        Choice("深夜提醒", ["關閉", .. late.Select(hour => CompanionReminders.Hour(hour) + "起")],
            Preferences.LateReminder ? 1 + Math.Max(0, Array.IndexOf(late, Preferences.LateReminderHour)) : 0,
            index => { Preferences.LateReminder = index > 0; if (index > 0) Preferences.LateReminderHour = late[index - 1]; });
        Switch("低電量提醒（沒插電且電量剩 20%、10% 時）", Preferences.BatteryReminder, value => Preferences.BatteryReminder = value);
        if (CompanionNative.BatteryPercent == null)
            body.Children.Add(Paragraph(OperatingSystem.IsMacOS() ? "這台 Mac 沒有內建電池，不會跳出低電量提醒。" : "這台電腦沒有偵測到電池，不會跳出低電量提醒。"));
        Switch("下雨提醒（需開啟連網取得天氣，6 小時內降雨機率 60% 以上時）", Preferences.RainReminder, value => Preferences.RainReminder = value);
    }
}
