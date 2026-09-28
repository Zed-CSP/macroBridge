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
                graphics.DrawImageUnscaled(layer, origin);
            }
            for (int index = control.Controls.Count - 1; index >= 0; index--)
            {
                Control child = control.Controls[index];
                Paint(child, graphics, new Point(origin.X + child.Left, origin.Y + child.Top));
            }
        }
    }
}
