using System.Runtime.InteropServices;

namespace screen_translate.Interface;

/// <summary>Shared theme propagation for application-owned dialogs, including nested modal windows.</summary>
public class ThemedForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        if (Owner is MainForm main) ApplyTheme(main.DialogSurface, main.DialogInk);
        else if (Owner is not null) ApplyTheme(Owner.BackColor, Owner.ForeColor);
        base.OnLoad(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeWindows.ApplyTitleBar(this, BackColor.GetBrightness() < .5F);
    }

    public void ApplyTheme(Color surface, Color ink) => ThemeWindows.Apply(this, surface, ink);
}

internal static class ThemeWindows
{
    public static void Apply(Form form, Color surface, Color ink)
    {
        if (form.IsDisposed) return;
        bool dark = surface.GetBrightness() < .5F;
        void Paint(Control control)
        {
            control.BackColor = surface;
            control.ForeColor = ink;
            if (control is PillButton button)
                button.BorderColor = dark ? Color.FromArgb(163, 182, 183) : Color.FromArgb(94, 111, 112);
            foreach (Control child in control.Controls) Paint(child);
        }
        Paint(form);
        ApplyTitleBar(form, dark);
        foreach (var child in form.OwnedForms) Apply(child, surface, ink);
        form.Invalidate(true);
    }

    public static void ApplyTitleBar(Form form, bool dark)
    {
        if (!form.IsHandleCreated) return;
        int enabled = dark ? 1 : 0;
        // Older Windows 10 builds use attribute 19; unsupported builds retain native chrome.
        if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) < 0)
            DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

internal sealed class ModelInventoryList : ListBox
{
    public ModelInventoryList() => DrawMode = DrawMode.OwnerDrawFixed;

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Font.Height + LogicalToDeviceUnits(8);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(4), 0);
        TextRenderer.DrawText(e.Graphics, (selected ? "✓ " : "   ") + GetItemText(Items[e.Index]), Font, bounds,
            ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected)
        {
            using var border = new Pen(ForeColor);
            e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        }
        e.DrawFocusRectangle();
    }
}
