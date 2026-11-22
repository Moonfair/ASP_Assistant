using System.Globalization;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

// Supported release tags are vX.Y.Z and vX.Y.Z-beta.N. A final release sorts
// after every beta of the same version; sequence numbers are numeric, not lexical.
internal sealed partial record ReleaseVersion(Version BaseVersion, int? Beta) : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.([1-9][0-9]*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static ReleaseVersion? Parse(string? text)
    {
        if (text is null || text.Length > 64) return null;
        var m = Pattern().Match(text);
        if (!m.Success || !Version.TryParse(string.Join(".", m.Groups[1], m.Groups[2], m.Groups[3]), out var version))
            return null;
        if (!m.Groups[4].Success) return new(version, null);
        return int.TryParse(m.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? new(version, n) : null;
    }

    internal static ReleaseVersion? FromTag(string? tag) =>
        tag?.StartsWith('v') == true ? Parse(tag[1..]) : null;

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var byBase = BaseVersion.CompareTo(other.BaseVersion);
        if (byBase != 0) return byBase;
        if (Beta.HasValue && other.Beta.HasValue) return Beta.Value.CompareTo(other.Beta.Value);
        return other.Beta.HasValue.CompareTo(Beta.HasValue);
    }

    public override string ToString() => Beta is { } n ? $"{BaseVersion}-beta.{n}" : BaseVersion.ToString();
}
