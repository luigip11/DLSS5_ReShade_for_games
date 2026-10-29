using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace GamePanel.Core;

public sealed record PeDetails(string Architecture, bool IsDll, IReadOnlyList<string> Imports);
public sealed record ComponentInfo(string RelativePath, string Kind, string Version);
public sealed record GameReport(
    int SchemaVersion, DateTimeOffset InspectedAt, string ExecutablePath,
    string Sha256, string Architecture, IReadOnlyList<string> GraphicsApis,
    IReadOnlyList<ComponentInfo> Components, IReadOnlyList<string> Notes,
    bool HasAntiCheatIndicators, bool ScanWasLimited);

/// <summary>Read PE headers and import descriptors; never loads the inspected image.</summary>
public static class PeInspector
{
    public static PeDetails Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        if (headers.PEHeader is null) throw new InvalidDataException("Il file non contiene un'immagine PE valida.");
        var architecture = headers.CoffHeader.Machine switch
        {
            Machine.I386 => "x86 (32 bit)",
            Machine.Amd64 => "x64 (64 bit)",
            Machine.Arm64 => "ARM64",
            _ => $"Non supportata ({headers.CoffHeader.Machine})"
        };
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = headers.PEHeader.ImportTableDirectory;
        if (directory.RelativeVirtualAddress != 0 && directory.Size >= 20)
        {
            var block = pe.GetSectionData(directory.RelativeVirtualAddress);
            var count = Math.Min(Math.Min(directory.Size, block.Length) / 20, 2048);
            var reader = block.GetReader(0, checked(count * 20));
            for (var i = 0; i < count; i++)
            {
                var originalThunk = reader.ReadUInt32();
                var timestamp = reader.ReadUInt32();
                var forwarder = reader.ReadUInt32();
                var nameRva = reader.ReadUInt32();
                var thunk = reader.ReadUInt32();
                if ((originalThunk | timestamp | forwarder | nameRva | thunk) == 0) break;
                if (nameRva == 0 || nameRva > int.MaxValue)
                    throw new BadImageFormatException("Import descriptor non valido.");
                var nameBlock = pe.GetSectionData((int)nameRva);
                var bytes = nameBlock.GetContent(0, Math.Min(nameBlock.Length, 512));
                var end = bytes.IndexOf((byte)0);
                if (end < 1) throw new BadImageFormatException("Nome DLL non valido.");
                var name = Encoding.ASCII.GetString(bytes.AsSpan(0, end));
                if (name.Any(c => c < 32 || c > 126 || c is '/' or '\\' or ':'))
                    throw new BadImageFormatException("Nome DLL non valido.");
                imports.Add(name);
            }
        }
        return new(architecture, headers.IsDll, imports.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public sealed class GameInspector
{
    private const int MaximumEntries = 6000;
    private const int MaximumDepth = 3;
    private static readonly Dictionary<string, string> ApiImports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8.dll"] = "Direct3D 8", ["d3d9.dll"] = "Direct3D 9",
        ["d3d10.dll"] = "Direct3D 10", ["d3d10_1.dll"] = "Direct3D 10",
        ["d3d11.dll"] = "Direct3D 11", ["d3d12.dll"] = "Direct3D 12",
        ["opengl32.dll"] = "OpenGL", ["vulkan-1.dll"] = "Vulkan", ["ddraw.dll"] = "DirectDraw"
    };
    private static readonly Dictionary<string, string> Components = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nvngx_dlss.dll"] = "DLSS Super Resolution", ["nvngx_dlssd.dll"] = "DLSS Ray Reconstruction",
        ["nvngx_dlssg.dll"] = "DLSS Frame Generation", ["nvngx_dlssnr.dll"] = "DLSS Neural Rendering",
        ["sl.interposer.dll"] = "Streamline", ["OptiScaler.dll"] = "OptiScaler",
        ["ReShade.ini"] = "Configurazione ReShade", ["ReShadePreset.ini"] = "Preset ReShade",
        ["dgVoodoo.conf"] = "Configurazione dgVoodoo", ["dxvk.conf"] = "Configurazione DXVK"
    };
    private static readonly HashSet<string> ProxyNames = new(StringComparer.OrdinalIgnoreCase)
    { "dxgi.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll", "dinput8.dll", "version.dll", "winmm.dll", "opengl32.dll" };

    public GameReport Inspect(string executablePath, CancellationToken token = default)
    {
        var path = Path.GetFullPath(ExecutablePath.Normalize(executablePath));
        var file = new FileInfo(path);
        // GetAttributes preserves access-denied/not-found errors; FileInfo.Exists hides them.
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.Directory))
            throw new InvalidDataException("Il percorso indica una cartella. Seleziona il file .exe al suo interno.");
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Seleziona il file reale, non un collegamento del filesystem.");
        var details = PeInspector.Read(path);
        if (details.IsDll) throw new InvalidDataException("Il file selezionato è una DLL, non un eseguibile.");
        var apis = details.Imports.Where(ApiImports.ContainsKey).Select(x => ApiImports[x]).Distinct().Order().ToArray();
        var notes = new List<string>
        {
            "L'analisi non esegue il gioco e non certifica l'assenza di malware.",
            "Renderer dedotto dagli import statici: può mancare o differire dal renderer scelto dal gioco."
        };
        if (apis.Length == 0) notes.Add("API grafica non determinata: potrebbe essere un launcher o un caricamento dinamico.");
        if (details.Architecture.StartsWith("x86", StringComparison.Ordinal))
            notes.Add("Per il Neural Rendering a 32 bit serve un percorso con helper a 64 bit, da collaudare per gioco.");
        var components = new List<ComponentInfo>();
        var root = file.DirectoryName!;
        var antiCheat = false;
        var limited = false;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var entries = 0;
        while (queue.Count > 0 && !limited)
        {
            token.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            try
            {
                foreach (var entry in new DirectoryInfo(current.Path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > MaximumEntries) { limited = true; break; }
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    { limited = true; break; }
                    if (entry.Name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
                        || entry.Name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase)
                        || entry.Name.Contains("XIGNCODE", StringComparison.OrdinalIgnoreCase)) antiCheat = true;
                    if (entry is DirectoryInfo)
                    {
                        if (current.Depth < MaximumDepth) queue.Enqueue((entry.FullName, current.Depth + 1));
                        continue;
                    }
                    string? kind = Components.GetValueOrDefault(entry.Name);
                    if (kind is null && ProxyNames.Contains(entry.Name)) kind = "DLL proxy: identità da verificare";
                    if (kind is null && (entry.Name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase)
                        || entry.Name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase))) kind = "Add-on: identità da verificare";
                    if (kind is not null)
                    {
                        var version = "—";
                        try { version = FileVersionInfo.GetVersionInfo(entry.FullName).FileVersion ?? "—"; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
                        components.Add(new(Path.GetRelativePath(root, entry.FullName), kind, version));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { notes.Add($"Cartella non leggibile: {Path.GetRelativePath(root, current.Path)}"); limited = true; }
        }
        if (limited) notes.Add("Scansione interrotta o parziale per limite di elementi, collegamenti o accesso negato.");
        notes.Add($"Scansione limitata a {MaximumDepth} livelli e {MaximumEntries} elementi; nessuna ricerca sugli altri dischi.");
        if (antiCheat) notes.Add("Rilevati possibili indicatori anti-cheat. Non applicare mod senza verificarne la compatibilità.");
        else notes.Add("Nessun indicatore anti-cheat riconosciuto nella scansione: non è una conferma di assenza.");
        if (components.Any(c => c.Kind.StartsWith("DLL proxy", StringComparison.Ordinal)))
            notes.Add("Sono presenti DLL proxy: vanno identificate prima di qualsiasi futura installazione.");
        token.ThrowIfCancellationRequested();
        // Keep a read handle without write sharing while hashing; never LoadLibrary/Assembly.Load the target.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            digest.AppendData(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        var hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
        return new(1, DateTimeOffset.UtcNow, path, hash, details.Architecture, apis,
            components.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), notes, antiCheat, limited);
    }

    public static string ToJson(GameReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
}
