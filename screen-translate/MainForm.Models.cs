using screen_translate.Interface;
using screen_translate.Models;

namespace screen_translate;

public partial class MainForm
{
    private async Task OpenModelManagerAsync(ModelPurpose purpose)
    {
        using var manager = new ModelManagerForm(purpose,
            purpose == ModelPurpose.Ocr ? OcrDataDirectory : TranslationModelDirectory);
        manager.ApplyTheme(_darkTheme ? DarkSurface : Surface, _darkTheme ? DarkInk : Ink);
        manager.ShowDialog(this);
        await RefreshSourceLanguagesAsync();
    }
}
