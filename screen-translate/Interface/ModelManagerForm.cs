using screen_translate.Models;
using screen_translate.Ocr;

namespace screen_translate.Interface;

public sealed class ModelManagerForm : Form
{
    private readonly ModelPurpose _purpose;
    private readonly string _root;
    private readonly ModelManager _manager;
    private readonly ListBox _models = new() { Name = "ModelInventory", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _details = new() { Name = "ModelDetails", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label _status = new() { Name = "ModelOperationStatus", Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly ProgressBar _progress = new() { Name = "ModelProgress", Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Button _cancel = new() { Text = "Cancel operation", Name = "CancelModelOperation", AutoSize = true, Enabled = false };
    private readonly Button _retry = new() { Text = "Retry", Name = "RetryModelOperation", AutoSize = true, Enabled = false };
    private readonly CheckBox _replace = new() { Text = "Replace existing model", Name = "ReplaceModel", AutoSize = true };
    private CancellationTokenSource? _work;
    private Func<CancellationToken, Task>? _lastOperation;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolTip _statusTip = new();
    private bool _closeAfterWork;
    private int _scanVersion;
    private bool _disposed;
    private readonly Dictionary<string, string> _loadFailures = new(StringComparer.OrdinalIgnoreCase);

    public ModelManagerForm(ModelPurpose purpose, string root, ModelManager? manager = null)
    {
        _purpose = purpose; _root = root; _manager = manager ?? new ModelManager();
        _status.TextChanged += (_, _) => _statusTip.SetToolTip(_status, _status.Text);
        Text = purpose == ModelPurpose.Ocr ? "Manage OCR models" : "Manage translation models";
        Name = "ModelManager";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(720, 540);
        Size = new Size(900, 720);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var row in new[] { new RowStyle(SizeType.Absolute, 90), new RowStyle(SizeType.Percent, 40),
            new RowStyle(SizeType.Percent, 60), new RowStyle(SizeType.Absolute, 35), new RowStyle(SizeType.Absolute, 22), new RowStyle(SizeType.Absolute, 96) })
            layout.RowStyles.Add(row);
        var heading = new TextBox { Name = "ModelStorageSummary", Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Text = $"Storage: {root}\r\nChoose storage folders on the Offline models page.\r\n" +
                (purpose == ModelPurpose.Ocr ? "Tesseract: import .traineddata, or review and start a download." :
                "Argos direct-pair .argosmodel / .zip packages. No translation runtime is selected in this build; no direction is engine-validated.") };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(_models, 0, 1); layout.Controls.Add(_details, 0, 2);
        layout.Controls.Add(_status, 0, 3); layout.Controls.Add(_progress, 0, 4); layout.Controls.Add(_actions, 0, 5);
        AddAction("Import…", "ImportModel", Import);
        AddAction("Download…", "DownloadModel", Download);
        AddAction("Validate", "ValidateModel", ValidateModel);
        AddAction("Remove…", "RemoveModel", Remove);
        AddAction("Refresh", "RefreshModelInventory", async () => await RefreshModelsAsync());
        AddAction("Package formats", "ModelFormatHelp", () =>
        {
            MessageBox.Show(this, ModelFormatHelp, "Accepted formats and engine support", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        });
        _actions.Controls.AddRange([_replace, _cancel, _retry]);
        _cancel.Click += (_, _) => { _work?.Cancel(); _status.Text = "Cancelling; waiting for file and engine work to finish…"; };
        _retry.Click += async (_, _) => { if (_lastOperation is not null) await Run(_lastOperation); };
        _models.SelectedIndexChanged += (_, _) => _details.Text = (_models.SelectedItem as ModelDetails)?.Describe() ?? "No model selected.";
        Controls.Add(layout);
        Shown += async (_, _) => await RefreshModelsAsync();
        FormClosing += (_, e) =>
        {
            if (_work is not null) { e.Cancel = true; _closeAfterWork = true; _work.Cancel(); _status.Text = "Cancelling; waiting for safe cleanup before closing…"; }
        };
    }

    public const string ModelFormatHelp = "OCR: one nonempty <language>.traineddata file. Installation loads it with the bundled Tesseract runtime. OSD/equation data is not a source language.\r\n\r\n" +
        "Translation: ZIP or .argosmodel containing one package at the root or in one wrapper folder: metadata.json (from_code, to_code; type=translate if present), model/model.bin, model/config.json, " +
        "model/shared_vocabulary.json OR both source_vocabulary.json and target_vocabulary.json, and sentencepiece.model OR bpe.model. JSON and required files must be readable and nonempty. Keep all additional tokenizer/sentence-splitting data in the archive.\r\n\r\n" +
        "Supported package directions are the explicit from_code → to_code in each direct-pair package. Reverse and pivot translation are not implied. Output preferences are zh, en, fil, fr, de, ja, ko, es. " +
        "No production translation engine is included yet, so no translation direction is validated for use. Discovery does not test binary compatibility. No dependencies download on first use.\r\n\r\n" +
        "Downloads require HTTPS and a user-reviewed source/license. Redirects are rejected; use the publisher's final file URL. Sizes may be unknown. Supply a trusted publisher SHA-256 when available. " +
        "Packages are limited to 8 GiB and 20,000 entries; links, traversal, duplicate Windows paths and reserved management files are rejected. Imported metadata is Unknown when absent.\r\n\r\n" +
        "Removal requires confirmation and only affects identified files. Unidentified external files are preserved; they can block replacement. Files in use block changes until the operation finishes.";

    public void ApplyTheme(Color surface, Color ink)
    {
        void Paint(Control control)
        {
            control.BackColor = surface; control.ForeColor = ink;
            foreach (Control child in control.Controls) Paint(child);
        }
        Paint(this);
    }

    private void AddAction(string text, string name, Func<Task> action)
    {
        var button = new Button { Text = text, Name = name, AutoSize = true, Padding = new Padding(4), Margin = new Padding(3) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) when (Recoverable(error)) { _status.Text = error.Message; } };
        _actions.Controls.Add(button);
    }

    public async Task RefreshModelsAsync()
    {
        int version = ++_scanVersion;
        _status.Text = "Checking local model files…";
        _models.Enabled = false;
        _details.Text = "Checking…";
        try
        {
            var models = (await Task.Run(() => _manager.Scan(_purpose, _root, _lifetime.Token)))
                .Select(m => _loadFailures.TryGetValue(m.Location, out string? failure)
                    ? m with { State = ModelInstallState.Invalid, Explanation = failure } : m).ToArray();
            if (IsDisposed || version != _scanVersion) return;
            string? selected = (_models.SelectedItem as ModelDetails)?.Location;
            _models.Items.Clear();
            _models.Items.AddRange(models.Cast<object>().ToArray());
            _models.SelectedItem = models.FirstOrDefault(m => m.Location == selected) ?? models.FirstOrDefault();
            if (models.Length == 0) _details.Text = "Missing: no local models found. Import a package or review a download.";
            _status.Text = $"{models.Length} local model(s). Discovered files require engine loading before validation.";
        }
        catch (OperationCanceledException) when (_disposed || _lifetime.IsCancellationRequested) { }
        catch (Exception error) when (Recoverable(error)) { if (!IsDisposed) { _models.Items.Clear(); _details.Text = "Cannot check: " + error.Message; } }
        finally { if (!IsDisposed && version == _scanVersion) _models.Enabled = _work is null; }
    }

    private async Task Import()
    {
        using var picker = new OpenFileDialog { Title = "Import offline model", Filter = _purpose == ModelPurpose.Ocr ?
            "Tesseract language (*.traineddata)|*.traineddata" : "Argos packages (*.argosmodel;*.zip)|*.argosmodel;*.zip" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        string file = picker.FileName;
        bool replace = _replace.Checked;
        var progress = Progress();
        await Run(token => Task.Run(async () => { await _manager.ImportAsync(_purpose, file, _root, replace, token, progress); }, token));
    }

    private async Task Download()
    {
        using var dialog = new ModelDownloadForm(_purpose);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Download is not ModelDownload download) return;
        bool replace = _replace.Checked;
        var progress = Progress();
        await Run(token => Task.Run(async () => { await _manager.DownloadAsync(download, _root, replace, token, progress); }, token));
    }

    private async Task ValidateModel()
    {
        if (_models.SelectedItem is not ModelDetails selected) return;
        if (_purpose == ModelPurpose.Translation)
        {
            _status.Text = "Cannot validate: no production translation runtime is included in this build.";
            return;
        }
        await Run(async token =>
        {
            try
            {
                await ModelUse.RunAsync(_root, () => new TesseractOcrEngine().ValidateLanguageAsync(_root,
                    Path.GetFileNameWithoutExtension(selected.Location), token));
                _loadFailures.Remove(selected.Location);
            }
            catch (OcrModelLoadException error) { _loadFailures[selected.Location] = error.Message; throw; }
        }, afterSuccess: () =>
        {
            if (_models.SelectedItem is ModelDetails model && model.Location == selected.Location)
                _details.Text = (model with { State = ModelInstallState.Validated, Explanation = "Tesseract successfully loaded these OCR files." }).Describe();
        });
    }

    private async Task Remove()
    {
        if (_models.SelectedItem is not ModelDetails selected) return;
        if (MessageBox.Show(this, $"Remove {selected.Name}?\r\n{selected.Purpose}: {selected.Language}\r\n{selected.Location}\r\n\r\n" +
            $"Only {selected.Files.Count} identified file(s) will be removed. This may make your selected language unavailable.",
            "Confirm model removal", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await Run(token => Task.Run(() => { token.ThrowIfCancellationRequested(); _manager.Remove(selected, _root); }, token), retryable: false);
    }

    private IProgress<ModelTransferProgress> Progress() => new Progress<ModelTransferProgress>(value =>
    {
        if (IsDisposed || _work is null) return;
        _status.Text = $"{value.Phase}: {ModelDetails.Size(value.Bytes)} / {ModelDetails.Size(value.Total)}";
        _progress.Style = value.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        if (value.Total > 0) _progress.Value = (int)Math.Clamp(value.Bytes * 100 / value.Total.Value, 0, 100);
    });

    private async Task Run(Func<CancellationToken, Task> operation, bool retryable = true, Action? afterSuccess = null)
    {
        if (_work is not null) return;
        _lastOperation = retryable ? operation : null;
        _work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _models.Enabled = false;
        foreach (Control control in _actions.Controls) control.Enabled = false;
        _cancel.Enabled = retryable;
        _status.Text = "Starting local model operation…";
        _progress.Style = ProgressBarStyle.Marquee;
        string result;
        bool success = false;
        try
        {
            // File enumeration and extraction preparation also belong off the UI thread.
            // Capture the WinForms progress reporter before dispatch via the operation closure.
            await operation(_work.Token);
            result = "Operation completed. Availability will refresh when you return to the main window.";
            success = true;
        }
        catch (OperationCanceledException) { result = "Cancelled. Existing usable models were preserved. You can retry."; }
        catch (Exception error) when (Recoverable(error)) { result = "Failed: " + error.Message; }
        finally
        {
            _work.Dispose(); _work = null;
            if (!IsDisposed)
            {
                foreach (Control control in _actions.Controls) control.Enabled = true;
                _cancel.Enabled = false; _retry.Enabled = _lastOperation is not null;
                _progress.Style = ProgressBarStyle.Continuous; _progress.Value = 0;
            }
        }
        if (IsDisposed) return;
        await RefreshModelsAsync();
        if (IsDisposed) return;
        _status.Text = result;
        _retry.Enabled = !success && _lastOperation is not null;
        if (success) afterSuccess?.Invoke();
        if (_closeAfterWork) Close();
    }

    private static bool Recoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        System.Net.Http.HttpRequestException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or OcrModelLoadException;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _work?.Cancel(); _lifetime.Cancel(); _lifetime.Dispose(); _statusTip.Dispose(); }
        base.Dispose(disposing);
    }
}
