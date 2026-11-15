// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using BigBlueFish.Windows;

internal static class WeatherCacheVerification
{
    private sealed class Feed : HttpMessageHandler
    {
        internal DateTimeOffset Now;
        internal int Calls;
        internal bool Offline, Invalid, Stale;
        internal TaskCompletionSource? Block;
        internal string LastUrl = "", LastAuthorization = "";
        internal string? RefusedCode;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var uri = request.RequestUri!;
            LastUrl = uri.ToString();
            if (Block != null) await Block.Task.WaitAsync(token);
            if (Offline) throw new HttpRequestException("offline test");
            if (uri.Host == "opendata.cwa.gov.tw")
            {
                LastAuthorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : "";
                if (LastAuthorization == RefusedCode) return new(HttpStatusCode.Unauthorized);
                return new(HttpStatusCode.OK) { Content = new StringContent(Invalid ? "broken json" : CwaBody(uri)) };
            }
            var offset = LastUrl.Contains("timezone=auto") ? TimeSpan.FromHours(-7) : TimeSpan.FromHours(8);
            var observed = (Stale ? Now.AddHours(-7) : Now).ToOffset(offset);
            var text = JsonSerializer.Serialize(new { utc_offset_seconds = (int)offset.TotalSeconds, current = new { time = observed.ToString("yyyy-MM-ddTHH:mm"),
                temperature_2m = 21, apparent_temperature = 20, relative_humidity_2m = 65, weather_code = 0, wind_speed_10m = 8 },
                daily = new { temperature_2m_min = new[] { 18 }, temperature_2m_max = new[] { 25 }, precipitation_probability_max = new[] { 10 } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(Invalid ? "broken json" : text) };
        }

        // CWA F-D0047 in its camelCase layout: three-hour periods of the asked township from 06:00 (UTC+8) on Now's date.
        private string CwaBody(Uri uri)
        {
            var query = Uri.UnescapeDataString(uri.Query);
            var name = query[(query.IndexOf("LocationName=", StringComparison.Ordinal) + "LocationName=".Length)..];
            var day = Now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            object Period(int hour, string text) => new { startTime = $"{day} {hour:00}:00:00", endTime = $"{day} {hour + 3:00}:00:00",
                elementValue = new[] { new { value = text, measures = "NA" } } };
            return JsonSerializer.Serialize(new { success = "true", records = new { locations = new[] { new { locationsName = "臺北市", location = new[] { new {
                locationName = name, weatherElement = new[] { new { elementName = "天氣預報綜合描述", time = new[] {
                    Period(6, "晴。降雨機率10%。溫度攝氏24至27度。"), Period(9, "多雲。降雨機率20%。溫度攝氏27至30度。"),
                    Period(12, "短暫陣雨。降雨機率70%。溫度攝氏28至31度。") } } } } } } } } });
        }
    }
    internal static async Task<int> Run(string directory, bool live)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        // Every invocation gets its own data file, avoiding stale fixtures between test runs.
        string file = Path.Combine(directory, Guid.NewGuid() + ".json");
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        var now = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var feed = new Feed { Now = now, Block = new() };
        using var web = new HttpClient(feed);
        using var cache = new CompanionWeather(web, file, () => now);
        var first = cache.RefreshIfDue("350622");
        Check(Enumerable.Range(0, 50).All(_ => ReferenceEquals(first, cache.RefreshIfDue("350622"))), "concurrent timer and manual requests share one network fetch");
        Check(cache.CachedContext("350622") == "", "ordinary speech returns immediately while first weather fetch is blocked");
        feed.Block.SetResult();
        await first;
        string good = cache.CachedContext("350622");
        Check(good.Contains("21℃") && File.Exists(file), "real parser saves valid weather to disk");
        Check(await cache.Context("350622", default) == good && feed.Calls == 1, "manual weather uses warm cache without another network request");
        now = now.AddMinutes(59); feed.Now = now;
        await cache.RefreshIfDue("350622");
        Check(feed.Calls == 1, "no refresh before one hour");
        now = now.AddMinutes(1); feed.Now = now; feed.Block = new();
        var hourly = cache.RefreshIfDue("350622");
        Check(await cache.Context("350622", default) == good && !hourly.IsCompleted, "cached manual broadcast does not wait for hourly refresh");
        feed.Block.SetResult(); await hourly;
        Check(feed.Calls == 2 && cache.CachedContext("350622") != good, "refresh begins at hour boundary and commits fresh observations");
        good = cache.CachedContext("350622");
        var committedJson = File.ReadAllText(file);
        using var restart = new CompanionWeather(web, file, () => now);
        Check(restart.CachedContext("350622") == good, "restart reloads persistent cache");
        await restart.RefreshIfDue("350622");
        Check(feed.Calls == 2, "restart with recent cache does not fetch again");
        Check(restart.CachedContext("340104") == "", "other district never receives selected district weather");
        feed.Block = null; feed.Offline = true;
        now = now.AddHours(1); feed.Now = now;
        await restart.RefreshIfDue("350622");
        Check(restart.CachedContext("350622") == good, "network failure preserves last successful entry");
        int afterFailure = feed.Calls;
        await restart.RefreshIfDue("350622");
        Check(feed.Calls == afterFailure, "failed refresh is hourly throttled rather than retrying every UI tick");
        now = now.AddHours(5);
        Check(restart.CachedContext("350622") == good, "exact six-hour boundary is usable");
        now = now.AddTicks(1);
        Check(restart.CachedContext("350622") == "", "weather older than six hours is excluded");
        try { await restart.Context("350622", default); throw new Exception("expired cache accepted"); }
        catch (IOException) { Check(true, "manual broadcast fails honestly when offline and cache expired"); }
        feed.Offline = false; feed.Now = now; feed.Stale = true;
        await restart.RefreshIfDue("340104");
        Check(restart.CachedContext("340104") == "", "newly downloaded but seven-hour-old observation is rejected");
        feed.Stale = false; feed.Invalid = true;
        now = now.AddHours(1); feed.Now = now;
        await restart.RefreshIfDue("350622");
        Check(File.ReadAllText(file) == committedJson,
            "malformed refresh leaves last valid JSON file intact");
        File.WriteAllText(file + ".tmp", "partial write");
        using var afterInterruptedWrite = new CompanionWeather(web, file, () => now.AddHours(-7));
        Check(afterInterruptedWrite.CachedContext("350622") == good, "interrupted temporary write cannot replace committed cache");
        File.WriteAllText(file, "broken persistent JSON");
        using var corrupt = new CompanionWeather(web, file, () => now);
        Check(corrupt.CachedContext("350622") == "", "corrupt persistent file is ignored without crashing");
        feed.Invalid = false; feed.Block = new();
        var loading = corrupt.Context("350622", new CancellationToken(true));
        try { await loading; throw new Exception("cancel ignored"); } catch (OperationCanceledException) { }
        feed.Block.SetResult(); await corrupt.RefreshIfDue("350622");
        Check(corrupt.CachedContext("350622").Length > 0, "cancelled manual waiter does not cancel shared background refresh");
        now = now.AddDays(-1);
        Check(corrupt.CachedContext("350622") == "", "clock rollback rejects future-dated cache");

        // Region table: the original lists and order (China first, the rest by English name) with 臺灣 Taiwan added as one
        // more country; new installs and invalid codes get 臺北市中正區, a valid saved code is kept as it is.
        var taipei = CompanionRegions.District(null);
        Check(CompanionRegions.DefaultCode == "TW-061-01" && taipei.Code == "TW-061-01" && CompanionRegions.FullName(taipei) == "臺灣 · 臺北市 · 中正區"
            && CompanionRegions.Country(taipei) == CompanionRegions.Taiwan && CompanionRegions.IsTaiwan(taipei) && CompanionRegions.CwaDataset(taipei) == "F-D0047-061",
            "new installs default to 臺北市中正區 and its CWA township dataset");
        var counties = CompanionRegions.Children(CompanionRegions.Taiwan.Root);
        Check(counties.Length == 22 && counties.Sum(c => CompanionRegions.Children(c.Code).Length) == 368
            && counties.All(c => CompanionRegions.Children(c.Code).All(t => t.Latitude != null && t.Longitude != null && CompanionRegions.District(t.Code) == t)),
            "Taiwan lists 22 counties and 368 located townships");
        Check(CompanionRegions.Chain(taipei).Select(r => r.Name).SequenceEqual(["臺北市", "中正區"]) && CompanionRegions.Taiwan.LeafLevel == 1
            && CompanionRegions.Taiwan.Captions.SequenceEqual(["縣市", "鄉鎮市區"])
            && CompanionRegions.District("TW-061").Code == CompanionRegions.DefaultCode && CompanionRegions.District("TW").Code == CompanionRegions.DefaultCode,
            "Taiwan picks a county, then a township; county and root codes fall back");
        var la = CompanionRegions.District("US-06037");
        Check(la.Code == "US-06037" && CompanionRegions.FullName(la) == "美國 United States · 加利福尼亞州 CA · Los Angeles County", "US county is a valid weather leaf, named in Traditional script");
        Check(CompanionRegions.District("US-06").Code == CompanionRegions.DefaultCode && CompanionRegions.District("340100").Code == CompanionRegions.DefaultCode
            && CompanionRegions.District("bogus").Code == CompanionRegions.DefaultCode && CompanionRegions.District("").Code == CompanionRegions.DefaultCode,
            "state, city and unknown codes fall back to the default");
        Check(CompanionRegions.Children("0").All(r => !CompanionRegions.IsUs(r)) && CompanionRegions.Children("US").Length == 52, "China provinces and 52 US states stay separate");
        Check(CompanionRegions.Children("US").All(s => CompanionRegions.Children(s.Code).Length > 0 && CompanionRegions.Children(s.Code).All(c => c.Latitude != null)), "every US state has located counties");
        feed.Now = now;
        await corrupt.RefreshIfDue("US-06037");
        var us = corrupt.CachedContext("US-06037");
        Check(feed.LastUrl.Contains("temperature_unit=fahrenheit") && feed.LastUrl.Contains("wind_speed_unit=mph") && feed.LastUrl.Contains("timezone=auto"), "US request asks for imperial units and local time zone");
        Check(us.Contains("21℉") && us.Contains("英里/小時") && us.Contains("當地時間") && us.Contains("Los Angeles County"), "US weather context uses Fahrenheit, mph and local time");
        var countries = CompanionRegions.Countries;
        int at = Array.IndexOf(countries, CompanionRegions.Taiwan);
        Check(countries[0] == CompanionRegions.China && countries.Length == 241 && countries.Contains(CompanionRegions.UnitedStates)
            && at > 0 && countries[at - 1].Code == "SY" && countries[at + 1].Code == "TJ"
            && countries.Skip(1).All(c => c.Code is not ("CN" or "HK" or "MO" or "PR")) && countries.Select(c => c.Code).Distinct().Count() == countries.Length,
            "China first, then 240 countries by English name, 臺灣 Taiwan between Syria and Tajikistan");
        Check(new[] { "71", "81", "82" }.All(code => CompanionRegions.Children("0").Any(r => r.Code == code))
            && CompanionRegions.All.All(r => r.Code[..2] is not ("HK" or "MO")), "China keeps 台湾省, Hong Kong and Macau as in the original");
        var province = CompanionRegions.All.Where(r => r.Code.StartsWith("71") && r.Code.Length == 6 && char.IsAsciiDigit(r.Code[0])).ToArray();
        Check(province.Length == 358 && province.All(r => CompanionRegions.District(r.Code) == r && CompanionRegions.Country(r) == CompanionRegions.China && !CompanionRegions.IsTaiwan(r) && r.Latitude != null),
            "every 台湾省 district stays a valid China code, never moved to the 臺灣 list");
        var saved = CompanionRegions.District("710101");
        Check(saved.Code == "710101" && CompanionRegions.FullName(saved) == "臺灣省 · 臺北市 · 中正區" && CompanionRegions.District("7101").Code == CompanionRegions.DefaultCode
            && CompanionRegions.District("71").Code == CompanionRegions.DefaultCode, "a saved 台湾省 district keeps its code; its city and province fall back");
        Check(CompanionRegions.DefaultChain(CompanionRegions.Taiwan).Select(r => r.Code).SequenceEqual(["TW-061", "TW-061-01"])
            && CompanionRegions.DefaultChain(CompanionRegions.China).Select(r => r.Code).SequenceEqual(["34", "3401", "340104"])
            && CompanionRegions.DefaultChain(CompanionRegions.UnitedStates).Length == 0, "switching country starts at 臺北市中正區, 合肥市蜀山區 or the first entries");
        await corrupt.RefreshIfDue("710101");
        Check(corrupt.CachedContext("710101").Contains("臺灣省 · 臺北市 · 中正區（區縣中心附近的天氣估算）") && corrupt.CachedContext("710101").Contains("北京時間")
            && feed.LastUrl.Contains("Asia%2FShanghai") && corrupt.CachedContext("710101") != corrupt.CachedContext("TW-061-01"),
            "a saved 台湾省 district keeps China-style weather, apart from the 臺灣 township");
        var tokyo = CompanionRegions.District("JP-1850147");
        Check(tokyo.Code == "JP-1850147" && CompanionRegions.FullName(tokyo).StartsWith("日本 Japan · ") && CompanionRegions.District("JP.40").Code == CompanionRegions.DefaultCode, "world city is a valid leaf and divisions fall back");
        Check(countries.Where(c => c != CompanionRegions.Taiwan && c != CompanionRegions.China && c != CompanionRegions.UnitedStates).All(c => CompanionRegions.Children(c.Root).Length > 0
            && CompanionRegions.Children(c.Root).All(d => CompanionRegions.Children(d.Code).All(x => x.Latitude != null))), "every world country has located cities");
        await corrupt.RefreshIfDue("JP-1850147");
        var world = corrupt.CachedContext("JP-1850147");
        Check(feed.LastUrl.Contains("timezone=auto") && !feed.LastUrl.Contains("fahrenheit") && world.Contains("21℃") && world.Contains("當地時間") && world.Contains("公里/小時"), "world weather uses Celsius, km/h and local time");
        await corrupt.RefreshIfDue("350623");
        Check(corrupt.CachedContext("350623").Contains("℃") && corrupt.CachedContext("350623").Contains("北京時間") && feed.LastUrl.Contains("Asia%2FShanghai"), "China weather keeps Celsius and Beijing time");
        Check(CompanionWeather.Description(2) == "局部多雲" && CompanionWeather.Description(0) == "晴朗", "weather codes read in Taiwan wording");

        // Taiwan: without a CWA authorization code an Open-Meteo estimate at the township centre, with one the CWA township
        // forecast; the two never share an entry or a retry spacing.
        var twNow = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        feed.Now = twNow; feed.Block = null; feed.Offline = feed.Invalid = feed.Stale = false;
        string twFile = Path.Combine(directory, Guid.NewGuid() + ".json");
        using var taiwan = new CompanionWeather(web, twFile, () => twNow);
        await taiwan.RefreshIfDue("TW-061-01");
        var estimate = taiwan.CachedContext("TW-061-01");
        Check(feed.LastUrl.Contains("latitude=25.03245") && feed.LastUrl.Contains("timezone=Asia%2FTaipei") && !feed.LastUrl.Contains("fahrenheit"),
            "Taiwan without a CWA code asks Open-Meteo at the township centre in Taiwan time");
        Check(estimate.StartsWith("地點：臺灣 · 臺北市 · 中正區（鄉鎮市區中心附近的天氣估算）。天氣資料記錄時間：2026-10-07T08:00（臺灣時間）。記錄時晴朗，")
            && estimate.Contains("氣溫21℃") && estimate.Contains("風速8公里/小時") && estimate.EndsWith("資料日期2026-10-07最低18℃、最高25℃，最高降水機率10%。"),
            "the Taiwan estimate says it is near the township centre, in Celsius and km/h");
        int calls = feed.Calls;
        taiwan.CwaApiKey = "CWA-TEST-CODE";
        Check(taiwan.CachedContext("TW-061-01") == "", "a CWA code never serves the Open-Meteo estimate");
        await taiwan.RefreshIfDue("TW-061-01");
        var forecast = taiwan.CachedContext("TW-061-01");
        Check(feed.Calls == calls + 1 && feed.LastAuthorization == "CWA-TEST-CODE" && feed.LastUrl.Contains("/F-D0047-061?")
            && Uri.UnescapeDataString(feed.LastUrl).EndsWith("LocationName=中正區"), "setting a CWA code fetches the township forecast at once with the code as Authorization");
        Check(forecast == "地點：臺灣 · 臺北市 · 中正區（中央氣象署鄉鎮天氣預報）。10月7日06:00到10月7日09:00：晴。降雨機率10%。溫度攝氏24至27度。"
            + "10月7日09:00到10月7日12:00：多雲。降雨機率20%。溫度攝氏27至30度。", "the CWA forecast reads the next two three-hour periods as before");
        await taiwan.RefreshIfDue("TW-061-01");
        Check(feed.Calls == calls + 1, "CWA forecasts keep the hourly spacing");
        taiwan.CwaApiKey = "";
        Check(taiwan.CachedContext("TW-061-01") == estimate, "clearing the code returns to the estimate, never the CWA text");
        await taiwan.RefreshIfDue("TW-061-01");
        Check(feed.Calls == calls + 1, "a recent estimate needs no new fetch");
        taiwan.CwaApiKey = "CWA-TEST-CODE";
        Check(taiwan.CachedContext("TW-061-01") == forecast && !File.ReadAllText(twFile).Contains("CWA-TEST-CODE"), "the code brings the forecast back and is never cached");
        using (var twRestart = new CompanionWeather(web, twFile, () => twNow))
        {
            Check(twRestart.CachedContext("TW-061-01") == estimate, "a restart without the code serves the saved estimate");
            twRestart.CwaApiKey = "CWA-TEST-CODE";
            await twRestart.RefreshIfDue("TW-061-01");
            Check(twRestart.CachedContext("TW-061-01") == forecast && feed.Calls == calls + 1, "a restart with the code reuses the saved CWA forecast");
        }
        string legacyFile = Path.Combine(directory, Guid.NewGuid() + ".json");
        File.WriteAllText(legacyFile, JsonSerializer.Serialize(new[] { new { Code = "TW-061-01", FetchedAt = twNow, ObservedAt = twNow, Context = forecast } }));
        using (var legacy = new CompanionWeather(web, legacyFile, () => twNow))
        {
            Check(legacy.CachedContext("TW-061-01") == "", "a forecast saved by the earlier Taiwan edition is never served as an estimate");
            legacy.CwaApiKey = "CWA-TEST-CODE";
            Check(legacy.CachedContext("TW-061-01") == forecast, "a forecast saved by the earlier Taiwan edition stays a CWA forecast");
        }
        feed.RefusedCode = "CWA-WRONG-CODE";
        taiwan.CwaApiKey = "CWA-WRONG-CODE";
        await taiwan.RefreshIfDue("TW-061-01");
        Check(feed.Calls == calls + 2 && feed.LastAuthorization == "CWA-WRONG-CODE" && taiwan.CachedContext("TW-061-01") == forecast,
            "replacing the code fetches again at once and keeps the last forecast meanwhile");
        try { await taiwan.Context("TW-061-02", default); throw new Exception("refused code accepted"); }
        catch (IOException e) { Check(e.Message == "中央氣象署不接受這組授權碼，請確認授權碼是否正確。" && feed.Calls == calls + 3, "a refused code is reported in the Taiwan edition's words"); }
        await taiwan.RefreshIfDue("TW-061-02");
        Check(feed.Calls == calls + 3, "a refused code keeps the hourly spacing");
        taiwan.CwaApiKey = "CWA-TEST-CODE";
        await taiwan.RefreshIfDue("TW-061-02");
        Check(feed.Calls == calls + 4 && taiwan.CachedContext("TW-061-02").StartsWith("地點：臺灣 · 臺北市 · 大同區（中央氣象署鄉鎮天氣預報）。10月7日06:00到"),
            "a corrected code is tried at once");
        feed.Invalid = true;
        try { await taiwan.Context("TW-061-04", default); throw new Exception("unreadable CWA answer accepted"); }
        catch (IOException e) { Check(e.Message == "暫時無法取得中央氣象署天氣資料", "an unreadable CWA answer is reported as unavailable"); }
        feed.Invalid = false;
        await taiwan.RefreshIfDue("TW-061-03");
        calls = feed.Calls;
        taiwan.CwaApiKey = "";
        await taiwan.RefreshIfDue("TW-061-03");
        Check(feed.Calls == calls + 1 && feed.LastUrl.Contains("api.open-meteo.com") && taiwan.CachedContext("TW-061-03").Contains("（鄉鎮市區中心附近的天氣估算）"),
            "CWA retry spacing never delays the estimate");
        taiwan.CwaApiKey = "CWA-TEST-CODE";
        feed.Offline = true;
        twNow = twNow.AddHours(1);
        await taiwan.RefreshIfDue("TW-061-02");
        calls = feed.Calls;
        twNow = twNow.AddMinutes(1);
        await taiwan.RefreshIfDue("TW-061-02");
        Check(feed.Calls == calls && taiwan.CachedContext("TW-061-02").Contains("中央氣象署"), "an unanswered CWA refresh keeps the forecast and waits");
        twNow = twNow.AddMinutes(1);
        await taiwan.RefreshIfDue("TW-061-02");
        Check(feed.Calls == calls + 1, "an unanswered CWA refresh is retried after two minutes, not an hour");
        feed.Offline = false;
        if (live)
        {
            string actualFile = Path.Combine(directory, "live-weather.json");
            using var actualWeb = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var actual = new CompanionWeather(actualWeb, actualFile);
            var actualContext = await actual.Context("350622", default);
            Check(actualContext.Contains("℃") && File.Exists(actualFile), "live Open-Meteo fetch is parsed and persisted");
            feed.Offline = true;
            using var offline = new CompanionWeather(web, actualFile);
            Check(await offline.Context("350622", default) == actualContext, "live downloaded weather is reusable after restart with offline HTTP transport");
            Console.WriteLine("LIVE " + actualContext);
            var actualUs = await actual.Context("US-36061", default);
            Check(actualUs.Contains("℉") && actualUs.Contains("New York County"), "live Open-Meteo US fetch is parsed");
            Console.WriteLine("LIVE " + actualUs);
            var actualLondon = await actual.Context("GB-2643743", default);
            Check(actualLondon.Contains("℃") && actualLondon.Contains("London"), "live Open-Meteo world fetch is parsed");
            Console.WriteLine("LIVE " + actualLondon);
            var actualTaiwan = await actual.Context("TW-061-01", default);
            Check(actualTaiwan.Contains("℃") && actualTaiwan.Contains("中正區") && actualTaiwan.Contains("臺灣時間"), "live Open-Meteo Taiwan estimate is parsed");
            Console.WriteLine("LIVE " + actualTaiwan);
        }
        Console.WriteLine($"RESULT {checks} weather cache checks passed ({CompanionBrain.InputMode})");
        return 0;
    }
}
