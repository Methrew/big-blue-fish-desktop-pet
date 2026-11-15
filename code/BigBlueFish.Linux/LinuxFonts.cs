using Avalonia;
using Avalonia.Media;

namespace BigBlueFish.Windows;

// Taiwan edition fonts of the Avalonia pet on Linux and macOS. The bubble uses the bundled jf open huninn 2.1
// (Res/Font, linked as Assets/), and characters it lacks fall back to the system's Traditional Chinese face.
// The avares URI is built from the assembly name, which is BigBlueFish.Linux or BigBlueFish.Mac.
// The Traditional faces show their Taiwan forms only while the text is shaped without a Chinese language: Avalonia
// passes the current culture's two-letter name to HarfBuzz, and "zh" (from zh-TW too) selects the faces' Simplified
// ZHS locl forms, such as ，。 in the lower left. Program.Main sets the invariant culture for that.
internal static class LinuxFonts
{
    static string SystemChinese => OperatingSystem.IsMacOS() ? "PingFang TC" : "Noto Sans CJK TC";
    internal static FontFamily Bubble => new("avares://" + typeof(App).Assembly.GetName().Name + "/Assets#jf-openhuninn-2.1, " + SystemChinese);
    internal static FontFamily Interface => new(SystemChinese);
    internal static FontFamily Log => new(OperatingSystem.IsMacOS() ? "Menlo, PingFang TC" : "DejaVu Sans Mono, Noto Sans CJK TC");

    // macOS only: controls that name no font (menus, tooltips, the Agent input, combo lists) default to Helvetica,
    // whose Chinese comes from CoreText's per-language fallback and turns into PingFang SC on an English or
    // Simplified Chinese system. Linux keeps fontconfig's choice: a missing default family must not stop startup.
    internal static AppBuilder WithTaiwanFonts(this AppBuilder builder) => !OperatingSystem.IsMacOS() ? builder
        : builder.With(new FontManagerOptions { DefaultFamilyName = "PingFang TC", FontFallbacks = [new FontFallback { FontFamily = new FontFamily("PingFang TC") }] });
}
