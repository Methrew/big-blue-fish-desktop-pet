// Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BigBlueFish.Deployment;

// A Taiwan edition version: the original's x.y.z plus an optional -tw.N revision, such as 1.1.3-tw.1.
// A plain 1.1.3 is revision 0, so 1.1.3 < 1.1.3-tw.1 < 1.1.3-tw.2 < 1.1.4. Other suffixes (beta builds) do not parse.
internal readonly record struct ReleaseVersion(Version Base, int Revision) : IComparable<ReleaseVersion>
{
    static readonly Regex Pattern = new(@"^v?(\d+\.\d+\.\d+)(?:-tw\.(\d+))?$", RegexOptions.CultureInvariant);

    internal static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        var match = Pattern.Match((text ?? "").Split('+')[0].Trim());
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var number)) return false;
        int revision = 0;
        if (match.Groups[2].Success && !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out revision)) return false;
        version = new(number, revision);
        return true;
    }

    // The running build: InformationalVersion carries the -tw.N revision; the numeric assembly version is the fallback.
    internal static ReleaseVersion Current(Assembly assembly) =>
        TryParse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, out var version) ? version
            : new(new Version((assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3)), 0);

    public int CompareTo(ReleaseVersion other) => Base != other.Base ? Base.CompareTo(other.Base) : Revision.CompareTo(other.Revision);
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public override string ToString() => Revision == 0 ? Base.ToString(3) : Base.ToString(3) + "-tw." + Revision.ToString(CultureInfo.InvariantCulture);
}
