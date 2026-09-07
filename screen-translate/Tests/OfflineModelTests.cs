using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using screen_translate.Models;
using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Translation;

internal static partial class Program
{
    private static async Task RejectModel(Func<Task> work, string description)
    {
        try { await work(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or HttpRequestException or OcrModelLoadException or OperationCanceledException)
        { Check(true, description); return; }
        throw new Exception("Expected rejection: " + description);
    }

    private static string PackageArchive(string name, Action<ZipArchive>? amend = null, bool wrapper = true)
    {
        string file = Path.Combine(Root, name + ".argosmodel");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        string prefix = wrapper ? "translate-en_es/" : "";
        foreach (var (path, data) in new Dictionary<string, string>
        {
            ["metadata.json"] = "{\"from_code\":\"en\",\"to_code\":\"es\",\"package_version\":\"1.0\"}",
            ["sentencepiece.model"] = "tokenizer fixture", ["model/model.bin"] = "synthetic weights, not inference",
            ["model/config.json"] = "{}", ["model/shared_vocabulary.json"] = "[\"fixture\"]",
            ["stanza/en/resources.json"] = "{}", ["stanza/en/tokenize/test.pt"] = "offline auxiliary fixture"
        })
        {
            using var writer = new StreamWriter(zip.CreateEntry(prefix + path).Open());
            writer.Write(data);
        }
        amend?.Invoke(zip);
        return file;
    }

    private static async Task TestOfflineModels()
    {
        string root = NewFolder("managed-translation");
        string archive = PackageArchive("complete", zip => zip.CreateEntry("translate-en_es/optional-empty.txt"));
        var manager = new ModelManager();
        var imported = await manager.ImportAsync(ModelPurpose.Translation, archive, root, false);
        Check(imported.State == ModelInstallState.Discovered && imported.Language == "en → es" && imported.Version == "1.0",
            "Import installs one complete direct-pair package without claiming binary validation");
        Check(imported.Source == "Unknown" && imported.License == "Unknown" && imported.DiskSize > 0,
            "Imported absent provenance remains Unknown and installed disk size is measured");
        Check(imported.Files.Any(p => p.EndsWith("test.pt")), "All archived offline auxiliary data is installed and identified");
        Check(imported.Files.Any(p => p.EndsWith("optional-empty.txt")), "Empty optional archive files do not invalidate a complete model");
        Check(new ArgosTranslationModelCatalog().Scan(root).Models.Count == 1, "Installed package is available to existing direction discovery");
        Check(manager.Scan(ModelPurpose.Translation, root).Count == 1, "Management inventory discovers installed package");
        string original = File.ReadAllText(Path.Combine(imported.Location, "model/model.bin"));
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, false), "Replacement requires an explicit choice");
        Check(File.ReadAllText(Path.Combine(imported.Location, "model/model.bin")) == original, "Rejected replacement preserves original weights");
        string unrelated = Path.Combine(imported.Location, "personal-notes.txt");
        File.WriteAllText(unrelated, "Keep this file");
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, true), "Unidentified package contents block destructive replacement");
        Check(File.ReadAllText(unrelated) == "Keep this file", "Blocked replacement preserves unrelated contents");
        File.Delete(unrelated);
        imported = await manager.ImportAsync(ModelPurpose.Translation, archive, root, true);
        Check(imported.State == ModelInstallState.Discovered, "Explicit managed replacement succeeds");

        foreach (string unsafeName in new[] { "../escape.txt", "/absolute.txt", "C:/outside.txt", "translate-en_es/../../escape.txt",
            "translate-en_es/model.bin:stream", "translate-en_es/CON.txt", "translate-en_es/file. ", "translate-en_es/.screen-translate.json",
            "translate-en_es/.st-spoof/metadata.json", "translate-en_es/MODEL/config.json" })
        {
            string bad = PackageArchive("unsafe-" + Guid.NewGuid().ToString("N"), zip =>
            { using var writer = new StreamWriter(zip.CreateEntry(unsafeName).Open()); writer.Write("bad"); });
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, bad, root, true), "Reject unsafe archive entry " + unsafeName);
            Check(File.ReadAllText(Path.Combine(imported.Location, "model/model.bin")) == original, "Unsafe replacement preserves usable package");
        }
        string symlink = PackageArchive("symlink", zip =>
        { var entry = zip.CreateEntry("translate-en_es/link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); });
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, symlink, root, true), "Archive symlink rejected");
        string incomplete = PackageArchive("incomplete", wrapper: false);
        using (var zip = ZipFile.Open(incomplete, ZipArchiveMode.Update)) zip.GetEntry("sentencepiece.model")!.Delete();
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, incomplete, root, true), "Missing tokenizer rejects incomplete package");
        string invalidJson = PackageArchive("invalid-json");
        using (var zip = ZipFile.Open(invalidJson, ZipArchiveMode.Update))
        {
            zip.GetEntry("translate-en_es/model/config.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("translate-en_es/model/config.json").Open()); writer.Write("invalid");
        }
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, invalidJson, root, true), "Malformed required JSON rejects installation");
        string oversizedMetadata = PackageArchive("oversized-metadata");
        using (var zip = ZipFile.Open(oversizedMetadata, ZipArchiveMode.Update))
        {
            zip.GetEntry("translate-en_es/metadata.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("translate-en_es/metadata.json").Open());
            writer.Write(new string(' ', 1024 * 1024) + "{\"from_code\":\"en\",\"to_code\":\"es\"}");
        }
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, oversizedMetadata, root, true), "Oversized metadata is rejected before unbounded JSON parsing");
        string unsupported = Path.Combine(Root, "unsupported.exe"); File.WriteAllText(unsupported, "unsupported");
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, unsupported, root, false), "Unsupported extension is rejected");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, true, cancelled.Token), "Cancelled import preserves existing model");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, true, cancelled.Token,
                new InlineModelProgress(_ => cancelled.Cancel())), "Cancellation during extraction preserves the existing package");
            Check(ModelPackage.Inspect(ModelPurpose.Translation, imported.Location).State == ModelInstallState.Discovered,
                "Extraction cancellation leaves the previous installation discovered");
        }
        using (ModelUse.Acquire(root))
        {
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, true), "Active model use blocks installation/replacement");
            await RejectModel(() => Task.Run(() => manager.Remove(imported, root)), "Active model use blocks removal");
            Check(new ArgosTranslationModelCatalog().Scan(root).Error?.Contains("in use") == true, "Discovery does not observe in-progress publication");
        }
        // A cancelled caller must not release the lease while an uninterruptible worker is still active.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = ModelUse.RunAsync(root, () => release.Task);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await RejectModel(() => worker.WaitAsync(cancelled.Token), "Caller can cancel waiting on native work");
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Translation, archive, root, true), "Cancelled wait retains actual worker protection");
        }
        release.SetResult(); await worker;
        using (ModelUse.Acquire(root)) Check(true, "Actual worker completion releases model protection");

        string transaction = Directory.CreateDirectory(Path.Combine(root, ".st-fixture")).FullName;
        InstallTranslation(transaction, "en", "es");
        Check(new ArgosTranslationModelCatalog().Scan(root).Models.Count == 1 && manager.Scan(ModelPurpose.Translation, root).Count == 1,
            "Temporary installation directories never count as installed");

        byte[] bytes = File.ReadAllBytes(archive);
        var handler = new ModelHttpHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        var downloader = new ModelManager(httpClient: http);
        var descriptor = new ModelDownload("Fixture direct pair", ModelPurpose.Translation, "download.argosmodel",
            new Uri("https://models.example.test/package"), "Fixture license", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), "2.0");
        Check(descriptor.Describe().Contains("Download size:") && descriptor.Describe().Contains("Installed disk size:"), "Download disclosure distinguishes archive and disk sizes");
        Check((descriptor with { DownloadSize = null }).Describe().Contains("Download size: Unknown"), "Unknown download size is explicit before network access");
        var progressEvents = new List<ModelTransferProgress>();
        var progress = new InlineModelProgress(p => progressEvents.Add(p));
        imported = await downloader.DownloadAsync(descriptor, root, true, progress: progress);
        Check(imported.Source == descriptor.Source.ToString() && imported.License == "Fixture license" && imported.Version == "2.0", "Reviewed download provenance persists with installation");
        Check(progressEvents.Any(p => p.Phase == "Downloading" && p.Bytes == bytes.Length) && progressEvents.Any(p => p.Phase.Contains("SHA-256")), "Download reports progress and verifies trusted checksum");
        await RejectModel(() => downloader.DownloadAsync(descriptor with { Sha256 = new string('0', 64) }, root, true), "Checksum mismatch rejects replacement");
        Check(File.ReadAllText(Path.Combine(imported.Location, "model/model.bin")) == original, "Checksum failure preserves existing model");
        await RejectModel(() => downloader.DownloadAsync(descriptor with { DownloadSize = bytes.Length + 1 }, root, true), "Truncated transfer rejected against declared size");
        using (var cancelled = new CancellationTokenSource())
        {
            await RejectModel(() => downloader.DownloadAsync(descriptor, root, true, cancelled.Token,
                new InlineModelProgress(_ => cancelled.Cancel())), "Cancellation during transfer never publishes partial data");
        }
        handler.Respond = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await RejectModel(() => downloader.DownloadAsync(descriptor, root, true), "Failed HTTP download is recoverable");
        handler.Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        imported = await downloader.DownloadAsync(descriptor, root, true);
        Check(imported.State == ModelInstallState.Discovered, "Retry after failed download succeeds from the beginning");
        handler.Respond = () => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://other.example.test/") } };
        await RejectModel(() => downloader.DownloadAsync(descriptor, root, true), "Redirect to an unreviewed source is rejected");

        string externalRoot = NewFolder("unmanaged-removal");
        string external = InstallTranslation(externalRoot, "en", "es");
        File.WriteAllText(Path.Combine(external, "notes.txt"), "unrelated");
        File.WriteAllText(Path.Combine(externalRoot, "parent-notes.txt"), "parent");
        var externalDetails = ModelPackage.Inspect(ModelPurpose.Translation, external);
        manager.Remove(externalDetails, externalRoot);
        Check(File.ReadAllText(Path.Combine(external, "notes.txt")) == "unrelated" && File.Exists(Path.Combine(externalRoot, "parent-notes.txt")),
            "Removing externally discovered model preserves unrelated package and parent files");
        Check(new ArgosTranslationModelCatalog().Scan(externalRoot).Models.Count == 0, "Removal updates discovery availability");
        File.WriteAllText(Path.Combine(imported.Location, "notes.txt"), "keep managed unrelated");
        using (var locked = new FileStream(Path.Combine(imported.Location, "model/model.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await RejectModel(() => Task.Run(() => manager.Remove(imported, root)), "Locked model file rolls back removal");
        Check(ModelPackage.Inspect(ModelPurpose.Translation, imported.Location).State == ModelInstallState.Discovered, "Failed removal restores all previously moved files");
        manager.Remove(imported, root);
        Check(File.Exists(Path.Combine(imported.Location, "notes.txt")) && Directory.Exists(root), "Managed removal preserves extra files and storage root");
        await RejectModel(() => Task.Run(() => manager.Remove(externalDetails, root)), "Removal rejects a model outside the configured root");

        string ocrRoot = NewFolder("managed-ocr");
        string invalidOcr = Path.Combine(NewFolder("ocr-import"), "eng.traineddata");
        File.WriteAllText(invalidOcr, "invalid OCR data");
        await RejectModel(() => manager.ImportAsync(ModelPurpose.Ocr, invalidOcr, ocrRoot, false), "OCR imports require successful real Tesseract loading");
        Check(Catalog.Scan(ocrRoot).Languages.Count == 0, "Corrupt imported OCR data is never installed");
        if (RealOcrData is string real)
        {
            var installedOcr = await manager.ImportAsync(ModelPurpose.Ocr, Path.Combine(real, "eng.traineddata"), ocrRoot, false);
            Check(installedOcr.State == ModelInstallState.Discovered && installedOcr.License == "Unknown", "Genuine imported OCR loads offline and does not guess its provenance");
            using (var lockedReceipt = new FileStream(installedOcr.Location + ".screen-translate.json", FileMode.Open, FileAccess.Read, FileShare.Read))
                await RejectModel(() => manager.ImportAsync(ModelPurpose.Ocr, Path.Combine(real, "eng.traineddata"), ocrRoot, true),
                    "OCR receipt publication failure restores the previous data and metadata");
            Check(ModelPackage.Inspect(ModelPurpose.Ocr, installedOcr.Location).State == ModelInstallState.Discovered,
                "Failed OCR publication restores a complete usable installation");
            await RejectModel(() => manager.ImportAsync(ModelPurpose.Ocr, invalidOcr, ocrRoot, true), "Invalid replacement cannot damage genuine OCR model");
            await new TesseractOcrEngine().ValidateLanguageAsync(ocrRoot, "eng");
            Check(true, "Original OCR remains usable after failed replacement");
            File.WriteAllText(Path.Combine(ocrRoot, "notes.txt"), "keep");
            manager.Remove(installedOcr, ocrRoot);
            Check(File.Exists(Path.Combine(ocrRoot, "notes.txt")) && Catalog.Scan(ocrRoot).Languages.Count == 0, "OCR removal affects only selected data and its receipt");
        }
        Check(ModelPackage.Inspect(ModelPurpose.Translation, Path.Combine(root, "missing")).State == ModelInstallState.Missing,
            "Missing model has a distinct inspection state");
    }

    private sealed class ModelHttpHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Respond()); }
    }
    private sealed class InlineModelProgress(Action<ModelTransferProgress> report) : IProgress<ModelTransferProgress>
    { public void Report(ModelTransferProgress value) => report(value); }

    private static async Task TestOfflineModelsUi(string artifacts)
    {
        string root = NewFolder("model-manager-ui");
        string package = InstallTranslation(root, "en", "es");
        using var manager = new ModelManagerForm(ModelPurpose.Translation, root);
        manager.Show();
        Application.DoEvents();
        await manager.RefreshModelsAsync();
        Check(Find<ListBox>(manager, "ModelInventory").Items.Count == 1, "Model manager lists the configured local model inventory: " + Find<TextBox>(manager, "ModelDetails").Text);
        string details = Find<TextBox>(manager, "ModelDetails").Text;
        foreach (string label in new[] { "Name:", "Purpose:", "Language / direction:", "Version:", "Location:", "Installation state:", "Installed disk size:", "Source:", "License:" })
            Check(details.Contains(label), "Inspection displays " + label);
        Find<Button>(manager, "ValidateModel").PerformClick();
        Check(Find<Label>(manager, "ModelOperationStatus").Text.Contains("no production translation runtime"), "Manager cannot falsely validate translation fixtures");
        Capture(manager, artifacts, "model-manager-light");
        manager.Size = manager.MinimumSize;
        manager.ApplyTheme(Color.FromArgb(30, 43, 48), Color.FromArgb(228, 234, 233));
        Application.DoEvents();
        Capture(manager, artifacts, "model-manager-dark-minimum");
        Check(Find<TextBox>(manager, "ModelDetails").Height > 80 && Find<ListBox>(manager, "ModelInventory").Height > 40,
            "Model list and scrollable metadata remain accessible at minimum size");
        ChangeDpi(manager, 144); Application.DoEvents();
        Capture(manager, artifacts, "model-manager-150-percent");
        manager.Close();
        using var download = new ModelDownloadForm(ModelPurpose.Ocr);
        download.Show(); Application.DoEvents();
        Check(Find<TextBox>(download, "DownloadDisclosure").Text.Contains("Apache-2.0") &&
            Find<TextBox>(download, "DownloadDownloadbytes").Text == "4113088", "Download dialog discloses source, license and pinned size before user initiation");
        Capture(download, artifacts, "model-download-review");
        Find<TextBox>(download, "DownloadHTTPSsource").Text = "https://models.example.test/custom.traineddata";
        Check(Find<TextBox>(download, "DownloadLicense").Text == "Unknown" && Find<TextBox>(download, "DownloadPublisherSHA-256").Text == "Unknown",
            "Editing source clears original publisher metadata rather than attributing it to a custom model");
        Check(Find<TextBox>(download, "DownloadDisclosure").Text.Contains("Download size: Unknown"), "Custom unknown size is explicit in the download review");
        Check(download.Download is null, "Opening and editing the review never initiates a download");
        download.Close();
    }
}
