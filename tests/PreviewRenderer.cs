using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal static class PreviewRenderer
    {
        internal static Bitmap Render(Control root)
        {
            // Create the top-level native handle while it is hidden, then make
            // only the managed layout tree visible. No Show/ShowWindow call is
            // made, and the desktop is never captured or changed.
            System.IntPtr hiddenHandle = root.Handle;
            typeof(Control).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(root, new object[] { 2, true });
            root.CreateControl();
            Layout(root);
            Bitmap image = new Bitmap(root.ClientSize.Width, root.ClientSize.Height);
            using (Graphics graphics = Graphics.FromImage(image)) Paint(root, graphics, Point.Empty);
            return image;
        }
        private static void Layout(Control control)
        {
            control.PerformLayout();
            foreach (Control child in control.Controls) Layout(child);
            ComboBox scene = control as ComboBox;
            if (scene != null) { System.IntPtr sceneHandle = scene.Handle; scene.Select(scene.Text.Length, 0); }
        }
        private static void Paint(Control control, Graphics graphics, Point origin)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            // Hidden forms do not print their child windows. Render each of our
            // controls independently, without displaying or capturing the desktop.
            using (Bitmap layer = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(layer, new Rectangle(Point.Empty, control.Size));
                ComboBox selector = control as ComboBox;
                // Hidden native dropdown lists omit their owner-drawn selection during WM_PRINT.
                // Draw the same selected text and colors used by SceneSelector.OnDrawItem.
                if (selector != null && selector.DropDownStyle == ComboBoxStyle.DropDownList && selector.SelectedIndex >= 0)
                {
                    using (Graphics selected = Graphics.FromImage(layer))
                    using (Brush fill = new SolidBrush(selector.BackColor))
                    {
                        Rectangle field = new Rectangle(2, 2, Math.Max(1, selector.Width - SystemInformation.VerticalScrollBarWidth - 4), Math.Max(1, selector.Height - 4));
                        selected.FillRectangle(fill, field);
                        field.X += 6; field.Width -= 6;
                        TextRenderer.DrawText(selected, selector.GetItemText(selector.SelectedItem), selector.Font, field, selector.ForeColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                }
                graphics.DrawImageUnscaled(layer, origin);
            }
            System.Drawing.Drawing2D.GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(new Rectangle(origin, control.ClientSize), System.Drawing.Drawing2D.CombineMode.Intersect);
                for (int index = control.Controls.Count - 1; index >= 0; index--)
                {
                    Control child = control.Controls[index];
                    Paint(child, graphics, new Point(origin.X + child.Left, origin.Y + child.Top));
                }
            }
            finally { graphics.Restore(state); }
        }
    }
}
