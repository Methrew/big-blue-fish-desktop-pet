// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// Mac-only checks of the Taiwan edition identity. Everything runs in a temporary folder: the login item checks run in a
// child process inside a stand-in .app with HOME redirected there, so the user's own ~/Library/LaunchAgents and
// ~/Library/Application Support are never read or written.
// Usage: MacEditionVerification [work folder] [--safe-app <folder holding a Safe build of BigBlueFish.Mac.dll>]
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using BigBlueFish.Deployment;
using BigBlueFish.Windows;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); passed++; Console.WriteLine("PASS " + label); }

if (args.Length == 2 && args[0] == "--login-item") { LoginItemChecks(args[1]); Console.WriteLine($"RESULT {passed} login item checks passed"); return 0; }
if (!OperatingSystem.IsMacOS()) { Console.Error.WriteLine("macOS only"); return 2; }
string? safeApp = null, folder = null;
for (int i = 0; i < args.Length; i++)
    if (args[i] == "--safe-app" && i + 1 < args.Length) safeApp = Path.GetFullPath(args[++i]);
    else folder ??= args[i];
string root = Path.GetFullPath(folder ?? Path.Combine(Path.GetTempPath(), "bbf-mac-edition-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);

// Release versions: the original's x.y.z plus -tw.N.
static ReleaseVersion V(string text) => ReleaseVersion.TryParse(text, out var version) ? version : throw new Exception("unparsed " + text);
Check(V("v1.1.3-tw.1") > V("1.1.3") && V("1.1.3-tw.2") > V("v1.1.3-tw.1") && V("1.1.4") > V("1.1.3-tw.9") && V("1.1.3-tw.10") > V("1.1.3-tw.9"), "Taiwan revisions order after their base version and before the next one");
Check(!(V("1.1.3-tw.1") > V("1.1.3-tw.1")) && V("1.1.3-tw.1") == V("v1.1.3-tw.1+abc"), "equal versions and build metadata never look like an update");
Check(!ReleaseVersion.TryParse("v1.1.3-beta.1", out _) && !ReleaseVersion.TryParse("latest", out _) && !ReleaseVersion.TryParse("1.1", out _), "other tags are not releases of this edition");
Check(V("v1.1.3-tw.1").ToString() == "1.1.3-tw.1" && V("1.1.3").ToString() == "1.1.3", "asset names use the canonical version");
using (var props = typeof(ReleaseVersion).Assembly.GetManifestResourceStream("Version.props")!)
{
    string version = XDocument.Load(props).Descendants("Version").Single().Value;
    Check(ReleaseVersion.TryParse(version, out var current) && current.ToString() == version, "Version.props " + version + " is a release version the update check understands");
}

// The packaging scripts read the same identity through tools/Get-EditionIdentity.py.
string repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../.."));
if (File.Exists(Path.Combine(repository, "tools/Get-EditionIdentity.py")))
{
    var python = Process.Start(new ProcessStartInfo("/usr/bin/python3") { ArgumentList = { Path.Combine(repository, "tools/Get-EditionIdentity.py"), "--json" }, RedirectStandardOutput = true, UseShellExecute = false })!;
    var identity = JsonNode.Parse(python.StandardOutput.ReadToEnd())!.AsObject(); python.WaitForExit();
    foreach (var field in typeof(CompanionEdition).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Where(f => f.IsLiteral))
        Check((string?)identity[field.Name] == (string)field.GetRawConstantValue()!, "packaging scripts see CompanionEdition." + field.Name);
}

ImportChecks(Path.Combine(root, "import"));
ReadOnlyImageCheck(Path.Combine(root, "image"));

// Login item checks in a stand-in bundle, with HOME redirected into the work folder.
string bundle = Path.Combine(root, "login", "藍色大肥魚 測試.app"), macOS = Path.Combine(bundle, "Contents/MacOS"), home = Path.Combine(root, "login", "home");
Directory.CreateDirectory(macOS); Directory.CreateDirectory(home);
foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(macOS, Path.GetFileName(file)), true);
File.WriteAllText(Path.Combine(macOS, "BigBlueFish.Mac"), "stand-in executable");
var child = new ProcessStartInfo(Path.Combine(macOS, Path.GetFileName(Environment.ProcessPath!))) { UseShellExecute = false, ArgumentList = { "--login-item", home } };
child.Environment["HOME"] = home;
using (var process = Process.Start(child)!) { process.WaitForExit(); Check(process.ExitCode == 0, "login item checks pass inside a stand-in app bundle"); }

if (safeApp != null) SafePreferenceChecks(safeApp, Path.Combine(root, "safe"));
else Console.WriteLine("SKIP Safe edition preferences (pass --safe-app <folder of a Safe BigBlueFish.Mac build>)");
Console.WriteLine($"RESULT {passed} Mac edition checks passed in {root}");
return 0;

static string SourcePath([CallerFilePath] string path = "") => path;

// A DMG mounted the way Finder mounts a compressed image is read-only (EROFS); a writable folder is not.
void ReadOnlyImageCheck(string folder)
{
    string content = Path.Combine(folder, "content/藍色大肥魚.app/Contents/MacOS"), image = Path.Combine(folder, "test.dmg"), mount = Path.Combine(folder, "mount");
    Directory.CreateDirectory(content); Directory.CreateDirectory(mount); File.WriteAllText(Path.Combine(content, "BigBlueFish.Mac"), "");
    if (Run("/usr/bin/hdiutil", "create", "-quiet", "-volname", "BigBlueFish", "-srcfolder", Path.Combine(folder, "content"), "-format", "UDZO", image) != 0
        || Run("/usr/bin/hdiutil", "attach", "-quiet", "-readonly", "-nobrowse", "-mountpoint", mount, image) != 0)
    { Console.WriteLine("SKIP read-only disk image (hdiutil unavailable here)"); return; }
    try { Check(MacLoginItem.ReadOnly(Path.Combine(mount, "藍色大肥魚.app/Contents/MacOS/")) && !MacLoginItem.ReadOnly(folder), "an app inside a mounted DMG is told apart from a writable install"); }
    finally { Run("/usr/bin/hdiutil", "detach", "-quiet", "-force", mount); }
}

void ImportChecks(string folder)
{
    string support = Path.Combine(folder, "Application Support"), source = Path.Combine(support, CompanionEdition.OriginalMacDataFolder), target = Path.Combine(support, CompanionEdition.MacDataFolder);
    string precious = Path.Combine(folder, "outside");
    Directory.CreateDirectory(precious); File.WriteAllText(Path.Combine(precious, "keep.txt"), "user file outside the data folder");
    Directory.CreateDirectory(Path.Combine(source, "models/sub")); Directory.CreateDirectory(Path.Combine(source, "agent/adapters/claude/node_modules/.bin")); Directory.CreateDirectory(Path.Combine(source, "cache"));
    File.WriteAllText(Path.Combine(source, "preferences.json"), "{\"WeatherRegionCode\":\"340104\",\"AgentEnabled\":true}");
    File.WriteAllText(Path.Combine(source, "preferences.json.bak"), "{\"WeatherRegionCode\":\"340104\"}");
    File.WriteAllText(Path.Combine(source, "model-selection.json"), "{\"Source\":\"huggingface\",\"FileName\":\"model.gguf\"}");
    File.WriteAllBytes(Path.Combine(source, "models/model.gguf"), RandomNumberGenerator.GetBytes(1 << 20));
    File.WriteAllText(Path.Combine(source, "models/sub/next.gguf.partial"), "partial");
    File.WriteAllText(Path.Combine(source, "agent/workspaces.json"), "{}");
    File.WriteAllText(Path.Combine(source, "agent/adapters/claude/node_modules/cli.js"), "cli");
    File.CreateSymbolicLink(Path.Combine(source, "agent/adapters/claude/node_modules/.bin/cli"), "../cli.js");
    Directory.CreateSymbolicLink(Path.Combine(source, "agent/outside"), precious);
    File.WriteAllText(Path.Combine(source, "cache/weather.json"), "{}");
    var before = Snapshot(source);

    // A second copy starting at the same moment waits for the first one's import instead of racing it.
    string gatePath = Path.Combine(support, "." + CompanionEdition.MacDataFolder + ".import-lock");
    var gate = new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var waiting = Task.Run(() => MacDataImport.Run(target, source));
    Thread.Sleep(700);
    Check(!waiting.IsCompleted && !Directory.Exists(target), "import waits while another copy holds the import lock");
    gate.Dispose(); waiting.Wait(TimeSpan.FromSeconds(30));
    Check(waiting.IsCompletedSuccessfully && Directory.Exists(target), "import runs once the other copy is done");

    Check(File.ReadAllText(Path.Combine(target, "preferences.json")) == File.ReadAllText(Path.Combine(source, "preferences.json"))
        && File.Exists(Path.Combine(target, "preferences.json.bak")), "settings and their backup are copied byte for byte");
    Check(File.ReadAllBytes(Path.Combine(target, "models/model.gguf")).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "models/model.gguf")))
        && File.Exists(Path.Combine(target, "models/sub/next.gguf.partial")), "models are cloned with their whole folder");
    Check(File.Exists(Path.Combine(target, "model-selection.json")), "the model choice comes along with its models");
    Check(new FileInfo(Path.Combine(target, "agent/adapters/claude/node_modules/.bin/cli")).LinkTarget == "../cli.js"
        && new DirectoryInfo(Path.Combine(target, "agent/outside")).LinkTarget == precious && File.Exists(Path.Combine(target, "agent/workspaces.json")), "Agent data is copied with its links kept as links");
    Check(!Directory.Exists(Path.Combine(target, "cache")), "caches are not imported");
    string note = File.ReadAllText(Path.Combine(target, MacDataImport.NoteName));
    Check(note.Contains("原版的資料沒有被移動、修改或刪除") && note.Contains("APFS 複本"), "the import note says what was copied, in Traditional Chinese");
    Check(Snapshot(source).SequenceEqual(before), "the original's folder is left exactly as it was");
    Check(File.Exists(Path.Combine(precious, "keep.txt")), "files reached through a link are untouched");
    Check(!Directory.Exists(Path.Combine(support, "." + CompanionEdition.MacDataFolder + ".importing")) && !File.Exists(gatePath), "no staging folder or lock is left behind");

    File.WriteAllText(Path.Combine(source, "preferences.json"), "{\"WeatherRegionCode\":\"110101\"}");
    MacDataImport.Run(target, source);
    Check(File.ReadAllText(Path.Combine(target, "preferences.json")).Contains("340104"), "the import happens only once: an existing folder is never overwritten");

    // An interrupted earlier attempt: its staging folder is replaced; a link in its place is removed as a link.
    Directory.Delete(target, true);
    Check(File.Exists(Path.Combine(precious, "keep.txt")), "deleting an imported folder never follows its links");
    string staging = Path.Combine(support, "." + CompanionEdition.MacDataFolder + ".importing");
    Directory.CreateSymbolicLink(staging, precious);
    MacDataImport.Run(target, source);
    Check(Directory.Exists(target) && File.Exists(Path.Combine(precious, "keep.txt")) && Directory.GetFileSystemEntries(precious).Length == 1, "a link left as the staging folder is never followed");
    Directory.Delete(target, true);
    Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(staging, "half-copied"), "");
    MacDataImport.Run(target, source);
    Check(Directory.Exists(target) && !File.Exists(Path.Combine(target, "half-copied")) && !Directory.Exists(staging), "a half-finished staging folder is redone from the start");

    string empty = Path.Combine(folder, "fresh", CompanionEdition.MacDataFolder);
    MacDataImport.Run(empty, Path.Combine(folder, "fresh", CompanionEdition.OriginalMacDataFolder));
    Check(!Directory.Exists(empty), "without the original's folder nothing is created ahead of the app");
    string symlinkedSource = Path.Combine(folder, "linked");
    Directory.CreateDirectory(symlinkedSource); File.CreateSymbolicLink(Path.Combine(symlinkedSource, "preferences.json"), Path.Combine(precious, "keep.txt"));
    string linkedTarget = Path.Combine(folder, "linked-target");
    MacDataImport.Run(linkedTarget, symlinkedSource);
    Check(Directory.Exists(linkedTarget) && !File.Exists(Path.Combine(linkedTarget, "preferences.json")), "a settings file that is a link is not imported");
}

static string[] Snapshot(string folder) => new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
    .Select(x => Path.GetRelativePath(folder, x.FullName) + " " + (x.LinkTarget != null ? "-> " + x.LinkTarget : x is FileInfo f ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.FullName))) + " " + f.LastWriteTimeUtc.Ticks : "dir"))
    .Order(StringComparer.Ordinal).ToArray();

void LoginItemChecks(string home)
{
    // Refuse to run anywhere but the redirected HOME of the stand-in bundle.
    if (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) != home || !home.Contains("/login/home") || !AppContext.BaseDirectory.Contains(".app/Contents/MacOS/"))
        throw new Exception("login item checks must run inside the stand-in bundle with HOME redirected");
    string agents = Path.Combine(home, "Library/LaunchAgents"), executable = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mac");
    string own = Path.Combine(agents, CompanionEdition.MacBundleId + ".plist"), legacy = Path.Combine(agents, "io.github.bai-cute.bigbluefish.plist");
    Check(MacLoginItem.PlistPath == own && MacLoginItem.Label == CompanionEdition.MacBundleId, "the login item is labelled with the edition's bundle identifier");
    Check(MacLoginItem.Unsupported() == null && !MacLoginItem.Enabled, "a normal install can register; nothing is registered yet");
    Check(MacLoginItem.Unsupported("/private/var/folders/ab/cd/T/AppTranslocation/0A1B/d/藍色大肥魚.app/Contents/MacOS/") is string moved && moved.Contains("應用程式"), "a translocated copy is refused with a reason");

    MacLoginItem.Set(true);
    Check(File.Exists(own) && MacLoginItem.ProgramPath(own) == executable && MacLoginItem.Enabled, "turning it on writes a LaunchAgent that starts this copy");
    Check(Run("/usr/bin/plutil", "-lint", own) == 0 && Run("/usr/bin/plutil", "-extract", "Label", "raw", own) == 0, "the LaunchAgent is a valid property list");
    Run("/usr/bin/plutil", "-convert", "binary1", own);
    Check(MacLoginItem.ProgramPath(own) == executable && MacLoginItem.Enabled, "a binary property list is still understood");
    MacLoginItem.Set(false);
    Check(!File.Exists(own) && !MacLoginItem.Enabled, "turning it off removes only this edition's LaunchAgent");

    WritePlist(legacy, "io.github.bai-cute.bigbluefish", executable);
    MacLoginItem.Refresh();
    Check(!File.Exists(legacy) && MacLoginItem.Enabled, "an old LaunchAgent that starts this same app is taken over");
    string original = "/Applications/蓝色大肥鱼.app/Contents/MacOS/BigBlueFish.Mac";
    WritePlist(legacy, "io.github.bai-cute.bigbluefish", original); string kept = File.ReadAllText(legacy);
    MacLoginItem.Refresh();
    Check(File.Exists(legacy) && File.ReadAllText(legacy) == kept, "the original app's own LaunchAgent is left alone");
    File.Delete(legacy);

    WritePlist(own, CompanionEdition.MacBundleId, "/nonexistent/Old.app/Contents/MacOS/BigBlueFish.Mac");
    MacLoginItem.Refresh();
    Check(MacLoginItem.ProgramPath(own) == executable, "a LaunchAgent naming a moved app is pointed at this copy");
    WritePlist(own, CompanionEdition.MacBundleId, "/bin/ls");
    MacLoginItem.Refresh();
    Check(MacLoginItem.ProgramPath(own) == "/bin/ls" && !MacLoginItem.Enabled, "a LaunchAgent for another existing copy is not taken over");
}

static void WritePlist(string path, string label, string program)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\"><dict><key>Label</key><string>"
        + label + "</string><key>ProgramArguments</key><array><string>" + System.Security.SecurityElement.Escape(program) + "</string></array><key>RunAtLoad</key><true/></dict></plist>\n");
}

static int Run(string file, params string[] arguments)
{
    var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string item in arguments) start.ArgumentList.Add(item);
    using var process = Process.Start(start)!; process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit(); return process.ExitCode;
}

// The Safe edition shares the data folder with the Standard edition: loading and saving must keep the Standard Agent switches.
void SafePreferenceChecks(string app, string folder)
{
    Directory.CreateDirectory(folder);
    var context = new AssemblyLoadContext("safe-edition", true);
    context.Resolving += (loader, name) => File.Exists(Path.Combine(app, name.Name + ".dll")) ? loader.LoadFromAssemblyPath(Path.Combine(app, name.Name + ".dll")) : null;
    var assembly = context.LoadFromAssemblyPath(Path.Combine(app, "BigBlueFish.Mac.dll"));
    Check(!assembly.GetReferencedAssemblies().Any(a => a.Name is "BigBlueFish.Agent" or "BigBlueFish.Control"), "the given build is the Safe edition");
    var type = assembly.GetType("BigBlueFish.Windows.CompanionPreferences")!;
    string previous = Environment.CurrentDirectory; Environment.CurrentDirectory = folder;
    try
    {
        File.WriteAllText("preferences.json", "{\"AgentEnabled\":true,\"PuppetMode\":true,\"AgentDailySpeech\":true,\"McpEnabled\":true,\"McpChatEnabled\":true,\"AgentHarnessId\":\"claude\",\"Scale\":1}");
        object preferences = type.GetMethod("Read", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null)!;
        bool Flag(string name) => (bool)type.GetProperty(name)!.GetValue(preferences)!;
        Check(!Flag("AgentEnabled") && !Flag("PuppetMode") && !Flag("AgentDailySpeech") && !Flag("McpEnabled") && !Flag("McpChatEnabled"), "the Safe edition runs with every Agent switch off");
        type.GetProperty("Scale")!.SetValue(preferences, 1.5);
        type.GetMethod("Save")!.Invoke(preferences, null);
        var saved = JsonNode.Parse(File.ReadAllText("preferences.json"))!.AsObject();
        Check((bool)saved["AgentEnabled"]! && (bool)saved["PuppetMode"]! && (bool)saved["AgentDailySpeech"]! && (bool)saved["McpEnabled"]! && (bool)saved["McpChatEnabled"]!, "saving in the Safe edition keeps the Standard edition's Agent and MCP interface switches");
        Check((double)saved["Scale"]! == 1.5 && (string?)saved["AgentHarnessId"] == "claude", "the Safe edition's own changes are still saved");
    }
    finally { Environment.CurrentDirectory = previous; context.Unload(); }
}
