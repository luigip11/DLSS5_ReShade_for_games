using System.Text.RegularExpressions;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RuntimeEvidence(int? Width, int? Height, bool ShaderCompiled, DateTimeOffset LogWrittenAt)
{
    public static RuntimeEvidence Read(string executablePath)
    {
        var executable = Path.GetFullPath(ExecutablePath.Normalize(executablePath));
        var path = GameFile(Path.GetDirectoryName(executable)!, "ReShade.log");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("Log oltre 2 MiB: esporta la parte dell'ultimo avvio per verificarla.");
        using var reader = new StreamReader(stream); var text = reader.ReadToEnd();
        if (!text.Contains($"into '{executable}'", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Il log non identifica l'eseguibile selezionato.");
        int? Last(string parameter)
        {
            var matches = Regex.Matches(text, @"\|\s*" + parameter + @"\s*\|\s*(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return matches.Count > 0 && int.TryParse(matches[^1].Groups[1].Value, out var n) && n is >= 320 and <= 16384 ? n : null;
        }
        return new(Last("BackBufferWidth"), Last("BackBufferHeight"),
            Regex.IsMatch(text, @"Successfully compiled '[^'\r\n]*[\\/]GamePanelSDR\.fx'", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                && !Regex.IsMatch(text, @"Failed to compile '[^'\r\n]*[\\/]GamePanelSDR\.fx'", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            File.GetLastWriteTimeUtc(path));
    }
}
