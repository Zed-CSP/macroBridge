# Windows virtual desktop helper

This folder vendors Markus Scholtes's unmodified [VirtualDesktop](https://github.com/MScholtes/VirtualDesktop) Windows 11 24H2 C# source, version 1.21 (2025-08-11). Its original MIT copyright notice is preserved in [LICENSE.VirtualDesktop](LICENSE.VirtualDesktop).

Upstream source: [VirtualDesktop11-24H2.cs](https://github.com/MScholtes/VirtualDesktop/blob/master/VirtualDesktop11-24H2.cs).

Source SHA-256:

```text
726d5703b0e7ebb628e4e618728a6ddd474df25dddfe974259d7bc40f937c1b8
```

## Build

The repository's root `build.ps1` compiles this helper automatically. To build it separately:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\desktop\build.ps1
```

The script uses the Windows .NET Framework compiler; no package download is needed. `Compile.upstream.bat` is preserved for provenance and refers to files from the full upstream project. Use `build.ps1` for this repository.

## Compatibility and read-only check

The helper uses version-specific Windows shell COM interfaces. A successful build does not prove compatibility with every Windows update. Verify on the target computer:

```powershell
& .\desktop\VirtualDesktop11-24H2.exe /List /GetCurrentDesktop
```

This lists desktops without creating, switching, or removing any. It needs a normal interactive Windows user session; a restricted sandbox may deny access to the Windows shell. Administrator access is not required.

## CLI conventions

- Desktop indices are zero-based: `/Switch:0` targets Windows Desktop 1.
- `/Animation:Off /Switch:1` targets Desktop 2 without animation.
- `/Quiet /GetCurrentDesktop` returns the current index in the process exit code.
- `/Quiet /Count` returns the desktop count in the process exit code.
- Nonnegative exit codes are successful values, including desktop indices 1–5. Negative values represent errors.
- `/Switch` fails when the requested desktop does not exist.

The bridge checks the desktop count before switching and verifies the resulting current index. Rerun the read-only check after Windows feature updates.
