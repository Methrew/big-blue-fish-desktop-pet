// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

internal sealed record CompanionRegion(string Code, string ParentCode, int Level, string Name, double? Latitude, double? Longitude)
{
    // The name pickers show and search (Traditional script); Name stays the data's own.
    public string Display => CompanionRegions.DisplayName(this);
    // What a screen reader says for a picker item (WPF's UI Automation name of a ComboBox item is its ToString): the record's
    // own ToString read out every field and the Simplified Name, e.g. "CompanionRegion { Code = TW-061, … }" (2026-11-22).
    public override string ToString() => Display;
}

// Country root: China is province → city → district (root "0", 台湾省 71 included as in the original list); the US is
// state → county (root "US"); 臺灣 is county → township (root "TW", taiwan-regions.json); other countries are
// first-level division → city (root = ISO code, from GeoNames cities15000).
internal sealed record CompanionCountry(string Code, string Name, string Root, int LeafLevel, string[] Captions)
{
    public string Display => CompanionRegions.DisplayName(this);
    // As CompanionRegion: a screen reader says the Traditional name, not "CompanionCountry { Code = CM, Name = … }".
    public override string ToString() => Display;
}

internal static class CompanionRegions
{
    // New installs, and a saved code that is no place of the lists, start at 臺北市中正區; a saved place is never changed.
    internal const string DefaultCode = "TW-061-01";
    // Where the picker starts after switching to China (合肥市蜀山區, the original edition's default).
    internal const string ChinaDefaultCode = "340104";
    // 臺灣 townships use CWA township forecasts when an authorization code is set, otherwise an Open-Meteo estimate
    // near the township centre; codes are TW-<F-D0047 dataset>-<index>. China's own 台湾省 (71…) stays as it was:
    // city-level Open-Meteo points in Beijing time.
    internal const string TaiwanRoot = "TW";
    internal static readonly CompanionCountry Taiwan = new("TW", "臺灣 Taiwan", TaiwanRoot, 1, ["縣市", "鄉鎮市區"]);
    internal static readonly CompanionCountry China = new("CN", "中國 China", "0", 2, ["省 / 自治區 / 直轄市", "市 / 州", "縣 / 區"]);
    internal static readonly CompanionCountry UnitedStates = new("US", "美國 United States", "US", 1, ["州", "郡 (County)"]);
    // China first, then every other country by English name, as in the original; 臺灣 Taiwan is one of them (after Syria).
    internal static readonly CompanionCountry[] Countries = LoadCountries();
    private static readonly Dictionary<string, CompanionCountry> ByCountry = Countries.ToDictionary(x => x.Code);
    internal static readonly IReadOnlyList<CompanionRegion> All = Load();
    private static readonly Dictionary<string, CompanionRegion> ByCode = All.ToDictionary(x => x.Code);
    private static readonly ILookup<string, CompanionRegion> ByParent = All.ToLookup(x => x.ParentCode);

    // taiwan-regions.json keeps its own root row (TW, level 0) above counties (1) and townships (2). As for the other
    // countries, the root is the country itself: the row is left out, counties become level 0 and townships level 1.
    private static CompanionRegion[] Load() =>
        [.. Read<CompanionRegion>("china-regions.json"), .. Read<CompanionRegion>("us-regions.json"), .. Read<CompanionRegion>("world-regions.json"),
         .. Read<CompanionRegion>("taiwan-regions.json").Where(x => x.Code != TaiwanRoot).Select(x => x with { Level = x.Level - 1 })];

    private sealed record WorldCountry(string Code, string Name, string EnglishName);

    private static CompanionCountry[] LoadCountries() =>
        [China, .. Read<WorldCountry>("world-countries.json").Append(new("US", UnitedStates.Name, "United States")).Append(new("TW", Taiwan.Name, "Taiwan"))
            .OrderBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Code == "US" ? UnitedStates : c.Code == "TW" ? Taiwan : new CompanionCountry(c.Code, c.Name + " " + c.EnglishName, c.Code, 1, ["一級行政區", "城市"]))];

    private static T[] Read<T>(string file)
    {
        using var stream = typeof(CompanionRegions).Assembly.GetManifestResourceStream(
            "BigBlueFish.Windows.assets.regions." + file)
            ?? throw new InvalidOperationException("缺少地區資料：" + file);
        return JsonSerializer.Deserialize<T[]>(stream) ?? throw new InvalidOperationException("地區資料為空：" + file);
    }

    // China codes are numeric; Taiwan, US and world codes start with the ISO country code ("TW-061-01", "US-06037", "JP.40", "JP-1850147").
    internal static CompanionCountry Country(CompanionRegion region) =>
        char.IsAsciiDigit(region.Code[0]) ? China : ByCountry[region.Code[..2]];

    internal static bool IsUs(CompanionRegion region) => Country(region) == UnitedStates;
    // Only the 臺灣 list (TW-…); China's 台湾省 rows are China's.
    internal static bool IsTaiwan(CompanionRegion region) => region.Code.StartsWith(TaiwanRoot + "-", StringComparison.Ordinal);
    internal static string CwaDataset(CompanionRegion district) => "F-D0047-" + district.ParentCode[3..];

    internal static CompanionRegion District(string? code) =>
        code != null && ByCode.TryGetValue(code, out var region) && region.Level == Country(region).LeafLevel ? region : ByCode[DefaultCode];

    internal static CompanionRegion Parent(CompanionRegion region) => ByCode[region.ParentCode];
    internal static CompanionRegion[] Children(string code) => ByParent[code].ToArray();

    // Root-to-leaf chain, e.g. [臺北市, 中正區], [安徽省, 合肥市, 蜀山区], [加利福尼亚州 CA, Los Angeles County] or [Tokyo, Shinjuku].
    internal static CompanionRegion[] Chain(CompanionRegion leaf)
    {
        var chain = new List<CompanionRegion> { leaf };
        while (ByCode.TryGetValue(chain[^1].ParentCode, out var parent)) chain.Add(parent);
        chain.Reverse();
        return [.. chain];
    }

    // Where a picker starts after switching country: 臺北市中正區 for 臺灣, 合肥市蜀山區 for China, the first entries elsewhere.
    internal static CompanionRegion[] DefaultChain(CompanionCountry country) =>
        country == Taiwan ? Chain(ByCode[DefaultCode]) : country == China ? Chain(ByCode[ChinaDefaultCode]) : [];

    // In the names the pickers show: 臺灣 · 臺北市 · 中正區 (as the CWA weather text has always read), 安徽省 · 合肥市 · 蜀山區,
    // 臺灣省 · 臺北市 · 中正區 for China's own 台湾省 (China places never name the country), 日本 Japan · Tokyo · Shinjuku.
    internal static string FullName(CompanionRegion district)
    {
        var names = Chain(district).Select(DisplayName);
        var country = Country(district);
        if (country != China) names = names.Prepend(country == Taiwan ? "臺灣" : DisplayName(country));
        return string.Join(" · ", names.Distinct());
    }

    // The names pickers and weather text show, in Traditional script. The data files keep their own (Simplified) names,
    // codes and order; region-display-names.json, made by code/tools/make-region-display-names.py (OpenCC s2tw without
    // TWPhrases, so script only, plus reviewed corrections such as 湖里區 and 巨鹿縣), maps a code to its source name and
    // the name shown. An entry counts only while the data still has that source name. 臺灣 rows are Traditional already.
    internal static string DisplayName(CompanionRegion region) =>
        Names.Value.Regions.TryGetValue(region.Code, out var pair) && pair.Length == 2 && pair[0] == region.Name ? pair[1] : region.Name;

    // China, the US and 臺灣 are named here; another country is "<Chinese name> <English name>" with the Chinese part shown.
    internal static string DisplayName(CompanionCountry country) =>
        Names.Value.Countries.TryGetValue(country.Code, out var pair) && pair.Length == 2 && country.Name.StartsWith(pair[0] + " ", StringComparison.Ordinal)
            ? pair[1] + country.Name[pair[0].Length..] : country.Name;

    private sealed record DisplayNames(Dictionary<string, string[]> Regions, Dictionary<string, string[]> Countries);
    private static readonly Lazy<DisplayNames> Names = new(LoadNames);

    private static DisplayNames LoadNames()
    {
        using var stream = typeof(CompanionRegions).Assembly.GetManifestResourceStream("BigBlueFish.Windows.assets.regions.region-display-names.json");
        // A build without the table still works: the pickers then show the data's own names.
        if (stream == null) return new([], []);
        using var json = JsonDocument.Parse(stream);
        Dictionary<string, string[]> Section(string name) =>
            json.RootElement.TryGetProperty(name, out var section) ? section.Deserialize<Dictionary<string, string[]>>() ?? [] : [];
        return new(Section("regions"), Section("countries"));
    }
}
