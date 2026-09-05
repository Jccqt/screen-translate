using System.ComponentModel;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;
using screen_translate.Interface;

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
        ITranslationModelCatalog? translationCatalog = null, InterfaceSettingsStore? interfaceSettingsStore = null,
        IGlobalShortcut? globalShortcut = null)
    {
        _settingsStore = settingsStore;
        _sourceSettings = _settingsStore.Load(out string? error);
        _targetSettingsStore = targetSettingsStore;
        _targetSettings = targetSettingsStore.Load(out string? targetError);
        _translationCatalog = translationCatalog ?? new ArgosTranslationModelCatalog();
        _interfaceSettingsStore = interfaceSettingsStore ?? InterfaceSettingsStore.CreateDefault();
        _interfaceSettings = _interfaceSettingsStore.Load(out string? interfaceError);
        _globalShortcut = globalShortcut ?? new GlobalShortcut();
        _lifetime.Own(_globalShortcut);
        InitializeComponent();
        BuildInterface();
        InitializeInterfaceBehavior(interfaceError);
        _settingsError.Text = error ?? "";
        _targetSettingsError.Text = targetError ?? "";
        Shown += async (_, _) => { _mainWindowShown = true; RegisterShortcut(); await RefreshSourceLanguagesAsync(); };
        Activated += async (_, _) => { ApplyTheme(); if (_mainWindowShown) await RefreshSourceLanguagesAsync(); };
    }

    public async Task RefreshSourceLanguagesAsync()
    {
        if (_lifetime.IsStopped || IsDisposed || Disposing) return;
        int version = ++_refreshVersion;
        _checkingSourceLanguages = true;
        _sourceLanguage.Enabled = false;
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
        _ocrModelStatus.Text = scan.Error is not null ? "●  Cannot check" : selected is null ? "●  Not installed" : "●  Installed";
        _ocrModelStatus.ForeColor = selected is null ? ModelWarningColor : ModelGoodColor;
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
