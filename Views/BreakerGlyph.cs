// IEDExplorer GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IEDExplorer.Views
{
    /// <summary>
    /// IEC 61850 DPC switch symbol. State comes from Pos.stVal (never from the
    /// last command issued). Clicking requests a command dialog; it does not
    /// itself send a control operation.
    /// </summary>
    internal sealed class BreakerGlyph : Control
    {
        internal BreakerPosition Position = BreakerPosition.Unknown;
        internal bool Online;
        internal bool QualityGood;
        internal string StatusText = "EJ AVLÄST";

        internal BreakerGlyph()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(224, 118);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color ink = Color.FromArgb(33, 55, 76);
            Color accent = !Online || !QualityGood ? Color.FromArgb(139, 149, 160) :
                Position == BreakerPosition.Open ? Color.FromArgb(27, 143, 102) :
                Position == BreakerPosition.Closed ? Color.FromArgb(191, 63, 66) :
                Color.FromArgb(206, 145, 56);
            int x = Width / 2;
            using (var line = new Pen(ink, 3f))
            using (var switchPen = new Pen(accent, 4f))
            using (var fill = new SolidBrush(accent))
            using (var badgeFill = new SolidBrush(Color.FromArgb(245, 247, 251)))
            using (var font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var stateFont = new Font("Segoe UI", 8f))
            {
                g.DrawLine(line, x, 7, x, 26);
                g.DrawLine(line, x, 81, x, 94);

                // IEC switch contact: open blade tilted, closed blade vertical.
                using (var contacts = new Pen(accent, 2.5f))
                {
                    g.DrawEllipse(contacts, x - 4, 27, 8, 8);
                    g.DrawEllipse(contacts, x - 4, 74, 8, 8);
                }
                if (Position == BreakerPosition.Closed && Online && QualityGood)
                    g.DrawLine(switchPen, x, 35, x, 74);
                else if (Position == BreakerPosition.Open && Online && QualityGood)
                    g.DrawLine(switchPen, x, 74, x + 24, 38);
                else if (Position == BreakerPosition.Intermediate && Online && QualityGood)
                {
                    g.DrawLine(switchPen, x, 35, x - 12, 49);
                    g.DrawLine(switchPen, x + 12, 60, x, 74);
                }
                else
                {
                    g.DrawLine(switchPen, x, 36, x - 12, 47);
                    g.DrawLine(switchPen, x + 12, 60, x, 73);
                    if (Position == BreakerPosition.Bad && Online)
                    {
                        g.DrawString("!", font, fill, x + 31, 44);
                    }
                }

                var rect = new Rectangle(0, 98, Width, 18);
                TextRenderer.DrawText(g, StatusText, font, rect, accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
            }
        }
    }
}
