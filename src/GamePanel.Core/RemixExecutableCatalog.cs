namespace GamePanel.Core;

/// <summary>Exact files inspected for configuration management; not runtime compatibility certification.</summary>
public static class RemixExecutableCatalog
{
    public const string NoCdSha256 = "5a4be6a2d072cf27f1aedf3119b75fd685a4ae5ecdf567cae7db26278c596e97";
    public static bool Contains(string hash) => string.Equals(hash, RemixTrialService.WorkingExecutableSha256, StringComparison.OrdinalIgnoreCase)
        || string.Equals(hash, NoCdSha256, StringComparison.OrdinalIgnoreCase);
}
