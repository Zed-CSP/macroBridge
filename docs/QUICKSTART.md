# Streaming Bridge

1. Extract the ZIP into a writable folder on your Windows computer. Keep the `desktop` folder beside `StreamingBridge.exe`.
2. Create the numbered Windows desktops you want to use with Win+Tab.
3. In OBS, enable Tools > WebSocket Server Settings > Enable WebSocket server.
4. Run `StreamingBridge.exe`. Enter the OBS computer's hostname or IP address, WebSocket port, and password. For OBS on the same computer, use `localhost`.
5. Connect and load scenes, then assign a scene to each macro. Use **+ Add macro** and the row's **−** button to add or remove rows. Blank scenes are unassigned.
6. Choose each macro's F key (F1–F24) and desktop number (1–64). Configure your keyboard or macro pad to send Ctrl+Alt+Shift plus that F key. Shortcuts must be unique within each profile.
7. Select **Save profile** to activate changes, or **Save + run in tray** to save and close the window. Double-click the tray icon to configure; use its Exit command to stop.

Use the saved profile selector to swap setups. Switching saves your current edits and updates hotkeys without changing the current desktop or OBS scene. **New +** creates an empty profile; **Copy +** duplicates the current setup. Edit the name field to rename it, then save. **Delete** asks before removing a profile, and keeps at least one. Profiles are also selectable from the tray menu. Switching bridge profiles does not change your keyboard's hardware or iCUE profile.

Upgrading an older six-key setup saves it as **Original setup** and keeps an exact local backup at `config.json.before-profiles.bak`. Its connection, password, shortcuts, and scenes are preserved. Copy that profile to try a different setup.

Test buttons and shortcuts change the real Windows desktop and OBS program scene. Desktop changes happen before scene changes; they are not an atomic operation.

All profiles and encrypted credentials are saved locally as `config.json`. Do not share that file, its backups, `status.json`, or `bridge.log`. Passwords are protected for the current Windows user using DPAPI. The OBS connection uses WebSocket v5 over a trusted LAN.

The desktop helper uses Windows 11 24H2 shell interfaces; Windows updates can change compatibility. The app and helper have separate MIT notices included in this archive.
