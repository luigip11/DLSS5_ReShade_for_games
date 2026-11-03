using System.ComponentModel;
using System.Diagnostics;

namespace GamePanel.Core;

/// <summary>Metadata declared by the executable, not a verified game patch identity.</summary>
public sealed record ExecutableVersion(string? ProductVersion, string? FileVersion)
{
    public static ExecutableVersion Read(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new(Clean(info.ProductVersion), Clean(info.FileVersion));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            // Missing or unreadable version resources must not prevent game analysis.
            return new(null, null);
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();
        return text.Length == 0 ? null : text;
    }
}
