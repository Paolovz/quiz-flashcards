using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Recall.ViewModels;

namespace Recall;

public partial class MainWindow : Window
{
    private const double DesignWidth = 1200;
    private const double DesignHeight = 820;
    private const double EdgeGap = 24;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => FitToMonitor();
        DataContext = new Shell();
    }

    /// A 1440x900 laptop panel at 200% scaling only offers 720x450 device-independent
    /// pixels. SystemParameters reports the unscaled desktop, so the work area is read
    /// from the monitor itself and converted with this window's real DPI.
    private void FitToMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var scale = MonitorScale(monitor, handle);
        if (scale <= 0) scale = 1;

        var areaWidth = (info.Work.Right - info.Work.Left) / scale;
        var areaHeight = (info.Work.Bottom - info.Work.Top) / scale;

        var width = Math.Min(DesignWidth, Math.Max(MinWidth, areaWidth - EdgeGap));
        var height = Math.Min(DesignHeight, Math.Max(MinHeight, areaHeight - EdgeGap));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = info.Work.Left / scale + Math.Max(0, (areaWidth - width) / 2);
        Top = info.Work.Top / scale + Math.Max(0, (areaHeight - height) / 2);
    }

    private const uint MonitorDefaultToNearest = 2;

    /// The window's own DPI is still 96 during SourceInitialized; the monitor's is final.
    private static double MonitorScale(IntPtr monitor, IntPtr handle)
    {
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96.0;

        var windowDpi = GetDpiForWindow(handle);
        return windowDpi > 0 ? windowDpi / 96.0 : 1.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
