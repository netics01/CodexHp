# CodexHp 핵심 기능 구현 계획

> 상태: 사용자 승인 후 구현 진행 중 — Task 1~15 완료, Task 16 착수

## 목표

Windows 11에서 `CodexHp.exe` 하나를 실행하면 트레이 아이콘과 불투명 최상위 사용량 오버레이가 나타나고, 이미 로그인된 ChatGPT 데스크톱 앱의 Codex 인증 캐시를 이용해 5시간·주간 한도와 로컬 토큰 활동을 표시하는 제품을 구현한다.

## 구현 기준

- 설계 정본: `Agent-Development-CodexHp.md`
- 요구사항 정본: `요구사항-CodexHp.md`
- 기술: .NET 10, WPF, Windows Forms `NotifyIcon`·`ColorDialog`, 필요한 Win32 API
- 구조: `CodexHp.App` + `CodexHp.Core` + 두 테스트 프로젝트, 최종 단일 프로세스
- 방법: 모든 동작 변경은 실패하는 테스트를 먼저 확인한 뒤 최소 구현한다.
- 작업 위치: 현재 `master` 작업트리에서 직접 진행하며 별도 branch와 worktree를 만들지 않는다.
- 제외: 초기화 티켓, 로그인·OAuth, App Server, OpenCode, 설치 프로그램, 최종 아이콘, 공개 배포

## 공통 검증 명령

저장소 루트 `D:\Github\CodexHp`에서 실행한다.

```powershell
dotnet restore CodexHp.slnx
dotnet build CodexHp.slnx --no-restore
dotnet test CodexHp.slnx --no-build
git diff --check
git status --short
```

개별 작업에서는 관련 테스트만 먼저 실행하고, 마일스톤 커밋 전에는 위 전체 검증을 다시 실행한다. 실제 사용자 인증값과 응답은 테스트 파일, 스냅샷, 로그 또는 커밋에 포함하지 않는다.

## Task 1. .NET 10 솔루션 골격과 Windows 앱 시작점

**파일**

- 생성: `CodexHp.slnx`
- 생성: `src/CodexHp.Core/CodexHp.Core.csproj`
- 생성: `src/CodexHp.Core/AssemblyMarker.cs`
- 생성: `src/CodexHp.App/CodexHp.App.csproj`
- 생성: `src/CodexHp.App/App.xaml`
- 생성: `src/CodexHp.App/App.xaml.cs`
- 생성: `src/CodexHp.App/app.manifest`
- 생성: `tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj`
- 생성: `tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj`
- 생성: `tests/CodexHp.Core.Tests/ProjectSmokeTests.cs`

**순서**

1. 네 프로젝트와 `.slnx` 참조만 만들고 `ProjectSmokeTests`에서 Core 어셈블리의 기준 형식을 참조한다.
2. 아직 기준 형식이 없으므로 다음 명령이 컴파일 실패하는지 확인한다.

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj
```

3. Core에 최소 `AssemblyMarker` 형식을 추가해 테스트 프로젝트 참조가 정상인지 확인한다.
4. App 프로젝트는 `net10.0-windows`, `WinExe`, `UseWPF`, `UseWindowsForms`, nullable과 implicit usings를 활성화한다.
5. 매니페스트에 Per-Monitor DPI Aware V2를 선언한다.
6. 두 테스트 프로젝트는 저장소에서 검증된 xUnit 패키지 버전을 사용하고 각각 Core와 App을 참조한다.
7. `dotnet restore`, `dotnet build`, `dotnet test`를 실행한다.
8. 커밋한다.

```text
feat: scaffold CodexHp application
```

## Task 2. 설정 모델, 기본값과 값 검증

**파일**

- 생성: `src/CodexHp.Core/Settings/AppSettings.cs`
- 생성: `src/CodexHp.Core/Settings/ColorValue.cs`
- 생성: `src/CodexHp.Core/Settings/AppearanceSettings.cs`
- 생성: `src/CodexHp.Core/Settings/OverlayLocationSettings.cs`
- 생성: `src/CodexHp.Core/Settings/SettingsValidator.cs`
- 생성: `tests/CodexHp.Core.Tests/Settings/SettingsDefaultsTests.cs`
- 생성: `tests/CodexHp.Core.Tests/Settings/SettingsValidatorTests.cs`

**테스트 우선 사례**

- 기본 표시 조건은 항상 표시다.
- 시작 프로그램 설정 기본값은 사용이다.
- 일곱 색상과 여섯 모양 값이 요구사항 기본값과 일치한다.
- 색상은 `#RRGGBB`만 허용한다.
- 모양 값 하나가 범위를 벗어나면 나머지는 보존하고 해당 항목만 기본값으로 복구한다.
- 모니터 식별자와 작업 영역 기준 논리 X/Y를 직렬화할 수 있다.

**순서**

1. 위 테스트를 작성한다.
2. 다음 명령에서 형식 부재로 실패하는지 확인한다.

```powershell
dotnet test tests/CodexHp.Core.Tests/CodexHp.Core.Tests.csproj --filter FullyQualifiedName~Settings
```

3. 불변 record 기반 설정 모델과 기본값을 구현한다.
4. 항목별 보정 결과와 보정 여부를 반환하는 `SettingsValidator`를 구현한다.
5. 관련 테스트와 전체 Core 테스트를 통과시킨다.

## Task 3. 게이지 계산, 색상 보간과 사용량 오버레이 상태 환원

**파일**

- 생성: `src/CodexHp.Core/Domain/UsageSnapshot.cs`
- 생성: `src/CodexHp.Core/Domain/TokenActivitySnapshot.cs`
- 생성: `src/CodexHp.Core/Domain/ServiceHealthState.cs`
- 생성: `src/CodexHp.Core/Domain/VisibilityState.cs`
- 생성: `src/CodexHp.Core/Domain/UsageOverlayState.cs`
- 생성: `src/CodexHp.Core/Domain/RefreshGaugeCalculator.cs`
- 생성: `src/CodexHp.Core/Domain/TokenColorInterpolator.cs`
- 생성: `src/CodexHp.Core/Domain/UsageOverlayStateReducer.cs`
- 생성: `tests/CodexHp.Core.Tests/Domain/RefreshGaugeCalculatorTests.cs`
- 생성: `tests/CodexHp.Core.Tests/Domain/TokenColorInterpolatorTests.cs`
- 생성: `tests/CodexHp.Core.Tests/Domain/UsageOverlayStateReducerTests.cs`

**테스트 우선 사례**

- 초기화 전·도중·후 Refresh 비율을 0~1로 제한한다.
- 10,000 이하와 100,000 이상에서 각각 끝 색상을 사용하고 중간은 선형 보간한다.
- 사용량이 없으면 두 게이지가 `--%` 상태다.
- 사용량 실패와 이전 성공값이 있으면 값은 유지하고 stale 표시가 켜진다.
- 그래프 실패는 게이지를 지우지 않는다.
- 서비스 정상은 수직바 없음, 장애와 알 수 없음은 각 색상 수직바다.
- 항상 표시와 ChatGPT 조건보다 같은 모니터 전체화면 숨김이 우선한다.

**순서**

1. 계산기와 상태 환원 테스트를 작성하고 실패를 확인한다.
2. 부동소수점 경계와 시각 의존성을 외부 시각 값으로 입력받는 순수 함수로 구현한다.
3. WPF 형식을 Core에 참조하지 않고 `ColorValue`, 숫자, enum과 record만 사용한다.
4. Core 테스트 전체를 통과시킨다.
5. Task 2~3을 함께 커밋한다.

```text
feat: add CodexHp core state model
```

## Task 4. Codex 인증 캐시와 사용량 클라이언트

**파일**

- 생성: `src/CodexHp.App/Infrastructure/CodexCredentials.cs`
- 생성: `src/CodexHp.App/Infrastructure/CodexCredentialSource.cs`
- 생성: `src/CodexHp.App/Infrastructure/OpenAiUsageClient.cs`
- 생성: `src/CodexHp.App/Application/ICodexCredentialSource.cs`
- 생성: `src/CodexHp.App/Application/IOpenAiUsageClient.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/CodexCredentialSourceTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/OpenAiUsageClientTests.cs`
- 참고하여 이식: `ManaBar/src/ManaBar.Backend/OpenCodeCredentialLocator.cs`
- 참고하여 이식: `ManaBar/src/ManaBar.Backend/OpenAiUsageClient.cs`

**테스트 우선 사례**

- `CODEX_HOME`이 있으면 해당 `auth.json`을 우선한다.
- 없으면 현재 Windows 사용자 프로필의 `.codex\auth.json`을 사용한다.
- snake_case와 camelCase 토큰 키를 모두 읽는다.
- 인증 파일 없음, 잘못된 JSON, 액세스 토큰 없음이 구분된 예외로 반환된다.
- Bearer와 선택적 계정 헤더를 포함한 GET 요청을 보낸다.
- 5시간과 주간 창의 순서가 바뀌어도 지속 시간으로 식별한다.
- 누락·변경된 응답은 계약 오류로 처리하고 비밀값을 오류 메시지에 넣지 않는다.

**순서**

1. 임시 폴더와 가짜 HTTP 처리기를 사용하는 테스트를 먼저 작성한다.
2. 관련 테스트가 실패하는지 확인한다.
3. V1 로직을 Codex 이름으로 정리해 최소 이식하고 OpenCode·Refresh token 코드를 가져오지 않는다.
4. HTTP 제한 시간을 조립 단계에서 10초로 설정할 수 있게 한다.
5. 관련 테스트를 통과시킨다.

## Task 5. Codex JSONL 토큰 활동 스캐너

**파일**

- 생성: `src/CodexHp.App/Infrastructure/CodexTokenUsageScanner.cs`
- 생성: `src/CodexHp.App/Infrastructure/TokenFileCursorCache.cs`
- 생성: `src/CodexHp.App/Application/ICodexTokenActivitySource.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/CodexTokenUsageScannerTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/TokenFileCursorCacheTests.cs`
- 참고하여 이식: `ManaBar/src/ManaBar.Backend/CodexTokenUsageScanner.cs`
- 참고하여 이식: `ManaBar/tests/ManaBar.Backend.Tests/CodexTokenUsageScannerTests.cs`

**테스트 우선 사례**

- sessions와 archived_sessions를 모두 읽는다.
- 15초 버킷 60개를 오래된 순서에서 최신 순서로 반환한다.
- `last_token_usage`와 `total_token_usage` 형식을 안전하게 처리한다.
- 초기 컨텍스트와 compaction 보정 결과가 V1 회귀 테스트와 같다.
- 알 수 없는 이벤트와 깨진 한 줄은 다른 정상 이벤트를 막지 않는다.
- 파일이 추가 기록되면 새 부분만 반영하고, 축소·교체되면 처음부터 안전하게 다시 읽는다.
- 파일 내용이나 인덱스를 디스크에 쓰지 않는다.

**순서**

1. V1의 Codex 테스트를 새 네임스페이스로 옮겨 먼저 실패시킨다.
2. V1 스캐너의 Codex 전용 규칙만 최소 이식한다.
3. 파일 길이·수정 시각·마지막 오프셋을 메모리에 보관하는 캐시 테스트를 추가한다.
4. 정확성 테스트가 유지되는 범위에서 증분 읽기를 구현한다.
5. 대량 샘플에서 한 번의 15초 갱신이 UI 스레드를 점유하지 않는 구조인지 확인한다.
6. Task 4~5를 함께 커밋한다.

```text
feat: collect Codex usage and token activity
```

## Task 6. OpenAI 서비스 상태와 폴링 정책

**파일**

- 생성: `src/CodexHp.App/Infrastructure/OpenAiServiceStatusClient.cs`
- 생성: `src/CodexHp.App/Infrastructure/OpenAiServiceStatusPoller.cs`
- 생성: `src/CodexHp.App/Application/IOpenAiServiceStatusClient.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/OpenAiServiceStatusClientTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/OpenAiServiceStatusPollerTests.cs`
- 참고하여 이식: `ManaBar/src/ManaBar.Backend/OpenAiServiceStatusClient.cs`
- 참고하여 이식: `ManaBar/src/ManaBar.Backend/OpenAiServiceStatusPoller.cs`

**테스트 우선 사례**

- `none`은 정상, 그 외 실제 장애 지표는 장애다.
- V1과 같은 FedRAMP 전용 예외 처리를 유지한다.
- 정상 결과는 3분 캐시하고 실패는 알 수 없음으로 바꾼 뒤 1분 후 재시도한다.
- 취소는 알 수 없음으로 삼키지 않고 상위 종료 흐름에 전달한다.

**순서**

1. V1 테스트를 이식해 실패를 확인한다.
2. HTTP 파서와 poller를 최소 구현한다.
3. 가짜 시계로 3분·1분 경계를 검증한다.
4. App 테스트 전체를 통과시킨다.

## Task 7. 설정 JSON 저장과 옵션 작업 사본

**파일**

- 생성: `src/CodexHp.App/Infrastructure/JsonSettingsStore.cs`
- 생성: `src/CodexHp.App/Application/ISettingsStore.cs`
- 생성: `src/CodexHp.Core/Settings/SettingsEditSession.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/JsonSettingsStoreTests.cs`
- 생성: `tests/CodexHp.Core.Tests/Settings/SettingsEditSessionTests.cs`

**테스트 우선 사례**

- 파일이 없으면 `%LOCALAPPDATA%\settings.json`에 기본값을 생성한다.
- 누락 필드와 잘못된 단일 값만 기본값으로 보충한다.
- 저장은 같은 폴더 임시 파일을 거친 뒤 완전한 JSON으로 교체한다.
- 손상된 전체 파일은 타임스탬프 보존 이름으로 옮기고 기본 파일을 만든다.
- 설정에는 인증·사용량 상태가 직렬화되지 않는다.
- 미리보기 변경 후 취소하면 원본 설정과 위치가 정확히 복원된다.
- 확인하면 작업 사본이 새 원본이 된다.

**순서**

1. 임시 LocalAppData 경계를 주입하는 테스트를 먼저 작성한다.
2. 테스트 실패를 확인하고 JSON source model과 원자 저장을 구현한다.
3. `SettingsEditSession`을 순수 Core 형식으로 구현한다.
4. 손상 파일을 실제로 삭제하지 않는지 검증한다.
5. 관련 테스트를 통과시킨다.

## Task 8. 제한 로그와 시작 프로그램 등록

**파일**

- 생성: `src/CodexHp.App/Infrastructure/RollingFileLogger.cs`
- 생성: `src/CodexHp.App/Infrastructure/StartupRegistration.cs`
- 생성: `src/CodexHp.App/Application/IDiagnosticLogger.cs`
- 생성: `src/CodexHp.App/Application/IStartupRegistration.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/RollingFileLoggerTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/StartupRegistrationTests.cs`

**테스트 우선 사례**

- 로그는 1MB를 넘으면 회전하고 최대 3개만 유지한다.
- 알려진 토큰·인증 헤더·계정 ID 패턴을 기록 전에 제거한다.
- 시작 프로그램 값은 현재 사용자 Run 키와 `CodexHp` 이름을 사용한다.
- 실행 파일 경로는 따옴표로 감싼다.
- 사용 안 함은 해당 값만 제거하고 다른 시작 프로그램 값은 건드리지 않는다.
- 앱 시작만으로 레지스트리를 변경하지 않는다.

**순서**

1. 파일 시스템과 레지스트리 작업을 작은 경계로 감싼 뒤 가짜 구현 테스트를 작성한다.
2. 실패를 확인하고 순환 로그와 HKCU 등록 구현을 추가한다.
3. 설정 확인 중 저장 또는 레지스트리 변경이 실패할 때 이전 상태를 복원하는 조정 테스트를 추가한다.
4. 전체 Core/App 테스트를 통과시킨다.
5. Task 6~8을 함께 커밋한다.

```text
feat: add CodexHp persistence and diagnostics
```

## Task 9. 모니터 위치와 DPI 보정

**파일**

- 생성: `src/CodexHp.Core/Positioning/MonitorGeometry.cs`
- 생성: `src/CodexHp.Core/Positioning/OverlayPlacementCalculator.cs`
- 생성: `src/CodexHp.App/Infrastructure/WindowsMonitorService.cs`
- 생성: `src/CodexHp.App/Application/IMonitorService.cs`
- 생성: `tests/CodexHp.Core.Tests/Positioning/OverlayPlacementCalculatorTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/WindowsMonitorServiceTests.cs`

**테스트 우선 사례**

- 설정이 없으면 주 모니터 작업 영역 좌하단에 배치한다.
- 저장된 모니터와 논리 좌표를 복원한다.
- 저장 모니터가 사라지면 가까운 모니터, 판단 불가면 주 모니터로 이동한다.
- 작업 영역 축소와 화면 배율 변경 후 전체 창이 보이도록 보정한다.
- 100%, 150%, 200% 사이에서 논리 크기가 일관된다.

**순서**

1. 물리 픽셀과 논리 픽셀을 명시한 순수 계산 테스트를 작성한다.
2. 실패를 확인하고 위치 계산기를 구현한다.
3. Win32 모니터·DPI 정보를 `MonitorGeometry`로 변환하는 어댑터를 구현한다.
4. 실제 모니터 API는 최소 smoke test로 확인하고 핵심 경우는 가짜 geometry로 검증한다.

## Task 10. ChatGPT 패키지와 동일 모니터 전체화면 감지

**파일**

- 생성: `src/CodexHp.App/Infrastructure/ChatGptProcessDetector.cs`
- 생성: `src/CodexHp.App/Infrastructure/FullscreenDetector.cs`
- 생성: `src/CodexHp.App/Infrastructure/NativeMethods.cs`
- 생성: `src/CodexHp.App/Application/IChatGptProcessDetector.cs`
- 생성: `src/CodexHp.App/Application/IFullscreenDetector.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/ChatGptProcessDetectorTests.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/FullscreenDetectorTests.cs`

**테스트 우선 사례**

- `ChatGPT.exe`이면서 `OpenAI.Codex_2p2nqsd0c76g0` 패키지인 프로세스만 현재 공식 앱으로 인정한다.
- 같은 이름의 비패키지 또는 다른 패키지 프로세스는 인정하지 않는다.
- `codex.exe`는 표시 조건에 영향을 주지 않는다.
- 최소화·숨김·cloaked·셸·CodexHp 창은 전체화면 후보에서 제외한다.
- 활성 창 경계가 모니터 경계를 덮고 사용량 오버레이와 같은 모니터일 때만 숨김이다.
- 다른 모니터의 전체화면은 숨김이 아니다.

**순서**

1. 프로세스와 창 정보를 record 스냅샷으로 받는 판정 테스트를 먼저 작성한다.
2. 순수 판정 로직을 구현한다.
3. `GetPackageFamilyName`, `GetForegroundWindow`, DWM 경계와 모니터 API를 호출하는 Windows 어댑터를 추가한다.
4. 접근할 수 없는 프로세스와 사라진 창을 정상적인 미검출로 처리한다.
5. Task 9~10을 함께 커밋한다.

```text
feat: detect Windows display conditions
```

## Task 11. 단일 프로세스 조정기와 폴링 수명주기

**파일**

- 생성: `src/CodexHp.App/Application/ApplicationCoordinator.cs`
- 생성: `src/CodexHp.App/Application/ProviderState.cs`
- 생성: `src/CodexHp.App/Application/PollSchedule.cs`
- 생성: `src/CodexHp.App/Application/IClock.cs`
- 생성: `tests/CodexHp.App.Tests/Application/ApplicationCoordinatorTests.cs`

**테스트 우선 사례**

- 시작 즉시 초기 사용량 오버레이 상태를 한 번 발행한다.
- 사용량·그래프는 15초, 가시성은 1초 주기로 갱신한다.
- Refresh 값은 1초마다 로컬 계산만 수행한다.
- 한 공급자 예외가 다른 공급자 루프와 프로세스를 종료하지 않는다.
- 사용량 실패 후 마지막 성공값을 유지하고 다음 주기에 인증 파일을 다시 읽는다.
- 취소 요청 후 모든 루프가 끝나고 추가 상태를 발행하지 않는다.

**순서**

1. 가짜 시계, 지연과 공급자를 사용하는 결정적 테스트를 작성한다.
2. 동시 루프 없이 직렬 실행하는 최소 구현부터 실패를 통과시킨다.
3. UI를 막지 않는 독립 비동기 루프로 분리하되 상태 갱신은 잠금 또는 단일 상태 큐로 직렬화한다.
4. 서비스 상태 poller의 3분·1분 캐시를 중복 호출하지 않는지 검증한다.
5. 종료 테스트에서 남은 Task가 없는지 확인한다.

## Task 12. 사용량 오버레이 렌더러와 창

**파일**

- 생성: `src/CodexHp.App/Presentation/UsageOverlayRenderer.cs`
- 생성: `src/CodexHp.App/Presentation/UsageOverlayWindow.xaml`
- 생성: `src/CodexHp.App/Presentation/UsageOverlayWindow.xaml.cs`
- 생성: `tests/CodexHp.App.Tests/Presentation/UsageOverlayLayoutTests.cs`
- 생성: `tests/CodexHp.App.Tests/Presentation/UsageOverlayRendererTests.cs`

**테스트 우선 사례**

- 기본 288×34에서 두 게이지, 두 2px Refresh, 그래프와 기준선 경계가 겹치지 않는다.
- 여섯 모양 값 변경이 논리 좌표 배치에 반영된다.
- 상태 수직바가 없을 때와 있을 때 그래프·게이지 경계를 침범하지 않는다.
- 그래프는 오른쪽부터 최신 60개를 배치하고 5분 점선을 계산한다.
- stale 사용량은 정상보다 낮은 불투명도로 그린다.
- 위치 변경 모드는 안쪽 4 논리 픽셀 외곽선을 추가한다.

**순서**

1. 렌더링 명령 목록을 만드는 순수 layout 테스트부터 작성한다.
2. `DrawingContext`에 적용할 도형·텍스트 명령 계산을 구현한다.
3. WPF `FrameworkElement.OnRender`에서 명령을 실제로 그린다.
4. 창을 `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=false`, `Topmost=true`, 불투명 `#18181C`로 구성한다.
5. 일반 상태에서는 단일 클릭·드래그를 무시하고 더블클릭 이벤트만 노출한다.

## Task 13. 옵션 ViewModel과 옵션 창

**파일**

- 생성: `src/CodexHp.App/Presentation/Settings/SettingsWindowViewModel.cs`
- 생성: `src/CodexHp.App/Presentation/Settings/SettingsGroup.cs`
- 생성: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml`
- 생성: `src/CodexHp.App/Presentation/Settings/SettingsWindow.xaml.cs`
- 생성: `tests/CodexHp.App.Tests/Presentation/SettingsWindowViewModelTests.cs`

**테스트 우선 사례**

- 그룹은 일반, 색상, 모양, 위치 변경 순서다.
- 색상·모양·위치는 즉시 preview 이벤트를 발행한다.
- 표시 조건과 시작 프로그램은 확인 전에는 실제 적용하지 않는다.
- 위치 변경 그룹 진입·이탈이 외곽선과 드래그 가능 상태를 바꾼다.
- 확인은 유효성 검사 후 저장·일반 옵션 적용을 완료해야 창을 닫는다.
- 취소, 닫기 버튼과 Esc는 같은 복원 경로를 사용한다.
- 이미 열린 옵션 창 열기 요청은 새 ViewModel을 만들지 않는다.

**순서**

1. UI를 만들기 전에 ViewModel과 설정 작업 사본 테스트를 작성한다.
2. 테스트 실패를 확인하고 ViewModel을 구현한다.
3. 왼쪽 ListBox와 오른쪽 ContentControl, 아래 확인·취소 구조의 WPF 창을 만든다.
4. 일반에는 두 체크박스, 색상에는 일곱 `ColorDialog` 진입점, 모양에는 검증되는 숫자 TextBox를 배치한다.
5. 위치 변경 오른쪽에는 지정 안내 문구만 표시한다.
6. Task 11~13을 함께 커밋한다.

```text
feat: add CodexHp overlay and settings UI
```

## Task 14. 트레이, 단일 인스턴스와 앱 조립

**파일**

- 생성: `src/CodexHp.App/Infrastructure/SingleInstanceGuard.cs`
- 생성: `src/CodexHp.App/Presentation/TrayIconController.cs`
- 생성: `tests/CodexHp.App.Tests/Infrastructure/SingleInstanceGuardTests.cs`
- 생성: `tests/CodexHp.App.Tests/Presentation/TrayIconControllerTests.cs`
- 수정: `src/CodexHp.App/App.xaml`
- 수정: `src/CodexHp.App/App.xaml.cs`

**테스트 우선 사례**

- 첫 인스턴스만 Mutex를 소유하고 두 번째는 앱 구성 전에 종료한다.
- 트레이 좌클릭은 옵션 열기만 호출한다.
- 우클릭 메뉴에는 `옵션`, `종료` 두 항목이 순서대로 존재한다.
- `옵션`은 옵션 창 열기, `종료`는 공용 종료 요청을 호출한다.
- 종료가 완료되면 NotifyIcon이 숨겨지고 폐기된다.
- 임시 아이콘은 `SystemIcons.Application`이며 ChatGPT 자산을 읽지 않는다.

**순서**

1. WinForms 객체 자체와 동작 결정을 분리한 controller 테스트를 먼저 작성한다.
2. 실패를 확인하고 Mutex guard와 트레이 controller를 구현한다.
3. `App.xaml.cs`에서 설정, 로그, HTTP, 공급자, coordinator, 화면, 옵션과 트레이를 수동 조립한다.
4. 앱 시작 시 화면을 즉시 보이고 background coordinator를 시작한다.
5. 종료 메뉴와 Windows 세션 종료가 같은 정리 경로를 사용하게 한다.

## Task 15. 위치 변경, 전체화면과 ChatGPT 표시 통합

**파일**

- 수정: `src/CodexHp.App/Presentation/UsageOverlayWindow.xaml.cs`
- 수정: `src/CodexHp.App/Presentation/Settings/SettingsWindowViewModel.cs`
- 수정: `src/CodexHp.App/Application/ApplicationCoordinator.cs`
- 생성: `tests/CodexHp.App.Tests/Application/VisibilityIntegrationTests.cs`
- 생성: `tests/CodexHp.App.Tests/Presentation/OverlayPositionPreviewIntegrationTests.cs`

**테스트 우선 사례**

- 위치 변경 모드에서만 `DragMove` 진입 조건이 참이다.
- 모니터 이동 후 작업 좌표가 대상 모니터 논리 좌표로 갱신된다.
- 취소하면 창을 열기 전 모니터와 좌표로 되돌아간다.
- 항상 표시 기본값은 인증 없음과 ChatGPT 미실행에서도 표시다.
- 조건 체크 시 공식 ChatGPT 앱이 없으면 숨기고 실행되면 표시한다.
- 같은 모니터 활성 전체화면은 모든 일반 표시 조건보다 우선해 숨긴다.
- 전체화면 종료 후 현재 일반 표시 조건으로 복원한다.

**순서**

1. 가짜 창·모니터·프로세스를 사용하는 통합 테스트를 작성한다.
2. 실패를 확인한 뒤 Window 이벤트와 coordinator 상태를 연결한다.
3. 실제 창 핸들이 생성된 뒤 DPI·ToolWindow 확장 스타일을 적용한다.
4. 1초 poll이 옵션 창과 트레이 반응을 막지 않는지 확인한다.
5. Task 14~15를 함께 커밋한다.

```text
feat: integrate CodexHp Windows lifecycle
```

## Task 16. 자동 검증 스크립트와 실제 Windows 11 검증

**파일**

- 생성: `Scripts/Agent/Verify-Core.ps1`
- 생성: `Scripts/Agent/Run-Development.ps1`
- 생성: `tests/Windows/Validate-PublishedApp.ps1`
- 수정: `Docs/Agent-Development-CodexHp.md`
- 수정: `Docs/요구사항-CodexHp.md`

**자동 검증**

1. `Verify-Core.ps1`이 restore, build, 전체 test와 `git diff --check`를 순서대로 실행하게 한다.
2. 스크립트 검증 테스트를 먼저 작성하거나 최소한 PowerShell parser로 문법을 검사한다.
3. 로컬 검증용으로 현재 PC 아키텍처의 자기 포함 단일 파일을 루트의 무시된 `out`에 게시한다. 이 아키텍처는 공개 지원 결정이 아니다.

```powershell
dotnet publish src/CodexHp.App/CodexHp.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -o out/win-x64
```

4. `Validate-PublishedApp.ps1`에서 게시 디렉터리의 실행 구성요소가 `CodexHp.exe` 하나인지, 시작 후 단일 프로세스와 트레이가 존재하는지 검사한다.
5. 최종 아이콘은 만들지 않고 Windows 기본 임시 아이콘을 유지한다.

**실제 화면 검증**

1. Windows 11에서 앱을 실행하고 사용량 오버레이와 트레이를 화면 진단 도구로 확인한다.
2. 트레이 좌클릭, 우클릭 메뉴, 옵션과 종료를 확인한다.
3. 사용량 오버레이 더블클릭과 위치 변경 탭의 4px 외곽선·드래그·취소 복원을 확인한다.
4. 색상과 모양 즉시 미리보기, 확인·취소를 확인한다.
5. 가능한 서로 다른 DPI 모니터 사이를 이동하고 재시작 위치를 확인한다.
6. 같은 모니터와 다른 모니터의 전체화면을 각각 실행해 숨김 차이를 확인한다.
7. ChatGPT 표시 조건과 실제 `ChatGPT.exe` 패키지 판정을 확인한다.
8. 실제 인증이 있는 환경에서 한도와 로컬 그래프가 나타나는지 확인하되 비밀값을 캡처하지 않는다.
9. 네트워크 차단 또는 가짜 오류 주입 뒤 마지막 값·stale·복구 동작을 확인한다.

**문서와 커밋**

1. 실제 구현과 다른 설계·요구사항 문구를 정본 문서에 반영한다.
2. 완료된 검증과 남은 설치·아이콘 마일스톤을 구분한다.
3. 전체 검증과 `git status`를 확인한다.
4. 커밋한다.

```text
test: verify CodexHp core functionality
```

## 핵심 기능 완료 조건

- 전체 자동 테스트가 통과한다.
- 게시된 `CodexHp.exe`가 .NET 런타임을 별도로 설치하지 않은 Windows 11 조건을 만족하도록 구성된다.
- 트레이, 사용량 오버레이, 옵션, 위치, DPI, ChatGPT 조건, 전체화면 숨김과 정상 종료를 실제 GUI에서 검증한다.
- ChatGPT 앱과 Codex가 정상 로그인된 현재 PC에서 별도 CodexHp 로그인 없이 한도와 그래프가 표시된다.
- 인증·네트워크·JSONL·서비스 상태 실패가 서로 격리되고 복구된다.
- 토큰, 인증 헤더, 계정 ID와 대화 내용이 설정·로그·테스트·Git에 남지 않는다.
- 초기화 티켓 관련 네트워크 호출과 UI가 존재하지 않는다.
- 최종 아이콘, Inno Setup, 에이전트 설치 스크립트와 공개 배포는 시작하지 않는다.

## 계획 종료 후 다음 단계

이 계획이 승인되면 `test-driven-development` 절차를 적용해 Task 1부터 순서대로 구현한다. 각 마일스톤에서 관련 테스트와 전체 회귀 테스트를 통과시키고 커밋하며, 실제 GUI 검증이 필요한 시점에는 Windows 화면 증거를 함께 확인한다.
