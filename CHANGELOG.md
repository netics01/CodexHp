# Changelog

All notable user-facing changes to CodexHp are documented here.

## [Unreleased]

### Added

- Add an optional Banked resets upper-bar mode showing the owned reset count and nearest known expiry, with detailed expiration dates and 5-hour usage in the standard tooltip. Keep 5H usage as the default mode and preserve weekly usage when reset-credit details are unavailable.

## [0.4.0] - 2026-09-19

### Added

- Add Light, Dark, and System overlay color modes in Settings, with a crisp light palette, separate per-theme custom colors, and readable percentage text across filled and empty gauge regions.
- Automatically switch the tray icon between white and black Codex marks to match the Windows taskbar's dark or light theme, including theme changes while running.
- Open the GitHub repository in the default browser from the tray menu.

### Fixed

- Remove the icon's opaque charcoal background and dark matte from logo edges, retaining the HP gauge and transparent negative spaces at every ICO resolution. Restore the complete logo with top padding so its upper curve is no longer clipped.
- Automatically recover saved taskbar placement after temporary startup taskbar detection or hosting failures, with fast initial retries and slower background recovery. Suspend recovery while editing the overlay position and log recovery state transitions without repeating unchanged failures.

### Changed

- Remove the service-status stripe's top and bottom insets so it spans the full overlay height.
- Separate color setting names and descriptions into two lines, with wrapping descriptions in narrow Settings windows. Increase the default Settings height so all color controls fit without scrolling.
- Divide the weekly refresh gauge into seven day-sized sections and the 5-hour refresh gauge into five hour-sized sections with visible gaps.
- Scale overlay spacing, refresh tracks, segment gaps, and position outlines in DIP, preserving the 200% reference appearance. Keep graph hairlines and dash patterns in physical pixels.
- Keep reported token history synchronized with the DPI-scaled graph viewport without rewriting existing appearance settings.
- Remove the gauge-side right inset, reducing the gauge-to-graph gap to 2 DIP without changing the graph viewport.
- Remove the gauge top/bottom insets and graph top/right/bottom insets; keep the 1px graph baseline visible along the bottom edge.
- Use a 32 DIP overlay height and 48 DIP gauge pane by default, with a reference width of 130 DIP. Continue adjusting first-run/reset width toward 20 minutes of token history on the target display, subject to the minimum window width.
- Open the color picker directly from each color chip instead of a separate Pick button. Use #005A86 as the default light-mode refresh gauge color.
- Refresh the English and Korean README illustrations with conceptual guides to usage, placement, colors, and independently adjustable element sizes.

## [0.3.8] - 2026-09-17

### Added

- Show time remaining until the weekly and 5-hour usage limits reset when hovering over the overlay, including when OpenAI services are operational.
- List the weekly reset first and hide the 5-hour reset when the blue usage bar shows 100% remaining.

### Changed

- Separate reset times from service issue details with a blank line in the tooltip.
- Group affected service components by product using the OpenAI status page's group structure, with each product on its own line.

## [0.3.7] - 2026-09-03

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
