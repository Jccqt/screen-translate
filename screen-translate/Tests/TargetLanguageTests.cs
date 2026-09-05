using System.Text.Json;
using screen_translate;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

internal static partial class Program
{
    private static void TestTranslationCatalog()
    {
        var catalog = new ArgosTranslationModelCatalog();
        string directory = NewFolder("translation-catalog");
        Check(catalog.Scan(Path.Combine(directory, "missing")).Models.Count == 0, "Missing translation folder is not installed");
        Check(catalog.Scan(directory).Models.Count == 0, "Empty translation folder is not installed");
        string valid = InstallTranslation(directory, "en", "es");
        string reverse = InstallTranslation(directory, "es", "en", bpe: true);
        string partial = InstallTranslation(directory, "ja", "es");
        File.Delete(Path.Combine(partial, "model", "model.bin"));
        string empty = InstallTranslation(directory, "ko", "es");
        File.WriteAllText(Path.Combine(empty, "sentencepiece.model"), "");
        string malformed = InstallTranslation(directory, "fr", "es");
        File.WriteAllText(Path.Combine(malformed, "metadata.json"), "{broken");
        string metadataOnly = Directory.CreateDirectory(Path.Combine(directory, "metadata-only")).FullName;
        File.WriteAllText(Path.Combine(metadataOnly, "metadata.json"), "{\"from_code\":\"de\",\"to_code\":\"es\"}");
        File.WriteAllText(Path.Combine(directory, "translate-zh_es.argosmodel"), "archive is not an installed package");
        var scan = catalog.Scan(directory);
        Check(scan.Error is null && scan.Models.Count == 2 && scan.IgnoredPackages == 4, "Only complete direct packages are discovered; archives, partial and invalid packages are excluded");
        Check(TranslationModelAvailability.Evaluate("eng", "es", scan).Model?.Directory == valid, "OCR code maps to direct translation pair");
        File.Delete(Path.Combine(valid, "model", "shared_vocabulary.json"));
        Check(TranslationModelAvailability.Evaluate("eng", "es", catalog.Scan(directory)).State == TranslationModelState.Missing,
            "Model without vocabulary is not installed");
        File.WriteAllText(Path.Combine(valid, "model", "source_vocabulary.json"), "[\"fixture\"]");
        File.WriteAllText(Path.Combine(valid, "model", "target_vocabulary.json"), "[\"fixture\"]");
        Check(TranslationModelAvailability.Evaluate("eng", "es", catalog.Scan(directory)).State == TranslationModelState.Installed,
            "Separate source and target vocabulary files are supported");
        Check(TranslationModelAvailability.Evaluate("spa", "en", scan).Model?.Directory == reverse, "BPE package can be discovered");
        Check(TranslationModelAvailability.Evaluate("jpn", "en", scan).State == TranslationModelState.Missing, "Unrelated installed target does not satisfy source pair");
        Check(TranslationModelAvailability.Evaluate("eng", "en", scan).State == TranslationModelState.NotRequired, "Identical languages need no model");
        Check(TranslationModelAvailability.Evaluate(null, "es", scan).State == TranslationModelState.SourceRequired, "No OCR source cannot claim model availability");
        Check(TranslationModelAvailability.Evaluate("custom_model", "es", scan).State == TranslationModelState.UnsupportedSource, "Unknown OCR codes are not guessed");
        Check(TranslationLanguage.FromOcrCode("chi_sim_vert") == "zh" && TranslationLanguage.FromOcrCode("chi_tra") == "zt" &&
            TranslationLanguage.FromOcrCode("JPN_VERT") == "ja" && TranslationLanguage.FromOcrCode("kor_vert") == "ko", "Chinese variants and vertical OCR codes map explicitly");
        using (var locked = new FileStream(Path.Combine(valid, "model", "model.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(TranslationModelAvailability.Evaluate("eng", "es", catalog.Scan(directory)).State == TranslationModelState.Missing, "Locked model is not reported installed");
        File.Delete(Path.Combine(reverse, "bpe.model"));
        scan = catalog.Scan(directory);
        Check(TranslationModelAvailability.Evaluate("spa", "en", scan).State == TranslationModelState.Missing, "Reverse direction cannot use a forward model");
        Check(catalog.Scan("\0").Error is not null, "Invalid translation folder reports scan error");
        var readError = catalog.Scan(Path.Combine(valid, "metadata.json"));
        Check(TranslationModelAvailability.Evaluate("eng", "es", readError).State == TranslationModelState.ReadError, "Unreadable folder is distinct from missing models");
        Check(TranslationModelAvailability.Evaluate("eng", "en", readError).State == TranslationModelState.NotRequired, "Identical language needs no folder access");
        File.Delete(Path.Combine(valid, "model", "config.json"));
        Check(catalog.Scan(directory).Models.Count == 0, "Removed required configuration invalidates installation");
    }

    private static void TestTargetSettings()
    {
        string path = Path.Combine(Root, "target-settings", "target-language.json");
        var store = new TargetLanguageSettingsStore(path);
        Check(store.Load(out var error) == TargetLanguageSettings.Default && error is null, "First launch target defaults are valid");
        var settings = new TargetLanguageSettings(NewFolder("saved-translation"), "ja");
        Check(store.Save(settings) is null && store.Load(out error) == settings && error is null, "Target code and folder persist");
        Check(store.Save(settings with { TargetLanguageCode = "FR" }) is null && store.Load(out _).TargetLanguageCode == "fr", "Target code is normalized on load");
        store.Save(settings with { TargetLanguageCode = "unknown" });
        var recovered = store.Load(out error);
        Check(recovered.TargetLanguageCode == "es" && recovered.TranslationModelDirectory == settings.TranslationModelDirectory && error is not null,
            "Unknown target recovers without losing model directory");
        File.WriteAllText(path, "{broken");
        Check(store.Load(out error) == TargetLanguageSettings.Default && error is not null, "Corrupt target settings recover with an error");
        store.Save(settings with { TranslationModelDirectory = "relative" });
        Check(store.Load(out error) == TargetLanguageSettings.Default && error is not null, "Relative translation directory is rejected on load");
        var blocked = new TargetLanguageSettingsStore(Path.Combine(path, "blocked.json"));
        Check(blocked.Save(settings) is not null, "Target save failures are reported");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Target settings save cleans up temporary files");
    }

    private static async Task TestTargetLanguageUi(MainForm form, SourceLanguageSettingsStore sourceStore,
        TargetLanguageSettingsStore targetStore, string artifactDirectory)
    {
        var target = Find<ComboBox>(form, "TargetLanguage");
        var source = Find<ComboBox>(form, "SourceLanguage");
        var status = Find<Label>(form, "TargetLanguageStatus");
        var badge = Find<Label>(form, "TranslationModelStatus");
        Check(target.Enabled && target.DropDownStyle == ComboBoxStyle.DropDownList && target.Items.Count == TranslationLanguage.All.Count,
            "Output choices remain selectable with no translation model");
        target.SelectedItem = TranslationLanguage.Resolve("fr");
        await form.RefreshTranslationModelsAsync();
        Check(form.SelectedTargetLanguageCode == "fr" && targetStore.Load(out _).TargetLanguageCode == "fr", "Changing target exposes and persists its stable code");
        Check(badge.Text.Contains("Not installed") && status.Text.Contains("ja → fr"), "Missing status describes the selected direction");
        Capture(form, artifactDirectory, "target-missing");
        string directory = form.TranslationModelDirectory;
        InstallTranslation(directory, "en", "fr");
        await form.RefreshTranslationModelsAsync();
        Check(form.SelectedTranslationModel is null, "UI rejects installed package for another source");
        string package = InstallTranslation(directory, "ja", "fr");
        await form.RefreshTranslationModelsAsync();
        Check(badge.Text.Contains("Installed") && form.SelectedTranslationModel?.Directory == package, "Installed pair updates badge and exposes package");
        Capture(form, artifactDirectory, "target-installed");
        using (var reopened = CreateMainForm(sourceStore, targetStore))
        {
            await reopened.RefreshSourceLanguagesAsync();
            Check(reopened.SelectedTargetLanguageCode == "fr" && reopened.SelectedTranslationModel?.Directory == package, "Restart restores target and recomputes model status");
        }
        target.SelectedItem = TranslationLanguage.Resolve("ja");
        await form.RefreshTranslationModelsAsync();
        Check(badge.Text.Contains("Not required") && form.SelectedTranslationModel is null, "UI handles identical languages without claiming installation");
        source.SelectedItem = source.Items.Cast<OcrLanguage>().Single(language => language.Code == "eng");
        await form.RefreshTranslationModelsAsync();
        Check(badge.Text.Contains("Not installed") && status.Text.Contains("en → ja"), "Changing source refreshes required direction");
        source.SelectedItem = source.Items.Cast<OcrLanguage>().Single(language => language.Code == "jpn");
        target.SelectedItem = TranslationLanguage.Resolve("fr");
        await form.RefreshTranslationModelsAsync();
        File.Delete(Path.Combine(package, "model", "model.bin"));
        await form.RefreshTranslationModelsAsync();
        Check(badge.Text.Contains("Not installed") && form.SelectedTargetLanguageCode == "fr", "Removing model updates availability without changing output choice");
        await form.SetTranslationModelDirectoryAsync(Path.Combine(package, "metadata.json"));
        Check(badge.Text.Contains("Cannot check") && status.Text.Contains("Cannot read") && target.Enabled, "Folder scan error is clear and leaves output selectable");
        Capture(form, artifactDirectory, "target-read-error");
        await form.SetTranslationModelDirectoryAsync(directory);
        Check(targetStore.Load(out _).TranslationModelDirectory == directory, "Choosing translation folder persists it");
        var pending = form.RefreshTranslationModelsAsync();
        Check(form.SelectedTranslationModel is null && badge.Text.Contains("Checking"), "Rescan invalidates cached installation immediately");
        await pending;
        Check(sourceStore.Load(out _).SourceLanguageCode == "jpn", "Target settings preserve original source selection");

        await TestTranslationRefreshOrdering(sourceStore, targetStore);
        string blockedPath = Path.Combine(Root, "blocked-target-parent");
        File.WriteAllText(blockedPath, "not a directory");
        using var blockedForm = CreateMainForm(sourceStore, new TargetLanguageSettingsStore(Path.Combine(blockedPath, "settings.json")));
        Find<ComboBox>(blockedForm, "TargetLanguage").SelectedItem = TranslationLanguage.Resolve("de");
        await blockedForm.RefreshTranslationModelsAsync();
        Check(Find<Label>(blockedForm, "TargetSettingsError").Text.Contains("Could not save") && blockedForm.SelectedTargetLanguageCode == "de",
            "Save error is visible and current session keeps target choice");
    }

    private static async Task TestTranslationRefreshOrdering(SourceLanguageSettingsStore sourceStore, TargetLanguageSettingsStore targetStore)
    {
        var catalog = new ControlledCatalog();
        using var form = CreateMainForm(sourceStore, targetStore, catalog);
        await form.RefreshSourceLanguagesAsync();
        catalog.DelayNext = true;
        var stale = form.RefreshTranslationModelsAsync();
        await catalog.Started.Task;
        try
        {
            await form.RefreshTranslationModelsAsync();
            catalog.Release.SetResult(new([], "Stale folder failure"));
            await stale;
            Check(!Find<Label>(form, "TargetLanguageStatus").Text.Contains("Stale"), "Slow older scan cannot overwrite the latest status");
        }
        finally { catalog.Release.TrySetResult(new([])); }
    }

    private sealed class ControlledCatalog : ITranslationModelCatalog
    {
        public bool DelayNext;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TranslationModelScan> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslationModelScan Scan(string directory)
        {
            if (!DelayNext) return new([]);
            DelayNext = false;
            Started.SetResult();
            return Release.Task.GetAwaiter().GetResult();
        }
    }

    // Synthetic discovery fixtures, deliberately not real or licensed translation weights.
    private static string InstallTranslation(string directory, string source, string target, bool bpe = false)
    {
        string package = Directory.CreateDirectory(Path.Combine(directory, source + "_" + target)).FullName;
        Directory.CreateDirectory(Path.Combine(package, "model"));
        File.WriteAllText(Path.Combine(package, "metadata.json"), JsonSerializer.Serialize(new { from_code = source, to_code = target }));
        File.WriteAllText(Path.Combine(package, "model", "model.bin"), "model discovery fixture");
        File.WriteAllText(Path.Combine(package, "model", "config.json"), "{}");
        File.WriteAllText(Path.Combine(package, "model", "shared_vocabulary.json"), "[\"fixture\"]");
        File.WriteAllText(Path.Combine(package, bpe ? "bpe.model" : "sentencepiece.model"), "tokenizer discovery fixture");
        return package;
    }
}
