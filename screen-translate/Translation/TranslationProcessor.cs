using screen_translate.Ocr;

namespace screen_translate.Translation;

public interface ITranslationEngine
{
    Task<string> TranslateAsync(string text, TranslationModel model, CancellationToken cancellationToken);
}

public sealed record TranslationResult(OcrResult Original, string OutputText, string TargetLanguageCode, bool TranslationSkipped);

/// <summary>The shared result path for OCR-only and translated output. Never stores recognized text.</summary>
public sealed class TranslationProcessor(ITranslationModelCatalog catalog, ITranslationEngine? engine = null)
{
    public async Task<TranslationResult> ProcessAsync(OcrResult recognized, string targetCode, string modelDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? source = TranslationLanguage.FromOcrCode(recognized.SourceLanguageCode);
        if (source is null)
            throw new InvalidOperationException("The OCR source has no known translation language code.");
        var target = TranslationLanguage.All.FirstOrDefault(language => string.Equals(language.Code, targetCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Choose a documented translation output language.");
        if (string.IsNullOrWhiteSpace(recognized.Text))
            throw new InvalidOperationException("No text was detected in the selected region.");

        // Do this before touching either the catalog or engine, even if the folder is inaccessible.
        if (string.Equals(source, target.Code, StringComparison.OrdinalIgnoreCase))
            return new(recognized, recognized.Text, target.Code, TranslationSkipped: true);

        var scan = await Task.Run(() => catalog.Scan(modelDirectory, cancellationToken), cancellationToken)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var availability = TranslationModelAvailability.Evaluate(recognized.SourceLanguageCode, target.Code, scan);
        if (availability.Model is not TranslationModel model)
            throw new InvalidOperationException(scan.Error ?? $"No offline translation model installed for {source} → {target.Code}.");
        if (engine is null)
            throw new InvalidOperationException("The offline translation engine is not available in this build yet.");
        cancellationToken.ThrowIfCancellationRequested();
        string output = "";
        // Keep the caller's operation active until the engine actually stops, even if it ignores cancellation.
        await Models.ModelUse.RunAsync(modelDirectory, async () =>
        {
            output = await engine.TranslateAsync(recognized.Text, model, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("The translation engine returned no text.");
        return new(recognized, output, target.Code, TranslationSkipped: false);
    }
}
