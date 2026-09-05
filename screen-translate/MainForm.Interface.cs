using System.ComponentModel;
using Microsoft.Win32;
using screen_translate.Interface;
using screen_translate.Settings;

namespace screen_translate;

public partial class MainForm
{
    private readonly ApplicationLifetime _lifetime = new();
    private readonly InterfaceSettingsStore _interfaceSettingsStore;
    private readonly IGlobalShortcut _globalShortcut;
    private InterfaceSettings _interfaceSettings;
    private Label _readinessStatus = null!;
    private Label _interfaceSettingsError = null!;
    private TextBox _shortcutInput = null!;
    private Label _shortcutStatus = null!;
    private Keys _pendingShortcut;
    private bool _checkingSourceLanguages = true;
    private string? _sourceScanError;
    private string? _shortcutError = "The global shortcut has not been registered yet.";
    private bool _darkTheme;
    private bool _mainWindowShown;
    private Color ModelGoodColor => _darkTheme ? Color.FromArgb(133, 218, 166) : Color.FromArgb(45, 112, 72);
    private Color ModelWarningColor => _darkTheme ? Color.FromArgb(255, 199, 123) : Color.FromArgb(173, 104, 27);
    private readonly Dictionary<Control, (Color Back, Color Fore)> _originalColors = [];
    private readonly Dictionary<RoundedPanel, Color> _originalBorders = [];
    private readonly Dictionary<Control, (string Family, float Pixels, FontStyle Style)> _fontSpecs = [];
    private readonly Dictionary<(string Family, float Pixels, FontStyle Style, int Dpi), Font> _dpiFonts = [];
    private int _typographyDpi;
    private string? _interfacePreferenceError;

    // Replace this blocker only when a runtime can validate and execute the selected configuration.
    private const string RuntimeUnavailable = "Screen translation isn't available in this build yet. You can still set up your languages and preferences.";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CancellationToken WorkCancellationToken => _lifetime.Token;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TranslationReadiness Readiness => TranslationReadiness.Evaluate(
        _checkingSourceLanguages || _checkingTranslationModels, _sourceScanError,
        SelectedSourceLanguageCode, SelectedTargetLanguageCode, _translationScan, RuntimeUnavailable, _shortcutError);

    /// <summary>Selection and overlay implementations must use this owner and the work cancellation token.</summary>
    public void ShowTranslationWindow(Form window)
    {
        if (_lifetime.IsStopped) { window.Dispose(); return; }
        window.Show(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        // Owned selection/overlay windows must not veto the Version 1.0 exit action.
        e.Cancel = false;
        StopApplicationWork();
    }

    private void StopApplicationWork()
    {
        if (_lifetime.IsStopped) return;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        _lifetime.Dispose();
        // Dispose bypasses child Close cancellation: no selection or overlay may keep the app alive.
        foreach (var window in OwnedForms) window.Dispose();
    }

    private void InitializeInterfaceBehavior(string? error)
    {
        _interfacePreferenceError = error;
        UpdateSettingsErrors();
        _globalShortcut.Pressed += (_, _) =>
        {
            if (_lifetime.IsStopped) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            NavigateTo("TranslationReadiness");
            UpdateReadiness();
        };
        void RememberColors(Control parent)
        {
            _originalColors[parent] = (parent.BackColor, parent.ForeColor);
            _fontSpecs[parent] = (parent.Font.FontFamily.Name, parent.Font.SizeInPoints * 96F / 72F, parent.Font.Style);
            if (parent is RoundedPanel panel) _originalBorders[panel] = panel.BorderColor;
            foreach (Control child in parent.Controls) RememberColors(child);
        }
        RememberColors(this);
        ApplyTypography();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        ApplyTheme();
        UpdateReadiness();
    }

    private void ApplyTypography()
    {
        if (_typographyDpi == DeviceDpi) return;
        _typographyDpi = DeviceDpi;
        foreach (var (control, spec) in _fontSpecs)
        {
            var key = (spec.Family, spec.Pixels, spec.Style, DeviceDpi);
            if (!_dpiFonts.TryGetValue(key, out var font))
            {
                font = new Font(spec.Family, spec.Pixels * DeviceDpi / 96F, spec.Style, GraphicsUnit.Pixel);
                _dpiFonts.Add(key, font);
                _lifetime.Own(font);
            }
            control.Font = font;
        }
        foreach (var combo in new[] { _sourceLanguage, _targetLanguage })
        {
            combo.ItemHeight = U(32);
            combo.DropDownHeight = U(260);
        }
    }

    private void NavigateTo(string name)
    {
        ShowPage(name is not "TranslationReadiness" and not "SourceLanguage");
        var control = Controls.Find(name, true).Single();
        _page.ScrollControlIntoView(control);
        if (control.CanSelect) control.Select();
        else if (control.HasChildren) control.SelectNextControl(null, true, true, true, false);
    }

    private void UpdateReadiness()
    {
        if (_readinessStatus is null) return;
        var readiness = Readiness;
        string title = readiness.State switch
        {
            ReadinessState.Ready => "Ready",
            ReadinessState.Checking => "Checking…",
            _ => "Action required"
        };
        string? source = (_sourceLanguage.SelectedItem as Ocr.OcrLanguage)?.DisplayName;
        string target = _targetLanguage.SelectedItem?.ToString() ?? SelectedTargetLanguageCode;
        _readinessTitle.Text = source is null ? title : $"{title} · {source} → {target}";
        _readinessStatus.Text = readiness.Reason;
        _readinessStatus.AccessibleDescription = _readinessTitle.Text + ". " + readiness.Reason;
        _readinessTitle.ForeColor = readiness.State == ReadinessState.ActionRequired
            ? (_darkTheme ? Color.FromArgb(237, 195, 126) : Color.FromArgb(135, 87, 23))
            : (_darkTheme ? Color.FromArgb(196, 187, 255) : Accent);
        _readinessStatus.ForeColor = _darkTheme ? Color.FromArgb(203, 204, 217) : Ink;
        bool actionRequired = readiness.State == ReadinessState.ActionRequired;
        _readinessCard.BackColor = actionRequired ? (_darkTheme ? Color.FromArgb(44, 40, 36) : Color.FromArgb(255, 249, 237))
            : (_darkTheme ? Color.FromArgb(36, 34, 53) : AccentSoft);
        _readinessCard.BorderColor = actionRequired ? (_darkTheme ? Color.FromArgb(79, 67, 45) : Color.FromArgb(237, 223, 194))
            : (_darkTheme ? Color.FromArgb(66, 60, 92) : Color.FromArgb(216, 211, 246));
        _readinessCard.Invalidate();
        _languageHint.Text = _checkingSourceLanguages ? "Checking installed source languages…" : source is null
            ? "No OCR languages found. Add a source language in Offline models."
            : "Only installed OCR languages appear in Read from. Target languages are always available.";
    }

    private void WireShortcutEditor(Button apply)
    {
        _shortcutInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Tab) return;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
            {
                _pendingShortcut = _interfaceSettings.Shortcut;
                _shortcutInput.Text = InterfaceSettings.FormatShortcut(_pendingShortcut);
                return;
            }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            _pendingShortcut = e.KeyData;
            _shortcutInput.Text = InterfaceSettings.FormatShortcut(_pendingShortcut);
        };
        apply.Click += (_, _) =>
        {
            string? error = _globalShortcut.TrySet(_pendingShortcut);
            if (error is not null)
            {
                _shortcutStatus.Text = error + " Configured: " + InterfaceSettings.FormatShortcut(_interfaceSettings.Shortcut);
                return;
            }
            _interfaceSettings = _interfaceSettings with { Shortcut = _pendingShortcut };
            _shortcutError = null;
            SaveInterfaceSettings();
            ShowShortcutStatus();
            UpdateReadiness();
        };
    }

    private void RegisterShortcut()
    {
        if (_lifetime.IsStopped) return;
        _shortcutError = _globalShortcut.TrySet(_interfaceSettings.Shortcut);
        ShowShortcutStatus();
        UpdateReadiness();
    }

    private void ShowShortcutStatus() => _shortcutStatus.Text = _shortcutError ??
        $"Registered · {InterfaceSettings.FormatShortcut(_interfaceSettings.Shortcut)}";

    private void SaveInterfaceSettings()
    {
        _interfacePreferenceError = _interfaceSettingsStore.Save(_interfaceSettings);
        UpdateSettingsErrors();
    }

    private void UpdateSettingsErrors()
    {
        if (_interfaceSettingsError is null) return;
        _interfaceSettingsError.Text = string.Join("\n", new[]
        {
            _interfacePreferenceError,
            _showModels ? null : _settingsError?.Text,
            _showModels ? null : _targetSettingsError?.Text
        }.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_lifetime.IsStopped || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!_lifetime.IsStopped) ApplyTheme(); })); }
        catch (InvalidOperationException) { } // Handle was destroyed during shutdown.
    }

    private void ApplyTheme()
    {
        bool systemDark = false;
        if (_interfaceSettings.Theme == AppTheme.System)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                systemDark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        _darkTheme = _interfaceSettings.Theme == AppTheme.Dark || (_interfaceSettings.Theme == AppTheme.System && systemDark);
        Color MapBack(Color color)
        {
            if (!_darkTheme || color == Accent || color == Color.Transparent) return color;
            if (color == Border) return Color.FromArgb(62, 67, 84);
            return color == Canvas ? Color.FromArgb(23, 25, 34) : Color.FromArgb(32, 35, 47);
        }
        Color MapFore(Color color)
        {
            if (!_darkTheme || color == Color.White) return color;
            if (color == Muted) return Color.FromArgb(183, 188, 200);
            if (color == Accent) return Color.FromArgb(184, 177, 255);
            if (color == Ink || color == SystemColors.ControlText || color == SystemColors.WindowText) return Color.FromArgb(238, 240, 245);
            return Color.FromArgb(255, 199, 123);
        }
        foreach (var (control, colors) in _originalColors)
        {
            control.BackColor = MapBack(colors.Back);
            control.ForeColor = MapFore(colors.Fore);
        }
        foreach (var (panel, color) in _originalBorders) panel.BorderColor = _darkTheme ? Color.FromArgb(65, 69, 81) : color;
        foreach (var button in _originalColors.Keys.OfType<PillButton>().Where(button => !button.IsTab && !button.IsSegment))
            button.BorderColor = button.Primary ? Accent : (_darkTheme ? Color.FromArgb(62, 67, 84) : Border);
        foreach (PillButton button in _themeButtons)
        {
            bool selected = button.Text == _interfaceSettings.Theme.ToString();
            button.Selected = selected;
            button.BackColor = selected ? (_darkTheme ? Color.FromArgb(53, 45, 84) : AccentSoft) : MapBack(Surface);
            button.BorderColor = selected ? (_darkTheme ? Color.FromArgb(143, 127, 229) : Accent) : (_darkTheme ? Color.FromArgb(62, 67, 84) : Border);
            button.ForeColor = selected ? (_darkTheme ? Color.FromArgb(204, 192, 255) : Accent) : MapFore(Muted);
        }
        foreach (var tab in new[] { _generalTab, _modelsTab })
        {
            bool selected = ReferenceEquals(tab, _modelsTab) == _showModels;
            tab.Selected = selected;
            tab.BackColor = MapBack(Canvas);
            tab.ForeColor = selected ? (_darkTheme ? Color.FromArgb(191, 177, 255) : Accent) : MapFore(Muted);
        }
        foreach (var badge in new[] { _ocrModelStatus, _translationModelStatus })
            badge.ForeColor = badge.Text is "●  Installed" or "●  Not required" ? ModelGoodColor
                : badge.Text.Contains("Checking") ? MapFore(Muted) : ModelWarningColor;
        UpdateReadiness();
        Invalidate(true);
    }
}
