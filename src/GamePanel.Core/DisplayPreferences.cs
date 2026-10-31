using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record DisplayPreference(int SchemaVersion, string? DisplayId);
public sealed class DisplayPreferences
{
    private readonly string _folder;
    public DisplayPreferences(string folder) => _folder = Path.GetFullPath(folder);
    public DisplayPreference Load()
    {
        var path = Path.Combine(_folder, "display.json"); GuardPath(path);
        if (AttributesOrMissing(path) is null) return new(1, null);
        var value = ReadJson<DisplayPreference>(path); Validate(value); return value;
    }
    public void Save(string? id)
    {
        var value = new DisplayPreference(1, id); Validate(value); Load();
        GuardPath(_folder); Directory.CreateDirectory(_folder); GuardPath(_folder);
        WriteJson(Path.Combine(_folder, "display.json"), value);
    }
    private static void Validate(DisplayPreference value)
    { if (value.SchemaVersion != 1 || value.DisplayId is not null && (value.DisplayId.Length is < 1 or > 1024 || value.DisplayId.Any(char.IsControl))) throw new InvalidDataException("Preferenza dello schermo non valida."); }
}
