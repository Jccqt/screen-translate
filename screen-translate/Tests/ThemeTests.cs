using screen_translate;
using screen_translate.Interface;
using screen_translate.Models;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

internal static partial class Program
{
    private static void TestThemePreferences()
    {
        foreach (bool systemDark in new[] { false, true })
        {
            Check(!WindowsSystemThemeSource.Resolve(AppTheme.Light, systemDark), "Explicit Light ignores Windows preference");
            Check(WindowsSystemThemeSource.Resolve(AppTheme.Dark, systemDark), "Explicit Dark ignores Windows preference");
            Check(WindowsSystemThemeSource.Resolve(AppTheme.System, systemDark) == systemDark, "System resolves current Windows preference");
        }
        Check(WindowsSystemThemeSource.ReadDarkPreference(() => 0), "Windows application preference 0 resolves Dark");
        foreach (object? value in new object?[] { null, 1, 7, "0" })
            Check(!WindowsSystemThemeSource.ReadDarkPreference(() => value), "Absent or unsupported Windows value falls back to Light");
        foreach (var error in new Exception[] { new IOException(), new UnauthorizedAccessException(), new System.Security.SecurityException() })
            Check(!WindowsSystemThemeSource.ReadDarkPreference(() => throw error), "Unreadable Windows preference safely falls back to Light");
    }

    private static MainForm ThemeFixture(ISystemThemeSource source, InterfaceSettingsStore preferences)
    {
        var ocr = new SourceLanguageSettingsStore(Path.Combine(Root, "theme-source.json"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, "theme-target.json"));
        ocr.Save(new(NewFolder("theme-ocr"), null));
        target.Save(new(NewFolder("theme-models"), "es"));
        return new MainForm(ocr, target, interfaceSettingsStore: preferences, globalShortcut: new FakeShortcut(), systemThemeSource: source);
    }

    private static TranslationResultForm ThemeResult() => new(new TranslationResult(
        new OcrResult("Theme verification text · 日本語", "eng", []), "Texto de prueba", "es", false),
        _ => throw new System.Runtime.InteropServices.ExternalException("Clipboard fixture"));

    private static async Task TestThemeUi(string artifacts)
    {
        var source = new FakeSystemTheme { IsDark = true };
        var preferences = new InterfaceSettingsStore(Path.Combine(Root, "theme-preferences.json"));
        preferences.Save(new(AppTheme.System, Keys.Control | Keys.Alt | Keys.F10));
        using var main = ThemeFixture(source, preferences);
        main.Show();
        await main.RefreshSourceLanguagesAsync();
        Check(main.BackColor.R < 60, "System uses current Windows preference on launch");
        using var result = ThemeResult();
        main.ShowTranslationWindow(result);
        using var manager = new ModelManagerForm(ModelPurpose.Translation, NewFolder("theme-inventory"));
        manager.Show(main);
        await manager.RefreshModelsAsync();
        using var download = new ModelDownloadForm(ModelPurpose.Ocr);
        download.Show(manager);
        using var message = new ThemedMessageForm("Cannot download: fixture feedback. Retry or cancel.", "Download feedback");
        message.Show(download);
        using var generic = new Form();
        generic.Controls.Add(new Label { Text = "Future owned translation window" });
        main.ShowTranslationWindow(generic);
        Check(new Form[] { result, manager, download, message, generic }.All(f => f.BackColor.R < 60),
            "New result, manager, nested download, feedback and generic owned windows inherit Dark");

        foreach (bool dark in new[] { false, true })
        {
            await Task.Run(() => source.SetDark(dark));
            await Until(() => (main.BackColor.R < 60) == dark && (message.BackColor.R < 60) == dark,
                "Worker-thread system change reaches main and nested windows without focus change");
            Check(new Form[] { result, manager, download, message, generic }.All(f => (f.BackColor.R < 60) == dark),
                "All open owned windows update in place");
            Check(preferences.Load(out _).Theme == AppTheme.System, "System update retains System preference");
            Find<Button>(result, "CopyOutput").PerformClick();
            Check(Find<Label>(result, "CopyError").Text.StartsWith("Could not copy"), "Copy failure communicates status in words");
            Find<TextBox>(download, "DownloadHTTPSsource").Text = "invalid";
            Check(!Find<Button>(download, "StartModelDownload").Enabled && Find<TextBox>(download, "DownloadDisclosure").Text.Contains("HTTPS"),
                "Invalid download has readable instructions and a disabled start action");
            foreach (var form in new Form[] { main, result, manager, download, message })
            {
                CheckTextContrast(form);
                Capture(form, artifacts, $"theme-{form.Name}-{(dark ? "dark" : "light")}");
            }
            var selectedTheme = Find<PillButton>(main, "ThemeSystem");
            Check(Contrast(selectedTheme.BorderColor, selectedTheme.BackColor) >= 3,
                "Selected theme border meets 3:1 contrast");
            Find<Button>(main, "NavigateModels").PerformClick();
            CheckTextContrast(main);
            CaptureLowerSettings(main, artifacts, $"theme-model-status-{(dark ? "dark" : "light")}");
            Find<Button>(main, "NavigateSettings").PerformClick();
        }

        Find<Button>(main, "ThemeLight").PerformClick();
        await Task.Run(() => source.SetDark(true));
        await Task.Delay(60);
        Check(main.BackColor.R > 230 && download.BackColor.R > 230, "Explicit Light stays Light after Windows event");
        Find<Button>(main, "ThemeDark").PerformClick();
        await Task.Run(() => source.SetDark(false));
        await Task.Delay(60);
        Check(main.BackColor.R < 60 && download.BackColor.R < 60, "Explicit Dark stays Dark after Windows event");
        Check(preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.F10), "Changing theme preserves shortcut");
        using (var reopened = ThemeFixture(new FakeSystemTheme(), preferences))
            Check(reopened.BackColor.R < 60, "Saved Dark is restored by a new main window");
        Find<Button>(main, "ThemeSystem").PerformClick();
        Check(main.BackColor.R > 230, "Reselecting System immediately reads latest preference");

        // Exercise an actual nested modal message loop while the owner is disabled.
        using (var modal = new ModelDownloadForm(ModelPurpose.Ocr))
        {
            Exception? failure = null;
            modal.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Run(() => source.SetDark(true));
                    await Until(() => modal.BackColor.R < 60, "System changes propagate through nested modal loop");
                    CheckTextContrast(modal);
                }
                catch (Exception error) { failure = error; }
                finally { modal.Close(); }
            };
            modal.ShowDialog(manager);
            if (failure is not null) throw failure;
        }

        using (var confirm = new ThemedMessageForm("Remove fixture?", "Confirm model removal", confirm: true))
        {
            Check(((Button)confirm.AcceptButton!).DialogResult == DialogResult.Cancel && confirm.CancelButton == confirm.AcceptButton,
                "Themed destructive confirmation defaults to cancel and supports Escape");
            confirm.Shown += (_, _) => confirm.BeginInvoke(() => ((Button)confirm.CancelButton!).PerformClick());
            Check(confirm.ShowDialog(manager) == DialogResult.Cancel, "Confirmation can be dismissed without removal");
        }
        using (var confirm = new ThemedMessageForm("Remove fixture?", "Confirm model removal", confirm: true))
        {
            confirm.Shown += (_, _) => confirm.BeginInvoke(() => Find<Button>(confirm, "ConfirmMessage").PerformClick());
            Check(confirm.ShowDialog(manager) == DialogResult.Yes, "Explicit Remove model confirms the dialog (no model mutation)");
        }

        // Save failure must not roll back the visible theme or crash the event handler.
        string blockedPath = NewFolder("theme-blocked-settings");
        using (var blocked = ThemeFixture(new FakeSystemTheme(), new InterfaceSettingsStore(blockedPath)))
        {
            blocked.Show();
            Find<Button>(blocked, "ThemeDark").PerformClick();
            Check(blocked.BackColor.R < 60 && Find<Label>(blocked, "InterfaceSettingsError").Text.Contains("only to this session"),
                "Save failure keeps session theme and explains persistence failure in text");
            CheckTextContrast(blocked);
            Capture(blocked, artifacts, "theme-save-failure");
        }

        source.SetDark(false); // Queued before shutdown: callback must be harmless afterwards.
        main.Close();
        await Task.Run(() => source.SetDark(true));
        await Task.Delay(60);
        Check(source.DisposeCount == 1 && source.SubscriberCount == 0 && result.IsDisposed && download.IsDisposed,
            "Shutdown unsubscribes system events and disposes nested windows despite queued update");
    }

    private static void CheckTextContrast(Form form)
    {
        foreach (var control in Descendants(form).Where(c => c.Visible && c.Enabled && c.Text.Length > 0 && c is Label or TextBox or Button or CheckBox))
        {
            Control surface = control;
            while (surface.BackColor == Color.Transparent && surface.Parent is not null) surface = surface.Parent;
            double ratio = Contrast(control.ForeColor, surface.BackColor);
            Check(ratio >= 4.5, $"{form.Name}/{control.Name} text contrast {ratio:F2}:1 meets 4.5:1");
        }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel) { double s = channel / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private sealed class FakeSystemTheme : ISystemThemeSource
    {
        public bool IsDark { get; set; }
        public int DisposeCount;
        public event EventHandler? Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        public void SetDark(bool dark) { IsDark = dark; Changed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() => DisposeCount++;
    }

    private static void RunThemePreview()
    {
        using var main = ThemeFixture(new WindowsSystemThemeSource(), new InterfaceSettingsStore(Path.Combine(Root, "preview-interface.json")));
        main.KeyPreview = true;
        main.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.R) { main.ShowTranslationWindow(ThemeResult()); e.SuppressKeyPress = true; }
        };
        Application.Run(main);
    }
}
