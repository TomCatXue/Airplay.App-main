# Repository Guidelines

## Project Structure & Module Organization
- `AirPlay.App/`: WinUI 3 desktop application (.NET 10, C# preview) for ARM64 and x64.
  - `Windows/`: XAML windows and views (`ControlWindow`, `MirrorWindow`, `SettingsPage`, `ControlPage`).
  - `Services/`: App services (`AudioPlayService`, `MirrorService`, `AppSettingsService`, `SmtcControlService`).
  - `FFmpeg/` & `Libraries/`: FFmpeg interop (`NativeFFmpeg.cs`) and native decoding binaries (`avcodec-62.dll`, `libfdk-aac.dll`).
  - `Assets/`: App icons, logos, and packaging tiles.
  - `Package.appxmanifest`: Package identity, capabilities, and versioning.
- `Airplay.Core2/AirPlay.Core2/`: Protocol core (.NET 10 library) implementing RTSP (ports 5000/7100), mDNS discovery, FairPlay/crypto (Curve25519/Ed25519), and audio decoding (ALAC, AAC-ELD).
- `Airplay.Core2/AirPlay.Example.ConsoleApp/`: Headless console demonstration.
- `Airplay.App.slnx`: Solution entrypoint connecting UI and protocol core.

## Build, Test, and Development Commands
- **Build Core Library**:
  ```powershell
  dotnet build Airplay.Core2/AirPlay.Core2/AirPlay.Core2.csproj
  ```
  Fast compilation check for protocol engine and decoders.
- **Compile App (No Packaging)**:
  ```powershell
  dotnet build AirPlay.App/AirPlay.App.csproj -c Debug -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64 -p:AppxPackageSigningEnabled=false -p:AppxPackage=false
  ```
  Inner-loop build for development (`Platform=x64` and `win-x64` for x64).
- **Build Signed Self-Contained ARM64 MSIX** (production — required for installable output):
  ```powershell
  $env:AirPlayCertificatePassword = '<pfx password>'
  dotnet build AirPlay.App/AirPlay.App.csproj -c Release -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64 -p:AppxPackageDir=AppPackages-SelfContained/ -p:GenerateAppxPackageOnBuild=true
  ```
  Generates a signed, self-contained MSIX plus its companion `.cer`.
  Never hardcode the password; see `docs/SIGNING.md` for the three supported injection methods
  and for why `-p:AppxPackageSigningEnabled=false` yields an **uninstallable** package.
- **Install MSIX Package**:
  ```powershell
  Import-Certificate -FilePath "AirPlay.App\AppPackages-SelfContained\<PackageDir>\AirPlay.App_<version>_arm64.cer" -CertStoreLocation Cert:\LocalMachine\Root
  Add-AppxPackage -Path "AirPlay.App\AppPackages-SelfContained\<PackageDir>\AirPlay.App_<version>_arm64.msix"
  ```
  The `.cer` must be imported once (Trusted Root) before the package installs.

## Coding Style & Naming Conventions
- **Style**: C# preview, 4-space indentation, file-scoped namespaces (`namespace AirPlay.App;`), and nullable types enabled (`#nullable enable`).
- **Naming**: `PascalCase` for types, methods, properties, and XAML elements; `camelCase` for parameters, locals, and private fields.
- **Interop**: Place P/Invoke declarations and pointer code strictly under `AllowUnsafeBlocks` with explicit null guards (e.g., `NativeFFmpeg.cs`).

## Testing Guidelines
- Run unit tests on protocol decoders and parsing with `dotnet test`.
- Verify manual scenarios before releases:
  - **Mirroring**: Default 85% window scaling, dynamic crop/aspect ratio, and full-screen toggle.
  - **Networking**: mDNS discovery on physical LAN (exclude virtual NICs) and open TCP ports 5000/7100.
  - **Audio & Media**: AAC-ELD/ALAC playback and SMTC lock-screen sync.

## Commit & Pull Request Guidelines
- **Commit History Style**: Follow concise action-oriented messages:
  - Format: `<action> v<version>：<summary>` or `<type>: <description>`
  - Examples: `发布 v1.0.1：控制面板设置入口 + 投屏全屏按钮`, `更新描述`.
- **Pull Requests**: Keep PRs focused, specify target platform (ARM64/x64), update version in `Package.appxmanifest` when packaging, and never commit private certificates or keys.
