using System.Drawing.Drawing2D;
using System.ComponentModel;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

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
    private Panel _page = null!;
    private RoundedPanel _settingsSurface = null!;
    private ComboBox _sourceLanguage = null!;
    private Label _sourceStatus = null!;
    private Label _dataFolder = null!;
    private Label _ocrModelStatus = null!;
    private Label _settingsError = null!;
    private readonly OcrLanguageCatalog _languageCatalog = new();
    private readonly SourceLanguageSettingsStore _settingsStore;
    private SourceLanguageSettings _sourceSettings;
    private int _refreshVersion;
    private bool _bindingLanguages;

    // The OCR pipeline must use this exact code and data directory when initializing Tesseract.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedSourceLanguageCode => _sourceLanguage.Enabled ? (_sourceLanguage.SelectedItem as OcrLanguage)?.Code : null;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string OcrDataDirectory => _sourceSettings.OcrDataDirectory;

    public MainForm() : this(SourceLanguageSettingsStore.CreateDefault()) { }

    public MainForm(SourceLanguageSettingsStore settingsStore) : this(settingsStore, TargetLanguageSettingsStore.CreateDefault()) { }

    public MainForm(SourceLanguageSettingsStore settingsStore, TargetLanguageSettingsStore targetSettingsStore,
        ITranslationModelCatalog? translationCatalog = null)
    {
        _settingsStore = settingsStore;
        _sourceSettings = _settingsStore.Load(out string? error);
        _targetSettingsStore = targetSettingsStore;
        _targetSettings = targetSettingsStore.Load(out string? targetError);
        _translationCatalog = translationCatalog ?? new ArgosTranslationModelCatalog();
        InitializeComponent();
        BuildInterface();
        _settingsError.Text = error ?? "";
        _targetSettingsError.Text = targetError ?? "";
        Shown += async (_, _) => await RefreshSourceLanguagesAsync();
        Activated += async (_, _) => await RefreshSourceLanguagesAsync();
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
        AutoScaleDimensions = new SizeF(96F, 96F);

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
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
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

        _page = new Panel
        {
            AutoScroll = true,
            BackColor = Canvas,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
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
        _settingsSurface = surface;

        AddDockedTop(surface, CreateModelsSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateShortcutSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateAppearanceSection());
        AddDockedTop(surface, CreateDivider());
        AddDockedTop(surface, CreateLanguageSection());
        foreach (Control child in surface.Controls)
            child.SizeChanged += (_, _) => ResizeSettingsSurface();
        ResizeSettingsSurface();
        return surface;
    }

    private void ResizeSettingsSurface()
    {
        _settingsSurface.Height = _settingsSurface.Controls.Cast<Control>().Sum(control => control.Height) + LogicalToDeviceUnits(8);
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
            "Choose the screen text language and your translation output language.",
            300);

        _sourceLanguage = CreateComboBox([]);
        _sourceLanguage.Name = "SourceLanguage";
        _sourceLanguage.AccessibleName = "Source language";
        _sourceLanguage.Enabled = false;
        _sourceLanguage.SelectedIndexChanged += SourceLanguageChanged;
        _targetLanguage = CreateComboBox(TranslationLanguage.All.Cast<object>().ToArray());
        _targetLanguage.Name = "TargetLanguage";
        _targetLanguage.AccessibleName = "Translation output language";
        _targetLanguage.SelectedItem = TranslationLanguage.Resolve(_targetSettings.TargetLanguageCode);
        _targetLanguage.SelectedIndexChanged += async (_, _) =>
        {
            _targetSettings = _targetSettings with { TargetLanguageCode = SelectedTargetLanguageCode };
            SaveTargetSettings();
            await RefreshTranslationModelsAsync();
        };

        var inputs = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            Name = "LanguageInputs",
            Size = new Size(380, 66)
        };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        inputs.Controls.Add(CreateInputLabel("SOURCE LANGUAGE (OCR)"), 0, 0);
        inputs.Controls.Add(CreateInputLabel("TRANSLATE TO"), 1, 0);
        inputs.Controls.Add(_sourceLanguage, 0, 1);
        inputs.Controls.Add(_targetLanguage, 1, 1);
        section.Controls.Add(inputs);
        _sourceStatus = new Label { Name = "SourceLanguageStatus", ForeColor = Muted, Text = "Checking installed OCR languages…" };
        _dataFolder = new Label { Name = "OcrDataFolder", ForeColor = Muted, AutoEllipsis = true };
        _settingsError = new Label { Name = "SourceSettingsError", ForeColor = Color.FromArgb(160, 65, 35) };
        var folderButton = new Button { Text = "Choose OCR folder…", AutoSize = true, AccessibleName = "Choose OCR data folder" };
        var refreshButton = new Button { Text = "Refresh languages", AutoSize = true };
        folderButton.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the tessdata folder containing your installed .traineddata language files.",
                UseDescriptionForTitle = true,
                SelectedPath = OcrDataDirectory,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _sourceSettings = _sourceSettings with { OcrDataDirectory = dialog.SelectedPath };
            SaveSourceSettings();
            await RefreshSourceLanguagesAsync();
        };
        refreshButton.Click += async (_, _) => await RefreshSourceLanguagesAsync();
        var actions = new FlowLayoutPanel { Name = "OcrFolderActions", WrapContents = false };
        actions.Controls.Add(folderButton);
        actions.Controls.Add(refreshButton);
        section.Controls.AddRange([_sourceStatus, _dataFolder, actions, _settingsError]);
        var translationActions = CreateTranslationModelControls(section);
        void LayoutLanguages()
        {
            int U(int value) => LogicalToDeviceUnits(value);
            int width = Math.Max(0, section.ClientSize.Width - U(50));
            section.Controls.Find("SectionDescription", false)[0].Width = width;
            inputs.SetBounds(U(25), U(92), width, U(66));
            _sourceStatus.SetBounds(U(25), U(157), width, U(38));
            _dataFolder.SetBounds(U(25), U(198), width, U(22));
            actions.SetBounds(U(22), U(224), width, U(34));
            _settingsError.SetBounds(U(25), U(263), width, U(36));
            int targetTop = string.IsNullOrEmpty(_settingsError.Text) ? 268 : 310;
            _targetStatus.SetBounds(U(25), U(targetTop), width, U(64));
            _translationFolder.SetBounds(U(25), U(targetTop + 68), width, U(22));
            translationActions.SetBounds(U(22), U(targetTop + 94), width, U(34));
            _targetSettingsError.SetBounds(U(25), U(targetTop + 132), width, U(string.IsNullOrEmpty(_targetSettingsError.Text) ? 0 : 42));
            section.Height = U(targetTop + (string.IsNullOrEmpty(_targetSettingsError.Text) ? 136 : 182));
        }
        section.Resize += (_, _) => LayoutLanguages();
        _settingsError.TextChanged += (_, _) => LayoutLanguages();
        _targetSettingsError.TextChanged += (_, _) => LayoutLanguages();
        LayoutLanguages();
        return section;
    }

    public async Task RefreshSourceLanguagesAsync()
    {
        int version = ++_refreshVersion;
        _sourceLanguage.Enabled = false;
        UpdateTranslationModelStatus();
        string directory = OcrDataDirectory;
        _dataFolder.Text = $"OCR data: {directory}";
        _dataFolder.AccessibleDescription = directory;
        _sourceStatus.Text = "Checking installed OCR languages…";
        OcrLanguageScan scan = await Task.Run(() => _languageCatalog.Scan(directory));
        if (IsDisposed || Disposing || version != _refreshVersion) return;

        string? previousCode = _sourceSettings.SourceLanguageCode;
        OcrLanguage? selected = OcrLanguageCatalog.ResolveSelection(scan.Languages, previousCode);
        _bindingLanguages = true;
        try
        {
            _sourceLanguage.Items.Clear();
            _sourceLanguage.Items.AddRange(scan.Languages.Cast<object>().ToArray());
            _sourceLanguage.SelectedItem = selected;
        }
        finally { _bindingLanguages = false; }
        _sourceLanguage.Enabled = scan.Languages.Count > 0;
        _ocrModelStatus.Text = selected is null ? "●  Not installed" : "●  Installed";
        _ocrModelStatus.ForeColor = selected is null ? Color.FromArgb(173, 104, 27) : Color.FromArgb(45, 112, 72);
        bool selectionChanged = previousCode is not null && selected?.Code != previousCode;
        _sourceStatus.Text = scan.Error ?? (selected is null
            ? "No OCR languages installed. Choose a folder containing .traineddata files, then refresh."
            : selectionChanged
                ? $"Previous language is unavailable. Selected {selected}."
                : $"{scan.Languages.Count} installed OCR language(s). Choose the language of your screen text.");
        // Preserve the saved preference on a temporary folder access error.
        if (scan.Error is null && selected?.Code != previousCode)
        {
            _sourceSettings = _sourceSettings with { SourceLanguageCode = selected?.Code };
            SaveSourceSettings();
        }
        await RefreshTranslationModelsAsync();
    }

    private async void SourceLanguageChanged(object? sender, EventArgs e)
    {
        if (_bindingLanguages) return;
        _sourceSettings = _sourceSettings with { SourceLanguageCode = SelectedSourceLanguageCode };
        SaveSourceSettings();
        await RefreshTranslationModelsAsync();
    }

    private void SaveSourceSettings() => _settingsError.Text = _settingsStore.Save(_sourceSettings) ?? "";

    private Control CreateAppearanceSection()
    {
        var section = CreateSection(
            "Appearance",
            "Use your Windows theme or choose one for this app.",
            108);

        var choices = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Size = new Size(320, 38),
            WrapContents = false
        };
        choices.Controls.Add(CreateThemeButton("System", true));
        choices.Controls.Add(CreateThemeButton("Light", false));
        choices.Controls.Add(CreateThemeButton("Dark", false));
        section.Controls.Add(choices);
        PositionRight(section, choices, 320, 35);
        return section;
    }

    private Control CreateShortcutSection()
    {
        var section = CreateSection(
            "Shortcut",
            "Press these keys anywhere to start selecting a region.",
            108);
        var shortcut = new Label
        {
            BackColor = Color.FromArgb(247, 247, 250),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Ink,
            Size = new Size(200, 40),
            Text = "Ctrl  +  Shift  +  T",
            TextAlign = ContentAlignment.MiddleCenter
        };
        section.Controls.Add(shortcut);
        PositionRight(section, shortcut, 200, 34);
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
            ColumnCount = 2,
            RowCount = 3,
            Size = new Size(380, 114)
        };
        modelList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        modelList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        modelList.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        modelList.Controls.Add(CreateModelName("OCR model"), 0, 0);
        _ocrModelStatus = CreateModelStatus();
        modelList.Controls.Add(_ocrModelStatus, 1, 0);
        modelList.Controls.Add(CreateModelName("Translation model"), 0, 1);
        _translationModelStatus = CreateModelStatus();
        _translationModelStatus.Name = "TranslationModelStatus";
        modelList.Controls.Add(_translationModelStatus, 1, 1);

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
        PositionRight(section, modelList, 380, 19);
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
            Name = "SectionDescription",
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

    private void PositionRight(Control parent, Control child, int width, int top)
    {
        void Reposition()
        {
            int U(int value) => LogicalToDeviceUnits(value);
            bool stacked = parent.ClientSize.Width < U(325 + width);
            child.SetBounds(
                stacked ? U(25) : parent.ClientSize.Width - U(width + 25),
                U(stacked ? 100 : top),
                Math.Min(U(width), Math.Max(0, parent.ClientSize.Width - U(50))),
                child.Height);
            parent.Height = Math.Max(U(stacked ? 100 : 0) + child.Height + U(20), U(top * 2) + child.Height);
        }

        parent.Resize += (_, _) => Reposition();
        Reposition();
    }

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
        int width = Math.Max(0, _page.ClientSize.Width - LogicalToDeviceUnits(2));
        int top = _page.AutoScrollPosition.Y;
        foreach (Control control in _page.Controls)
        {
            control.SetBounds(0, top, width, control.Height);
            top += control.Height + control.Margin.Bottom;
        }
        // ScrollableControl calculates its scroll range before Resize handlers run.
        // Recalculate after the responsive sections have settled at their new sizes.
        if (_page.IsHandleCreated)
            _page.BeginInvoke((Action)(() => { if (!_page.IsDisposed) _page.PerformLayout(); }));
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
