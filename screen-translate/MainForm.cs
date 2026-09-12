using System.ComponentModel;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;
using screen_translate.Interface;
using screen_translate.Capture;

namespace screen_translate;

public partial class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(245, 247, 246);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(216, 225, 222);
    private static readonly Color Ink = Color.FromArgb(36, 52, 60);
    private static readonly Color Muted = Color.FromArgb(94, 111, 112);
    private static readonly Color Accent = Color.FromArgb(8, 118, 104);
    private static readonly Color AccentSoft = Color.FromArgb(231, 244, 240);
    private static readonly Color DarkCanvas = Color.FromArgb(22, 32, 37);
    private static readonly Color DarkSurface = Color.FromArgb(30, 43, 48);
    private static readonly Color DarkBorder = Color.FromArgb(57, 76, 80);
    private static readonly Color DarkInk = Color.FromArgb(228, 234, 233);
    private static readonly Color DarkMuted = Color.FromArgb(163, 182, 183);
    private static readonly Color DarkAccent = Color.FromArgb(49, 196, 174);
    private static readonly Color DarkAccentSoft = Color.FromArgb(29, 64, 62);
    private readonly AppArtwork _artwork = new();

    private readonly List<PillButton> _themeButtons = [];
    private Panel _page = null!;
    private ComboBox _sourceLanguage = null!;
    private Label _sourceStatus = null!;
    private Label _dataFolder = null!;
    private Label _ocrModelStatus = null!;
    private Label _settingsError = null!;
    private readonly OcrLanguageCatalog _languageCatalog = new();
    private readonly SourceLanguageSettingsStore _settingsStore;
    private SourceLanguageSettings _sourceSettings;
    private bool _sourceSettingsNeedRecovery;
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
        ITranslationModelCatalog? translationCatalog = null, InterfaceSettingsStore? interfaceSettingsStore = null,
        IGlobalShortcut? globalShortcut = null, IOcrEngine? ocrEngine = null, ISystemThemeSource? systemThemeSource = null,
        ITranslationWorkflow? translationWorkflow = null, IScreenRegionCaptureService? captureService = null,
        ITranslationEngine? translationEngine = null)
    {
        _settingsStore = settingsStore;
        _ocrEngine = ocrEngine ?? new TesseractOcrEngine();
        _sourceSettings = _settingsStore.Load(out string? error);
        _sourceSettingsNeedRecovery = error is not null;
        _targetSettingsStore = targetSettingsStore;
        _targetSettings = targetSettingsStore.Load(out string? targetError);
        _translationCatalog = translationCatalog ?? new ArgosTranslationModelCatalog();
        _translationWorkflow = translationWorkflow ?? new ScreenTranslationWorkflow(
            _ocrEngine, _translationCatalog, captureService, translationEngine);
        _interfaceSettingsStore = interfaceSettingsStore ?? InterfaceSettingsStore.CreateDefault();
        _interfaceSettings = _interfaceSettingsStore.Load(out string? interfaceError);
        _globalShortcut = globalShortcut ?? new GlobalShortcut();
        _lifetime.Own(_globalShortcut);
        _systemThemeSource = systemThemeSource ?? new WindowsSystemThemeSource();
        _lifetime.Own(_systemThemeSource);
        InitializeComponent();
        BuildInterface();
        InitializeInterfaceBehavior(interfaceError);
        InitializeTranslationMonitoring();
        _settingsError.Text = error ?? "";
        _targetSettingsError.Text = targetError ?? "";
        Shown += async (_, _) => { _mainWindowShown = true; RegisterShortcut(); await RefreshSourceLanguagesAsync(); };
        Activated += async (_, _) => { ApplyTheme(); if (_mainWindowShown) await RefreshSourceLanguagesAsync(); };
    }

    public async Task RefreshSourceLanguagesAsync()
    {
        if (_lifetime.IsStopped || IsDisposed || Disposing) return;
        int version = ++_refreshVersion;
        if (_sourceSettingsNeedRecovery)
        {
            var recovered = _settingsStore.Load(out string? error, requireExisting: true);
            _settingsError.Text = error ?? "";
            if (error is null)
            {
                _sourceSettings = recovered;
                _sourceSettingsNeedRecovery = false;
            }
        }
        _checkingSourceLanguages = true;
        _sourceLanguage.Enabled = false;
        ResetOcrValidation();
        UpdateTranslationModelStatus();
        string directory = OcrDataDirectory;
        _dataFolder.Text = directory;
        _dataFolder.AccessibleDescription = directory;
        _sourceStatus.Text = "Checking installed OCR languages…";
        _ocrModelStatus.Text = "●  Checking…";
        OcrLanguageScan scan;
        try { scan = await Task.Run(() => _languageCatalog.Scan(directory, WorkCancellationToken), WorkCancellationToken).WaitAsync(WorkCancellationToken); }
        catch (OperationCanceledException) when (WorkCancellationToken.IsCancellationRequested) { return; }
        if (_lifetime.IsStopped || IsDisposed || Disposing || version != _refreshVersion) return;
        _checkingSourceLanguages = false;
        _sourceScanError = scan.Error;

        string? previousCode = _sourceSettings.SourceLanguageCode;
        OcrLanguage? selected = _sourceSettingsNeedRecovery ? null : OcrLanguageCatalog.ResolveSelection(scan.Languages, previousCode);
        _bindingLanguages = true;
        try
        {
            _sourceLanguage.Items.Clear();
            _sourceLanguage.Items.AddRange(scan.Languages.Cast<object>().ToArray());
            _sourceLanguage.SelectedItem = selected;
        }
        finally { _bindingLanguages = false; }
        _sourceLanguage.Enabled = scan.Languages.Count > 0;
        ResetOcrValidation();
        UpdateSourceStatus();
        // Discovery never replaces or erases an existing preference, even if a file is locked or missing.
        if (!_sourceSettingsNeedRecovery && scan.Error is null && previousCode is null && selected is not null)
        {
            _sourceSettings = _sourceSettings with { SourceLanguageCode = selected?.Code };
            SaveSourceSettings();
        }
        await RefreshTranslationModelsAsync();
    }

    private async void SourceLanguageChanged(object? sender, EventArgs e)
    {
        if (_bindingLanguages || !_sourceLanguage.Enabled || _sourceLanguage.SelectedItem is not OcrLanguage) return;
        _sourceSettings = _sourceSettings with { SourceLanguageCode = SelectedSourceLanguageCode };
        SaveSourceSettings(explicitChoice: true);
        ResetOcrValidation();
        UpdateSourceStatus();
        await RefreshTranslationModelsAsync();
    }

    private void SaveSourceSettings(bool explicitChoice = false)
    {
        // Only a user choice may replace settings that could not be loaded.
        if (explicitChoice) _sourceSettingsNeedRecovery = false;
        if (_sourceSettingsNeedRecovery) return;
        _settingsError.Text = _settingsStore.Save(_sourceSettings) ?? "";
    }

    private void ThemeButton_Click(object? sender, EventArgs e)
    {
        if (sender is not PillButton selectedButton)
        {
            return;
        }

        _interfaceSettings = _interfaceSettings with { Theme = Enum.Parse<AppTheme>(selectedButton.Text) };
        SaveInterfaceSettings();
        ApplyTheme();
    }
}
