// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

// AreaCityCode, on Taiwan rows only, is the code of the same county or township under China's 台湾省 (71) in china-regions.json,
// the AreaCity row it was generated from (code/tools/import-taiwan-regions.py); Kinmen and Lienchiang have none.
internal sealed record CompanionRegion(string Code, string ParentCode, int Level, string Name, double? Latitude, double? Longitude, string? AreaCityCode = null);

// Country root: Taiwan is county → township (root "TW"); China is province → city → district (root "0"); the US is
// state → county (root "US"); other countries are first-level division → city (root = ISO code, from GeoNames cities15000).
internal sealed record CompanionCountry(string Code, string Name, string Root, int LeafLevel, string[] Captions);

internal static class CompanionRegions
{
    // New installs start at 臺北市中正區.
    internal const string DefaultCode = "TW-061-01", TaiwanDefaultCode = DefaultCode;
    // Taiwan townships use CWA township forecasts when an authorization code is set, otherwise an Open-Meteo estimate
    // near the township centre; codes are TW-<F-D0047 dataset>-<index>.
    internal const string TaiwanRoot = "TW";
    // China's own list keeps 台湾省 (71); pickers list Taiwan as its own country instead, and District moves a code saved
    // under 71 to the same Taiwan county and township.
    private const string ChinaTaiwanProvince = "71", ChinaDefaultCode = "340104", TaiwanName = "臺灣";
    internal static readonly CompanionCountry Taiwan = new("TW", TaiwanName + " Taiwan", TaiwanRoot, 1, ["縣市", "鄉鎮市區"]);
    internal static readonly CompanionCountry China = new("CN", "中國 China", "0", 2, ["省 / 自治區 / 直轄市", "市 / 州", "縣 / 區"]);
    internal static readonly CompanionCountry UnitedStates = new("US", "美國 United States", "US", 1, ["州", "郡 (County)"]);
    // Taiwan first, then China, then every other country by English name.
    internal static readonly CompanionCountry[] Countries = LoadCountries();
    private static readonly Dictionary<string, CompanionCountry> ByCountry = Countries.ToDictionary(x => x.Code);
    internal static readonly IReadOnlyList<CompanionRegion> All = Load();
    private static readonly Dictionary<string, CompanionRegion> ByCode = All.ToDictionary(x => x.Code);
    private static readonly ILookup<string, CompanionRegion> ByParent = All.Where(x => x.Code != ChinaTaiwanProvince).ToLookup(x => x.ParentCode);
    // AreaCity code under 71 (a county or a township) → the Taiwan code made from it.
    private static readonly Dictionary<string, string> FromAreaCity = All.Where(x => x.AreaCityCode != null).ToDictionary(x => x.AreaCityCode!, x => x.Code);

    // taiwan-regions.json keeps its own root row (TW, level 0) above counties (1) and townships (2). As for the other
    // countries, the root is the country itself: the row is left out, counties become level 0 and townships level 1.
    private static CompanionRegion[] Load() =>
        [.. Read<CompanionRegion>("taiwan-regions.json").Where(x => x.Code != TaiwanRoot).Select(x => x with { Level = x.Level - 1 }),
         .. Read<CompanionRegion>("china-regions.json"), .. Read<CompanionRegion>("us-regions.json"), .. Read<CompanionRegion>("world-regions.json")];

    private sealed record WorldCountry(string Code, string Name, string EnglishName);

    private static CompanionCountry[] LoadCountries() =>
        [Taiwan, China, .. Read<WorldCountry>("world-countries.json").Append(new("US", UnitedStates.Name, "United States"))
            .OrderBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Code == "US" ? UnitedStates : new CompanionCountry(c.Code, c.Name + " " + c.EnglishName, c.Code, 1, ["一級行政區", "城市"]))];

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
    // Only the Taiwan list (TW-…); District never returns a row of China's 台湾省.
    internal static bool IsTaiwan(CompanionRegion region) => region.Code.StartsWith(TaiwanRoot + "-", StringComparison.Ordinal);
    internal static string CwaDataset(CompanionRegion district) => "F-D0047-" + district.ParentCode[3..];

    internal static CompanionRegion District(string? code) =>
        code != null && ByCode.TryGetValue(Current(code), out var region) && region.Level == Country(region).LeafLevel ? region : ByCode[DefaultCode];

    // A code saved under China's 台湾省 (picked in the original edition, 71…) becomes the same Taiwan township, else the first
    // township of the same county, else the default, so that pickers show the place whose weather is used.
    private static string Current(string code)
    {
        if (!code.StartsWith(ChinaTaiwanProvince, StringComparison.Ordinal) || !code.All(char.IsAsciiDigit)) return code;
        if (FromAreaCity.TryGetValue(code, out var same) && ByCode[same].Level == Taiwan.LeafLevel) return same;
        return code.Length >= 4 && FromAreaCity.TryGetValue(code[..4], out var county) && ByParent[county].FirstOrDefault() is { } first
            ? first.Code : DefaultCode;
    }

    internal static CompanionRegion Parent(CompanionRegion region) => ByCode[region.ParentCode];
    // What the location pickers list under a code; China's 台湾省 is not offered.
    internal static CompanionRegion[] Children(string code) => ByParent[code].ToArray();

    // Root-to-leaf chain, e.g. [臺北市, 中正區], [安徽省, 合肥市, 蜀山区], [加利福尼亚州 CA, Los Angeles County] or [Tokyo, Shinjuku].
    internal static CompanionRegion[] Chain(CompanionRegion leaf)
    {
        var chain = new List<CompanionRegion> { leaf };
        while (ByCode.TryGetValue(chain[^1].ParentCode, out var parent)) chain.Add(parent);
        chain.Reverse();
        return [.. chain];
    }

    // Where a picker starts after switching country: 臺北市中正區 for Taiwan, 合肥市蜀山區 for China, the first entries elsewhere.
    internal static CompanionRegion[] DefaultChain(CompanionCountry country) =>
        country == Taiwan ? Chain(ByCode[DefaultCode]) : country == China ? Chain(ByCode[ChinaDefaultCode]) : [];

    // 臺灣 · 臺北市 · 中正區 (as the CWA weather text has always read), 安徽省 · 合肥市 · 蜀山区, 日本 Japan · Tokyo · Shinjuku.
    internal static string FullName(CompanionRegion district)
    {
        var names = Chain(district).Select(x => x.Name);
        var country = Country(district);
        if (country != China) names = names.Prepend(country == Taiwan ? TaiwanName : country.Name);
        return string.Join(" · ", names.Distinct());
    }
}
