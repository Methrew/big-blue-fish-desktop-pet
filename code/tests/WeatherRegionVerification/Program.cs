// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BigBlueFish.Control;
using BigBlueFish.Windows;

// Regions, display names, MCP region codes, saved locations, macOS secret storage, then the shared weather cache checks
// (tests/RunnerVerification/WeatherCacheVerification.cs). Pass "live" to add real Open-Meteo requests.
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("FAIL " + name + " (no " + typeof(T).Name + ")");
}
var work = Path.Combine(Path.GetTempPath(), "bbf-weather-region-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
var start = Environment.CurrentDirectory;
try
{
    // --- Country order: exactly the original (China, then the rest by English name), plus 臺灣 Taiwan at its English name.
    using var resource = typeof(CompanionRegions).Assembly.GetManifestResourceStream("BigBlueFish.Windows.assets.regions.world-countries.json")!;
    var world = JsonDocument.Parse(resource).RootElement.EnumerateArray()
        .Select(c => (Code: c.GetProperty("Code").GetString()!, EnglishName: c.GetProperty("EnglishName").GetString()!)).ToList();
    var original = new[] { "CN" }.Concat(world.Append((Code: "US", EnglishName: "United States")).OrderBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase).Select(c => c.Code)).ToArray();
    var countries = CompanionRegions.Countries;
    Check(countries.Where(c => c != CompanionRegions.Taiwan).Select(c => c.Code).SequenceEqual(original) && original.Length == 240,
        "without 臺灣, the 240 countries keep the original order exactly");
    int taiwan = Array.IndexOf(countries, CompanionRegions.Taiwan);
    Check(taiwan == 210 && countries[taiwan - 1].Code == "SY" && countries[taiwan + 1].Code == "TJ" && countries.Count(c => c.Code == "TW") == 1
        && !world.Any(c => c.Code is "TW" or "HK" or "MO" or "PR"), "臺灣 Taiwan sorts at its English name, once, after Syria");
    Check(CompanionRegions.DisplayName(CompanionRegions.Taiwan) == "臺灣 Taiwan" && CompanionRegions.DisplayName(CompanionRegions.China) == "中國 China"
        && CompanionRegions.DisplayName(CompanionRegions.UnitedStates) == "美國 United States", "fixed country names are Traditional");

    // --- China keeps 台湾省 and every code of the original lists.
    Check(CompanionRegions.Children("0").Select(r => r.Code).Contains("71") && CompanionRegions.Children("71").Length == 20
        && CompanionRegions.Children("71").Sum(c => CompanionRegions.Children(c.Code).Length) == 358, "台湾省 is listed under China with 20 cities and 358 districts");
    Check(CompanionRegions.All.Where(r => char.IsAsciiDigit(r.Code[0]) && r.Level == 2 || r.Code.StartsWith("US-") && r.Level == 1 || r.Code.Contains('-') && !r.Code.StartsWith("US-") && !r.Code.StartsWith("TW-") && r.Level == 1)
        .All(r => CompanionRegions.District(r.Code) == r), "every leaf of the original China, US and world lists stays a valid saved location");
    foreach (var code in new[] { "340104", "350622", "710101", "712628", "419001000", "US-06037", "JP-1850147", "TW-061-01", "TW-013-04", "TW-085-06" })
        Check(CompanionRegions.District(code).Code == code, "saved location kept: " + code);
    foreach (var code in new[] { null, "", "bogus", "71", "7101", "34", "3401", "TW", "TW-061", "TW-999-99", "US", "US-06", "JP", "JP.40", "999999" })
        Check(CompanionRegions.District(code).Code == "TW-061-01", "invalid location falls back to 臺北市中正區: " + (code ?? "null"));
    Check(!CompanionRegions.IsTaiwan(CompanionRegions.District("710101")) && CompanionRegions.Country(CompanionRegions.District("710101")) == CompanionRegions.China
        && CompanionRegions.IsTaiwan(CompanionRegions.District("TW-061-01")), "China's 台湾省 and the 臺灣 townships stay apart");

    // --- MCP: the documented pattern accepts every leaf (9-digit China codes and 臺灣 townships included).
    var definitions = JsonSerializer.SerializeToElement(ControlTools.Definitions);
    var region = definitions.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "update_settings").GetProperty("inputSchema")
        .GetProperty("properties").GetProperty("settings").GetProperty("properties").GetProperty("WeatherRegionCode");
    var pattern = new Regex(region.GetProperty("pattern").GetString()!);
    var leaves = CompanionRegions.All.Where(r => CompanionRegions.District(r.Code) == r).ToArray();
    Check(leaves.All(r => pattern.IsMatch(r.Code)) && leaves.Any(r => r.Code.Length == 9) && !pattern.IsMatch("臺北市中正區") && !pattern.IsMatch("TW-061-1"),
        "the MCP pattern accepts every leaf code and no place name (update_settings then checks the code is a leaf)");
    var description = region.GetProperty("description").GetString()!;
    Check(new[] { "TW-061-01", "340104", "710101", "419001000", "US-06037", "JP-1850147", "中央氣象署", "新安裝" }.All(description.Contains)
        && !Regex.IsMatch(description, "[设气区页号码务启]"), "the MCP description explains every code kind in Traditional Chinese");

    // --- Display names: script only (no Taiwan vocabulary), with the reviewed corrections; the data keeps its own names.
    string Shown(string code) => CompanionRegions.DisplayName(CompanionRegions.All.Single(r => r.Code == code));
    string Country(string code) => CompanionRegions.DisplayName(countries.Single(c => c.Code == code));
    foreach (var (code, name) in new[] { ("350206", "湖里區"), ("130529", "巨鹿縣"), ("371724", "巨野縣"), ("150205", "石拐區"), ("360824", "新干縣"),
        ("210703", "凌河區"), ("445302", "雲城區"), ("711522", "南庄鄉"), ("340828", "岳西縣"), ("653128", "岳普湖縣"), ("4401", "廣州市"), ("44", "廣東省"),
        ("654321", "布爾津縣"), ("71", "臺灣省"), ("7101", "臺北市"), ("710101", "中正區"), ("710436", "后里區"), ("711526", "苑裡鎮"), ("2101", "瀋陽市"),
        ("3310", "台州市"), ("331023", "天台縣"), ("361127", "餘干縣"), ("US-34", "新澤西州 NJ"), ("US-06", "加利福尼亞州 CA") })
        Check(Shown(code) == name, $"display name {code} {name}");
    foreach (var (code, name) in new[] { ("ML", "馬里 Mali"), ("GR", "希臘 Greece"), ("TD", "乍得 Chad"), ("IT", "意大利 Italy"), ("NZ", "新西蘭 New Zealand"), ("JP", "日本 Japan") })
        Check(Country(code) == name, $"country display name {code} {name} (script only)");
    Check(CompanionRegions.All.Single(r => r.Code == "350206").Name == "湖里区" && countries.Single(c => c.Code == "ML").Name == "马里 Mali",
        "the data keeps its Simplified names");
    Check(CompanionRegions.DisplayName(new CompanionRegion("350206", "3502", 2, "改过的名字", null, null)) == "改过的名字",
        "a table entry for a name the data no longer has is not used");
    Check(CompanionRegions.All.Where(CompanionRegions.IsTaiwan).All(r => CompanionRegions.DisplayName(r) == r.Name), "臺灣 townships show their own names");
    Dictionary<string, string[]> Dictionary(string name)
    {
        var table = new Dictionary<string, string[]>();
        using var reader = new StreamReader(typeof(CompanionRegions).Assembly.GetManifestResourceStream(name)!);
        while (reader.ReadLine() is { } line)
            if (line.Length > 0 && line[0] != '#' && line.Split('\t') is [var key, var values]) table.TryAdd(key, values.Split(' '));
        return table;
    }
    // Simplified-only: no Traditional candidate is the character itself, nor do the Taiwan variants turn it back (峰, 群).
    var variants = Dictionary("TWVariants.txt");
    var simplifiedOnly = Dictionary("STCharacters.txt").Where(e => !e.Value.Contains(e.Key) && (variants.TryGetValue(e.Value[0], out var tw) ? tw[0] : e.Value[0]) != e.Key)
        .Select(e => e.Key).ToHashSet();
    var shownNames = CompanionRegions.All.Select(CompanionRegions.DisplayName).Concat(countries.Select(CompanionRegions.DisplayName))
        .Concat(countries.SelectMany(c => c.Captions)).ToArray();
    // 南庄鄉 is the official name (CWA, the Ministry of the Interior) although OpenCC lists 庄 as Simplified only.
    var leftover = shownNames.Where(n => n != "南庄鄉" && n.EnumerateRunes().Any(r => simplifiedOnly.Contains(r.ToString()))).ToArray();
    Check(leftover.Length == 0, "no shown name or caption keeps a Simplified-only character" + (leftover.Length > 0 ? ": " + string.Join(" ", leftover.Take(10)) : ""));
    Check(CompanionRegions.FullName(CompanionRegions.District("340104")) == "安徽省 · 合肥市 · 蜀山區"
        && CompanionRegions.FullName(CompanionRegions.District("710101")) == "臺灣省 · 臺北市 · 中正區"
        && CompanionRegions.FullName(CompanionRegions.District("TW-061-01")) == "臺灣 · 臺北市 · 中正區"
        && CompanionRegions.FullName(CompanionRegions.District("110101")) == "北京市 · 東城區", "full names use the shown names");

    // --- Saved preferences: new installs get 臺北市中正區; any valid saved place is kept, an invalid one falls back.
    Directory.SetCurrentDirectory(work);
    Check(CompanionPreferences.Read().WeatherRegionCode == "TW-061-01", "a new install starts at 臺北市中正區");
    foreach (var (stored, expected) in new[] { ("340104", "340104"), ("710101", "710101"), ("419001000", "419001000"), ("US-06037", "US-06037"),
        ("JP-1850147", "JP-1850147"), ("TW-061-02", "TW-061-02"), ("999999", "TW-061-01"), ("TW-061", "TW-061-01"), ("7101", "TW-061-01") })
    {
        File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { WeatherRegionCode = stored, Scale = 0.8 }));
        File.Delete("preferences.json.bak");
        var read = CompanionPreferences.Read();
        Check(read.WeatherRegionCode == expected && read.Scale == 0.8, $"saved WeatherRegionCode {stored} reads as {expected}");
        read.Save();
        Check(CompanionPreferences.Read().WeatherRegionCode == expected, $"saving keeps {expected}");
    }
    File.WriteAllText("preferences.json", "{\"Scale\":0.8}");
    Check(CompanionPreferences.Read().WeatherRegionCode == "TW-061-01", "preferences from before weather locations existed start at 臺北市中正區");

    // --- MCP never sees a secret: get_settings lists public, not [JsonIgnore], not private properties.
    var listed = typeof(CompanionPreferences).GetProperties().Where(p => !p.IsDefined(typeof(JsonIgnoreAttribute)) && !CompanionPreferences.PrivateSettings.Contains(p.Name))
        .Select(p => p.Name).ToArray();
    Check(!listed.Any(n => n.Contains("ApiKey")) && CompanionPreferences.PrivateSettings.SequenceEqual(["RemoteApiKeyProtected", "CwaApiKeyProtected",
            "ClaudeApiKeyProtected", "OpenAiApiKeyProtected", "GeminiApiKeyProtected"])
        && typeof(CompanionPreferences).GetProperty("CwaApiKey", BindingFlags.Instance | BindingFlags.NonPublic) != null
        && typeof(CompanionPreferences).GetProperty("CwaApiKey") == null && !ControlTools.SettingNames.Any(n => n.Contains("ApiKey")),
        "get_settings has no secret and update_settings accepts none");
    Check(listed.Contains("CloudProvider") && listed.Contains("ClaudeModel") && listed.Contains("CloudDailyLimit")
        && new[] { "CloudProvider", "ClaudeModel", "OpenAiModel", "GeminiModel", "CloudDailyLimit" }.All(n => !ControlTools.SettingNames.Contains(n)
            && ControlTools.RefusedSetting(n, CompanionPreferences.PrivateSettings.Contains(n)) == "模型來源與雲端模型只能由使用者在設定中修改。")
        && ControlTools.RefusedSetting("ClaudeApiKeyProtected", true).StartsWith("API Key 與授權碼", StringComparison.Ordinal),
        "get_settings shows the cloud provider, models and limit read-only; update_settings refuses them and the keys");

    // --- The cloud model source survives a restart; anything unknown falls back to what the settings offer.
    File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { ModelSource = "Cloud", CloudProvider = "Gemini", GeminiModel = " gemini-4-hydrogen ", CloudDailyLimit = 500 }));
    File.Delete("preferences.json.bak");
    var cloudPrefs = CompanionPreferences.Read();
    Check(cloudPrefs.ModelSource == "Cloud" && cloudPrefs.CloudSelected && cloudPrefs.CloudProvider == "Gemini" && cloudPrefs.CloudModel("Gemini") == "gemini-4-hydrogen"
        && cloudPrefs.CloudModel("Claude") == "" && cloudPrefs.CloudDailyLimit == 500 && !cloudPrefs.UseCloudModel && !cloudPrefs.UseRemoteModel,
        "ModelSource Cloud is kept with its provider, model and limit (not usable without a key)");
    File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { ModelSource = "Elsewhere", CloudProvider = "Bard", CloudDailyLimit = 7 }));
    cloudPrefs = CompanionPreferences.Read();
    Check(cloudPrefs.ModelSource == "Local" && cloudPrefs.CloudProvider == "Claude" && cloudPrefs.CloudDailyLimit == 200, "an unknown source, provider or limit reads as the default");
    File.WriteAllText("preferences.json", "{\"Scale\":0.8}");
    cloudPrefs = CompanionPreferences.Read();
    Check(cloudPrefs.ModelSource == "Local" && cloudPrefs.CloudProvider == "Claude" && cloudPrefs.CloudDailyLimit == 200 && cloudPrefs.ClaudeModel == ""
        && CompanionPreferences.CloudDailyLimitChoices.SequenceEqual([50, 100, 200, 500, 0]), "preferences from before the cloud source start with Claude and 200 requests a day");

    if (OperatingSystem.IsWindows()) Console.WriteLine("SKIP the keychain stand-in needs bash");
    else Keychain();
}
finally
{
    Directory.SetCurrentDirectory(start);
}

// --- macOS keychain, through a stand-in for /usr/bin/security that keeps items as files and logs its argv.
void Keychain()
{
    var fake = Path.Combine(work, "security");
    File.WriteAllText(fake, """
        #!/bin/bash
        set -f
        dir="$(cd "$(dirname "$0")" && pwd)"; store="$dir/store"
        printf 'argv: %s\n' "$*" >> "$dir/calls.log"
        [ -f "$dir/hang" ] && sleep 30
        run() {
          cmd="$1"; shift; service=""; account=""; secret=""
          while [ $# -gt 0 ]; do
            case "$1" in
              -s) service="$2"; shift 2;;
              -a) account="$2"; shift 2;;
              -w) if [ "$cmd" = add-generic-password ]; then secret="$2"; shift 2; else shift; fi;;
              *) shift;;
            esac
          done
          [ -f "$dir/locked" ] && { echo "security: locked" >&2; return 36; }
          case "$cmd" in
            find-generic-password) [ -f "$store/$service/$account" ] || { echo "security: not found" >&2; return 44; }; cat "$store/$service/$account"; echo;;
            add-generic-password) [ -f "$dir/drop" ] && return 0; mkdir -p "$store/$service"; printf '%s' "$secret" > "$store/$service/$account";;
            delete-generic-password) [ -f "$store/$service/$account" ] || return 44; rm "$store/$service/$account";;
            *) return 1;;
          esac
        }
        if [ "$1" = "-i" ]; then
          status=0
          while read -r line; do printf 'stdin: %s\n' "${line%% -w *}" >> "$dir/calls.log"; set -- $line; run "$@"; status=$?; done
          exit $status
        fi
        run "$@"
        """.Replace("\r\n", "\n") + "\n");
    File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    MacKeychain.Tool = fake;
    MacKeychain.Timeout = TimeSpan.FromSeconds(2);
    string Item(string account) => Path.Combine(work, "store", CompanionEdition.KeychainService, account);
    string Calls() => File.Exists(Path.Combine(work, "calls.log")) ? File.ReadAllText(Path.Combine(work, "calls.log")) : "";
    void Mode(string name, bool on) { var flag = Path.Combine(work, name); if (on) File.WriteAllText(flag, ""); else File.Delete(flag); }
    const string Cwa = "CWA-1234ABCD-5678-90EF-ABCD-1234567890AB", Remote = "sk-test_AB.c~1+2/3=:@9";

    File.Delete("preferences.json"); File.Delete("preferences.json.bak");
    var prefs = CompanionPreferences.Read();
    Check(prefs.CwaApiKey == "" && prefs.LoadSecrets() == (false, null) && prefs.CwaApiKey == "", "no stored code reads as empty");
    prefs.CwaApiKey = " " + Cwa + " ";
    prefs.RemoteApiKey = Remote;
    Check(prefs.CwaApiKeyProtected == CompanionPreferences.KeychainMarker && prefs.RemoteApiKeyProtected == CompanionPreferences.KeychainMarker
        && File.ReadAllText(Item(MacKeychain.CwaAccount)) == Cwa && File.ReadAllText(Item(MacKeychain.RemoteAccount)) == Remote && prefs.CwaApiKey == Cwa,
        "secrets go to the keychain under the edition's service, trimmed");
    Check(!Calls().Contains(Cwa) && !Calls().Contains(Remote) && Calls().Contains("stdin: add-generic-password -U -s " + CompanionEdition.KeychainService + " -a CwaApiKey"),
        "secrets reach security through stdin, never argv");
    prefs.Save();
    var file = File.ReadAllText("preferences.json");
    Check(!file.Contains(Cwa) && !file.Contains(Remote) && file.Contains("\"CwaApiKeyProtected\": \"keychain\""), "preferences.json only says keychain");
    var reread = CompanionPreferences.Read();
    Check(reread.CwaApiKey == "" && reread.LoadSecrets() == (false, null) && reread.CwaApiKey == Cwa && reread.RemoteApiKey == Remote,
        "a restart reads the secrets back from the keychain");
    Throws<ArgumentException>(() => reread.CwaApiKey = "CWA 1234", "a code with a space is refused");
    Throws<ArgumentException>(() => reread.RemoteApiKey = "key\"quote", "a key with a quote is refused");
    Check(reread.CwaApiKey == Cwa && File.ReadAllText(Item(MacKeychain.CwaAccount)) == Cwa, "a refused value changes nothing");
    Mode("locked", true);
    try { reread.CwaApiKey = "CWA-NEW-0001"; throw new Exception("FAIL a locked keychain was accepted"); }
    catch (IOException e) { Check(!e.Message.Contains("CWA-NEW-0001") && reread.CwaApiKey == Cwa && reread.CwaApiKeyProtected == CompanionPreferences.KeychainMarker, "a refused write keeps the old code and says nothing secret"); }
    var locked = CompanionPreferences.Read();
    var (_, readError) = locked.LoadSecrets();
    Check(readError is IOException && locked.CwaApiKey == "", "an unreadable keychain leaves the code empty (the estimate is used)");
    Check(locked.StateOf(MacKeychain.CwaAccount) == CompanionPreferences.SecretState.Failed && locked.SecretsPending && locked.SecretProblem(MacKeychain.CwaAccount) is { Length: > 0 }
        && locked.CwaApiKeyProtected == CompanionPreferences.KeychainMarker, "an unread code is told apart from a removed one, and kept for a retry");
    Mode("locked", false);
    Check(locked.LoadSecrets() == (false, null) && locked.CwaApiKey == Cwa && locked.RemoteApiKey == Remote && !locked.SecretsPending
        && locked.StateOf(MacKeychain.RemoteAccount) == CompanionPreferences.SecretState.Loaded, "a retry once the keychain answers reads both secrets");
    Mode("drop", true);
    try { reread.CwaApiKey = "CWA-NEW-0002"; throw new Exception("FAIL an unconfirmed write was accepted"); }
    catch (IOException) { Check(reread.CwaApiKey == Cwa && File.ReadAllText(Item(MacKeychain.CwaAccount)) == Cwa, "a write the keychain does not read back is reported"); }
    Mode("drop", false);
    Mode("hang", true);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    try { MacKeychain.Read(MacKeychain.CwaAccount); throw new Exception("FAIL a hanging security was waited on"); }
    catch (IOException) { Check(clock.Elapsed < TimeSpan.FromSeconds(10), "a keychain that does not answer times out"); }
    Mode("hang", false);
    reread.CwaApiKey = "";
    Check(reread.CwaApiKeyProtected == null && reread.CwaApiKey == "" && !File.Exists(Item(MacKeychain.CwaAccount)) && File.Exists(Item(MacKeychain.RemoteAccount)),
        "clearing the code removes only its keychain item");
    MacKeychain.Delete(MacKeychain.CwaAccount);
    Check(MacKeychain.Read(MacKeychain.CwaAccount) == "", "removing a missing item is fine");
    Throws<ArgumentOutOfRangeException>(() => MacKeychain.Read("Other"), "only the known accounts are used");
    // A code an earlier build kept in the file as Base64 moves into the keychain.
    File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { WeatherRegionCode = "340104", CwaApiKeyProtected = Convert.ToBase64String(Encoding.UTF8.GetBytes("CWA-LEGACY-0001")) }));
    var legacy = CompanionPreferences.Read();
    var (moved, legacyError) = legacy.LoadSecrets();
    Check(moved && legacyError == null && legacy.CwaApiKey == "CWA-LEGACY-0001" && legacy.CwaApiKeyProtected == CompanionPreferences.KeychainMarker
        && File.ReadAllText(Item(MacKeychain.CwaAccount)) == "CWA-LEGACY-0001" && legacy.WeatherRegionCode == "340104", "a Base64 code from the file moves into the keychain");
    // A locked keychain keeps a legacy value in memory and in the file until a retry can move it.
    string Legacy(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { CwaApiKeyProtected = Legacy("CWA-LEGACY-0002") }));
    Mode("locked", true);
    var pending = CompanionPreferences.Read();
    Check(pending.LoadSecrets() is (false, IOException) && pending.CwaApiKey == "CWA-LEGACY-0002" && pending.CwaApiKeyProtected == Legacy("CWA-LEGACY-0002")
        && pending.StateOf(MacKeychain.CwaAccount) == CompanionPreferences.SecretState.Legacy && pending.SecretsPending, "a legacy code the locked keychain cannot take yet waits for a retry");
    Mode("locked", false);
    Check(pending.LoadSecrets() is (true, null) && pending.CwaApiKeyProtected == CompanionPreferences.KeychainMarker && File.ReadAllText(Item(MacKeychain.CwaAccount)) == "CWA-LEGACY-0002"
        && !pending.SecretsPending, "the retry moves it into the keychain");
    // A legacy key the keychain refuses for good is used for this run only and leaves the file.
    File.WriteAllText("preferences.json", JsonSerializer.Serialize(new { RemoteApiKeyProtected = Legacy("pass phrase!") }));
    var refused = CompanionPreferences.Read();
    Check(refused.LoadSecrets() is (true, ArgumentException) && refused.RemoteApiKey == "pass phrase!" && refused.RemoteApiKeyProtected == null
        && refused.StateOf(MacKeychain.RemoteAccount) == CompanionPreferences.SecretState.Unsaved && refused.SecretProblem(MacKeychain.RemoteAccount) is { Length: > 0 } && !refused.SecretsPending,
        "a legacy key the keychain refuses stays in memory, leaves the file and asks to be entered again");
    refused.Save(); refused.Save();
    Check(!File.ReadAllText("preferences.json").Contains(Legacy("pass phrase!")) && !File.ReadAllText("preferences.json.bak").Contains(Legacy("pass phrase!")),
        "neither preferences.json nor its backup keeps the refused key");
    Throws<ArgumentException>(() => refused.RemoteApiKey = refused.RemoteApiKey, "saving the refused key again names why the keychain refuses it");
    refused.RemoteApiKey = "pass-phrase";
    Check(refused.StateOf(MacKeychain.RemoteAccount) == CompanionPreferences.SecretState.Loaded && refused.RemoteApiKeyProtected == CompanionPreferences.KeychainMarker, "a key the keychain accepts replaces it");

    // The cloud AI services' keys: one keychain item each, read back with the other secrets.
    File.Delete("preferences.json"); File.Delete("preferences.json.bak");
    const string ClaudeKey = "sk-ant-api03-Test_key-0001", OpenAiKey = "sk-proj-Test_key-0002", GeminiKey = "AQ.Test_key-0003";
    var cloud = CompanionPreferences.Read();
    cloud.ModelSource = "Cloud";
    Check(cloud.CloudSelected && !cloud.UseCloudModel, "the cloud source without a key is chosen but not usable");
    cloud.SetCloudApiKey("Claude", ClaudeKey);
    cloud.SetCloudApiKey("OpenAI", OpenAiKey);
    cloud.SetCloudApiKey("Gemini", " " + GeminiKey + " ");
    Check(File.ReadAllText(Item(MacKeychain.ClaudeAccount)) == ClaudeKey && File.ReadAllText(Item(MacKeychain.OpenAiAccount)) == OpenAiKey && File.ReadAllText(Item(MacKeychain.GeminiAccount)) == GeminiKey
        && new[] { cloud.ClaudeApiKeyProtected, cloud.OpenAiApiKeyProtected, cloud.GeminiApiKeyProtected }.All(v => v == CompanionPreferences.KeychainMarker)
        && cloud.UseCloudModel && cloud.CloudApiKey("Gemini") == GeminiKey, "each cloud key is a keychain item of its own (ClaudeApiKey, OpenAiApiKey, GeminiApiKey), trimmed");
    Check(new[] { ClaudeKey, OpenAiKey, GeminiKey }.All(k => !Calls().Contains(k)) && Calls().Contains("-a GeminiApiKey"), "cloud keys reach security through stdin, never argv");
    cloud.CloudProvider = "Gemini";
    cloud.SetCloudModel("Gemini", " gemini-4-hydrogen ");
    cloud.Save();
    var saved = File.ReadAllText("preferences.json");
    Check(new[] { ClaudeKey, OpenAiKey, GeminiKey }.All(k => !saved.Contains(k)) && saved.Contains("\"GeminiApiKeyProtected\": \"keychain\"") && saved.Contains("\"CloudProvider\": \"Gemini\""),
        "preferences.json only says keychain for the cloud keys");
    var back = CompanionPreferences.Read();
    Check(back.LoadSecrets() == (false, null) && back.CloudApiKey("Claude") == ClaudeKey && back.CloudApiKey("OpenAI") == OpenAiKey && back.CloudApiKey("Gemini") == GeminiKey
        && back.CloudProvider == "Gemini" && back.CloudModel("Gemini") == "gemini-4-hydrogen" && back.UseCloudModel
        && CompanionPreferences.CloudAccount("OpenAI") == MacKeychain.OpenAiAccount && back.SecretValue(MacKeychain.ClaudeAccount) == ClaudeKey
        && back.StateOf(MacKeychain.GeminiAccount) == CompanionPreferences.SecretState.Loaded, "a restart reads the cloud keys back from the keychain");
    back.SetCloudApiKey("OpenAI", "");
    Check(!File.Exists(Item(MacKeychain.OpenAiAccount)) && back.OpenAiApiKeyProtected == null && File.Exists(Item(MacKeychain.ClaudeAccount)) && File.Exists(Item(MacKeychain.GeminiAccount)),
        "clearing one cloud key removes only its keychain item");
}

// --- The shared weather checks: CWA forecast with a code, Open-Meteo estimate without, separate caches and retries.
int weatherResult;
try { weatherResult = await WeatherCacheVerification.Run(Path.Combine(work, "weather"), args.Contains("live")); }
catch { foreach (var entry in DiagnosticLog.Snapshot().TakeLast(5)) Console.WriteLine("LOG " + entry); throw; }
if (args.Contains("live"))
{
    // TLS to the CWA open data platform through this runtime, and its answer to a code that cannot be valid.
    using var web = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    using var cwa = new CompanionWeather(web) { CwaApiKey = "CWA-00000000-0000-0000-0000-000000000000" };
    try { await cwa.Context("TW-061-01", CancellationToken.None); throw new Exception("FAIL an impossible CWA code was accepted"); }
    catch (IOException e) { Check(e.Message == "中央氣象署不接受這組授權碼，請確認授權碼是否正確。", "live CWA request is answered and refuses an impossible code"); }
}
try { Directory.Delete(work, true); } catch (IOException) { }
Console.WriteLine($"RESULT {checks} region, display name and secret checks passed");
return weatherResult;
