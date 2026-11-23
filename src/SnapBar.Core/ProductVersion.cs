using System;
using System.Text.RegularExpressions;

namespace SnapBar.Core;

// Also compiled by PowerShell release tooling: keep this independent of Core services.
public sealed class ProductVersion : IComparable<ProductVersion>
{
    private static readonly Regex Pattern = new Regex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);
    public string Version { get; }
    public string Channel { get; }
    public bool IsPublic => Channel == "Stable" || Channel == "Preview";

    private ProductVersion(string version, string channel) { Version = version; Channel = channel; }

    public int CompareTo(ProductVersion? other)
    {
        if (other == null) return 1;
        var left = Version.Split(new[] { '-' }, 2);
        var right = other.Version.Split(new[] { '-' }, 2);
        var leftCore = left[0].Split('.');
        var rightCore = right[0].Split('.');
        for (var i = 0; i < 3; i++)
        {
            var result = CompareNumeric(leftCore[i], rightCore[i]);
            if (result != 0) return result;
        }
        if (left.Length == 1 || right.Length == 1)
            return left.Length == right.Length ? 0 : left.Length == 1 ? 1 : -1;
        var leftParts = left[1].Split('.');
        var rightParts = right[1].Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftNumeric = Regex.IsMatch(leftParts[i], @"\A[0-9]+\z");
            var rightNumeric = Regex.IsMatch(rightParts[i], @"\A[0-9]+\z");
            var result = leftNumeric && rightNumeric ? CompareNumeric(leftParts[i], rightParts[i])
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1)
                : string.CompareOrdinal(leftParts[i], rightParts[i]);
            if (result != 0) return result;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int CompareNumeric(string left, string right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);

    public static ProductVersion Parse(string version)
    {
        if (version == null) throw new FormatException("Version is required.");
        var match = Pattern.Match(version);
        if (!match.Success) throw new FormatException("Invalid semantic version.");
        var suffix = match.Groups[4].Value;
        foreach (var part in suffix.Split('.'))
            if (part.Length > 1 && part[0] == '0' && Regex.IsMatch(part, @"\A[0-9]+\z"))
                throw new FormatException("Numeric prerelease identifiers cannot have leading zeroes.");
        string channel;
        if (suffix.Length == 0) channel = "Stable";
        else if (suffix == "dev") channel = "Development";
        else if (suffix == "validation" || Regex.IsMatch(suffix, @"\Aci\.[1-9][0-9]*\.g[0-9a-f]{12}\z")) channel = "Test";
        else
        {
            var prefix = suffix.Split('.')[0];
            if (prefix == "dev" || prefix == "ci" || prefix == "validation")
                throw new FormatException("Invalid reserved version suffix.");
            channel = "Preview";
        }
        return new ProductVersion(version, channel);
    }
}
