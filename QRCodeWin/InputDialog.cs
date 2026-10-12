using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace QRCodeWin
{
    /// <summary>
    /// Small text prompt with OK / Cancel, the Windows counterpart of the
    /// AppleScript "display dialog ... default answer" in the Mac version.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    internal sealed class InputDialog : Form
    {
        private const string AboutUrl = "https://github.com/carstenbund/qr-windows";

        private readonly TextBox textBox;
        private readonly CheckBox largeBox;

        private InputDialog(string title, string prompt)
        {
            Text = title;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); // app.ico
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(420, 130);

            var icon = new PictureBox
            {
                Image = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(14, 14)
            };

            var label = new Label
            {
                Text = prompt,
                AutoSize = true,
                Location = new Point(60, 16)
            };

            textBox = new TextBox
            {
                Location = new Point(60, 42),
                Width = 346,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            largeBox = new CheckBox
            {
                Text = "Large (for print)",
                AutoSize = true,
                Location = new Point(60, 92)
            };

            var about = new LinkLabel
            {
                Text = "About",
                AutoSize = true,
                Location = new Point(14, 94),
                TabStop = false
            };
            about.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(AboutUrl) { UseShellExecute = true }); }
                catch { /* no browser available: ignore */ }
            };

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(250, 88),
                Size = new Size(75, 26)
            };

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(331, 88),
                Size = new Size(75, 26)
            };

            Controls.AddRange(new Control[] { icon, label, textBox, largeBox, about, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>
        /// Shows the prompt; returns the entered text, or null if cancelled.
        /// large = true if the "Large (for print)" box was ticked.
        /// </summary>
        public static string Ask(string title, string prompt, out bool large)
        {
            using (var dlg = new InputDialog(title, prompt))
            {
                bool ok = dlg.ShowDialog() == DialogResult.OK;
                large = ok && dlg.largeBox.Checked;
                return ok ? dlg.textBox.Text : null;
            }
        }
    }
}
