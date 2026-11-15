// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Deployment;

internal sealed record AvailableUpdate(string Version, string Name, string Url, string Sha256, string Notes);

internal static class UpdateService
{
    // This edition's own releases, so an update never replaces it with the original (CompanionEdition).
    internal const string LatestUrl = BigBlueFish.Windows.CompanionEdition.LatestReleaseApi;
    internal static async Task<AvailableUpdate?> Check(string currentVersion, CancellationToken token = default, HttpClient? client = null, string? apiUrl = null)
    {
        using var owned = client == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) } : null;
        var http = client ?? owned!;
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl ?? LatestUrl);
        request.Headers.UserAgent.ParseAdd("BigBlueFish/" + typeof(UpdateService).Assembly.GetName().Version!.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, token);
        // No release published yet: only a manual check reports it, automatic checks stay silent.
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("這個版本目前還沒有發布任何正式版本，之後再來看看。");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        // Compared as (x.y.z, -tw.N); currentVersion is the InformationalVersion, such as 1.1.3-tw.1. Any other suffix
        // (a beta build) counts as its x.y.z.
        if (!ReleaseVersion.TryParse(release.GetProperty("tag_name").GetString(), out var next)
            || !(ReleaseVersion.TryParse(currentVersion, out var current) || ReleaseVersion.TryParse(currentVersion.Split('+')[0].Split('-')[0], out current))
            || !(next > current)) return null;
        string version = next.ToString();
#if COMPANION_SAFE
        string name = $"{BigBlueFish.Windows.CompanionEdition.AssetPrefix}-v{version}-Safe-OCR-Setup.exe";
#else
        string name = $"{BigBlueFish.Windows.CompanionEdition.AssetPrefix}-v{version}-OCR-Setup.exe";
#endif
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (installer.ValueKind == JsonValueKind.Undefined) throw new IOException("新版本的安裝器暫未準備完成，請稍後重試。");
        string digest = installer.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        string hash = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : "";
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
        {
            var checksum = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name.Replace(".exe", ".sha256"));
            if (checksum.ValueKind == JsonValueKind.Undefined) throw new IOException("新版本的校驗檔案暫未準備完成。");
            using var checksumRequest = new HttpRequestMessage(HttpMethod.Get, checksum.GetProperty("browser_download_url").GetString());
            checksumRequest.Headers.UserAgent.ParseAdd("BigBlueFish/" + typeof(UpdateService).Assembly.GetName().Version!.ToString(3));
            using var checksumResponse = await http.SendAsync(checksumRequest, token); checksumResponse.EnsureSuccessStatusCode();
            var line = await checksumResponse.Content.ReadAsStringAsync(token);
            hash = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("更新檔案校驗資訊無效。");
        }
        return new AvailableUpdate(version, name, installer.GetProperty("browser_download_url").GetString()!, hash.ToLowerInvariant(),
            release.GetProperty("body").GetString() ?? "");
    }
}
