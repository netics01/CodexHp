# CodexHp

[한국어](README.ko.md)

**See your Codex usage limits and recent token activity at a glance—right on the Windows 11 taskbar.**

CodexHp is a compact taskbar overlay for people who use Codex in the ChatGPT desktop app on Windows 11. Without opening another window, you can see what remains in your 5-hour and weekly limits, when they reset, recent activity from local Codex sessions, and OpenAI service status.

![CodexHp on the Windows 11 taskbar in normal operation and with an orange OpenAI service-issue stripe plus its affected-component tooltip](docs/assets/readme/codexhp-taskbar.png)

**[Get the latest release](https://github.com/netics01/CodexHp/releases/latest)**

*Current downloads are unsigned and may trigger a Windows security warning. See [Install](#install) for details.*

## Codex usage at a glance

![Numbered overview of the CodexHp usage gauges, reset progress, token activity graph, and service-status indicator](docs/assets/readme/codexhp-at-a-glance.svg)

| No. | What you see |
| --- | --- |
| **1** | Remaining usage in your current **5-hour session** and **weekly** windows |
| **2** | Time remaining until each usage window resets |
| **3** | Recent token activity read from your local Codex sessions |
| **4** | OpenAI service incidents, with affected components shown on hover over the status indicator |

The activity graph adds context to the remaining limits. Idle periods, steady work, and sudden bursts are easy to distinguish at a glance.

## Put it anywhere. Make it yours.

![CodexHp on the taskbar, positioned freely on the desktop, and customized with colors and size controls](docs/assets/readme/codexhp-placement.svg)

| No. | Make it yours |
| --- | --- |
| **1** | Keep CodexHp on the Windows taskbar for a native-feeling, at-a-glance display |
| **2** | Drag it to any position on any connected display when the taskbar is not the best fit |
| **3** | Tune the gauge colors, overlay dimensions, graph density, and status indicator to suit your setup |

Open **Overlay Position** in Settings and drag the outlined overlay to place it. Use **Colors** and **Appearance** to make the display fit your taskbar, monitor, and taste.

## Fits your Windows setup

CodexHp keeps saved placement usable across monitor bounds, taskbar layouts, and DPI scales, then gives you control over how and when it appears.

- Starts with Windows by default when installed, and preserves your choice during upgrades.
- Can stay visible all the time or appear only while ChatGPT is running.
- Hides automatically when a full-screen app is active on the same monitor.
- Opens settings when you double-click the overlay or click its notification-area icon.

> Once CodexHp becomes part of your Windows setup, you may wonder how you ever used Codex without it.

## Install

1. Download `CodexHp-Setup-<version>-x64.exe` from the [latest GitHub Release](https://github.com/netics01/CodexHp/releases/latest).
2. Run the per-user installer. It places CodexHp under `%LocalAppData%\Programs\CodexHp` and adds Start menu and uninstall entries.
3. Launch CodexHp from the installer or Start menu. Starting automatically when you sign in is selected by default and can be changed in Settings.

`CodexHp-Portable-<version>-x64.exe` is also available for temporary use. Move the portable build out of Downloads or other temporary locations before enabling Windows startup; CodexHp disables startup registration from locations likely to be cleaned or moved.

> [!WARNING]
> The current release is not Authenticode-signed. Windows SmartScreen or Smart App Control may warn about or block it. Download only from this repository's GitHub Release and verify the files against `SHA256SUMS.txt`. CodexHp is not yet distributed through WinGet.

To calculate the installer's SHA-256 digest in PowerShell, run the following command and compare the result with the matching entry in `SHA256SUMS.txt`:

```powershell
Get-FileHash .\CodexHp-Setup-<version>-x64.exe -Algorithm SHA256
```

### Requirements

- Windows 11 build 22000 or later (x64)
- The ChatGPT desktop app installed, signed in, and able to use Codex

CodexHp is built for the Codex experience in the ChatGPT desktop app. It does not support other operating systems or ordinary ChatGPT conversations.

## Why the name CodexHp?

“HP” comes from health bars in games. When you use Codex frequently, the remaining limit can feel like a resource you need to watch. The name is playful, but the goal is practical—one glance shows whether you can keep going or should pace yourself until the next reset.

## Data and privacy

CodexHp reads the existing Codex authentication cache from `%CODEX_HOME%\auth.json` or `%USERPROFILE%\.codex\auth.json` and local Codex activity data. It uses the cached token only to request Codex usage data from `chatgpt.com`.

CodexHp does not perform sign-in, store the authentication token in its settings or logs, or send it to a separate server operated by the CodexHp developer. It relies on a non-public usage endpoint and local activity formats that may change without notice; CodexHp can stop working if they do. If credential handling is a concern, review the source and release checksums before using the app.

## Build from source

Development requires the .NET 10 SDK pinned by `global.json`. Inno Setup 6 is required only to build the installer.

```powershell
pwsh -NoProfile -File .\scripts\Run-Development.ps1
pwsh -NoProfile -File .\scripts\Verify-Core.ps1
pwsh -NoProfile -File .\scripts\Build-Installer.ps1
```

The development publish is written to `out\win-x64`, and installer output is written to `out\installer`. Both are intentionally untracked. For maintainers, official release assets are built only by this local command; GitHub Actions remains an independent CI check and does not create a second set of binaries.

Regular local builds identify themselves as **CodexHp-Dev** in About. The official build created by the release command identifies itself as **CodexHp**.

```powershell
pwsh -NoProfile -File .\scripts\Publish-LocalRelease.ps1 -AllowUnsignedRelease
```

## Project status

CodexHp is an unofficial early-stage project independent of OpenAI. It is not affiliated with, endorsed by, or supported by OpenAI. Changes to ChatGPT, Codex, Windows, or their internal integration details may temporarily break some features.

## Feedback

Have an idea that would make CodexHp more useful on Windows? Please [open an issue](https://github.com/netics01/codexhp/issues) with your use case or feature request.

## License

Licensed under the [Apache License, Version 2.0](LICENSE).
