using System.Text.Json;
using System.Text.RegularExpressions;

namespace screen_translate.Models;

public enum ModelPurpose { Ocr, Translation }
public enum ModelInstallState { Missing, Checking, Discovered, Validated, Invalid, ReadError }
public sealed record ModelDetails(string Name, ModelPurpose Purpose, string Language, string Version,
    string Location, ModelInstallState State, long? DiskSize, string Source, string License,
    IReadOnlyList<string> Files, string Explanation = "")
{
    public override string ToString() => $"{Name} · {Language} · {State}";
    public string Describe() => $"Name: {Name}\r\nPurpose: {Purpose}\r\nLanguage / direction: {Language}\r\n" +
        $"Version: {Version}\r\nLocation: {Location}\r\nInstallation state: {State}\r\n" +
        $"Installed disk size: {Size(DiskSize)}\r\nSource: {Source}\r\nLicense: {License}\r\n{Explanation}";
    public static string Size(long? bytes) => bytes is long value ? $"{value:N0} bytes ({value / 1048576d:N2} MiB)" : "Unknown";
}

public sealed record ModelDownload(string Name, ModelPurpose Purpose, string FileName, Uri Source,
    string License, long? DownloadSize = null, string? Sha256 = null, string Version = "Unknown")
{
    public override string ToString() => Name;
    public string Describe() => $"{Name}\r\nPurpose: {Purpose}\r\nSource: {Source}\r\nLicense: {License}\r\n" +
        $"Download size: {ModelDetails.Size(DownloadSize)}\r\nInstalled disk size: determined after extraction\r\n" +
        $"SHA-256: {Sha256 ?? "Unknown — no trusted checksum supplied"}\r\nVersion: {Version}";
}

internal sealed record ModelReceipt(string Source, string License, string Version, string[] Files);

public static class ModelPackage
{
    internal const string Receipt = ".screen-translate.json";
    public static bool IsTransaction(string path) => Path.GetFileName(path).StartsWith(".st-", StringComparison.OrdinalIgnoreCase);
    public static bool IsLanguage(string code) => Regex.IsMatch(code, "^[a-zA-Z][a-zA-Z0-9_-]{0,39}$");

    // Reject Windows aliases, streams, links and traversal even when examining packages on another OS.
    public static string SafePath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.Contains(':') ||
            Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\\.)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Package contains an unsafe file name: " + relative);
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package entry would write outside the installation area.");
        NoLinks(full);
        return full;
    }

    public static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked files and folders are not supported for model management: " + current);
        }
    }

    internal static string Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : "Unknown";

    public static ModelDetails Inspect(ModelPurpose purpose, string location)
    {
        string name = Path.GetFileName(location);
        string language = purpose == ModelPurpose.Ocr ? Path.GetFileNameWithoutExtension(location) : "Unknown";
        var files = new List<string>();
        string version = "Unknown", source = "Unknown", license = "Unknown";
        try
        {
            NoLinks(location);
            if (purpose == ModelPurpose.Ocr)
            {
                if (!File.Exists(location)) return new(name, purpose, language, version, location, ModelInstallState.Missing, 0, source, license, []);
                if (!IsLanguage(language) || !Ocr.OcrLanguageCatalog.IsSourceCode(language))
                    throw new InvalidDataException("Unsupported OCR language filename.");
                files.Add(location);
                RequireFile(location);
            }
            else
            {
                if (!Directory.Exists(location)) return new(name, purpose, language, version, location, ModelInstallState.Missing, 0, source, license, []);
                // External packages own only identified format files, never every file under a user folder.
                foreach (string relative in CoreFiles)
                {
                    string candidate = SafePath(location, relative);
                    if (File.Exists(candidate)) files.Add(candidate);
                }
                using var metadata = JsonDocument.Parse(ReadLimitedText(Path.Combine(location, "metadata.json"), 1024 * 1024));
                string from = Text(metadata.RootElement, "from_code"), to = Text(metadata.RootElement, "to_code");
                if (from == "Unknown" || to == "Unknown" || !IsLanguage(from) || !IsLanguage(to) ||
                    (metadata.RootElement.TryGetProperty("type", out var type) && type.GetString() != "translate"))
                    throw new InvalidDataException("Unsupported direct-pair translation metadata.");
                language = from.ToLowerInvariant() + " → " + to.ToLowerInvariant();
                name = Text(metadata.RootElement, "name");
                if (name == "Unknown") name = Path.GetFileName(location);
                version = Text(metadata.RootElement, "package_version");
                source = Text(metadata.RootElement, "source");
                license = Text(metadata.RootElement, "license");
                foreach (string required in new[] { "metadata.json", "model/model.bin", "model/config.json" }) RequireFile(SafePath(location, required));
                RequireJson(Path.Combine(location, "model/config.json"));
                string shared = Path.Combine(location, "model/shared_vocabulary.json");
                if (File.Exists(shared)) RequireJson(shared);
                else
                {
                    RequireJson(Path.Combine(location, "model/source_vocabulary.json"));
                    RequireJson(Path.Combine(location, "model/target_vocabulary.json"));
                }
                string tokenizer = Path.Combine(location, "sentencepiece.model");
                RequireFile(File.Exists(tokenizer) ? tokenizer : Path.Combine(location, "bpe.model"));
            }
            string receiptPath = purpose == ModelPurpose.Ocr ? location + Receipt : Path.Combine(location, Receipt);
            if (File.Exists(receiptPath))
            {
                NoLinks(receiptPath);
                var receipt = JsonSerializer.Deserialize<ModelReceipt>(ReadLimitedText(receiptPath, 16 * 1024 * 1024))
                    ?? throw new InvalidDataException("Unreadable model receipt.");
                if (receipt.Files is null) throw new InvalidDataException("Incomplete model receipt.");
                if (purpose == ModelPurpose.Translation)
                    foreach (string relative in receipt.Files)
                    {
                        string owned = SafePath(location, relative);
                        // Optional archived files may be empty; only required engine data must be nonempty.
                        using var readable = new FileStream(owned, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!files.Contains(owned, StringComparer.OrdinalIgnoreCase)) files.Add(owned);
                    }
                source = string.IsNullOrWhiteSpace(receipt.Source) ? "Unknown" : receipt.Source;
                license = string.IsNullOrWhiteSpace(receipt.License) ? "Unknown" : receipt.License;
                if (receipt.Version != "Unknown" && !string.IsNullOrWhiteSpace(receipt.Version)) version = receipt.Version;
                files.Add(receiptPath);
            }
            return new(name, purpose, language, version, location, ModelInstallState.Discovered,
                files.Sum(p => new FileInfo(p).Length), source, license, files,
                purpose == ModelPurpose.Ocr ? "Local files discovered. Use Validate to load with Tesseract." :
                "Package files discovered; engine compatibility is unvalidated. This build has no production translation runtime.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(name, purpose, language, version, location,
                error is UnauthorizedAccessException ? ModelInstallState.ReadError : ModelInstallState.Invalid,
                null, source, license, files, error.Message);
        }
    }

    internal static readonly string[] CoreFiles = ["metadata.json", "sentencepiece.model", "bpe.model", "model/model.bin",
        "model/config.json", "model/shared_vocabulary.json", "model/source_vocabulary.json", "model/target_vocabulary.json",
        "README", "README.md", "LICENSE", "LICENSE.txt"];
    private static void RequireFile(string path)
    {
        NoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0) throw new InvalidDataException("Required model file is empty: " + Path.GetFileName(path));
    }
    private static void RequireJson(string path)
    {
        RequireFile(path);
        using var json = JsonDocument.Parse(ReadLimitedText(path, 64 * 1024 * 1024));
        if (json.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("Invalid model JSON: " + Path.GetFileName(path));
    }

    private static string ReadLimitedText(string path, int limit)
    {
        // Hold the same handle while measuring and reading, so a concurrent writer cannot grow the allocation.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new InvalidDataException("Model JSON exceeds its supported size limit: " + Path.GetFileName(path));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
