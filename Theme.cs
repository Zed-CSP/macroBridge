using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal static class BridgeTheme
    {
        internal static readonly Color Background = Color.FromArgb(8, 14, 20);
        internal static readonly Color Surface = Color.FromArgb(13, 23, 31);
        internal static readonly Color Input = Color.FromArgb(8, 18, 25);
        internal static readonly Color Line = Color.FromArgb(39, 67, 77);
        internal static readonly Color Cyan = Color.FromArgb(66, 232, 224);
        internal static readonly Color Pink = Color.FromArgb(255, 100, 159);
        internal static readonly Color Text = Color.FromArgb(231, 240, 242);
        internal static readonly Color Muted = Color.FromArgb(153, 177, 187);
        internal static readonly Color Amber = Color.FromArgb(255, 205, 113);

        internal static Font Mono(float size, bool bold = false)
        {
            return new Font("Consolas", size, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        internal static Label Caption(string text, Color color)
        {
            return new Label { Text = text, ForeColor = color, Font = Mono(9F),
                AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0), UseMnemonic = false };
        }

        internal static Control Field(Control input)
        {
            input.BackColor = Input; input.ForeColor = Text; input.Font = Mono(11F);
            input.Dock = DockStyle.Fill; input.Margin = new Padding(0);
            TextBox text = input as TextBox;
            if (text != null) text.BorderStyle = BorderStyle.None;
            NumericUpDown number = input as NumericUpDown;
            if (number != null) number.BorderStyle = BorderStyle.None;
            TerminalPanel frame = new TerminalPanel { BackColor = Input, Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 5), Margin = new Padding(0, 0, 12, 0) };
            frame.Controls.Add(input);
            input.Enter += (s, e) => { frame.Accent = Cyan; frame.Invalidate(); };
            input.Leave += (s, e) => { frame.Accent = Line; frame.Invalidate(); };
            return frame;
        }

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        internal static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                using (Pen cyan = new Pen(Cyan, 5))
                using (Pen pink = new Pen(Pink, 5))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Background);
                    g.DrawRectangle(cyan, 8, 12, 19, 28); g.DrawRectangle(pink, 37, 24, 19, 28);
                    g.DrawLine(cyan, 27, 26, 38, 26); g.DrawLine(cyan, 32, 21, 38, 26);
                    g.DrawLine(pink, 38, 26, 32, 31); g.DrawLine(cyan, 13, 48, 23, 48);
                    g.DrawLine(pink, 42, 16, 52, 16);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    internal sealed class TerminalPanel : Panel
    {
        internal Color Accent = BridgeTheme.Line;
        internal TerminalPanel() { DoubleBuffered = true; BackColor = BridgeTheme.Surface; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Accent))
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
    }

    // Inherit keyboard activation, focus navigation, and accessibility from Button.
    internal sealed class TerminalButton : Button
    {
        internal bool Primary;
        private bool hovering;
        internal TerminalButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = BridgeTheme.Mono(10F, true); Cursor = Cursors.Hand;
            BackColor = BridgeTheme.Surface; ForeColor = BridgeTheme.Text;
        }
        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color accent = Enabled ? BridgeTheme.Cyan : BridgeTheme.Line;
            Color fill = Primary && Enabled ? (hovering ? Color.FromArgb(112, 255, 239) : accent) :
                (hovering && Enabled ? Color.FromArgb(21, 45, 53) : BridgeTheme.Surface);
            Color foreground = !Enabled ? BridgeTheme.Muted : (Primary ? BridgeTheme.Background : BridgeTheme.Text);
            using (Brush background = new SolidBrush(fill)) e.Graphics.FillRectangle(background, ClientRectangle);
            using (Pen edge = new Pen(hovering || Primary ? accent : BridgeTheme.Line))
                e.Graphics.DrawRectangle(edge, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (Brush stripe = new SolidBrush(Enabled ? BridgeTheme.Pink : BridgeTheme.Line))
                e.Graphics.FillRectangle(stripe, Math.Max(0, Width - 4), 0, 4, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(4, 0, Width - 12, Height), foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                using (Pen focus = new Pen(Primary ? BridgeTheme.Background : BridgeTheme.Cyan) { DashStyle = DashStyle.Dot })
                    e.Graphics.DrawRectangle(focus, 5, 5, Math.Max(0, Width - 14), Math.Max(0, Height - 11));
            }
        }
    }

    internal sealed class SceneSelector : ComboBox
    {
        internal SceneSelector()
        {
            BackColor = BridgeTheme.Input; ForeColor = BridgeTheme.Text;
            Font = BridgeTheme.Mono(11F); FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 25;
            DropDownStyle = ComboBoxStyle.DropDown; IntegralHeight = false;
            DropDownHeight = 250; MaxDropDownItems = 8;
            AutoCompleteMode = AutoCompleteMode.SuggestAppend; AutoCompleteSource = AutoCompleteSource.ListItems;
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush fill = new SolidBrush(selected ? BridgeTheme.Line : BridgeTheme.Input)) e.Graphics.FillRectangle(fill, e.Bounds);
            string text = GetItemText(Items[e.Index]);
            if (String.IsNullOrEmpty(text)) text = "(unassigned)";
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                selected ? BridgeTheme.Cyan : BridgeTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
    }

    internal sealed class BridgeHeader : Control
    {
        internal BridgeHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "Streaming Bridge. One key. Two systems. Your profiles.";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BridgeTheme.Background);
            float scale = g.DpiX / 96F; g.ScaleTransform(scale, scale);
            float width = Width / scale, height = Height / scale;
            using (Pen grid = new Pen(Color.FromArgb(18, 37, 45)))
            {
                for (float x = width - 440; x < width; x += 32) g.DrawLine(grid, x, 0, x, height - 13);
                for (int y = 5; y < height - 13; y += 20) g.DrawLine(grid, width - 440, y, width, y);
            }
            using (Font small = new Font("Consolas", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font title = new Font("Consolas", 38F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font body = new Font("Consolas", 13F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush cyan = new SolidBrush(BridgeTheme.Cyan))
            using (Brush pink = new SolidBrush(BridgeTheme.Pink))
            using (Brush text = new SolidBrush(BridgeTheme.Text))
            using (Brush muted = new SolidBrush(BridgeTheme.Muted))
            {
                g.FillRectangle(pink, 1, 8, 5, 5); g.DrawString("PERET / TOOLS / 01", small, cyan, 15, 3);
                g.DrawString("STREAMING", title, text, -3, 22); g.DrawString("BRIDGE_", title, cyan, 211, 22);
                g.DrawString("One key. Two systems. Your profiles.", body, muted, 0, 75);
                if (width > 820)
                {
                    float start = width - 355; string[] names = { "MACROS", "WINDOWS", "OBS" };
                    using (Pen line = new Pen(BridgeTheme.Cyan, 1))
                    using (Pen border = new Pen(BridgeTheme.Line, 1))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            float x = start + i * 126;
                            g.DrawRectangle(border, x, 34, 100, 40); g.FillRectangle(i == 2 ? pink : cyan, x, 34, 3, 40);
                            g.DrawString(names[i], small, i == 2 ? pink : text, x + 16, 47);
                            if (i < 2)
                            {
                                g.DrawLine(line, x + 100, 54, x + 121, 54);
                                g.DrawLine(line, x + 117, 50, x + 121, 54); g.DrawLine(line, x + 117, 58, x + 121, 54);
                            }
                        }
                    }
                    g.DrawString("DESKTOP + PROGRAM SCENE", small, muted, start + 3, 82);
                }
                g.FillRectangle(cyan, 0, height - 10, width * 0.68F, 2);
                g.FillRectangle(pink, width * 0.68F, height - 10, width * 0.12F, 2);
                g.FillRectangle(muted, width * 0.8F, height - 10, width * 0.2F, 2);
            }
            using (Pen scanline = new Pen(Color.FromArgb(30, 8, 14, 20)))
                for (int y = 0; y < height - 13; y += 4) g.DrawLine(scanline, 0, y, width, y);
        }
    }
}
