namespace GamePanel.Core;

/// <summary>Accept one pasted Explorer path, never a command line.</summary>
public static class ExecutablePath
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidDataException("Incolla il percorso dell'EXE oppure usa Seleziona gioco .exe.");
        var path = input.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        if (path.Length == 0 || path.IndexOfAny(['"', '\r', '\n', '\0']) >= 0
            || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Inserisci il percorso di un solo file .exe, senza argomenti. Le virgolette esterne di Windows sono accettate.");
        return path;
    }
}
