using System.Reflection;
using screen_translate;
using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

internal static partial class Program
{
    private static void RunResultPreview()
    {
        var recognized = new OcrResult("Screen Translate verification\r\nSame-language text remains unchanged.", "eng", []);
        using var form = new TranslationResultForm(new(recognized, recognized.Text, "en", true), text =>
        {
            Clipboard.SetText(text);
            Check(Clipboard.GetText() == recognized.Text, "Desktop copy places exact fixture text on the Windows clipboard");
        });
        Application.Run(form);
        Console.WriteLine("Result preview closed.");
    }

    private static async Task TestTranslationResults()
    {
        var catalog = new ResultCatalog();
        var engine = new ResultEngine();
        var processor = new TranslationProcessor(catalog, engine);
        var recognized = new OcrResult("  Hello 世界\r\nsecond line  ", "ENG", [new("Hello", new(1, 2, 30, 10), 92.5F)]);
        var result = await processor.ProcessAsync(recognized, "EN", "\0");
        Check(result.TranslationSkipped && result.OutputText == recognized.Text && ReferenceEquals(result.Original, recognized) &&
            result.TargetLanguageCode == "en" && catalog.Calls == 0 && engine.Calls == 0,
            "Same-language processing preserves exact text, regions and confidence without scanning or translating");
        foreach (var pair in new[] { ("chi_sim_vert", "zh"), ("jpn_vert", "ja"), ("kor_vert", "ko"), ("fil", "fil") })
        {
            var mapped = await processor.ProcessAsync(recognized with { SourceLanguageCode = pair.Item1 }, pair.Item2, "\0");
            Check(mapped.TranslationSkipped, $"Same-language bypass uses the mapped OCR language: {pair.Item1}");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await processor.ProcessAsync(recognized, "en", "\0", cancelled.Token); throw new Exception("Ignored cancellation"); }
        catch (OperationCanceledException) { Check(catalog.Calls == 0 && engine.Calls == 0, "Cancelled same-language result performs no work"); }
        async Task Reject(OcrResult input, string target, string expected)
        {
            try { await processor.ProcessAsync(input, target, "fixture"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains(expected), expected); return; }
            throw new Exception("Expected failure: " + expected);
        }
        await Reject(recognized with { Text = "\r\n " }, "en", "No text was detected");
        await Reject(recognized with { SourceLanguageCode = "custom_model" }, "en", "no known translation language code");
        await Reject(recognized, "unknown", "documented translation output language");
        catalog.Result = new([new("es", "en", "reverse"), new("ja", "es", "unrelated")]);
        await Reject(recognized, "es", "en → es");
        Check(engine.Calls == 0, "Reverse and unrelated models cannot reach the translation engine");
        catalog.Result = new([new("en", "es", "direct")]);
        result = await processor.ProcessAsync(recognized, "es", "fixture");
        Check(!result.TranslationSkipped && result.OutputText == "Hola" && engine.Model?.Directory == "direct" && engine.Calls == 1,
            "Different languages use the exact direction and return through the same result contract (fake engine)");
        catalog.Result = new([], "Cannot read fixture folder");
        await Reject(recognized, "es", "Cannot read fixture folder");
    }

    private static async Task TestTargetLanguageRevisionUi(string artifacts)
    {
        string data = NewFolder("revision-ocr");
        Install(data, "eng");
        Install(data, "custom_model");
        string models = Path.Combine(NewFolder("revision-parent"), "not-created-yet");
        var sourceStore = new SourceLanguageSettingsStore(Path.Combine(Root, "revision-source.json"));
        var targetStore = new TargetLanguageSettingsStore(Path.Combine(Root, "revision-target.json"));
        sourceStore.Save(new(data, "eng"));
        targetStore.Save(new(models, "es"));
        using var form = CreateMainForm(sourceStore, targetStore);
        form.Show();
        await form.RefreshSourceLanguagesAsync();
        await WaitForReadiness(form);
        var target = Find<ComboBox>(form, "TargetLanguage");
        var source = Find<ComboBox>(form, "SourceLanguage");
        var badge = Find<Label>(form, "TranslationModelStatus");
        var status = Find<Label>(form, "TargetLanguageStatus");
        Check(Find<Label>(form, "LanguageHint").Text.Contains("may not exist") && target.Enabled,
            "Language selectors explain that listed pairs do not promise compatible models");
        string package = InstallTranslation(models, "en", "es");
        await Until(() => form.SelectedTranslationModel?.Directory == package, "Automatic installation refresh, including creation of a missing model root");
        File.Delete(Path.Combine(package, "model", "model.bin"));
        await Until(() => badge.Text.Contains("Not installed"), "Removing package files automatically refreshes availability");
        Check(form.SelectedTranslationModel is null && target.Enabled && form.SelectedTargetLanguageCode == "es",
            "Automatic removal preserves the target and clears the exposed package");
        InstallTranslation(models, "en", "es");
        await Until(() => form.SelectedTranslationModel?.Directory == package, "Repairing a package automatically restores discovery");
        string moved = models + "-moved";
        Directory.Move(models, moved);
        await Until(() => badge.Text.Contains("Not installed"), "Renaming the model root invalidates its packages");
        Directory.Move(moved, models);
        await Until(() => form.SelectedTranslationModel?.Directory == package, "Restoring the model root is observed");

        var refresh = form.RefreshSourceLanguagesAsync();
        Check(form.SelectedTranslationModel is null && badge.Text.Contains("Checking") && status.AccessibleDescription == status.Text,
            "Pending OCR checks clear target readiness and its accessible description");
        await refresh;
        await WaitForReadiness(form);
        typeof(Form).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [EventArgs.Empty]);
        Check(form.Readiness.State == ReadinessState.Checking && form.SelectedTranslationModel is null,
            "Regaining focus immediately invalidates the previously confirmed pair");
        await WaitForReadiness(form);
        source.SelectedItem = source.Items.Cast<OcrLanguage>().Single(language => language.Code == "custom_model");
        await WaitForReadiness(form);
        Check(badge.Text.Contains("Unknown source") && status.Text.Contains("no known translation language code") && target.Enabled,
            "Unmapped OCR source explains why model availability cannot be determined");
        CaptureLowerSettings(form, artifacts, "target-unmapped-source");
        source.SelectedItem = source.Items.Cast<OcrLanguage>().Single(language => language.Code == "eng");
        await WaitForReadiness(form);
        await form.ValidateSelectedOcrLanguageAsync();
        Check(form.SelectedTranslationModel is null && badge.Text.Contains("Cannot check") && status.Text.Contains("corrupt or incompatible"),
            "Known OCR load failure cannot expose a confirmed translation model");
        File.Delete(Path.Combine(data, "eng.traineddata"));
        await form.RefreshSourceLanguagesAsync();
        await WaitForReadiness(form);
        Check(badge.Text.Contains("Cannot check") && status.Text.Contains("'eng' is unavailable") && target.Enabled,
            "Unavailable saved source is explained in the translation-model status");
        CaptureLowerSettings(form, artifacts, "target-unavailable-source");

        // Recognition has its own captured source: subsequent preference changes must not relabel it.
        target.SelectedItem = TranslationLanguage.Resolve("en");
        var recognized = new OcrResult("Recognized text\r\nSecond line", "eng", []);
        await form.ShowOcrResultAsync(recognized);
        var owned = form.OwnedForms.OfType<TranslationResultForm>().Single();
        Check(Find<TextBox>(owned, "OriginalText").Text == recognized.Text && Find<TextBox>(owned, "OutputText").Text == recognized.Text,
            "Main-form result integration returns identical-language recognition unchanged without a translation model");
        Find<Button>(form, "NavigateSettings").PerformClick();
        Find<Button>(form, "ThemeDark").PerformClick();
        Check(owned.BackColor.R < 60, "Open result follows theme changes without restart");
        Capture(owned, artifacts, "target-same-language-result-dark");
        Find<Button>(form, "ThemeLight").PerformClick();
        Capture(owned, artifacts, "target-same-language-result-light");
        form.Close();
        Check(owned.IsDisposed, "Closing main form disposes the recognized-text result");

        string? copied = null;
        bool failCopy = false;
        using var resultForm = new TranslationResultForm(new(recognized, recognized.Text, "en", true), text =>
        {
            if (failCopy) throw new System.Runtime.InteropServices.ExternalException("busy clipboard fixture");
            copied = text;
        });
        resultForm.Show();
        Find<Button>(resultForm, "CopyOriginal").PerformClick();
        Check(copied == recognized.Text, "Normal Copy original control receives the exact recognized text");
        copied = null;
        Find<Button>(resultForm, "CopyOutput").PerformClick();
        Check(copied == recognized.Text, "Normal Copy output control receives the same text when translation is skipped");
        failCopy = true;
        Find<Button>(resultForm, "CopyOutput").PerformClick();
        Check(Find<Label>(resultForm, "CopyError").Text.Contains("Could not copy"), "Clipboard failure is recoverable");
        failCopy = false;
        Find<Button>(resultForm, "CopyOutput").PerformClick();
        Check(Find<Label>(resultForm, "CopyError").Text == "Copied.", "Copy can be retried successfully");
        resultForm.Size = resultForm.MinimumSize;
        Capture(resultForm, artifacts, "target-result-minimum");
        void AssertResultLayout()
        {
            foreach (var control in Descendants(resultForm).Where(control => control is TextBox or Button || control.Name == "ResultStatus"))
                Check(control.Left >= 0 && control.Top >= 0 && control.Right <= control.Parent!.ClientSize.Width && control.Bottom <= control.Parent.ClientSize.Height,
                    $"Result control {control.Name} fits at {resultForm.DeviceDpi} DPI");
        }
        AssertResultLayout();
        ChangeDpi(resultForm, 144);
        Application.DoEvents();
        AssertResultLayout();
        Capture(resultForm, artifacts, "target-result-150-percent");
        ChangeDpi(resultForm, 96);
        Application.DoEvents();
        ((Button)resultForm.CancelButton!).PerformClick();
        Check(resultForm.IsDisposed, "Result provides a dismiss action for Escape");
    }

    private static async Task Until(Func<bool> predicate, string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { while (!predicate()) await Task.Delay(20, timeout.Token); }
        catch (OperationCanceledException) { throw new InvalidOperationException("Timed out: " + message); }
        Check(true, message);
    }

    private sealed class ResultCatalog : ITranslationModelCatalog
    {
        public int Calls;
        public TranslationModelScan Result = new([]);
        public TranslationModelScan Scan(string directory) { Calls++; return Result; }
    }

    private sealed class ResultEngine : ITranslationEngine
    {
        public int Calls;
        public TranslationModel? Model;
        public Task<string> TranslateAsync(string text, TranslationModel model, CancellationToken cancellationToken)
        { Calls++; Model = model; return Task.FromResult("Hola"); }
    }
}
