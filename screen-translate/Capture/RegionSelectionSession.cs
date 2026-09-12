namespace screen_translate.Capture;

/// <summary>Physical-pixel drag state, independent of pointer direction and desktop origin.</summary>
public sealed class RegionSelectionSession(Size desktopSize)
{
    private Point? _anchor;
    public Rectangle Bounds { get; private set; }
    public bool IsDragging => _anchor is not null;

    public void Begin(Point point)
    {
        _anchor = Clamp(point);
        Bounds = Rectangle.Empty;
    }

    public Rectangle Update(Point point)
    {
        if (_anchor is not Point anchor) return Bounds;
        Point current = Clamp(point);
        Bounds = Rectangle.FromLTRB(Math.Min(anchor.X, current.X), Math.Min(anchor.Y, current.Y),
            Math.Max(anchor.X, current.X), Math.Max(anchor.Y, current.Y));
        return Bounds;
    }

    public Rectangle? Complete(Point point)
    {
        Update(point);
        _anchor = null;
        if (Bounds.Width == 0 || Bounds.Height == 0)
        {
            Bounds = Rectangle.Empty;
            return null;
        }
        return Bounds;
    }

    public void Reset()
    {
        _anchor = null;
        Bounds = Rectangle.Empty;
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, desktopSize.Width),
        Math.Clamp(point.Y, 0, desktopSize.Height));
}
