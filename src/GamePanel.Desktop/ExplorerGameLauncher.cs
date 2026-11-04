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
        object? windows = null, desktop = null, browser = null, view = null, folder = null, application = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!;
            windows = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Shell Windows non disponibile.");
            object location = 0, root = null!; int handle;
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out handle, 1);
            if (desktop is null) throw new InvalidOperationException("Desktop di Esplora file non disponibile. Avvia il gioco dalla sua cartella.");
            // Obtain the desktop folder view through its documented shell service,
            // rather than assuming the desktop dispatch is a folder browser window.
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var browserId = typeof(IShellBrowser).GUID;
            ((IShellServiceProvider)desktop).QueryService(ref service, ref browserId, out browser);
            ((IShellBrowser)browser).QueryActiveShellView(out var shellView); view = shellView;
            var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
            shellView.GetItemObject(0, ref dispatch, out folder);
            application = ((dynamic)folder).Application;
            ((dynamic)application).ShellExecute(executable, "", Path.GetDirectoryName(executable)!, "open", 1);
        }
        catch (COMException e)
        { throw new InvalidOperationException("Esplora file non ha accettato l'avvio. Puoi avviare lo stesso EXE dalla sua cartella. " + e.Message, e); }
        catch (InvalidCastException e)
        { throw new InvalidOperationException("Il desktop Windows non espone le interfacce di avvio richieste. Avvia il gioco dalla sua cartella.", e); }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException e)
        { throw new InvalidOperationException("Interfaccia del desktop di Esplora file non disponibile. Avvia il gioco dalla sua cartella.", e); }
        finally
        {
            foreach (var item in new[] { application, folder, view, browser, desktop, windows })
                if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        void QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        void InsertMenusSB(IntPtr menu, IntPtr widths);
        void SetMenuSB(IntPtr menu, IntPtr holeMenu, IntPtr activeWindow);
        void RemoveMenusSB(IntPtr menu);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        void TranslateAcceleratorSB(IntPtr message, ushort id);
        void BrowseObject(IntPtr pidl, uint flags);
        void GetViewStateStream(uint mode, out IntPtr stream);
        void GetControlWindow(uint id, out IntPtr hwnd);
        void SendControlMsg(uint id, uint message, UIntPtr wParam, IntPtr lParam, out IntPtr result);
        void QueryActiveShellView(out IShellView view);
        void OnViewWindowActive(IShellView view);
        void SetToolbarItems(IntPtr buttons, uint count, uint flags);
    }
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        void TranslateAccelerator(IntPtr message);
        void EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(IShellView previous, IntPtr settings, IShellBrowser browser, IntPtr rectangle, out IntPtr hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(IntPtr settings);
        void AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam);
        void SaveViewState();
        void SelectItem(IntPtr pidl, uint flags);
        void GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
}
