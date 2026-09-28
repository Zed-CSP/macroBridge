using System;
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
        private readonly Label[] keyLabels = new Label[6];

        private void BuildUi()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
                ColumnCount = 1, RowCount = 7, BackColor = BridgeTheme.Background };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            Controls.Add(layout);
            layout.Controls.Add(new BridgeHeader { Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);

            TerminalPanel connectionPanel = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10),
                Margin = new Padding(0, 0, 0, 8) };
            TableLayoutPanel connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Margin = new Padding(0) };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connectionPanel.Controls.Add(connection); layout.Controls.Add(connectionPanel, 0, 1);
            Label uplinkTitle = BridgeTheme.Caption("01 / OBS UPLINK", BridgeTheme.Cyan);
            connection.Controls.Add(uplinkTitle, 0, 0); connection.SetColumnSpan(uplinkTitle, 3);
            linkState.Dock = DockStyle.Fill; linkState.Font = BridgeTheme.Mono(9F, true); linkState.TextAlign = ContentAlignment.MiddleRight;
            linkState.Text = "OBS / DISCONNECTED"; linkState.ForeColor = BridgeTheme.Muted; linkState.Margin = new Padding(0);
            connection.Controls.Add(linkState, 3, 0);
            connection.Controls.Add(BridgeTheme.Caption("HOST / IP OR HOSTNAME", BridgeTheme.Muted), 0, 1);
            connection.Controls.Add(BridgeTheme.Caption("PORT", BridgeTheme.Muted), 1, 1);
            connection.Controls.Add(BridgeTheme.Caption("WEBSOCKET PASSWORD", BridgeTheme.Muted), 2, 1);
            host.AccessibleName = "OBS host"; host.TabIndex = 0; connection.Controls.Add(BridgeTheme.Field(host), 0, 2);
            port.Minimum = 1; port.Maximum = 65535; port.AccessibleName = "OBS WebSocket port"; port.TabIndex = 1;
            connection.Controls.Add(BridgeTheme.Field(port), 1, 2);
            password.UseSystemPasswordChar = true; password.AccessibleName = "OBS WebSocket password"; password.TabIndex = 2;
            connection.Controls.Add(BridgeTheme.Field(password), 2, 2);
            connect.Text = "CONNECT / LOAD >"; connect.AccessibleName = "Connect and load OBS scenes"; connect.TabIndex = 3;
            connect.Dock = DockStyle.Fill; connect.Margin = new Padding(0); connect.Click += async (s, e) => await LoadScenes();
            connection.Controls.Add(connect, 3, 2);
            Label credentialNote = BridgeTheme.Caption("Saved passwords stay on this Windows account. Load scenes without changing OBS output.", BridgeTheme.Muted);
            credentialNote.Font = BridgeTheme.Mono(8.5F); connection.Controls.Add(credentialNote, 0, 3); connection.SetColumnSpan(credentialNote, 4);

            TableLayoutPanel section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            section.Controls.Add(BridgeTheme.Caption("02 / CHANNEL ROUTING", BridgeTheme.Cyan), 0, 0);
            routeCount.Dock = DockStyle.Fill; routeCount.Font = BridgeTheme.Mono(9F); routeCount.ForeColor = BridgeTheme.Muted;
            routeCount.TextAlign = ContentAlignment.MiddleRight; routeCount.Margin = new Padding(0);
            section.Controls.Add(routeCount, 1, 0); layout.Controls.Add(section, 0, 2);

            TerminalPanel routingPanel = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 8), Margin = new Padding(0) };
            TableLayoutPanel mappings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 7, Margin = new Padding(0) };
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mappings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118)); mappings.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            string[] headings = { "KEY", "KEYBOARD SHORTCUT", "WINDOWS", "OBS PROGRAM SCENE", "ACTION" };
            for (int column = 0; column < 5; column++) mappings.Controls.Add(BridgeTheme.Caption(headings[column], BridgeTheme.Muted), column, 0);
            mappings.CellPaint += (s, e) => { if (e.Row > 0) using (Pen line = new Pen(BridgeTheme.Line))
                e.Graphics.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1); };
            for (int i = 0; i < 6; i++)
            {
                int index = i; mappings.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));
                keyLabels[i] = new Label { Text = "G" + (i + 1), Font = BridgeTheme.Mono(15F, true), ForeColor = BridgeTheme.Cyan,
                    BackColor = Color.FromArgb(15, 42, 48), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 7, 18, 7) };
                mappings.Controls.Add(keyLabels[i], 0, i + 1);
                Label shortcut = BridgeTheme.Caption("CTRL+ALT+SHIFT+F" + (i + 1), BridgeTheme.Muted);
                shortcut.Font = BridgeTheme.Mono(8.5F); mappings.Controls.Add(shortcut, 1, i + 1);
                mappings.Controls.Add(BridgeTheme.Caption("DESKTOP " + (i + 1).ToString("00"), BridgeTheme.Text), 2, i + 1);
                scenes[i] = new SceneSelector { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 14, 8),
                    AccessibleName = "OBS scene for G" + (i + 1), TabIndex = i + 4 };
                scenes[i].TextChanged += (s, e) => UpdateRouteCount(); mappings.Controls.Add(scenes[i], 3, i + 1);
                Button test = new TerminalButton { Text = "TEST G" + (i + 1) + " >", Dock = DockStyle.Fill,
                    Margin = new Padding(0, 7, 0, 7), AccessibleName = "Test G" + (i + 1) + ": changes desktop and OBS scene", TabIndex = i + 10 };
                test.Click += async (s, e) => await ApplyMapping(index, true); mappings.Controls.Add(test, 4, i + 1);
            }
            routingPanel.Controls.Add(mappings); layout.Controls.Add(routingPanel, 0, 3);
            Label routingNote = BridgeTheme.Caption("! TEST changes your desktop and OBS program scene. A blank scene leaves the key unassigned.", BridgeTheme.Amber);
            routingNote.Font = BridgeTheme.Mono(8.5F); layout.Controls.Add(routingNote, 0, 4);

            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 8, 0, 10) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            hotkeyState.Dock = DockStyle.Fill; hotkeyState.Font = BridgeTheme.Mono(9F); hotkeyState.ForeColor = BridgeTheme.Muted;
            hotkeyState.Text = "HOTKEYS / STARTING"; hotkeyState.TextAlign = ContentAlignment.MiddleLeft; hotkeyState.Margin = new Padding(0);
            actions.Controls.Add(hotkeyState, 0, 0);
            Button exit = new TerminalButton { Text = "EXIT BRIDGE", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), TabIndex = 17 };
            exit.Click += (s, e) => { exiting = true; Close(); }; actions.Controls.Add(exit, 1, 0);
            Button save = new TerminalButton { Text = "SAVE + RUN IN TRAY >", Primary = true, Dock = DockStyle.Fill, Margin = new Padding(0), TabIndex = 16 };
            save.Click += (s, e) => SaveSettings(); actions.Controls.Add(save, 2, 0); layout.Controls.Add(actions, 0, 5);

            TerminalPanel console = new TerminalPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0) };
            TableLayoutPanel consoleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0) };
            consoleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21)); consoleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            consoleLayout.Controls.Add(BridgeTheme.Caption("03 / SYSTEM CONSOLE", BridgeTheme.Muted), 0, 0);
            status.Dock = DockStyle.Fill; status.Font = BridgeTheme.Mono(9F); status.ForeColor = BridgeTheme.Text; status.Margin = new Padding(0);
            status.UseMnemonic = false; status.AccessibleName = "Bridge status"; consoleLayout.Controls.Add(status, 0, 1);
            console.Controls.Add(consoleLayout); layout.Controls.Add(console, 0, 6); UpdateRouteCount();
        }

        private void UpdateRouteCount()
        {
            int count = scenes.Count(box => box != null && !String.IsNullOrWhiteSpace(box.Text));
            routeCount.Text = count.ToString("00") + " / 06 ASSIGNED";
        }
    }
}
