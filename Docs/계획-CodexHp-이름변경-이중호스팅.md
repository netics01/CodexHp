# CodexHp 이름 변경·작업표시줄 이중 호스팅 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task in the current workspace. Do not create a worktree, branch, or subagent.

**Goal:** 제품과 저장소 이름을 `CodexHp`로 통일하고, 작업표시줄 내부에서는 자식 HWND로 결합되어 반복 클릭에도 가려지지 않으며 외부에서는 자유로운 topmost 팝업으로 동작하게 한다.

**Architecture:** 저장 위치는 전체 화면 물리 픽셀 좌표로 유지하고, 순수 Core 계산기가 작업표시줄과의 포함·교차 관계로 호스팅 모드와 스냅 위치를 결정한다. App 계층의 작업표시줄 탐색기와 Win32 호스트가 `WS_CHILD + SetParent` 또는 `WS_POPUP + HWND_TOPMOST`를 적용하며, `UsageOverlayWindow`가 드래그와 네이티브 HWND 재생성 수명주기를 연결한다.

**Tech Stack:** Windows 11, .NET 10, C# 14, WPF 옵션 창, Win32/User32, GDI, xUnit, PowerShell 7, ScreenProof

## Global Constraints

- 지원 플랫폼은 Windows 11뿐이다.
- 제품 이름, 루트 폴더, 프로젝트, 어셈블리, 네임스페이스, 실행 파일과 런타임 식별자는 `CodexHp`다.
- 사용량 오버레이 크기와 저장 위치는 정수 물리 픽셀의 전체 화면 좌표다.
- 작업표시줄 HWND는 호스팅과 좌표 변환에만 사용하며 제품 기본 좌표의 정본이 아니다.
- 작업표시줄 내부는 `TaskbarChild`, 외부는 `DesktopPopup`으로 동작한다.
- 작업표시줄 부분 교차 위치는 완전 내부 또는 완전 외부로 스냅한다.
- Deskband11Lib NuGet 패키지와 Windhawk를 런타임 의존성으로 추가하지 않는다.
- 사용자 인증, 사용량 조회, GDI 렌더링, 전체화면 숨김, 트레이와 옵션 트랜잭션 동작을 유지한다.
- 설치 프로그램, 공개 게시와 아이콘 제작은 이번 구현 범위가 아니다.
- 사용자 지시에 따라 브랜치, 워크트리와 다른 에이전트를 사용하지 않는다.

---

### Task 1: 저장소와 제품 이름을 CodexHp로 통일

**Files:**
- Renamed: product root to ``
- Renamed: solution to `CodexHp.slnx`
- Renamed: application and core projects under `src/`
- Renamed: application and core test projects under `tests/`
- Renamed: every solution/project/document filename containing the previous product name
- Modified: every tracked text file containing the previous product name
- Modify: `README.md`
- Test: all renamed test projects and PowerShell verification scripts

**Interfaces:**
- Consumes: the current clean `CodexHp` implementation and committed design document
- Produces: `CodexHp.slnx`, `CodexHp.exe`, `CodexHp.App`, `CodexHp.Core`, `%LOCALAPPDATA%\\CodexHp`, `Local\\CodexHp.SingleInstance`

- [x] **Step 1: Capture the current tracked rename surface**

Run:

```powershell
git status --short
$retiredName = 'CodexHp' + 'Bar'
rg -l -S $retiredName .
rg --files . | Where-Object { $_ -match [Regex]::Escape($retiredName) }
```

Expected: the working tree contains only the committed plan as a new change, and the commands enumerate all names that must move or change.

- [x] **Step 2: Rename tracked folders and files with Git-aware moves**

Tracked folders and files were moved with `git mv`. Because the old root directory was locked against a single directory rename, its tracked children were moved individually into the new root and the verified-empty old root was removed non-recursively.

- [x] **Step 3: Replace product identifiers in tracked text**

Use the split `$retiredName` expression from Step 1 to select only tracked text files. For each file, replace that exact ordinal string with `CodexHp`, write UTF-8 without BOM, and normalize CRLF. Do not touch binary assets or ignored runtime data.

Expected name-bearing values after replacement:

```csharp
namespace CodexHp.App;
public const string DefaultMutexName = "Local\\CodexHp.SingleInstance";
public const string ValueName = "CodexHp";
this.SettingsDirectory = Path.Combine(root, "CodexHp");
this.LogsDirectory = Path.Combine(root, "CodexHp", "Logs");
```

Expected project values:

```xml
<AssemblyName>CodexHp</AssemblyName>
<RootNamespace>CodexHp.App</RootNamespace>
<InternalsVisibleTo Include="CodexHp.App.Tests" />
```

- [x] **Step 4: Verify that the old product identifier is absent**

Run:

```powershell
$retiredName = 'CodexHp' + 'Bar'
$trackedMatches = @(git grep -n -I $retiredName -- .)
$namedPaths = @(rg --files . | Where-Object { $_ -match [Regex]::Escape($retiredName) })
if ($trackedMatches.Count -ne 0 -or $namedPaths.Count -ne 0) { throw 'Old product name remains.' }
```

Expected: no output and no exception. Git history and ignored `%LOCALAPPDATA%` or old `out` artifacts are not rewritten.

- [x] **Step 5: Run the renamed solution tests**

Run:

```powershell
dotnet restore CodexHp.slnx
dotnet build CodexHp.slnx --no-restore
dotnet test CodexHp.slnx --no-build
```

Expected: restore, build and all existing tests pass with zero warnings and zero errors.

- [x] **Step 6: Commit the complete rename**

Run `git diff --check`, inspect `git status --short` and `git diff --stat`, stage only the renamed product tree and root documentation references, then commit:

```powershell
git commit -m "refactor: rename product to CodexHp"
```

---

### Task 2: Make the options window follow the Windows system theme

**Files:**
- Modify: `src/CodexHp.App/App.xaml`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- Test: compiled app plus XAML contract command

**Interfaces:**
- Consumes: .NET 10 WPF `ThemeMode`
- Produces: system light/dark selection at window creation; no live in-window theme-switch requirement

- [x] **Step 1: Verify the current XAML lacks the required theme contract**

Run:

```powershell
if (-not (Select-String -LiteralPath src/CodexHp.App/App.xaml -SimpleMatch 'ThemeMode="System"')) {
    throw 'RED: App.xaml does not follow the Windows system theme.'
}
```

Expected before implementation: command fails with the RED message because the system theme is not yet configured.

- [x] **Step 2: Apply the system theme and remove fixed light-only colors**

Set the root application property:

```xml
<Application x:Class="CodexHp.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown"
             ThemeMode="System">
```

In `SettingsWindow.xaml`, remove fixed `#D0D0D0` and `#666666` values. Use dynamic system brushes:

```xml
BorderBrush="{DynamicResource {x:Static SystemColors.ControlDarkBrushKey}}"
Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}"
```

Keep user-selected color swatches unchanged because they represent saved product colors, not window chrome.

- [x] **Step 3: Verify the theme contract and build**

Run:

```powershell
$appXaml = Get-Content -Raw -Encoding UTF8 src/CodexHp.App/App.xaml
if (-not $appXaml.Contains('ThemeMode="System"')) { throw 'System theme is missing.' }
rg -n -S "#D0D0D0|#666666" src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml
dotnet build CodexHp.slnx
```

Expected: `rg` has no matches and build passes with zero warnings and errors.

- [x] **Step 4: Commit the system theme support**

```powershell
git commit -m "fix: follow Windows theme in options"
```

---

### Task 3: Add the pure physical-pixel host placement decision

**Files:**
- Modify: `src/CodexHp.Core/Positioning/MonitorGeometry.cs`
- Create: `src/CodexHp.Core/Positioning/OverlayHostPlacementCalculator.cs`
- Create: `tests/CodexHp.Core.Tests/Positioning/OverlayHostPlacementCalculatorTests.cs`
- Modify: `tests/CodexHp.Core.Tests/Positioning/OverlayPlacementCalculatorTests.cs`

**Interfaces:**
- Consumes: `PhysicalRect desiredBounds`, `PhysicalRect monitorBounds`, nullable `PhysicalRect taskbarBounds`
- Produces: `OverlayHostPlacement Resolve(...)`, `OverlayHostMode.DesktopPopup`, `OverlayHostMode.TaskbarChild`

- [x] **Step 1: Write failing host placement tests**

Add tests with these exact cases:

```csharp
[Theory]
[InlineData(2090, OverlayHostMode.TaskbarChild, 2090)]
[InlineData(1996, OverlayHostMode.DesktopPopup, 1996)]
[InlineData(2012, OverlayHostMode.DesktopPopup, 1996)]
[InlineData(2050, OverlayHostMode.TaskbarChild, 2078)]
[InlineData(2043, OverlayHostMode.TaskbarChild, 2078)]
public void Resolves_inside_outside_and_partial_overlap(
    int desiredTop,
    OverlayHostMode expectedMode,
    int expectedTop)
{
    var result = OverlayHostPlacementCalculator.Resolve(
        new PhysicalRect(2, desiredTop, 288, 68),
        new PhysicalRect(0, 0, 3840, 2160),
        new PhysicalRect(0, 2064, 3840, 96));

    Assert.Equal(expectedMode, result.Mode);
    Assert.Equal(new PhysicalRect(2, expectedTop, 288, 68), result.OverlayBounds);
}
```

Also test a missing taskbar, a 40px auto-hide taskbar that cannot contain the 68px window, horizontal monitor clamping and a secondary monitor with negative coordinates.

- [x] **Step 2: Run the new tests and observe the expected failure**

Run:

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj --filter FullyQualifiedName~OverlayHostPlacementCalculator
```

Expected: compilation fails because `OverlayHostPlacementCalculator` and `OverlayHostMode` do not exist.

- [x] **Step 3: Implement the minimal placement model**

Create:

```csharp
namespace CodexHp.Core.Positioning;

public enum OverlayHostMode
{
    DesktopPopup,
    TaskbarChild,
}

public readonly record struct OverlayHostPlacement(
    OverlayHostMode Mode,
    PhysicalRect OverlayBounds);
```

Implement `Resolve` so that it clamps to `monitorBounds`, returns child for complete containment, popup for no intersection, and compares taskbar-intersection area with the remaining window area for partial overlap. The partial-overlap child candidate is vertically centered in a horizontal taskbar, and equal areas choose that child candidate. Add `Contains(PhysicalRect)` and `IntersectsWith(PhysicalRect)` to `PhysicalRect` using half-open rectangle bounds.

- [x] **Step 4: Preserve the separation between requested and hosted placement**

Keep the current `OverlayPlacementCalculator` comparison result at Y `2012` because that type represents the requested whole-screen placement before host resolution. Add an assertion that passing that requested rectangle to `OverlayHostPlacementCalculator` with the `2064..2160` taskbar resolves the hosted overlay bounds to Y `1996`. Do not make `OverlayPlacementCalculator` query a taskbar.

- [x] **Step 5: Run the focused and full Core tests**

Run:

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj --filter FullyQualifiedName~Positioning
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj
```

Expected: all Core tests pass with zero warnings and errors.

- [x] **Step 6: Commit the pure host placement calculation**

```powershell
git commit -m "feat: calculate taskbar hosting placement"
```

---

### Task 4: Locate the taskbar and execute Win32 host transitions

**Files:**
- Modify: `src/CodexHp.App/Infrastructure/NativeMethods.cs`
- Create: `src/CodexHp.App/Infrastructure/TaskbarWindowLocator.cs`
- Create: `src/CodexHp.App/Presentation/OverlayWindowHost.cs`
- Create: `tests/CodexHp.App.Tests/Infrastructure/TaskbarWindowLocatorTests.cs`
- Create: `tests/CodexHp.App.Tests/Presentation/OverlayWindowHostTests.cs`

**Interfaces:**
- Consumes: monitor identity or desired overlay bounds, usage overlay HWND, `OverlayHostPlacementCalculator`
- Produces: `TaskbarWindowInfo`, `OverlayWindowHost.Apply`, `OverlayWindowHost.DetachForDrag`, current mode and taskbar handle

- [x] **Step 1: Write failing taskbar locator and style-transition tests**

Add a Windows 11 integration test:

```csharp
[Fact]
public void Finds_the_primary_taskbar_on_the_primary_monitor()
{
    var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
    var result = new TaskbarWindowLocator().FindForMonitor(monitor.Id);

    Assert.NotNull(result);
    Assert.NotEqual(nint.Zero, result.Value.WindowHandle);
    Assert.Equal(monitor.Id, result.Value.MonitorId, StringComparer.OrdinalIgnoreCase);
    Assert.True(result.Value.TaskbarBounds.Height > 0);
}
```

Add pure style tests calling internal helpers:

```csharp
[Fact]
public void Child_style_replaces_popup_and_topmost()
{
    var style = OverlayWindowHost.BuildWindowStyle(0x80000000u, OverlayHostMode.TaskbarChild);
    var exStyle = OverlayWindowHost.BuildExtendedStyle(0x08000088u, OverlayHostMode.TaskbarChild);

    Assert.Equal(0x40000000u, style & 0xC0000000u);
    Assert.Equal(0u, exStyle & 0x00000008u);
    Assert.Equal(0x08000080u, exStyle & 0x08000080u);
}
```

Add the inverse popup test.

Add host-health cases:

```csharp
[Theory]
[InlineData(false, 10, 10, 96, 96, true)]
[InlineData(true, 10, 11, 96, 96, true)]
[InlineData(true, 10, 10, 96, 120, true)]
[InlineData(true, 10, 10, 96, 96, false)]
public void Hosted_window_health_detects_recreation_conditions(
    bool overlayAlive,
    long expectedParent,
    long actualParent,
    uint overlayDpi,
    uint taskbarDpi,
    bool expected)
{
    var health = new TaskbarHostHealth(
        overlayAlive,
        new nint(expectedParent),
        new nint(actualParent),
        overlayDpi,
        taskbarDpi);

    Assert.Equal(expected, health.RequiresRecreation);
}
```

- [x] **Step 2: Run focused tests and observe missing-type failures**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~TaskbarWindowLocator|FullyQualifiedName~OverlayWindowHost"
```

Expected: compilation fails because the locator and host types do not exist.

- [x] **Step 3: Add required User32 contracts**

Add constants and P/Invoke declarations for:

```csharp
internal const int GwlStyle = -16;
internal const uint WsChild = 0x40000000;
internal const uint SwpFrameChanged = 0x0020;
internal static readonly nint HwndTop = nint.Zero;
```

Add `EnumWindows`, `GetClassNameW`, `IsWindow`, `GetParent`, `SetParent`, `GetDpiForWindow`, `MapWindowPoints`, `RegisterWindowMessageW`, and style-safe `GetWindowLongPointer`/`SetWindowLongPointer` calls. Every mutating call must expose success or last-error state to its caller.

- [x] **Step 4: Implement monitor-matched taskbar discovery**

Implement:

```csharp
public readonly record struct TaskbarWindowInfo(
    nint WindowHandle,
    string MonitorId,
    PhysicalRect TaskbarBounds,
    uint Dpi);

public sealed class TaskbarWindowLocator
{
    public TaskbarWindowInfo? FindForMonitor(string monitorId);
    public TaskbarWindowInfo? FindForOverlayBounds(PhysicalRect overlayBounds);
}
```

Enumerate only `Shell_TrayWnd` and `Shell_SecondaryTrayWnd`, map each HWND through `MonitorFromWindow` and `GetMonitorInfoW`, and compare the monitor device name ordinal-ignore-case. Do not infer secondary monitor identity from enumeration order.

- [x] **Step 5: Implement reversible popup/child transitions**

Implement:

```csharp
internal sealed class OverlayWindowHost
{
    public OverlayHostMode Mode { get; private set; } = OverlayHostMode.DesktopPopup;
    public nint TaskbarWindowHandle { get; private set; }

    public PhysicalRect Apply(nint windowHandle, PhysicalRect desiredBounds, string? monitorId);
    public PhysicalRect DetachForDrag(nint windowHandle);
    public bool RequiresRecreation(nint windowHandle);
    internal static uint BuildWindowStyle(uint current, OverlayHostMode mode);
    internal static uint BuildExtendedStyle(uint current, OverlayHostMode mode);
}

internal readonly record struct TaskbarHostHealth(
    bool OverlayAlive,
    nint ExpectedParent,
    nint ActualParent,
    uint OverlayDpi,
    uint TaskbarDpi)
{
    public bool RequiresRecreation =>
        !OverlayAlive
        || ExpectedParent == nint.Zero
        || ActualParent != ExpectedParent
        || OverlayDpi == 0
        || TaskbarDpi == 0
        || OverlayDpi != TaskbarDpi;
}
```

For child mode, clear `WS_POPUP`, set `WS_CHILD`, clear `WS_EX_TOPMOST`, call `SetParent`, verify `GetParent`, convert the overlay origin with `MapWindowPoints`, and use `SetWindowPos(HWND_TOP, ..., SWP_FRAMECHANGED | SWP_NOACTIVATE)`. For popup mode, detach to `NULL`, restore popup and topmost styles, then use screen coordinates with `HWND_TOPMOST`. Hide only during structural transition and restore the preceding visibility afterward. If any child transition fails, restore `DesktopPopup` at the resolved overlay bounds.

The live round-trip test temporarily applies the same Per-Monitor V2 DPI awareness used by the product manifest so monitor, taskbar and usage-overlay window values are all physical pixels. Popup detachment verifies the parent only after restoring `WS_POPUP`, because a detached `WS_CHILD` can temporarily report the desktop parent.

- [x] **Step 6: Run focused tests and full App tests**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~TaskbarWindowLocator|FullyQualifiedName~OverlayWindowHost"
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj
```

Expected: all tests pass with no warnings or errors.

- [x] **Step 7: Commit taskbar discovery and host transitions**

```powershell
git commit -m "feat: host usage overlay window in taskbar"
```

---

### Task 5: Integrate dragging, placement, Explorer and DPI recovery

**Files:**
- Modify: `src/CodexHp.App/Presentation/UsageOverlayWindow.cs`
- Modify: `src/CodexHp.App/App.xaml.cs`
- Modify: `src/CodexHp.App/Application/OverlayPositionController.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/OverlayPositionPreviewIntegrationTests.cs`
- Create: `tests/CodexHp.App.Tests/Presentation/UsageOverlayHostingIntegrationTests.cs`

**Interfaces:**
- Consumes: `OverlayWindowHost`, existing `SetPlacement`, `OverlayPositionChanged`, WPF Dispatcher
- Produces: stable hosted placement, drag detachment/re-attachment, native HWND recreation without process restart

- [x] **Step 1: Write failing integration-contract tests**

Add a failing test for the registered taskbar recreation message:

```csharp
[Theory]
[InlineData(0xC123u, 0xC123u, true)]
[InlineData(0x0201u, 0xC123u, false)]
[InlineData(0u, 0u, false)]
public void Recognizes_only_the_registered_taskbar_created_message(
    uint message,
    uint registeredMessage,
    bool expected)
{
    Assert.Equal(
        expected,
        UsageOverlayWindow.IsTaskbarCreatedMessage(message, registeredMessage));
}
```

Keep tests for `CanBeginDrag` and cancellation restoring the baseline through the host calculator. The focused red condition is the missing `IsTaskbarCreatedMessage` API.

- [x] **Step 2: Run the focused tests and confirm the expected failures**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~OverlayPositionPreviewIntegration|FullyQualifiedName~UsageOverlayHostingIntegration"
```

Expected: new lifecycle and hosting contract assertions fail because the integration is absent.

- [x] **Step 3: Route every placement through OverlayWindowHost**

Construct `UsageOverlayWindow` with a `TaskbarWindowLocator` and `OverlayWindowHost`. Change `SetPlacement` so it builds a `PhysicalRect` from `OverlayPlacement`, applies the host, stores the returned snapped overlay bounds, and returns or exposes the final physical bounds for capture.

Keep this public contract:

```csharp
public void SetPlacement(OverlayPlacement placement);
public PhysicalRect? GetOverlayBounds();
public nint WindowHandle { get; private set; }
```

- [x] **Step 4: Detach before native dragging and re-apply after the move loop**

In `WM_LBUTTONDOWN` position mode:

```csharp
this.windowHost.DetachForDrag(windowHandle);
_ = NativeMethods.ReleaseCapture();
_ = NativeMethods.SendMessageW(
    windowHandle,
    NativeMethods.WmNcLeftButtonDown,
    new nint(NativeMethods.HitTestCaption),
    nint.Zero);

if (this.GetOverlayBounds() is { } moved)
{
    var finalBounds = this.windowHost.Apply(windowHandle, moved, monitorId: null);
    this.OverlayPositionChanged?.Invoke(finalBounds);
}
```

Do not publish an intermediate child-relative coordinate.

- [x] **Step 5: Recreate the usage overlay HWND when the taskbar host becomes invalid**

Register `TaskbarCreated` once, add a one-second Dispatcher timer, and call a private recreation method when either the broadcast arrives or `OverlayWindowHost.RequiresRecreation` returns true. Recreation must preserve the last usage overlay state, settings, placement request, position-change mode, show request and actual visibility. It destroys only the stale usage overlay HWND, creates a replacement under the already registered class, reapplies state and host placement, and updates `WindowHandle`.

- [x] **Step 6: Keep application-level consumers bound to the current HWND**

Verify `WindowsVisibilitySource.Read(this.usageOverlayWindow.WindowHandle)` remains a lambda that reads the current property value. Update startup/error/log strings to `CodexHp`. Ensure shutdown stops the host monitor timer before destroying the final HWND.

- [x] **Step 7: Run focused, App and full solution tests**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~OverlayPositionPreviewIntegration|FullyQualifiedName~UsageOverlayHostingIntegration"
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj
dotnet test CodexHp.slnx
```

Expected: all tests pass with zero warnings and errors.

- [x] **Step 8: Commit integrated window hosting and recovery**

```powershell
git commit -m "feat: switch overlay hosting by placement"
```

---

### Task 6: Publish and verify the Windows 11 behavior

**Files:**
- Modify: `tests/Windows/Validate-PublishedApp.ps1`
- Modify: `Scripts/Agent/Verify-Core.ps1`
- Modify: `Scripts/Agent/Run-Development.ps1`
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`
- Modify: `Docs/설계-CodexHp-이름변경-이중호스팅.md`

**Interfaces:**
- Consumes: final `CodexHp.exe`, taskbar HWND hierarchy, ScreenProof
- Produces: automated single-file/parent/bounds validation and actual Windows 11 desktop evidence

- [x] **Step 1: Update the published-app probe before the production script**

Change the test probe to enumerate both top-level windows and descendants of every `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`. It must find the visible `CodexHp` usage overlay HWND by process ID and title, then return its parent, styles and physical bounds.

Add assertions:

```powershell
if ($files.Count -ne 1 -or $files[0].Name -ne 'CodexHp.exe') { throw 'Single-file publish mismatch.' }
if ($overlayParent -ne $taskbarHandle) { throw 'Product-default usage overlay window is not taskbar-hosted.' }
if (($overlayStyle -band 0x40000000) -eq 0) { throw 'WS_CHILD is missing.' }
```

구현 결과는 자식 HWND 탐색, WPF `HwndWrapper` 클래스, 프레임·`WS_EX_APPWINDOW` 제거, `WS_EX_LAYERED`, 물리 경계와 화면 픽셀 차이까지 검사한다. 마지막 픽셀 검사는 투명하지만 존재하는 HWND를 성공으로 오판한 실제 실패를 막는다.

- [x] **Step 2: Run core verification and published-app validation**

```powershell
pwsh -NoProfile -File Scripts/Agent/Verify-Core.ps1
pwsh -NoProfile -File tests/Windows/Validate-PublishedApp.ps1 -KeepRunning
```

검증 결과: `out/win-x64`에는 `CodexHp.exe` 하나만 있고, 전체 `188개` 테스트와 단일 인스턴스가 통과했다. 제품 기본 창은 `2,2080,288,68`, 부모 `Shell_TrayWnd`, 스타일 `0x56000000`, 확장 스타일 `0x00080000`이며 내부 픽셀은 주변 작업표시줄과 달랐다.

- [ ] **Step 3: Collect ScreenProof evidence**

Run:

```powershell
screenproof doctor
screenproof list --json
screenproof capture --screen primary --output-dir out/screenproof/taskbar-hosted-before-clicks
```

Use stable HWND probes to record the parent and styles. Bring the taskbar to the foreground or click safe taskbar targets repeatedly, then capture:

```powershell
screenproof capture --screen primary --output-dir out/screenproof/taskbar-hosted-after-clicks
```

Inspect each `screenshot.png`, `meta.json` and `windows.json`. The usage overlay must remain visibly unchanged and the parent must remain the taskbar HWND.

현재 `out/screenproof/latest-build-visible/` 전체 화면과 좌하단 확대에서 실제 게이지·그래프를 확인했고 `WindowFromPoint(150,2100)`도 CodexHp HWND를 적중했다. 작업표시줄 반복 클릭의 사용자 체감 확인은 최신 빌드에서 남아 있다.

- [ ] **Step 4: Verify free placement, reattachment and system theme**

Open options from the tray, select 위치 변경, drag the usage overlay fully above the taskbar and verify it becomes a top-level popup with no parent and topmost style. Drag it back into the taskbar and verify `WS_CHILD` and taskbar parent return. Capture the options window in the currently active Windows dark theme and verify its background, text, controls and borders use the system theme without fixed light panels.

자동 실제 픽셀 테스트는 최초 작업표시줄 자식과 데스크톱 팝업을 통과했다. 같은 WPF HWND를 작업표시줄로 되붙일 때 합성 픽셀이 사라지는 실패를 검출했기 때문에, 최종 내부 결합은 새 WPF 표면을 만드는 방식으로 바꿔 세 위치의 지정 픽셀이 모두 통과했다. 보이지 않는 영역을 드래그했던 이전 검증 오류를 반복하지 않기 위해 실제 마우스 왕복은 최신 실행 파일을 사용자가 볼 수 있는 상태에서 확인할 항목으로 남긴다.

- [ ] **Step 5: Verify fullscreen and lifecycle recovery**

Check that a fullusage overlay window on the hosted monitor hides the usage overlay and that it returns in its preceding host mode afterward. Do not restart Explorer or change display scaling without a separate explicit user approval because both visibly change system state. Without that approval, verify the dead-child, replaced-parent, `TaskbarCreated` and DPI-mismatch recovery paths through automated tests and report the two disruptive live checks as remaining.

- [x] **Step 6: Update canonical documentation from verified behavior**

Document the final names, dual hosting rule, physical screen-coordinate contract, partial-overlap snap, Explorer/DPI recovery, system theme and actual validation paths. Remove superseded statements saying taskbar windows are never queried. Keep historical investigation rationale but use only the current product name.

- [x] **Step 7: Run final integrated checks**

```powershell
$retiredName = 'CodexHp' + 'Bar'
$oldTracked = @(git grep -n -I $retiredName -- .)
if ($oldTracked.Count -ne 0) { throw 'Old product name remains in tracked content.' }
dotnet test CodexHp.slnx
pwsh -NoProfile -File Scripts/Agent/Verify-Core.ps1
git diff --check
git status --short
```

Expected: no old identifier, every test passes, single-file publish passes, no whitespace errors, and only intended documentation or evidence-index changes remain.

- [ ] **Step 8: Commit final verification and documentation**

```powershell
git commit -m "test: verify CodexHp taskbar hosting"
```

## Plan Self-Review Result

- Spec coverage: naming, physical coordinates, two host modes, snap behavior, drag, Explorer/DPI recovery, system theme, single-file publish and real GUI proof each have an owning task.
- Placeholder scan: all production and test steps specify concrete files, APIs, commands and expected outcomes.
- Type consistency: `OverlayHostMode`, `OverlayHostPlacement`, `TaskbarWindowInfo`, `TaskbarWindowLocator` and `OverlayWindowHost` are introduced before their consumers.
- Scope: installation, publishing to GitHub, icon creation and usage-ticket functionality remain excluded.
