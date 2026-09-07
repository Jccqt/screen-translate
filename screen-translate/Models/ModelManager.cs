using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using screen_translate.Ocr;

namespace screen_translate.Models;

public sealed record ModelTransferProgress(long Bytes, long? Total, string Phase);

/// <summary>All writes are staged on the destination volume. Publication is the last, non-cancellable step.</summary>
public sealed class ModelManager(IOcrEngine? ocrEngine = null, HttpClient? httpClient = null,
    OcrValidationHistory? validationHistory = null)
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(30) };
    private readonly IOcrEngine _ocr = ocrEngine ?? new TesseractOcrEngine();
    public OcrValidationHistory ValidationHistory { get; } = validationHistory ?? new();

    public Task ValidateOcrAsync(string root, string code, CancellationToken token) =>
        ValidationHistory.ValidateAsync(_ocr, root, code, token);
    private const long MaxBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxEntries = 20000;

    public IReadOnlyList<ModelDetails> Scan(ModelPurpose purpose, string root, CancellationToken token = default)
    {
        using var lease = ModelUse.AcquireRead(root);
        ModelPackage.NoLinks(root);
        try
        {
            var paths = purpose == ModelPurpose.Ocr ? Directory.EnumerateFiles(root, "*.traineddata")
                .Concat(Directory.EnumerateFiles(root, "*.traineddata" + ModelPackage.Receipt).Select(p => p[..^ModelPackage.Receipt.Length]))
                .Distinct(StringComparer.OrdinalIgnoreCase) : Directory.EnumerateDirectories(root);
            return paths.Where(p => !ModelPackage.IsTransaction(p)).Order(StringComparer.OrdinalIgnoreCase)
                .Select(p => { token.ThrowIfCancellationRequested(); return ModelPackage.Inspect(purpose, p); }).ToArray();
        }
        catch (DirectoryNotFoundException) { return []; }
    }

    public async Task<ModelDetails> ImportAsync(ModelPurpose purpose, string package, string root,
        bool replace, CancellationToken token = default, IProgress<ModelTransferProgress>? progress = null)
    {
        using var lease = ModelUse.Acquire(root);
        string stage = CreateStage(root);
        try { return await StageAndInstall(purpose, package, root, stage, replace, null, token, progress).ConfigureAwait(false); }
        finally { CleanStage(stage); }
    }

    public async Task<ModelDetails> DownloadAsync(ModelDownload download, string root, bool replace,
        CancellationToken token = default, IProgress<ModelTransferProgress>? progress = null)
    {
        if (download.Source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(download.Source.UserInfo))
            throw new InvalidDataException("Model downloads require an HTTPS source without embedded credentials.");
        if (download.Sha256 is not null && (download.Sha256.Length != 64 || !download.Sha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("A published SHA-256 checksum must contain 64 hexadecimal characters.");
        using var lease = ModelUse.Acquire(root);
        string stage = CreateStage(root);
        try
        {
            string archive = ModelPackage.SafePath(stage, download.FileName);
            // Do not follow redirects to a source the user did not review.
            using var response = await (httpClient ?? Client).GetAsync(download.Source,
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength ?? download.DownloadSize;
            if (total > MaxBytes) throw new InvalidDataException("Download exceeds the supported 8 GiB limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                long count = await Copy(input, output, total, "Downloading", token, progress).ConfigureAwait(false);
                if ((total is long size && count != size) || (download.DownloadSize is long expected && count != expected))
                    throw new InvalidDataException("Download is incomplete or its size differs from the published size.");
            }
            if (download.Sha256 is string hash)
            {
                progress?.Report(new(0, null, "Checking SHA-256"));
                await using var file = File.OpenRead(archive);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Published SHA-256 checksum does not match. The download was not installed.");
            }
            return await StageAndInstall(download.Purpose, archive, root, stage, replace, download, token, progress).ConfigureAwait(false);
        }
        finally { CleanStage(stage); }
    }

    private async Task<ModelDetails> StageAndInstall(ModelPurpose purpose, string package, string root, string stage,
        bool replace, ModelDownload? download, CancellationToken token, IProgress<ModelTransferProgress>? progress)
    {
        token.ThrowIfCancellationRequested();
        ModelPackage.NoLinks(package);
        string payload = Directory.CreateDirectory(Path.Combine(stage, "payload")).FullName;
        string location;
        if (purpose == ModelPurpose.Ocr)
        {
            string code = Path.GetFileNameWithoutExtension(package);
            if (!Path.GetExtension(package).Equals(".traineddata", StringComparison.OrdinalIgnoreCase) ||
                !ModelPackage.IsLanguage(code) || !OcrLanguageCatalog.IsSourceCode(code))
                throw new InvalidDataException("Import one source-language .traineddata file; auxiliary OSD/equation data is unsupported.");
            location = ModelPackage.SafePath(payload, code + ".traineddata");
            await using (var input = File.OpenRead(package))
            await using (var output = File.Create(location))
                await Copy(input, output, input.Length, "Importing OCR data", token, progress).ConfigureAwait(false);
            progress?.Report(new(0, null, "Checking OCR data with Tesseract"));
            // Await actual native completion before touching staged files, even on cancellation.
            await _ocr.ValidateLanguageAsync(payload, code, token).ConfigureAwait(false);
        }
        else
        {
            if (!new[] { ".argosmodel", ".zip" }.Contains(Path.GetExtension(package), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Translation imports require a .argosmodel or .zip direct-pair package.");
            await Extract(package, payload, token, progress).ConfigureAwait(false);
            var directories = Directory.GetDirectories(payload);
            if (File.Exists(Path.Combine(payload, "metadata.json"))) location = payload;
            else if (directories.Length == 1 && Directory.GetFiles(payload).Length == 0) location = directories[0];
            else throw new InvalidDataException("The archive must contain exactly one translation package.");
            var details = ModelPackage.Inspect(purpose, location);
            if (details.State != ModelInstallState.Discovered) throw new InvalidDataException(details.Explanation);
        }
        var staged = ModelPackage.Inspect(purpose, location);
        if (staged.State != ModelInstallState.Discovered) throw new InvalidDataException(staged.Explanation);
        string[] owned = purpose == ModelPurpose.Ocr ? [Path.GetFileName(location)] :
            EnumerateSafeFiles(location).Select(p => Path.GetRelativePath(location, p)).ToArray();
        var receipt = new ModelReceipt(download?.Source.ToString() ?? staged.Source,
            download?.License ?? staged.License, download?.Version ?? staged.Version, owned, staged.Name, staged.Language);
        string receiptPath = purpose == ModelPurpose.Ocr ? location + ModelPackage.Receipt : Path.Combine(location, ModelPackage.Receipt);
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt), token).ConfigureAwait(false);
        string destination = ModelPackage.SafePath(root, purpose == ModelPurpose.Ocr ? Path.GetFileName(location) :
            "translate-" + staged.Language.Replace(" → ", "_"));
        progress?.Report(new(0, null, "Installing local files"));
        token.ThrowIfCancellationRequested();
        Publish(purpose, location, destination, stage, replace);
        if (purpose == ModelPurpose.Ocr) ValidationHistory.Loaded(destination);
        return ModelPackage.Inspect(purpose, destination);
    }

    private static void Publish(ModelPurpose purpose, string source, string destination, string stage, bool replace)
    {
        // If rollback itself fails (e.g. disk disconnection), keep backups for recovery.
        string recovery = Path.Combine(stage, "preserve-backups");
        ModelPackage.NoLinks(destination);
        if (purpose == ModelPurpose.Translation)
        {
            string backup = Path.Combine(stage, "previous");
            bool exists = Directory.Exists(destination);
            if (File.Exists(destination)) throw new IOException("A file already occupies the installation location.");
            if (exists)
            {
                if (!replace) throw new IOException("This model already exists. Select Replace existing model and retry.");
                var previous = ModelPackage.Inspect(purpose, destination);
                // Never move or delete unrelated files just because they share a package folder.
                var actual = EnumerateSafeFiles(destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!actual.SetEquals(previous.Files))
                    throw new IOException("Replacement is blocked because the package folder contains unidentified files. Import into a different storage folder.");
                File.WriteAllText(recovery, "Do not delete: a model transaction may need recovery.");
                try { Directory.Move(destination, backup); }
                catch { File.Delete(recovery); throw; }
            }
            try { Directory.Move(source, destination); }
            catch { if (exists) Directory.Move(backup, destination); File.Delete(recovery); throw; }
        }
        else
        {
            File.WriteAllText(recovery, "Do not delete: a model transaction may need recovery.");
            var moves = new List<(string Original, string Backup)>();
            var published = new List<string>();
            try
            {
                foreach (string path in new[] { destination, destination + ModelPackage.Receipt })
                {
                    ModelPackage.NoLinks(path);
                    if (Directory.Exists(path)) throw new IOException("A folder occupies an OCR installation file location.");
                    if (!File.Exists(path)) continue;
                    if (!replace) throw new IOException("This OCR model already exists. Select Replace existing model and retry.");
                    string backup = Path.Combine(stage, "previous-" + moves.Count);
                    File.Move(path, backup);
                    moves.Add((path, backup));
                }
                File.Move(source, destination);
                published.Add(destination);
                File.Move(source + ModelPackage.Receipt, destination + ModelPackage.Receipt);
                published.Add(destination + ModelPackage.Receipt);
            }
            catch
            {
                foreach (string path in published) File.Delete(path);
                foreach (var move in moves.AsEnumerable().Reverse()) File.Move(move.Backup, move.Original);
                File.Delete(recovery);
                throw;
            }
        }
        File.Delete(recovery);
    }

    public void Remove(ModelDetails selected, string root)
    {
        using var lease = ModelUse.Acquire(root);
        string expected = ModelPackage.SafePath(root, Path.GetFileName(selected.Location));
        if (!expected.Equals(Path.GetFullPath(selected.Location), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected model is outside this storage folder.");
        var current = ModelPackage.Inspect(selected.Purpose, expected);
        if (!current.Files.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected.Files))
            throw new IOException("The model's identified files changed. Refresh and review it before removal.");
        foreach (string file in current.Files)
            if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
                throw new IOException("Removal is blocked by a read-only model file: " + file + ". Clear its read-only attribute and retry.");
        string stage = CreateStage(root);
        string recovery = Path.Combine(stage, "preserve-backups");
        File.WriteAllText(recovery, "Do not delete: a removal transaction may need recovery.");
        var moved = new List<(string Original, string Backup)>();
        try
        {
            foreach (string file in current.Files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ModelPackage.NoLinks(file);
                string relative = Path.GetRelativePath(root, file);
                ModelPackage.SafePath(root, relative);
                string backup = Path.Combine(stage, "removed-" + moved.Count);
                File.Move(file, backup);
                moved.Add((file, backup));
            }
            File.Delete(recovery);
        }
        catch
        {
            foreach (var item in moved.AsEnumerable().Reverse()) File.Move(item.Backup, item.Original);
            File.Delete(recovery);
            throw;
        }
        finally { CleanStage(stage, reportFailure: true); }
        // Deliberately no recursive deletion of the package or user-selected folder.
        if (selected.Purpose == ModelPurpose.Translation)
        {
            var knownDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { expected };
            foreach (string file in current.Files)
                for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    knownDirectories.Add(directory);
                    if (directory.Equals(expected, StringComparison.OrdinalIgnoreCase)) break;
                }
            foreach (string directory in knownDirectories.OrderByDescending(p => p.Length))
            {
                ModelPackage.NoLinks(directory);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
        }
    }

    private static string CreateStage(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new InvalidDataException("Choose an absolute local storage folder.");
        ModelPackage.NoLinks(root);
        Directory.CreateDirectory(root);
        return Directory.CreateDirectory(Path.Combine(root, ".st-" + Guid.NewGuid().ToString("N"))).FullName;
    }

    private static void CleanStage(string stage, bool reportFailure = false)
    {
        // Only fresh, private transaction directories; never a configured root or selected package.
        if (!ModelPackage.IsTransaction(stage) || !Directory.Exists(stage) || File.Exists(Path.Combine(stage, "preserve-backups"))) return;
        try
        {
            foreach (string file in EnumerateSafeFiles(stage)) File.Delete(file);
            foreach (string dir in Directory.GetDirectories(stage, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length)) Directory.Delete(dir);
            Directory.Delete(stage);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Preserve remaining data and identify the exact private directory for recovery.
            if (reportFailure) throw new IOException("Model removal did not finish cleaning up its files. Remaining data is in " + stage +
                ". Resolve the file access problem before removing this transaction directory. " + error.Message, error);
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        ModelPackage.NoLinks(root);
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            ModelPackage.NoLinks(entry);
            if (Directory.Exists(entry))
                foreach (string file in EnumerateSafeFiles(entry)) yield return file;
            else yield return entry;
        }
    }

    private static async Task Extract(string archivePath, string root, CancellationToken token, IProgress<ModelTransferProgress>? progress)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > MaxEntries || zip.Entries.Any(e => e.Length < 0 || e.Length > MaxBytes) || zip.Entries.Sum(e => e.Length) > MaxBytes)
            throw new InvalidDataException("Package exceeds the supported extraction limits (8 GiB / 20,000 entries).");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            bool directory = relative.EndsWith('/');
            string path = ModelPackage.SafePath(root, directory ? relative[..^1] : relative);
            if ((entry.ExternalAttributes & 0x400) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                Path.GetFileName(path).Equals(ModelPackage.Receipt, StringComparison.OrdinalIgnoreCase) ||
                relative.Split('/').Any(ModelPackage.IsTransaction) || !seen.Add(path))
                throw new InvalidDataException("Package contains a link, reserved file or duplicate entry.");
            if (directory) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            long count = await Copy(input, output, entry.Length, "Extracting " + entry.Name, token, progress).ConfigureAwait(false);
            if (count != entry.Length) throw new InvalidDataException("Incomplete archive entry.");
        }
    }

    private static async Task<long> Copy(Stream input, Stream output, long? total, string phase,
        CancellationToken token, IProgress<ModelTransferProgress>? progress)
    {
        byte[] buffer = new byte[81920];
        long count = 0;
        long nextReport = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            count += read;
            if (count > MaxBytes || (total is long expected && count > expected)) throw new InvalidDataException("Model data exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (Environment.TickCount64 >= nextReport)
            {
                progress?.Report(new(count, total, phase));
                nextReport = Environment.TickCount64 + 100;
            }
        }
        progress?.Report(new(count, total, phase));
        return count;
    }
}
