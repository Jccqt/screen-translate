using System.ComponentModel;
using screen_translate.Models;

namespace screen_translate.Interface;

/// <summary>Nothing accesses the network until the user reviews the final descriptor and starts.</summary>
public sealed class ModelDownloadForm : Form
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModelDownload? Download { get; private set; }

    public ModelDownloadForm(ModelPurpose purpose)
    {
        Text = "Review model download"; Name = "ModelDownloadReview";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(660, 580); Size = new Size(780, 700);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        TextBox Field(string label, int row, string value)
        {
            layout.RowStyles.Add(new(SizeType.Absolute, 36));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var field = new TextBox { Text = value, Dock = DockStyle.Fill, Name = "Download" + label.Replace(" ", "") };
            layout.Controls.Add(field, 1, row); return field;
        }
        bool ocr = purpose == ModelPurpose.Ocr;
        var name = Field("Name", 0, ocr ? "Tesseract English fast" : "Translation package");
        var filename = Field("Package filename", 1, ocr ? "eng.traineddata" : "model.argosmodel");
        var source = Field("HTTPS source", 2, ocr ? "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata" : "");
        var license = Field("License", 3, ocr ? "Apache-2.0 — https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/LICENSE" : "Unknown");
        var size = Field("Download bytes", 4, ocr ? "4113088" : "Unknown");
        var checksum = Field("Publisher SHA-256", 5, ocr ? "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2" : "Unknown");
        var version = Field("Version", 6, ocr ? "4.1.0" : "Unknown");
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        var preview = new TextBox { Name = "DownloadDisclosure", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        layout.Controls.Add(preview, 0, 7); layout.SetColumnSpan(preview, 2);
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        var start = new Button { Text = "Start download", Name = "StartModelDownload", AutoSize = true, Dock = DockStyle.Right };
        layout.Controls.Add(start, 1, 8);
        Controls.Add(layout);
        ModelDownload Descriptor()
        {
            if (!Uri.TryCreate(source.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
                throw new ArgumentException("Enter the publisher's final HTTPS file URL, without credentials.");
            string file = filename.Text.Trim();
            if (file != Path.GetFileName(file) || (ocr ? !file.EndsWith(".traineddata", StringComparison.OrdinalIgnoreCase) :
                !file.EndsWith(".argosmodel", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Enter an accepted package filename with no folder path.");
            long? bytes = null;
            if (size.Text.Trim() is not ("" or "Unknown"))
            {
                if (!long.TryParse(size.Text, out long parsed) || parsed <= 0) throw new ArgumentException("Download bytes must be positive, or Unknown.");
                bytes = parsed;
            }
            string? hash = checksum.Text.Trim() is "" or "Unknown" ? null : checksum.Text.Trim();
            if (hash is not null && (hash.Length != 64 || !hash.All(Uri.IsHexDigit))) throw new ArgumentException("Enter a trusted published 64-character SHA-256, or Unknown.");
            return new(string.IsNullOrWhiteSpace(name.Text) ? "Unknown" : name.Text.Trim(), purpose, file, uri,
                string.IsNullOrWhiteSpace(license.Text) ? "Unknown" : license.Text.Trim(), bytes, hash,
                string.IsNullOrWhiteSpace(version.Text) ? "Unknown" : version.Text.Trim());
        }
        void Review()
        {
            try
            {
                preview.Text = Descriptor().Describe() + "\r\n\r\nOnly start after reviewing the publisher's license and checksum. " +
                    "Source/license fields for custom downloads are supplied by you. No model is bundled or redistributed. Downloads do not resume.";
                start.Enabled = true;
            }
            catch (ArgumentException error) { preview.Text = error.Message; start.Enabled = false; }
        }
        source.TextChanged += (_, _) => { checksum.Text = "Unknown"; license.Text = "Unknown"; size.Text = "Unknown"; version.Text = "Unknown"; };
        foreach (var field in new[] { name, filename, source, license, size, checksum, version }) field.TextChanged += (_, _) => Review();
        start.Click += (_, _) => { Download = Descriptor(); DialogResult = DialogResult.OK; Close(); };
        Review();
    }
}
