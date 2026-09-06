using Tesseract;

namespace screen_translate.Ocr;

/// <summary>Uses native Tesseract initialization to validate data, without capturing or storing screen content.</summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    public Task ValidateLanguageAsync(string dataDirectory, string languageCode, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OcrLanguageCatalog.IsSourceCode(languageCode))
                throw new OcrModelLoadException("Select a single installed OCR language before loading data.");

            try
            {
                if (!Path.IsPathFullyQualified(dataDirectory))
                    throw new OcrModelLoadException("Choose an absolute OCR data folder before loading data.");
                // Reopen the exact file at load time. Hold it against replacement/deletion during initialization.
                using var data = new FileStream(Path.Combine(dataDirectory, languageCode + ".traineddata"),
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                if (data.ReadByte() < 0)
                    throw new OcrModelLoadException($"OCR data for '{languageCode}' is empty. Reinstall it and refresh languages.");
                cancellationToken.ThrowIfCancellationRequested();
                using var engine = new TesseractEngine(dataDirectory, languageCode, EngineMode.Default);
                // Native initialization cannot be interrupted safely. The worker owns and disposes it even on cancellation.
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (TesseractException error)
            {
                throw new OcrModelLoadException($"Tesseract could not load OCR data for '{languageCode}'. The data may be corrupt or incompatible. Reinstall it and refresh languages.", error);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new OcrModelLoadException($"OCR data for '{languageCode}' is missing or unreadable. Check the folder and refresh languages.", error);
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                throw new OcrModelLoadException("The Tesseract runtime could not load. Repair the application and install the Microsoft Visual C++ x64 runtime.", error);
            }
        }, cancellationToken);
}
