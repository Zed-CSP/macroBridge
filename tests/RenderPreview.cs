using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal static class RenderPreview
    {
        [STAThread]
        private static int Main(string[] arguments)
        {
            try
            {
                if (arguments.Length != 1) throw new ArgumentException("Pass the repository directory.");
                string root = Path.GetFullPath(arguments[0]);
                string assets = Path.Combine(root, "assets");
                string previews = Path.Combine(root, "docs", "assets");
                Directory.CreateDirectory(assets); Directory.CreateDirectory(previews);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (Icon icon = BridgeTheme.CreateIcon())
                using (FileStream output = File.Create(Path.Combine(assets, "bridge.ico"))) icon.Save(output);
                using (BridgeForm form = new BridgeForm(true, true))
                {
                    form.FormBorderStyle = FormBorderStyle.None;
                    form.CreateControl();
                    // DrawToBitmap renders our own controls off screen; no desktop capture.
                    using (Bitmap bitmap = PreviewRenderer.Render(form))
                    {
                        bitmap.Save(Path.Combine(previews, "bridge-preview.png"), ImageFormat.Png);
                    }
                }
                Console.WriteLine("Rendered the sample-data app preview and application icon.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
