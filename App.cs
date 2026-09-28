using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal sealed class BridgeConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string ProtectedPassword { get; set; }
        public string[] Scenes { get; set; }
        public BridgeConfig()
        {
            Host = "localhost"; Port = 4455;
            ProtectedPassword = ""; Scenes = new string[6];
        }
    }

    internal static class ConfigStore
    {
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string PathName = Path.Combine(Root, "config.json");
        internal static BridgeConfig Load()
        {
            if (!File.Exists(PathName)) return new BridgeConfig();
            BridgeConfig config = new JavaScriptSerializer().Deserialize<BridgeConfig>(File.ReadAllText(PathName));
            if (config == null || config.Scenes == null || config.Scenes.Length != 6)
                throw new InvalidDataException("The saved configuration must contain six scene assignments.");
            return config;
        }
        internal static string Password(BridgeConfig config)
        {
            return String.IsNullOrEmpty(config.ProtectedPassword) ? "" : Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(config.ProtectedPassword), null, DataProtectionScope.CurrentUser));
        }
        internal static BridgeConfig Make(string host, int port, string password, string[] scenes)
        {
            if (String.IsNullOrWhiteSpace(host) || host.IndexOfAny(new char[] { '/', '\\', ':', '@', '?', '#', ' ', '\t', '\r', '\n' }) >= 0)
                throw new ArgumentException("Enter the OBS computer's IP address or hostname, without ws:// or a port.");
            if (port < 1 || port > 65535) throw new ArgumentException("Enter a port between 1 and 65535.");
            return new BridgeConfig { Host = host.Trim(), Port = port, Scenes = scenes,
                ProtectedPassword = String.IsNullOrEmpty(password) ? "" : Convert.ToBase64String(
                    ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser)) };
        }
        internal static void Save(BridgeConfig config)
        {
            string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(config), new UTF8Encoding(false));
                if (File.Exists(PathName)) File.Replace(temporary, PathName, null);
                else File.Move(temporary, PathName);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class DesktopHelper
    {
        internal static async Task<int> Run(string arguments)
        {
            string executable = Path.Combine(ConfigStore.Root, "desktop", "VirtualDesktop11-24H2.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("The Windows desktop helper is missing.");
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(5000));
                if (!exited)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("Windows desktop selection timed out.");
                }
                await output; await errors;
                if (process.ExitCode < 0) throw new InvalidOperationException("The Windows desktop helper failed (" + process.ExitCode + ").");
                return process.ExitCode;
            }
        }
        internal static async Task Switch(int index)
        {
            int count = await Run("/Quiet /Count");
            if (count <= index) throw new InvalidOperationException("Desktop " + (index + 1) + " does not exist. Create it with Win+Tab first.");
            await Run("/Quiet /Animation:Off /Switch:" + index);
            int current = await Run("/Quiet /GetCurrentDesktop");
            if (current != index) throw new InvalidOperationException("Windows did not select Desktop " + (index + 1) + ".");
        }
    }

    internal sealed partial class BridgeForm : Form
    {
        private readonly TextBox host = new TextBox();
        private readonly NumericUpDown port = new NumericUpDown();
        private readonly TextBox password = new TextBox();
        private readonly ComboBox[] scenes = new ComboBox[6];
        private readonly Button connect = new TerminalButton { Primary = true };
        private readonly Label status = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
        private readonly ObsClient obs = new ObsClient();
        private BridgeConfig saved;
        private string savedPassword = "";
        private string connectedHost = "", connectedPassword = "";
        private int connectedPort;
        private bool exiting, ready;
        private bool drainingHotkeys;
        private int pendingHotkey = -1;
        private readonly bool[] registered = new bool[6];
        private string startupError;
        private readonly bool previewOnly;
        private const int HotkeyBase = 7300, WmHotkey = 0x0312;
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);

        internal BridgeForm(bool forceSettings) : this(forceSettings, false) { }

        // A preview uses sample data and never reads local credentials or registers hotkeys.
        internal BridgeForm(bool forceSettings, bool previewOnly)
        {
            this.previewOnly = previewOnly;
            try { saved = previewOnly ? new BridgeConfig() : ConfigStore.Load(); savedPassword = previewOnly ? "" : ConfigStore.Password(saved); }
            catch (Exception) { saved = new BridgeConfig(); startupError = "Saved settings could not be loaded. Enter the connection and scene assignments again."; }
            Text = "Streaming Bridge // Desktop + OBS";
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1060, 840); MinimumSize = new Size(980, 840);
            Font = BridgeTheme.Mono(11F); BackColor = BridgeTheme.Background; ForeColor = BridgeTheme.Text;
            StartPosition = FormStartPosition.CenterScreen; Icon = BridgeTheme.CreateIcon();
            BuildUi(); host.Text = saved.Host; port.Value = Math.Max(1, Math.Min(65535, saved.Port)); password.Text = savedPassword;
            for (int i = 0; i < 6; i++) { scenes[i].Text = saved.Scenes[i] ?? ""; scenes[i].SelectionStart = scenes[i].Text.Length; scenes[i].SelectionLength = 0; }
            if (previewOnly)
            {
                host.Text = "obs-studio.local";
                string[] examples = { "01 / Main feed", "02 / Code & create", "03 / Gameplay", "04 / Camera", "05 / Intermission", "06 / Signing off" };
                for (int i = 0; i < 6; i++) { scenes[i].Items.Add(""); scenes[i].Items.AddRange(examples); scenes[i].Text = examples[i]; scenes[i].SelectionStart = scenes[i].Text.Length; scenes[i].SelectionLength = 0; }
                hotkeyState.Text = "HOTKEYS / PREVIEW"; hotkeyState.ForeColor = BridgeTheme.Muted;
                Report("Preview uses sample scenes. Load OBS scenes to configure your own six channels.", false);
                return;
            }
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Configure", null, (s, e) => ShowSettings());
            ToolStripMenuItem testMenu = new ToolStripMenuItem("Test saved mapping");
            for (int i = 0; i < 6; i++) { int index = i; testMenu.DropDownItems.Add("G" + (i + 1) + " / Desktop " + (i + 1), null, async (s, e) => await ApplyMapping(index, false)); }
            menu.Items.Add(testMenu); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { exiting = true; Close(); });
            menu.BackColor = BridgeTheme.Surface; menu.ForeColor = BridgeTheme.Text; menu.Font = BridgeTheme.Mono(10F);
            tray.Icon = Icon; tray.Text = "Streaming Bridge"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowSettings();
            FormClosing += (s, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            Shown += (s, e) => {
                ready = true;
                string unavailable = String.Join(", ", Enumerable.Range(0, 6).Where(i => !registered[i]).Select(i => "G" + (i + 1)));
                hotkeyState.Text = "HOTKEYS / " + registered.Count(value => value) + " OF 6 READY";
                hotkeyState.ForeColor = unavailable.Length > 0 ? BridgeTheme.Amber : BridgeTheme.Cyan;
                if (unavailable.Length > 0) Report("Hotkeys unavailable: " + unavailable + ". Another app is using these shortcuts.", true);
                else if (startupError != null) Report(startupError, true);
                else Report("Ready. Enter your OBS host and password, then load scenes. Tests use the fields currently shown.", false);
                if (!forceSettings && startupError == null && File.Exists(ConfigStore.PathName) && saved.Scenes.Any(scn => !String.IsNullOrWhiteSpace(scn))) Hide();
            };
        }

        private BridgeConfig ReadFields()
        {
            return ConfigStore.Make(host.Text.Trim(), (int)port.Value, password.Text, scenes.Select(box => box.Text).ToArray());
        }
        private void SaveSettings()
        {
            if (previewOnly) return;
            try
            {
                BridgeConfig next = ReadFields(); ConfigStore.Save(next); saved = next; savedPassword = password.Text;
                Report("Settings saved. The bridge is running in the system tray.", false); Hide();
                tray.ShowBalloonTip(3000, "Desktop + OBS Bridge", "Saved. G1–G6 are ready. Double-click this icon to configure.", ToolTipIcon.Info);
            }
            catch (Exception ex) { Report("Could not save settings: " + ex.Message, true); }
        }
        private async Task EnsureConnected(BridgeConfig config, string secret)
        {
            if (!obs.IsConnected || connectedHost != config.Host || connectedPort != config.Port || connectedPassword != secret)
            {
                await obs.ConnectAsync(config.Host, config.Port, secret);
                connectedHost = config.Host; connectedPort = config.Port; connectedPassword = secret;
            }
        }
        private async Task LoadScenes()
        {
            if (previewOnly) return;
            if (!await operation.WaitAsync(0)) { Report("A connection or mapping is already running. Try again in a moment.", false); return; }
            connect.Enabled = false;
            try
            {
                BridgeConfig config = ReadFields(); Report("Connecting to OBS on " + config.Host + "...", false);
                await EnsureConnected(config, password.Text); string[] names = await obs.GetSceneNamesAsync();
                foreach (ComboBox box in scenes) { string previous = box.Text; box.Items.Clear(); box.Items.Add(""); box.Items.AddRange(names); box.Text = previous; }
                Report("Connected. Loaded " + names.Length + " scenes. Choose an exact scene for each key, then save.", false);
            }
            catch (Exception ex) { Report("OBS connection failed: " + ex.Message, true); }
            finally { if (!IsDisposed) connect.Enabled = true; operation.Release(); }
        }
        private async Task ApplyMapping(int index, bool useFields, bool waitForBusy = false)
        {
            if (previewOnly) return;
            if (waitForBusy) await operation.WaitAsync();
            else if (!await operation.WaitAsync(0)) { Report("A connection or mapping is already running. Try this test again in a moment.", false); return; }
            bool desktopChanged = false;
            try
            {
                BridgeConfig config = useFields ? ReadFields() : saved;
                string secret = useFields ? password.Text : savedPassword;
                string target = config.Scenes[index];
                if (String.IsNullOrWhiteSpace(target)) { Report("G" + (index + 1) + " is unassigned. Choose an OBS scene in Configure.", true); return; }
                Report("Checking OBS before selecting Desktop " + (index + 1) + "...", false);
                await EnsureConnected(config, secret);
                string[] available = await obs.GetSceneNamesAsync();
                if (!available.Contains(target, StringComparer.Ordinal)) throw new InvalidOperationException("OBS does not contain the assigned scene '" + target + "'. Load scenes and update this key.");
                await DesktopHelper.Switch(index); desktopChanged = true;
                await obs.SetSceneAsync(target);
                for (int i = 0; i < keyLabels.Length; i++) keyLabels[i].ForeColor = i == index ? BridgeTheme.Pink : BridgeTheme.Cyan;
                Report("G" + (index + 1) + ": Desktop " + (index + 1) + " + OBS scene '" + target + "'." + (useFields ? " Test used the fields shown; save to use them with hotkeys." : ""), false);
            }
            catch (Exception ex) { Report((desktopChanged ? "Desktop switched, but OBS scene selection failed: " : "Mapping stopped: ") + ex.Message, true); }
            finally { operation.Release(); }
        }
        private void Report(string message, bool error)
        {
            if (IsDisposed || exiting) return;
            foreach (string secret in new string[] { password.Text, savedPassword, connectedPassword })
                if (!String.IsNullOrEmpty(secret)) message = message.Replace(secret, "[password removed]");
            message = message.Replace("\r", " ").Replace("\n", " ");
            status.Text = (error ? "[ERROR] " : "> ") + message;
            status.ForeColor = error ? BridgeTheme.Pink : BridgeTheme.Text;
            linkState.Text = obs.IsConnected ? "OBS / CONNECTED" : "OBS / DISCONNECTED";
            linkState.ForeColor = obs.IsConnected ? BridgeTheme.Cyan : (error ? BridgeTheme.Pink : BridgeTheme.Muted);
            if (previewOnly) return;
            try
            {
                string statusPath = Path.Combine(ConfigStore.Root, "status.json");
                string temporary = statusPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new {
                    timestampUtc = DateTime.UtcNow.ToString("o"), connected = obs.IsConnected,
                    error = error, lastAction = message }), new UTF8Encoding(false));
                if (File.Exists(statusPath)) File.Replace(temporary, statusPath, null); else File.Move(temporary, statusPath);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (error)
            {
                try { string log = Path.Combine(ConfigStore.Root, "bridge.log"); if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024) File.WriteAllText(log, ""); File.AppendAllText(log, DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                tray.ShowBalloonTip(5000, "Desktop + OBS Bridge", message.Length > 250 ? message.Substring(0, 250) : message, ToolTipIcon.Warning);
            }
        }
        private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (previewOnly) return;
            for (int i = 0; i < 6; i++) registered[i] = RegisterHotKey(Handle, HotkeyBase + i, 0x4000 | 0x0001 | 0x0002 | 0x0004, (uint)Keys.F1 + (uint)i);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            for (int i = 0; i < 6; i++) if (registered[i]) { UnregisterHotKey(Handle, HotkeyBase + i); registered[i] = false; }
            base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && ready) { int index = message.WParam.ToInt32() - HotkeyBase; if (index >= 0 && index < 6) HandleHotkey(index); }
            base.WndProc(ref message);
        }
        private async void HandleHotkey(int index)
        {
            // Keep the newest press while a connection or desktop change is in progress.
            // The final press must win even if two G keys are pressed quickly.
            pendingHotkey = index;
            if (drainingHotkeys) return;
            drainingHotkeys = true;
            try
            {
                while (pendingHotkey >= 0)
                {
                    int next = pendingHotkey;
                    pendingHotkey = -1;
                    await ApplyMapping(next, false, true);
                }
            }
            finally { drainingHotkeys = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { exiting = true; tray.Visible = false; tray.Dispose(); obs.Dispose(); if (Icon != null) Icon.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] arguments)
        {
            if (arguments.Contains("--check-desktops"))
            {
                try { int count = DesktopHelper.Run("/Quiet /Count").GetAwaiter().GetResult(); int current = DesktopHelper.Run("/Quiet /GetCurrentDesktop").GetAwaiter().GetResult(); Console.WriteLine("Desktops: " + count + "; current desktop: " + (current + 1)); return count > 0 && current < count ? 0 : 1; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\DesktopObsBridge-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value, out created))
            {
                if (!created) { MessageBox.Show("The bridge is already running. Double-click its icon in the Windows system tray to configure it.", "Desktop + OBS Bridge"); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new BridgeForm(arguments.Contains("--settings") || !arguments.Contains("--tray")));
            }
            return 0;
        }
    }
}
