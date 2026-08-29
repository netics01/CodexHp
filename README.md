# CodexHp

[한국어](README.ko.md)

CodexHp is a Windows 11 desktop overlay for monitoring Codex usage. It displays session and weekly usage gauges, recent local token activity, and an OpenAI service-status indicator above the taskbar.

> CodexHp is an independent, unofficial project. It is not affiliated with, endorsed by, or supported by OpenAI.

## Status

Version 0.2.0 introduces installer-first distribution. For regular use, download `CodexHp-Setup-<version>-x64.exe` from [GitHub Releases](https://github.com/netics01/CodexHp/releases). It installs for the current user under `%LocalAppData%\Programs\CodexHp`, adds a Start menu shortcut and an uninstall entry, and offers to start CodexHp when you sign in. The startup choice is selected on the first install and preserved by later upgrades.

`CodexHp-Portable-<version>-x64.exe` remains available for temporary or portable use. Move it out of Downloads or temporary directories before enabling **Run CodexHp when Windows starts**; CodexHp disables that option in locations likely to be cleaned or moved. The installer is the recommended choice for an always-running companion.

## Requirements

- Windows 11 build 22000 or later (x64)
- .NET 10 SDK for development builds
- The ChatGPT desktop app installed, signed in, and able to use Codex

CodexHp is designed for the Codex experience in the ChatGPT desktop app. It does not support other operating systems, OpenCode, or general ChatGPT conversation usage.

## What it does

- Shows remaining session and weekly Codex usage, plus reset progress.
- Displays recent local Codex token activity as a compact graph.
- Indicates known OpenAI service issues and hides the overlay when a fullscreen app covers its monitor.
- Provides a tray icon and settings window for appearance, placement, visibility, and startup behavior.

Double-click the overlay or click the tray icon to open settings. The app normally starts in the notification area and keeps the overlay visible even while usage data is unavailable.

## Data and privacy

CodexHp reads the existing Codex authentication cache from `%CODEX_HOME%\auth.json` or `%USERPROFILE%\.codex\auth.json` and local Codex activity data to retrieve and display usage. It sends the existing authentication token only with the usage request required for that display.

CodexHp does not perform sign-in, store the authentication token in its settings, or intentionally write it to its logs. The usage endpoint and local data formats are not a public compatibility contract and may change without notice; the app can stop working as a result. Review the source before using it with your account.

## Develop and verify

Run a development build:

```powershell
pwsh -NoProfile -File .\scripts\Run-Development.ps1
```

Build, test, and create a self-contained single-file `win-x64` publish:

```powershell
pwsh -NoProfile -File .\scripts\Verify-Core.ps1
```

The local publish output is `out\win-x64\CodexHp.exe`. The `out` directory is intentionally untracked.

Build the per-user installer with Inno Setup 6:

```powershell
pwsh -NoProfile -File .\scripts\Build-Installer.ps1
```

The installer is written to `out\installer`. To exercise a real first install, GUI startup, upgrade with startup disabled, and uninstall, close CodexHp and run:

```powershell
pwsh -NoProfile -File .\tests\Windows\Validate-Installer.ps1
```

The GitHub release workflow requires the `WINDOWS_SIGNING_CERTIFICATE_BASE64` and `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` secrets in the `release` environment. It signs both the portable executable and installer, refuses unsigned release assets, publishes checksums, and produces validated WinGet manifests for `netics01.CodexHp`. Creating a WinGet pull request remains a separate maintainer action after the signed GitHub Release is available.

## License

Licensed under the [Apache License, Version 2.0](LICENSE).
