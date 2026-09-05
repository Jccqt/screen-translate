using System.Text.Json;
using screen_translate.Translation;

namespace screen_translate.Settings;

public sealed record TargetLanguageSettings(string TranslationModelDirectory, string TargetLanguageCode)
{
    public static TargetLanguageSettings Default => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenTranslate", "translation-models"), "es");
}

/// <summary>Separate from existing OCR settings so upgrading preserves source preferences.</summary>
public sealed class TargetLanguageSettingsStore(string filePath)
{
    public static TargetLanguageSettingsStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTranslate", "target-language.json"));

    public TargetLanguageSettings Load(out string? error)
    {
        error = null;
        try
        {
            var settings = JsonSerializer.Deserialize<TargetLanguageSettings>(File.ReadAllText(filePath));
            if (settings is null || string.IsNullOrWhiteSpace(settings.TranslationModelDirectory) ||
                !Path.IsPathFullyQualified(settings.TranslationModelDirectory))
                throw new JsonException("Invalid translation model folder.");
            var language = TranslationLanguage.Resolve(settings.TargetLanguageCode);
            if (!string.Equals(language.Code, settings.TargetLanguageCode, StringComparison.OrdinalIgnoreCase))
                error = "Saved target language is unavailable. Selected Spanish; choose your output language again.";
            return settings with { TargetLanguageCode = language.Code };
        }
        catch (FileNotFoundException) { return TargetLanguageSettings.Default; }
        catch (DirectoryNotFoundException) { return TargetLanguageSettings.Default; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            error = "Target-language settings could not be read. Choose your output language and model folder again.";
            return TargetLanguageSettings.Default;
        }
    }

    public string? Save(TargetLanguageSettings settings)
    {
        string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, filePath, overwrite: true);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "Could not save target-language settings. Your choice applies only to this session.";
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
