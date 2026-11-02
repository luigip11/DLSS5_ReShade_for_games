using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Experimental API-level texture replacement; never loads or patches the inspected EXE.</summary>
public static class DirectXTextureCatalog
{
    public const string Version = "1.2.1";
    public const string SourceCommit = "d600d7bdc8293dd1e372f4565a970af99e0acffd";
    public const string SourceUrl = "https://github.com/BadassBaboon/Texture-Toolkit/tree/" + SourceCommit;
    public const string LoaderUrl = "https://github.com/BadassBaboon/Texture-Toolkit/releases/download/v1.2.1/TextureToolkit-x86.asi";
    public const string LoaderRelativePath = "scripts/TextureToolkit-x86.asi";
    public const string LoaderSha256 = "4c7763ae8b54f5077365729b7360f427109bfb73f9500e2d48e6082930d065e4";
    public const long LoaderBytes = 1122304;
    public const string License = "MIT, BadassBaboon. The SHA-256 pin verifies bytes, not Windows trust or the absence of malware.";

    public static string? CheckGame(GameReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Path.GetFileName(report.ExecutablePath).Equals("speed2.exe", StringComparison.OrdinalIgnoreCase)
            || report.Architecture != "x86 (32 bit)" || !report.GraphicsApis.Contains("Direct3D 9"))
            return "Il percorso DirectX sperimentale richiede Underground 2, speed2.exe x86 Direct3D 9.";
        if (report.HasAntiCheatIndicators || report.ScanWasLimited)
            return "Percorso DirectX non disponibile: indicatori anti-cheat o scansione incompleta.";
        try
        {
            var before = ReadExecutableHash(report.ExecutablePath);
            if (!before.Equals(report.Sha256, StringComparison.OrdinalIgnoreCase))
                return "L'EXE è cambiato dopo l'analisi: esegui di nuovo la scansione.";
            var details = PeInspector.Read(report.ExecutablePath);
            if (details.IsDll || details.Architecture != "x86 (32 bit)"
                || !details.Imports.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase))
                return "Il file reale non è un eseguibile x86 Direct3D 9.";
            if (!ReadExecutableHash(report.ExecutablePath).Equals(before, StringComparison.OrdinalIgnoreCase))
                return "L'EXE è cambiato durante la verifica: esegui di nuovo la scansione.";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException or ArgumentException or OverflowException)
        { return "Verifica DirectX non completata: " + e.Message; }
    }

    private static string ReadExecutableHash(string path)
    {
        GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("L'EXE non è un file regolare.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 64 or > 64 * 1024 * 1024) throw new InvalidDataException("Dimensione EXE fuori limite.");
        var length = input.Length;
        var digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        if (input.Position != length || input.Length != length) throw new InvalidDataException("Dimensione EXE cambiata durante la lettura.");
        return digest;
    }
}
