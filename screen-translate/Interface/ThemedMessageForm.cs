namespace screen_translate.Interface;

/// <summary>App-owned feedback and confirmations follow the selected theme; OS file pickers remain native.</summary>
public sealed class ThemedMessageForm : ThemedForm
{
    public ThemedMessageForm(string message, string title, bool confirm = false)
    {
        Text = title;
        Name = "ApplicationMessage";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Segoe UI", 10);
        Size = new Size(700, 500);
        MinimumSize = new Size(460, 320);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new TextBox { Name = "MessageText", Text = message, ReadOnly = true, Multiline = true,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = title });
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new PillButton { Text = confirm ? "Cancel" : "OK", Name = "DismissMessage", AutoSize = true,
            Padding = new Padding(12, 6, 12, 6), DialogResult = confirm ? DialogResult.Cancel : DialogResult.OK };
        actions.Controls.Add(cancel);
        CancelButton = cancel;
        AcceptButton = cancel; // Destructive confirmations default to cancellation.
        if (confirm) actions.Controls.Add(new PillButton { Text = "Remove model", Name = "ConfirmMessage", AutoSize = true,
            Padding = new Padding(12, 6, 12, 6), DialogResult = DialogResult.Yes });
        layout.Controls.Add(actions);
        Controls.Add(layout);
    }

    public static DialogResult Show(Form owner, string message, string title, bool confirm = false)
    {
        using var dialog = new ThemedMessageForm(message, title, confirm);
        return dialog.ShowDialog(owner);
    }
}
