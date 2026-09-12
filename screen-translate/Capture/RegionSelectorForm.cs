using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace screen_translate.Capture;

internal sealed class RegionSelectorForm : Form
{
    private readonly Bitmap _snapshot;
    private readonly ScreenTopology _topology;
    private readonly IDesktopGeometry _geometry;
    private readonly RegionSelectionSession _selection;
    private readonly System.Windows.Forms.Timer _displayCheck = new() { Interval = 250 };
    private TaskCompletionSource<RegionCaptureOutcome>? _completion;
    private CancellationTokenRegistration _cancellation;
    private bool _finished;

    internal Rectangle SelectionBounds => _selection.Bounds;

    public RegionSelectorForm(Bitmap snapshot, ScreenTopology topology, IDesktopGeometry geometry)
    {
        _snapshot = snapshot;
        _topology = topology;
        _geometry = geometry;
        _selection = new(topology.VirtualBounds.Size);
        Name = "ScreenRegionSelector";
        Text = "Select text region";
        AccessibleName = "Screen region selection";
        AccessibleDescription = "Drag to select text. Press Escape to cancel.";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = topology.VirtualBounds;
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        _displayCheck.Tick += (_, _) => CheckDisplays();
    }

    public Task<RegionCaptureOutcome> SelectAsync(Form owner, CancellationToken cancellationToken)
    {
        if (_completion is not null) throw new InvalidOperationException("Selection has already started.");
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            _completion.SetCanceled(cancellationToken);
            return _completion.Task;
        }
        FormClosed += (_, _) => CompleteClosedWindow();
        // An owned form is hidden by Windows while its owner is minimized. Keep the
        // selector independent so the global shortcut always produces visible UI.
        Show();
        Activate();
        _displayCheck.Start();
        _cancellation = cancellationToken.Register(() =>
        {
            if (IsDisposed) return;
            if (!InvokeRequired) { FinishCancelled(cancellationToken); return; }
            try { BeginInvoke((Action)(() => FinishCancelled(cancellationToken))); }
            catch (InvalidOperationException) { FinishCancelled(cancellationToken); }
        });
        return _completion.Task;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = e.SuppressKeyPress = true;
            Finish(new());
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Finish(new()); return; }
        if (e.Button != MouseButtons.Left) return;
        _selection.Begin(e.Location);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_selection.IsDragging) return;
        Rectangle previous = _selection.Bounds;
        Rectangle current = _selection.Update(e.Location);
        Invalidate(Rectangle.Union(Inflate(previous), Inflate(current)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_selection.IsDragging) return;
        Capture = false;
        if (!DisplaysMatch()) { Finish(ScreenRegionCaptureService.DisplayChanged()); return; }
        Rectangle? selected = _selection.Complete(e.Location);
        if (selected is not Rectangle area)
        {
            // A click or collapsed drag is not confirmation; leave the selector ready for another drag.
            Invalidate();
            return;
        }
        try
        {
            Finish(new(CreateCapturedRegion(_snapshot, _topology, area)));
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or OutOfMemoryException)
        {
            Finish(new(Error: "Windows could not finish the selected capture. Try selecting the region again."));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_snapshot, Point.Empty);
        using (var shade = new SolidBrush(Color.FromArgb(112, Color.Black)))
            e.Graphics.FillRectangle(shade, ClientRectangle);
        Rectangle selected = _selection.Bounds;
        if (selected.Width <= 0 || selected.Height <= 0) return;
        e.Graphics.DrawImage(_snapshot, selected, selected, GraphicsUnit.Pixel);
        float scale = Math.Max(1F, DeviceDpi / 96F);
        using (var border = new Pen(Color.FromArgb(0, 220, 190), 2F * scale) { Alignment = PenAlignment.Inset })
            e.Graphics.DrawRectangle(border, selected.X, selected.Y, Math.Max(0, selected.Width - 1), Math.Max(0, selected.Height - 1));
        DrawBounds(e.Graphics, selected, scale);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x007E && Visible) // WM_DISPLAYCHANGE
            CheckDisplays();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayCheck.Dispose();
            _cancellation.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawBounds(Graphics graphics, Rectangle selected, float scale)
    {
        string text = $"{selected.X + _topology.VirtualBounds.X}, {selected.Y + _topology.VirtualBounds.Y}  ·  {selected.Width} × {selected.Height}";
        Font font = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        Size size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        int padding = (int)Math.Ceiling(6 * scale);
        int width = size.Width + padding * 2;
        int height = size.Height + padding * 2;
        int x = Math.Clamp(selected.Left, 0, Math.Max(0, ClientSize.Width - width));
        int preferredY = selected.Top - height - (int)(4 * scale);
        int y = preferredY >= 0 ? preferredY : Math.Clamp(selected.Top + (int)(4 * scale), 0, Math.Max(0, ClientSize.Height - height));
        var label = new Rectangle(x, y, width, height);
        using var background = new SolidBrush(Color.FromArgb(230, 25, 35, 40));
        graphics.FillRectangle(background, label);
        TextRenderer.DrawText(graphics, text, font, new Point(label.X + padding, label.Y + padding), Color.White,
            TextFormatFlags.NoPadding);
    }

    private void CheckDisplays()
    {
        if (!DisplaysMatch()) Finish(ScreenRegionCaptureService.DisplayChanged());
    }

    private bool DisplaysMatch()
    {
        try { return _topology.Matches(_geometry.GetCurrent()); }
        catch (Exception) { return false; }
    }

    private void FinishCancelled(CancellationToken token)
    {
        if (_finished) return;
        _finished = true;
        _displayCheck.Stop();
        _completion!.TrySetCanceled(token);
        Close();
    }

    private void CompleteClosedWindow()
    {
        if (_finished) return;
        _finished = true;
        _displayCheck.Stop();
        _completion?.TrySetResult(new());
    }

    private void Finish(RegionCaptureOutcome outcome)
    {
        if (_finished) return;
        _finished = true;
        _displayCheck.Stop();
        _completion?.TrySetResult(outcome);
        if (!IsDisposed) Close();
    }

    private static Rectangle Inflate(Rectangle rectangle)
    {
        rectangle.Inflate(180, 50);
        return rectangle;
    }

    internal static CapturedRegion CreateCapturedRegion(Bitmap snapshot, ScreenTopology topology, Rectangle area)
    {
        var image = snapshot.Clone(area, PixelFormat.Format32bppPArgb);
        var desktopBounds = new Rectangle(
            area.X + topology.VirtualBounds.X, area.Y + topology.VirtualBounds.Y, area.Width, area.Height);
        return new(image, desktopBounds);
    }
}
