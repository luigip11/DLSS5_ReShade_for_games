using System.IO;
using System.Runtime.InteropServices;

namespace GamePanel.Desktop;

/// <summary>Requests an EXE launch from the existing Explorer desktop, with an explicit game directory.</summary>
internal static class ExplorerGameLauncher
{
    public static void Launch(string executable)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("L'avvio tramite Esplora file richiede il thread desktop.");
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Eseguibile del gioco non trovato.");
        object? windows = null, desktop = null, document = null, application = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!;
            windows = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Shell Windows non disponibile.");
            object location = Type.Missing, root = Type.Missing; int handle;
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out handle, 1);
            if (desktop is null) throw new InvalidOperationException("Desktop di Esplora file non disponibile. Avvia il gioco dalla sua cartella.");
            document = ((dynamic)desktop).Document;
            application = ((dynamic)document).Application;
            ((dynamic)application).ShellExecute(executable, "", Path.GetDirectoryName(executable)!, "open", 1);
        }
        catch (COMException e)
        { throw new InvalidOperationException("Esplora file non ha accettato l'avvio. Puoi avviare lo stesso EXE dalla sua cartella. " + e.Message, e); }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException e)
        { throw new InvalidOperationException("Interfaccia del desktop di Esplora file non disponibile. Avvia il gioco dalla sua cartella.", e); }
        finally
        {
            foreach (var item in new[] { application, document, desktop, windows })
                if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }
}
