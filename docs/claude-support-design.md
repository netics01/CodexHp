# Claude support — design decisions

This document records the agreed design for adding Claude as a second usage provider next to Codex. It is a decision record for implementation, not end-user documentation.

- **Decided:** 2026-10-14
- **Status:** Implemented on `main` (unreleased)
- **Open:** see [Open items](#open-items)

## Goals

- Keep CodexHp's core property: it works immediately after installation, without extra setup, and shows usage on the taskbar without clicking.
- Show Claude usage with the same gauges, reset gauges, and token activity graph used for Codex.
- Read only data that the official apps already write locally. Never read, store, or send Claude credentials.

## Supported Claude surface

Claude support targets the **Claude desktop app** (Claude for Windows, Microsoft Store/MSIX package `Claude_*`).

| User setup | Usage percentages | Reset times | Token graph |
| --- | --- | --- | --- |
| Claude desktop app | Yes | Yes, when the app's cache holds a recent response | Yes (Code tab and CLI sessions) |
| Claude Code CLI only | No | No | Yes |
| claude.ai in a browser only | No | No | No |

## Data sources

### Adopted

| Data | Source | Notes |
| --- | --- | --- |
| 5-hour and weekly utilization, reset times | Desktop app HTTP cache: `%APPDATA%\Claude\Cache\Cache_Data`, entry for `https://claude.ai/api/organizations/{org}/usage` | Preferred source. Body fields `five_hour` / `seven_day` with `utilization` (percent used) and `resets_at` (ISO 8601 UTC). |
| 5-hour and weekly utilization (fallback) | Desktop app history: `%APPDATA%\Claude\plan-usage-history.json` | `samples[].t` (Unix ms), `samples[].org`, `samples[].u.fh` (5-hour % used), `samples[].u.sd` (7-day % used). No reset times. |
| Token activity graph | `%USERPROFILE%\.claude\projects\**\*.jsonl` (or `%CLAUDE_CONFIG_DIR%\projects`) | Assistant entries carry `timestamp` and `message.usage`. |
| Service status | `https://status.claude.com/api/v2/` (Atlassian Statuspage) | Components have no groups; show a flat list. |
| Claude app running / foreground | Process path under `C:\Program Files\WindowsApps\Claude_*\app\Claude.exe` | Distinguishes the desktop app from the bundled CLI under `%APPDATA%\Claude\claude-code\`. |

Verified on 2026-10-14 on the maintainer's machine:

- The history values matched the percentages shown by the desktop app.
- The cache held three `usage` entries, all `HTTP 200`, body compressed with **zstd**. The newest entry was more recent than the newest history sample, so the cache wins when both exist.
- Cache files are locked by the running app. They open with `FileShare.ReadWrite | FileShare.Delete`.
- The cache uses Chromium's blockfile format (`index`, `data_0..3`, `f_*`). Entry keys carry a partition prefix such as `1/0/`.

### Rejected

| Method | Reason |
| --- | --- |
| Claude OAuth token + `api.anthropic.com/api/oauth/usage` | Claude Code [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance): developers "may not collect, store, or intermediate Claude.ai credentials or session tokens". Enforcement may happen without notice and can affect the user's account. |
| Decrypting the desktop app token cache (`config.json` `oauth:tokenCacheV2`) | Same policy issue, plus security-software false positives for credential-decryption code. |
| claude.ai `sessionKey` browser cookie | Same policy issue; browser cookie decryption looks like infostealer behavior. |
| Driving the CLI through a pseudo-terminal and parsing `/usage` | Fragile; conflicts with running sessions. |
| Claude Code status line `rate_limits` | Officially documented, but requires user setup, covers only the CLI, and only Pro/Max. May be revisited as an optional source for CLI-only users. |

## Data source limits

- The app's internal files are undocumented. A change to the history format, the cache format, the API path, the response fields, or the compression can break a source without notice. Each source must fail independently: losing the cache only removes reset times; percentages continue from the history file.
- CodexHp cannot trigger a refresh. Values update only when the desktop app fetches usage. Observed intervals were 5–15 minutes for history samples and 20–30 minutes for cache entries.
- Values freeze while the desktop app is closed.
- Login state cannot be detected; only missing or stale data can.
- Utilization includes all Claude usage on the account (chat, Cowork, cloud sessions). The token graph covers only local Code tab and CLI sessions.
- There is no Claude equivalent of Codex banked resets.

## Provider switching

CodexHp shows **one provider at a time** and follows the foreground window.

| Situation | Behavior |
| --- | --- |
| ChatGPT desktop app is the foreground window | Show Codex |
| Claude desktop app is the foreground window | Show Claude |
| Any other foreground window (browser, Explorer, terminal, VS Code) | Keep the last shown provider |
| Codex or Claude CLI running in a terminal, or used through an editor extension | Not detected in the first version; keep the last shown provider |
| Quick Alt+Tab passes | Switch only after the target window stays in the foreground for 0.3 seconds. Foreground changes are observed through a Windows event, so the switch is not delayed by the 1-second poll. (Originally 1–2 seconds; shortened on 2026-10-14 because switching felt slow.) |
| User preference | Setting: `Automatic` / `Always Codex` / `Always Claude` |
| Only one provider is available | Show that provider; no switching |

## Display

### Gauges

- **Codex:** the upper bar keeps the existing choice between the 5-hour limit and banked resets.
- **Claude:** the upper bar always shows the 5-hour limit. It is not configurable.
- Both providers use the **same color settings**.

### Provider label

- The graph shows the provider name as small, very faint text in its center: `Codex` or `Claude`.
- Text only. No company logos.
- On a provider switch, the label becomes slightly brighter for a moment, then returns to its normal faint level. The change must be noticeable when looking directly at the overlay, but must not draw attention from elsewhere on the screen.

### Token graph

- The vertical scale is relative and is computed **per provider**. Heights are not comparable across providers.
- Claude token counting follows the same meaning as the existing Codex scanner (for example, how cached input is treated). Confirm the Codex definition before implementing the Claude scanner.
- Claude log entries may repeat for one response; deduplicate by message identity before counting.

### Service status

- The status stripe shows issues for the **currently shown provider** only.
- Issues for the other provider appear in its tooltip summary line.

### Stale and missing data (Claude)

| State | Display |
| --- | --- |
| Data older than 30 minutes | Reduce bar saturation; tooltip shows the observation time |
| Data older than 2 hours | Show `—` and empty bars |
| Reset time unknown (no usable cache entry) | Reset gauge shown as unknown; tooltip says the reset time is unavailable |
| Reset time passed without a newer observation | Show "waiting for update"; do not assume 100% remaining |

The tooltip always shows the observation time and source (app cache or history file).

### Tooltip

- Existing provider-specific content (reset countdowns, service issues).
- A final summary line for the other provider, for example `Codex · 5h 78% · weekly 42%`, including its service issue state.

### Visibility

- The existing "show only while ChatGPT is running" option becomes "show only while the Codex or Claude app is running".

## Multiple organizations

When samples or cache entries exist for more than one Claude organization, use the organization from the most recent observation. Never combine values across organizations.

## Privacy and naming

- README and About must state that CodexHp is unofficial and not affiliated with, endorsed by, or supported by Anthropic (in addition to the existing OpenAI statement).
- README must state that, for Claude, CodexHp reads the desktop app's usage history file and only the `usage` response from the app's HTTP cache, and that it does not read Claude tokens or cookies.
- The product name is not decided yet.

## Open items

- Product name.
- Release scope and order (for example, all Claude features in one release, or graph and service status first).
- Resolved: the development simulator has Claude presets (`claude`, `claude-stale`, `claude-expired`, `claude-reset-unknown`, `claude-switch`).
- Resolved: .NET 10 has no zstd decoder; cache bodies are decoded with the `ZstdSharp.Port` package (MIT).
- Meaning of other `usage` response fields (`limits`, `spend`, `extra_usage`, `cedar_ember`); not used for now.
- Optional status line source for CLI-only users.

## References

- Claude Code docs: [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance), [Status line](https://code.claude.com/docs/en/statusline), [Manage costs](https://code.claude.com/docs/en/costs), [Authentication](https://code.claude.com/docs/en/authentication)
- Prior art using the desktop app history file and cache: [aqua5230/usage](https://github.com/aqua5230/usage) (`loaders/claude_desktop.py`, `loaders/chromium_cache.py`)
- Chromium blockfile format: `net/disk_cache/blockfile/disk_format.h` and `addr.h` in the Chromium source
