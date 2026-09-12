using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace screen_translate.Capture;

public sealed class CapturedRegion(Bitmap image, Rectangle bounds) : IDisposable
{
    public Bitmap Image { get; } = image;
    public Rectangle Bounds { get; } = bounds;
    public void Dispose() => Image.Dispose();
}

public sealed record RegionCaptureOutcome(CapturedRegion? Region = null, string? Error = null);

public interface IScreenRegionCaptureService
{
    string? GetUnavailableReason();
    Task<RegionCaptureOutcome> SelectAsync(Form owner, CancellationToken cancellationToken = default);
}

public interface IDesktopSnapshotProvider
{
    Bitmap Capture(ScreenTopology topology);
}

public sealed class GdiDesktopSnapshotProvider : IDesktopSnapshotProvider
{
    public Bitmap Capture(ScreenTopology topology)
    {
        var bounds = topology.VirtualBounds;
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        try
        {
            // Finish pending DWM composition (including disposal of an earlier result overlay)
            // before reading the desktop. The selector itself is not created until afterward.
            try { _ = DwmFlush(); } catch (DllNotFoundException) { }
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return image;
        }
        catch (Exception error) when (error is Win32Exception or ExternalException or ArgumentException or OutOfMemoryException)
        {
            image.Dispose();
            throw new ScreenCaptureException(
                "Windows could not capture the selected desktop. Secure desktops and capture-protected content are not supported. Return to the desktop and try again.", error);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}

public sealed class ScreenCaptureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Captures a private in-memory desktop snapshot before displaying any selection UI.</summary>
public sealed class ScreenRegionCaptureService(
    IDesktopGeometry? geometry = null,
    IDesktopSnapshotProvider? snapshots = null,
    TimeSpan? compositorDelay = null) : IScreenRegionCaptureService
{
    private readonly IDesktopGeometry _geometry = geometry ?? new WindowsDesktopGeometry();
    private readonly IDesktopSnapshotProvider _snapshots = snapshots ?? new GdiDesktopSnapshotProvider();
    private readonly TimeSpan _compositorDelay = compositorDelay ?? TimeSpan.FromMilliseconds(50);

    public string? GetUnavailableReason()
    {
        if (!OperatingSystem.IsWindows()) return "Screen-region capture is available only on Windows.";
        try { return _geometry.GetCurrent().ValidationError; }
        catch (Exception error) when (error is Win32Exception or ExternalException or InvalidOperationException or
            ArgumentException or TypeInitializationException or OverflowException)
        { return "Windows display information is unavailable. Reconnect the display and try again."; }
    }

    public async Task<RegionCaptureOutcome> SelectAsync(Form owner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Result overlays are hidden/disposed immediately before this call. Give the window manager
        // one non-blocking composition interval to reveal and repaint the underlying application.
        if (_compositorDelay > TimeSpan.Zero)
            await Task.Delay(_compositorDelay, cancellationToken);
        ScreenTopology topology;
        Bitmap snapshot;
        try
        {
            topology = _geometry.GetCurrent();
            if (topology.ValidationError is string problem) return new(Error: problem);
            snapshot = _snapshots.Capture(topology);
            var afterCapture = _geometry.GetCurrent();
            if (!topology.Matches(afterCapture))
            {
                snapshot.Dispose();
                return DisplayChanged();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ScreenCaptureException error) { return new(Error: error.Message); }
        catch (Exception error) when (error is Win32Exception or ExternalException or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            return new(Error: "Windows could not start screen capture. Check the connected displays and try again.");
        }

        using (snapshot)
        using (var selector = new RegionSelectorForm(snapshot, topology, _geometry))
            return await selector.SelectAsync(owner, cancellationToken);
    }

    internal static RegionCaptureOutcome DisplayChanged() => new(Error:
        "The display configuration changed during selection, so capture was cancelled. Check the connected displays and try again.");
}
