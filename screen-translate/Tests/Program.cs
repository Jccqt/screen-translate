using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using screen_translate;
using screen_translate.Ocr;
using screen_translate.Settings;

internal static partial class Program
{
    private static int _passed;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ScreenTranslateTests-" + Guid.NewGuid().ToString("N"));
    private static readonly OcrLanguageCatalog Catalog = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--result-preview"))
        {
            RunResultPreview();
            return 0;
        }
        Directory.CreateDirectory(Root);
        try
        {
            if (args.Contains("--theme-preview"))
            {
                RunThemePreview();
                return 0;
            }
            if (args.Contains("--shortcut-preview"))
            {
                RunShortcutPreview();
                return 0;
            }
            if (args.Contains("--models-preview"))
            {
                Application.Run(new screen_translate.Interface.ModelManagerForm(screen_translate.Models.ModelPurpose.Ocr,
                    Directory.CreateDirectory(Path.Combine(Root, "tessdata")).FullName));
                return 0;
            }
            TestCatalog();
            TestSettings();
            TestOcrModelLoading().GetAwaiter().GetResult();
            TestTranslationCatalog();
            TestTargetSettings();
            TestTranslationResults().GetAwaiter().GetResult();
            TestOfflineModels().GetAwaiter().GetResult();
            TestOfflineModelRecovery().GetAwaiter().GetResult();
            TestInterfaceSettingsAndReadiness();
            TestThemePreferences();
            TestLifetime();
            TestNativeShortcut();
            TestShortcutValidation();
            TestTranslationRequestGate().GetAwaiter().GetResult();
            RunUiTests(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "Artifacts"));
            TestMainMessageLoopExit();
            Console.WriteLine($"PASS: {_passed} assertions, including WinForms integration and layout checks.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { Directory.Delete(Root, recursive: true); }
    }

    private static void TestCatalog()
    {
        Check(Catalog.Scan(Path.Combine(Root, "missing")).Languages.Count == 0, "Missing folder has no languages");
        string data = NewFolder("catalog");
        Check(Catalog.Scan(data).Languages.Count == 0, "Empty folder has no languages");
        // Discovery fixtures test file presence only; they are not usable OCR models.
        Install(data, "eng");
        Install(data, "jpn");
        Install(data, "chi_sim");
        Install(data, "custom_model");
        Install(data, "osd");
        Install(data, "equ");
        Install(data, "eng+jpn");
        Install(data, "~eng");
        File.WriteAllText(Path.Combine(data, "fra.traineddata"), "");
        File.WriteAllText(Path.Combine(data, "deu.traineddata.download"), "partial");
        File.WriteAllText(Path.Combine(data, "spa.TRAINEDDATA"), "installed discovery fixture");
        Directory.CreateDirectory(Path.Combine(data, "nested"));
        Install(Path.Combine(data, "nested"), "kor");
        var scan = Catalog.Scan(data);
        Check(scan.Error is null, "Readable directory has no scan error");
        Check(scan.Languages.Select(x => x.Code).Order().SequenceEqual(new[] { "chi_sim", "custom_model", "eng", "jpn", "spa" }),
            "Only installed language files appear; auxiliary, empty, partial and nested files excluded");
        Check(scan.Languages.Single(x => x.Code == "chi_sim").DisplayName == "Chinese (Simplified)", "Tesseract special language name");
        Check(scan.Languages.Single(x => x.Code == "eng").DisplayName == "English", "Readable language name");
        Check(scan.Languages.Single(x => x.Code == "custom_model").DisplayName == "custom_model", "Custom model remains selectable");
        Check(OcrLanguageCatalog.ResolveSelection(scan.Languages, "JPN")?.Code == "jpn", "Saved selection resolves case insensitively");
        Check(OcrLanguageCatalog.ResolveSelection(scan.Languages, "missing") is null, "Unavailable preference requires explicit replacement");
        Check(OcrLanguageCatalog.ResolveSelection([new("MiXeD_custom", "Custom")], "mixed_custom")?.Code == "MiXeD_custom", "Selection preserves exact discovered engine code");
        Check(OcrLanguageCatalog.ResolveSelection([], "eng") is null, "No selection without data");
        using (var locked = new FileStream(Path.Combine(data, "eng.traineddata"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(Catalog.Scan(data).Languages.All(x => x.Code != "eng"), "Unreadable model cannot be selected");
        File.Delete(Path.Combine(data, "jpn.traineddata"));
        Check(Catalog.Scan(data).Languages.All(x => x.Code != "jpn"), "Removed data disappears on refresh");
        Check(Catalog.Scan("\0").Error is not null, "Invalid directory reports scan error");
        Check(Catalog.Scan(Path.Combine(data, "eng.traineddata")).Error is not null, "File instead of directory reports scan error");
    }

    private static void TestSettings()
    {
        string path = Path.Combine(Root, "settings", "source-language.json");
        var store = new SourceLanguageSettingsStore(path);
        Check(store.Load(out var error) == SourceLanguageSettings.Default && error is null, "First launch uses default folder");
        var settings = new SourceLanguageSettings(NewFolder("saved-data"), "jpn");
        Check(store.Save(settings) is null && store.Load(out error) == settings && error is null, "Directory and exact OCR code round trip");
        Check(store.Save(settings with { SourceLanguageCode = "eng" }) is null && store.Load(out _).SourceLanguageCode == "eng", "Existing settings replaced");
        File.WriteAllText(path, "{broken");
        Check(store.Load(out error) == SourceLanguageSettings.Default && error is not null, "Corrupt settings recover with message");
        File.WriteAllText(path, "{\"OcrDataDirectory\":\"relative\",\"SourceLanguageCode\":\"eng\"}");
        Check(store.Load(out error) == SourceLanguageSettings.Default && error is not null, "Relative saved directory is rejected");
        var blocked = new SourceLanguageSettingsStore(Path.Combine(path, "child.json"));
        Check(blocked.Save(settings) is not null, "Write failure returns user-facing error");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Atomic save leaves no temporary files");
    }

    private static void RunUiTests(string artifactDirectory)
    {
        Directory.CreateDirectory(artifactDirectory);
        string data = NewFolder("ui-data");
        var store = new SourceLanguageSettingsStore(Path.Combine(Root, "ui-settings.json"));
        store.Save(new SourceLanguageSettings(data, null));
        var targetStore = new TargetLanguageSettingsStore(Path.Combine(Root, "ui-target-settings.json"));
        targetStore.Save(new TargetLanguageSettings(NewFolder("ui-translation-models"), "es"));
        using var form = CreateMainForm(store, targetStore);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.RefreshSourceLanguagesAsync();
                Check(form.Visible && form.WindowState != FormWindowState.Minimized, "Launch displays the main interface");
                var combo = Find<ComboBox>(form, "SourceLanguage");
                Check(!combo.Enabled && combo.Items.Count == 0 && form.SelectedSourceLanguageCode is null, "Empty source selector is disabled and unselected");
                Check(Find<Label>(form, "SourceLanguageStatus").Text.Contains("No OCR languages"), "Empty state explains how to install data");
                Capture(form, artifactDirectory, "empty");
                TestRedesignedLayout(form);
                Size launchSize = form.Size;
                form.Height = Math.Min(form.Height, 788 * form.DeviceDpi / 96);
                Application.DoEvents();
                TestRedesignedLayout(form);
                Capture(form, artifactDirectory, "desktop-constrained-launch");
                form.Size = launchSize;
                await TestMainInterfaceUi(form, artifactDirectory);
                await TestThemeUi(artifactDirectory);
                await TestGlobalShortcutUi(artifactDirectory);
                await TestOfflineModelsUi(artifactDirectory);
                await TestOfflineModelRecoveryUi(artifactDirectory);
                Install(data, "eng");
                Install(data, "jpn");
                Install(data, "chi_sim");
                await form.RefreshSourceLanguagesAsync();
                Check(combo.Enabled && combo.Items.Count == 3, "Refresh adds installed languages to the real dropdown");
                Check(combo.DropDownStyle == ComboBoxStyle.DropDownList, "User cannot type an uninstalled language");
                Check(combo.Items.Cast<OcrLanguage>().All(x => !x.Code.Equals("auto", StringComparison.OrdinalIgnoreCase)), "No automatic detection option");
                combo.SelectedItem = combo.Items.Cast<OcrLanguage>().Single(x => x.Code == "jpn");
                Check(form.SelectedSourceLanguageCode == "jpn" && form.OcrDataDirectory == data, "Selected exact code and directory are available to the OCR pipeline");
                Check(store.Load(out _).SourceLanguageCode == "jpn", "Dropdown change persists immediately");
                await form.RefreshSourceLanguagesAsync();
                Check(form.SelectedSourceLanguageCode == "jpn", "Refresh preserves current installed selection");
                using (var reopened = CreateMainForm(store, targetStore))
                {
                    await reopened.RefreshSourceLanguagesAsync();
                    Check(reopened.SelectedSourceLanguageCode == "jpn", "New form restores saved source language");
                }
                Capture(form, artifactDirectory, "installed-default");
                await TestTargetLanguageUi(form, store, targetStore, artifactDirectory);
                await TestTargetLanguageRevisionUi(artifactDirectory);
                foreach (Size size in new[] { new Size(900, 650), new Size(1280, 850) })
                {
                    form.Size = size;
                    form.PerformLayout();
                    Application.DoEvents();
                    AssertLayout(form);
                    Capture(form, artifactDirectory, $"installed-{size.Width}");
                    CaptureLowerSettings(form, artifactDirectory, $"main-settings-{size.Width}");
                }
                // Deliver the DPI-change notification used by Windows when moving between monitors.
                float titleFont = Find<Label>(form, "PageTitle").Font.Size;
                int pickerHeight = Find<ComboBox>(form, "TargetLanguage").ItemHeight;
                ChangeDpi(form, 144);
                form.PerformLayout();
                Application.DoEvents();
                AssertLayout(form);
                Check(Math.Abs(Find<Label>(form, "PageTitle").Font.Size / titleFont - 1.5F) < .01F &&
                    Find<ComboBox>(form, "TargetLanguage").ItemHeight == pickerHeight * 1.5F,
                    "Typography and dropdown hit areas scale with the 150% layout");
                Capture(form, artifactDirectory, "scaled-150-percent");
                CaptureLowerSettings(form, artifactDirectory, "main-settings-150-percent");
                ChangeDpi(form, 96);
                Application.DoEvents();
                Check(Math.Abs(Find<Label>(form, "PageTitle").Font.Size - titleFont) < .01F,
                    "Returning to 100% restores typography without cumulative scaling");
                File.Delete(Path.Combine(data, "jpn.traineddata"));
                await form.RefreshSourceLanguagesAsync();
                Check(form.SelectedSourceLanguageCode is null && combo.Enabled && combo.Items.Count == 2, "Removed selected language requires explicit replacement");
                Check(Find<Label>(form, "SourceLanguageStatus").Text.Contains("unavailable"), "Unavailable preference is explained");
                Check(Find<Label>(form, "LanguageHint").Visible && Find<Label>(form, "LanguageHint").Text.Contains("'jpn' is unavailable"), "Unavailable preference is visible beside the source selector");
                Capture(form, artifactDirectory, "source-unavailable");
                await form.RefreshSourceLanguagesAsync();
                Check(form.SelectedSourceLanguageCode is null && store.Load(out _).SourceLanguageCode == "jpn", "Repeated refresh does not acknowledge or replace an unavailable preference");
                using (var reopened = CreateMainForm(store, targetStore))
                {
                    await reopened.RefreshSourceLanguagesAsync();
                    Check(reopened.SelectedSourceLanguageCode is null && Find<Label>(reopened, "LanguageHint").Text.Contains("'jpn' is unavailable"), "Unavailable preference survives restart with its warning");
                }
                File.Delete(Path.Combine(data, "eng.traineddata"));
                File.Delete(Path.Combine(data, "chi_sim.traineddata"));
                await form.RefreshSourceLanguagesAsync();
                Check(!combo.Enabled && form.SelectedSourceLanguageCode is null && store.Load(out _).SourceLanguageCode == "jpn",
                    "Removing all data disables selection without erasing the saved preference");
                Install(data, "eng");
                await form.RefreshSourceLanguagesAsync();
                Check(combo.Enabled && combo.Items.Count == 1 && form.SelectedSourceLanguageCode is null,
                    "A different available language requires a new selection after the empty state");
                Install(data, "jpn");
                await form.RefreshSourceLanguagesAsync();
                Check(form.SelectedSourceLanguageCode == "jpn", "Returning saved data restores the exact preference");
                combo.SelectedItem = combo.Items.Cast<OcrLanguage>().Single(x => x.Code == "eng");
                Check(store.Load(out _).SourceLanguageCode == "eng" && !Find<Label>(form, "LanguageHint").Text.Contains("unavailable"), "Explicit replacement saves and clears the warning");
                await TestSourceLanguageUi(form, store, targetStore, data, artifactDirectory);
                var pending = form.RefreshSourceLanguagesAsync();
                Check(form.SelectedSourceLanguageCode is null, "No OCR source is exposed while a rescan is pending");
                await pending;
                var invalidStore = new SourceLanguageSettingsStore(Path.Combine(Root, "invalid-ui.json"));
                invalidStore.Save(new SourceLanguageSettings(Path.Combine(data, "eng.traineddata"), "eng"));
                using (var invalidForm = CreateMainForm(invalidStore, targetStore))
                {
                    await invalidForm.RefreshSourceLanguagesAsync();
                    Check(!Find<ComboBox>(invalidForm, "SourceLanguage").Enabled && invalidForm.SelectedSourceLanguageCode is null,
                        "Folder read error disables source selection");
                    Check(Find<Label>(invalidForm, "SourceLanguageStatus").Text.Contains("Cannot read"), "Folder read error is explained in the UI");
                    Check(invalidStore.Load(out _).SourceLanguageCode == "eng", "Temporary read failure preserves the saved preference");
                }
            }
            catch (Exception error) { failure = error; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("WinForms integration failed", failure);
    }

    private static void AssertLayout(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is ScrollableControl scrollable && scrollable.AutoScroll)
            {
                if (scrollable.HorizontalScroll.Visible)
                    Console.WriteLine($"Scroll bounds: client={scrollable.ClientSize}, display={scrollable.DisplayRectangle}; " +
                        string.Join("; ", scrollable.Controls.Cast<Control>().Select(c => $"{c.GetType().Name} {c.Bounds} preferred={c.PreferredSize}")));
                Check(!scrollable.HorizontalScroll.Visible, "Settings page needs no horizontal scrolling");
            }
            // Page scrolling is intentional; its cards must still fit horizontally.
            if (child.Name is "LanguageInputs" or "OcrFolderActions" or "TranslationFolderActions" or "TargetLanguageStatus" or "TargetSettingsError" or "GlobalShortcut" or "ApplyShortcut" or "ShortcutStatus" or "TranslationReadiness" || child is ComboBox || parent.Name is "OcrFolderActions" or "TranslationFolderActions")
                Check(child.Left >= 0 && child.Right <= parent.ClientSize.Width && child.Bottom <= parent.ClientSize.Height,
                    $"{child.Name} fits at {parent.ClientSize}");
            AssertLayout(child);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, ref NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static void ChangeDpi(Form form, int dpi)
    {
        var bounds = form.Bounds;
        float scale = (float)dpi / form.DeviceDpi;
        var rectangle = new NativeRect { Left = bounds.Left, Top = bounds.Top,
            Right = bounds.Left + (int)(bounds.Width * scale), Bottom = bounds.Top + (int)(bounds.Height * scale) };
        SendMessage(form.Handle, 0x02E0, (nuint)(dpi | (dpi << 16)), ref rectangle);
        Check(form.DeviceDpi == dpi, $"Form received the {dpi} DPI change");
    }

    private static void Capture(Form form, string directory, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
    }

    private static T Find<T>(Control control, string name) where T : Control => (T)control.Controls.Find(name, true).Single();
    private static string NewFolder(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
    private static void Install(string folder, string code) => File.WriteAllText(Path.Combine(folder, code + ".traineddata"), "discovery fixture");
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("FAIL: " + message);
        _passed++;
        Console.WriteLine("PASS: " + message);
    }
}
