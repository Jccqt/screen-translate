using System.Reflection;
using screen_translate;
using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;

internal static partial class Program
{
    private static void TestShortcutValidation()
    {
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Alt, Keys.Control | Keys.Shift })
            Check(InterfaceSettings.ValidateShortcut(modifiers | Keys.Escape)?.Contains("reserved") == true,
                "Esc remains reserved with every modifier combination");
        Check(InterfaceSettings.ValidateShortcut(Keys.Control | Keys.F12)?.Contains("Windows") == true,
            "F12 is rejected before Windows registration");
        foreach (Keys key in new[] { Keys.None, Keys.ControlKey, Keys.LWin, Keys.Insert, Keys.NumPad1, Keys.OemQuestion })
            Check(!InterfaceSettings.IsValidShortcut(Keys.Control | key), "Unsupported key is rejected: " + key);
        Check(!InterfaceSettings.IsValidShortcut(unchecked((Keys)0x80020041)), "Unknown modifier bits are rejected");
        foreach (Keys key in new[] { Keys.A, Keys.Z, Keys.D0, Keys.D9, Keys.F1, Keys.F11, Keys.F13, Keys.F24 })
            Check(InterfaceSettings.IsValidShortcut(Keys.Control | Keys.Alt | Keys.Shift | key), "Supported key: " + key);

        using var first = new GlobalShortcut();
        using var second = new GlobalShortcut();
        Keys RegisterAvailable(GlobalShortcut owner, params Keys[] keys)
        {
            foreach (var key in keys)
            {
                var chord = Keys.Control | Keys.Alt | Keys.Shift | key;
                if (owner.TrySet(chord) is null) return chord;
            }
            throw new InvalidOperationException("No native fixture shortcut available");
        }
        var original = RegisterAvailable(first, Keys.F6, Keys.F7, Keys.F8);
        var conflict = RegisterAvailable(second, Keys.F9, Keys.F10, Keys.F11);
        Check(first.TrySet(conflict) is not null && second.TrySet(original) is not null,
            "Failed native replacement preserves the working registration");
        Check(first.TrySet(original) is null, "Applying the current native registration is idempotent");
        second.Dispose();
        int saves = 0;
        Check(first.TrySet(Keys.Control | Keys.Escape, () => { saves++; return null; }) is not null && saves == 0,
            "Invalid shortcuts never attempt persistence");
        Check(first.TrySet(conflict, () => { saves++; return "disk fixture failure"; }) == "disk fixture failure" && saves == 1,
            "Save failure rolls back a newly registered shortcut");
        using (var rollbackProbe = new GlobalShortcut())
        {
            Check(rollbackProbe.TrySet(original) is not null && rollbackProbe.TrySet(conflict) is null,
                "After save rollback the old shortcut stays registered and the candidate is released");
        }
        using (var conflictProbe = new GlobalShortcut())
        {
            conflictProbe.TrySet(conflict);
            Check(first.TrySet(conflict, () => { saves++; return null; }) is not null && saves == 1,
                "Conflicting shortcuts never attempt persistence");
        }
        Check(first.TrySet(conflict) is null, "A released conflicting shortcut can be applied");
        using var probe = new GlobalShortcut();
        Check(probe.TrySet(original) is null, "Successful replacement releases the old native shortcut");
        Keys latest = RegisterAvailable(first, Keys.F3, Keys.F4, Keys.F5);
        int deliveries = 0;
        first.Pressed += (_, _) => deliveries++;
        void Deliver(int id, Keys key, int modifiers = 7)
        {
            var message = Message.Create(first.Handle, 0x0312, id, ((int)(key & Keys.KeyCode) << 16) | modifiers);
            typeof(GlobalShortcut).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(first, [message]);
        }
        Deliver(1, original);
        Deliver(2, latest);
        Deliver(1, latest, 2);
        Check(deliveries == 0, "Queued hotkey messages with stale keys, IDs, or modifiers are ignored");
        Deliver(1, latest);
        Check(deliveries == 1, "The current native hotkey message is delivered once");
    }

    private static async Task TestTranslationRequestGate()
    {
        var gate = new TranslationRequestGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        Task active = gate.RunAsync(async () => { starts++; await release.Task; });
        for (int i = 0; i < 10; i++) await gate.RunAsync(() => { starts++; return Task.CompletedTask; });
        Check(gate.IsBusy && starts == 1, "Overlapping requests are dropped while the first request remains active");
        release.SetResult();
        await active;
        Check(!gate.IsBusy && starts == 1, "Completion does not replay dropped requests");
        try { await gate.RunAsync(() => throw new InvalidOperationException("fixture")); } catch (InvalidOperationException) { }
        Check(!gate.IsBusy, "Failure releases the request guard");
        try { await gate.RunAsync(() => Task.FromCanceled(new CancellationToken(true))); } catch (OperationCanceledException) { }
        await gate.RunAsync(() => { starts++; return Task.CompletedTask; });
        Check(starts == 2 && !gate.IsBusy, "Cancellation releases the guard for the next explicit request");
    }

    private static MainForm ShortcutFixture(IGlobalShortcut shortcut, InterfaceSettingsStore preferences,
        ITranslationWorkflow? workflow = null, ITranslationModelCatalog? catalog = null)
    {
        string id = Guid.NewGuid().ToString("N");
        string data = NewFolder("shortcut-ocr-" + id);
        Install(data, "eng");
        var source = new SourceLanguageSettingsStore(Path.Combine(Root, id + "-source.json"));
        source.Save(new(data, "eng"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, id + "-target.json"));
        target.Save(new(NewFolder("shortcut-models-" + id), "en"));
        return new MainForm(source, target, catalog, interfaceSettingsStore: preferences, globalShortcut: shortcut,
            translationWorkflow: workflow);
    }

    private static async Task TestGlobalShortcutUi(string artifacts)
    {
        var preferences = new InterfaceSettingsStore(Path.Combine(Root, "shortcut-prefs.json"));
        var saved = new InterfaceSettings(AppTheme.Light, Keys.Control | Keys.Alt | Keys.F8);
        preferences.Save(saved);
        var shortcut = new FakeShortcut { Conflict = true };
        var workflow = new ControlledWorkflow();
        using var main = ShortcutFixture(shortcut, preferences, workflow);
        main.Show();
        await main.RefreshSourceLanguagesAsync();
        await WaitForReadiness(main);
        var status = Find<Label>(main, "ShortcutStatus");
        var input = Find<TextBox>(main, "GlobalShortcut");
        var apply = Find<Button>(main, "ApplyShortcut");
        Check(main.Visible && input.Enabled && apply.Enabled && status.Visible && status.Text.Contains("not registered"),
            "Startup conflict is visible and leaves the shortcut editor available");
        Check(preferences.Load(out _) == saved, "Startup registration failure never rewrites preferences");
        Capture(main, artifacts, "shortcut-startup-conflict");
        void Press(Keys keys) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(input, [new KeyEventArgs(keys)]);
        Press(Keys.Control | Keys.F12);
        apply.PerformClick();
        Check(status.Text.Contains("reserved") && status.Text.Contains("No shortcut") && preferences.Load(out _) == saved,
            "Reserved key explains the rejection without claiming an active registration");
        Capture(main, artifacts, "shortcut-reserved-key");
        Press(Keys.Control | Keys.Escape);
        Check(status.Text.Contains("Esc is reserved") && input.Text == InterfaceSettings.FormatShortcut(saved.Shortcut),
            "Modified Escape cancels editing and explains its reserved role");
        shortcut.Conflict = false;
        Press(Keys.Control | Keys.Alt | Keys.F9);
        apply.PerformClick();
        Check(status.Text.StartsWith("Registered") && preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.F9),
            "A valid replacement recovers startup registration and persists");
        shortcut.Conflict = true;
        Press(Keys.Control | Keys.Alt | Keys.F10);
        apply.PerformClick();
        Check(status.Text.Contains("Still registered") && preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.F9),
            "Failed edit explains which shortcut remains active");
        ChangeDpi(main, 144);
        AssertLayout(main);
        Capture(main, artifacts, "shortcut-conflict-150-percent");
        ChangeDpi(main, 96);
        shortcut.Conflict = false;
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Controlled workflow becomes available (fixture only)");

        shortcut.Press();
        Check(workflow.Calls == 1 && workflow.Selection?.Visible == true, "Hotkey starts one visible selection fixture");
        var selection = workflow.Selection!;
        for (int i = 0; i < 4; i++) shortcut.Press();
        Check(workflow.Calls == 1 && workflow.Selection == selection && selection.Visible,
            "Repeated selection hotkeys keep the existing selection visible");
        workflow.Report(TranslationStage.Recognizing);
        await Until(() => main.OwnedForms.OfType<TranslationProgressForm>().Any(f => f.Visible), "OCR progress is visible");
        var progress = main.OwnedForms.OfType<TranslationProgressForm>().Single();
        for (int i = 0; i < 4; i++) shortcut.Press();
        Check(workflow.Calls == 1 && Find<Label>(progress, "OperationStatus").Text.Contains("Recognizing"),
            "Repeated OCR hotkeys leave the active operation and progress unchanged");
        workflow.Report(TranslationStage.Translating);
        await Until(() => Find<Label>(progress, "OperationStatus").Text.Contains("Translating"), "Translation progress is visible");
        for (int i = 0; i < 4; i++) shortcut.Press();
        await main.ShowOcrResultAsync(new OcrResult("Must not become a second result", "eng", []));
        Check(workflow.Calls == 1 && !main.OwnedForms.OfType<TranslationResultForm>().Any(),
            "Hotkey and direct OCR entry point share the same request guard");
        Capture(progress, artifacts, "shortcut-translation-progress");
        workflow.Complete();
        await Until(() => main.OwnedForms.OfType<TranslationResultForm>().Any(), "Completed workflow shows one result");
        var result = main.OwnedForms.OfType<TranslationResultForm>().Single();
        Check(workflow.Calls == 1 && result.TopMost && progress.IsDisposed, "Dropped presses are not replayed; result replaces progress");
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Configuration is ready for another selection");
        shortcut.Press();
        Check(result.IsDisposed && workflow.Calls == 2 && workflow.Selection?.Visible == true,
            "Hotkey dismisses the existing result and starts a new selection");
        workflow.CancelSelection();
        await Until(() => workflow.Selection is null, "Selection cancellation finishes");
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Ready after selection cancellation");
        shortcut.Press();
        Check(workflow.Calls == 3, "Cancellation permits a new explicit request");
        workflow.Report(TranslationStage.Recognizing);
        await Until(() => main.OwnedForms.OfType<TranslationProgressForm>().Any(), "Cancellation fixture shows progress");
        var cancelling = main.OwnedForms.OfType<TranslationProgressForm>().Single();
        Find<Button>(cancelling, "CancelTranslation").PerformClick();
        await Until(() => cancelling.IsDisposed, "Cancelling OCR releases feedback and request guard");
        Check(workflow.Token.IsCancellationRequested, "Cancel reaches the running workflow");
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Ready after OCR cancellation");
        shortcut.Press();
        workflow.Fail();
        await Until(() => Find<Label>(main, "TranslationReadiness").Text.Contains("fixture failure"), "Workflow failure is visible without crashing");
        await main.RefreshSourceLanguagesAsync();
        Check(Find<Label>(main, "TranslationReadiness").Text.Contains("fixture failure"), "Automatic setup refresh cannot erase a workflow failure");
        Check(main.Visible && input.Enabled, "Workflow errors leave settings usable");
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Ready after workflow failure");
        shortcut.Press();
        workflow.Selection!.Disposed += (_, _) => Check(main.Font.Height > 0,
            "Owner drawing resources remain usable while owned translation windows are disposed");
        main.Close();
        Check(workflow.Token.IsCancellationRequested, "Closing settings cancels the active shortcut workflow");

        using var unavailable = ShortcutFixture(new FakeShortcut(), preferences, new UnavailableWorkflow());
        unavailable.Show();
        await unavailable.RefreshSourceLanguagesAsync();
        await WaitForReadiness(unavailable);
        unavailable.ShowTranslationWindow(ThemeResult());
        var kept = unavailable.OwnedForms.OfType<TranslationResultForm>().Single();
        await (Task)typeof(MainForm).GetMethod("HandleTranslationShortcutAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(unavailable, null)!;
        await WaitForReadiness(unavailable);
        Check(!kept.IsDisposed && unavailable.Readiness.Reason.Contains("isn't available"),
            "Unavailable production workflow explains setup without destroying the current result");
        unavailable.Close();

        string blockedPath = Path.Combine(Root, "shortcut-blocked");
        File.WriteAllText(blockedPath, "fixture");
        using var unsaved = ShortcutFixture(new FakeShortcut(), new InterfaceSettingsStore(Path.Combine(blockedPath, "preferences.json")));
        unsaved.Show();
        await unsaved.RefreshSourceLanguagesAsync();
        await WaitForReadiness(unsaved);
        await Until(() => Find<Label>(unsaved, "ShortcutStatus").Text.StartsWith("Registered"),
            "Save-failure fixture waits for the startup registration event");
        var unsavedInput = Find<TextBox>(unsaved, "GlobalShortcut");
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(unsavedInput, [new KeyEventArgs(Keys.Control | Keys.Alt | Keys.Y)]);
        Find<Button>(unsaved, "ApplyShortcut").PerformClick();
        Capture(unsaved, artifacts, "shortcut-save-failure");
        Check(Find<Label>(unsaved, "ShortcutStatus").Text.Contains("Could not save") &&
            Find<Label>(unsaved, "ShortcutStatus").Text.Contains("Still registered: " + InterfaceSettings.FormatShortcut(InterfaceSettings.Default.Shortcut)),
            "Save failure explains that the previously working shortcut remains registered: " + Find<Label>(unsaved, "ShortcutStatus").Text);
        unsaved.Close();
        await TestCancelledTranslationUi(artifacts);
    }

    private static async Task TestCancelledTranslationUi(string artifacts)
    {
        var shortcut = new FakeShortcut();
        var workflow = new ControlledWorkflow();
        var catalog = new ResultCatalog { Result = new([new("en", "es", "engine fixture")]) };
        using var main = ShortcutFixture(shortcut, new InterfaceSettingsStore(Path.Combine(Root, "cancel-shortcut.json")), workflow, catalog);
        main.Show();
        Find<ComboBox>(main, "TargetLanguage").SelectedItem = TranslationLanguage.Resolve("es");
        await main.RefreshSourceLanguagesAsync();
        await Until(() => main.Readiness.State == ReadinessState.Ready, "Cancellation fixture is ready");
        var engine = new UncooperativeTranslationEngine();
        var recognized = new OcrResult("Cancellation fixture", "eng", []);
        Task active = main.ShowOcrResultAsync(recognized, engine);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => main.OwnedForms.OfType<TranslationProgressForm>().Any(f => f.Visible), "Slow engine shows progress");
            var feedback = main.OwnedForms.OfType<TranslationProgressForm>().Single();
            Find<Button>(feedback, "CancelTranslation").PerformClick();
            await Task.Delay(50);
            for (int i = 0; i < 5; i++) shortcut.Press();
            await main.ShowOcrResultAsync(recognized, engine);
            Check(engine.Token.IsCancellationRequested && !active.IsCompleted && workflow.Calls == 0 && engine.Calls == 1,
                "Cancellation cannot allow overlapping requests while an uncooperative engine still runs");
            Check(feedback.Visible && Find<Label>(feedback, "OperationStatus").Text == "Cancelling...",
                "Pending engine cancellation retains visible progress");
            Capture(feedback, artifacts, "shortcut-cancelling-engine");
            engine.Finish.TrySetResult("Late fixture result");
            await active;
            Check(feedback.IsDisposed && !main.OwnedForms.OfType<TranslationResultForm>().Any(),
                "Actual engine completion releases progress without showing a cancelled result");
            await Until(() => main.Readiness.State == ReadinessState.Ready, "Ready after the actual engine stops");
            shortcut.Press();
            Check(workflow.Calls == 1, "Only a fresh hotkey starts work after engine cancellation finishes");
            workflow.CancelSelection();
            await Until(() => workflow.Selection is null, "Fresh selection fixture closes");
        }
        finally { engine.Finish.TrySetResult("Cleanup"); await active; }
        main.Close();
    }

    private sealed class UncooperativeTranslationEngine : ITranslationEngine
    {
        public int Calls;
        public CancellationToken Token;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<string> Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> TranslateAsync(string text, TranslationModel model, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            Started.TrySetResult();
            return Finish.Task;
        }
    }

    private sealed class ControlledWorkflow : ITranslationWorkflow
    {
        public int Calls;
        public Form? Selection;
        public CancellationToken Token;
        private IProgress<TranslationStage>? _progress;
        private TaskCompletionSource<TranslationResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? GetUnavailableReason(TranslationRequest request) => null;
        public async Task<TranslationResult?> RunAsync(Form owner, TranslationRequest request,
            IProgress<TranslationStage> progress, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            _progress = progress;
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Selection = new Form { Text = "Selection fixture", TopMost = true, Size = new Size(340, 180) };
            Selection.Controls.Add(new Label { Text = "Selection fixture", Dock = DockStyle.Fill });
            Selection.Show(owner);
            progress.Report(TranslationStage.Selecting);
            try { return await _completion.Task.WaitAsync(cancellationToken); }
            finally { Selection?.Dispose(); Selection = null; }
        }
        public void Report(TranslationStage stage) { Selection?.Hide(); _progress!.Report(stage); }
        public void Complete() => _completion.SetResult(new(new OcrResult("Workflow fixture", "eng", []), "Workflow fixture", "en", true));
        public void CancelSelection() => _completion.SetResult(null);
        public void Fail() => _completion.SetException(new InvalidOperationException("fixture failure"));
    }

    private sealed class UnavailableWorkflow : ITranslationWorkflow
    {
        public string? GetUnavailableReason(TranslationRequest request) =>
            "Screen translation isn't available in this test workflow.";
        public Task<TranslationResult?> RunAsync(Form owner, TranslationRequest request,
            IProgress<TranslationStage> progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An unavailable workflow must not run.");
    }

    private static void RunShortcutPreview()
    {
        var preferences = new InterfaceSettingsStore(Path.Combine(Root, "shortcut-preview.json"));
        preferences.Save(new(AppTheme.Light, Keys.Control | Keys.Alt | Keys.F9));
        using var main = ShortcutFixture(new GlobalShortcut(), preferences, new UnavailableWorkflow());
        main.Text = "Screen Translate - Shortcut verification";
        Application.Run(main);
    }
}
