using System.Text.Json;

namespace screen_translate.Settings;

public enum AppTheme { System, Light, Dark }
public sealed record InterfaceSettings(AppTheme Theme, Keys Shortcut)
{
    public static InterfaceSettings Default => new(AppTheme.System, Keys.Control | Keys.Shift | Keys.T);

    public static bool IsValidShortcut(Keys shortcut)
    {
        var key = shortcut & Keys.KeyCode;
        var modifiers = shortcut & Keys.Modifiers;
        return (modifiers & (Keys.Control | Keys.Alt)) != 0 &&
            (modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) == 0 &&
            (key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.F1 and <= Keys.F24);
    }

    public static string FormatShortcut(Keys keys) => new KeysConverter().ConvertToString(keys) ?? "";
}

/// <summary>Separate preferences preserve the existing language/model settings files.</summary>
public sealed class InterfaceSettingsStore(string filePath)
{
    public static InterfaceSettingsStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenTranslate", "interface.json"));

    public InterfaceSettings Load(out string? error)
    {
        error = null;
        try
        {
            var settings = JsonSerializer.Deserialize<InterfaceSettings>(File.ReadAllText(filePath))
                ?? throw new JsonException();
            bool invalidTheme = !Enum.IsDefined(settings.Theme);
            bool invalidShortcut = !InterfaceSettings.IsValidShortcut(settings.Shortcut);
            if (invalidTheme || invalidShortcut)
                error = "Some interface settings were invalid. Only invalid preferences were reset.";
            return settings with
            {
                Theme = invalidTheme ? InterfaceSettings.Default.Theme : settings.Theme,
                Shortcut = invalidShortcut ? InterfaceSettings.Default.Shortcut : settings.Shortcut
            };
        }
        catch (FileNotFoundException) { return InterfaceSettings.Default; }
        catch (DirectoryNotFoundException) { return InterfaceSettings.Default; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            error = "Interface settings could not be read. Check appearance and shortcut settings.";
            return InterfaceSettings.Default;
        }
    }

    public string? Save(InterfaceSettings settings)
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
            return "Could not save interface settings. Your changes apply only to this session.";
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
