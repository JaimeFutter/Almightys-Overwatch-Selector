namespace Aos.Core.Updates;

/// <summary>
/// A semantic version (1.2.3 or 1.2.3-beta.1), compared by the SemVer 2.0 rules: a pre-release
/// sorts before its release (1.0.0-beta &lt; 1.0.0), and numeric identifiers compare as numbers.
/// Build metadata after '+' is ignored. A leading "v" is allowed.
/// </summary>
public sealed record SemVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<SemVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>True for 0.x versions and suffixed versions: those users also get offered pre-releases.</summary>
    public bool OnPreReleaseChannel => Major == 0 || IsPreRelease;

    public static bool TryParse(string? text, out SemVersion version)
    {
        version = new SemVersion(0, 0, 0, "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        int dash = s.IndexOf('-');
        string pre = dash >= 0 ? s[(dash + 1)..] : "";
        string core = dash >= 0 ? s[..dash] : s;
        string[] parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        int[] nums = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0) return false;
        if (dash >= 0 && pre.Length == 0) return false;
        version = new SemVersion(nums[0], nums[1], nums[2], pre);
        return true;
    }

    public static SemVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a version.");

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;   // 1.0.0 > 1.0.0-beta
        if (!other.IsPreRelease) return -1;

        string[] a = PreRelease.Split('.'), b = other.PreRelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = int.TryParse(a[i], out int ai), bn = int.TryParse(b[i], out int bi);
            c = (an, bn) switch
            {
                (true, true) => ai.CompareTo(bi),
                (true, false) => -1,   // numeric identifiers sort before alphanumeric ones
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
