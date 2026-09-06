using System.Text.Json;

namespace screen_translate.Settings;

public sealed record SourceLanguageSettings(string OcrDataDirectory, string? SourceLanguageCode)
{
    public static SourceLanguageSettings Default => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenTranslate", "tessdata"), null);
}

/// <summary>Stores only configuration, never screenshots or recognized text.</summary>
public sealed class SourceLanguageSettingsStore(string filePath, SourceLanguageSettings? defaults = null)
{
    private SourceLanguageSettings Fallback => defaults ?? SourceLanguageSettings.Default;
    public static SourceLanguageSettingsStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTranslate", "source-language.json"));

    public SourceLanguageSettings Load(out string? error, bool requireExisting = false)
    {
        error = null;
        try
        {
            var settings = JsonSerializer.Deserialize<SourceLanguageSettings>(File.ReadAllText(filePath));
            if (settings is null || string.IsNullOrWhiteSpace(settings.OcrDataDirectory) ||
                !Path.IsPathFullyQualified(settings.OcrDataDirectory))
                throw new JsonException("Invalid OCR data folder.");
            return settings;
        }
        catch (FileNotFoundException) when (!requireExisting) { return Fallback; }
        catch (DirectoryNotFoundException) when (!requireExisting) { return Fallback; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            error = "Source-language settings could not be read. Refresh to retry, or choose your OCR folder and language again.";
            return Fallback;
        }
    }

    public string? Save(SourceLanguageSettings settings)
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
            return "Could not save source-language settings. Your choice applies only to this session.";
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
