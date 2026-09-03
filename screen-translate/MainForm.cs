using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace screen_translate;

public partial class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(247, 248, 250);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(226, 229, 235);
    private static readonly Color Ink = Color.FromArgb(28, 32, 41);
    private static readonly Color Muted = Color.FromArgb(105, 111, 123);
    private static readonly Color Accent = Color.FromArgb(91, 85, 214);
    private static readonly Color AccentSoft = Color.FromArgb(239, 238, 252);

    private readonly List<PillButton> _themeButtons = [];
    private FlowLayoutPanel _page = null!;

    public MainForm()
    {
        InitializeComponent();
        BuildInterface();
    }

    private void BuildInterface()
    {
        SuspendLayout();
        Text = "Screen Translate";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(1080, 720);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateSidebar(), 0, 0);
        layout.Controls.Add(CreatePageHost(), 1, 0);
        Controls.Add(layout);
        ResumeLayout(true);
    }

    private Control CreateSidebar()
    {
        var sidebar = new Panel
        {
            BackColor = Surface,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 26, 18, 22)
        };

        var brand = new Panel { Dock = DockStyle.Top, Height = 58 };
        brand.Controls.Add(new Label
        {
            BackColor = Accent,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(2, 0),
            Size = new Size(38, 38),
            Text = "文",
            TextAlign = ContentAlignment.MiddleCenter
        });
        brand.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(51, 8),
            Text = "Screen Translate"
        });

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Height = 176,
            Padding = new Padding(0, 16, 0, 0),
            WrapContents = false
        };
        nav.Controls.Add(CreateNavigationItem("⌂", "Home", false));
        nav.Controls.Add(CreateNavigationItem("⚙", "Settings", true));
        nav.Controls.Add(CreateNavigationItem("↓", "Models", false));

        var localStatus = new RoundedPanel
        {
            BackColor = Color.FromArgb(244, 249, 246),
            BorderColor = Color.FromArgb(220, 235, 226),
            CornerRadius = 10,
            Dock = DockStyle.Bottom,
            Height = 72
        };
        localStatus.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(45, 112, 72),
            Location = new Point(14, 12),
            Text = "●  Offline mode"
        });
        localStatus.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8F),
            ForeColor = Muted,
            Location = new Point(14, 38),
            Text = "Your data stays on this device"
        });

        sidebar.Controls.Add(localStatus);
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    private Control CreateNavigationItem(string glyph, string text, bool selected)
    {
        var item = new Button
        {
            BackColor = selected ? AccentSoft : Surface,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5F),
            ForeColor = selected ? Accent : Muted,
            Height = 42,
            Margin = new Padding(0, 0, 0, 5),
            Padding = new Padding(12, 0, 0, 0),
            Size = new Size(188, 42),
            Text = $"{glyph}     {text}",
            TextAlign = ContentAlignment.MiddleLeft,
            UseVisualStyleBackColor = false
        };
        item.FlatAppearance.BorderSize = 0;
        return item;
    }

    private Control CreatePageHost()
    {
        var host = new Panel
        {
            BackColor = Canvas,
            Dock = DockStyle.Fill,
            Padding = new Padding(46, 35, 46, 34)
        };

        _page = new FlowLayoutPanel
        {
            AutoScroll = true,
            BackColor = Canvas,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };
        _page.Controls.Add(CreatePageHeader());
        _page.Controls.Add(CreateSettingsSurface());
        _page.Resize += Page_Resize;

        host.Controls.Add(_page);
        return host;
    }

    private static Control CreatePageHeader()
    {
        var header = new Panel
        {
            BackColor = Canvas,
            Height = 78,
            Margin = new Padding(0, 0, 0, 13)
        };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(-3, -6),
            Text = "Settings"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Muted,
            Location = new Point(0, 43),
            Text = "Set up how text is captured and translated."
        });
        return header;
    }

    private RoundedPanel CreateSettingsSurface()
    {
        var surface = new RoundedPanel
        {
            BackColor = Surface,
            BorderColor = Border,
            CornerRadius = 12,
            Height = 520,
            Margin = Padding.Empty
        };

        AddDockedTop(surface, CreateModelsSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateShortcutSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateAppearanceSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateLanguageSection());
        return surface;
    }

    private static void AddDockedTop(Control parent, Control child)
    {
        child.Dock = DockStyle.Top;
        parent.Controls.Add(child);
    }

    private static Control CreateDivider() => new Panel
    {
        BackColor = Border,
        Height = 1,
        Margin = Padding.Empty
    };

    private Control CreateLanguageSection()
    {
        var section = CreateSection(
            "Language",
            "Choose the text language and where it should be translated.",
            150);

        var source = CreateComboBox(new[]
        {
            "English", "Spanish", "French", "German", "Japanese", "Korean"
        });
        source.SelectedIndex = 0;
        var target = CreateComboBox(new[]
        {
            "English", "Spanish", "French", "German", "Japanese", "Korean", "Filipino"
        });
        target.SelectedIndex = 1;

        var inputs = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            Location = new Point(347, 20),
            RowCount = 2,
            Size = new Size(380, 108)
        };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        inputs.Controls.Add(CreateInputLabel("SCREEN TEXT"), 0, 0);
        inputs.Controls.Add(CreateInputLabel("TRANSLATE TO"), 1, 0);
        inputs.Controls.Add(source, 0, 1);
        inputs.Controls.Add(target, 1, 1);
        section.Controls.Add(inputs);
        return section;
    }

    private Control CreateAppearanceSection()
    {
        var section = CreateSection(
            "Appearance",
            "Use your Windows theme or choose one for this app.",
            108);

        var choices = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(407, 35),
            Size = new Size(320, 38),
            WrapContents = false
        };
        choices.Controls.Add(CreateThemeButton("System", true));
        choices.Controls.Add(CreateThemeButton("Light", false));
        choices.Controls.Add(CreateThemeButton("Dark", false));
        section.Controls.Add(choices);
        return section;
    }

    private Control CreateShortcutSection()
    {
        var section = CreateSection(
            "Shortcut",
            "Press these keys anywhere to start selecting a region.",
            108);
        section.Controls.Add(new Label
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(247, 247, 250),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Ink,
            Location = new Point(527, 34),
            Size = new Size(200, 40),
            Text = "Ctrl  +  Shift  +  T",
            TextAlign = ContentAlignment.MiddleCenter
        });
        return section;
    }

    private Control CreateModelsSection()
    {
        var section = CreateSection(
            "Offline models",
            "OCR and translation run privately on this computer.",
            151);

        var modelList = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            Location = new Point(347, 19),
            RowCount = 3,
            Size = new Size(380, 114)
        };
        modelList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        modelList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        modelList.Controls.Add(CreateModelName("OCR model"), 0, 0);
        modelList.Controls.Add(CreateModelStatus(), 1, 0);
        modelList.Controls.Add(CreateModelName("Translation model"), 0, 1);
        modelList.Controls.Add(CreateModelStatus(), 1, 1);

        var manageButton = new PillButton
        {
            BackColor = Accent,
            BorderColor = Accent,
            CornerRadius = 8,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Right,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.White,
            Margin = new Padding(0, 7, 0, 0),
            Size = new Size(132, 34),
            Text = "Manage models"
        };
        modelList.Controls.Add(manageButton, 1, 2);
        section.Controls.Add(modelList);
        return section;
    }

    private static Panel CreateSection(string title, string description, int height)
    {
        var section = new Panel
        {
            BackColor = Surface,
            Height = height,
            Padding = new Padding(25, 20, 25, 16)
        };
        section.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(25, 22),
            Text = title
        });
        section.Controls.Add(new Label
        {
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.8F),
            ForeColor = Muted,
            Location = new Point(25, 51),
            Size = new Size(270, 40),
            Text = description
        });
        return section;
    }

    private static Label CreateInputLabel(string text) => new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 7.5F),
        ForeColor = Muted,
        Margin = new Padding(3, 4, 8, 0),
        Text = text
    };

    private static ComboBox CreateComboBox(object[] choices)
    {
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(0, 0, 10, 0)
        };
        comboBox.Items.AddRange(choices);
        return comboBox;
    }

    private PillButton CreateThemeButton(string text, bool selected)
    {
        var button = new PillButton
        {
            BackColor = selected ? Accent : Color.FromArgb(247, 247, 250),
            BorderColor = selected ? Accent : Border,
            CornerRadius = 8,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = selected ? Color.White : Muted,
            Margin = new Padding(0, 0, 7, 0),
            Size = new Size(96, 36),
            Text = text
        };
        button.Click += ThemeButton_Click;
        _themeButtons.Add(button);
        return button;
    }

    private static Label CreateModelName(string text) => new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Ink,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label CreateModelStatus() => new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 8F),
        ForeColor = Color.FromArgb(173, 104, 27),
        Text = "●  Not installed",
        TextAlign = ContentAlignment.MiddleRight
    };

    private void Page_Resize(object? sender, EventArgs e)
    {
        int width = Math.Max(600, _page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (Control control in _page.Controls)
        {
            control.Width = width;
        }
    }

    private void ThemeButton_Click(object? sender, EventArgs e)
    {
        if (sender is not PillButton selectedButton)
        {
            return;
        }

        foreach (PillButton button in _themeButtons)
        {
            bool selected = ReferenceEquals(button, selectedButton);
            button.BackColor = selected ? Accent : Color.FromArgb(247, 247, 250);
            button.BorderColor = selected ? Accent : Border;
            button.ForeColor = selected ? Color.White : Muted;
            button.Invalidate();
        }
    }
}

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Transparent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 12;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        using GraphicsPath path = CreateRoundedPath(ClientRectangle, CornerRadius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (BorderColor == Color.Transparent || Width < 2 || Height < 2)
        {
            return;
        }

        Rectangle bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        using GraphicsPath path = CreateRoundedPath(bounds, CornerRadius);
        using var pen = new Pen(BorderColor);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.DrawPath(pen, path);
    }

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class PillButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Transparent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    public PillButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        using GraphicsPath path = RoundedPanel.CreateRoundedPath(bounds, CornerRadius);
        using var backgroundBrush = new SolidBrush(BackColor);
        using var borderPen = new Pen(BorderColor);
        eventArgs.Graphics.FillPath(backgroundBrush, path);
        eventArgs.Graphics.DrawPath(borderPen, path);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            bounds,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (Focused && ShowFocusCues)
        {
            Rectangle focusBounds = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focusBounds, ForeColor, BackColor);
        }
    }
}
