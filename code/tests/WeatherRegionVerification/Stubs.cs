// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
global using BigBlueFish.Diagnostics;

namespace BigBlueFish.Windows;

// What the linked shared files expect from the app around them.
internal static class App { internal static string[] Args = []; }
internal static class CompanionBrain { internal const string InputMode = "WeatherRegionVerification"; }
// CompanionPreferences.AiPausedOnBattery (1.1.4) asks for the power source; here always on AC unless a check says otherwise.
internal static class CompanionNative
{
    internal static bool? TestPowerOverride { get; set; }
    internal static bool OnAC => TestPowerOverride ?? true;
}
