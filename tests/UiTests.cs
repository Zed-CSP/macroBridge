using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        }
        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
        }
        private static object Invoke(BridgeForm form, string name, params object[] arguments)
        {
            return typeof(BridgeForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, arguments);
        }
        private static IList Rows(BridgeForm form) { return Field<IList>(form, "macroRows"); }
        private static BridgeProfile Draft(BridgeForm form) { return (BridgeProfile)Invoke(form, "ReadFields"); }
        private static BridgeSettings Settings(BridgeForm form) { return Field<BridgeSettings>(form, "settings"); }

        [STAThread]
        private static int Main()
        {
            string sentinel = ConfigStore.PathName;
            bool createdSentinel = false;
            try
            {
                // Even an invalid saved config must be ignored by the sample-data editor.
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
                    form.CreateControl(); IntPtr handle = form.Handle;
                    Assert(handle != IntPtr.Zero, "Preview creates an off-screen form.");
                    Assert(Field<string>(form, "startupError") == null, "Preview must not read invalid local settings.");
                    Assert(!Field<NotifyIcon>(form, "tray").Visible, "Preview must not create a tray icon.");
                    Assert(Field<Dictionary<int, string>>(form, "registered").Count == 0, "Preview must not reserve any hotkeys.");
                    Assert(Field<TextBox>(form, "password").Text == "", "Preview must not display a saved password.");
                    Assert(Rows(form).Count == 6 && Draft(form).Macros.All(macro => macro.Scene.Length > 0), "Six sample routes must be present.");
                    Assert(Field<ComboBox>(form, "profiles").Items.Count == 2, "The editor must offer saved sample profiles.");
                    string originalId = Settings(form).ActiveProfileId, studioId = Settings(form).Profiles[1].Id;

                    Render(form, "ui-default.png");
                    form.Size = form.MinimumSize; Render(form, "ui-minimum.png");
                    foreach (object row in Rows(form))
                    {
                        ComboBox scene = Field<ComboBox>(row, "Scene");
                        Assert(scene.Width > 160 && scene.Height >= 24, "Scene selectors must remain usable at minimum window size.");
                    }
                    form.ClientSize = new Size(1140, 980);

                    Field<ComboBox>(Rows(form)[0], "Scene").Text = "";
                    Assert(Field<Label>(form, "routeCount").Text.StartsWith("05"), "Clearing a scene must update the assignment count.");
                    string exactScene = " Caf\u00e9 / \"wide\" ";
                    Field<ComboBox>(Rows(form)[0], "Scene").Text = exactScene;
                    Field<ComboBox>(Rows(form)[0], "Shortcut").SelectedIndex = 7;
                    Field<NumericUpDown>(Rows(form)[0], "Desktop").Value = 10;
                    Assert(Draft(form).Macros[0].Scene == exactScene && Draft(form).Macros[0].FunctionKey == 8 && Draft(form).Macros[0].Desktop == 10,
                        "The editor must preserve exact scenes and read independent shortcut and desktop choices.");
                    Assert(Settings(form).Active().Macros[0].FunctionKey == 1, "Draft edits must not silently change active hotkeys.");

                    MacroBinding retained = Draft(form).Macros[2].Copy(); string removedId = Draft(form).Macros[1].Id;
                    Invoke(form, "RemoveMacro", removedId);
                    Assert(Rows(form).Count == 5 && Draft(form).Macros[1].Id == retained.Id && Draft(form).Macros[1].Number == 3,
                        "Removing G2 must keep G3 and its binding intact.");
                    Invoke(form, "AddMacro");
                    MacroBinding added = Draft(form).Macros.Last();
                    Assert(Rows(form).Count == 6 && added.Number == 2 && added.FunctionKey == 2, "Adding a macro must fill an available slot.");
                    Field<TextBox>(form, "profileName").Text = "Streaming / custom";
                    Assert((bool)Invoke(form, "SaveProfile"), "Saving a valid edited profile must succeed.");
                    Assert(Settings(form).Active().Name == "Streaming / custom" && Settings(form).Active().Macros[0].Scene == exactScene, "Saved names and edited macros must become active.");

                    Field<ComboBox>(Rows(form)[0], "Scene").Text = exactScene + "saved on switch";
                    Assert((bool)Invoke(form, "SwitchProfile", studioId), "Selecting a different profile must succeed.");
                    Assert(Rows(form).Count == 3 && Settings(form).ActiveProfileId == studioId, "Selecting a profile must rebuild its macro rows.");
                    Assert(Settings(form).Profiles.Single(profile => profile.Id == originalId).Macros[0].Scene == exactScene + "saved on switch",
                        "Switching profiles must preserve outgoing unsaved edits.");
                    Assert(!Field<ObsClient>(form, "obs").IsConnected, "Switching profiles must not connect to OBS.");

                    int generation = Field<int>(form, "profileGeneration");
                    Type pendingType = typeof(BridgeForm).GetNestedType("PendingPress", BindingFlags.NonPublic);
                    object pending = Activator.CreateInstance(pendingType, true);
                    SetField(pending, "MacroId", Settings(form).Active().Macros[0].Id); SetField(pending, "Generation", generation);
                    SetField(form, "pendingHotkey", pending);
                    Assert((bool)Invoke(form, "SwitchProfile", originalId) && Field<object>(form, "pendingHotkey") == null
                        && Field<int>(form, "profileGeneration") > generation, "Activation must discard queued hotkeys from the previous profile.");

                    SemaphoreSlim gate = Field<SemaphoreSlim>(form, "operation");
                    Assert(gate.Wait(0), "The operation gate must be idle in preview.");
                    try { Assert(!(bool)Invoke(form, "SwitchProfile", studioId) && Settings(form).ActiveProfileId == originalId, "Profiles must not change while a macro is in progress."); }
                    finally { gate.Release(); }

                    int originalShortcut = Field<ComboBox>(Rows(form)[1], "Shortcut").SelectedIndex;
                    Field<ComboBox>(Rows(form)[1], "Shortcut").SelectedIndex = Field<ComboBox>(Rows(form)[0], "Shortcut").SelectedIndex;
                    Assert(!(bool)Invoke(form, "SaveProfile"), "Duplicate shortcuts must prevent saving.");
                    Assert(!(bool)Invoke(form, "SwitchProfile", studioId) && Settings(form).ActiveProfileId == originalId,
                        "Invalid outgoing edits must prevent profile switching instead of being lost.");
                    Field<ComboBox>(Rows(form)[1], "Shortcut").SelectedIndex = originalShortcut;

                    Invoke(form, "CreateProfile", true);
                    BridgeProfile copied = Settings(form).Active();
                    Assert(Settings(form).Profiles.Count == 3 && copied.Id != originalId && copied.Macros.Count == 6, "Copy must create and select an independent saved profile.");
                    Field<ComboBox>(Rows(form)[0], "Scene").Text = "Copy only";
                    Invoke(form, "SaveProfile");
                    Assert(Settings(form).Profiles.Single(profile => profile.Id == originalId).Macros[0].Scene != "Copy only", "Editing a copied profile must not change the source.");
                    Invoke(form, "DeleteProfile");
                    Assert(Settings(form).Profiles.Count == 2 && Settings(form).ActiveProfileId != copied.Id, "Deleting a profile must select a remaining saved profile.");

                    Invoke(form, "CreateProfile", false);
                    Assert(Rows(form).Count == 0 && Settings(form).Profiles.Count == 3, "New must create an empty profile.");
                    Render(form, "ui-empty-profile.png");
                    for (int i = 0; i < ConfigStore.MaximumMacros; i++) Invoke(form, "AddMacro");
                    Assert(Rows(form).Count == 24 && !Field<Button>(form, "addMacro").Enabled, "Adding macros must support 24 rows and disable addition at the limit.");
                    Assert((bool)Invoke(form, "SaveProfile"), "A profile with 24 distinct macros must save.");
                    Render(form, "ui-24-macros.png");
                    Assert(Field<TableLayoutPanel>(form, "mappings").Height > Field<Panel>(form, "routingScroller").ClientSize.Height
                        && Field<Panel>(form, "routingScroller").AutoScroll, "Long profiles must scroll within the routing panel.");
                    foreach (string id in Draft(form).Macros.Select(macro => macro.Id).ToArray()) Invoke(form, "RemoveMacro", id);
                    Assert(Rows(form).Count == 0 && (bool)Invoke(form, "SaveProfile") && Settings(form).Active().Macros.Count == 0,
                        "Removing all macros must leave a valid empty profile.");
                    while (Settings(form).Profiles.Count > 1) Invoke(form, "DeleteProfile");
                    Invoke(form, "DeleteProfile");
                    Assert(Settings(form).Profiles.Count == 1, "The UI must keep at least one saved profile.");

                    Invoke(form, "SaveSettings");
                    ((Task)Invoke(form, "LoadScenes")).GetAwaiter().GetResult();
                    ((Task)Invoke(form, "ApplyMapping", "sample", true, false, -1)).GetAwaiter().GetResult();
                    Assert(!Field<ObsClient>(form, "obs").IsConnected, "Preview actions must never connect to OBS.");
                    Assert(File.ReadAllBytes(sentinel).SequenceEqual(before), "Preview profile editing must not overwrite local configuration.");
                    Assert(beforeStatus == null ? !File.Exists(statusPath) : File.ReadAllBytes(statusPath).SequenceEqual(beforeStatus), "Preview actions must not write runtime status.");
                    string sampleSecret = String.Concat("sample", "-password-for-redaction");
                    Field<TextBox>(form, "password").Text = sampleSecret;
                    Invoke(form, "Report", "Could not connect: " + sampleSecret, true);
                    Assert(!Field<Label>(form, "status").Text.Contains(sampleSecret), "Console errors must redact entered passwords.");
                    Assert(Field<TextBox>(form, "password").UseSystemPasswordChar, "The password field must stay masked.");
                }
                Console.WriteLine("PASS: " + assertions + " macro editing, profile switching, layout, isolation, and redaction checks.");
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
