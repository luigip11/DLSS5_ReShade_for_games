namespace GamePanel.Core;

public static class GamePanelPaths
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("GAMEPANEL_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!Path.IsPathFullyQualified(configured)) throw new InvalidOperationException("GAMEPANEL_DATA_ROOT deve essere un percorso assoluto.");
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Cartella dati utente non disponibile.");
            return Path.Combine(local, "GamePanel");
        }
    }
}
