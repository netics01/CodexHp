# CodexHp Alt+Tab 및 완화 로그 토큰 그래프 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 옵션 창을 Alt+Tab에 표시하고 토큰 막대 높이를 10K knee 완화 로그로 렌더링한다.

**Architecture:** 옵션 창 HWND에는 명시적인 앱 창 확장 스타일을 적용하되 오버레이 숨김 스타일은 유지한다. 토큰 높이 계산은 `CodexHp.Core.Domain.TokenGraphHeightScaler`에 순수 함수로 분리하고 렌더러는 버킷 값·화면 최대값·가용 높이만 전달한다.

**Tech Stack:** Windows 11, .NET 10, WPF, Win32 HWND styles, xUnit, GDI-backed overlay layout

## Global Constraints

- Windows 11 빌드 22000 이상만 지원한다.
- WPF는 유지하고 Windows Forms를 다시 도입하지 않는다.
- 사용량 오버레이와 숨은 트레이 메시지 창은 Alt+Tab에서 계속 제외한다.
- 토큰 버킷은 15초 × 60개이며 토큰 색상 보간은 변경하지 않는다.
- 완화 로그 knee는 `10,000` 토큰으로 고정하고 새 설정 항목을 추가하지 않는다.
- 현재 `master` 워크트리에서 브랜치·워크트리 분리 없이 작업한다.
- push, 설치와 공개 배포는 수행하지 않는다.

---

### Task 1: 옵션 창 Alt+Tab 표시

**Files:**
- Modify: `tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs`
- Modify: `src/CodexHp.App/Presentation/AltTabWindowStyle.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `NativeMethods.WsExToolWindow`, `NativeMethods.WsExAppWindow`, 실제 WPF 설정 창 HWND.
- Produces: `AltTabWindowStyle.BuildVisibleExtendedStyle(uint)`와 `AltTabWindowStyle.ApplyVisible(nint)`.

- [x] **Step 1: 표시용 확장 스타일과 실제 HWND 실패 테스트를 작성한다.**

```csharp
[Fact]
public void Alt_tab_visible_style_removes_tool_window_and_adds_app_window()
{
    var style = AltTabWindowStyle.BuildVisibleExtendedStyle(NativeMethods.WsExToolWindow);
    Assert.Equal(0u, style & NativeMethods.WsExToolWindow);
    Assert.NotEqual(0u, style & NativeMethods.WsExAppWindow);
}
```

기존 `Visible_settings_window_is_excluded_from_alt_tab`은 `Visible_settings_window_is_included_in_alt_tab`으로 바꾸고 `ShowInTaskbar=true`, `WS_EX_TOOLWINDOW=0`, `WS_EX_APPWINDOW!=0`을 기대한다.

- [x] **Step 2: 테스트가 표시용 API 부재와 현재 숨김 스타일 때문에 실패하는지 확인한다.**

Run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~SettingsWindowTests.Alt_tab_visible_style|FullyQualifiedName~SettingsWindowTests.Visible_settings_window_is_included"
```

Expected: `BuildVisibleExtendedStyle` 부재 또는 기존 HWND 스타일 불일치로 FAIL.

- [x] **Step 3: 표시용 스타일과 설정 창 연결을 최소 구현한다.**

```csharp
internal static uint BuildVisibleExtendedStyle(uint current) =>
    (current | NativeMethods.WsExAppWindow) & ~NativeMethods.WsExToolWindow;
```

`ApplyVisible`은 기존 `Apply`와 같은 오류 처리·`SWP_FRAMECHANGED` 갱신을 사용하고 표시용 계산식을 선택한다. XAML은 `ShowInTaskbar="True"`, `OnSourceInitialized`는 `AltTabWindowStyle.ApplyVisible(...)`을 사용한다.

- [x] **Step 4: 설정 창 집중 테스트를 GREEN으로 확인한다.**

Run: Step 2와 같은 명령.

Expected: 관련 테스트 PASS.

- [x] **Step 5: 안정 마일스톤을 커밋한다.**

```powershell
git add src/CodexHp.App/Presentation/AltTabWindowStyle.cs src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml.cs tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs
git commit -m "feat: show CodexHp settings in Alt+Tab"
```

### Task 2: 완화 로그 토큰 높이

**Files:**
- Create: `src/CodexHp.Core/Domain/TokenGraphHeightScaler.cs`
- Create: `tests/CodexHp.Core.Tests/Domain/TokenGraphHeightScalerTests.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/UsageOverlayRendererTests.cs`
- Modify: `src/CodexHp.App/Presentation/UsageOverlayRenderer.cs`

**Interfaces:**
- Consumes: 토큰 값 `int tokens`, 화면 내 최대값 `int maximumTokens`, 가용 높이 `int maximumHeight`.
- Produces: `TokenGraphHeightScaler.Scale(int tokens, int maximumTokens, int maximumHeight) : int`와 `KneeTokenCount=10_000`.

- [x] **Step 1: 순수 높이 계산 실패 테스트를 작성한다.**

```csharp
[Theory]
[InlineData(1_000, 45_172, 58, 3)]
[InlineData(3_677, 45_172, 58, 10)]
[InlineData(5_000, 45_172, 58, 13)]
[InlineData(10_000, 45_172, 58, 23)]
[InlineData(20_000, 45_172, 58, 37)]
[InlineData(45_172, 45_172, 58, 58)]
public void Scale_uses_a_ten_thousand_token_soft_log_knee(
    int tokens, int maximumTokens, int maximumHeight, int expected) =>
    Assert.Equal(expected, TokenGraphHeightScaler.Scale(tokens, maximumTokens, maximumHeight));
```

별도 theory에서 0과 음수 입력은 `0`, 양의 작은 값은 최소 `1`, 최대 초과는 최대 높이로 제한되는지 검사한다.

- [x] **Step 2: Core 테스트가 타입 부재로 실패하는지 확인한다.**

Run:

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj --filter FullyQualifiedName~TokenGraphHeightScalerTests
```

Expected: `TokenGraphHeightScaler` 부재로 FAIL.

- [x] **Step 3: 완화 로그 순수 함수를 최소 구현한다.**

```csharp
public static int Scale(int tokens, int maximumTokens, int maximumHeight)
{
    if (tokens <= 0 || maximumTokens <= 0 || maximumHeight <= 0) return 0;
    if (tokens >= maximumTokens) return maximumHeight;
    var numerator = Math.Log(1d + (tokens / (double)KneeTokenCount));
    var denominator = Math.Log(1d + (maximumTokens / (double)KneeTokenCount));
    return Math.Max(1, (int)Math.Floor(maximumHeight * numerator / denominator));
}
```

- [x] **Step 4: Core 집중 테스트를 GREEN으로 확인한다.**

Run: Step 2와 같은 명령.

Expected: 관련 테스트 PASS.

- [x] **Step 5: 렌더러 연결 실패 테스트를 작성한다.**

```csharp
[Fact]
public void Token_bars_use_soft_log_height_against_the_visible_maximum()
{
    var bars = UsageOverlayRenderer.CreateLayout(State([3_677, 45_172]), AppSettings.Default, false)
        .Commands.Where(command => command.Role == OverlayElementRole.TokenBar).ToArray();
    Assert.Equal(58, bars[0].Bounds.Height);
    Assert.Equal(10, bars[1].Bounds.Height);
}
```

- [x] **Step 6: 렌더러 테스트가 현재 선형 높이 `4px` 때문에 실패하는지 확인한다.**

Run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~UsageOverlayRendererTests.Token_bars_use_soft_log_height
```

Expected: `Expected: 10`, `Actual: 4`로 FAIL.

- [x] **Step 7: 렌더러를 순수 높이 계산기에 연결한다.**

```csharp
var barHeight = TokenGraphHeightScaler.Scale(value, maximumBucket, chartBottom - chartTop);
```

기존 `TokenColorInterpolator.Interpolate(value, ...)` 호출은 변경하지 않는다.

- [x] **Step 8: Core와 렌더러 집중 테스트를 GREEN으로 확인한다.**

Run: Step 2와 Step 6 명령.

Expected: 관련 테스트 모두 PASS.

- [x] **Step 9: 안정 마일스톤을 커밋한다.**

```powershell
git add src/CodexHp.Core/Domain/TokenGraphHeightScaler.cs tests/CodexHp.Core.Tests/Domain/TokenGraphHeightScalerTests.cs src/CodexHp.App/Presentation/UsageOverlayRenderer.cs tests/CodexHp.App.Tests/Presentation/UsageOverlayRendererTests.cs
git commit -m "feat: use soft-log token graph heights"
```

### Task 3: 정본 문서와 통합 검증

**Files:**
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`
- Modify: `Docs/계획-CodexHp-AltTab-완화로그그래프.md`

**Interfaces:**
- Consumes: Task 1과 Task 2의 최종 동작과 테스트 결과.
- Produces: 현재 동작, 계산식, 실제 게시본 증거를 담은 정본 문서.

- [x] **Step 1: 요구사항과 개발 문서의 기존 옵션 창 Alt+Tab 제외 문구를 표시 규칙으로 바꾼다.**

오버레이 제외 규칙은 유지하고 옵션 창에는 `WS_EX_APPWINDOW=설정`, `WS_EX_TOOLWINDOW=제거`를 기록한다. 토큰 높이의 10K knee 식과 색상 선형 보간 유지도 기록한다.

- [x] **Step 2: 전체 테스트와 게시를 실행한다.**

Run:

```powershell
pwsh -NoProfile -File Scripts/Agent/Verify-Core.ps1
```

Expected: 모든 Core/App 테스트 PASS, 경고 0, 오류 0, 단일 `CodexHp.exe` 게시와 100 MiB 상한 PASS.

- [x] **Step 3: 게시본 오버레이와 단일 인스턴스를 검증한다.**

Run:

```powershell
pwsh -NoProfile -File tests/Windows/Validate-PublishedApp.ps1 -ExpectedOverlayBounds @(0,2078,288,68) -KeepRunning
```

Expected: 오버레이 HWND·물리 경계·실제 픽셀·단일 인스턴스 PASS, 최신 게시본은 실행 상태로 유지.

- [x] **Step 4: 실제 옵션 창 HWND 스타일과 그래프 막대 픽셀을 확인한다.**

트레이 좌클릭 또는 오버레이 더블클릭으로 옵션 창을 열고 Win32 스타일에서 `WS_EX_APPWINDOW!=0`, `WS_EX_TOOLWINDOW=0`을 확인한다. 실제 현재 버킷으로 렌더링된 레이아웃에서 최대 막대와 평상시 막대가 순수 계산 결과와 일치하는지 검사한다.

- [x] **Step 5: 문서에 최종 테스트 수, 게시본 경로, HWND 스타일과 그래프 검증 결과를 기록한다.**

- [x] **Step 6: diff, CRLF와 임시 산출물 부재를 확인한다.**

Run:

```powershell
git diff --check
git ls-files --eol -- CodexHp
git status --short
```

Expected: 변경 텍스트 `w/crlf`, 의도한 파일만 변경, 임시 진단 소스 없음.

- [x] **Step 7: 정본 문서를 커밋한다.**

```powershell
git add Docs/요구사항-CodexHp.md Docs/Agent-Development-CodexHp.md Docs/계획-CodexHp-AltTab-완화로그그래프.md
git commit -m "docs: record CodexHp Alt+Tab and soft-log behavior"
```

### Task 4: 옵션 창 높이 15% 축소

**Files:**
- Modify: `tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`
- Modify: `Docs/계획-CodexHp-AltTab-완화로그그래프.md`

**Interfaces:**
- Consumes: 기존 논리 높이 `590`, 고정 폭 `650`, 동일 높이 좌우 패널 레이아웃.
- Produces: `Height=502`, `MinHeight=502`인 정수 높이의 고정 옵션 창.

- [x] **Step 1: 15% 축소 높이 실패 테스트를 작성한다.**

기존 PicPick 레이아웃 계약 테스트에서 다음 값을 기대한다.

```csharp
Assert.Equal(502, window.Height);
Assert.Equal(502, window.MinHeight);
```

- [x] **Step 2: 테스트가 현재 높이 `590` 때문에 실패하는지 확인한다.**

Run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~SettingsWindowTests.Picpick_reference_contract_uses_compact_settings_layout
```

Expected: `Expected: 502`, `Actual: 590`으로 FAIL.

- [x] **Step 3: XAML 높이와 최소 높이를 최소 변경한다.**

```xml
Height="502"
MinHeight="502"
```

폭과 내부 레이아웃 값은 변경하지 않는다.

- [x] **Step 4: 설정 창 전체 테스트를 GREEN으로 확인한다.**

Run:

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~SettingsWindowTests
```

Expected: 모든 설정 창 테스트 PASS.

- [x] **Step 5: 전체 테스트·게시와 실제 200% DPI 창 높이를 확인한다.**

`Verify-Core.ps1`과 `Validate-PublishedApp.ps1 -KeepRunning`을 다시 실행한다. 게시 옵션 창의 물리 크기는 폭 `1300px`, 높이 `1004px`여야 하며 모든 탭의 본문과 하단 `OK`·`Cancel` 버튼이 잘리지 않아야 한다.

- [x] **Step 6: 정본 문서, diff와 CRLF를 확인하고 커밋한다.**

```powershell
git add src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs Docs/요구사항-CodexHp.md Docs/Agent-Development-CodexHp.md Docs/설계-CodexHp-AltTab-완화로그그래프.md Docs/계획-CodexHp-AltTab-완화로그그래프.md
git commit -m "style: reduce CodexHp settings height"
```

## 계획 자체 검토

- 승인된 Alt+Tab 및 C안 요구사항은 각각 Task 1과 Task 2에 대응한다.
- 실제 관측값, 계산식, 타입과 메서드 이름은 설계 문서와 모든 Task에서 일치한다.
- 미확정 값이나 후속 구현 자리표시자와 범위 밖 설정 옵션이 없다.
- 실제 게시본 검증과 정본 문서 갱신은 Task 3에 포함되어 있다.
- 추가 요청의 `590 × 0.85 = 501.5` 계산과 정수 반올림 `502`, 레이아웃 회귀와 실제 200% DPI 게시 검증은 Task 4에 포함되어 있다.
