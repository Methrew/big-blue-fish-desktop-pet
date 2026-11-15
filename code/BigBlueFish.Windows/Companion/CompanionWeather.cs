// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Windows;

internal sealed class CompanionWeather : IDisposable
{
    private readonly HttpClient web;
    private readonly string? cachePath;
    private readonly Func<DateTimeOffset> clock;
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    // Keyed by region code. A Taiwan township's CWA forecast is keyed by its code plus CwaSuffix, so a CWA forecast and
    // an Open-Meteo estimate never share an entry, a retry spacing, a pending fetch or an error.
    private readonly Dictionary<string, Entry> entries = new();
    private readonly Dictionary<string, DateTimeOffset> attempts = new();
    private readonly Dictionary<string, Task> pending = new();
    private readonly Dictionary<string, string> cwaErrors = new();
    private const string CwaSuffix = "|cwa";
    // Cwa is null in entries saved before Taiwan had an Open-Meteo estimate; Taiwan entries of that kind are CWA forecasts.
    internal sealed record Entry(string Code, DateTimeOffset FetchedAt, DateTimeOffset ObservedAt, string Context, bool? Cwa = null);

    internal CompanionWeather(HttpClient web, string? cachePath = null, Func<DateTimeOffset>? clock = null)
    {
        this.web = web;
        this.cachePath = cachePath == null ? null : Path.GetFullPath(cachePath);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (this.cachePath != null)
        {
            try
            {
                foreach (var entry in JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(this.cachePath)) ?? [])
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Code) && !string.IsNullOrWhiteSpace(entry.Context))
                        entries[Key(entry.Code, entry.Cwa ?? IsTaiwanKey(entry.Code))] = entry;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    private bool Fresh(Entry entry, DateTimeOffset now) =>
        now >= entry.FetchedAt && now >= entry.ObservedAt
        && now - entry.FetchedAt <= TimeSpan.FromHours(6)
        && now - entry.ObservedAt <= TimeSpan.FromHours(6);

    private static string Key(string code, bool cwa) => cwa ? code + CwaSuffix : code;
    private static bool IsTaiwanKey(string key) => key.StartsWith(CompanionRegions.TaiwanRoot + "-", StringComparison.Ordinal);
    private static bool IsCwaKey(string key) => key.EndsWith(CwaSuffix, StringComparison.Ordinal);
    // Callers hold sync.
    private bool UsesCwa(CompanionRegion region) => CompanionRegions.IsTaiwan(region) && cwaApiKey.Length > 0;

    // CWA open data needs the user's own authorization code; it is sent as a header and never logged or cached.
    // Without a code, Taiwan townships get an Open-Meteo estimate near the township centre instead.
    internal string CwaApiKey
    {
        get { lock (sync) return cwaApiKey; }
        set
        {
            lock (sync)
            {
                if (cwaApiKey == value) return;
                var replaced = cwaApiKey.Length > 0 && value.Length > 0;
                cwaApiKey = value;
                cwaGeneration++;
                // A corrected code should not wait out the hourly retry spacing of a rejected one, and a fetch still
                // running with the old code can no longer store or report anything.
                foreach (var key in attempts.Keys.Where(IsTaiwanKey).ToList()) attempts.Remove(key);
                foreach (var key in pending.Keys.Where(IsCwaKey).ToList()) pending.Remove(key);
                cwaErrors.Clear();
                // Replacing one code with another fetches again at once; the last forecast stays until the new one arrives.
                if (replaced)
                    foreach (var key in entries.Keys.Where(IsCwaKey)) attempts[key] = DateTimeOffset.MinValue;
            }
        }
    }
    private string cwaApiKey = "";
    private int cwaGeneration;

    // Called by the normal UI timer; no HTTP or disk work runs on the UI thread.
    internal Task RefreshIfDue(string regionCode)
    {
        var region = CompanionRegions.District(regionCode);
        lock (sync)
        {
            if (lifetime.IsCancellationRequested) return Task.CompletedTask;
            var cwa = UsesCwa(region);
            var key = Key(region.Code, cwa);
            if (pending.TryGetValue(key, out var active) && !active.IsCompleted) return active;
            var now = clock();
            var last = attempts.TryGetValue(key, out var attempt) ? attempt
                : entries.TryGetValue(key, out var cached) ? cached.FetchedAt : DateTimeOffset.MinValue;
            if (now >= last && now - last < TimeSpan.FromHours(1)) return Task.CompletedTask;
            DiagnosticLog.Write(DiagnosticEvent.WeatherRefresh);
            attempts[key] = now;
            var code = cwa ? cwaApiKey : null;
            var generation = cwaGeneration;
            return pending[key] = Task.Run(() => Fetch(region, key, code, generation));
        }
    }

    internal string CachedContext(string regionCode)
    {
        var region = CompanionRegions.District(regionCode);
        lock (sync)
            return entries.TryGetValue(Key(region.Code, UsesCwa(region)), out var entry) && Fresh(entry, clock()) ? entry.Context : "";
    }

    internal async Task<string> Context(string regionCode, CancellationToken token)
    {
        var refresh = RefreshIfDue(regionCode);
        var cached = CachedContext(regionCode);
        if (cached.Length > 0) { DiagnosticLog.Write(DiagnosticEvent.WeatherCached); return cached; }
        await refresh.WaitAsync(token);
        cached = CachedContext(regionCode);
        if (cached.Length == 0) throw new IOException(CwaError(regionCode) ?? "暫時沒有六小時內的天氣資料，請稍後重試。");
        return cached;
    }

    // Why the last CWA fetch for this township failed (for example a refused authorization code), if it did.
    private string? CwaError(string regionCode)
    {
        var region = CompanionRegions.District(regionCode);
        lock (sync)
            return UsesCwa(region) && cwaErrors.TryGetValue(Key(region.Code, true), out var error) ? error : null;
    }

    public void Dispose() { lock (sync) lifetime.Cancel(); }


    internal static string Description(int code) => code switch
    {
        0 => "晴朗", 1 => "大致晴朗", 2 => "局部多雲", 3 => "陰天",
        45 or 48 => "有霧", 51 or 53 or 55 => "毛毛雨", 56 or 57 => "凍毛毛雨",
        61 => "小雨", 63 => "中雨", 65 => "大雨", 66 or 67 => "凍雨",
        71 => "小雪", 73 => "中雪", 75 => "大雪", 77 => "雪粒",
        80 => "小陣雨", 81 => "陣雨", 82 => "強陣雨", 85 or 86 => "陣雪",
        95 => "雷雨", 96 or 99 => "雷雨伴冰雹", _ => "天氣狀況未提供"
    };

    // Taiwan (without a CWA code) and China use their own UTC+8 zones; the US gets °F and mph; elsewhere metric in local time.
    internal static string ForecastUrl(CompanionRegion region) =>
        "https://api.open-meteo.com/v1/forecast?latitude=" + region.Latitude!.Value.ToString(CultureInfo.InvariantCulture)
        + "&longitude=" + region.Longitude!.Value.ToString(CultureInfo.InvariantCulture)
        + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m"
        + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max"
        + (CompanionRegions.IsTaiwan(region) ? "&forecast_days=1&timezone=Asia%2FTaipei"
            : CompanionRegions.Country(region) == CompanionRegions.China ? "&forecast_days=1&timezone=Asia%2FShanghai"
            : CompanionRegions.IsUs(region) ? "&temperature_unit=fahrenheit&wind_speed_unit=mph&forecast_days=1&timezone=auto"
            : "&forecast_days=1&timezone=auto");

    internal static string CwaUrl(CompanionRegion district) => "https://opendata.cwa.gov.tw/api/v1/rest/datastore/"
        + CompanionRegions.CwaDataset(district) + "?LocationName=" + Uri.EscapeDataString(district.Name);

    // CWA has shipped both camelCase and PascalCase layouts of F-D0047; fields are read by name in either.
    internal static string CwaForecast(JsonElement root, CompanionRegion district, DateTimeOffset now)
    {
        static JsonElement Child(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return default;
        }
        static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
            Child(e, name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];
        static string Text(JsonElement e, string name) => Child(e, name) is { ValueKind: JsonValueKind.String } s ? s.GetString() ?? "" : "";
        static string FirstText(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString() ?? "";
            return "";
        }
        static string Same(string name) => name.Replace('台', '臺');
        static DateTimeOffset Time(string text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            && !text.Contains('+') && !text.EndsWith('Z') ? new DateTimeOffset(local, TimeSpan.FromHours(8))
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        var location = Items(Child(root, "records"), "locations").SelectMany(l => Items(l, "location"))
            .FirstOrDefault(l => Same(Text(l, "locationName")) == Same(district.Name));
        if (location.ValueKind == JsonValueKind.Undefined) throw new IOException($"中央氣象署資料中找不到「{district.Name}」");
        var element = Items(location, "weatherElement").FirstOrDefault(e => Text(e, "elementName") is "天氣預報綜合描述" or "WeatherDescription");
        var periods = Items(element, "time")
            .Select(t => (Start: Time(Text(t, "startTime")), End: Time(Text(t, "endTime")), Text: FirstText(Items(t, "elementValue").FirstOrDefault())))
            .Where(p => p.End > now && p.Text.Length > 0).OrderBy(p => p.Start).Take(2).ToArray();
        if (periods.Length == 0) throw new IOException("中央氣象署暫時沒有這個時段的預報");
        var context = new StringBuilder($"地點：{CompanionRegions.FullName(district)}（中央氣象署鄉鎮天氣預報）。");
        // Reminders and the Hermes bridge read these periods back, so the times never follow the user's culture.
        foreach (var p in periods)
            context.Append(CultureInfo.InvariantCulture, $"{p.Start:M月d日HH:mm}到{p.End:M月d日HH:mm}：{p.Text}");
        return context.ToString();
    }

    private async Task<string> FetchCwa(CompanionRegion district, string authorization, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CwaUrl(district));
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await web.SendAsync(request, token);
        // The status goes to the diagnostic log; the code itself never does.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("中央氣象署不接受這組授權碼，請確認授權碼是否正確。", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(token);
        // In the data directory with the other test-mode files (the program directory stays as installed).
        if (App.Args.Contains("--test-mode"))
            try { File.WriteAllText(Path.GetFullPath("test-cwa-weather.json"), body); } catch { }
        using var json = JsonDocument.Parse(body);
        return CwaForecast(json.RootElement, district, clock());
    }

    // cwaCode is the authorization code for a CWA fetch, null for Open-Meteo; generation is the code's version when the fetch began.
    private async Task Fetch(CompanionRegion region, string key, string? cwaCode, int generation)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var token = deadline.Token;
            if (cwaCode != null)
            {
                var forecast = await FetchCwa(region, cwaCode, token);
                // Forecast periods start in the future; freshness follows the fetch time.
                var fetched = clock();
                Store(key, new Entry(region.Code, fetched, fetched, forecast, Cwa: true), generation);
                return;
            }
            if (region.Latitude == null || region.Longitude == null) return;
            using var response = await web.GetAsync(ForecastUrl(region), token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var current = json.RootElement.GetProperty("current");
            var daily = json.RootElement.GetProperty("daily");
            var taiwan = CompanionRegions.IsTaiwan(region);
            var china = CompanionRegions.Country(region) == CompanionRegions.China;
            var us = CompanionRegions.IsUs(region);
            // Taiwan (Asia/Taipei) and China (Asia/Shanghai) are always UTC+8; elsewhere uses the location's own zone (timezone=auto).
            var offset = taiwan || china ? TimeSpan.FromHours(8) : TimeSpan.FromSeconds(json.RootElement.GetProperty("utc_offset_seconds").GetInt32());
            var observed = new DateTimeOffset(DateTime.Parse(current.GetProperty("time").GetString()!,
                CultureInfo.InvariantCulture, DateTimeStyles.None), offset);
            string Number(JsonElement value) => value.GetDouble().ToString("0.#", CultureInfo.InvariantCulture);
            var degree = us ? "℉" : "℃";
            var context = $"地點：{CompanionRegions.FullName(region)}（{(taiwan ? "鄉鎮市區中心" : china ? "區縣中心" : us ? "郡中心" : "城市中心")}附近的天氣估算）。"
                + $"天氣資料記錄時間：{current.GetProperty("time").GetString()}（{(taiwan ? "臺灣時間" : china ? "北京時間" : "當地時間")}）。"
                + $"記錄時{Description(current.GetProperty("weather_code").GetInt32())}，"
                + $"氣溫{Number(current.GetProperty("temperature_2m"))}{degree}，體感{Number(current.GetProperty("apparent_temperature"))}{degree}，"
                + $"相對溼度{Number(current.GetProperty("relative_humidity_2m"))}%，風速{Number(current.GetProperty("wind_speed_10m"))}{(us ? "英里/小時" : "公里/小時")}。"
                + $"資料日期{observed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}最低{Number(daily.GetProperty("temperature_2m_min")[0])}{degree}、最高{Number(daily.GetProperty("temperature_2m_max")[0])}{degree}，"
                + $"最高降水機率{Number(daily.GetProperty("precipitation_probability_max")[0])}%。";
            Store(key, new Entry(region.Code, clock(), observed, context, Cwa: false), null);
        }
        // A failed hourly refresh retains the last good entry, never extending its lifetime.
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or JsonException or InvalidOperationException or FormatException
            or KeyNotFoundException or ArgumentException or ObjectDisposedException)
        {
            DiagnosticLog.Write(DiagnosticEvent.WeatherFailed, error: e);
            lock (sync)
            {
                // A CWA fetch made with a code that has since changed reports nothing.
                if (cwaCode != null && generation != cwaGeneration) return;
                if (cwaCode != null)
                    cwaErrors[key] = e is IOException or HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                        ? e.Message : "暫時無法取得中央氣象署天氣資料";
                // No answer at all (no network yet, just woken up, or a timeout): try again in two minutes, not an hour.
                // An answer that was an error (a refused code, 404, 429, 5xx) keeps the hour.
                if ((e is HttpRequestException { StatusCode: null } || e is OperationCanceledException) && !lifetime.IsCancellationRequested)
                    attempts[key] = clock() - TimeSpan.FromMinutes(58);
            }
        }
    }

    // generation is set for a CWA forecast: one fetched with a code that has since changed is dropped.
    private void Store(string key, Entry entry, int? generation)
    {
        lock (sync)
        {
            if (lifetime.IsCancellationRequested || !Fresh(entry, clock())) return;
            if (generation != null && generation != cwaGeneration) return;
            entries[key] = entry;
            if (generation != null) cwaErrors.Remove(key);
            if (cachePath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temporary = cachePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new List<Entry>(entries.Values)));
                File.Move(temporary, cachePath, overwrite: true);
            }
        }
    }
}
