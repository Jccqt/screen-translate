using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Translation;

namespace screen_translate;

public partial class MainForm
{
    /// <summary>Capture/OCR integration point. The result carries the source used for recognition.</summary>
    public async Task ShowOcrResultAsync(OcrResult recognized, ITranslationEngine? engine = null)
    {
        if (_lifetime.IsStopped) return;
        // Snapshot the target before asynchronous work; later preference edits do not relabel this result.
        string target = SelectedTargetLanguageCode;
        var result = await new TranslationProcessor(_translationCatalog, engine).ProcessAsync(
            recognized, target, TranslationModelDirectory, WorkCancellationToken);
        if (_lifetime.IsStopped) return;
        var window = new TranslationResultForm(result);
        window.ApplyTheme(_darkTheme ? DarkSurface : Surface, _darkTheme ? DarkInk : Ink);
        ShowTranslationWindow(window);
    }
}
