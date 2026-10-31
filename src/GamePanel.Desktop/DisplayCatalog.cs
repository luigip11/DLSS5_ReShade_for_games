using System.Runtime.InteropServices;

namespace GamePanel.Desktop;

internal sealed record DetectedDisplay(string Id, string Device, string Name, bool Primary, int Width, int Height, int? NativeWidth, int? NativeHeight)
{
    public int TargetWidth => NativeWidth ?? Width;
    public int TargetHeight => NativeHeight ?? Height;
    public string Label => $"{Name} · {Width} × {Height}" + (Primary ? " · principale" : "");
}

internal static class DisplayCatalog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields; public int X, Y; public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels; public uint BitsPerPixel, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id, Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct Target
    { public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling; public Rational Refresh; public uint Scan; public int Available; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Size2 { public uint X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Signal
    { public ulong PixelRate; public Rational Horizontal, Vertical; public Size2 Active, Total; public uint Additional, Scan; }
    [StructLayout(LayoutKind.Sequential)] private struct Preferred { public Header Header; public uint Width, Height; public Signal Signal; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string? device, uint index, ref Device info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref Mode info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] DisplayPath[] values, ref uint modes, IntPtr modeValues, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int Config(ref SourceName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int Config(ref TargetName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int Config(ref Preferred info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    public static IReadOnlyList<DetectedDisplay> Read()
    {
        if (Marshal.SizeOf<Mode>() != 220 || Marshal.SizeOf<DisplayPath>() != 72 || Marshal.SizeOf<Preferred>() != 80
            || Marshal.SizeOf<SourceName>() != 84 || Marshal.SizeOf<TargetName>() != 420)
            throw new InvalidOperationException("Strutture di rilevamento schermo incompatibili.");
        var preferred = ReadPreferred(); var result = new List<DetectedDisplay>();
        for (uint index = 0; index < 32; index++)
        {
            var device = new Device { Size = Marshal.SizeOf<Device>() }; if (!EnumDisplayDevicesW(null, index, ref device, 0)) break;
            if ((device.Flags & 1) == 0 || (device.Flags & 8) != 0) continue;
            var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() }; if (!EnumDisplaySettingsW(device.DeviceName, -1, ref mode) || mode.Width == 0 || mode.Height == 0) continue;
            var monitor = new Device { Size = Marshal.SizeOf<Device>() }; var found = EnumDisplayDevicesW(device.DeviceName, 0, ref monitor, 0);
            var p = preferred.GetValueOrDefault(device.DeviceName);
            var name = p.Name ?? (found ? monitor.DeviceString : device.DeviceName);
            result.Add(new(p.Id ?? (found ? monitor.DeviceId : device.DeviceName), device.DeviceName, name, (device.Flags & 4) != 0,
                (int)mode.Width, (int)mode.Height, p.Width, p.Height));
        }
        return result;
    }
    public static string? WindowDevice(IntPtr window)
    { var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; return GetMonitorInfoW(MonitorFromWindow(window, 2), ref info) ? info.Device : null; }
    private static Dictionary<string, (string? Id, string? Name, int? Width, int? Height)> ReadPreferred()
    {
        var result = new Dictionary<string, (string?, string?, int?, int?)>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(2, out var count, out var modes) != 0 || count is 0 or > 64 || modes > 256) return result;
        var paths = new DisplayPath[count]; var buffer = Marshal.AllocHGlobal(checked((int)modes * 64));
        try
        {
            if (QueryDisplayConfig(2, ref count, paths, ref modes, buffer, IntPtr.Zero) != 0) return result;
            foreach (var path in paths.Take((int)count))
            {
                var source = new SourceName { Header = new() { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
                var target = new TargetName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                var preferred = new Preferred { Header = new() { Type = 3, Size = (uint)Marshal.SizeOf<Preferred>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                if (Config(ref source) != 0) continue;
                var named = Config(ref target) == 0; var native = Config(ref preferred) == 0 && preferred.Width > 0 && preferred.Height > 0;
                int? width = native ? (int)preferred.Width : null; int? height = native ? (int)preferred.Height : null;
                if (path.Target.Rotation is 2 or 4) (width, height) = (height, width);
                result.TryAdd(source.Name, (named ? target.Path : null, named && target.Name.Length > 0 ? target.Name : null, width, height));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }
}
