using System.Text.Json;

namespace screen_translate.Translation;

public sealed record TranslationModel(string SourceCode, string TargetCode, string Directory);
public sealed record TranslationModelScan(IReadOnlyList<TranslationModel> Models, string? Error = null, int IgnoredPackages = 0);

public interface ITranslationModelCatalog
{
    TranslationModelScan Scan(string directory);
}

/// <summary>Discovers extracted, direct-pair Argos packages. The engine must validate models when loading.</summary>
public sealed class ArgosTranslationModelCatalog : ITranslationModelCatalog
{
    public TranslationModelScan Scan(string directory)
    {
        var models = new List<TranslationModel>();
        int ignored = 0;
        try
        {
            foreach (string package in System.IO.Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "metadata.json")));
                    var root = metadata.RootElement;
                    string? source = root.GetProperty("from_code").GetString();
                    string? target = root.GetProperty("to_code").GetString();
                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) ||
                        (root.TryGetProperty("type", out var type) && type.GetString() != "translate") ||
                        !ReadableFile(Path.Combine(package, "model", "model.bin")) ||
                        !ReadableFile(Path.Combine(package, "model", "config.json")) ||
                        !(ReadableFile(Path.Combine(package, "model", "shared_vocabulary.json")) ||
                          (ReadableFile(Path.Combine(package, "model", "source_vocabulary.json")) &&
                           ReadableFile(Path.Combine(package, "model", "target_vocabulary.json")))) ||
                        !(ReadableFile(Path.Combine(package, "sentencepiece.model")) || ReadableFile(Path.Combine(package, "bpe.model"))))
                    {
                        ignored++;
                        continue;
                    }
                    models.Add(new TranslationModel(source.ToLowerInvariant(), target.ToLowerInvariant(), package));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                    InvalidOperationException or KeyNotFoundException)
                {
                    ignored++;
                }
            }
            return new(models, IgnoredPackages: ignored);
        }
        catch (DirectoryNotFoundException) { return new([]); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new([], "Cannot read the translation model folder. Choose an accessible local folder and refresh.");
        }
    }

    private static bool ReadableFile(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return file.Length > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

public enum TranslationModelState { SourceRequired, UnsupportedSource, NotRequired, Missing, Installed, ReadError }

public sealed record TranslationModelAvailability(TranslationModelState State, TranslationModel? Model = null)
{
    public static TranslationModelAvailability Evaluate(string? ocrCode, string targetCode, TranslationModelScan scan)
    {
        if (ocrCode is null) return new(TranslationModelState.SourceRequired);
        string? sourceCode = TranslationLanguage.FromOcrCode(ocrCode);
        if (sourceCode is null) return new(TranslationModelState.UnsupportedSource);
        if (string.Equals(sourceCode, targetCode, StringComparison.OrdinalIgnoreCase)) return new(TranslationModelState.NotRequired);
        if (scan.Error is not null) return new(TranslationModelState.ReadError);
        var model = scan.Models.FirstOrDefault(model =>
            string.Equals(model.SourceCode, sourceCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(model.TargetCode, targetCode, StringComparison.OrdinalIgnoreCase));
        return model is null ? new(TranslationModelState.Missing) : new(TranslationModelState.Installed, model);
    }
}
