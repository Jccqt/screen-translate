using screen_translate;
using screen_translate.Ocr;
using screen_translate.Settings;

internal static partial class Program
{
    private static string? RealOcrData => Environment.GetEnvironmentVariable("SCREEN_TRANSLATE_TEST_TESSDATA");

    private static async Task ExpectOcrFailure(IOcrEngine engine, string directory, string code, string message)
    {
        try { await engine.ValidateLanguageAsync(directory, code); }
        catch (OcrModelLoadException error)
        {
            Check(error.Message.Contains(message, StringComparison.OrdinalIgnoreCase), $"Load rejects {code}: {message}");
            return;
        }
        throw new InvalidOperationException($"Expected model-load failure for {code}");
    }

    private static async Task TestOcrModelLoading()
    {
        var engine = new TesseractOcrEngine();
        string data = NewFolder("engine-data");
        foreach (string code in new[] { "osd", "equ", "eng+jpn", "~eng", "../eng", "" })
            await ExpectOcrFailure(engine, data, code, "single installed");
        await ExpectOcrFailure(engine, data, "eng", "missing or unreadable");
        File.WriteAllBytes(Path.Combine(data, "eng.traineddata"), []);
        await ExpectOcrFailure(engine, data, "eng", "empty");
        Install(data, "eng");
        using (var locked = new FileStream(Path.Combine(data, "eng.traineddata"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await ExpectOcrFailure(engine, data, "eng", "missing or unreadable");
        await ExpectOcrFailure(engine, data, "eng", "corrupt or incompatible");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await engine.ValidateLanguageAsync(data, "eng", cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "Cancelled model load does not initialize the native engine"); }

        if (RealOcrData is not string realData)
        {
            Console.WriteLine("SKIP: Genuine OCR model loading (set SCREEN_TRANSLATE_TEST_TESSDATA to a folder containing eng.traineddata).");
            return;
        }
        // Copy the supplied model to an isolated folder; all mutation tests act on this copy.
        File.Copy(Path.Combine(realData, "eng.traineddata"), Path.Combine(data, "MiXeD_custom.traineddata"));
        await engine.ValidateLanguageAsync(data, "MiXeD_custom");
        Check(true, "Native Tesseract loads real data using an exact mixed-case custom code");
        await ExpectOcrFailure(engine, data, "eng", "corrupt or incompatible");
        File.Copy(Path.Combine(realData, "eng.traineddata"), Path.Combine(data, "eng.traineddata"), true);
        await engine.ValidateLanguageAsync(data, "eng");
        Check(true, "Native Tesseract loads a genuine English model offline");
        string? previousPrefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        try
        {
            Environment.SetEnvironmentVariable("TESSDATA_PREFIX", NewFolder("wrong-tessdata-prefix"));
            await engine.ValidateLanguageAsync(data, "eng");
            Check(true, "Explicit selected folder takes precedence over an unrelated TESSDATA_PREFIX");
        }
        finally { Environment.SetEnvironmentVariable("TESSDATA_PREFIX", previousPrefix); }
        // A discovered model can change between discovery and engine initialization.
        Check(Catalog.Scan(data).Languages.Any(x => x.Code == "eng"), "Genuine model is discoverable");
        File.WriteAllBytes(Path.Combine(data, "eng.traineddata"), [0, 0, 0, 0]);
        await ExpectOcrFailure(engine, data, "eng", "corrupt or incompatible");
        File.Delete(Path.Combine(data, "eng.traineddata"));
        await ExpectOcrFailure(engine, data, "eng", "missing or unreadable");
        using var released = new FileStream(Path.Combine(data, "MiXeD_custom.traineddata"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check(true, "Successful and failed validation release model file handles");
    }

    private static async Task TestSourceLanguageUi(MainForm form, SourceLanguageSettingsStore store,
        TargetLanguageSettingsStore targetStore, string data, string artifacts)
    {
        await TestSourceSettingsRecoveryUi(targetStore, artifacts);
        using (var locked = new FileStream(Path.Combine(data, "eng.traineddata"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await form.RefreshSourceLanguagesAsync();
            Check(form.SelectedSourceLanguageCode is null && store.Load(out _).SourceLanguageCode == "eng",
                "Temporary individual-file read failure cannot replace the saved language");
        }
        await form.RefreshSourceLanguagesAsync();
        Check(form.SelectedSourceLanguageCode == "eng", "Unlocking saved data restores selection");
        await form.ValidateSelectedOcrLanguageAsync();
        Check(Find<Label>(form, "LanguageHint").Text.Contains("corrupt or incompatible") &&
            Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed"), "Native validation failure is explained on both pages");
        await form.RefreshSourceLanguagesAsync();
        Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed") &&
            Find<Label>(form, "LanguageHint").Text.Contains("corrupt or incompatible"),
            "Refreshing unchanged corrupt data preserves its native load failure");
        typeof(Form).GetMethod("OnActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(form, [EventArgs.Empty]);
        await WaitForReadiness(form);
        Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed") &&
            form.Readiness.Reason.Contains("corrupt or incompatible"), "Activation refresh preserves the model failure and readiness blocker");
        var sourceCombo = Find<ComboBox>(form, "SourceLanguage");
        sourceCombo.SelectedItem = sourceCombo.Items.Cast<OcrLanguage>().Single(x => x.Code == "jpn");
        Check(!Find<Label>(form, "LanguageHint").Text.Contains("corrupt or incompatible"), "One model's failure does not follow another language");
        sourceCombo.SelectedItem = sourceCombo.Items.Cast<OcrLanguage>().Single(x => x.Code == "eng");
        Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed"), "Reselecting a failed model restores its last failure");
        using (var locked = new FileStream(Path.Combine(data, "eng.traineddata"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await form.RefreshSourceLanguagesAsync();
        await form.RefreshSourceLanguagesAsync();
        Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed"), "A temporary scan omission does not erase a known load failure");
        Capture(form, artifacts, "source-invalid-data");
        Size originalSize = form.Size;
        form.Size = new Size(900, 700);
        Find<Button>(form, "ThemeLight").PerformClick();
        Application.DoEvents();
        AssertLayout(form);
        Capture(form, artifacts, "source-invalid-light-minimum");
        var hint = Find<Label>(form, "LanguageHint");
        Check(TextRenderer.MeasureText(hint.Text, hint.Font, new Size(hint.Width, int.MaxValue), TextFormatFlags.WordBreak).Height <= hint.Height,
            "Source error wraps without clipping at minimum window width");
        Find<Button>(form, "NavigateModels").PerformClick();
        Capture(form, artifacts, "source-validation-models");
        AssertLayout(form);
        Find<Button>(form, "NavigateSettings").PerformClick();
        Find<Button>(form, "ThemeDark").PerformClick();
        ChangeDpi(form, 144);
        Application.DoEvents();
        Capture(form, artifacts, "source-invalid-dark-150-percent");
        Check(TextRenderer.MeasureText(hint.Text, hint.Font, new Size(hint.Width, int.MaxValue), TextFormatFlags.WordBreak).Height <= hint.Height,
            "Source error wraps without clipping at synthetic 150 percent DPI");
        ChangeDpi(form, 96);
        Application.DoEvents();
        form.Size = originalSize;
        await form.RefreshSourceLanguagesAsync();

        string recoverablePath = Path.Combine(NewFolder("source-folder-recovery"), "tessdata");
        File.WriteAllText(recoverablePath, "A file temporarily blocks the folder");
        var recoveryStore = new SourceLanguageSettingsStore(Path.Combine(Root, "source-recovery-settings.json"));
        recoveryStore.Save(new(recoverablePath, "jpn"));
        using (var recovery = CreateMainForm(recoveryStore, targetStore))
        {
            await recovery.RefreshSourceLanguagesAsync();
            Check(Find<Label>(recovery, "LanguageHint").Text.Contains("Cannot read") && recoveryStore.Load(out _).SourceLanguageCode == "jpn",
                "Folder read error is distinct from empty data and preserves preference");
            File.Delete(recoverablePath);
            Directory.CreateDirectory(recoverablePath);
            Install(recoverablePath, "jpn");
            await recovery.RefreshSourceLanguagesAsync();
            Check(recovery.SelectedSourceLanguageCode == "jpn", "Recovered folder restores saved preference");
        }

        var slow = new DelayedOcrEngine();
        using var controlled = new MainForm(store, targetStore, interfaceSettingsStore:
            new InterfaceSettingsStore(Path.Combine(Root, "ocr-delayed-interface.json")), globalShortcut: new FakeShortcut(), ocrEngine: slow);
        await controlled.RefreshSourceLanguagesAsync();
        Task pending = controlled.ValidateSelectedOcrLanguageAsync();
        Check(slow.Code == "eng" && slow.Directory == data && Find<ComboBox>(controlled, "SourceLanguage").Enabled,
            "Validation uses exact selected code and folder while leaving UI responsive");
        Find<ComboBox>(controlled, "SourceLanguage").SelectedItem = Find<ComboBox>(controlled, "SourceLanguage").Items.Cast<OcrLanguage>().Single(x => x.Code == "jpn");
        slow.Release.SetException(new OcrModelLoadException("Stale load failure"));
        await pending;
        Check(!Find<Label>(controlled, "LanguageHint").Text.Contains("Stale"), "Old validation result cannot overwrite a newer source selection");
        var slowClose = new DelayedOcrEngine();
        using var closing = new MainForm(store, targetStore, interfaceSettingsStore:
            new InterfaceSettingsStore(Path.Combine(Root, "ocr-close-interface.json")), globalShortcut: new FakeShortcut(), ocrEngine: slowClose);
        await closing.RefreshSourceLanguagesAsync();
        Task closingLoad = closing.ValidateSelectedOcrLanguageAsync();
        closing.Dispose();
        await closingLoad.WaitAsync(TimeSpan.FromSeconds(5));
        Check(slowClose.Token.IsCancellationRequested, "Closing during validation cancels pending UI work");
        slowClose.Release.SetResult();
        // Restore the shared fixture preference after independently exercising selection changes.
        store.Save(new(data, "eng"));

        if (RealOcrData is string realData)
        {
            File.Copy(Path.Combine(realData, "eng.traineddata"), Path.Combine(data, "eng.traineddata"), true);
            await form.RefreshSourceLanguagesAsync();
            Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed"), "Replacing failed data requires successful revalidation before clearing the failure");
            await form.ValidateSelectedOcrLanguageAsync();
            Check(Find<Label>(form, "OcrModelStatus").Text.Contains("Validated") &&
                Find<Label>(form, "LanguageHint").Text.Contains("successfully loaded"), "UI confirms genuine selected-model loading");
            Capture(form, artifacts, "source-validated");
            await form.RefreshSourceLanguagesAsync();
            Check(!Find<Label>(form, "OcrModelStatus").Text.Contains("Load failed") &&
                !Find<Label>(form, "LanguageHint").Text.Contains("corrupt or incompatible"), "Successful native revalidation clears the remembered failure on subsequent refresh");
        }
    }

    private static async Task TestSourceSettingsRecoveryUi(TargetLanguageSettingsStore targetStore, string artifacts)
    {
        string fallbackData = NewFolder("settings-recovery-default-data");
        string savedData = NewFolder("settings-recovery-saved-data");
        Install(fallbackData, "eng");
        Install(savedData, "jpn");
        string path = Path.Combine(Root, "settings-read-recovery.json");
        var store = new SourceLanguageSettingsStore(path, new(fallbackData, null));
        var original = new SourceLanguageSettings(savedData, "jpn");
        store.Save(original);
        string originalJson = File.ReadAllText(path);
        MainForm form;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            form = CreateMainForm(store, targetStore);
            await form.RefreshSourceLanguagesAsync();
            Check(form.SelectedSourceLanguageCode is null && Find<Label>(form, "SourceSettingsError").Text.Contains("could not be read"),
                "Unreadable saved settings never activate or autosave a fallback source");
            await form.RefreshSourceLanguagesAsync();
            Check(form.SelectedSourceLanguageCode is null && Find<Label>(form, "SourceSettingsError").Text.Contains("could not be read"),
                "Repeated settings read failures retain the warning and require a source choice");
        }
        using (form)
        {
            Check(File.ReadAllText(path) == originalJson, "Failed settings reads leave the original JSON intact");
            await form.RefreshSourceLanguagesAsync();
            Check(form.SelectedSourceLanguageCode == "jpn" && form.OcrDataDirectory == savedData && store.Load(out _) == original,
                "Refresh retries recovered settings and restores the original folder and language");
            Check(Find<Label>(form, "SourceSettingsError").Text.Length == 0,
                "Settings read warning clears only after successful recovery");
        }

        // A file that is missing during recovery must not be treated as a new installation.
        File.WriteAllText(path, "{broken");
        using (var missing = CreateMainForm(store, targetStore))
        {
            File.Delete(path);
            await missing.RefreshSourceLanguagesAsync();
            Check(!File.Exists(path) && missing.SelectedSourceLanguageCode is null &&
                Find<Label>(missing, "SourceSettingsError").Text.Contains("could not be read"),
                "A settings file missing during recovery is not overwritten with first-launch defaults");
            File.WriteAllText(path, originalJson);
            await missing.RefreshSourceLanguagesAsync();
            Check(missing.SelectedSourceLanguageCode == "jpn" && File.ReadAllText(path) == originalJson,
                "A restored settings file recovers its preference without rewriting it");
        }

        File.WriteAllText(path, "{broken");
        using (var replacement = CreateMainForm(store, targetStore))
        {
            await replacement.RefreshSourceLanguagesAsync();
            Check(File.ReadAllText(path) == "{broken", "Corrupt settings remain untouched during automatic refresh");
            var combo = Find<ComboBox>(replacement, "SourceLanguage");
            combo.SelectedItem = combo.Items.Cast<OcrLanguage>().Single(x => x.Code == "eng");
            await replacement.RefreshTranslationModelsAsync();
            Check(store.Load(out _) == new SourceLanguageSettings(fallbackData, "eng") &&
                Find<Label>(replacement, "SourceSettingsError").Text.Length == 0 &&
                !Find<Label>(replacement, "LanguageHint").Text.Contains("could not be read"),
                "An explicit source choice may replace unreadable settings and clears the recovery warning");
            await replacement.RefreshSourceLanguagesAsync();
            Check(replacement.SelectedSourceLanguageCode == "eng", "Explicit replacement remains stable after refresh");
        }
    }

    private sealed class DelayedOcrEngine : IOcrEngine
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Code;
        public string? Directory;
        public CancellationToken Token;
        public Task ValidateLanguageAsync(string directory, string code, CancellationToken cancellationToken = default)
        {
            Directory = directory; Code = code; Token = cancellationToken;
            return Release.Task;
        }
    }
}
