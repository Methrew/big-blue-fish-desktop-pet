// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// macOS edition logic that needs no window, permission or bundled runtime: inference device reporting, the
// compatibility-only animation check, the login-shell PATH import under slow or hanging shells, the foreground
// window rules and the Terminal login script. Usage: dotnet MacLogicVerification.dll [repository root]
// (found from the program or the current folder when left out).
using System.Diagnostics;
using System.Reflection;
using BigBlueFish.Windows;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); checks++; Console.WriteLine("PASS " + label); }
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot(AppContext.BaseDirectory) ?? FindRoot(Environment.CurrentDirectory)
    ?? throw new DirectoryNotFoundException("repository root (Version.props) not found; pass it as the first argument");
var scratch = Directory.CreateTempSubdirectory("bbf-mac-logic-").FullName;
try
{
    InferenceDevices();
    AnimationPolicy();
    WindowRules();
    if (!OperatingSystem.IsWindows())
    {
        await LoginPath();
        await TerminalScript();
    }
    Console.WriteLine($"RESULT {checks} macOS logic checks passed");
}
finally { Directory.Delete(scratch, true); }

void InferenceDevices()
{
    // Real llama.cpp b10809 lines on an Apple M4.
    var cpu = new CompanionInferenceDevices();
    cpu.Observe("0.00.334.763 I load_tensors:   CPU_Mapped model buffer size =  2572.86 MiB");
    cpu.Observe("0.00.334.763 I load_tensors:   CPU_REPACK model buffer size =  2569.19 MiB");
    Check(cpu.Describe(true) == "CPU", "CPU fallback with CPU_REPACK weights reads CPU");
    var metal = new CompanionInferenceDevices();
    metal.Observe("0.00.338.214 I llama_prepare_model_devices: using device MTL0 (Apple M4) (unknown id) - 12123 MiB free");
    metal.Observe("0.00.794.738 I load_tensors:   CPU_Mapped model buffer size =   497.31 MiB");
    metal.Observe("0.00.794.739 I load_tensors:  MTL0_Mapped model buffer size =  2572.86 MiB");
    Check(metal.Describe(true) == "GPU：Apple M4（MTL0）", "Metal weights in MTL0_Mapped read as the named Apple GPU");
    var host = new CompanionInferenceDevices();
    host.Observe("load_tensors: MTL0_Host model buffer size = 300.00 MiB");
    Check(!host.Describe(true).StartsWith("GPU"), "host buffers still do not count as GPU use");
}

void AnimationPolicy()
{
    // The macOS bundle has animations.json and compat/ only (COMPANION_MAC is defined for this project).
    string assets = Path.Combine(scratch, "fish"), compat = Path.Combine(assets, "compat");
    Directory.CreateDirectory(compat);
    File.Copy(Path.Combine(root, "code/BigBlueFish.Windows/assets/fish/animations.json"), Path.Combine(assets, "animations.json"));
    foreach (var file in Directory.GetFiles(Path.Combine(root, "code/BigBlueFish.Windows/assets/fish/compat"), "*.webm"))
        File.WriteAllBytes(Path.Combine(compat, Path.GetFileName(file)), []);
    var policy = CompanionAnimationPolicy.Load(assets);
    Check(policy.Rules.Count == 106 && Directory.GetFiles(assets, "*.webm").Length == 0, "policy loads with compatibility clips only");
    var missing = Path.Combine(compat, policy.Rules[0].File); File.Delete(missing);
    try { CompanionAnimationPolicy.Load(assets); Check(false, "a missing compatibility clip is reported"); }
    catch (FileNotFoundException error) { Check(error.FileName == policy.Rules[0].File, "a missing compatibility clip is reported"); }
}

void WindowRules()
{
    const int self = 100, editor = 200, menuBar = 300, finder = 400;
    var windows = new List<MacWindow>
    {
        new(11, editor, 0, 0, 30, 20, 1),       // a tiny helper window in front
        new(12, editor, 0, 25, 1200, 800, 0),   // fully transparent
        new(13, editor, 10, 25, 1200, 800, 1),  // the document window
        new(21, self, 900, 600, 498, 280, 1),
        new(41, finder, 0, 0, 800, 500, 1),
    };
    Check(MacWindowRules.Foreground(windows, editor, self, "com.example.editor") == (13, false), "foreground skips tiny and fully transparent windows");
    Check(MacWindowRules.Foreground(windows, menuBar, self, "com.example.menubar") == (0, false), "an app without a window is not the desktop");
    Check(MacWindowRules.Foreground(windows.Where(w => w.Pid != finder), finder, self, MacWindowRules.FinderBundle) == (0, true), "Finder without a window is the desktop");
    Check(MacWindowRules.Foreground(windows, finder, self, MacWindowRules.FinderBundle) == (41, false), "a Finder window is a window");
    Check(MacWindowRules.Foreground(windows, self, self, null) == (0, false), "the pet itself is never the foreground");
    var display = new[] { (0d, 0d, 1512d, 982d), (1512d, 0d, 2560d, 1440d) };
    Check(MacWindowRules.Covers(new(1, editor, 1512, 0, 2560, 1440, 1), display), "a window covering the second display is full screen");
    Check(MacWindowRules.Covers(new(1, editor, -1, -1, 1514, 984, 1), display), "a window a little larger than the display is full screen");
    Check(!MacWindowRules.Covers(new(1, editor, 0, 25, 1512, 957, 1), display), "a maximized window below the menu bar is not full screen");
}

async Task LoginPath()
{
    string Shell(string name, string body)
    {
        string path = Path.Combine(scratch, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    var limit = TimeSpan.FromSeconds(2); var drain = TimeSpan.FromMilliseconds(500);
    async Task<(string? Path, string State, double Seconds)> Run(string shell)
    {
        var clock = Stopwatch.StartNew(); var (path, state) = await MacLoginPath.Read(shell, limit, drain);
        return (path, state, clock.Elapsed.TotalSeconds);
    }
    const string marker = "printf '\\n<bbf-path>%s</bbf-path>\\n' '/opt/fake/bin:/usr/bin'";
    var plain = await Run(Shell("plain", "echo profile noise; " + marker));
    Check(plain is { Path: "/opt/fake/bin:/usr/bin", State: "success" } && plain.Seconds < 1.5, $"login PATH is read from the marker line ({plain.Seconds:0.00} s)");
    var held = await Run(Shell("held", marker + "; sleep 5 &"));
    Check(held is { Path: "/opt/fake/bin:/usr/bin", State: "success" } && held.Seconds < 1.5, $"a background job holding the pipe does not delay the import ({held.Seconds:0.00} s)");
    var unterminated = await Run(Shell("unterminated", "printf '\\n<bbf-path>/a:/b</bbf-path>'"));
    Check(unterminated.Path == "/a:/b", "a marker without a final newline is read at the end of output");
    string pidFile = Path.Combine(scratch, "hang.pid");
    var hang = await Run(Shell("hang", "echo $$ > '" + pidFile + "'; sleep 30"));
    Check(hang is { Path: null, State: "timeout" } && hang.Seconds is >= 1.9 and < 3.5, $"a hanging profile is abandoned at the time limit ({hang.Seconds:0.00} s)");
    await Task.Delay(200);
    int shellPid = int.Parse(File.ReadAllText(pidFile).Trim());
    Check(!Alive(shellPid), "the hanging shell is killed");
    var silent = await Run(Shell("silent", "echo no marker; sleep 5 &"));
    Check(silent is { Path: null, State: "unavailable" } && silent.Seconds < 1.5, $"a shell that exits without the marker gives up after the drain time ({silent.Seconds:0.00} s)");
    Check(MacLoginPath.Merge("/opt/homebrew/bin:/usr/bin", "/usr/bin:/bin:/usr/sbin:relative::/sbin", ["/Users/x/.bun/bin", "/bin"])
        == "/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:/Users/x/.bun/bin", "login PATH order first, then the missing entries, then fallback folders");
    Check(MacLoginPath.Merge(null, "/usr/bin:/bin", []) == "/usr/bin:/bin", "a failed import keeps the current PATH");
    // The real login shell, for information only: its time depends on this user's profile.
    string real = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } configured && File.Exists(configured) ? configured : "/bin/zsh";
    var clock = Stopwatch.StartNew(); var (realPath, realState) = await MacLoginPath.Read(real, MacLoginPath.Limit, MacLoginPath.Drain);
    Console.WriteLine($"INFO {real} -ilc: {realState} in {clock.Elapsed.TotalSeconds:0.00} s, {realPath?.Split(':').Length ?? 0} PATH entries");
}

async Task TerminalScript()
{
    var login = typeof(BigBlueFish.Agent.HarnessCatalog).Assembly.GetType("BigBlueFish.Agent.AgentTerminalLogin")!
        .GetMethod("LoginScript", BindingFlags.Static | BindingFlags.NonPublic)!;
    var method = new BigBlueFish.Agent.AgentAuthenticationMethod("terminal", "Terminal", "", "terminal", [], new Dictionary<string, string>());
    async Task<(string? Status, bool Leftover)> Run(string name, string command, string? signal = null)
    {
        string folder = Path.Combine(scratch, name); Directory.CreateDirectory(folder);
        string status = Path.Combine(folder, "status"), script = Path.Combine(folder, "login.command");
        var configured = new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        File.WriteAllText(script, (string)login.Invoke(null, [configured, method, status])!);
        using var process = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { script }, UseShellExecute = false })!;
        if (signal != null) { await Task.Delay(300); using var kill = Process.Start("/bin/kill", ["-" + signal, process.Id.ToString()])!; await kill.WaitForExitAsync(); }
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return (File.Exists(status) ? File.ReadAllText(status).Trim() : null, File.Exists(status + ".tmp"));
    }
    Check(await Run("ok", "exit 0") == ("0", false), "a finished login reports 0");
    Check(await Run("failed", "exit 3") == ("3", false), "a failed login reports its exit code");
    // Ctrl+C in Terminal reaches the whole foreground group: the login command and the script.
    Check(await Run("interrupt", "kill -INT $PPID; kill -INT $$") == ("130", false), "Ctrl+C is reported as 130 instead of leaving no status");
    Check(await Run("hangup", "sleep 1", "HUP") == ("129", false), "a closed Terminal window (HUP) is reported as 129");
    Check(await Run("terminate", "sleep 1", "TERM") == ("143", false), "TERM is reported as 143");
}

static bool Alive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }

static string? FindRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Version.props"))) return directory.FullName;
    return null;
}
