using System.Reflection;
using screen_translate;
using screen_translate.Interface;
using screen_translate.Models;
using screen_translate.Ocr;
using screen_translate.Settings;

internal static partial class Program
{
    private static async Task TestOfflineModelRecovery()
    {
        var manager = new ModelManager();
        string archive = PackageArchive("recovery-package");
        foreach (string brokenFile in new[] { "metadata.json", "model/model.bin", "stanza/en/resources.json" })
        {
            string root = NewFolder("recovery-" + Guid.NewGuid().ToString("N"));
            var installed = await manager.ImportAsync(ModelPurpose.Translation, archive, root, false);
            File.Delete(Path.Combine(installed.Location, brokenFile));
            var broken = manager.Scan(ModelPurpose.Translation, root).Single();
            Check(broken.State == ModelInstallState.Invalid && broken.Language == "en → es", "Damaged package retains its known direction: " + brokenFile);
            Check(broken.Files.Any(p => p.EndsWith(".screen-translate.json")) && broken.Files.Any(p => p.EndsWith("test.pt")),
                "Missing data does not truncate the installation inventory: " + brokenFile);
            var repaired = await manager.ImportAsync(ModelPurpose.Translation, archive, root, true);
            Check(repaired.State == ModelInstallState.Discovered, "Damaged package can be replaced in place: " + brokenFile);
            File.Delete(Path.Combine(repaired.Location, brokenFile));
            manager.Remove(manager.Scan(ModelPurpose.Translation, root).Single(), root);
            Check(!Directory.Exists(repaired.Location) && !Directory.EnumerateFileSystemEntries(root).Any(),
                "Removing damaged package reclaims receipt, auxiliary data and known empty ancestors: " + brokenFile);
        }
        string corruptRoot = NewFolder("corrupt-metadata-recovery");
        var corrupt = await manager.ImportAsync(ModelPurpose.Translation, archive, corruptRoot, false);
        File.WriteAllText(Path.Combine(corrupt.Location, "metadata.json"), "{broken");
        var corruptDetails = manager.Scan(ModelPurpose.Translation, corruptRoot).Single();
        Check(corruptDetails.State == ModelInstallState.Invalid && corruptDetails.Files.Count == corrupt.Files.Count,
            "Malformed metadata preserves the complete safe receipt inventory");
        File.WriteAllText(Path.Combine(corrupt.Location, "user-notes.txt"), "keep");
        manager.Remove(corruptDetails, corruptRoot);
        Check(Directory.GetFiles(corrupt.Location).Select(Path.GetFileName).SequenceEqual(new[] { "user-notes.txt" }),
            "Damaged-package removal still preserves unrelated files");

        foreach (string unsafeFiles in new[] { "[\"user-notes.txt\",\"../outside.txt\"]", "[null]" })
        {
            string unsafeRoot = NewFolder("unsafe-receipt-" + Guid.NewGuid().ToString("N"));
            var unsafeModel = await manager.ImportAsync(ModelPurpose.Translation, archive, unsafeRoot, false);
            string outside = Path.Combine(unsafeRoot, "outside.txt");
            string notes = Path.Combine(unsafeModel.Location, "user-notes.txt");
            File.WriteAllText(outside, "keep");
            File.WriteAllText(notes, "keep");
            File.WriteAllText(Path.Combine(unsafeModel.Location, ".screen-translate.json"),
                "{\"Files\":" + unsafeFiles + "}");
            var unsafeDetails = manager.Scan(ModelPurpose.Translation, unsafeRoot).Single();
            Check(unsafeDetails.State == ModelInstallState.Invalid && !unsafeDetails.Files.Contains(notes),
                "A malformed receipt cannot partially authorize extra files");
            manager.Remove(unsafeDetails, unsafeRoot);
            Check(File.Exists(outside) && File.Exists(notes), "Damaged-receipt removal preserves files outside its safe inventory");
        }

        string readOnlyRoot = NewFolder("read-only-removal");
        var readOnly = await manager.ImportAsync(ModelPurpose.Translation, archive, readOnlyRoot, false);
        string weights = Path.Combine(readOnly.Location, "model/model.bin");
        File.SetAttributes(weights, FileAttributes.ReadOnly);
        try
        {
            await RejectModel(() => Task.Run(() => manager.Remove(readOnly, readOnlyRoot)), "Read-only removal is reported as blocked before files move");
            Check(readOnly.Files.All(File.Exists) && !Directory.GetDirectories(readOnlyRoot).Any(ModelPackage.IsTransaction),
                "Blocked read-only removal leaves the complete model visible and creates no hidden orphan");
        }
        finally { File.SetAttributes(weights, FileAttributes.Normal); }
        manager.Remove(readOnly, readOnlyRoot);
        Check(!Directory.EnumerateFileSystemEntries(readOnlyRoot).Any(), "Removal can be retried after resolving read-only access");

        // Exercise cleanup with a Windows sharing violation on isolated transaction files.
        string cleanup = Directory.CreateDirectory(Path.Combine(NewFolder("cleanup-recovery"), ".st-test")).FullName;
        string held = Path.Combine(cleanup, "removed-0");
        File.WriteAllText(held, "isolated quarantined fixture");
        var cleanStage = typeof(ModelManager).GetMethod("CleanStage", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var locked = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { cleanStage.Invoke(null, [cleanup, true]); throw new Exception("Cleanup failure was hidden"); }
            catch (TargetInvocationException error) when (error.InnerException is IOException failure)
            { Check(failure.Message.Contains(cleanup), "Cleanup failure reports the exact retained transaction directory"); }
            Check(File.Exists(held), "Failed cleanup preserves the remaining data for recovery");
        }
        cleanStage.Invoke(null, [cleanup, true]);
        Check(!Directory.Exists(cleanup), "Cleanup succeeds after the blocking handle is released");

        if (RealOcrData is not string real) return;
        string ocrRoot = NewFolder("ocr-history-recovery");
        string data = Path.Combine(ocrRoot, "eng.traineddata");
        Install(ocrRoot, "eng");
        await RejectModel(() => manager.ValidateOcrAsync(ocrRoot, "eng", CancellationToken.None), "OCR failure enters the shared validation history");
        Check(manager.ValidationHistory.Failure(data) is not null, "Known OCR load failure is retained");
        string invalidImport = Path.Combine(NewFolder("invalid-history-import"), "eng.traineddata");
        File.WriteAllText(invalidImport, "invalid replacement");
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Ocr, invalidImport, ocrRoot, true),
            "A failed replacement does not claim that the existing model was repaired");
        Check(manager.ValidationHistory.Failure(data) is not null, "Failed replacement preserves the previous known OCR failure");
        await manager.ImportAsync(ModelPurpose.Ocr, Path.Combine(real, "eng.traineddata"), ocrRoot, true);
        Check(manager.ValidationHistory.Failure(data) is null, "Successful validated import clears the old OCR failure");
        File.WriteAllText(data, "corrupt replacement");
        await RejectModel(() => manager.ValidateOcrAsync(ocrRoot, "eng", CancellationToken.None), "A later OCR failure is remembered independently");
        byte[] modelBytes = File.ReadAllBytes(Path.Combine(real, "eng.traineddata"));
        using var client = new System.Net.Http.HttpClient(new ModelHttpHandler(() => new(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.ByteArrayContent(modelBytes) }));
        var downloader = new ModelManager(httpClient: client, validationHistory: manager.ValidationHistory);
        await downloader.DownloadAsync(new("English fixture", ModelPurpose.Ocr, "eng.traineddata", new("https://models.example.test/eng"), "Unknown"), ocrRoot, true);
        Check(manager.ValidationHistory.Failure(data) is null, "Successful validated download clears the same shared failure");
        File.Delete(data);
        var orphan = manager.Scan(ModelPurpose.Ocr, ocrRoot).Single();
        Check(orphan.State == ModelInstallState.Missing && orphan.Files.Count == 1, "An orphan OCR receipt stays visible and removable");
        manager.Remove(orphan, ocrRoot);
        Check(!Directory.EnumerateFileSystemEntries(ocrRoot).Any(), "Removing an orphan receipt leaves the OCR storage root intact and empty");
    }

    private static async Task TestOfflineModelRecoveryUi(string artifacts)
    {
        string data = NewFolder("ocr-shared-history-ui");
        Install(data, "eng");
        Install(data, "jpn");
        var source = new SourceLanguageSettingsStore(Path.Combine(Root, "shared-history-source.json"));
        source.Save(new(data, "eng"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, "shared-history-target.json"));
        target.Save(new(NewFolder("shared-history-translations"), "es"));
        using var main = CreateMainForm(source, target);
        main.Show(); Application.DoEvents();
        await main.RefreshSourceLanguagesAsync();
        await main.ValidateSelectedOcrLanguageAsync();
        Check(Find<Label>(main, "OcrModelStatus").Text.Contains("Load failed"), "Main window remembers a real OCR loading failure");

        async Task ExerciseManager(Func<ModelManagerForm, Task> exercise)
        {
            Exception? failure = null;
            Find<Button>(main, "NavigateModels").PerformClick();
            main.BeginInvoke((Action)(async () =>
            {
                var window = Application.OpenForms.OfType<ModelManagerForm>().Single(f => f.Owner == main);
                try
                {
                    await window.RefreshModelsAsync();
                    await Until(() => Find<ListBox>(window, "ModelInventory").Enabled &&
                        Find<ListBox>(window, "ModelInventory").Items.Count == 2, "Latest model-manager scan completes");
                    await exercise(window);
                }
                catch (Exception error) { failure = error; }
                finally { window.Close(); }
            }));
            Find<Button>(main, "ManageOcrModels").PerformClick();
            await main.RefreshSourceLanguagesAsync();
            if (failure is not null) throw new InvalidOperationException("Model manager recovery UI failed", failure);
        }

        if (RealOcrData is string real)
        {
            File.Copy(Path.Combine(real, "eng.traineddata"), Path.Combine(data, "eng.traineddata"), true);
            await ExerciseManager(async window =>
            {
                Check(Find<TextBox>(window, "ModelDetails").Text.Contains("corrupt or incompatible"), "Manager inherits the main window's known OCR failure");
                Find<Button>(window, "ValidateModel").PerformClick();
                await Until(() => Find<TextBox>(window, "ModelDetails").Text.Contains("Installation state: Validated"), "Manager validates the repaired genuine OCR data");
                Capture(window, artifacts, "model-recovery-validated");
            });
            Check(!Find<Label>(main, "OcrModelStatus").Text.Contains("Load failed"), "Successful manager validation clears the main-window failure");
        }
        File.WriteAllText(Path.Combine(data, "eng.traineddata"), "corrupt again");
        await ExerciseManager(async window =>
        {
            Find<Button>(window, "ValidateModel").PerformClick();
            await Until(() => Find<TextBox>(window, "ModelDetails").Text.Contains("corrupt or incompatible"), "Manager records a new native OCR failure");
            Capture(window, artifacts, "model-recovery-invalid");
        });
        Check(Find<Label>(main, "OcrModelStatus").Text.Contains("Load failed"), "Manager rejection remains known after returning to the main window");
        main.Close();

        var slow = new DelayedOcrEngine();
        using var switching = new MainForm(source, target, interfaceSettingsStore:
            new InterfaceSettingsStore(Path.Combine(Root, "busy-ocr-interface.json")), globalShortcut: new FakeShortcut(), ocrEngine: slow);
        await switching.RefreshSourceLanguagesAsync();
        Task pending = switching.ValidateSelectedOcrLanguageAsync();
        var combo = Find<ComboBox>(switching, "SourceLanguage");
        combo.SelectedItem = combo.Items.Cast<OcrLanguage>().Single(x => x.Code == "jpn");
        await switching.ValidateSelectedOcrLanguageAsync();
        Check(Find<Label>(switching, "OcrModelStatus").Text.Contains("In use"), "Concurrent validation explains its temporary model-use block");
        slow.Release.SetResult(); await pending;
        await switching.RefreshSourceLanguagesAsync();
        Check(Find<Label>(switching, "OcrModelStatus").Text.Contains("Discovered") && !Find<Label>(switching, "LanguageHint").Text.Contains("in use"),
            "Refreshing after native completion clears the block without caching a false model failure");
    }
}
