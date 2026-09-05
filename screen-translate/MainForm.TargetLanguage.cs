using System.ComponentModel;
using screen_translate.Settings;
using screen_translate.Translation;

namespace screen_translate;

public partial class MainForm
{
    private readonly TargetLanguageSettingsStore _targetSettingsStore;
    private readonly ITranslationModelCatalog _translationCatalog;
    private TargetLanguageSettings _targetSettings;
    private ComboBox _targetLanguage = null!;
    private Label _targetStatus = null!;
    private Label _translationModelStatus = null!;
    private Label _translationFolder = null!;
    private Label _targetSettingsError = null!;
    private TranslationModelScan _translationScan = new([]);
    private int _translationRefreshVersion;
    private bool _checkingTranslationModels;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedTargetLanguageCode => ((TranslationLanguage)_targetLanguage.SelectedItem!).Code;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TranslationModelDirectory => _targetSettings.TranslationModelDirectory;

    // A discovered package path for future engine loading, not a guarantee of engine compatibility.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TranslationModel? SelectedTranslationModel => _checkingTranslationModels ? null :
        TranslationModelAvailability.Evaluate(SelectedSourceLanguageCode, SelectedTargetLanguageCode, _translationScan).Model;

    private FlowLayoutPanel CreateTranslationModelControls(Control section)
    {
        _targetStatus = new Label { Name = "TargetLanguageStatus", ForeColor = Muted, Text = "Checking local translation models…" };
        _translationFolder = new Label { Name = "TranslationModelFolder", ForeColor = Muted, AutoEllipsis = true };
        _targetSettingsError = new Label { Name = "TargetSettingsError", ForeColor = Color.FromArgb(160, 65, 35) };
        var folder = new Button { Text = "Choose translation folder…", AutoSize = true, AccessibleName = "Choose translation model folder" };
        var refresh = new Button { Text = "Refresh models", AutoSize = true, Name = "RefreshTranslationModels" };
        folder.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder containing extracted Argos translation package directories.",
                UseDescriptionForTitle = true,
                SelectedPath = TranslationModelDirectory,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await SetTranslationModelDirectoryAsync(dialog.SelectedPath);
        };
        refresh.Click += async (_, _) => await RefreshTranslationModelsAsync();
        var actions = new FlowLayoutPanel { Name = "TranslationFolderActions", WrapContents = false };
        actions.Controls.AddRange([folder, refresh]);
        section.Controls.AddRange([_targetStatus, _translationFolder, actions, _targetSettingsError]);
        return actions;
    }

    public async Task SetTranslationModelDirectoryAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Choose an absolute translation model directory.", nameof(directory));
        _targetSettings = _targetSettings with { TranslationModelDirectory = directory };
        SaveTargetSettings();
        await RefreshTranslationModelsAsync();
    }

    private void SaveTargetSettings() => _targetSettingsError.Text = _targetSettingsStore.Save(_targetSettings) ?? "";

    public async Task RefreshTranslationModelsAsync()
    {
        if (IsDisposed || Disposing) return;
        int version = ++_translationRefreshVersion;
        string directory = TranslationModelDirectory;
        _translationFolder.Text = $"Translation models: {directory}";
        _translationFolder.AccessibleDescription = directory;
        _checkingTranslationModels = true;
        UpdateTranslationModelStatus();
        var scan = await Task.Run(() => _translationCatalog.Scan(directory));
        if (IsDisposed || Disposing || version != _translationRefreshVersion) return;
        _translationScan = scan;
        _checkingTranslationModels = false;
        UpdateTranslationModelStatus();
    }

    private void UpdateTranslationModelStatus()
    {
        if (_checkingTranslationModels)
        {
            _translationModelStatus.Text = "●  Checking…";
            _translationModelStatus.ForeColor = Muted;
            _targetStatus.Text = "Checking local translation models…";
            return;
        }
        var availability = TranslationModelAvailability.Evaluate(SelectedSourceLanguageCode, SelectedTargetLanguageCode, _translationScan);
        string pair = $"{TranslationLanguage.FromOcrCode(SelectedSourceLanguageCode)} → {SelectedTargetLanguageCode}";
        (string badge, string message) = availability.State switch
        {
            TranslationModelState.SourceRequired => ("Select source", "Select an installed OCR source language to check the required translation model. Your output language can be selected now."),
            TranslationModelState.UnsupportedSource => ("Unknown source", "This OCR model has no known translation language code. Choose a supported OCR source language."),
            TranslationModelState.NotRequired => ("Not required", "Source and output languages are the same. No translation model is required."),
            TranslationModelState.ReadError => ("Cannot check", _translationScan.Error!),
            TranslationModelState.Installed => ("Installed", $"Translation model installed for {pair} (local files found)."),
            _ => ("Not installed", $"No offline translation model installed for {pair}. Choose a folder containing an extracted Argos package for this direction, then refresh.")
        };
        if (_translationScan.IgnoredPackages > 0 && availability.State is TranslationModelState.Installed or TranslationModelState.Missing)
            message += $" Skipped {_translationScan.IgnoredPackages} incomplete or unreadable package(s).";
        _translationModelStatus.Text = $"●  {badge}";
        _translationModelStatus.ForeColor = availability.State is TranslationModelState.Installed or TranslationModelState.NotRequired
            ? Color.FromArgb(45, 112, 72) : Color.FromArgb(173, 104, 27);
        _targetStatus.Text = message;
        _targetStatus.AccessibleDescription = message;
    }
}
