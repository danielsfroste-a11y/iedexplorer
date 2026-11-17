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
            Color ink = Color.FromArgb(35, 54, 74);
            Color accent = !Online || !QualityGood ? Color.FromArgb(148, 158, 171) :
                Position == BreakerPosition.Open ? Color.FromArgb(29, 151, 112) :
                Position == BreakerPosition.Closed ? Color.FromArgb(209, 76, 76) :
                Color.FromArgb(213, 152, 57);
            Color soft = !Online || !QualityGood ? Color.FromArgb(241, 243, 246) :
                Position == BreakerPosition.Open ? Color.FromArgb(232, 248, 242) :
                Position == BreakerPosition.Closed ? Color.FromArgb(253, 238, 237) :
                Color.FromArgb(253, 246, 229);
            Rectangle frame = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath path = Rounded(frame, 14))
            using (Brush bg = new SolidBrush(Color.FromArgb(247, 250, 253)))
            using (Pen border = new Pen(Color.FromArgb(226, 234, 242)))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }
            int x = Width / 2;
            int cy = 53;
            using (Brush halo = new SolidBrush(soft))
                g.FillEllipse(halo, x - 42, cy - 38, 84, 84);
            using (Pen conductor = new Pen(ink, 3f))
            using (Pen contact = new Pen(accent, 3.4f))
            using (Brush contactFill = new SolidBrush(Color.White))
            using (Brush statusBrush = new SolidBrush(accent))
            using (Font font = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                conductor.StartCap = LineCap.Round;
                conductor.EndCap = LineCap.Round;
                contact.StartCap = LineCap.Round;
                contact.EndCap = LineCap.Round;
                g.DrawLine(conductor, x, 7, x, 31);
                g.DrawLine(conductor, x, 75, x, 91);
                g.FillEllipse(contactFill, x - 5, 28, 10, 10);
                g.FillEllipse(contactFill, x - 5, 70, 10, 10);
                g.DrawEllipse(contact, x - 5, 28, 10, 10);
                g.DrawEllipse(contact, x - 5, 70, 10, 10);
                if (Online && QualityGood && Position == BreakerPosition.Closed)
                    g.DrawLine(contact, x, 38, x, 70);
                else if (Online && QualityGood && Position == BreakerPosition.Open)
                    g.DrawLine(contact, x, 72, x + 25, 40);
                else
                {
                    g.DrawLine(contact, x, 38, x - 12, 49);
                    g.DrawLine(contact, x + 12, 59, x, 70);
                    if (Position == BreakerPosition.Bad && Online)
                        g.DrawString("!", font, statusBrush, x + 28, 42);
                }
                Rectangle status = new Rectangle(10, Height - 25, Width - 20, 19);
                TextRenderer.DrawText(g, StatusText, font, status, accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
