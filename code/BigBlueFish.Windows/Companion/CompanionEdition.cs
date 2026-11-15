// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
namespace BigBlueFish.Windows;

// Identity of this Taiwan edition, kept apart from the original so both can be installed side by side
// and update checks never replace it with the original. Credits to the original author stay in the UI and docs.
internal static class CompanionEdition
{
    internal const string DisplayName = "藍色大肥魚";
    internal const string SafeDisplayName = "藍色大肥魚無MCP版";
    internal const string ReleaseRepository = "Methrew/big-blue-fish-desktop-pet";
    internal const string Homepage = "https://github.com/" + ReleaseRepository;
    internal const string ReleasesPage = Homepage + "/releases";
    internal const string LatestReleaseApi = "https://api.github.com/repos/" + ReleaseRepository + "/releases/latest";
    internal const string OriginalRepository = "Bai-Cute/big-blue-fish-desktop-pet";
    internal const string TaiwanEditionRepository = "acer1204/big-blue-fish-desktop-pet";
    // Publisher shown by the Windows installer and the Linux package; release assets start with AssetPrefix
    // (BigBlueFish-TW-v1.1.3-tw.1-macOS-arm64.dmg), so a release of the original never matches an update check here.
    internal const string Publisher = "Methrew";
    internal const string AssetPrefix = "BigBlueFish-TW";
    // macOS bundle identifiers. Each edition's LaunchAgent label is its own bundle identifier; the keychain service
    // is the Standard identifier for both editions, which share one data folder.
    internal const string MacBundleId = "io.github.methrew.bigbluefish.tw";
    internal const string MacSafeBundleId = MacBundleId + ".safe";
    internal const string KeychainService = MacBundleId;
    // Per-user data folder name under ~/Library/Application Support (macOS); the original uses "bigbluefish".
    internal const string MacDataFolder = "bigbluefish-tw";
    internal const string OriginalMacDataFolder = "bigbluefish";
    internal const string Credits = "原作者 Bai-Cute（" + OriginalRepository + "）；台灣版修改 acer1204（" + TaiwanEditionRepository + "）；台灣 macOS 版整合 Methrew。";
}
