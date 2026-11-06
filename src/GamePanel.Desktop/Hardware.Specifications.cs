using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal static partial class Hardware
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKilobytes);

    public static string Processor()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : "Non disponibile";
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        { return "Non disponibile"; }
    }

    public static string Memory() => GetPhysicallyInstalledSystemMemory(out var kb) && kb > 0
        ? $"{kb / (1024d * 1024):0.#} GB RAM" : "Non disponibile";
}
