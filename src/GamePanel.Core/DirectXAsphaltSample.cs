using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public static class DirectXAsphaltSample
{
    public const string Sha256 = "7adaf63cc2234e6dc29ab87faa01fbc273523580ab14ef30e52babfe1a04e98f";
    public const long Bytes = 2796344;
    public static string GetVerifiedPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DirectXAsphalt", "Asphalt.dds");
        GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Materiale GamePanel mancante: usa il pacchetto completo.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != Bytes || Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != Sha256)
            throw new InvalidDataException("Il materiale GamePanel non corrisponde alla versione verificata.");
        var info = DirectXTextureData.ReadDds(path);
        if (!info.FullMipChain || info.Width != 2048 || info.Height != 2048 || info.Sha256 != Sha256)
            throw new InvalidDataException("Materiale GamePanel non valido.");
        return path;
    }
}
