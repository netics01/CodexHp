# CodexHp 입력·드래그·UI 영문화·아이콘 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task in the current workspace. Do not create a worktree, branch, or subagent.

**Goal:** 사용량 오버레이 더블클릭과 위치 변경 왕복을 안정화하고, CodexHp 사용자 UI를 영어로 통일하며, 공식 Codex 문양과 HP 게이지를 결합한 고정 제품 아이콘을 탑재한다.

**Architecture:** WPF 입력 계층이 계산한 클릭 횟수를 네이티브 호스트와 분리해 옵션 열기 이벤트로 전달한다. 모든 물리 배치는 공통 호스트 전환 함수로 모으고, 이미 배치된 표면이 팝업에서 작업표시줄 자식으로 돌아올 때 Dispatcher에서 표면을 한 번만 재생성한다. 사용자 문구는 기존 XAML과 작은 상수 표면에서 직접 영어로 관리하며, 아이콘은 설치된 공식 패키지 자산으로부터 빌드 타임에 한 번 생성한 고정 ICO/PNG를 실행 파일과 트레이에 포함한다.

**Tech Stack:** Windows 11, .NET 10, C# 14, WPF, Win32/User32, GDI, Windows Forms NotifyIcon, System.Drawing, xUnit, PowerShell 7, ScreenProof

**Execution Status (2026-08-15):** Task 1~4와 Task 5의 구현·문서·게시 검증을 완료했다. 후속 사용자 요청으로 실제 Windows 드래그 인수테스트 4개와 공식 문양 시각 크기 회귀 테스트를 추가했으며, 결과는 `Acceptance-Test-Report-CodexHp-드래그.md`에 기록했다.

## Global Constraints

- 지원 플랫폼과 검증 대상은 Windows 11뿐이다.
- 사용량 오버레이 크기와 위치는 전체 화면 물리 픽셀 좌표다.
- 작업표시줄 내부는 `TaskbarChild`, 외부는 `DesktopPopup`으로 유지한다.
- `OK`는 위치를 저장하고 `Cancel`, `Esc`, `X`는 편집 전 위치로 복구한다.
- CodexHp가 소유한 사용자 노출 UI와 오류 메시지는 영어다.
- 개발자 로그, 내부 예외, 테스트 진단 문구와 문서는 영문화 대상이 아니다.
- Windows 시스템 대화상자는 Windows 표시 언어를 따른다.
- 실행 중 공식 앱에서 아이콘을 추출하거나 합성하지 않는다.
- 설치 프로그램, 공개 GitHub 게시와 사용량 초기화 티켓 기능은 범위 밖이다.
- 사용자 지시에 따라 브랜치, 워크트리와 다른 에이전트를 사용하지 않는다.
- 되돌리기 쉬운 구현 세부 결정에는 반복 승인을 요청하지 않고, 차단되거나 되돌리기 비싼 결정만 사용자에게 묻는다.

---

### Task 1: WPF 클릭 횟수로 옵션 열기

**Files:**
- Modify: `src/CodexHp.App/Presentation/WpfOverlaySurface.cs`
- Modify: `src/CodexHp.App/Presentation/UsageOverlayWindow.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/UsageOverlayHostingIntegrationTests.cs`

**Interfaces:**
- Consumes: WPF `PreviewMouseLeftButtonDown`, `MouseButtonEventArgs.ClickCount`
- Produces: `WpfOverlaySurface.OpenSettingsRequested`, `WpfOverlaySurface.ProcessLeftButtonDown(int clickCount)`, `UsageOverlayWindow.OpenSettingsRequested`

- [ ] **Step 1: Write the failing click-routing test**

Add this STA test before changing production code:

```csharp
[Fact]
public void WPF_surface_requests_settings_only_for_a_left_double_click() =>
    StaTest.Run(() =>
{
    using var surface = new WpfOverlaySurface(32, 16, NoOpHook);
    var requestCount = 0;
    surface.OpenSettingsRequested += (_, _) => requestCount++;

    surface.ProcessLeftButtonDown(1);
    surface.ProcessLeftButtonDown(2);

    Assert.Equal(1, requestCount);
});
```

- [ ] **Step 2: Run the focused test and confirm RED**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~WPF_surface_requests_settings
```

Expected: compilation fails because the surface event and click-processing method do not exist.

- [ ] **Step 3: Implement WPF click routing and remove raw double-click reliance**

Add to `WpfOverlaySurface`:

```csharp
internal event EventHandler? OpenSettingsRequested;

internal void ProcessLeftButtonDown(int clickCount)
{
    if (clickCount == 2)
    {
        this.OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }
}

private void OnPreviewMouseLeftButtonDown(
    object sender,
    System.Windows.Input.MouseButtonEventArgs eventArgs) =>
    this.ProcessLeftButtonDown(eventArgs.ClickCount);
```

Subscribe the WPF window to `PreviewMouseLeftButtonDown` in the constructor and unsubscribe during `Dispose`. In `UsageOverlayWindow.CreateWpfSurface`, subscribe the new surface event and relay it through `UsageOverlayWindow.OpenSettingsRequested`. Remove the `WM_LBUTTONDBLCLK` switch case so one gesture cannot open twice if a future native class style changes.

- [ ] **Step 4: Run focused and App tests**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~UsageOverlayHostingIntegration|FullyQualifiedName~SettingsWindow"
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj
```

Expected: the focused test and all App tests pass.

- [ ] **Step 5: Commit the input fix**

```powershell
git add src/CodexHp.App/Presentation/WpfOverlaySurface.cs src/CodexHp.App/Presentation/UsageOverlayWindow.cs tests/CodexHp.App.Tests/Presentation/UsageOverlayHostingIntegrationTests.cs
git commit -m "fix: open CodexHp settings on double click"
```

---

### Task 2: Make every popup-to-taskbar return recreate the WPF surface

**Files:**
- Modify: `src/CodexHp.App/Presentation/UsageOverlayWindow.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/UsageOverlayHostingIntegrationTests.cs`

**Interfaces:**
- Consumes: `OverlayWindowHost.Apply`, `OverlayHostMode`, last physical bounds and monitor
- Produces: `ApplyHostedPlacement(PhysicalRect, string?)`, `ScheduleNativeWindowRecreation()`

- [ ] **Step 1: Replace the manual round-trip test with a failing UsageOverlayWindow pixel test**

Create a visible state with 100% mana and HP gauges. Apply a taskbar placement, show the window, move it to a `(600, 600)` popup placement, then return it through `SetPlacement` to the original taskbar placement. Record the first HWND and assert after one Dispatcher pump:

```csharp
Assert.NotEqual(firstWindowHandle, window.WindowHandle);
Assert.Equal(taskbar.Value.WindowHandle, NativeMethods.GetParent(window.WindowHandle));
AssertOverlayPixelEventually(childLeft + 10, childTop + 10, 0x00FF8E3Au);
```

Also extend `SetPlacement_routes_a_taskbar_position_through_child_hosting` to record the initial HWND and assert the first taskbar placement does not replace it.

- [ ] **Step 2: Run the round-trip test and confirm RED**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~WPF_pixels_survive|FullyQualifiedName~SetPlacement_routes"
```

Expected: the direct `SetPlacement` popup-to-child return keeps the same HWND or loses its expected overlay pixel.

- [ ] **Step 3: Centralize hosted placement and schedule one recreation**

Add `hasHostedSurface` and `recreationPending` fields. Route `SetPlacement` and the post-drag placement through:

```csharp
private PhysicalRect ApplyHostedPlacement(PhysicalRect desiredBounds, string? monitorId)
{
    var previousMode = this.windowHost.Mode;
    var hadHostedSurface = this.hasHostedSurface;
    var hostedBounds = this.windowHost.Apply(this.WindowHandle, desiredBounds, monitorId);
    this.hasHostedSurface = true;
    this.lastHostedBounds = hostedBounds;
    this.lastMonitorId = monitorId;
    this.SubmitLayeredSurface();

    if (hadHostedSurface
        && RequiresSurfaceRecreationAfterHostTransition(previousMode, this.windowHost.Mode))
    {
        this.ScheduleNativeWindowRecreation();
    }

    return hostedBounds;
}
```

Use this non-reentrant scheduler for both host transitions and `TaskbarCreated`:

```csharp
private void ScheduleNativeWindowRecreation()
{
    if (this.isClosed || this.recreationPending)
    {
        return;
    }

    this.recreationPending = true;
    _ = this.dispatcher.BeginInvoke(() =>
    {
        this.recreationPending = false;
        this.TryRecreateNativeWindow();
    });
}
```

Keep the direct host application inside `RecreateNativeWindow` so the replacement surface cannot recursively schedule itself.

- [ ] **Step 4: Run focused, repeated and complete tests**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~UsageOverlayHostingIntegration
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~UsageOverlayHostingIntegration
dotnet test CodexHp.slnx
```

Expected: both focused runs and the complete solution pass with zero warnings and errors.

- [ ] **Step 5: Commit the host-transition fix**

```powershell
git add src/CodexHp.App/Presentation/UsageOverlayWindow.cs tests/CodexHp.App.Tests/Presentation/UsageOverlayHostingIntegrationTests.cs
git commit -m "fix: restore CodexHp pixels after placement cancel"
```

---

### Task 3: Change all CodexHp-owned user UI to English

**Files:**
- Create: `src/CodexHp.App/Presentation/UserInterfaceText.cs`
- Modify: `src/CodexHp.App/App.xaml.cs`
- Modify: `src/CodexHp.App/Presentation/TrayIconController.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindowViewModel.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/SettingsWindowViewModelTests.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/TrayIconControllerTests.cs`

**Interfaces:**
- Produces: English options window, tray menu, and user-facing startup/save errors

- [ ] **Step 1: Change tests to require English UI and confirm RED**

Require group titles `General`, `Colors`, `Appearance`, `Overlay Position` and tray items `Options`, `Exit`. Add a `SettingsWindowTests` STA test that walks the logical tree, collects `TextBlock.Text` and string `ContentControl.Content`, and asserts the title is `CodexHp Settings` and no collected value matches `[가-힣]`. Add assertions for these error prefixes:

```csharp
Assert.Equal("CodexHp could not start.", UserInterfaceText.StartupFailure);
Assert.Equal("Settings could not be saved.", UserInterfaceText.SettingsSaveFailure);
```

Run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~SettingsWindow|FullyQualifiedName~TrayIconController"
```

Expected: English expectations fail and `UserInterfaceText` is missing.

- [ ] **Step 2: Add the user-facing error text surface**

Create:

```csharp
namespace CodexHp.App.Presentation;

internal static class UserInterfaceText
{
    internal const string StartupFailure = "CodexHp could not start.";
    internal const string SettingsSaveFailure = "Settings could not be saved.";
}
```

Compose each MessageBox body as `$"{UserInterfaceText...}\n\n{exception.Message}"`. Do not change developer log or internal exception messages.

- [ ] **Step 3: Translate program-owned options and tray text**

Use these exact labels:

```text
CodexHp Settings
General / Colors / Appearance / Overlay Position
Run CodexHp when Windows starts
Show the usage overlay only while the ChatGPT desktop app is running
General settings take effect after you select OK. The default is Always show.
Mana Bar (upper gauge) / HP Bar (lower gauge) / Refresh Gauge
Service Issue Stripe / Unknown Service Status Stripe
Low Token Graph Value / High Token Graph Value
Choose
Usage Overlay Width / Usage Overlay Height / Gauge Area Width
Graph Bar Width / Graph Bar Gap / Service Status Stripe Width
Physical px
Drag the usage overlay to the desired location.
OK / Cancel
Options / Exit
```

- [ ] **Step 4: Verify tests and source string inventory**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~SettingsWindow|FullyQualifiedName~TrayIconController"
rg -n --glob '!bin/**' --glob '!obj/**' "[가-힣]" src/CodexHp.App
dotnet test CodexHp.slnx
```

Expected: focused and full tests pass; the source scan has no CodexHp-owned Korean UI strings. Developer messages may be reported separately if any are intentionally retained.

- [ ] **Step 5: Commit UI English text**

```powershell
git add src/CodexHp.App tests/CodexHp.App.Tests/Presentation
git commit -m "feat: use English text across CodexHp UI"
```

---

### Task 4: Extract and embed the fixed CodexHp HP-gauge icon

**Files:**
- Create: `Scripts/Assets/New-CodexHpIcon.ps1`
- Create: `src/CodexHp.App/Assets/CodexHp.png`
- Create: `src/CodexHp.App/Assets/CodexHp.ico`
- Modify: `src/CodexHp.App/CodexHp.App.csproj`
- Modify: `src/CodexHp.App/Presentation/TrayIconController.cs`
- Test: `tests/CodexHp.App.Tests/Presentation/TrayIconControllerTests.cs`

**Interfaces:**
- Consumes: installed `OpenAI.Codex` package `assets/Square44x44Logo.targetsize-256_altform-unplated.png`
- Produces: fixed 256px PNG, multi-size ICO, assembly icon and managed tray icon resource

- [ ] **Step 1: Write failing product-icon tests**

Change the tray asset expectation to `TrayIconAsset.CodexHpGauge`. Add a test that opens the manifest resource `CodexHp.App.Assets.CodexHp.ico`, constructs `System.Drawing.Icon`, and asserts its dimensions are at least 16x16. Before changing the project, the enum/resource assertions must fail.

- [ ] **Step 2: Add and run the deterministic icon generator**

The PowerShell script must:

1. Resolve the installed `OpenAI.Codex` package with `Get-AppxPackage`.
2. load the exact 256px official unplated PNG;
3. draw a transparent 256px canvas with a dark rounded plate;
4. draw the official mark unchanged in the upper region;
5. draw a white-bordered `#DC4856` HP gauge filled to 84% at the bottom;
6. write `CodexHp.png`;
7. create PNG frames at `16, 20, 24, 32, 40, 48, 64, 128, 256` and assemble them into one ICO.

Run:

```powershell
pwsh -NoProfile -File Scripts/Assets/New-CodexHpIcon.ps1
```

Expected: both binary assets exist and the script records the source package name/version in its console output. Binary files are generated directly and are never modified with `apply_patch`.

- [ ] **Step 3: Embed the icon for the executable and tray**

Add to the project:

```xml
<ApplicationIcon>Assets\CodexHp.ico</ApplicationIcon>
<EmbeddedResource Include="Assets\CodexHp.ico" LogicalName="CodexHp.App.Assets.CodexHp.ico" />
```

Load that resource once in `WindowsTrayIconView`, assign it to `NotifyIcon.Icon`, and dispose the owned `System.Drawing.Icon` after disposing the notify icon. Replace `TemporarySystemApplication` with `CodexHpGauge`.

- [ ] **Step 4: Inspect the generated PNG and run icon tests**

Open `CodexHp.png` with the image viewer, then run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~TrayIconController
dotnet build CodexHp.slnx
```

Expected: the official white Codex mark remains recognizable, the bottom red HP gauge remains visible at tray sizes, and tests/build pass.

- [ ] **Step 5: Commit the fixed icon**

```powershell
git add Scripts/Assets/New-CodexHpIcon.ps1 src/CodexHp.App/Assets src/CodexHp.App/CodexHp.App.csproj src/CodexHp.App/Presentation/TrayIconController.cs tests/CodexHp.App.Tests/Presentation/TrayIconControllerTests.cs
git commit -m "feat: add CodexHp HP gauge icon"
```

---

### Task 5: Update canonical docs, publish, and verify the complete Windows UI

**Files:**
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`
- Modify: `Docs/설계-입력-드래그-UI영문화.md`
- Modify: `Docs/계획-입력-드래그-UI영문화.md`
- Verify: `tests/Windows/Validate-PublishedApp.ps1`
- Verify: `Scripts/Agent/Verify-Core.ps1`

**Interfaces:**
- Consumes: final app and fixed icon
- Produces: single-file `out/win-x64/CodexHp.exe`, ScreenProof evidence and current canonical docs

- [ ] **Step 1: Update durable requirements and development guidance**

Record the implemented double-click path, common popup-to-child recreation, English user UI, fixed icon source/generation path and the user's low-cost autonomous-development preference. Mark runtime icon extraction and the temporary system icon as superseded.

- [ ] **Step 2: Run complete automated verification**

```powershell
dotnet test CodexHp.slnx
pwsh -NoProfile -File Scripts/Agent/Verify-Core.ps1
git diff --check
```

Expected: every test passes, build/publish report zero warnings/errors, and the output directory contains only `CodexHp.exe`.

- [ ] **Step 3: Restart only the published CodexHp process and validate pixels**

Verify the running `CodexHp` process path, stop only that process, run the published executable, then collect:

```powershell
screenproof capture --screen primary --output-dir out/screenproof/input-drag-english-icon
```

Verify the taskbar child parent, physical bounds, window styles and at least one expected non-taskbar pixel. Open options through a double-click, verify the English window, exercise popup placement followed by `X` cancellation, and verify the restored taskbar pixels. Confirm the tray uses the fixed icon.

- [ ] **Step 4: Commit final documentation and verification updates**

```powershell
git add Docs tests/Windows Scripts/Agent
git commit -m "test: verify CodexHp input and drag workflow"
```

## Plan Self-Review Result

- Spec coverage: double-click, popup/taskbar cancellation, drag round trip, English UI, user errors, fixed icon, executable/tray embedding and Windows 11 proof each have an owning task. 후속 AT-DRAG-001~004는 별도 인수 기준·계획·결과 문서와 상시 Windows GUI 테스트로 연결했다.
- Placeholder scan: every task names concrete files, APIs, commands, expected failures and success conditions.
- Type consistency: surface input events precede their `UsageOverlayWindow` consumer; host helpers retain existing physical-coordinate contracts; `UserInterfaceText` and `CodexHpGauge` are introduced before their tests consume them.
- Scope: authentication, usage-ticket functionality, installer work, public distribution and runtime OpenAI asset extraction remain excluded.
