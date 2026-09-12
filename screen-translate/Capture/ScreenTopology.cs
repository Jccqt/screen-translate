using System.Runtime.InteropServices;

namespace screen_translate.Capture;

public sealed record DisplayMonitor(string DeviceName, Rectangle Bounds, int DpiX, int DpiY);

/// <summary>A physical-pixel snapshot of the Windows virtual desktop.</summary>
public sealed record ScreenTopology(Rectangle VirtualBounds, IReadOnlyList<DisplayMonitor> Monitors)
{
    public string? ValidationError
    {
        get
        {
            if (VirtualBounds.Width <= 0 || VirtualBounds.Height <= 0 || Monitors.Count == 0)
                return "Windows did not report a usable display. Reconnect the display and try again.";
            try { _ = checked(VirtualBounds.Width * VirtualBounds.Height * 4L); }
            catch (OverflowException) { return "The combined desktop is too large to capture reliably."; }
            return null;
        }
    }

    public bool Matches(ScreenTopology other) => VirtualBounds == other.VirtualBounds &&
        Monitors.Count == other.Monitors.Count && Monitors.Zip(other.Monitors).All(pair => pair.First == pair.Second);
}

public interface IDesktopGeometry
{
    ScreenTopology GetCurrent();
}

public sealed class WindowsDesktopGeometry : IDesktopGeometry
{
    public ScreenTopology GetCurrent()
    {
        var monitors = Screen.AllScreens
            .Select(screen =>
            {
                Point center = new(screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2);
                nint handle = MonitorFromPoint(center, 2); // MONITOR_DEFAULTTONEAREST
                int dpiX = 0, dpiY = 0;
                try
                {
                    if (handle != 0 && GetDpiForMonitor(handle, 0, out uint x, out uint y) == 0)
                    {
                        dpiX = checked((int)x);
                        dpiY = checked((int)y);
                    }
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                return new DisplayMonitor(screen.DeviceName, screen.Bounds, dpiX, dpiY);
            })
            .OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(SystemInformation.VirtualScreen, monitors);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
