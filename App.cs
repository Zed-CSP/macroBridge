using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StreamingBridge
{
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
        private readonly ComboBox profiles = new SceneSelector();
        private readonly TextBox profileName = new TextBox();
        private readonly Button connect = new TerminalButton { Primary = true };
        private readonly Button addMacro = new TerminalButton();
        private readonly Label status = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
        private ObsClient obs = new ObsClient();
        private BridgeSettings settings;
        private BridgeProfile saved;
        private string savedPassword = "", passwordLoadError;
        private string connectedHost = "", connectedPassword = "";
        private int connectedPort;
        private bool exiting, ready, drainingHotkeys, loadingFields, loadingProfiles, passwordEdited;
        private bool configurationWritable = true;
        private PendingPress pendingHotkey;
        private int profileGeneration, nextHotkeyId = 7300;
        private readonly Dictionary<int, string> registered = new Dictionary<int, string>();
        private string startupError, startupMessage;
        private string[] loadedScenes = new string[0];
        private readonly bool previewOnly;
        private const int WmHotkey = 0x0312;
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);

        private sealed class PendingPress
        {
            internal string MacroId;
            internal int Generation;
        }

        internal BridgeForm(bool forceSettings) : this(forceSettings, false) { }

        // Preview mode exercises profile editing using sample data, without disk or runtime side effects.
        internal BridgeForm(bool forceSettings, bool previewOnly)
        {
            this.previewOnly = previewOnly;
            bool migrated = false;
            try { settings = previewOnly ? SampleSettings() : ConfigStore.Load(out migrated); }
            catch (Exception)
            {
                settings = ConfigStore.Defaults(); configurationWritable = false;
                startupError = "Saved settings could not be loaded. Your file has been kept untouched; restore or repair it before saving.";
            }
            saved = settings.Active();
            if (migrated) startupMessage = "Your previous six mappings are saved as 'Original setup'. A local backup of the original config was kept.";
            Text = "Streaming Bridge // Desktop + OBS";
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1140, 980); MinimumSize = new Size(1040, 840);
            Font = BridgeTheme.Mono(11F); BackColor = BridgeTheme.Background; ForeColor = BridgeTheme.Text;
            StartPosition = FormStartPosition.CenterScreen; Icon = BridgeTheme.CreateIcon();
            BuildUi(); RefreshProfileChoices(); LoadProfileFields();
            if (previewOnly)
            {
                UpdateHotkeyState();
                Report("Profile ready. Add or remove macros, or select another saved bridge profile.", false);
                return;
            }
            RebuildTrayMenu();
            tray.Icon = Icon; tray.Text = "Streaming Bridge"; tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowSettings();
            FormClosing += (s, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            Shown += (s, e) => {
                FitToScreen(); ready = true; UpdateHotkeyState();
                if (startupError != null) Report(startupError, true);
                else if (passwordLoadError != null) Report(passwordLoadError, true);
                else if (startupMessage != null) ReportHotkeyResult(startupMessage);
                else ReportHotkeyResult("Ready. Edit a profile, load OBS scenes, and save to activate its macros.");
                if (!forceSettings && startupError == null && passwordLoadError == null && File.Exists(ConfigStore.PathName)
                    && saved.Macros.Any(macro => !String.IsNullOrWhiteSpace(macro.Scene))) Hide();
            };
        }

        private static BridgeSettings SampleSettings()
        {
            BridgeSettings sample = ConfigStore.Defaults();
            BridgeProfile main = sample.Active(); main.Name = "Streaming"; main.Host = "obs-studio.local";
            string[] examples = { "01 / Main feed", "02 / Code & create", "03 / Gameplay", "04 / Camera", "05 / Intermission", "06 / Signing off" };
            for (int i = 0; i < main.Macros.Count; i++) main.Macros[i].Scene = examples[i];
            BridgeProfile studio = ConfigStore.Duplicate(sample, main); studio.Name = "Studio / minimal";
            studio.Macros = studio.Macros.Take(3).ToList(); studio.Macros[1].Desktop = 4; studio.Macros[1].Scene = examples[3];
            sample.Profiles.Add(studio); return sample;
        }

        private void RefreshProfileChoices()
        {
            loadingProfiles = true;
            try
            {
                profiles.Items.Clear(); profiles.Items.AddRange(settings.Profiles.ToArray());
                profiles.SelectedItem = settings.Active();
            }
            finally { loadingProfiles = false; }
        }

        private void LoadProfileFields()
        {
            loadingFields = true;
            try
            {
                host.Text = saved.Host; port.Value = saved.Port; profileName.Text = saved.Name;
                passwordLoadError = null;
                try { savedPassword = previewOnly ? "" : ConfigStore.Password(saved); }
                catch (Exception)
                {
                    savedPassword = "";
                    passwordLoadError = "This profile's saved password cannot be decrypted. Retype it before connecting; the saved credential has been preserved.";
                }
                password.Text = savedPassword; passwordEdited = false; loadedScenes = new string[0];
                RebuildMacroRows(saved.Macros);
                profileState.Text = "ACTIVE / " + saved.Name; profileState.ForeColor = BridgeTheme.Cyan;
            }
            finally { loadingFields = false; }
        }

        private BridgeProfile ReadFields()
        {
            BridgeProfile profile = saved.Copy();
            profile.Name = profileName.Text.Trim(); profile.Host = host.Text.Trim(); profile.Port = (int)port.Value;
            profile.Macros = ReadMacroRows();
            if (passwordEdited) profile.ProtectedPassword = ConfigStore.ProtectPassword(password.Text);
            ConfigStore.Validate(profile);
            return profile;
        }

        private BridgeSettings WithDraft()
        {
            BridgeSettings next = settings.Copy();
            BridgeProfile draft = ReadFields();
            next.Profiles[next.Profiles.FindIndex(profile => profile.Id == draft.Id)] = draft;
            ConfigStore.Validate(next); return next;
        }

        private bool CanEditProfiles()
        {
            if (operation.CurrentCount == 0)
            {
                Report("Wait for the current connection or macro to finish before changing profiles.", false); return false;
            }
            if (!configurationWritable)
            {
                Report(startupError, true); return false;
            }
            return true;
        }

        private void PersistAndActivate(BridgeSettings next, bool reloadFields)
        {
            ConfigStore.Validate(next);
            string previousId = saved.Id, previousHost = saved.Host, previousSecret = savedPassword;
            int previousPort = saved.Port;
            if (!previewOnly) ConfigStore.Save(next);
            settings = next; saved = next.Active();
            if (reloadFields) LoadProfileFields();
            else
            {
                if (passwordEdited) { savedPassword = password.Text; passwordLoadError = null; }
                passwordEdited = false;
                profileState.Text = "ACTIVE / " + saved.Name; profileState.ForeColor = BridgeTheme.Cyan;
            }
            if (previousId != saved.Id || previousHost != saved.Host || previousPort != saved.Port || previousSecret != savedPassword)
            {
                obs.Dispose(); obs = new ObsClient(); connectedHost = ""; connectedPort = 0; connectedPassword = "";
            }
            RefreshProfileChoices(); RefreshHotkeys(); RebuildTrayMenu();
        }

        private bool SaveProfile()
        {
            if (!CanEditProfiles()) return false;
            try
            {
                PersistAndActivate(WithDraft(), false);
                ReportHotkeyResult("Profile '" + saved.Name + "' saved. " + saved.Macros.Count + " macros are active.");
                return true;
            }
            catch (Exception ex) { Report("Could not save this profile: " + ex.Message, true); return false; }
        }

        private void SaveSettings()
        {
            if (!SaveProfile() || previewOnly) return;
            Hide();
            tray.ShowBalloonTip(3000, "Streaming Bridge", "Profile saved. Double-click the tray icon to configure.", ToolTipIcon.Info);
        }

        private bool SwitchProfile(string id)
        {
            if (id == saved.Id) return true;
            if (!CanEditProfiles()) { RefreshProfileChoices(); return false; }
            try
            {
                BridgeSettings next = WithDraft();
                if (!next.Profiles.Any(profile => profile.Id == id)) throw new ArgumentException("This profile no longer exists.");
                next.ActiveProfileId = id; PersistAndActivate(next, true);
                ReportHotkeyResult("Activated '" + saved.Name + "'. Previous edits saved; the current desktop and OBS scene stay as they are.");
                return true;
            }
            catch (Exception ex)
            {
                RefreshProfileChoices(); Report("Could not switch profiles: " + ex.Message, true); return false;
            }
        }

        private void CreateProfile(bool duplicate)
        {
            if (!CanEditProfiles()) return;
            try
            {
                BridgeSettings next = WithDraft();
                BridgeProfile profile = duplicate ? ConfigStore.Duplicate(next, next.Active())
                    : new BridgeProfile { Name = ConfigStore.UniqueName(next, "New profile") };
                next.Profiles.Add(profile); next.ActiveProfileId = profile.Id;
                PersistAndActivate(next, true);
                ReportHotkeyResult((duplicate ? "Copied the current setup to '" : "Created '") + saved.Name + "'. Rename it and configure its macros.");
                profileName.Focus(); profileName.SelectAll();
            }
            catch (Exception ex) { Report("Could not create this profile: " + ex.Message, true); }
        }

        private void DeleteProfile()
        {
            if (!CanEditProfiles()) return;
            if (settings.Profiles.Count < 2) { Report("Keep at least one profile. Create another before deleting this one.", false); return; }
            if (!previewOnly && MessageBox.Show(this, "Delete profile '" + saved.Name + "' and its saved macros?",
                "Delete bridge profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                BridgeSettings next = settings.Copy(); int index = next.Profiles.FindIndex(profile => profile.Id == saved.Id);
                next.Profiles.RemoveAt(index); next.ActiveProfileId = next.Profiles[Math.Min(index, next.Profiles.Count - 1)].Id;
                PersistAndActivate(next, true); ReportHotkeyResult("Profile deleted. Activated '" + saved.Name + "'.");
            }
            catch (Exception ex) { Report("Could not delete this profile: " + ex.Message, true); }
        }

        private void AddMacro()
        {
            BridgeProfile draft = saved.Copy(); draft.Macros = ReadMacroRows();
            try
            {
                draft.Macros.Add(ConfigStore.NewMacro(draft)); RebuildMacroRows(draft.Macros); MarkDraft();
                Report("Macro added. Choose its shortcut, desktop, and scene, then save the profile.", false);
            }
            catch (Exception ex) { Report(ex.Message, true); }
        }

        private void RemoveMacro(string id)
        {
            List<MacroBinding> draft = ReadMacroRows();
            draft.RemoveAll(macro => macro.Id == id); RebuildMacroRows(draft); MarkDraft();
            Report("Macro removed from this draft. Save the profile to update its hotkeys.", false);
        }

        private void MarkDraft()
        {
            if (loadingFields) return;
            profileState.Text = "EDITING / SAVE TO APPLY"; profileState.ForeColor = BridgeTheme.Amber;
        }

        private void RebuildTrayMenu()
        {
            if (previewOnly) return;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = BridgeTheme.Surface; menu.ForeColor = BridgeTheme.Text; menu.Font = BridgeTheme.Mono(10F);
            menu.Items.Add("Configure", null, (s, e) => ShowSettings());
            ToolStripMenuItem profileMenu = new ToolStripMenuItem("Profiles");
            foreach (BridgeProfile profile in settings.Profiles)
            {
                string id = profile.Id;
                ToolStripMenuItem choice = new ToolStripMenuItem(profile.Name) { Checked = id == saved.Id };
                choice.Click += (s, e) => SwitchProfile(id); profileMenu.DropDownItems.Add(choice);
            }
            menu.Items.Add(profileMenu);
            ToolStripMenuItem testMenu = new ToolStripMenuItem("Test saved macro");
            foreach (MacroBinding macro in saved.Macros)
            {
                string id = macro.Id;
                testMenu.DropDownItems.Add("G" + macro.Number + " / Desktop " + macro.Desktop, null, async (s, e) => await ApplyMapping(id, false));
            }
            testMenu.Enabled = saved.Macros.Count > 0; menu.Items.Add(testMenu); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { exiting = true; Close(); });
            ContextMenuStrip previous = tray.ContextMenuStrip; tray.ContextMenuStrip = menu;
            if (previous != null) previous.Dispose();
        }

        private async Task EnsureConnected(BridgeProfile config, string secret)
        {
            if (!obs.IsConnected || connectedHost != config.Host || connectedPort != config.Port || connectedPassword != secret)
            {
                await obs.ConnectAsync(config.Host, config.Port, secret);
                connectedHost = config.Host; connectedPort = config.Port; connectedPassword = secret;
            }
        }

        private void CheckCredential()
        {
            if (passwordLoadError != null && !passwordEdited) throw new InvalidOperationException(passwordLoadError);
        }

        private async Task LoadScenes()
        {
            if (previewOnly) return;
            if (!await operation.WaitAsync(0)) { Report("A connection or macro is already running. Try again in a moment.", false); return; }
            connect.Enabled = false;
            try
            {
                CheckCredential(); BridgeProfile config = ReadFields(); Report("Connecting to OBS on " + config.Host + "...", false);
                await EnsureConnected(config, password.Text); loadedScenes = await obs.GetSceneNamesAsync();
                foreach (MacroRow row in macroRows)
                {
                    string previous = row.Scene.Text; row.Scene.Items.Clear(); row.Scene.Items.Add("");
                    row.Scene.Items.AddRange(loadedScenes); row.Scene.Text = previous;
                }
                Report("Connected. Loaded " + loadedScenes.Length + " scenes. Assign scenes and save this profile.", false);
            }
            catch (Exception ex) { Report("OBS connection failed: " + ex.Message, true); }
            finally { if (!IsDisposed) connect.Enabled = true; operation.Release(); }
        }

        private async Task ApplyMapping(string id, bool useFields, bool waitForBusy = false, int expectedGeneration = -1)
        {
            if (previewOnly) return;
            if (waitForBusy) await operation.WaitAsync();
            else if (!await operation.WaitAsync(0)) { Report("A connection or macro is already running. Try this test again in a moment.", false); return; }
            bool desktopChanged = false;
            try
            {
                if (IsDisposed || exiting || (expectedGeneration >= 0 && expectedGeneration != profileGeneration)) return;
                BridgeProfile config = useFields ? ReadFields() : saved;
                if (useFields) CheckCredential();
                else if (passwordLoadError != null) throw new InvalidOperationException(passwordLoadError);
                MacroBinding macro = config.Macros.FirstOrDefault(item => item.Id == id);
                if (macro == null) return;
                string secret = useFields ? password.Text : savedPassword, target = macro.Scene;
                if (String.IsNullOrWhiteSpace(target)) { Report("G" + macro.Number + " is unassigned. Choose an OBS scene in Configure.", true); return; }
                Report("Checking OBS before selecting Desktop " + macro.Desktop + "...", false);
                await EnsureConnected(config, secret);
                string[] available = await obs.GetSceneNamesAsync();
                if (!available.Contains(target, StringComparer.Ordinal)) throw new InvalidOperationException("OBS does not contain the assigned scene '" + target + "'. Load scenes and update this macro.");
                await DesktopHelper.Switch(macro.Desktop - 1); desktopChanged = true;
                await obs.SetSceneAsync(target);
                foreach (MacroRow row in macroRows) row.KeyLabel.ForeColor = row.Binding.Id == id ? BridgeTheme.Pink : BridgeTheme.Cyan;
                Report("G" + macro.Number + ": Desktop " + macro.Desktop + " + OBS scene '" + target + "'." +
                    (useFields ? " Test used the fields shown; save to use them with hotkeys." : ""), false);
            }
            catch (Exception ex) { Report((desktopChanged ? "Desktop switched, but OBS scene selection failed: " : "Macro stopped: ") + ex.Message, true); }
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
            bool matchesFields = obs.IsConnected && connectedHost == host.Text.Trim() && connectedPort == (int)port.Value && connectedPassword == password.Text;
            linkState.Text = matchesFields ? "OBS / CONNECTED" : "OBS / DISCONNECTED";
            linkState.ForeColor = matchesFields ? BridgeTheme.Cyan : (error ? BridgeTheme.Pink : BridgeTheme.Muted);
            if (previewOnly) return;
            try
            {
                string statusPath = Path.Combine(ConfigStore.Root, "status.json");
                string temporary = statusPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new {
                    timestampUtc = DateTime.UtcNow.ToString("o"), connected = matchesFields,
                    activeProfile = saved.Name, macroCount = saved.Macros.Count, registeredHotkeys = registered.Count,
                    error = error, lastAction = message }), new UTF8Encoding(false));
                if (File.Exists(statusPath)) File.Replace(temporary, statusPath, null); else File.Move(temporary, statusPath);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (error)
            {
                try { string log = Path.Combine(ConfigStore.Root, "bridge.log"); if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024) File.WriteAllText(log, ""); File.AppendAllText(log, DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                tray.ShowBalloonTip(5000, "Streaming Bridge", message.Length > 250 ? message.Substring(0, 250) : message, ToolTipIcon.Warning);
            }
        }

        private void FitToScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; FitToScreen(); Activate(); }

        private void UnregisterBindings()
        {
            if (!previewOnly && IsHandleCreated)
                foreach (int id in registered.Keys) UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        private void RegisterBindings()
        {
            if (previewOnly || !IsHandleCreated || saved == null) return;
            foreach (MacroBinding macro in saved.Macros)
            {
                // Fresh IDs ensure queued Windows messages from an old profile cannot trigger a new one.
                int id = nextHotkeyId++;
                if (nextHotkeyId > 0xBFFF) nextHotkeyId = 7300;
                if (RegisterHotKey(Handle, id, 0x4000 | 0x0001 | 0x0002 | 0x0004, (uint)Keys.F1 + (uint)macro.FunctionKey - 1))
                    registered.Add(id, macro.Id);
            }
        }

        private void RefreshHotkeys()
        {
            profileGeneration++; pendingHotkey = null;
            UnregisterBindings(); RegisterBindings(); UpdateHotkeyState();
        }

        private void UpdateHotkeyState()
        {
            hotkeyState.Text = previewOnly ? "HOTKEYS / PREVIEW" : "HOTKEYS / " + registered.Count + " OF " + saved.Macros.Count + " READY";
            hotkeyState.ForeColor = previewOnly ? BridgeTheme.Muted : (registered.Count == saved.Macros.Count ? BridgeTheme.Cyan : BridgeTheme.Amber);
        }

        private void ReportHotkeyResult(string message)
        {
            string unavailable = previewOnly ? "" : String.Join(", ", saved.Macros.Where(macro => !registered.ContainsValue(macro.Id)).Select(macro => "G" + macro.Number));
            Report(message + (unavailable.Length > 0 ? " Hotkeys unavailable: " + unavailable + ". Another app is using these shortcuts." : ""), unavailable.Length > 0);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); RegisterBindings();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterBindings(); base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            string id;
            if (message.Msg == WmHotkey && ready && registered.TryGetValue(message.WParam.ToInt32(), out id)) HandleHotkey(id);
            base.WndProc(ref message);
        }

        private async void HandleHotkey(string id)
        {
            pendingHotkey = new PendingPress { MacroId = id, Generation = profileGeneration };
            if (drainingHotkeys) return;
            drainingHotkeys = true;
            try
            {
                while (pendingHotkey != null)
                {
                    PendingPress next = pendingHotkey; pendingHotkey = null;
                    await ApplyMapping(next.MacroId, false, true, next.Generation);
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
