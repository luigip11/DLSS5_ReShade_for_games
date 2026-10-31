using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GamePanel.Core;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr window, string text, string caption, uint flags);
    public static int Main(string[] args)
    {
        UpdateRequest? request = null; UpdateInstallation? install = null;
        try
        {
            if (args.Length is < 2 or > 3 || args[0] != "--request" || args.Length == 3 && args[2] != "--rollback") throw new InvalidDataException("Avvia l'aggiornamento dal pulsante di GamePanel.");
            var path = Path.GetFullPath(args[1]);
            if (new FileInfo(path).Length > 64 * 1024 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Richiesta aggiornamento non valida.");
            request = JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Richiesta mancante.");
            if (request.SchemaVersion != 1 || request.ParentPid <= 0 || request.TargetVersion != AppVersion.Current.ToString()
                || Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) != Path.GetFullPath(request.ReadyDirectory).TrimEnd(Path.DirectorySeparatorChar)
                || Path.GetDirectoryName(path) != Path.GetDirectoryName(Path.GetFullPath(request.ReadyDirectory).TrimEnd(Path.DirectorySeparatorChar))) throw new InvalidDataException("Updater, richiesta e versione non corrispondono.");
            install = new(request.ApplicationDirectory, request.ReadyDirectory, request.DataRoot);
            // Verify all staged bytes again before replacing the running application's files.
            PackageInventory.Verify(request.ReadyDirectory, PackageInventory.Read(request.ReadyDirectory));
            try
            {
                using var parent = Process.GetProcessById(request.ParentPid);
                if (parent.StartTime.ToUniversalTime().Ticks == request.ParentStartedUtcTicks && !parent.WaitForExit(60000)) throw new InvalidOperationException("Chiudi GamePanel prima di completare l'aggiornamento.");
            }
            catch (ArgumentException) { /* Parent already exited. Never kill unrelated PIDs. */ }
            foreach (var other in Process.GetProcessesByName("GamePanel"))
            {
                using (other)
                    if (!other.HasExited && string.Equals(other.MainModule?.FileName, Path.Combine(request.ApplicationDirectory, "GamePanel.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Un'altra istanza di GamePanel sta usando la stessa cartella. Chiudila e ripeti.");
            }
            if (args.Length == 3) install.Recover(rollback: true);
            else if (install.HasPendingJournal) install.Recover(rollback: false);
            else install.Apply();
            if (request.Restart) Restart(request);
            return 0;
        }
        catch (Exception e)
        {
            var restored = false;
            try { if (install?.HasPendingJournal == true) { install.Recover(rollback: true); restored = true; } } catch { /* Keep journal and byte copies for explicit recovery. */ }
            MessageBoxW(IntPtr.Zero, "Aggiornamento non completato.\n\n" + e.Message + (restored ? "\n\nVersione precedente ripristinata." : "\n\nLe copie e l'eventuale journal sono conservati nella cartella Updates dei dati GamePanel.")
                + "\nLibreria, backup e componenti scaricati non sono stati cancellati.", "GamePanel · aggiornamento", 0x10);
            if (restored && request?.Restart == true) try { Restart(request); } catch { }
            return 1;
        }
    }
    private static void Restart(UpdateRequest request) => Process.Start(new ProcessStartInfo(Path.Combine(request.ApplicationDirectory, "GamePanel.exe"))
    { WorkingDirectory = request.ApplicationDirectory, UseShellExecute = true })?.Dispose();
}
