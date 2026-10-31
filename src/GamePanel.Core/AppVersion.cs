using System.Reflection;
using System.Text.RegularExpressions;

namespace GamePanel.Core;

public sealed record AppVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<AppVersion>
{
    public static AppVersion Current => Parse(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);
    public static AppVersion Parse(string value)
    {
        if (value.Length > 100) throw new InvalidDataException("Versione non valida.");
        var m = Regex.Match(value, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var a) || !int.TryParse(m.Groups[2].Value, out var b) || !int.TryParse(m.Groups[3].Value, out var c)) throw new InvalidDataException("Versione non valida.");
        var pre = m.Groups[4].Success ? m.Groups[4].Value : null;
        if (pre?.Split('.').Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0') == true) throw new InvalidDataException("Versione prerelease ambigua.");
        return new(a, b, c, pre);
    }
    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        foreach (var n in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch) }) if (n != 0) return n;
        if (PreRelease is null || other.PreRelease is null) return PreRelease is null ? other.PreRelease is null ? 0 : 1 : -1;
        var left = PreRelease.Split('.'); var right = other.PreRelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = left[i].All(char.IsAsciiDigit); var rn = right[i].All(char.IsAsciiDigit);
            int result;
            if (ln && rn)
            {
                result = left[i].Length.CompareTo(right[i].Length);
                if (result == 0) result = string.CompareOrdinal(left[i], right[i]);
            }
            else result = ln != rn ? ln ? -1 : 1 : string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (PreRelease is null ? "" : "-" + PreRelease);
}
