namespace screen_translate.Ocr;

public sealed record OcrTextRegion(string Text, Rectangle Bounds, float? Confidence = null);

/// <summary>In-memory recognition output, with the exact OCR language used for this capture.</summary>
public sealed record OcrResult(string Text, string SourceLanguageCode, IReadOnlyList<OcrTextRegion> Regions);
