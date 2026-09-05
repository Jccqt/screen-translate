using System.Globalization;

namespace screen_translate.Translation;

public sealed record TranslationLanguage(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;

    // Output choices are independent of local installation state.
    public static IReadOnlyList<TranslationLanguage> All { get; } = Array.AsReadOnly(new[]
    {
        new TranslationLanguage("zh", "Chinese (Simplified)"),
        new TranslationLanguage("en", "English"),
        new TranslationLanguage("fil", "Filipino"),
        new TranslationLanguage("fr", "French"),
        new TranslationLanguage("de", "German"),
        new TranslationLanguage("ja", "Japanese"),
        new TranslationLanguage("ko", "Korean"),
        new TranslationLanguage("es", "Spanish")
    });

    public static TranslationLanguage Resolve(string? code) =>
        All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? All.Single(language => language.Code == "es");

    public static string? FromOcrCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.ToLowerInvariant();
        // Tesseract uses its own codes for Chinese and vertical text models.
        if (code is "chi_sim" or "chi_sim_vert") return "zh";
        if (code is "chi_tra" or "chi_tra_vert") return "zt";
        if (code == "jpn_vert") return "ja";
        if (code == "kor_vert") return "ko";
        if (code == "fil") return "fil";
        return CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .FirstOrDefault(culture => culture.ThreeLetterISOLanguageName == code)?.TwoLetterISOLanguageName;
    }
}
