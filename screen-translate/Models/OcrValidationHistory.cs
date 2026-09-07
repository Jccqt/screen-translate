using System.Collections.Concurrent;
using screen_translate.Ocr;

namespace screen_translate.Models;

/// <summary>Session-wide load failures shared by the main window and its model managers.</summary>
public sealed class OcrValidationHistory
{
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);
    public string? Failure(string location) => _failures.GetValueOrDefault(Path.GetFullPath(location));
    public void Loaded(string location) => _failures.TryRemove(Path.GetFullPath(location), out _);

    public Task ValidateAsync(IOcrEngine engine, string directory, string code, CancellationToken token) =>
        ModelUse.RunAsync(directory, async () =>
        {
            string location = Path.GetFullPath(Path.Combine(directory, code + ".traineddata"));
            try
            {
                await engine.ValidateLanguageAsync(directory, code, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Loaded(location);
            }
            catch (OcrModelLoadException error) { _failures[location] = error.Message; throw; }
            // A busy lease or cancelled wait is not evidence that model data is invalid.
        });
}
