# Simulation — developer tool

Simulation is a **development and screenshot tool**, not an end-user feature or a source of real account information. It is available only in Development builds, including Release-configuration Development builds. Official builds exclude the simulation implementation and its launch path.

## Start and stop

Exit any running CodexHp instance first. Simulation uses the same single-instance guard as the normal app; it does not start a second overlay alongside it.

Build the Development executable using the existing verification workflow:

```powershell
pwsh -NoProfile -File .\scripts\Verify-Core.ps1
```

Launch from the repository root:

```powershell
.\out\win-x64\CodexHp.exe --simulate
.\out\win-x64\CodexHp.exe --simulate service-issue
```

The first command selects `normal`. Use only one launch command at a time. An invalid preset is rejected, not silently replaced by live mode.

Close the **CodexHp Simulation — Developer tool** window, or choose Exit in the tray menu, to end the simulation. Start the same executable **without arguments** to return to real data. Simulation mode is never saved for the next launch.

## Controls

- **Scenario:** switch presets without rebuilding. Selection restarts the scenario's clock and selects its intended upper-gauge content.
- **Freeze simulated time:** enabled initially. Freezes reset countdowns, expiration calculations, and token-history movement together. Uncheck it to advance at normal speed.
- **+1 minute / +1 hour / +1 day:** advance simulated time, including while frozen.
- **Restart scenario:** return to the session's initial simulated time. Freeze/play selection stays unchanged.
- **Tooltip:** `Hover` uses normal hover behavior; `Pinned` keeps the real tooltip visible without positioning the cursor; `Hidden` suppresses it. Placement and rendering remain the production implementation.
- **Sample appearance & position:** use the normal settings editor against temporary, in-memory settings. Colors, sizes, theme, and location do not affect the user's saved settings. Windows startup is disabled in this sandbox.
- **Hide controls for capture:** hide the developer window without hiding the overlay. Reopen it by clicking the tray icon, choosing Settings in its menu, or double-clicking the overlay.

The control window, tray tooltip, sample settings title, and About heading identify Simulation. The overlay itself has no simulation badge, so it can be photographed without editing out a watermark. Screenshots still contain **illustrative data** and should not be presented as measured account usage or evidence of a real outage.

## Presets

| ID | Scenario |
| --- | --- |
| `normal` | 5H 100%, Week 70%, deterministic token activity; suitable for README captures |
| `low-usage` | 5H 35%, Week 8%, both reset times |
| `service-issue` | Grouped APIs and Codex incidents |
| `long-issue` | Synthetic product groups for wrapping and scrolling checks |
| `banked` | Three credits, two nearest expiration times |
| `banked-zero` | Confirmed zero credits |
| `banked-soon` | Next credit expires in 30 minutes |
| `banked-missing` | Known count, missing expiration details |
| `banked-unavailable` | Usage available, reset-credit inventory unavailable |
| `loading` | Initial usage request pending |
| `failed` | Usage failure with no previous successful snapshot |
| `stale` | Usage failure retaining the last successful snapshot |
| `sign-in` | Credentials missing |
| `reconnect` | Credentials unusable |
| `status-unknown` | Usage available, service status unknown |
| `update-available` | Sample newer release; show the tray and Settings update links |
| `update-current` | Successful check with no newer release; hide the update links |
| `update-failed` | Failed check; retain a previously confirmed sample update, if any |

Simulated update links display the target release URL in a preview dialog instead of opening a browser. No GitHub request is made. Select `update-available` followed by `update-failed` to verify that a transient failure does not hide a known update.

## Isolation and limits

Simulation starts from product defaults, not personal configuration. All setting edits are discarded on exit. It does not read credentials, token-history files, or saved settings; does not call usage, status, or update APIs; and does not change startup registration or production logs. Operating-system monitor, taskbar, theme, and window APIs remain real.

The simulator creates provider states and feeds the same `UsageOverlayStateReducer`, overlay renderer, tooltip, and taskbar host used by the product. It does **not** exercise HTTP parsing, authentication, network retry timing, or real Codex token scanning. Those require their own tests.

The virtual clock starts at the launch minute. Restarting a scenario repeats the same timestamps within that session. Advancing beyond a reset or credit expiry intentionally shows pending/expired data; it does not fabricate a subsequent successful server response or replenish inventory. Select/restart a preset to begin again.

Changing a simulated scenario does not change Windows resolution or DPI. Use actual display changes for OS-level placement/DPI acceptance tests.

## Maintenance

Developer-only code lives in `src/CodexHp.App/Development/`. Shared UI hooks are guarded by `CODEXHP_DEVELOPMENT`, which is defined by `CodexHpBuildFlavor=Development`, not by `DEBUG`. The project excludes the Development directory from Official compilation.

Add reusable situations to `SimulationSession.Presets` and `CreateState`, then test their domain states and time behavior. Avoid adding screenshot-only copies of the overlay or tooltip renderer. Keep generated captures under ignored `out/`; the tool source and this guide belong in version control.
