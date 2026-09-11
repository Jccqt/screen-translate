using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Translation;

namespace screen_translate;

public partial class MainForm
{
    private readonly ITranslationWorkflow? _translationWorkflow;
    private readonly TranslationRequestGate _translationRequests = new();
    private object? _activeTranslation;
    private string? _translationFailure;
    private string? WorkflowUnavailableReason => _translationWorkflow is null ? RuntimeUnavailable
        : SelectedSourceLanguageCode is null ? "Choose an installed OCR source language."
        : _translationWorkflow.GetUnavailableReason(CurrentTranslationRequest());

    private TranslationRequest CurrentTranslationRequest() => new(SelectedSourceLanguageCode!, SelectedTargetLanguageCode,
        OcrDataDirectory, TranslationModelDirectory);

    private async Task HandleTranslationShortcutAsync()
    {
        // Check before readiness or activation: an existing selection/progress window keeps focus and its operation.
        if (_lifetime.IsStopped || _translationRequests.IsBusy) return;
        _translationFailure = null;
        try
        {
            if (Readiness.State != ReadinessState.Ready || _translationWorkflow is null)
            {
                ShowTranslationSetup();
                return;
            }
            var request = CurrentTranslationRequest();
            await RunTranslationRequestAsync((progress, token) => _translationWorkflow.RunAsync(this, request, progress, token));
        }
        catch (OperationCanceledException) when (WorkCancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_lifetime.IsStopped) return;
            _translationFailure = "Could not complete screen translation: " + error.Message;
            ShowTranslationSetup();
        }
    }

    private void ShowTranslationSetup()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        NavigateTo("TranslationReadiness");
        UpdateReadiness();
    }

    /// <summary>Capture/OCR integration point. The result carries the source used for recognition.</summary>
    public async Task ShowOcrResultAsync(OcrResult recognized, ITranslationEngine? engine = null)
    {
        if (_lifetime.IsStopped) return;
        string target = SelectedTargetLanguageCode;
        string directory = TranslationModelDirectory;
        await RunTranslationRequestAsync(async (progress, token) =>
        {
            progress.Report(TranslationStage.Translating);
            return await new TranslationProcessor(_translationCatalog, engine).ProcessAsync(recognized, target, directory, token);
        });
    }

    private Task RunTranslationRequestAsync(Func<IProgress<TranslationStage>, CancellationToken, Task<TranslationResult?>> run) =>
        _translationRequests.RunAsync(async () =>
        {
            if (_lifetime.IsStopped) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(WorkCancellationToken);
            object operation = new();
            _activeTranslation = operation;
            TranslationProgressForm? feedback = null;
            var progress = new Progress<TranslationStage>(stage =>
            {
                if (_lifetime.IsStopped || _activeTranslation != operation) return;
                // Selection feedback belongs to the region selector; never cover it with a progress dialog.
                if (stage == TranslationStage.Selecting) { feedback?.Hide(); return; }
                feedback ??= new TranslationProgressForm(() =>
                {
                    cancellation.Cancel();
                    feedback?.SetStatus("Cancelling...");
                });
                feedback.SetStatus(cancellation.IsCancellationRequested ? "Cancelling..."
                    : stage == TranslationStage.Recognizing ? "Recognizing text..." : "Translating text...");
                if (!feedback.Visible) ShowTranslationWindow(feedback);
            });
            try
            {
                foreach (var previous in OwnedForms.OfType<TranslationResultForm>()) previous.Dispose();
                var result = await run(progress, cancellation.Token);
                if (_lifetime.IsStopped || cancellation.IsCancellationRequested || result is null) return;
                feedback?.Dispose();
                ShowTranslationWindow(new TranslationResultForm(result));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                _activeTranslation = null;
                feedback?.Dispose();
            }
        });
}
