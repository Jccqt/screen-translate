using screen_translate;
using screen_translate.Capture;
using screen_translate.Interface;
using screen_translate.Ocr;
using screen_translate.Settings;
using screen_translate.Translation;
using System.Reflection;

internal static partial class Program
{
    private static async Task TestScreenRegionCapture(string artifacts)
    {
        TestSelectionGeometry();
        TestTopologyAndSnapshotCropping();
        await TestDisplayChangeBeforeSelection();
        await TestCaptureWorkflowBoundaries();
        TestCaptureFailureFeedback();
        await TestSelectionInterface(artifacts);
        await TestMinimizedOwnerSelection();
    }

    private static void TestSelectionGeometry()
    {
        var session = new RegionSelectionSession(new Size(900, 600));
        session.Begin(new Point(700, 500));
        Check(session.Update(new Point(100, 80)) == new Rectangle(100, 80, 600, 420),
            "Region selection normalizes an up-left drag before pointer release");
        Check(session.Complete(new Point(100, 80)) == new Rectangle(100, 80, 600, 420),
            "Reverse drag confirms the visible normalized rectangle");
        session.Begin(new Point(200, 150));
        Check(session.Complete(new Point(200, 150)) is null && session.Bounds.IsEmpty,
            "A click with zero area does not confirm a capture");
        session.Begin(new Point(40, 40));
        Check(session.Update(new Point(1200, -50)) == new Rectangle(40, 0, 860, 40),
            "Pointer movement outside the virtual desktop is clamped to physical-pixel bounds");
    }

    private static void TestTopologyAndSnapshotCropping()
    {
        var left = new DisplayMonitor("left", new Rectangle(-1280, 0, 1280, 1024), 120, 120);
        var upper = new DisplayMonitor("upper", new Rectangle(0, -900, 1600, 900), 144, 144);
        var primary = new DisplayMonitor("primary", new Rectangle(0, 0, 1920, 1080), 96, 96);
        var topology = new ScreenTopology(new Rectangle(-1280, -900, 3200, 1980), [left, upper, primary]);
        Check(topology.ValidationError is null && topology.Matches(new(topology.VirtualBounds, [left, upper, primary])),
            "Virtual desktop topology accepts monitors left of and above primary with mixed DPI");
        Check(!topology.Matches(new(topology.VirtualBounds,
            [left, upper with { DpiX = 192, DpiY = 192 }, primary])),
            "A monitor scaling change invalidates an active capture topology");
        using var snapshot = new Bitmap(20, 16);
        snapshot.SetPixel(3, 4, Color.Fuchsia);
        var smallTopology = new ScreenTopology(new Rectangle(-400, -200, 20, 16),
            [new("fixture", new Rectangle(-400, -200, 20, 16), 144, 144)]);
        using var selected = RegionSelectorForm.CreateCapturedRegion(snapshot, smallTopology, new Rectangle(3, 4, 5, 6));
        Check(selected.Bounds == new Rectangle(-397, -196, 5, 6) &&
            selected.Image.GetPixel(0, 0).ToArgb() == Color.Fuchsia.ToArgb(),
            "Confirmed capture maps local pixels to negative virtual coordinates without redrawing selector UI");
    }

    private static async Task TestDisplayChangeBeforeSelection()
    {
        var first = new ScreenTopology(new Rectangle(-100, 0, 300, 200),
            [new("fixture", new Rectangle(-100, 0, 300, 200), 96, 96)]);
        var changed = new ScreenTopology(new Rectangle(0, 0, 200, 200),
            [new("fixture", new Rectangle(0, 0, 200, 200), 144, 144)]);
        var geometry = new SequencedGeometry(first, changed);
        var snapshots = new SnapshotFixture();
        var service = new ScreenRegionCaptureService(geometry, snapshots, TimeSpan.Zero);
        using var owner = new Form();
        RegionCaptureOutcome outcome = await service.SelectAsync(owner);
        Check(snapshots.Calls == 1 && outcome.Region is null && outcome.Error?.Contains("display configuration changed") == true,
            "A display change between desktop snapshot and selector safely cancels with retry guidance");
        bool disposed = false;
        try { _ = snapshots.Image!.GetPixel(0, 0); }
        catch (ArgumentException) { disposed = true; }
        Check(disposed, "A cancelled topology change promptly disposes the private desktop snapshot");
    }

    private static async Task TestCaptureWorkflowBoundaries()
    {
        string data = NewFolder("capture-workflow");
        Install(data, "eng");
        var request = new TranslationRequest("eng", "en", data, NewFolder("capture-workflow-models"));
        var recognizer = new RecognizerFixture();
        var capture = new CaptureFixture(new());
        var workflow = new ScreenTranslationWorkflow(recognizer, new ResultCatalog(), capture);
        var stages = new List<TranslationStage>();
        using var owner = new Form();
        Check(workflow.GetUnavailableReason(request) is null, "Valid OCR-only configuration is accepted before capture");
        Check(await workflow.RunAsync(owner, request, new InlineProgress<TranslationStage>(stages.Add), default) is null &&
            recognizer.Calls == 0 && stages.SequenceEqual([TranslationStage.Selecting]),
            "Escape-style selection cancellation returns without starting OCR or translation");

        capture.Outcome = new(Error: "Secure desktop capture failed. Try again.");
        try
        {
            await workflow.RunAsync(owner, request, new InlineProgress<TranslationStage>(_ => { }), default);
            throw new InvalidOperationException("Expected capture failure");
        }
        catch (ScreenCaptureException error)
        {
            Check(error.Message.Contains("Secure desktop") && recognizer.Calls == 0,
                "Detectable capture failure remains understandable and never starts OCR");
        }

        var pixels = new Bitmap(12, 8);
        capture.Outcome = new(new CapturedRegion(pixels, new Rectangle(-40, 25, 12, 8)));
        stages.Clear();
        TranslationResult? result = await workflow.RunAsync(owner, request,
            new InlineProgress<TranslationStage>(stages.Add), default);
        Check(recognizer.Calls == 1 && recognizer.Size == new Size(12, 8) && result?.TranslationSkipped == true &&
            stages.SequenceEqual([TranslationStage.Selecting, TranslationStage.Recognizing, TranslationStage.Translating]),
            "A confirmed non-empty in-memory capture reaches OCR once and then the same-language result path");
        bool released = false;
        try { _ = pixels.GetPixel(0, 0); }
        catch (ArgumentException) { released = true; }
        Check(released, "Captured pixels are disposed immediately after processing");

        using var cancellation = new CancellationTokenSource();
        var cancellationRecognizer = new CancellationRecognizer(cancellation);
        var cancellationCapture = new CaptureFixture(new(new CapturedRegion(
            new Bitmap(9, 7), new Rectangle(10, 10, 9, 7))));
        var cancellationWorkflow = new ScreenTranslationWorkflow(cancellationRecognizer, new ResultCatalog(), cancellationCapture);
        Task<TranslationResult?> active = cancellationWorkflow.RunAsync(owner, request,
            new InlineProgress<TranslationStage>(_ => { }), cancellation.Token);
        try { await active; throw new InvalidOperationException("Expected recognition cancellation"); }
        catch (OperationCanceledException) { }
        bool cancelledPixelsReleased = false;
        try { _ = cancellationRecognizer.Image!.GetPixel(0, 0); }
        catch (ArgumentException) { cancelledPixelsReleased = true; }
        Check(cancellationRecognizer.Token.IsCancellationRequested && cancelledPixelsReleased,
            "Cancellation reaches OCR and disposes captured pixels after the recognizer stops");

        var missing = request with { OcrDataDirectory = NewFolder("capture-missing-model") };
        int before = capture.Calls;
        Check(workflow.GetUnavailableReason(missing)?.Contains("missing") == true,
            "Known missing OCR configuration is explained before capture starts");
        try { await workflow.RunAsync(owner, missing, new InlineProgress<TranslationStage>(_ => { }), default); }
        catch (InvalidOperationException) { }
        Check(capture.Calls == before, "Known configuration failure does not display the selector");
    }

    private static async Task TestSelectionInterface(string artifacts)
    {
        Directory.CreateDirectory(artifacts);
        var topology = new ScreenTopology(new Rectangle(0, 0, 640, 360),
            [new("fixture", new Rectangle(0, 0, 640, 360), 144, 144)]);
        var geometry = new SequencedGeometry(topology);
        using var owner = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 200, 100) };
        owner.Show();
        using var snapshot = new Bitmap(640, 360);
        using (var graphics = Graphics.FromImage(snapshot))
        {
            graphics.Clear(Color.FromArgb(235, 240, 242));
            using var font = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("Drag in any direction", font, Brushes.DarkSlateGray, new PointF(150, 155));
        }
        using (var selector = new RegionSelectorForm(snapshot, topology, geometry))
        {
            Task<RegionCaptureOutcome> pending = selector.SelectAsync(owner, default);
            InvokeMouse(selector, "OnMouseDown", MouseButtons.Left, 520, 300);
            InvokeMouse(selector, "OnMouseMove", MouseButtons.Left, 100, 60);
            Application.DoEvents();
            Check(selector.SelectionBounds == new Rectangle(100, 60, 420, 240) && !pending.IsCompleted,
                "Selection UI exposes normalized bounds while the pointer is still down");
            Capture(selector, artifacts, "screen-region-selection");
            InvokeMouse(selector, "OnMouseUp", MouseButtons.Left, 100, 60);
            RegionCaptureOutcome outcome = await pending;
            using var region = outcome.Region!;
            Check(region.Bounds == new Rectangle(100, 60, 420, 240) && region.Image.Size == region.Bounds.Size,
                "Releasing the pointer confirms exactly the displayed region");
        }
        using (var selector = new RegionSelectorForm(snapshot, topology, geometry))
        {
            Task<RegionCaptureOutcome> pending = selector.SelectAsync(owner, default);
            InvokeMouse(selector, "OnMouseDown", MouseButtons.Left, 80, 80);
            InvokeMouse(selector, "OnMouseUp", MouseButtons.Left, 80, 80);
            Check(!pending.IsCompleted && selector.Visible, "Zero-area release leaves selection active without confirming pixels");
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(selector, [new KeyEventArgs(Keys.Escape)]);
            Check((await pending).Region is null, "Escape closes selection without returning a capture");
        }
        owner.Close();
    }

    private static async Task TestMinimizedOwnerSelection()
    {
        var topology = new ScreenTopology(new Rectangle(0, 0, 320, 180),
            [new("fixture", new Rectangle(0, 0, 320, 180), 96, 96)]);
        using var owner = new Form { WindowState = FormWindowState.Minimized };
        owner.Show();
        using var snapshot = new Bitmap(topology.VirtualBounds.Width, topology.VirtualBounds.Height);
        using var selector = new RegionSelectorForm(snapshot, topology, new SequencedGeometry(topology));
        Task<RegionCaptureOutcome> pending = selector.SelectAsync(owner, default);
        Application.DoEvents();
        Check(selector.Visible && selector.Owner is null,
            "A minimized main window does not hide the global-hotkey selector");
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(selector, [new KeyEventArgs(Keys.Escape)]);
        Check((await pending).Region is null, "The independent selector still cancels normally");
        owner.Close();
    }

    private static void TestCaptureFailureFeedback()
    {
        string id = Guid.NewGuid().ToString("N");
        string data = NewFolder("capture-feedback-" + id);
        Install(data, "eng");
        var source = new SourceLanguageSettingsStore(Path.Combine(Root, id + "-capture-source.json"));
        source.Save(new(data, "eng"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, id + "-capture-target.json"));
        target.Save(new(NewFolder("capture-feedback-models-" + id), "en"));
        var shortcut = new FakeShortcut();
        var capture = new CaptureFixture(new(Error: "Windows fixture capture failure. Check displays and try again."));
        var recognizer = new RecognizerFixture();
        using var main = new MainForm(source, target,
            interfaceSettingsStore: new InterfaceSettingsStore(Path.Combine(Root, id + "-capture-interface.json")),
            globalShortcut: shortcut, ocrEngine: recognizer, captureService: capture);
        Exception? failure = null;
        main.Shown += async (_, _) =>
        {
            try
            {
                await main.RefreshSourceLanguagesAsync();
                await Until(() => main.Readiness.State == ReadinessState.Ready, "Capture-feedback fixture becomes ready");
                shortcut.Press();
                await Until(() => Find<Label>(main, "TranslationReadiness").Text.Contains("fixture capture failure"),
                    "Capture failure reaches the retryable readiness surface");
                Check(recognizer.Calls == 0, "Capture failure feedback appears without invoking OCR");
                capture.Outcome = new();
                shortcut.Press();
                await Until(() => capture.Calls == 2, "A new hotkey retries capture after a detectable failure");
                Check(!Find<Label>(main, "TranslationReadiness").Text.Contains("fixture capture failure"),
                    "A clean cancelled retry clears the previous capture failure");
            }
            catch (Exception error) { failure = error; }
            finally { main.Close(); }
        };
        Application.Run(main);
        if (failure is not null) throw new InvalidOperationException("Capture feedback integration failed", failure);
    }

    private static void InvokeMouse(Control control, string method, MouseButtons button, int x, int y) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [new MouseEventArgs(button, 1, x, y, 0)]);

    private static void RunCapturePreview()
    {
        string data = NewFolder("capture-preview-ocr");
        Install(data, "eng");
        var source = new SourceLanguageSettingsStore(Path.Combine(Root, "capture-preview-source.json"));
        source.Save(new(data, "eng"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, "capture-preview-target.json"));
        target.Save(new(NewFolder("capture-preview-models"), "en"));
        var preferences = new InterfaceSettingsStore(Path.Combine(Root, "capture-preview-interface.json"));
        preferences.Save(new(AppTheme.Dark, Keys.Control | Keys.Alt | Keys.F7));
        using var main = new MainForm(source, target, interfaceSettingsStore: preferences,
            globalShortcut: new GlobalShortcut(), ocrEngine: new RecognizerFixture());
        main.Text = "Screen Translate - Capture verification (fixture OCR)";
        Application.Run(main);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SequencedGeometry(params ScreenTopology[] values) : IDesktopGeometry
    {
        private int _index;
        public ScreenTopology GetCurrent() => values[Math.Min(_index++, values.Length - 1)];
    }

    private sealed class SnapshotFixture : IDesktopSnapshotProvider
    {
        public int Calls;
        public Bitmap? Image;
        public Bitmap Capture(ScreenTopology topology)
        {
            Calls++;
            return Image = new Bitmap(topology.VirtualBounds.Width, topology.VirtualBounds.Height);
        }
    }

    private sealed class CaptureFixture(RegionCaptureOutcome outcome) : IScreenRegionCaptureService
    {
        public RegionCaptureOutcome Outcome = outcome;
        public int Calls;
        public string? GetUnavailableReason() => null;
        public Task<RegionCaptureOutcome> SelectAsync(Form owner, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Outcome);
        }
    }

    private sealed class RecognizerFixture : IOcrRecognizer
    {
        public int Calls;
        public Size Size;
        public Task ValidateLanguageAsync(string dataDirectory, string languageCode,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OcrResult> RecognizeAsync(Bitmap image, string dataDirectory, string languageCode,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Size = image.Size;
            return Task.FromResult(new OcrResult("Captured words", languageCode,
                [new("Captured", new Rectangle(0, 0, image.Width, image.Height), 99)]));
        }
    }

    private sealed class CancellationRecognizer(CancellationTokenSource cancellation) : IOcrRecognizer
    {
        public Bitmap? Image;
        public CancellationToken Token;
        public Task ValidateLanguageAsync(string dataDirectory, string languageCode,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OcrResult> RecognizeAsync(Bitmap image, string dataDirectory, string languageCode,
            CancellationToken cancellationToken = default)
        {
            Image = image;
            Token = cancellationToken;
            cancellation.Cancel();
            return Task.FromCanceled<OcrResult>(cancellationToken);
        }
    }
}
