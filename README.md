# CodexHp

CodexHp is a Windows 11 desktop overlay that shows OpenAI token-usage gauges and activity graphs above the taskbar. It runs as a single self-contained executable, provides a tray-based settings window, and reads the local Codex/ChatGPT desktop usage context available to the signed-in user.

## Development

Requirements:

- Windows 11
- .NET SDK compatible with the project targets
- An installed and signed-in ChatGPT desktop app

Run the development build:

```powershell
pwsh -NoProfile -File .\Scripts\Agent\Run-Development.ps1
```

Run the build, test, and single-file publish workflow:

```powershell
pwsh -NoProfile -File .\Scripts\Agent\Verify-Core.ps1
```

The published executable is created locally at `out\win-x64\CodexHp.exe`. The `out\` directory is intentionally untracked.

## Documentation

Product requirements, design history, acceptance material, and development notes are in [Docs](Docs/).

## Distribution status

This repository is currently prepared for public publication. A remote repository, release process, and license will be selected separately before publication.
