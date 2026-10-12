using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace QRCodeWin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. Ask for the text / URL
            bool large;
            string data = InputDialog.Ask("QR Code", "Enter text for the QR code:", out large);
            if (data != null) data = data.Trim();
            if (string.IsNullOrEmpty(data)) return; // cancelled or empty

            try
            {
                // 2. Generate the QR code (same settings as the Mac version:
                //    error correction L, 10 px per module, 4-module border).
                //    "Large (for print)" uses 40 px per module instead.
                QrCode qr = QrCode.EncodeText(data, Ecc.L);

                // 3. Save it to the temp folder
                string filePath = Path.Combine(Path.GetTempPath(), "qrcode.png");
                using (var bmp = QrRenderer.ToBitmap(qr, large ? 40 : 10, 4))
                {
                    bmp.Save(filePath, ImageFormat.Png);
                }

                // 4. Open it with the default image viewer
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "QR Code", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
