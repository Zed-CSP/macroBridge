# Configuration guide

Streaming Bridge combines a keyboard shortcut, a numbered Windows virtual desktop, and an exact OBS program scene. Saved profiles keep complete routing setups, including their OBS connection details.

## Macro routing

Each profile supports **zero to 24 macros**. Use **+ Add macro** to create a row, or the row's **−** button to remove it. Removing a macro keeps the other G numbers, shortcuts, and desktop targets intact. Save the profile to apply the change to hotkeys.

Every macro selects:

| Field | Allowed values |
| --- | --- |
| Keyboard shortcut | Ctrl+Alt+Shift plus F1–F24; unique within the profile |
| Windows target | Desktop 1–64; the desktop must already exist |
| OBS program scene | An exact scene name, including any significant spaces |

A blank scene leaves the macro unassigned. Different macros can select the same desktop with different OBS scenes. The initial six bindings are:

| Macro | Shortcut | Windows target |
| --- | --- | --- |
| G1 | Ctrl+Alt+Shift+F1 | Desktop 1 |
| G2 | Ctrl+Alt+Shift+F2 | Desktop 2 |
| G3 | Ctrl+Alt+Shift+F3 | Desktop 3 |
| G4 | Ctrl+Alt+Shift+F4 | Desktop 4 |
| G5 | Ctrl+Alt+Shift+F5 | Desktop 5 |
| G6 | Ctrl+Alt+Shift+F6 | Desktop 6 |

Use **Connect / load** to fetch scene names from OBS. Loading scenes does not change OBS output. **Test** uses the editor's current fields; hotkeys use the active profile's last saved mappings. Both Test and hotkeys change the actual desktop and OBS program scene.

The bridge checks the connection and scene name before switching desktops. It then changes the Windows desktop and selects the OBS program scene. These actions are sequential; if scene selection fails after the desktop changes, the console reports the partial result.

## Profiles and migration

Select a **Saved profile** in the window, or choose one from **Profiles** in the tray menu. Switching saves outgoing edits first. Invalid edits block activation and stay in the editor, so they are not lost. Activation updates shortcuts without changing the current desktop or OBS scene; pending presses from the previous profile are discarded.

| Control | Behavior |
| --- | --- |
| **New +** | Create and activate an empty profile. |
| **Copy +** | Duplicate the current setup, including its encrypted credential. |
| **Profile name / rename** | Edit the name, then save. Names must be unique. |
| **Save profile** | Save edits and activate their hotkeys while keeping settings open. |
| **Save + run in tray** | Save edits, activate their hotkeys, and close settings. |
| **Delete** | Ask before deleting the selected profile; at least one must remain. |

Each profile keeps its own host, port, encrypted password, and macros. An empty profile is valid and registers no hotkeys. Switching bridge profiles does not change your keyboard's hardware or iCUE profile.

On the first launch after upgrading an older six-key configuration, the bridge saves it as **Original setup**. Its connection, encrypted password, shortcuts, desktop numbers, and exact scene names are preserved. A byte-for-byte backup remains beside the configuration as `config.json.before-profiles.bak`. Existing migration backups are kept; a later upgrade uses a unique backup name.

Use **Copy +** before experimenting with the original setup. All profiles and migration backups remain local and are excluded from Git and packages.

## Keyboard and Corsair setup

Configure your keyboard or macro pad to send the shortcuts shown in the editor. For Corsair K100 onboard recording, exit the bridge while recording so the shortcut does not trigger an action. Follow [Corsair's recording instructions](https://help.corsair.com/hc/en-us/articles/360050055212-How-to-Set-up-the-iCUE-control-wheel-of-your-K100-RGB-keyboard), then use the hardware profile containing those macros. If iCUE overrides onboard settings, configure its active profile too.

If a shortcut is already used by another application, the console names the unavailable G key and the hotkey readiness indicator shows the conflict. Choose a free shortcut or adjust the other application's binding.

## Windows desktop numbering

Create the required desktops with **Win+Tab**, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-desktops.ps1
```

This creates missing desktops up to six without deleting or renaming existing ones. For a larger setup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-desktops.ps1 -EnsureCount 8
```

Desktop numbers follow their order in Task View. Reordering or deleting a desktop changes what its number selects. Manual desktop changes do not automatically change OBS scenes.

The included helper targets Windows 11 24H2 shell interfaces. If compatibility changes after a Windows update, consult the [desktop helper notes](../desktop/README.md).

## Connection and local files

For OBS on this computer, use `localhost`. For a remote OBS computer, use its hostname or IP address without `ws://` or a port in the host field. The default WebSocket port is **4455**. Keep remote OBS awake and use a stable LAN hostname or DHCP reservation.

The bridge uses `ws://` on a trusted LAN. Windows DPAPI protects saved passwords on disk for the current Windows account; it does not encrypt WebSocket traffic.

| Local file | Contents |
| --- | --- |
| `config.json` | All profiles, connection details, macros, encrypted credentials, and active profile selection |
| `config.json.before-profiles.bak` | Exact backup of an older single-profile configuration |
| `status.json` | Last action, connection state, active profile, macro count, and hotkey readiness |
| `bridge.log` | Local error details, which can include host and scene names |

These files, their temporary/backup copies, and generated programs are ignored by Git. Do not share runtime files. `set-password.ps1` updates the saved active profile's credential through a secure prompt; restart the bridge afterward to load it.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Connection refused or timed out | OBS is running, its WebSocket server is enabled, and both computers can reach each other through the firewall. |
| Authentication failed | Re-enter the password from OBS WebSocket Server Settings and save the profile. |
| Saved password cannot be decrypted | Run under the Windows account that saved it, or enter and save the credential again. The existing blob is preserved until replaced. |
| Macro key does nothing | Try its full keyboard shortcut directly, check the active bridge and keyboard profiles, and review any hotkey conflict. |
| Profile will not switch | Finish any running connection or macro, and resolve invalid fields such as duplicate shortcuts or profile names. |
| Desktop does not exist | Create it in Win+Tab or run `setup-desktops.ps1` with the required count. |
| Windows desktop switched but OBS did not | Read the console's partial-failure message, then check the OBS connection and assigned scene. |
| Desktop switching broke after a Windows update | Review the version-specific [desktop helper](../desktop/README.md). |

For an independent, read-only authentication and scene-list check, see [diagnostics](../diagnostics/README.md). Diagnostic output contains your host and scene names; keep it local.
