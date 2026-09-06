namespace screen_translate.Ocr;

/// <summary>The model-loading boundary; recognition will extend this interface with the OCR pipeline.</summary>
public interface IOcrEngine
{
    Task ValidateLanguageAsync(string dataDirectory, string languageCode, CancellationToken cancellationToken = default);
}

public sealed class OcrModelLoadException(string message, Exception? inner = null) : Exception(message, inner);
