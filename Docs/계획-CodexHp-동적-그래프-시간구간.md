# CodexHp 동적 그래프 시간 구간 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Usage Overlay의 전체 그래프 폭만큼 실제 15초 토큰 기록을 수집하고, Appearance 탭에 계산된 표시 시간과 48px 숫자 입력 칸을 제공한다.

**Architecture:** `TokenGraphViewport`가 그래프 경계, 막대 슬롯, 표시 가능한 버킷 수와 시간을 순수 계산한다. 수집기·렌더러·설정 ViewModel이 이 계산을 공유하여 표시 범위와 데이터 범위가 어긋나지 않게 한다.

**Tech Stack:** .NET 10, C#, WPF, Win32/GDI, xUnit, PowerShell 7, ScreenProof

## Global Constraints

- Windows 11 이상만 지원한다.
- 버킷 간격은 15초, 시간 구분선은 5분 간격을 유지한다.
- 기본 Appearance는 89개 버킷, 22분 15초를 표시한다.
- 안내 문구는 영문 `Visible token history: N min N sec` 형식이다.
- 숫자 입력 열은 80px에서 48px로 줄인다.
- 완화 로그 높이와 토큰 색상 보간은 변경하지 않는다.
- 현재 master 작업 공간에서 직접 진행하고 push·설치·공개 배포는 하지 않는다.

---

### Task 1: 공통 그래프 뷰포트 계산

**Files:**
- Create: `src/CodexHp.Core/Domain/TokenGraphViewport.cs`
- Create: `tests/CodexHp.Core.Tests/Domain/TokenGraphViewportTests.cs`

**Interfaces:**
- Consumes: `AppearanceSettings`
- Produces: `TokenGraphViewport.BucketSeconds`, `ChartLeft`, `ChartRight`, `CalculateVisibleBucketCount`, `CalculateVisibleDuration`

- [x] **Step 1: 실패 테스트 작성**

기본값에서 경계 `104..282`, 버킷 `89`, 시간 `00:22:15`를 기대한다. 폭·막대·간격을 변경한 값과 막대가 들어가지 않는 값도 추가한다.

- [x] **Step 2: RED 확인**

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj --filter FullyQualifiedName~TokenGraphViewportTests
```

예상: `TokenGraphViewport` 형식이 없어서 컴파일 실패.

- [x] **Step 3: 최소 구현**

```csharp
public static int CalculateVisibleBucketCount(AppearanceSettings appearance)
{
    var chartLeft = ChartLeft(appearance);
    var firstBarLeft = ChartRight(appearance) - appearance.GraphBarWidth;
    if (firstBarLeft < chartLeft)
    {
        return 0;
    }

    var slotWidth = appearance.GraphBarWidth + appearance.GraphBarGap;
    return ((firstBarLeft - chartLeft) / slotWidth) + 1;
}
```

- [x] **Step 4: GREEN 확인**

같은 필터 테스트가 모두 통과해야 한다.

- [x] **Step 5: 안정 커밋**

```powershell
git add -- src/CodexHp.Core/Domain/TokenGraphViewport.cs tests/CodexHp.Core.Tests/Domain/TokenGraphViewportTests.cs
git commit -m "feat: calculate CodexHp graph history capacity"
```

### Task 2: 실제 수집 범위를 그래프 용량과 연결

**Files:**
- Modify: `src/CodexHp.App/Application/ApplicationCoordinator.cs`
- Modify: `tests/CodexHp.App.Tests/Application/ApplicationCoordinatorTests.cs`
- Modify: `src/CodexHp.App/Presentation/UsageOverlayRenderer.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/UsageOverlayRendererTests.cs`

**Interfaces:**
- Consumes: Task 1의 `TokenGraphViewport`
- Produces: 매 폴링 시 현재 Appearance에 맞는 실제 토큰 버킷 배열

- [x] **Step 1: 실패 테스트 작성**

`ApplicationCoordinator`가 기본값에서 `bucketSeconds=15`, `maxBuckets=89`를 전달하고 사용자 Appearance에서는 다시 계산된 값을 전달하는 테스트를 작성한다. 렌더러의 그래프 경계가 공통 계산 결과와 일치하는 테스트도 작성한다.

- [x] **Step 2: RED 확인**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~ApplicationCoordinatorTests|FullyQualifiedName~UsageOverlayRendererTests"
```

예상: 현재 고정값 `60`이 전달되어 `Expected: 89, Actual: 60`으로 실패.

- [x] **Step 3: 최소 구현**

`PollTokenActivityOnceAsync`에서 `readSettings().Appearance`를 읽고 공통 계산 결과를 사용한다. 스캐너 계약을 위해 요청 버킷은 최소 1개로 제한한다. 렌더러의 왼쪽·오른쪽 경계도 공통 계산을 호출한다.

- [x] **Step 4: GREEN 확인**

Task 2 필터 테스트가 모두 통과해야 한다.

- [x] **Step 5: 안정 커밋**

```powershell
git add -- src/CodexHp.App/Application/ApplicationCoordinator.cs tests/CodexHp.App.Tests/Application/ApplicationCoordinatorTests.cs src/CodexHp.App/Presentation/UsageOverlayRenderer.cs tests/CodexHp.App.Tests/Presentation/UsageOverlayRendererTests.cs
git commit -m "feat: fill CodexHp graph with actual history"
```

### Task 3: Appearance 표시 시간과 입력 폭

**Files:**
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindowViewModel.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- Modify: `tests/CodexHp.App.Tests/Presentation/SettingsWindowViewModelTests.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs`

**Interfaces:**
- Consumes: Task 1의 `CalculateVisibleDuration`
- Produces: `SettingsWindowViewModel.VisibleTokenHistoryText`

- [x] **Step 1: 실패 테스트 작성**

기본 문구가 `Visible token history: 22 min 15 sec`인지, Appearance 변경과 기본값 복원 후 갱신되는지 검증한다. XAML 테스트는 마지막 항목의 바인딩과 숫자 입력 열 `48`을 검증한다.

- [x] **Step 2: RED 확인**

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter "FullyQualifiedName~SettingsWindowViewModelTests|FullyQualifiedName~SettingsWindowTests"
```

예상: 속성과 UI 요소가 없고 숫자 입력 열이 `80`이어서 실패.

- [x] **Step 3: 최소 구현**

```csharp
public string VisibleTokenHistoryText
{
    get
    {
        var duration = TokenGraphViewport.CalculateVisibleDuration(this.Working.Appearance);
        return $"Visible token history: {(int)duration.TotalMinutes} min {duration.Seconds} sec";
    }
}
```

Appearance 내부 그리드에 마지막 안내 TextBlock을 추가하고 입력 열 폭을 `48`로 변경한다. 기존 `OnPropertyChanged(string.Empty)`가 설정 변경과 초기화 시 문구도 갱신한다.

- [x] **Step 4: GREEN 확인**

Task 3 필터 테스트가 모두 통과해야 한다.

- [x] **Step 5: 안정 커밋**

```powershell
git add -- src/CodexHp.App/Presentation/Settings/SettingsWindowViewModel.cs src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml tests/CodexHp.App.Tests/Presentation/SettingsWindowViewModelTests.cs tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs
git commit -m "feat: show CodexHp visible graph history"
```

### Task 4: 통합 검증과 문서화

**Files:**
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`
- Modify: `Docs/계획-CodexHp-동적-그래프-시간구간.md`

- [x] **Step 1: 전체 테스트와 게시**

```powershell
pwsh -NoProfile -File Scripts/Agent/Verify-Core.ps1
```

예상: Core와 App 테스트 전체 통과, 경고·오류 0, 단일 `CodexHp.exe` 게시.

- [x] **Step 2: Windows 11 실행 검증**

```powershell
pwsh -NoProfile -File tests/Windows/Validate-PublishedApp.ps1 -ExpectedOverlayBounds @(0,2078,288,68) -KeepRunning
```

예상: 실제 픽셀, 단일 인스턴스, 오버레이 HWND와 실행 유지 검증 통과.

- [x] **Step 3: Appearance 화면 검증**

게시본의 Appearance 탭을 열고 `Visible token history: 22 min 15 sec`, 48px 입력 열, 잘리지 않는 하단 버튼을 ScreenProof로 캡처한다.

- [x] **Step 4: 정본 문서 갱신**

요구사항과 개발 문서에 동적 시간 범위 계산식, UI 문구, 입력 폭, 테스트 수와 ScreenProof 위치를 기록한다.

- [x] **Step 5: 최종 안정 커밋**

```powershell
git add -- Docs/요구사항-CodexHp.md Docs/Agent-Development-CodexHp.md Docs/계획-CodexHp-동적-그래프-시간구간.md
git commit -m "docs: record dynamic CodexHp graph history"
```
