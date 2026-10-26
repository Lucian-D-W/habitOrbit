# Development and Windows packaging

The repository contains the .NET desktop host, the Tauri/React manager, and isolated automated tests. Normal use starts with the installer linked from the README; the tools below are only for development.

## Prerequisites

- Windows x64, PowerShell 7, and the .NET SDK pinned by `global.json` (10.0.401).
- Node.js and npm. The release was built with Node 24.19.0 and npm 11.17.0.
- Rust MSVC, Visual Studio C++ Build Tools, a Windows SDK, and WebView2. See the [Tauri Windows prerequisites](https://v2.tauri.app/start/prerequisites/#windows).
- For installer compilation, Inno Setup 6 and the Microsoft-signed offline WebView2 x64 and VC++ x64 redistributable installers.

Package versions are fixed in `package-lock.json`, `Cargo.lock`, and the .NET `packages.lock.json` files. The main components are Tauri 2, React 19, .NET 10, Win2D 1.4.0, Windows App SDK 2.5.1, and SQLite.

## Build

From the repository root:

```powershell
pwsh -File scripts/build.ps1 -Locked
```

The complete application is written to `artifacts/app/`. Open `artifacts/app/Pulse.exe` to use the development build. The host is self-contained and the manager is copied beside it. `-Configuration Debug` creates a debug build; `-Dotnet` can select an installed SDK executable. `-TargetDirectory` (or `CARGO_TARGET_DIR`) selects a reusable Rust cache; otherwise it is `target/` in this repository. No parent project is required.

Use an explicit isolated profile for development work that should not touch everyday records:

```powershell
artifacts/app/Pulse.exe --data-dir C:/path/to/habitOrbit-dev-profile
```

Keep that profile across rebuilds. Program files and data have separate lifecycles. Do not delete or replace a real database as part of a build.

## Tests

```powershell
cd apps/manager
npm ci
npm test
npm run build
cd ../..
dotnet restore tests/Pulse.Core.Tests/Pulse.Core.Tests.csproj --locked-mode
dotnet run --project tests/Pulse.Core.Tests/Pulse.Core.Tests.csproj --no-restore
```

The .NET test executable uses isolated databases. Native diagnostics are available through `artifacts/app/Pulse.Host.exe --diagnostics` and `artifacts/app/Pulse.exe --diagnostics`; these inspect runtime readiness and paths without writing everyday records. UI behavior still requires a person to try it.

## Installer

Download the [offline WebView2 x64 installer](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) and [VC++ x64 redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe). Place them in `.tools/runtime-installers/` as `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` and `vc_redist.x64.exe`, then run:

```powershell
pwsh -File scripts/package.ps1 -SkipBuild -Iscc 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

Omit `-SkipBuild` to build first. `-DependencyDirectory` selects another location for the two Microsoft installers. Their signatures are checked before packaging. The result is `artifacts/installer/habitOrbit-Setup-x64.exe`.

`collect-licenses.ps1` gathers dependency notices from restored npm, NuGet, and Cargo packages, together with the checked-in supplements under `licenses/`. It fails if a required notice is absent. The package includes the project license, copyright scope, and collected third-party notices.

The application retains the existing `Pulse` installation/data identifiers for compatibility. Renaming the display brand does not migrate user data. Installation defaults to the current user's Programs/Pulse directory; records are stored separately in LocalAppData/Pulse. Uninstalling leaves user records in place.
