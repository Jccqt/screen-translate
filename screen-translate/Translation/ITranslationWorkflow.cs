namespace screen_translate.Translation;

public enum TranslationStage { Selecting, Recognizing, Translating }

public sealed record TranslationRequest(string SourceLanguageCode, string TargetLanguageCode,
    string OcrDataDirectory, string TranslationModelDirectory);

/// <summary>
/// Runtime integration for one on-demand selection, OCR and translation operation.
/// Implementations must validate engine compatibility, show selection feedback, honor cancellation,
/// dispose capture resources, and return null when selection is cancelled. No text or images are stored.
/// </summary>
public interface ITranslationWorkflow
{
    string? GetUnavailableReason(TranslationRequest request);
    Task<TranslationResult?> RunAsync(Form owner, TranslationRequest request,
        IProgress<TranslationStage> progress, CancellationToken cancellationToken);
}
