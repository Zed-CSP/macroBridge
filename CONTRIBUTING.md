# Contributing

Build from the repository directory with Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-hooks.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\artifacts\app
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-check.ps1
```

Keep the bridge compatible with the built-in .NET Framework C# compiler (C# 5). `App.cs` owns configuration, orchestration, and tray behavior; `Ui.cs` owns layout; `Theme.cs` owns colors and custom controls; `ObsClient.cs` owns the OBS v5 protocol.

Use synthetic hosts such as `obs-studio.local` or the documentation address `192.0.2.10` in fixtures. Do not include saved settings, credentials, real LAN addresses, local logs, or screenshots with personal scene names. Preserve exact scene strings, including Unicode and spaces.

For visual changes, regenerate the sample-data preview with `scripts/render-preview.ps1` and inspect it at the normal and minimum window sizes. Keep native keyboard access, a masked password field, and clear Test behavior.

Before committing, inspect `git diff --cached` and run `scripts/check-repository.ps1 -Staged`. Tests must use mock OBS transports and must not create or switch real desktops. Keep the upstream desktop helper and its license notice intact unless a helper update is the explicit purpose of a change.
