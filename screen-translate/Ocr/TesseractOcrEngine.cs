using Tesseract;

namespace screen_translate.Ocr;

/// <summary>Uses native Tesseract initialization to validate data, without capturing or storing screen content.</summary>
public sealed class TesseractOcrEngine : IOcrRecognizer
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

    public async Task<OcrResult> RecognizeAsync(Bitmap image, string dataDirectory, string languageCode,
        CancellationToken cancellationToken = default)
    {
        OcrResult? result = null;
        await Models.ModelUse.RunAsync(dataDirectory, async () =>
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (image.Width <= 0 || image.Height <= 0)
                    throw new ArgumentException("The selected image is empty.", nameof(image));
                if (!OcrLanguageCatalog.IsSourceCode(languageCode))
                    throw new OcrModelLoadException("Select a single installed OCR language before recognizing text.");
                try
                {
                    using var data = new FileStream(Path.Combine(dataDirectory, languageCode + ".traineddata"),
                        FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (data.ReadByte() < 0)
                        throw new OcrModelLoadException($"OCR data for '{languageCode}' is empty. Reinstall it and refresh languages.");
                    using var engine = new TesseractEngine(dataDirectory, languageCode, EngineMode.Default);
                    using var encoded = new MemoryStream();
                    image.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                    using var pix = Pix.LoadFromMemory(encoded.ToArray());
                    using var page = engine.Process(pix);
                    string text = page.GetText()?.Trim() ?? "";
                    var regions = new List<OcrTextRegion>();
                    using var iterator = page.GetIterator();
                    iterator.Begin();
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string? word = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                        if (!string.IsNullOrWhiteSpace(word) &&
                            iterator.TryGetBoundingBox(PageIteratorLevel.Word, out Rect bounds))
                            regions.Add(new(word, new Rectangle(bounds.X1, bounds.Y1, bounds.Width, bounds.Height),
                                iterator.GetConfidence(PageIteratorLevel.Word)));
                    } while (iterator.Next(PageIteratorLevel.Word));
                    cancellationToken.ThrowIfCancellationRequested();
                    result = new(text, languageCode, regions);
                }
                catch (TesseractException error)
                {
                    throw new OcrModelLoadException($"Tesseract could not recognize the selected region with '{languageCode}'. The OCR data may be corrupt or incompatible. Reinstall it and try again.", error);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    throw new OcrModelLoadException($"OCR data for '{languageCode}' is missing or unreadable. Check the folder and try again.", error);
                }
                catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or TypeInitializationException)
                {
                    throw new OcrModelLoadException("The Tesseract runtime could not load. Repair the application and install the Microsoft Visual C++ x64 runtime.", error);
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result!;
    }
}
