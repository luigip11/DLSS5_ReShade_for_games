using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private bool UseExplorerForSelectedGame => SelectedIsUnderground1 && _report is not null
        && Directory.Exists(Path.Combine(Path.GetDirectoryName(_report.ExecutablePath)!, ".trex"))
        && File.Exists(Path.Combine(Path.GetDirectoryName(_report.ExecutablePath)!, "rtx.conf"));
    private static string LaunchReportPath => Path.Combine(GamePanelPaths.Root, "Diagnostics", "last-launch.json");
    private static void GuardLaunchPath(string path)
    {
        for (string? node = Path.GetFullPath(path); node is not null; node = Path.GetDirectoryName(node))
        {
            try
            {
                if ((File.GetAttributes(node) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidDataException("Collegamenti o dispositivi non ammessi nel percorso del report.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private void RecordLaunch(string route, IReadOnlyList<string> arguments, string result, int? processId = null, string? error = null)
    {
        if (_report is null) return;
        try
        {
            var root = Path.GetDirectoryName(_report.ExecutablePath)!;
            var hashes = new Dictionary<string, string?>();
            foreach (var name in new[] { "rtx.conf", "user.conf", "dxvk.conf", "scripts/NFSUnderground.WidescreenFix.ini" })
            {
                var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)); GuardLaunchPath(path);
                if (!File.Exists(path)) { hashes[name] = null; continue; }
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                hashes[name] = stream.Length > 1024 * 1024 ? "oltre limite di lettura" : Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            using var identity = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            var environment = new Dictionary<string, string?>();
            // Explicit rendering variables only. Never export the full process environment.
            foreach (var name in new[] { "DXVK_CONFIG_FILE", "DXVK_LOG_PATH", "DXVK_FILTER_DEVICE_NAME", "RTX_CONFIG", "RTX_CONF", "RTX_MODS_PATH", "VK_INSTANCE_LAYERS" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                environment[name] = value?.Length > 4096 ? "oltre limite di lettura" : value;
            }
            var selected = _library.Games.FirstOrDefault(g => g.Id == _library.SelectedId);
            var payload = new { SchemaVersion = 1, AtUtc = DateTimeOffset.UtcNow, Route = route, Result = result,
                Executable = _report.ExecutablePath, AnalyzedExecutableHash = _report.Sha256,
                WorkingDirectory = root, Arguments = arguments, GamePanelElevated = elevated,
                SelectedExecutable = selected?.ExecutablePath, RequestedProcessId = processId,
                ConfigurationHashes = hashes, GamePanelRenderingEnvironment = environment,
                Note = route == "ExplorerDesktop" ? "Avvio richiesto alla shell desktop; ambiente e privilegi effettivi del gioco non misurati dal report." : "Processo avviato da GamePanel.", Error = error };
            var target = LaunchReportPath; var folder = Path.GetDirectoryName(target)!; GuardLaunchPath(folder); Directory.CreateDirectory(folder); GuardLaunchPath(target);
            var temporary = Path.Combine(folder, "launch-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Security.SecurityException)
        { _libraryMessage = "Avvio richiesto; report diagnostico non salvato: " + e.Message; }
    }
}
