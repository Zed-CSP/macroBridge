using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StreamingBridge
{
    internal sealed partial class BridgeForm
    {
        private readonly Label linkState = new Label();
        private readonly Label hotkeyState = new Label();
        private readonly Label routeCount = new Label();
        private readonly Label profileState = new Label();
        private readonly Panel routingScroller = new Panel();
        private readonly TableLayoutPanel mappings = new TableLayoutPanel();
        private readonly List<MacroRow> macroRows = new List<MacroRow>();

        private sealed class MacroRow
        {
            internal MacroBinding Binding;
            internal Label KeyLabel;
            internal ComboBox Shortcut;
            internal NumericUpDown Desktop;
            internal ComboBox Scene;
        }

        private void BuildUi()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            Controls.Add(layout);
            layout.Controls.Add(new BridgeHeader { Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);

            TerminalPanel profilesPanel = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 10), Margin = new Padding(0, 0, 0, 8) };
            TableLayoutPanel profileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Margin = new Padding(0) };
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label profilesTitle = BridgeTheme.Caption("01 / BRIDGE PROFILES", BridgeTheme.Cyan);
            profileLayout.Controls.Add(profilesTitle, 0, 0); profileLayout.SetColumnSpan(profilesTitle, 2);
            profileState.Dock = DockStyle.Fill; profileState.Font = BridgeTheme.Mono(8.5F); profileState.Margin = new Padding(0);
            profileState.TextAlign = ContentAlignment.MiddleRight; profileState.AutoEllipsis = true;
            profileLayout.Controls.Add(profileState, 2, 0); profileLayout.SetColumnSpan(profileState, 3);
            profileLayout.Controls.Add(BridgeTheme.Caption("SAVED PROFILE", BridgeTheme.Muted), 0, 1);
            profileLayout.Controls.Add(BridgeTheme.Caption("PROFILE NAME / RENAME", BridgeTheme.Muted), 1, 1);
            profiles.DropDownStyle = ComboBoxStyle.DropDownList; profiles.AutoCompleteMode = AutoCompleteMode.None;
            profiles.Dock = DockStyle.Fill; profiles.Margin = new Padding(0, 0, 12, 0); profiles.AccessibleName = "Saved bridge profile";
            profiles.SelectedIndexChanged += (s, e) => { if (!loadingProfiles && profiles.SelectedItem != null) SwitchProfile(((BridgeProfile)profiles.SelectedItem).Id); };
            profileLayout.Controls.Add(profiles, 0, 2);
            profileName.AccessibleName = "Profile name"; profileName.MaxLength = 48; profileName.TextChanged += (s, e) => MarkDraft();
            profileLayout.Controls.Add(BridgeTheme.Field(profileName), 1, 2);
            Button newProfile = new TerminalButton { Text = "NEW +", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), AccessibleName = "Create empty bridge profile" };
            newProfile.Click += (s, e) => CreateProfile(false); profileLayout.Controls.Add(newProfile, 2, 2);
            Button copyProfile = new TerminalButton { Text = "COPY +", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), AccessibleName = "Duplicate current bridge profile" };
            copyProfile.Click += (s, e) => CreateProfile(true); profileLayout.Controls.Add(copyProfile, 3, 2);
            Button deleteProfile = new TerminalButton { Text = "DELETE", Dock = DockStyle.Fill, Margin = new Padding(0), AccessibleName = "Delete current bridge profile" };
            deleteProfile.Click += (s, e) => DeleteProfile(); profileLayout.Controls.Add(deleteProfile, 4, 2);
            profilesPanel.Controls.Add(profileLayout); layout.Controls.Add(profilesPanel, 0, 1);

            TerminalPanel connectionPanel = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 0, 8) };
            TableLayoutPanel connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Margin = new Padding(0) };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connectionPanel.Controls.Add(connection); layout.Controls.Add(connectionPanel, 0, 2);
            Label uplinkTitle = BridgeTheme.Caption("02 / OBS UPLINK", BridgeTheme.Cyan);
            connection.Controls.Add(uplinkTitle, 0, 0); connection.SetColumnSpan(uplinkTitle, 3);
            linkState.Dock = DockStyle.Fill; linkState.Font = BridgeTheme.Mono(9F, true); linkState.TextAlign = ContentAlignment.MiddleRight;
            linkState.Text = "OBS / DISCONNECTED"; linkState.ForeColor = BridgeTheme.Muted; linkState.Margin = new Padding(0);
            connection.Controls.Add(linkState, 3, 0);
            connection.Controls.Add(BridgeTheme.Caption("HOST / IP OR HOSTNAME", BridgeTheme.Muted), 0, 1);
            connection.Controls.Add(BridgeTheme.Caption("PORT", BridgeTheme.Muted), 1, 1);
            connection.Controls.Add(BridgeTheme.Caption("WEBSOCKET PASSWORD", BridgeTheme.Muted), 2, 1);
            host.AccessibleName = "OBS host"; host.TextChanged += (s, e) => MarkDraft();
            connection.Controls.Add(BridgeTheme.Field(host), 0, 2);
            port.Minimum = 1; port.Maximum = 65535; port.AccessibleName = "OBS WebSocket port"; port.ValueChanged += (s, e) => MarkDraft();
            connection.Controls.Add(BridgeTheme.Field(port), 1, 2);
            password.UseSystemPasswordChar = true; password.AccessibleName = "OBS WebSocket password";
            password.TextChanged += (s, e) => { if (!loadingFields) passwordEdited = true; MarkDraft(); };
            connection.Controls.Add(BridgeTheme.Field(password), 2, 2);
            connect.Text = "CONNECT / LOAD >"; connect.AccessibleName = "Connect and load OBS scenes";
            connect.Dock = DockStyle.Fill; connect.Margin = new Padding(0); connect.Click += async (s, e) => await LoadScenes();
            connection.Controls.Add(connect, 3, 2);
            Label credentialNote = BridgeTheme.Caption("Each profile keeps its own connection and Windows-protected password.", BridgeTheme.Muted);
            credentialNote.Font = BridgeTheme.Mono(8.5F); connection.Controls.Add(credentialNote, 0, 3); connection.SetColumnSpan(credentialNote, 4);

            TableLayoutPanel section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            section.Controls.Add(BridgeTheme.Caption("03 / MACRO ROUTING", BridgeTheme.Cyan), 0, 0);
            routeCount.Dock = DockStyle.Fill; routeCount.Font = BridgeTheme.Mono(9F); routeCount.ForeColor = BridgeTheme.Muted;
            routeCount.TextAlign = ContentAlignment.MiddleRight; routeCount.Margin = new Padding(0, 0, 16, 0);
            section.Controls.Add(routeCount, 1, 0);
            addMacro.Text = "+ ADD MACRO"; addMacro.Dock = DockStyle.Fill; addMacro.Margin = new Padding(0, 4, 0, 4);
            addMacro.AccessibleName = "Add a macro to this profile"; addMacro.Click += (s, e) => AddMacro();
            section.Controls.Add(addMacro, 2, 0); layout.Controls.Add(section, 0, 3);

            TerminalPanel routingPanel = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8), Margin = new Padding(0) };
            routingScroller.Dock = DockStyle.Fill; routingScroller.AutoScroll = true; routingScroller.Margin = new Padding(0);
            mappings.Dock = DockStyle.Top; mappings.AutoSize = true; mappings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            mappings.ColumnCount = 6; mappings.Margin = new Padding(0);
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            mappings.CellPaint += (s, e) => { if (e.Row > 0) using (Pen line = new Pen(BridgeTheme.Line))
                e.Graphics.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1); };
            routingScroller.Controls.Add(mappings); routingPanel.Controls.Add(routingScroller); layout.Controls.Add(routingPanel, 0, 4);
            Label routingNote = BridgeTheme.Caption("! TEST changes desktop + OBS scene. Profile switches save edits; saved macros drive hotkeys.", BridgeTheme.Amber);
            routingNote.Font = BridgeTheme.Mono(8.5F); layout.Controls.Add(routingNote, 0, 5);

            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 4, 0, 8) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            hotkeyState.Dock = DockStyle.Fill; hotkeyState.Font = BridgeTheme.Mono(9F); hotkeyState.ForeColor = BridgeTheme.Muted;
            hotkeyState.Text = "HOTKEYS / STARTING"; hotkeyState.TextAlign = ContentAlignment.MiddleLeft; hotkeyState.Margin = new Padding(0);
            actions.Controls.Add(hotkeyState, 0, 0);
            Button exit = new TerminalButton { Text = "EXIT BRIDGE", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            exit.Click += (s, e) => { exiting = true; Close(); }; actions.Controls.Add(exit, 1, 0);
            Button saveProfile = new TerminalButton { Text = "SAVE PROFILE", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            saveProfile.Click += (s, e) => SaveProfile(); actions.Controls.Add(saveProfile, 2, 0);
            Button save = new TerminalButton { Text = "SAVE + RUN IN TRAY >", Primary = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            save.Click += (s, e) => SaveSettings(); actions.Controls.Add(save, 3, 0); layout.Controls.Add(actions, 0, 6);

            TerminalPanel console = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0) };
            TableLayoutPanel consoleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0) };
            consoleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); consoleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            consoleLayout.Controls.Add(BridgeTheme.Caption("04 / SYSTEM CONSOLE", BridgeTheme.Muted), 0, 0);
            status.Dock = DockStyle.Fill; status.Font = BridgeTheme.Mono(9F); status.ForeColor = BridgeTheme.Text; status.Margin = new Padding(0);
            status.UseMnemonic = false; status.AccessibleName = "Bridge status"; consoleLayout.Controls.Add(status, 0, 1);
            console.Controls.Add(consoleLayout); layout.Controls.Add(console, 0, 7);
        }

        private List<MacroBinding> ReadMacroRows()
        {
            return macroRows.Select(row => new MacroBinding { Id = row.Binding.Id, Number = row.Binding.Number,
                FunctionKey = row.Shortcut.SelectedIndex + 1, Desktop = (int)row.Desktop.Value, Scene = row.Scene.Text }).ToList();
        }

        private void RebuildMacroRows(IEnumerable<MacroBinding> macros)
        {
            List<MacroBinding> values = macros.Select(macro => macro.Copy()).ToList();
            bool wasLoading = loadingFields; loadingFields = true;
            mappings.SuspendLayout();
            try
            {
                foreach (Control control in mappings.Controls.Cast<Control>().ToArray()) control.Dispose();
                mappings.Controls.Clear(); mappings.RowStyles.Clear(); macroRows.Clear();
                mappings.RowCount = Math.Max(2, values.Count + 1);
                mappings.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                string[] headings = { "KEY", "KEYBOARD SHORTCUT", "DESKTOP #", "OBS PROGRAM SCENE", "TEST", "" };
                for (int column = 0; column < headings.Length; column++) mappings.Controls.Add(BridgeTheme.Caption(headings[column], BridgeTheme.Muted), column, 0);
                string[] choices = loadedScenes.Length > 0 ? loadedScenes : values.Select(macro => macro.Scene).Where(scene => !String.IsNullOrEmpty(scene)).Distinct(StringComparer.Ordinal).ToArray();
                for (int i = 0; i < values.Count; i++)
                {
                    MacroBinding macro = values[i]; int rowIndex = i + 1;
                    mappings.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                    MacroRow row = new MacroRow { Binding = macro };
                    row.KeyLabel = new Label { Text = "G" + macro.Number, Font = BridgeTheme.Mono(14F, true), ForeColor = BridgeTheme.Cyan,
                        BackColor = Color.FromArgb(15, 42, 48), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 5, 10, 5) };
                    mappings.Controls.Add(row.KeyLabel, 0, rowIndex);
                    TableLayoutPanel shortcut = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 12, 0) };
                    shortcut.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shortcut.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
                    Label modifiers = BridgeTheme.Caption("CTRL+ALT+SHIFT+", BridgeTheme.Muted); modifiers.Font = BridgeTheme.Mono(8F);
                    shortcut.Controls.Add(modifiers, 0, 0);
                    row.Shortcut = new SceneSelector { DropDownStyle = ComboBoxStyle.DropDownList, AutoCompleteMode = AutoCompleteMode.None,
                        Dock = DockStyle.Fill, Margin = new Padding(0, 7, 0, 6), AccessibleName = "Function key for G" + macro.Number };
                    row.Shortcut.Items.AddRange(Enumerable.Range(1, ConfigStore.MaximumMacros).Select(key => "F" + key).ToArray());
                    row.Shortcut.SelectedIndex = macro.FunctionKey - 1; row.Shortcut.SelectedIndexChanged += (s, e) => MarkDraft();
                    shortcut.Controls.Add(row.Shortcut, 1, 0); mappings.Controls.Add(shortcut, 1, rowIndex);
                    row.Desktop = new NumericUpDown { Minimum = 1, Maximum = ConfigStore.MaximumDesktop, Value = macro.Desktop, AccessibleName = "Windows desktop number for G" + macro.Number };
                    row.Desktop.ValueChanged += (s, e) => MarkDraft();
                    Control desktopField = BridgeTheme.Field(row.Desktop); desktopField.Margin = new Padding(0, 7, 12, 6); desktopField.Padding = new Padding(7, 5, 3, 3);
                    mappings.Controls.Add(desktopField, 2, rowIndex);
                    row.Scene = new SceneSelector { Dock = DockStyle.Fill, Margin = new Padding(0, 7, 12, 6), AccessibleName = "OBS scene for G" + macro.Number };
                    row.Scene.Items.Add(""); row.Scene.Items.AddRange(choices); row.Scene.Text = macro.Scene ?? "";
                    row.Scene.SelectionStart = row.Scene.Text.Length; row.Scene.SelectionLength = 0;
                    row.Scene.TextChanged += (s, e) => { UpdateRouteCount(); MarkDraft(); };
                    mappings.Controls.Add(row.Scene, 3, rowIndex);
                    Button test = new TerminalButton { Text = "TEST >", Dock = DockStyle.Fill, Margin = new Padding(0, 5, 8, 5),
                        AccessibleName = "Test G" + macro.Number + ": changes desktop and OBS scene" };
                    test.Click += async (s, e) => await ApplyMapping(macro.Id, true); mappings.Controls.Add(test, 4, rowIndex);
                    Button remove = new TerminalButton { Text = "-", Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 5), AccessibleName = "Remove G" + macro.Number };
                    remove.Click += (s, e) => RemoveMacro(macro.Id); mappings.Controls.Add(remove, 5, rowIndex);
                    macroRows.Add(row);
                }
                if (values.Count == 0)
                {
                    mappings.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
                    Label empty = BridgeTheme.Caption("NO MACROS YET / Use + ADD MACRO to build this profile.", BridgeTheme.Muted);
                    mappings.Controls.Add(empty, 0, 1); mappings.SetColumnSpan(empty, 6);
                }
                addMacro.Enabled = macroRows.Count < ConfigStore.MaximumMacros; UpdateRouteCount();
            }
            finally { loadingFields = wasLoading; mappings.ResumeLayout(true); }
        }

        private void UpdateRouteCount()
        {
            int count = macroRows.Count(row => !String.IsNullOrWhiteSpace(row.Scene.Text));
            routeCount.Text = count.ToString("00") + " / " + macroRows.Count.ToString("00") + " ASSIGNED";
        }
    }
}
