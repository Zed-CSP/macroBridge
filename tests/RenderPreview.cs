using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
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
                    BridgeSettings samples = (BridgeSettings)typeof(BridgeForm).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    bool switched = (bool)typeof(BridgeForm).GetMethod("SwitchProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { samples.Profiles[1].Id });
                    if (!switched) throw new InvalidOperationException("The alternate sample profile could not be rendered.");
                    form.ClientSize = new Size(1140, 840);
                    using (Bitmap bitmap = PreviewRenderer.Render(form))
                    {
                        bitmap.Save(Path.Combine(previews, "profiles-preview.png"), ImageFormat.Png);
                    }
                }
                Console.WriteLine("Rendered both sample profile previews and the application icon.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
