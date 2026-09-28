# Read-only OBS authentication check

This diagnostic uses [obs-websocket-dotnet 5.7.0](https://www.nuget.org/packages/obs-websocket-dotnet/5.7.0) rather than the bridge's custom WebSocket client. It reads the active profile from `../config.json` (or an older single-profile configuration), decrypts the saved credential with Windows DPAPI for the current Windows user, waits for OBS authentication, and requests only the scene list. It never changes a scene or prints the password.

From the bridge repository directory, build and run:

```powershell
dotnet build .\diagnostics\ObsAuthDiagnostic.csproj --configuration Release
dotnet .\diagnostics\bin\Release\net8.0-windows\ObsAuthDiagnostic.dll
```

Run under the same interactive Windows account that saved the bridge settings, since DPAPI cannot decrypt them under another account or a restricted execution sandbox. `AuthenticationFailed` means OBS rejected the saved credential. A successful connection prints the scene names.

This optional tool requires the .NET 8 SDK and restores its two pinned NuGet dependencies. It is not part of the dependency-free bridge build. Its output contains your host and scene names; keep diagnostic output local.
