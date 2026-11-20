// Modified by Methrew on 2026-11-21 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.5, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BigBlueFish.Deployment;

internal static class ReleaseAssetDigest
{
    internal static string Sha256(JsonElement asset)
    {
        string digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("GitHub 還沒有提供有效的安裝檔檢查碼資訊，請稍後再試一次。");
        return digest[7..].ToLowerInvariant();
    }
}
