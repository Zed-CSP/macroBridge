using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal static class UiTests
    {
        private static int assertions;
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
        }
        private static T Field<T>(BridgeForm form, string name)
        {
            return (T)typeof(BridgeForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
        private static object Invoke(BridgeForm form, string name, params object[] arguments)
        {
            return typeof(BridgeForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, arguments);
        }
        [STAThread]
        private static int Main()
        {
            string sentinel = ConfigStore.PathName;
            bool createdSentinel = false;
            try
            {
                // The renderer must ignore even an invalid saved config.
                if (!File.Exists(sentinel))
                {
                    using (FileStream file = new FileStream(sentinel, FileMode.CreateNew))
                    using (StreamWriter writer = new StreamWriter(file)) writer.Write("preview-test: invalid configuration");
                    createdSentinel = true;
                }
                byte[] before = File.ReadAllBytes(sentinel);
                string statusPath = Path.Combine(ConfigStore.Root, "status.json");
                byte[] beforeStatus = File.Exists(statusPath) ? File.ReadAllBytes(statusPath) : null;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (BridgeForm form = new BridgeForm(true, true))
                {
                    form.CreateControl();
                    IntPtr handle = form.Handle;
                    Assert(handle != IntPtr.Zero, "Preview creates an off-screen form.");
                    Assert(Field<string>(form, "startupError") == null, "Preview must not read invalid local settings.");
                    Assert(!Field<NotifyIcon>(form, "tray").Visible, "Preview must not create a tray icon.");
                    Assert(Array.TrueForAll(Field<bool[]>(form, "registered"), value => !value), "Preview must not reserve any hotkeys.");
                    Assert(Field<TextBox>(form, "password").Text == "", "Preview must not display a saved password.");
                    ComboBox[] scenes = Field<ComboBox[]>(form, "scenes");
                    Assert(scenes.Length == 6 && Array.TrueForAll(scenes, scene => scene.Text.Length > 0), "Six sample routes must be present.");
                    scenes[0].Text = "";
                    Assert(Field<Label>(form, "routeCount").Text.StartsWith("05"), "Clearing a scene updates the assignment count.");
                    scenes[0].Text = " Caf\u00e9 / \"wide\" ";
                    BridgeConfig values = (BridgeConfig)Invoke(form, "ReadFields");
                    Assert(values.Scenes[0] == scenes[0].Text, "Editing must preserve Unicode, punctuation, and significant spaces.");
                    Invoke(form, "SaveSettings");
                    ((Task)Invoke(form, "LoadScenes")).GetAwaiter().GetResult();
                    ((Task)Invoke(form, "ApplyMapping", 0, true, false)).GetAwaiter().GetResult();
                    Assert(!Field<ObsClient>(form, "obs").IsConnected, "Preview actions must never connect to OBS.");
                    Assert(Convert.ToBase64String(File.ReadAllBytes(sentinel)) == Convert.ToBase64String(before), "Preview actions must not overwrite configuration.");
                    Assert(beforeStatus == null ? !File.Exists(statusPath) : Convert.ToBase64String(File.ReadAllBytes(statusPath)) == Convert.ToBase64String(beforeStatus), "Preview actions must not write runtime status.");
                    string sampleSecret = String.Concat("sample", "-password-for-redaction");
                    Field<TextBox>(form, "password").Text = sampleSecret;
                    Invoke(form, "Report", "Could not connect: " + sampleSecret, true);
                    Assert(!Field<Label>(form, "status").Text.Contains(sampleSecret), "Console errors must redact entered passwords.");
                    Assert(Field<TextBox>(form, "password").UseSystemPasswordChar, "The password field must stay masked.");
                    Render(form, "ui-default.png");
                    form.Size = form.MinimumSize;
                    Render(form, "ui-minimum.png");
                    foreach (ComboBox scene in scenes)
                        Assert(scene.Width > 160 && scene.Height >= 24, "Scene selectors must remain usable at the minimum window size.");
                }
                Console.WriteLine("PASS: " + assertions + " UI, preview isolation, and password redaction checks.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.GetBaseException().Message); return 1; }
            finally { if (createdSentinel) File.Delete(sentinel); }
        }
        private static void Render(BridgeForm form, string name)
        {
            using (Bitmap image = PreviewRenderer.Render(form))
            {
                image.Save(Path.Combine(ConfigStore.Root, name));
                int cyanPixels = 0;
                for (int y = 0; y < image.Height; y += 3)
                    for (int x = 0; x < image.Width; x += 3)
                    {
                        Color pixel = image.GetPixel(x, y);
                        if (pixel.G > 170 && pixel.B > 150 && pixel.R < 140) cyanPixels++;
                    }
                Assert(cyanPixels > 500, "Preview must render the interface, including its cyan controls.");
            }
        }
    }
}
