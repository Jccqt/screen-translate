namespace screen_translate.Interface;

internal sealed class TranslationProgressForm : ThemedForm
{
    private readonly Label _status;
    public TranslationProgressForm(Action cancel)
    {
        Name = "TranslationProgress";
        Text = "Screen Translate";
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(340, 128);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _status = new Label { Name = "OperationStatus", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var button = new PillButton { Name = "CancelTranslation", Text = "Cancel", AutoSize = true };
        button.Click += (_, _) => cancel();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { cancel(); e.Cancel = true; } };
        CancelButton = button;
        layout.Controls.Add(_status);
        layout.Controls.Add(button);
        Controls.Add(layout);
    }

    public void SetStatus(string status) => _status.Text = status;
}
