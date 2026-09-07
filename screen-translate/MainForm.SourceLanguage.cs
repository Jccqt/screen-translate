using screen_translate.Ocr;

namespace screen_translate;

public partial class MainForm
{
    private readonly IOcrEngine _ocrEngine;
    private Button _validateOcr = null!;
    private string? _ocrValidationMessage;
    private string? _ocrValidationError;
    private bool _validatingOcr;
    private bool _ocrValidated;
    private int _ocrValidationVersion;
    // Retain the last failure for each model in this session, including across selection changes.
    // Discovery cannot prove that replacement data has repaired an engine-load failure.
    private readonly Dictionary<string, string> _ocrLoadFailures = new(StringComparer.OrdinalIgnoreCase);

    private string OcrModelKey(string code) => Path.GetFullPath(Path.Combine(OcrDataDirectory, code + ".traineddata"));

    private string? SourceSelectionIssue => _sourceSettingsNeedRecovery
        ? "Saved source settings could not be read. Refresh to retry, or explicitly choose a source language and OCR folder to replace them."
        : SelectedSourceLanguageCode is not null ? null :
        _sourceSettings.SourceLanguageCode is string saved
            ? $"Saved OCR language '{saved}' is unavailable. " + (_sourceLanguage.Items.Count > 0
                ? "Choose a source language in Read from."
                : "No OCR languages found. Choose an OCR folder with .traineddata files in Offline models, then refresh.")
            : "No OCR languages found. Choose an OCR folder with .traineddata files in Offline models, then refresh.";

    private void ResetOcrValidation()
    {
        ++_ocrValidationVersion;
        _validatingOcr = false;
        _ocrValidated = false;
        _ocrValidationError = SelectedSourceLanguageCode is string code
            ? _ocrLoadFailures.GetValueOrDefault(OcrModelKey(code)) : null;
        _ocrValidationMessage = _ocrValidationError;
        _validateOcr.Enabled = SelectedSourceLanguageCode is not null;
    }

    private void UpdateSourceStatus()
    {
        _ocrModelStatus.Text = _checkingSourceLanguages || _validatingOcr ? "●  Checking…"
            : _sourceScanError is not null ? "●  Cannot check"
            : SelectedSourceLanguageCode is null ? (_sourceLanguage.Items.Count > 0 ? "●  Select source" : "●  Not installed")
            : _ocrValidationError is not null ? "●  Load failed"
            : _ocrValidated ? "●  Validated" : "●  Discovered";
        _ocrModelStatus.ForeColor = _ocrModelStatus.Text is "●  Discovered" or "●  Validated" ? ModelGoodColor : ModelWarningColor;
        _sourceStatus.Text = _sourceScanError ?? _ocrValidationMessage ?? SourceSelectionIssue ??
            $"{_sourceLanguage.Items.Count} installed OCR language(s). Use Validate OCR data to check the selected language with Tesseract.";
        _sourceStatus.AccessibleDescription = _sourceStatus.Text;
        _sourceLanguage.AccessibleDescription = _sourceStatus.Text;
        UpdateTranslationModelStatus();
    }

    public async Task ValidateSelectedOcrLanguageAsync()
    {
        if (_lifetime.IsStopped || SelectedSourceLanguageCode is not string code || _validatingOcr) return;
        int version = ++_ocrValidationVersion;
        string directory = OcrDataDirectory;
        string modelKey = OcrModelKey(code);
        _validatingOcr = true;
        _validateOcr.Enabled = false;
        _ocrValidated = false;
        _ocrValidationMessage = $"Loading OCR data for '{code}' with Tesseract…";
        UpdateSourceStatus();
        string? error = null;
        try { await Models.ModelUse.RunAsync(directory, () => _ocrEngine.ValidateLanguageAsync(directory, code, WorkCancellationToken)).WaitAsync(WorkCancellationToken); }
        catch (OperationCanceledException) when (WorkCancellationToken.IsCancellationRequested) { return; }
        catch (OcrModelLoadException exception) { error = exception.Message; }
        catch (IOException exception) { error = exception.Message; }
        if (_lifetime.IsStopped || version != _ocrValidationVersion) return;
        _validatingOcr = false;
        _validateOcr.Enabled = true;
        if (error is null) _ocrLoadFailures.Remove(modelKey);
        else _ocrLoadFailures[modelKey] = error;
        _ocrValidationError = error;
        _ocrValidated = error is null;
        _ocrValidationMessage = error ?? $"Tesseract successfully loaded OCR data for '{code}'.";
        UpdateSourceStatus();
    }
}
