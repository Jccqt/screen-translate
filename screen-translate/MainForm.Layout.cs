using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

namespace screen_translate;

public partial class MainForm
{
    private Panel _generalPage = null!;
    private Panel _modelsPage = null!;
    private Panel _content = null!;
    private Panel _sectionHeader = null!;
    private RoundedPanel _readinessCard = null!;
    private Label _readinessTitle = null!;
    private Label _pageTitle = null!;
    private Label _pageSubtitle = null!;
    private Label _languageHint = null!;
    private PillButton _generalTab = null!;
    private PillButton _modelsTab = null!;
    private PillButton _readinessAction = null!;
    private bool _showModels;
    private bool _layingOut;
    private bool _layoutQueued;
    private readonly List<Action> _responsiveLayouts = [];
    private readonly ToolTip _tooltips = new();

    private int U(int value) => LogicalToDeviceUnits(value);

    private static Label TextLabel(string text, float size = 10F, bool strong = false, bool muted = false) => new()
    {
        Text = text, Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size),
        ForeColor = muted ? Muted : Ink, BackColor = Color.Transparent,
        AutoSize = false, UseMnemonic = false
    };

    private PillButton ActionButton(string text, string name, bool primary = false) => new()
    {
        Text = text, Name = name, AccessibleName = text, Size = new Size(136, 36),
        Font = new Font("Segoe UI Semibold", 9.5F), CornerRadius = 6,
        BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.White : Ink,
        BorderColor = primary ? Accent : Border, Primary = primary, Cursor = Cursors.Hand,
        Margin = new Padding(0, 0, 8, 0)
    };

    private RoundedPanel Card(string name) => new()
    {
        Name = name, BackColor = Surface, BorderColor = Border, CornerRadius = 10,
        Margin = new Padding(0, 0, 0, 12)
    };

    private void BuildInterface()
    {
        SuspendLayout();
        Text = "Screen Translate";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 700);
        ClientSize = new Size(1040, 840);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        _lifetime.Own(_tooltips);

        var masthead = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Surface };
        var mark = new AppMark { Location = new Point(32, 22), Size = new Size(40, 40), BackColor = Accent, ForeColor = Color.White };
        var brand = TextLabel("Screen Translate", 14, true);
        brand.SetBounds(84, 28, 240, 30);
        var privacy = TextLabel("Local processing. Private by design.", 9.5F, muted: true);
        privacy.TextAlign = ContentAlignment.MiddleRight;
        masthead.Controls.AddRange([mark, brand, privacy]);
        masthead.Resize += (_, _) => privacy.SetBounds(masthead.Width - U(344), U(27), U(312), U(30));
        var line = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Border };
        masthead.Controls.Add(line);

        _page = new Panel { Name = "SettingsViewport", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Canvas };
        _content = new Panel { Name = "SettingsContent", BackColor = Canvas, Margin = Padding.Empty };
        _page.Controls.Add(_content);
        _pageTitle = TextLabel("Translation setup", 23, true);
        _pageTitle.Name = "PageTitle";
        _pageSubtitle = TextLabel("Choose how screen text is captured and translated.", 10.5F, muted: true);
        _generalTab = ActionButton("General", "NavigateSettings");
        _modelsTab = ActionButton("Offline models", "NavigateModels");
        _generalTab.IsTab = _modelsTab.IsTab = true;
        _generalTab.Click += (_, _) => ShowPage(false);
        _modelsTab.Click += (_, _) => { ShowPage(true); FocusModelActions(); };
        _sectionHeader = new Panel { Name = "PageHeading", Dock = DockStyle.Top, Height = 122, BackColor = Canvas };
        _sectionHeader.Controls.AddRange([_pageTitle, _pageSubtitle, _generalTab, _modelsTab]);

        BuildReadinessCard();
        _interfaceSettingsError = TextLabel("", 9.5F);
        _interfaceSettingsError.Name = "InterfaceSettingsError";
        _interfaceSettingsError.ForeColor = Color.FromArgb(160, 65, 35);
        _interfaceSettingsError.TextChanged += (_, _) => LayoutPages();
        _content.Controls.Add(_interfaceSettingsError);

        _generalPage = new Panel { Name = "GeneralPage", BackColor = Canvas };
        _modelsPage = new Panel { Name = "ModelsPage", BackColor = Canvas, Visible = false };
        // Construct model labels before language selection events can cause a refresh.
        BuildModelsPage();
        BuildGeneralPage();
        _content.Controls.AddRange([_generalPage, _modelsPage]);
        Controls.Add(_page);
        Controls.Add(_sectionHeader);
        Controls.Add(masthead);
        _page.Resize += (_, _) => LayoutPages();
        DpiChanged += (_, _) =>
        {
            // Apply custom typography after Windows has finished notifying child controls.
            BeginInvoke((Action)(() => { if (!_lifetime.IsStopped) { ApplyTypography(); LayoutPages(); Invalidate(true); } }));
        };
        ResumeLayout(true);
        LayoutPages();
    }

    private void BuildReadinessCard()
    {
        _readinessCard = Card("ReadinessCard");
        _readinessTitle = TextLabel("Checking your setup", 10.5F, true);
        _readinessStatus = TextLabel("Checking the configured language pair…", 9.5F);
        _readinessStatus.Name = "TranslationReadiness";
        _readinessStatus.AccessibleName = "Translation readiness";
        _readinessAction = ActionButton("Manage models", "ManageModels", primary: true);
        _readinessAction.Click += async (_, _) =>
        {
            if (_showModels) await RefreshSourceLanguagesAsync();
            else { ShowPage(true); FocusModelActions(); }
        };
        _readinessCard.Controls.AddRange([_readinessTitle, _readinessStatus, _readinessAction]);
        _content.Controls.Add(_readinessCard);
    }

    private void BuildGeneralPage()
    {
        var languages = Card("LanguagesCard");
        var title = TextLabel("Translation languages", 12, true);
        var sourceLabel = TextLabel("Read from", 9.5F, muted: true);
        var targetLabel = TextLabel("Translate to", 9.5F, muted: true);
        var arrow = TextLabel("→", 19, muted: true);
        arrow.TextAlign = ContentAlignment.MiddleCenter;
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
        _languageHint = TextLabel("Only installed OCR languages appear in Read from.", 9F, muted: true);
        _languageHint.Name = "LanguageHint";
        languages.Controls.AddRange([title, sourceLabel, targetLabel, arrow, _sourceLanguage, _targetLanguage, _languageHint]);
        _generalPage.Controls.Add(languages);
        _responsiveLayouts.Add(() =>
        {
            int w = languages.Width, half = (w - U(92)) / 2;
            title.SetBounds(U(24), U(19), w - U(48), U(28));
            sourceLabel.SetBounds(U(24), U(58), half, U(22));
            targetLabel.SetBounds(U(68) + half, U(58), half, U(22));
            _sourceLanguage.SetBounds(U(24), U(85), half, U(38));
            arrow.SetBounds(U(24) + half, U(84), U(44), U(38));
            _targetLanguage.SetBounds(U(68) + half, U(85), half, U(38));
            _languageHint.SetBounds(U(24), U(137), w - U(48), U(24));
            languages.Height = U(178);
        });

        var appearance = Card("AppearanceCard");
        var appearanceTitle = TextLabel("Appearance", 11, true);
        var appearanceHint = TextLabel("Follow Windows or choose a theme.", 9.5F, muted: true);
        appearance.Controls.AddRange([appearanceTitle, appearanceHint]);
        foreach (string theme in new[] { "System", "Light", "Dark" })
        {
            var button = ActionButton(theme, "Theme" + theme);
            button.IsSegment = true;
            button.Click += ThemeButton_Click;
            _themeButtons.Add(button);
            appearance.Controls.Add(button);
        }
        _generalPage.Controls.Add(appearance);
        _responsiveLayouts.Add(() =>
        {
            appearanceTitle.SetBounds(U(24), U(22), U(320), U(24));
            appearanceHint.SetBounds(U(24), U(49), U(320), U(23));
            for (int i = 0; i < _themeButtons.Count; i++)
                _themeButtons[i].SetBounds(appearance.Width - U(328) + U(i * 104), U(29), U(96), U(38));
            appearance.Height = U(96);
        });

        var shortcut = Card("ShortcutCard");
        var shortcutTitle = TextLabel("Global shortcut", 11, true);
        var shortcutHint = TextLabel("Focus the field, press your keys, then Apply.", 9.5F, muted: true);
        _pendingShortcut = _interfaceSettings.Shortcut;
        var inputFrame = Card("ShortcutField");
        inputFrame.CornerRadius = 6;
        _shortcutInput = new TextBox
        {
            Name = "GlobalShortcut", AccessibleName = "Global translation shortcut", ReadOnly = true,
            BorderStyle = BorderStyle.None, BackColor = Surface, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 10),
            Text = InterfaceSettings.FormatShortcut(_pendingShortcut), TextAlign = HorizontalAlignment.Center
        };
        inputFrame.Controls.Add(_shortcutInput);
        _shortcutInput.GotFocus += (_, _) => { inputFrame.BorderColor = Accent; inputFrame.Invalidate(); };
        _shortcutInput.LostFocus += (_, _) => { inputFrame.BorderColor = _darkTheme ? Color.FromArgb(62, 67, 84) : Border; inputFrame.Invalidate(); };
        var apply = ActionButton("Apply", "ApplyShortcut");
        _shortcutStatus = TextLabel("Use Ctrl or Alt with a letter, number, or F key.", 9F, muted: true);
        _shortcutStatus.Name = "ShortcutStatus";
        WireShortcutEditor(apply);
        shortcut.Controls.AddRange([shortcutTitle, shortcutHint, inputFrame, apply, _shortcutStatus]);
        _generalPage.Controls.Add(shortcut);
        _responsiveLayouts.Add(() =>
        {
            int right = shortcut.Width - U(388);
            shortcutTitle.SetBounds(U(24), U(22), U(340), U(24));
            shortcutHint.SetBounds(U(24), U(50), U(340), U(42));
            inputFrame.SetBounds(right, U(23), U(264), U(40));
            _shortcutInput.SetBounds(U(12), U(10), U(240), U(24));
            apply.SetBounds(right + U(276), U(23), U(88), U(40));
            _shortcutStatus.SetBounds(right, U(72), U(364), U(48));
            shortcut.Height = U(132);
        });

        var footer = TextLabel("Changes are saved automatically. Closing this window exits the app.", 9F, muted: true);
        footer.Name = "GeneralFooter";
        _generalPage.Controls.Add(footer);
    }

    private void BuildModelsPage()
    {
        var ocr = Card("OcrModelCard");
        var ocrTitle = TextLabel("Text recognition", 12, true);
        var ocrDescription = TextLabel("Tesseract OCR · Reads text in the source language", 9.5F, muted: true);
        _ocrModelStatus = TextLabel("●  Checking…", 9.5F, true);
        _ocrModelStatus.Name = "OcrModelStatus";
        _ocrModelStatus.TextAlign = ContentAlignment.MiddleRight;
        _sourceStatus = TextLabel("Checking installed OCR languages…", 9.5F, muted: true);
        _sourceStatus.Name = "SourceLanguageStatus";
        _dataFolder = FolderLabel("OcrDataFolder");
        _settingsError = ErrorLabel("SourceSettingsError");
        var folder = ActionButton("Choose folder…", "ChooseOcrFolder");
        folder.AccessibleName = "Choose OCR data folder";
        folder.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the tessdata folder containing your .traineddata language files.",
                UseDescriptionForTitle = true, SelectedPath = OcrDataDirectory, ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _sourceSettings = _sourceSettings with { OcrDataDirectory = dialog.SelectedPath };
            SaveSourceSettings();
            await RefreshSourceLanguagesAsync();
        };
        var refresh = ActionButton("Refresh languages", "RefreshOcrLanguages");
        refresh.Width = 154;
        refresh.Click += async (_, _) => await RefreshSourceLanguagesAsync();
        var ocrActions = new FlowLayoutPanel { Name = "OcrFolderActions", WrapContents = false, BackColor = Color.Transparent };
        ocrActions.Controls.AddRange([folder, refresh]);
        ocr.Controls.AddRange([ocrTitle, ocrDescription, _ocrModelStatus, _sourceStatus, _dataFolder, ocrActions, _settingsError]);
        _modelsPage.Controls.Add(ocr);

        var translation = Card("TranslationModelCard");
        var translationTitle = TextLabel("Translation", 12, true);
        var translationDescription = TextLabel("Offline language packages · Specific to your language pair", 9.5F, muted: true);
        _translationModelStatus = TextLabel("●  Checking…", 9.5F, true);
        _translationModelStatus.Name = "TranslationModelStatus";
        _translationModelStatus.TextAlign = ContentAlignment.MiddleRight;
        var translationActions = CreateTranslationModelControls(translation);
        translation.Controls.AddRange([translationTitle, translationDescription, _translationModelStatus]);
        _modelsPage.Controls.Add(translation);

        void ModelLayout(RoundedPanel card, Label title, Label subtitle, Label badge, Label status, Label path, Control actions, Label error)
        {
            int width = card.Width - U(48);
            title.SetBounds(U(24), U(19), width - U(180), U(28));
            badge.SetBounds(card.Width - U(206), U(19), U(182), U(28));
            subtitle.SetBounds(U(24), U(51), width, U(24));
            status.SetBounds(U(24), U(88), width, U(44));
            path.SetBounds(U(24), U(144), width, U(36));
            actions.SetBounds(U(24), U(194), width, U(38));
            bool hasError = !string.IsNullOrEmpty(error.Text);
            error.SetBounds(U(24), U(243), width, hasError ? U(44) : 0);
            card.Height = U(hasError ? 303 : 250);
        }
        _responsiveLayouts.Add(() => ModelLayout(ocr, ocrTitle, ocrDescription, _ocrModelStatus, _sourceStatus, _dataFolder, ocrActions, _settingsError));
        _responsiveLayouts.Add(() => ModelLayout(translation, translationTitle, translationDescription, _translationModelStatus, _targetStatus, _translationFolder, translationActions, _targetSettingsError));
        _settingsError.TextChanged += (_, _) => { UpdateSettingsErrors(); LayoutPages(); };
        _targetSettingsError.TextChanged += (_, _) => { UpdateSettingsErrors(); LayoutPages(); };
        var footer = TextLabel("Model files stay on this device. Finding a package does not verify that an engine can load it.", 9F, muted: true);
        footer.Name = "ModelsFooter";
        _modelsPage.Controls.Add(footer);
    }

    private Label FolderLabel(string name)
    {
        var label = TextLabel("", 9F, muted: true);
        label.Name = name;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Padding = new Padding(10, 0, 10, 0);
        label.BackColor = Canvas;
        label.TextChanged += (_, _) => _tooltips.SetToolTip(label, label.Text);
        return label;
    }

    private static Label ErrorLabel(string name) => new()
    {
        Name = name, Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(160, 65, 35), BackColor = Color.Transparent
    };

    private static ComboBox CreateComboBox(object[] choices)
    {
        var combo = new LanguagePicker
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 11F),
            ForeColor = Ink, BackColor = Surface, Margin = Padding.Empty,
            ItemHeight = 28, IntegralHeight = false, DropDownHeight = 260
        };
        combo.Items.AddRange(choices);
        return combo;
    }

    private void ShowPage(bool models)
    {
        _showModels = models;
        _generalPage.Visible = !models;
        _modelsPage.Visible = models;
        _readinessAction.Text = models ? "Refresh setup" : "Manage models";
        _readinessAction.AccessibleName = _readinessAction.Text;
        _pageTitle.Text = models ? "Offline models" : "Translation setup";
        _pageSubtitle.Text = models ? "Everything your languages need, stored on your device." : "Choose how screen text is captured and translated.";
        _page.AutoScrollPosition = Point.Empty;
        UpdateSettingsErrors();
        LayoutPages();
        ApplyTheme();
    }

    private void FocusModelActions()
    {
        var actions = Controls.Find(_sourceLanguage.Items.Count == 0 ? "OcrFolderActions" : "TranslationFolderActions", true).Single();
        actions.SelectNextControl(null, true, true, true, false);
    }

    private void LayoutPages()
    {
        if (_layingOut || _generalPage is null || _modelsPage is null) return;
        _layingOut = true;
        try
        {
            int width = Math.Min(U(960), Math.Max(U(720), _page.Width - U(80)));
            int left = Math.Max(U(24), (_page.Width - width) / 2);
            _content.SetBounds(left, _page.AutoScrollPosition.Y, width, _content.Height);
            _pageTitle.SetBounds(left, U(26), width - U(300), U(44));
            _pageSubtitle.SetBounds(left, U(75), width, U(26));
            _generalTab.SetBounds(left + width - U(276), U(30), U(108), U(42));
            _modelsTab.SetBounds(left + width - U(152), U(30), U(152), U(42));
            _readinessCard.SetBounds(0, 0, width, U(96));
            _readinessTitle.SetBounds(U(20), U(16), width - U(210), U(24));
            _readinessStatus.SetBounds(U(20), U(44), width - U(210), U(40));
            _readinessAction.SetBounds(width - U(168), U(29), U(148), U(38));
            bool hasError = !string.IsNullOrEmpty(_interfaceSettingsError.Text);
            int errorHeight = hasError ? Math.Max(U(36), TextRenderer.MeasureText(_interfaceSettingsError.Text, _interfaceSettingsError.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + U(8)) : 0;
            _interfaceSettingsError.SetBounds(0, U(106), width, errorHeight);
            int pagesTop = U(112) + (hasError ? errorHeight + U(10) : 0);
            foreach (var page in new[] { _generalPage, _modelsPage }) page.SetBounds(0, pagesTop, width, page.Height);
            foreach (var card in _generalPage.Controls.OfType<RoundedPanel>().Concat(_modelsPage.Controls.OfType<RoundedPanel>())) card.Width = width;
            foreach (var layout in _responsiveLayouts) layout();
            foreach (var page in new[] { _generalPage, _modelsPage })
            {
                int y = 0;
                foreach (Control child in page.Controls)
                {
                    child.SetBounds(0, y, width, child is Label ? U(36) : child.Height);
                    y += child.Height + U(12);
                }
                page.Height = y;
            }
            _content.Height = pagesTop + (_showModels ? _modelsPage.Height : _generalPage.Height);
            _page.AutoScrollMinSize = new Size(0, _content.Height + U(26));
        }
        finally { _layingOut = false; }
        // ScrollableControl measures its range before Resize handlers; measure the settled cards again.
        if (_page.IsHandleCreated && !_layoutQueued)
        {
            _layoutQueued = true;
            _page.BeginInvoke((Action)(() =>
            {
                _layoutQueued = false;
                if (!_page.IsDisposed) _page.PerformLayout();
            }));
        }
    }
}
