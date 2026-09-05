using screen_translate.Translation;

namespace screen_translate.Interface;

public enum ReadinessState { Checking, ActionRequired, Ready }

/// <summary>Overall readiness is stricter than discovery of local model files.</summary>
public sealed record TranslationReadiness(ReadinessState State, string Reason)
{
    public static TranslationReadiness Evaluate(bool checking, string? sourceError, string? sourceCode,
        string targetCode, TranslationModelScan scan, string? runtimeError, string? shortcutError)
    {
        if (checking) return new(ReadinessState.Checking, "Checking the configured OCR language and local translation model.");
        if (sourceError is not null) return new(ReadinessState.ActionRequired, sourceError);
        var availability = TranslationModelAvailability.Evaluate(sourceCode, targetCode, scan);
        string pair = $"{TranslationLanguage.FromOcrCode(sourceCode)} → {targetCode}";
        string? issue = availability.State switch
        {
            TranslationModelState.SourceRequired => "Choose an OCR folder with a readable .traineddata language file, then refresh languages.",
            TranslationModelState.UnsupportedSource => "Choose an OCR source language with a supported translation language code.",
            TranslationModelState.ReadError => scan.Error,
            TranslationModelState.Missing => $"Install a complete, readable offline translation package for {pair} and refresh models.",
            _ => null
        };
        issue ??= runtimeError ?? shortcutError;
        return issue is not null ? new(ReadinessState.ActionRequired, issue) : new(ReadinessState.Ready,
            availability.State == TranslationModelState.NotRequired
                ? $"{pair}: OCR is ready; no translation model is required for identical languages."
                : $"{pair}: OCR and translation models are validated and the shortcut is available.");
    }
}
