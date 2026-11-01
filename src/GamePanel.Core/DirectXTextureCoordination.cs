using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

internal static class DirectXTextureCoordination
{
    internal static bool HasPending(string folder)
    {
        foreach (var name in new[] { "directx-operation.json", "directx-replacements-operation.json" })
        {
            var path = Path.Combine(folder, name); GuardPath(path);
            if (AttributesOrMissing(path) is not FileAttributes attributes) continue;
            if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0 || new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidDataException("Journal DirectX non regolare o fuori limite.");
            try
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > 1024 * 1024) throw new InvalidDataException("Journal DirectX fuori limite.");
                var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw new InvalidDataException("Journal DirectX cresciuto durante la lettura.");
                using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 24 });
                if (!doc.RootElement.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String
                    || status.GetString() is not ("Pending" or "Complete" or "RolledBack"))
                    throw new InvalidDataException("Journal DirectX privo di stato valido.");
                if (status.GetString() == "Pending") return true;
            }
            catch (JsonException e) { throw new InvalidDataException("Journal DirectX non valido.", e); }
        }
        return false;
    }
}
