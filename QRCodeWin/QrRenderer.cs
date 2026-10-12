using System.Drawing;

namespace QRCodeWin
{
    public static class QrRenderer
    {
        /// <summary>
        /// Renders the QR code as a black-on-white bitmap.
        /// boxSize = pixels per module, border = quiet zone in modules
        /// (same meaning as box_size / border in the Mac version).
        /// </summary>
        public static Bitmap ToBitmap(QrCode qr, int boxSize, int border)
        {
            int pixels = (qr.Size + border * 2) * boxSize;
            var bmp = new Bitmap(pixels, pixels);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < qr.Size; y++)
                    for (int x = 0; x < qr.Size; x++)
                        if (qr[x, y])
                            g.FillRectangle(Brushes.Black,
                                (x + border) * boxSize, (y + border) * boxSize, boxSize, boxSize);
            }
            return bmp;
        }
    }
}
