namespace screen_translate.Ocr;

/// <summary>The replaceable OCR model-loading boundary used by setup validation.</summary>
public interface IOcrEngine
{
    Task ValidateLanguageAsync(string dataDirectory, string languageCode, CancellationToken cancellationToken = default);
}

/// <summary>Recognizes one in-memory capture. Implementations must not retain the image or text.</summary>
public interface IOcrRecognizer : IOcrEngine
{
    Task<OcrResult> RecognizeAsync(Bitmap image, string dataDirectory, string languageCode,
        CancellationToken cancellationToken = default);
}

public sealed class OcrModelLoadException(string message, Exception? inner = null) : Exception(message, inner);
