using System.Security.Cryptography;
using System.Text;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Structural recognition and local identity, not a certification of the mod archive.</summary>
public static class Underground1RemixInstallation
{
    private static readonly (string Path, bool X64, bool Dll)[] Required =
    [
        ("d3d9.dll", false, true), (".trex/d3d9.dll", true, true),
        (".trex/NvRemixBridge.exe", true, false), ("dinput8.dll", false, true),
        ("scripts/NFSUnderground.WidescreenFix.asi", false, true), ("scripts/RTXRemix.asi", false, true)
    ];
    internal static readonly string[] DlssFiles = ["nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll"];
    public static bool HasRenderer(string root)
    {
        try { Validate(root); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException or ArgumentException) { return false; }
    }
    private static void Validate(string root)
    {
        foreach (var item in Required)
        {
            var path = GameFile(root, item.Path); GuardPath(path);
            if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("Componente UG1 Remix mancante: " + item.Path);
            var pe = PeInspector.Read(path);
            if (pe.IsDll != item.Dll || pe.Architecture != (item.X64 ? "x64 (64 bit)" : "x86 (32 bit)"))
                throw new InvalidDataException("Formato componente UG1 Remix non compatibile: " + item.Path);
        }
        foreach (var name in new[] { "rtx.conf", "scripts/NFSUnderground.WidescreenFix.ini" })
        { var path = GameFile(root, name); GuardPath(path); if (!File.Exists(path)) throw new InvalidDataException("Configurazione UG1 Remix mancante: " + name); }
    }
    internal static string Fingerprint(string root, CancellationToken token)
    {
        Validate(root);
        var paths = Required.Select(x => x.Path).Concat(DlssFiles.Select(x => ".trex/" + x))
            .Concat(DlssFiles).Where(x => File.Exists(GameFile(root, x))).Order(StringComparer.Ordinal);
        var text = new StringBuilder("GamePanel.UG1.LocalRemix.v1\n");
        foreach (var relative in paths)
        {
            token.ThrowIfCancellationRequested(); var path = GameFile(root, relative); GuardPath(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is < 1 or > 256 * 1024 * 1024) throw new InvalidDataException("Dimensione componente Remix non valida: " + relative);
            text.Append(relative).Append(':').Append(Convert.ToHexString(SHA256.HashData(stream))).Append('\n');
        }
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
    public static bool HasDlssFile(string root, string name)
    {
        if (!DlssFiles.Contains(name, StringComparer.Ordinal)) return false;
        foreach (var relative in new[] { ".trex/" + name, name })
        {
            var path = GameFile(root, relative); GuardPath(path); if (!File.Exists(path)) continue;
            var pe = PeInspector.Read(path); if (pe.IsDll && pe.Architecture == "x64 (64 bit)") return true;
        }
        return false;
    }
}
