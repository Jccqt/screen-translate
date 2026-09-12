using screen_translate.Capture;
using screen_translate.Ocr;

namespace screen_translate.Translation;

/// <summary>Production on-demand workflow. Captures and recognizes in memory, then immediately disposes pixels.</summary>
public sealed class ScreenTranslationWorkflow(
    IOcrEngine ocrEngine,
    ITranslationModelCatalog translationCatalog,
    IScreenRegionCaptureService? capture = null,
    ITranslationEngine? translationEngine = null) : ITranslationWorkflow
{
    private readonly IScreenRegionCaptureService _capture = capture ?? new ScreenRegionCaptureService();

    public string? GetUnavailableReason(TranslationRequest request)
    {
        if (ocrEngine is not IOcrRecognizer)
            return "Screen translation isn't available because the configured OCR engine cannot recognize captured images.";
        if (!Path.IsPathFullyQualified(request.OcrDataDirectory))
            return "Choose an absolute OCR data folder before starting capture.";
        if (!OcrLanguageCatalog.IsSourceCode(request.SourceLanguageCode))
            return "Choose one installed OCR source language before starting capture.";
        string model = Path.Combine(request.OcrDataDirectory, request.SourceLanguageCode + ".traineddata");
        try
        {
            using var file = new FileStream(model, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length == 0) return $"OCR data for '{request.SourceLanguageCode}' is empty. Reinstall it before starting capture.";
        }
        catch (Exception) when (!File.Exists(model))
        { return $"OCR data for '{request.SourceLanguageCode}' is missing. Refresh languages before starting capture."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return $"OCR data for '{request.SourceLanguageCode}' is unreadable. Check the OCR folder before starting capture."; }

        string? source = TranslationLanguage.FromOcrCode(request.SourceLanguageCode);
        if (!string.Equals(source, request.TargetLanguageCode, StringComparison.OrdinalIgnoreCase) && translationEngine is null)
            return "Screen translation isn't available because the offline translation engine is not installed in this build.";
        return _capture.GetUnavailableReason();
    }

    public async Task<TranslationResult?> RunAsync(Form owner, TranslationRequest request,
        IProgress<TranslationStage> progress, CancellationToken cancellationToken)
    {
        if (GetUnavailableReason(request) is string unavailable)
            throw new InvalidOperationException(unavailable);
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(TranslationStage.Selecting);
        RegionCaptureOutcome selected = await _capture.SelectAsync(owner, cancellationToken);
        if (selected.Error is string captureError) throw new ScreenCaptureException(captureError);
        if (selected.Region is not CapturedRegion region) return null;
        using (region)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(TranslationStage.Recognizing);
            var recognized = await ((IOcrRecognizer)ocrEngine).RecognizeAsync(region.Image,
                request.OcrDataDirectory, request.SourceLanguageCode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(TranslationStage.Translating);
            return await new TranslationProcessor(translationCatalog, translationEngine).ProcessAsync(
                recognized, request.TargetLanguageCode, request.TranslationModelDirectory, cancellationToken);
        }
    }
}
