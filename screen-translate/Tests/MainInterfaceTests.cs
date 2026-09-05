using System.Reflection;
using screen_translate;
using screen_translate.Interface;
using screen_translate.Settings;
using screen_translate.Translation;

internal static partial class Program
{
    private static MainForm CreateMainForm(SourceLanguageSettingsStore source, TargetLanguageSettingsStore target,
        ITranslationModelCatalog? catalog = null, InterfaceSettingsStore? preferences = null, IGlobalShortcut? shortcut = null) =>
        new(source, target, catalog, preferences ?? new InterfaceSettingsStore(Path.Combine(Root, "isolated-interface.json")),
            shortcut ?? new FakeShortcut());

    private static void TestInterfaceSettingsAndReadiness()
    {
        string path = Path.Combine(Root, "interface-settings.json");
        var store = new InterfaceSettingsStore(path);
        Check(store.Load(out var error) == InterfaceSettings.Default && error is null, "First launch interface preferences use valid defaults");
        var preferences = new InterfaceSettings(AppTheme.Dark, Keys.Control | Keys.Alt | Keys.F10);
        Check(store.Save(preferences) is null && store.Load(out error) == preferences && error is null, "Appearance and shortcut persist together");
        store.Save(preferences with { Theme = (AppTheme)999 });
        Check(store.Load(out error) == preferences with { Theme = AppTheme.System } && error is not null, "Invalid theme preserves valid shortcut");
        store.Save(preferences with { Shortcut = Keys.T });
        Check(store.Load(out error) == preferences with { Shortcut = InterfaceSettings.Default.Shortcut } && error is not null, "Invalid shortcut preserves valid theme");
        File.WriteAllText(path, "{broken");
        Check(store.Load(out error) == InterfaceSettings.Default && error is not null, "Corrupt interface preferences report a recoverable error");
        Check(new InterfaceSettingsStore(Path.Combine(path, "blocked.json")).Save(preferences) is not null, "Interface save failures are recoverable");
        Check(!InterfaceSettings.IsValidShortcut(Keys.T) && !InterfaceSettings.IsValidShortcut(Keys.Shift | Keys.T) &&
            !InterfaceSettings.IsValidShortcut(Keys.Control | Keys.Escape) && InterfaceSettings.IsValidShortcut(Keys.Alt | Keys.F9), "Shortcut validation rejects unmodified and unsupported keys");

        var scan = new TranslationModelScan([new("en", "es", "fixture")]);
        TranslationReadiness Evaluate(bool checking = false, string? source = "eng", string target = "es",
            string? sourceError = null, string? runtimeError = null, string? hotkeyError = null, TranslationModelScan? models = null) =>
            TranslationReadiness.Evaluate(checking, sourceError, source, target, models ?? scan, runtimeError, hotkeyError);
        Check(Evaluate(checking: true).State == ReadinessState.Checking, "Readiness stays checking during discovery");
        Check(Evaluate(source: null).Reason.Contains("OCR folder"), "Readiness explains missing OCR models");
        Check(Evaluate(sourceError: "Cannot read OCR").Reason == "Cannot read OCR", "Readiness preserves OCR scan errors");
        Check(Evaluate(models: new([], "Cannot read translation")).Reason.Contains("Cannot read translation"), "Readiness preserves translation scan errors");
        Check(Evaluate(target: "ja").Reason.Contains("en → ja"), "Readiness requires the configured translation direction");
        Check(Evaluate(runtimeError: "Engine not validated").State == ReadinessState.ActionRequired, "Discovery alone cannot establish runtime readiness");
        Check(Evaluate(hotkeyError: "Shortcut conflict").Reason == "Shortcut conflict", "Readiness includes shortcut failures");
        Check(Evaluate().State == ReadinessState.Ready && Evaluate().Reason.Contains("en → es"), "Validated prerequisites yield ready with pair and reason (logic fixture only)");
        Check(Evaluate(target: "en", models: new([], "unreadable")).State == ReadinessState.Ready, "Identical languages bypass translation models after OCR runtime validation (logic fixture)");
        Check(Evaluate(source: "custom_model").State == ReadinessState.ActionRequired, "Unmapped source requires user action");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool ocrCancelled = false, translationCancelled = false;
        try { Catalog.Scan(Root, cancellation.Token); } catch (OperationCanceledException) { ocrCancelled = true; }
        try { new ArgosTranslationModelCatalog().Scan(Root, cancellation.Token); } catch (OperationCanceledException) { translationCancelled = true; }
        Check(ocrCancelled && translationCancelled, "Both production model catalogs honor cancellation");
    }

    private static void TestLifetime()
    {
        var lifetime = new ApplicationLifetime();
        var token = lifetime.Token;
        int cancelled = 0;
        using var subscription = token.Register(() => cancelled++);
        using var faultySubscription = token.Register(() => throw new InvalidOperationException("fixture"));
        var resource = new DisposableProbe();
        lifetime.Own(resource);
        lifetime.Dispose();
        lifetime.Dispose();
        Check(token.IsCancellationRequested && cancelled == 1 && resource.Disposed == 1, "Shutdown cancels work and releases resources once even if a callback fails");
        var late = new DisposableProbe();
        lifetime.Own(late);
        Check(late.Disposed == 1, "Work resources submitted after shutdown are disposed immediately");
    }

    private static void TestNativeShortcut()
    {
        using var first = new GlobalShortcut();
        using var second = new GlobalShortcut();
        Keys chosen = Keys.None;
        foreach (var key in new[] { Keys.F9, Keys.F10, Keys.F11, Keys.F8 })
        {
            var candidate = Keys.Control | Keys.Alt | Keys.Shift | key;
            if (first.TrySet(candidate) is null) { chosen = candidate; break; }
        }
        Check(chosen != Keys.None, "Native isolated test shortcut registers");
        Check(second.TrySet(chosen) is not null, "Native hotkey conflict is rejected");
        Check(first.TrySet(Keys.T) is not null && second.TrySet(chosen) is not null, "Invalid shortcut leaves previous native registration intact");
        first.Dispose();
        Check(second.TrySet(chosen) is null, "Disposal releases the real Windows hotkey for another owner");
    }

    private static async Task TestMainInterfaceUi(MainForm form, string artifacts)
    {
        Check(form.Readiness.State == ReadinessState.ActionRequired && Find<Label>(form, "TranslationReadiness").Text.Contains("OCR folder"), "Main interface explains missing-model readiness");
        foreach (var name in new[] { "TargetLanguage", "GlobalShortcut", "ApplyShortcut", "ManageModels", "NavigateModels" })
            Check(form.Controls.Find(name, true).Single().Enabled, name + " stays available without models");
        Button Theme(string text) => Descendants(form).OfType<Button>().Single(button => button.Text == text);
        Theme("Dark").PerformClick();
        Check((Theme("Dark").AccessibilityObject.State & AccessibleStates.Checked) != 0,
            "Selected appearance is exposed to assistive technology");
        Check(form.BackColor.R < 60 && new InterfaceSettingsStore(Path.Combine(Root, "isolated-interface.json")).Load(out _).Theme == AppTheme.Dark,
            "Dark appearance applies immediately and persists without models");
        Capture(form, artifacts, "main-dark-missing");
        CaptureLowerSettings(form, artifacts, "main-dark-settings");
        Theme("Light").PerformClick();
        Check(form.BackColor.R > 230, "Light appearance applies immediately");
        Capture(form, artifacts, "redesign-general-light");
        CaptureLowerSettings(form, artifacts, "redesign-models-light");
        Theme("System").PerformClick();
        Check(new InterfaceSettingsStore(Path.Combine(Root, "isolated-interface.json")).Load(out _).Theme == AppTheme.System, "System appearance remains selectable");
        Find<Button>(form, "NavigateModels").PerformClick();
        Check((Find<Button>(form, "NavigateModels").AccessibilityObject.State & AccessibleStates.Selected) != 0,
            "Current navigation page is exposed to assistive technology");
        Check(Find<FlowLayoutPanel>(form, "OcrFolderActions").ContainsFocus, "Models navigation directs missing-source setup to OCR folder controls");
        Find<Button>(form, "NavigateSettings").PerformClick();
        var sourceStore = new SourceLanguageSettingsStore(Path.Combine(Root, "main-source.json"));
        var targetStore = new TargetLanguageSettingsStore(Path.Combine(Root, "main-target.json"));
        string data = NewFolder("main-ocr");
        Install(data, "eng");
        string models = NewFolder("main-models");
        InstallTranslation(models, "en", "es");
        sourceStore.Save(new(data, "eng"));
        targetStore.Save(new(models, "es"));
        var shortcut = new FakeShortcut();
        var preferences = new InterfaceSettingsStore(Path.Combine(Root, "main-interface.json"));
        using var configured = CreateMainForm(sourceStore, targetStore, preferences: preferences, shortcut: shortcut);
        configured.Show();
        await configured.RefreshSourceLanguagesAsync();
        await WaitForReadiness(configured);
        Check(configured.Readiness.State == ReadinessState.ActionRequired && configured.Readiness.Reason.Contains("isn't available"), $"Complete discovery fixtures never claim actual translation readiness ({configured.Readiness})");
        Capture(configured, artifacts, "main-runtime-unavailable");
        var input = Find<TextBox>(configured, "GlobalShortcut");
        void Press(Keys keys) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, [new KeyEventArgs(keys)]);
        Press(Keys.Control | Keys.Alt | Keys.Y);
        Find<Button>(configured, "ApplyShortcut").PerformClick();
        Check(preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.Y), "Shortcut editor applies and persists captured keys");
        shortcut.Conflict = true;
        Press(Keys.Control | Keys.Alt | Keys.U);
        Find<Button>(configured, "ApplyShortcut").PerformClick();
        Check(Find<Label>(configured, "ShortcutStatus").Text.Contains("fixture conflict") && preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.Y), "Conflicting edit reports reason and preserves configured shortcut");
        shortcut.Conflict = false;
        Press(Keys.T);
        Find<Button>(configured, "ApplyShortcut").PerformClick();
        Check(Find<Label>(configured, "ShortcutStatus").Text.Contains("Invalid") && preferences.Load(out _).Shortcut == (Keys.Control | Keys.Alt | Keys.Y), "Invalid edit preserves saved shortcut");
        Press(Keys.Escape);
        Check(input.Text == InterfaceSettings.FormatShortcut(Keys.Control | Keys.Alt | Keys.Y), "Escape restores the configured shortcut in the editor");
        Find<Button>(configured, "ManageModels").PerformClick();
        Check(Find<FlowLayoutPanel>(configured, "TranslationFolderActions").ContainsFocus, "Manage models reaches model configuration");
        targetStore.Save(new(Path.Combine(models, "en_es", "metadata.json"), "es"));
        await configured.SetTranslationModelDirectoryAsync(Path.Combine(models, "en_es", "metadata.json"));
        Check(configured.Readiness.Reason.Contains("Cannot read") && input.Enabled && Find<ComboBox>(configured, "TargetLanguage").Enabled, "Invalid model directory leaves configuration enabled with an actionable reason");
        Capture(configured, artifacts, "main-invalid-models");
        configured.Close();
        Check(shortcut.DisposeCount == 1 && configured.WorkCancellationToken.IsCancellationRequested, "Closing main interface cancels work and releases its hotkey owner");
        await TestCloseDuringScan(sourceStore, targetStore);

        string corruptPreferences = Path.Combine(Root, "redesign-invalid-settings.json");
        File.WriteAllText(corruptPreferences, "{invalid");
        using var recovery = CreateMainForm(sourceStore, targetStore, preferences: new InterfaceSettingsStore(corruptPreferences));
        recovery.Show();
        await recovery.RefreshSourceLanguagesAsync();
        await WaitForReadiness(recovery);
        var error = Find<Label>(recovery, "InterfaceSettingsError");
        Check(error.Visible && error.Text.Contains("could not be read") &&
            Find<Panel>(recovery, "SettingsViewport").ClientRectangle.Contains(
                Find<Panel>(recovery, "SettingsViewport").RectangleToClient(error.RectangleToScreen(error.ClientRectangle))),
            "Recoverable preference errors are visible above the redesigned settings cards");
        Capture(recovery, artifacts, "redesign-settings-error");
        recovery.Close();
    }

    private static async Task TestCloseDuringScan(SourceLanguageSettingsStore source, TargetLanguageSettingsStore target)
    {
        var catalog = new CancellationCatalog();
        var shortcut = new FakeShortcut();
        using var form = CreateMainForm(source, target, catalog, shortcut: shortcut);
        form.Show();
        await form.RefreshSourceLanguagesAsync();
        await WaitForReadiness(form);
        catalog.Delay = true;
        Task pending = form.RefreshTranslationModelsAsync();
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(form.Readiness.State == ReadinessState.Checking && Find<ComboBox>(form, "TargetLanguage").Enabled, "Pending model check is explicit and leaves settings available");
        var selection = new Form { Text = "Selection lifecycle fixture", ShowInTaskbar = false };
        var overlay = new Form { Text = "Overlay lifecycle fixture", ShowInTaskbar = false, TopMost = true };
        overlay.FormClosing += (_, e) => e.Cancel = true;
        form.ShowTranslationWindow(selection);
        form.ShowTranslationWindow(overlay);
        form.Close();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await catalog.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(form.IsDisposed && selection.IsDisposed && overlay.IsDisposed && shortcut.DisposeCount == 1,
            "Close during model scan cancels worker, dismisses owned windows even if a child cancels close, and releases hotkey");
        await form.RefreshSourceLanguagesAsync();
        await form.RefreshTranslationModelsAsync();
        Check(form.WorkCancellationToken.IsCancellationRequested, "Refresh after close cannot restart work");
        var late = new Form();
        form.ShowTranslationWindow(late);
        Check(late.IsDisposed, "Late selection or overlay cannot reopen after close");
    }

    private static void TestMainMessageLoopExit()
    {
        var source = new SourceLanguageSettingsStore(Path.Combine(Root, "exit-source.json"));
        var target = new TargetLanguageSettingsStore(Path.Combine(Root, "exit-target.json"));
        source.Save(new(NewFolder("exit-ocr"), null));
        target.Save(new(NewFolder("exit-models"), "es"));
        using var form = CreateMainForm(source, target);
        bool shown = false;
        form.Shown += (_, _) => { shown = form.Visible; form.BeginInvoke((Action)form.Close); };
        Application.Run(form);
        Check(shown && form.IsDisposed && form.WorkCancellationToken.IsCancellationRequested, "Launch message loop displays main window and exits when it closes");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static async Task WaitForReadiness(MainForm form)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (form.Readiness.State == ReadinessState.Checking) await Task.Delay(10, timeout.Token);
    }

    private static void CaptureLowerSettings(MainForm form, string directory, string name)
    {
        Find<Button>(form, "NavigateModels").PerformClick();
        var page = Descendants(form).OfType<Panel>().Single(panel => panel.AutoScroll);
        form.PerformLayout();
        page.AutoScrollPosition = new Point(0, page.DisplayRectangle.Height);
        Application.DoEvents();
        var button = Find<Button>(form, "ChooseTranslationFolder");
        var bounds = page.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
        Check(page.ClientRectangle.Contains(bounds), "Offline model controls remain reachable at the bottom of the settings page");
        Check(Find<Button>(form, "NavigateSettings").Visible &&
            form.ClientRectangle.Contains(form.RectangleToClient(Find<Button>(form, "NavigateSettings").RectangleToScreen(Find<Button>(form, "NavigateSettings").ClientRectangle))),
            "General navigation remains visible while model settings are scrolled");
        Capture(form, directory, name);
        page.AutoScrollPosition = Point.Empty;
        Find<Button>(form, "NavigateSettings").PerformClick();
    }

    private static void TestRedesignedLayout(MainForm form)
    {
        var page = Find<Panel>(form, "SettingsViewport");
        Check(Find<Panel>(form, "GeneralPage").Visible && !Find<Panel>(form, "ModelsPage").Visible,
            "Launch focuses everyday preferences and keeps model diagnostics on a separate page");
        foreach (var name in new[] { "LanguagesCard", "AppearanceCard", "ShortcutCard" })
        {
            var card = form.Controls.Find(name, true).Single();
            var bounds = page.RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
            Check(page.ClientRectangle.Contains(bounds), name + " is fully visible at the default window size");
        }
        Check(!page.VerticalScroll.Visible, "Everyday settings need no scrolling at the default window size");
        var source = Find<ComboBox>(form, "SourceLanguage");
        var target = Find<ComboBox>(form, "TargetLanguage");
        Check(source.Width == target.Width && source.Top == target.Top && source.Height >= 36,
            "Language controls align and provide comfortable hit areas");
        foreach (var name in new[] { "ManageModels", "NavigateModels", "NavigateSettings", "ApplyShortcut" })
        {
            var button = Find<Button>(form, name);
            Check(button.TabStop && button.Height >= 36, name + " remains keyboard reachable with a comfortable hit area");
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public int Disposed;
        public void Dispose() => Disposed++;
    }

    private sealed class FakeShortcut : IGlobalShortcut
    {
        public event EventHandler? Pressed { add { } remove { } }
        public bool Conflict;
        public int DisposeCount;
        public string? TrySet(Keys shortcut) => !InterfaceSettings.IsValidShortcut(shortcut) ? "Invalid shortcut" : Conflict ? "fixture conflict" : null;
        public void Dispose() => DisposeCount++;
    }

    private sealed class CancellationCatalog : ITranslationModelCatalog
    {
        public bool Delay;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslationModelScan Scan(string directory) => new([]);
        public TranslationModelScan Scan(string directory, CancellationToken cancellationToken)
        {
            if (!Delay) return new([]);
            Started.TrySetResult();
            try { Task.Delay(Timeout.Infinite, cancellationToken).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            return new([]);
        }
    }
}
