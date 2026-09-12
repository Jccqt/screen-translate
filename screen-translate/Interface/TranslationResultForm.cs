using System.Runtime.InteropServices;
using screen_translate.Translation;

namespace screen_translate.Interface;

/// <summary>Common in-memory result and explicit copy controls for both processing paths.</summary>
public sealed class TranslationResultForm : ThemedForm
{
    public TranslationResultForm(TranslationResult result, Action<string>? copyText = null)
    {
        copyText ??= text => Clipboard.SetText(text);
        Text = "Screen Translate · Result";
        Name = "TranslationResult";
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 430);
        MinimumSize = new Size(460, 360);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        TextBox TextArea(string name, string text) => new()
        {
            Name = name, AccessibleName = name == "OriginalText" ? "Original text" : "Output text",
            Text = text, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill
        };
        layout.Controls.Add(new Label { Text = "Original text", AutoSize = true });
        layout.Controls.Add(TextArea("OriginalText", result.Original.Text));
        layout.Controls.Add(new Label
        {
            Name = "ResultStatus", AutoSize = true,
            Text = result.TranslationSkipped ? "Output · Same language; translation skipped" : "Translated output"
        });
        layout.Controls.Add(TextArea("OutputText", result.OutputText));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var error = new Label { Name = "CopyError", AutoSize = true, Dock = DockStyle.Fill };
        void AddCopy(string title, string name, string text)
        {
            var button = new PillButton { Name = name, Text = title, AutoSize = true };
            button.Click += (_, _) =>
            {
                try { copyText(text); error.Text = "Copied."; }
                catch (Exception failure) when (failure is ExternalException or InvalidOperationException)
                { error.Text = "Could not copy text. Try again, or select the text and press Ctrl+C."; }
            };
            actions.Controls.Add(button);
        }
        AddCopy("Copy original", "CopyOriginal", result.Original.Text);
        AddCopy("Copy output", "CopyOutput", result.OutputText);
        var close = new PillButton { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        actions.Controls.Add(close);
        CancelButton = close;
        layout.Controls.Add(actions);
        layout.Controls.Add(error);
        Controls.Add(layout);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 10 2004+ omits this overlay from supported capture APIs. The capture
        // workflow also removes it and waits for desktop composition before using GDI.
        _ = CaptureProtection.TryExclude(Handle);
    }

}
