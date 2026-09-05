using System.Globalization;

namespace screen_translate.Ocr;

public sealed record OcrLanguage(string Code, string DisplayName)
{
    public override string ToString() => $"{DisplayName} ({Code})";
}

public sealed record OcrLanguageScan(IReadOnlyList<OcrLanguage> Languages, string? Error = null);

/// <summary>Discovers locally installed data. Engine compatibility is checked when OCR loads a model.</summary>
public sealed class OcrLanguageCatalog
{
    private static readonly Dictionary<string, string> Names = CreateNames();

    public OcrLanguageScan Scan(string dataDirectory)
    {
        try
        {
            // Enumerate directly: Directory.Exists would conceal access errors as a missing folder.
            var languages = new List<OcrLanguage>();
            foreach (string path in Directory.EnumerateFiles(dataDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(Path.GetExtension(path), ".traineddata", StringComparison.OrdinalIgnoreCase))
                    continue;

                string code = Path.GetFileNameWithoutExtension(path);
                // OSD and equation data are auxiliary models, not source languages.
                if (code.Equals("osd", StringComparison.OrdinalIgnoreCase) ||
                    code.Equals("equ", StringComparison.OrdinalIgnoreCase) ||
                    code.Length == 0 || code.Contains('+'))
                    continue;

                try
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (file.Length == 0) continue;
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                languages.Add(new OcrLanguage(code, Names.GetValueOrDefault(code, code)));
            }

            return new OcrLanguageScan(languages
                .DistinctBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(language => language.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(language => language.Code, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (DirectoryNotFoundException) { return new OcrLanguageScan([]); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new OcrLanguageScan([], "Cannot read the OCR data folder. Choose an accessible local folder and refresh.");
        }
    }

    public static OcrLanguage? ResolveSelection(IReadOnlyList<OcrLanguage> languages, string? savedCode) =>
        languages.FirstOrDefault(language => string.Equals(language.Code, savedCode, StringComparison.OrdinalIgnoreCase))
        ?? languages.FirstOrDefault();

    private static Dictionary<string, string> CreateNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            names.TryAdd(culture.ThreeLetterISOLanguageName, culture.EnglishName);

        names["chi_sim"] = "Chinese (Simplified)";
        names["chi_tra"] = "Chinese (Traditional)";
        names["chi_sim_vert"] = "Chinese (Simplified, vertical)";
        names["chi_tra_vert"] = "Chinese (Traditional, vertical)";
        names["jpn_vert"] = "Japanese (vertical)";
        names["kor_vert"] = "Korean (vertical)";
        names["fil"] = "Filipino";
        return names;
    }
}
