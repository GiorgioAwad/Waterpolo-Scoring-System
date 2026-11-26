using System.Runtime.InteropServices;
using FDNDA.Waterpolo.Desktop.ViewModels;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>Enumeración de pantallas y posicionamiento de ventanas con Win32 (escritorio extendido).</summary>
internal static class NativeMonitors
{
    private const uint MonitorInfoPrimary = 0x1;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    public static IReadOnlyList<MonitorOption> GetMonitors()
    {
        var found = new List<(string Device, Rect Bounds, bool Primary)>();

        bool Callback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                found.Add((info.Device, info.Monitor, (info.Flags & MonitorInfoPrimary) != 0));
            }

            return true;
        }

        MonitorEnumProc callback = Callback;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // La principal primero; el resto de izquierda a derecha.
        return found
            .OrderByDescending(m => m.Primary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .Select((m, index) => new MonitorOption(
                m.Device,
                m.Bounds.Left,
                m.Bounds.Top,
                m.Bounds.Right - m.Bounds.Left,
                m.Bounds.Bottom - m.Bounds.Top,
                m.Primary,
                index + 1))
            .ToList();
    }

    /// <summary>Ubica y dimensiona la ventana en píxeles del escritorio virtual.</summary>
    public static void SetBounds(IntPtr window, int x, int y, int width, int height) =>
        SetWindowPos(window, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);

    /// <summary>Mueve la ventana sin cambiar su tamaño.</summary>
    public static void Move(IntPtr window, int x, int y) =>
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SwpNoZOrder | SwpNoActivate | SwpNoSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }
}
