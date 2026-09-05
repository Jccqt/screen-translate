using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace screen_translate;

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(new RectangleF(.5F, .5F, Width - 1, Height - 1), LogicalToDeviceUnits(CornerRadius));
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(new RectangleF(.5F, .5F, Width - 1, Height - 1), LogicalToDeviceUnits(CornerRadius));
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    internal static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        float d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal class PillButton : Button
{
    private bool _hover, _pressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 6;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSegment { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsTab { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public PillButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new StyledButtonAccessibleObject(this);

    private sealed class StyledButtonAccessibleObject(PillButton owner) : ButtonBaseAccessibleObject(owner)
    {
        public override string DefaultAction => owner.IsTab || owner.IsSegment ? "Select" : "Press";
        public override void DoDefaultAction() => owner.PerformClick();
        public override AccessibleRole Role => owner.IsTab ? AccessibleRole.PageTab : owner.IsSegment ? AccessibleRole.RadioButton : base.Role;
        public override AccessibleStates State => base.State | (owner.Selected
            ? (owner.IsTab ? AccessibleStates.Selected : owner.IsSegment ? AccessibleStates.Checked : AccessibleStates.None)
            : AccessibleStates.None);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parent = Parent;
        while (parent?.BackColor == Color.Transparent) parent = parent.Parent;
        g.Clear(parent?.BackColor ?? BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5F, .5F, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Color fill = BackColor;
        if (_hover && Enabled) fill = Blend(fill, ForeColor, _pressed ? .14F : .07F);
        using var path = RoundedPanel.RoundedPath(bounds, LogicalToDeviceUnits(CornerRadius));
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(BorderColor);
        g.FillPath(brush, path);
        if (!IsTab) g.DrawPath(pen, path);
        Color textColor = Enabled ? ForeColor : Blend(ForeColor, BackColor, .5F);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (IsTab && Selected)
        {
            using var indicator = new SolidBrush(ForeColor);
            g.FillRectangle(indicator, LogicalToDeviceUnits(16), Height - LogicalToDeviceUnits(3), Width - LogicalToDeviceUnits(32), LogicalToDeviceUnits(3));
        }
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(ForeColor, LogicalToDeviceUnits(2));
            using var focusPath = RoundedPanel.RoundedPath(new RectangleF(3, 3, Width - 6, Height - 6), LogicalToDeviceUnits(4));
            g.DrawPath(focus, focusPath);
        }
    }

    private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}

/// <summary>Retains the native dropdown, keyboard navigation and accessibility with a themed field.</summary>
internal sealed class LanguagePicker : ComboBox
{
    public LanguagePicker()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool dropdownSelection = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        bool dark = BackColor.GetBrightness() < .5;
        Color background = dropdownSelection ? (dark ? Color.FromArgb(70, 62, 111) : Color.FromArgb(237, 234, 255)) : BackColor;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        string text = e.Index >= 0 ? GetItemText(Items[e.Index]) ?? "" : "No OCR languages installed";
        var bounds = e.Bounds;
        bounds.X += LogicalToDeviceUnits(12);
        bounds.Width -= LogicalToDeviceUnits(18);
        TextRenderer.DrawText(e.Graphics, text, Font, bounds,
            Enabled ? ForeColor : (dark ? Color.FromArgb(155, 164, 186) : Color.FromArgb(110, 118, 139)),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x000F && IsHandleCreated) // WM_PAINT
        {
            using var g = Graphics.FromHwnd(Handle);
            DrawChrome(g);
        }
        else if (message.Msg is 0x0318 or 0x0317 && message.WParam != 0) // WM_PRINT/WM_PRINTCLIENT, including DrawToBitmap
        {
            using var g = Graphics.FromHdc(message.WParam);
            DrawChrome(g);
        }
    }

    private void DrawChrome(Graphics g)
    {
        bool dark = BackColor.GetBrightness() < .5;
        using var background = new SolidBrush(BackColor);
        // Cover the native inner bevel as well as the dropdown button, in both paint and print paths.
        int edge = LogicalToDeviceUnits(3);
        g.FillRectangle(background, 0, 0, Width, edge);
        g.FillRectangle(background, 0, Height - edge, Width, edge);
        g.FillRectangle(background, 0, 0, edge, Height);
        g.FillRectangle(background, Width - edge, 0, edge, Height);
        int arrowWidth = LogicalToDeviceUnits(34);
        g.FillRectangle(background, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
        using var border = new Pen(Focused ? Color.FromArgb(126, 110, 238) : dark ? Color.FromArgb(73, 79, 98) : Color.FromArgb(213, 218, 231));
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ForeColor, Math.Max(1, LogicalToDeviceUnits(1)));
        int x = Width - LogicalToDeviceUnits(19), y = Height / 2;
        g.DrawLines(pen, [new Point(x - LogicalToDeviceUnits(4), y - 2), new Point(x, y + 2), new Point(x + LogicalToDeviceUnits(4), y - 2)]);
    }
}

internal sealed class AppMark : Control
{
    public AppMark() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        using var path = RoundedPanel.RoundedPath(ClientRectangle, LogicalToDeviceUnits(10));
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        using var font = new Font("Segoe UI Semibold", 15F * DeviceDpi / 96F, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics, "文", font, new Rectangle(0, 0, Width, Height), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
