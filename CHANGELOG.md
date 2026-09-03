# Changelog

All notable user-facing changes to CodexHp are documented here.

## [Unreleased]

### Added

- Show clear loading, sign-in, reconnect, and temporary-unavailability messages in the overlay and tray tooltip when Codex usage is not yet available.
- Detect newly available Codex credentials on the regular usage poll without requiring a CodexHp restart.

### Changed

- Scale gauge percentage text in proportion to its row height, using the current 200% appearance as the reference while keeping it at least 8.5 DIP tall.
- Remove the blank space between each quota gauge and its refresh track while preserving separation between the two gauge groups.
- Identify local development builds as `CodexHp-Dev` in About while official release builds remain `CodexHp`.

## [0.3.6] - 2026-09-02

### Fixed

- Restore the exact taskbar overlay position after a Windows display-scaling change repositions the hosted window.

## [0.3.5] - 2026-09-02

### Fixed

- Use the complete configured graph area and report its actual visible duration without adding time-alignment padding.
- Target a 20-minute history only when creating or resetting default appearance settings; preserve existing user appearance values.

## [0.3.4] - 2026-09-02

### Fixed

- Keep the visible token history reported in Settings identical to the graph's actual time window across display scaling configurations.

## [0.3.3] - 2026-09-02

### Fixed

- Preserve a chosen overlay position when the resolution changes on the same display.
- Keep the existing overlay placement while Windows temporarily recreates the taskbar, then retry the display refresh.

### Changed

- Rename the tray-menu entry from `Options` to `Settings`.

## Earlier releases

Release notes before 0.3.3 were not maintained in this file. See the [GitHub Releases](https://github.com/netics01/CodexHp/releases) page for the published release history.
