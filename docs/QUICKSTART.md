# Streaming Bridge

1. Extract the ZIP into a writable folder on your Windows computer. Keep the `desktop` folder beside `StreamingBridge.exe`.
2. Create the numbered Windows desktops you want to use with Win+Tab.
3. In OBS, enable Tools > WebSocket Server Settings > Enable WebSocket server.
4. Run `StreamingBridge.exe`. Enter the OBS computer's hostname or IP address, WebSocket port, and password. For OBS on the same computer, use `localhost`.
5. Connect and load scenes, then assign a scene to each G key. Blank scenes are unassigned.
6. Configure your keyboard or macro pad to send Ctrl+Alt+Shift+F1 through F6.
7. Save and run in the tray. Double-click the tray icon to configure; use its Exit command to stop.

Test buttons and shortcuts change the real Windows desktop and OBS program scene. Desktop changes happen before scene changes; they are not an atomic operation.

Settings and encrypted credentials are saved locally as `config.json`. Do not share that file, `status.json`, or `bridge.log`. The password is protected for the current Windows user using DPAPI. The OBS connection uses WebSocket v5 over a trusted LAN.

The desktop helper uses Windows 11 24H2 shell interfaces; Windows updates can change compatibility. The app and helper have separate MIT notices included in this archive.
