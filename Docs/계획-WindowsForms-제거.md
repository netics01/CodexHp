# CodexHp Windows Forms 제거 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use test-driven-development and execute each checked step in order.

**Goal:** WPF는 유지하면서 Windows Forms 트레이·메뉴·컬러 피커를 Win32 구현으로 교체하고 게시 크기를 줄인다.

**Architecture:** 기존 `TrayIconController`와 Settings ViewModel 계약은 유지한다. 운영체제 연동은 `WindowsTrayIconView`, `TrayIconMessageRouter`, `IColorPicker`/`Win32ColorPicker`로 격리하고 순수 변환과 정적 의존성 계약을 자동 테스트한다.

**Tech Stack:** Windows 11 빌드 22000 이상, .NET 10, WPF, HwndSource, Shell32/User32/Comdlg32 P/Invoke, xUnit, PowerShell 7.

## Global Constraints

- `System.Windows.Forms`와 `UseWindowsForms`를 남기지 않는다.
- UI 텍스트와 사용자 노출 오류는 영어로 유지한다.
- 기존 트레이, 설정, 오버레이와 단일 인스턴스 동작을 변경하지 않는다.
- self-contained single-file과 `100 MiB` 상한을 유지한다.
- 브랜치·워크트리를 만들지 않고 현재 작업 트리에서 구현한다.

---

### Task 1: Windows Forms 부재 계약

**Files:**
- Modify: `tests/CodexHp.App.Tests/Application/PublishConfigurationTests.cs`

**Interfaces:**
- Consumes: App 프로젝트 XML과 `src/CodexHp.App/**/*.cs`.
- Produces: `UseWindowsForms`와 `System.Windows.Forms`가 다시 도입되면 실패하는 회귀 테스트.

- [x] 프로젝트 속성과 소스 참조 부재 테스트를 작성한다.
- [x] 테스트가 현재 `UseWindowsForms=true`와 세 소스 참조 때문에 실패하는지 확인한다.

### Task 2: Win32 트레이와 메뉴

**Files:**
- Modify: `src/CodexHp.App/Presentation/TrayIconController.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/TrayIconControllerTests.cs`

**Interfaces:**
- Consumes: `ITrayIconView`, `TrayMouseButton`, `TrayMenuCommand`, 고정 아이콘 리소스.
- Produces: `TrayIconMessageRouter.RouteMouseButton(uint)`, `TrayIconMessageRouter.RouteMenuCommand(uint)`, Win32 기반 `WindowsTrayIconView`.

- [x] 메시지와 명령 ID 라우팅 실패 테스트를 작성하고 RED를 확인한다.
- [x] `HwndSource`, `Shell_NotifyIconW`, 네이티브 팝업 메뉴를 최소 구현한다.
- [x] 트레이 테스트 전체 GREEN을 확인한다.

### Task 3: Win32 컬러 피커

**Files:**
- Create: `src/CodexHp.App/Presentation/Settings/Win32ColorPicker.cs`
- Modify: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml.cs`
- Create: `tests/CodexHp.App.Tests/Presentation/Settings/Win32ColorPickerTests.cs`
- Modify: `tests/CodexHp.App.Tests/Presentation/SettingsWindowTests.cs`

**Interfaces:**
- Produces: `IColorPicker.PickColor(nint ownerWindow, ColorValue current)`가 `ColorValue?`를 반환한다.
- `Win32ColorPicker.ToColorRef(ColorValue)`와 `FromColorRef(uint)`가 `0x00BBGGRR`를 왕복한다.

- [x] COLORREF 왕복과 선택 확인·취소 테스트를 작성하고 RED를 확인한다.
- [x] `ChooseColorW` 구현과 SettingsWindow 주입 경계를 추가한다.
- [x] 컬러 피커와 옵션 창 테스트 GREEN을 확인한다.

### Task 4: 참조 제거와 통합 검증

**Files:**
- Modify: `src/CodexHp.App/CodexHp.App.csproj`
- Modify: `Docs/요구사항-CodexHp.md`
- Modify: `Docs/Agent-Development-CodexHp.md`

**Interfaces:**
- Produces: WPF 프로필만 사용하는 App 게시와 기록된 크기 결과.

- [x] `<UseWindowsForms>true</UseWindowsForms>`를 제거하고 부재 계약 GREEN을 확인한다.
- [x] `Verify-Core.ps1` 전체 테스트와 게시를 실행한다.
- [x] `Validate-PublishedApp.ps1 -KeepRunning`으로 오버레이와 단일 인스턴스를 확인한다.
- [x] 실제 트레이 좌클릭·우클릭 메뉴와 컬러 피커를 확인한다.
- [x] 이전 `76,479,585` bytes와 새 EXE 크기를 문서에 기록한다.
- [x] `git diff --check`, CRLF와 clean commit을 확인한다.

## 계획 자체 검토

- 설계의 트레이, 메뉴, 컬러 피커, 의존성 제거, 게시와 실제 UI 검증이 모두 작업에 연결되어 있다.
- 인터페이스 이름과 반환 형식은 작업 사이에서 일치한다.
- 미확정 구현 항목이나 자리표시자는 없다.
