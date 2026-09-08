using Microsoft.Win32;
using screen_translate.Settings;

namespace screen_translate.Interface;

public interface ISystemThemeSource : IDisposable
{
    bool IsDark { get; }
    event EventHandler? Changed;
}

/// <summary>Reads the application preference (not the taskbar preference). Never writes Windows settings.</summary>
public sealed class WindowsSystemThemeSource : ISystemThemeSource
{
    public event EventHandler? Changed;
    public WindowsSystemThemeSource() => SystemEvents.UserPreferenceChanged += PreferenceChanged;

    public bool IsDark => ReadDarkPreference(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme");
    });

    internal static bool ReadDarkPreference(Func<object?> read)
    {
        try { return read() is int value && value == 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return false; } // Missing/unreadable preference uses the Windows default, Light.
    }

    public static bool Resolve(AppTheme theme, bool systemDark) =>
        theme == AppTheme.Dark || (theme == AppTheme.System && systemDark);

    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => SystemEvents.UserPreferenceChanged -= PreferenceChanged;
}
