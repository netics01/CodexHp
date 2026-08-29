# CodexHp 개발 설계

## 1. 문서 목적과 상태

- 대상 환경: Windows 11 빌드 22000 이상
- 구현 기술: .NET 10, WPF와 Win32 API
- 상태: 사용자와 구간별로 승인한 구현 설계를 정본으로 정리했으며 문서 검토 대기 중
- 목적: CodexHp 핵심 기능의 구조, 데이터 흐름, Windows 연동 경계, 오류 처리와 검증 방법을 구현 기준으로 유지한다.
- 요구사항 정본: `요구사항-CodexHp.md`
- 조사 기록: `사전조사-CodexHp.md`

이 문서는 핵심 기능 구현을 다룬다. 설치 프로그램, 에이전트용 설치 스크립트, 최종 아이콘 제작과 공개 GitHub 배포는 핵심 기능 완성·검증 이후의 별도 마일스톤이다.

## 2. 설계 원칙

1. `CodexHp.exe` 하나가 트레이, 사용량 오버레이, 옵션 창과 모든 데이터 수집을 담당한다.
2. 실행 중 별도 app-server, 백엔드 실행 파일, Windows 서비스 또는 자기 자신의 두 번째 상주 프로세스를 만들지 않는다.
3. V1의 Codex 인증 캐시와 ChatGPT 사용량 HTTP 연결 방식을 유지한다.
4. 인증·사용량·로컬 그래프·서비스 상태·화면 가시성은 서로 독립적으로 실패하고 복구할 수 있어야 한다.
5. 화면 표시 규칙과 계산 로직은 WPF에서 분리해 자동 테스트할 수 있게 한다.
6. Windows 전용 제품이므로 불필요한 다중 플랫폼 추상화는 만들지 않는다.
7. UI 프레임워크, 의존성 주입 컨테이너, 트레이 또는 컬러 피커용 외부 패키지는 필요성이 입증되기 전에는 추가하지 않는다.
8. 인증 토큰과 계정 식별자는 메모리의 요청 경계 밖으로 전달하거나 설정·로그에 기록하지 않는다.

## 3. 솔루션 구조

예정 소스 구조는 다음과 같다.

```text

  CodexHp.slnx
  src/
    CodexHp.App/
      CodexHp.App.csproj
      App.xaml
      Presentation/
      Application/
      Infrastructure/
      Assets/
    CodexHp.Core/
      CodexHp.Core.csproj
      Domain/
      Settings/
  tests/
    CodexHp.Core.Tests/
    CodexHp.App.Tests/
  Docs/
```

### 3.1 `CodexHp.Core`

Windows UI와 네트워크 구현을 모르는 순수 핵심 로직을 둔다.

- 사용량과 Refresh 비율 계산
- 토큰 버킷, 10K knee 완화 로그 높이와 그래프 색상 보간
- 공급자별 상태 모델
- 최종 사용량 오버레이 상태 환원
- 설정 모델과 기본값
- 모니터 전체 영역 기준 물리 위치 계산과 보정
- 옵션 작업 사본의 확인·취소 규칙

### 3.2 `CodexHp.App`

실행 수명주기와 Windows 기능을 둔다.

- Win32/GDI 사용량 오버레이와 WPF 옵션 창
- Win32 `Shell_NotifyIconW`, 팝업 메뉴와 `ChooseColorW`
- 인증 파일과 Codex JSONL 읽기
- HTTP 사용량과 OpenAI 서비스 상태 조회
- ChatGPT 패키지 프로세스 감지
- 활성 전체화면 창과 모니터 판정
- JSON 설정 저장
- 사용자별 시작 프로그램 등록
- 제한된 순환 진단 로그

### 3.3 테스트 프로젝트

- `CodexHp.Core.Tests`: 계산, 상태 환원, 설정, 위치와 옵션 트랜잭션 단위 테스트
- `CodexHp.App.Tests`: 파일·HTTP·레지스트리·프로세스·창 판정 어댑터의 계약 테스트

소스 프로젝트가 여러 개여도 게시 단계에서는 자기 포함 단일 파일 `CodexHp.exe`로 묶는다. 실행 중 상주하는 애플리케이션 프로세스는 하나다.

### 3.4 게시 크기 정책

- App과 App 테스트 TFM은 `net10.0-windows10.0.22000.0`이며 최소 지원 OS를 Windows 11 빌드 22000으로 고정한다.
- 기본 산출물은 `win-x64` self-contained single-file이므로 깨끗한 Windows 11에 .NET Desktop Runtime을 별도로 설치하지 않는다.
- `EnableCompressionInSingleFile=true`로 번들 내부 관리 런타임을 압축한다.
- `SatelliteResourceLanguages=ko`로 중립 영문 리소스와 한국어 위성 리소스만 남긴다. 느슨한 게시 검증에서 지역화 디렉터리는 `ko` 하나만 생성되어야 한다.
- `PublishTrimmed=false`를 명시한다. WPF는 XAML과 리플렉션을 사용하므로 안전성이 확인되지 않은 어셈블리 삭제를 크기 절감 수단으로 사용하지 않는다.
- `Verify-Core.ps1`은 게시 결과가 `CodexHp.exe` 하나인지 확인하고 크기가 `100 MiB`를 넘으면 실패한다.

## 4. 애플리케이션 수명주기

### 4.1 시작

1. `Local\CodexHp.SingleInstance` 이름의 Mutex를 확보한다.
2. 이미 다른 인스턴스가 보유 중이면 중복 트레이와 사용량 오버레이를 만들지 않고 종료한다.
3. 설정 파일을 읽고 누락되거나 잘못된 항목에 기본값을 적용한다. 파일이 없으면 기본 설정 파일을 생성한다.
4. Windows 기본 아이콘을 임시 개발 아이콘으로 사용해 트레이 아이콘을 만든다.
5. 사용량 오버레이를 즉시 표시한다. 최초 기본 표시 조건은 `항상 표시`이므로 인증 데이터가 없어도 보인다.
6. 같은 프로세스 안에서 데이터 수집과 가시성 감시 작업을 시작한다.

### 4.2 실행

- WPF Dispatcher 스레드는 창과 렌더링만 담당한다.
- 네트워크, JSONL 탐색과 프로세스 감지는 취소 가능한 비동기 작업으로 실행한다.
- `ApplicationCoordinator`가 공급자 결과를 최신 상태로 보관하고 하나의 `UsageOverlayState`로 합친다.
- 사용량 오버레이 상태 변경은 WPF Dispatcher를 통해 표시 창에 전달한다.
- V1의 Windhawk 전달용 상태 JSON 파일은 만들지 않는다.

### 4.3 종료

- 트레이 우클릭 메뉴의 `종료`만 정상 사용자 종료 진입점으로 제공한다.
- 종료 요청 시 공용 `CancellationTokenSource`를 취소하고 진행 중인 작업을 제한 시간 안에 정리한다.
- 트레이 아이콘을 숨기고 폐기한 다음 WPF 애플리케이션을 종료한다.

## 5. 구성요소 경계

핵심 인터페이스는 구현을 교체하기 위한 범용 계층이 아니라 테스트 경계를 만들기 위한 최소 단위다.

| 구성요소 | 책임 |
| --- | --- |
| `ICodexCredentialSource` | Codex 인증 캐시 위치 결정과 현재 요청용 인증 정보 읽기 |
| `IOpenAiUsageClient` | 세션·주간 한도와 초기화 시각 조회 |
| `ICodexTokenActivitySource` | 로컬 세션 JSONL을 15초 토큰 버킷으로 집계 |
| `IOpenAiServiceStatusClient` | OpenAI 서비스 상태 조회와 정규화 |
| `IChatGptProcessDetector` | 공식 Windows 앱 패키지의 `ChatGPT.exe` 실행 여부 판정 |
| `IFullscreenDetector` | 사용량 오버레이 모니터의 활성 전체화면 창 판정 |
| `ISettingsStore` | 버전이 있는 JSON 설정을 원자적으로 읽고 저장 |
| `IStartupRegistration` | 현재 사용자 시작 프로그램 등록 확인·변경 |
| `IClock` | Refresh 계산과 폴링 테스트를 위한 현재 시각 제공 |

구체 구현은 애플리케이션 시작점에서 직접 조립한다. 별도 의존성 주입 컨테이너는 사용하지 않는다.

## 6. 데이터 수집과 갱신

### 6.1 인증과 사용량

인증 경로 우선순위는 다음과 같다.

1. `CODEX_HOME` 환경 변수가 비어 있지 않으면 `%CODEX_HOME%\auth.json`
2. 아니면 `%USERPROFILE%\.codex\auth.json`

매 사용량 조회 주기에 인증 파일을 다시 읽는다. `access_token`과 선택적 `account_id`로 다음 주소에 읽기 전용 GET 요청을 보낸다.

```text
https://chatgpt.com/backend-api/wham/usage
```

- 요청 간격: 15초
- HTTP 제한 시간: 10초
- 인증 헤더: `Authorization: Bearer ...`
- 계정 ID가 있을 때: `ChatGPT-Account-Id: ...`
- 응답에서 5시간 창과 주간 창을 지속 시간으로 식별한다.
- `used_percent`는 `100 - used_percent`로 남은 비율로 변환하고 0~100으로 제한한다.
- CodexHp는 토큰 갱신이나 로그인 흐름을 수행하지 않는다. ChatGPT 앱이 인증 파일을 갱신하면 다음 주기에 새 값을 사용한다.

### 6.2 로컬 토큰 활동

- 대상: `.codex\sessions`와 `.codex\archived_sessions` 아래 JSONL
- 해상도: 15초 버킷 60개, 총 15분
- 갱신 간격: 15초
- 오른쪽 끝 버킷이 최신 구간이다.
- V1의 초기 컨텍스트 분산, compaction 보정과 token-count 파싱 규칙을 테스트와 함께 이식한다.
- 변경된 최근 파일만 다시 읽을 수 있도록 파일 길이와 최종 수정 시각 기반의 메모리 캐시를 사용한다.
- 캐시는 성능 최적화 수단이며 정본 파일을 수정하거나 별도 세션 인덱스 파일을 만들지 않는다.
- `TokenGraphHeightScaler`는 화면 내 최대 버킷과 그래프 가용 높이를 받아 `10K` knee의 `log(1 + value / 10K) / log(1 + maximum / 10K)` 비율을 정수 픽셀로 변환한다.
- 값이 0이면 `0px`, 양수면 최소 `1px`, 화면 최대값 이상이면 전체 높이를 반환한다. 높이만 로그 압축하고 `TokenColorInterpolator`의 실제 토큰 `10K~100K` 선형 색상 보간은 유지한다.

### 6.3 OpenAI 서비스 상태

- V1의 `status.openai.com` 상태와 구성요소 조회 방식을 유지한다.
- 정상 조회 후 다음 확인: 3분
- 조회 실패 후 재시도: 1분
- 정상일 때 상태 수직바를 숨긴다.
- 장애일 때 장애 색상, 확인 실패일 때 알 수 없음 색상을 사용한다.
- 장애 상태에서는 상태 API의 `description`을 `ProviderState`와 `UsageOverlayState`까지 보존한다. 알 수 없음 상태는 툴팁 데이터를 만들지 않으며, 장애 설명이 비어 있으면 `OpenAI service issue detected.`를 대체 문구로 사용한다.
- `Issue` 상태의 사용량 오버레이에 마우스를 200ms 머무르면 `OpenAI service issue: {description}` 네이티브 툴팁을 표시한다. 상태 API의 비정상 구성요소는 정상·FedRAMP 항목을 제외해 이름만 두 번째 행에 쉼표로 나열한다. 목록이 비어 있으면 이 행을 생략한다. `tooltips_class32`는 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`로 만들며, 오버레이와 동일하게 Alt+Tab에 나타나거나 포커스를 얻지 않는다.
- 명시적 `\r\n` 행바꿈을 실제 툴팁에 반영하기 위해 `TTM_SETMAXTIPWIDTH`를 가상 데스크톱 전체 폭으로 설정한다. 이전 360px 고정 폭보다 넓어 임의의 좁은 폭 제한 없이 행바꿈만 활성화한다.
- 앱 매니페스트가 Common Controls v6에 의존하지 않으므로 툴팁 도구를 등록할 때는 `TTTOOLINFOW`의 v2 크기(`lParam`까지)를 사용한다. 등록 뒤 `TTM_GETTOOLCOUNT`가 `1`인지 통합 테스트로 확인해 v5/v6 공통 컨트롤에서 모두 마우스 대상이 실제로 연결됐음을 보장한다.

### 6.4 화면 가시성

- `ChatGPT.exe`와 활성 전체화면 창은 1초 간격으로 확인한다.
- Refresh 게이지는 저장된 초기화 시각과 현재 시각으로 매초 계산하며 이를 위해 서버를 다시 호출하지 않는다.
- 데이터 조회와 가시성 감시는 독립적이므로 인증 실패가 전체화면 자동 숨김이나 트레이 동작을 중단시키지 않는다.

### 6.5 초기화 티켓 제외

초기화 티켓 잔여 수는 같은 인증으로 조회 가능하다는 조사만 보존한다. CodexHp는 당분간 티켓 조회 주소를 호출하지 않으며 개수·만료 시각을 표시하거나 티켓을 사용하지 않는다.

## 7. 상태 모델과 오류 격리

`ApplicationCoordinator`는 다음 부분 상태를 각각 보관한다.

- `UsageState`: 아직 없음, 최신 성공, 마지막 성공값을 가진 실패, 성공값 없는 실패
- `TokenActivityState`: 최신 성공 또는 실패
- `ServiceHealthState`: 정상, 장애, 알 수 없음
- `VisibilityState`: 표시 조건, ChatGPT 실행 여부, 같은 모니터 전체화면 여부
- `SettingsState`: 저장된 설정, 옵션 창의 작업 사본, 미리보기 여부

순수 `UsageOverlayStateReducer`가 부분 상태, 현재 시각과 설정을 입력받아 최종 사용량 오버레이 상태를 만든다.

| 상황 | 화면 동작 |
| --- | --- |
| 인증 없음 또는 첫 사용량 대기 | ManaBar/HpBar에 `--%`, 로컬 그래프는 가능하면 표시 |
| 사용량 재조회 실패와 이전 성공값 있음 | 마지막 값을 유지하고 게이지 불투명도를 낮춰 오래된 값임을 표시 |
| 사용량 성공값 없음 | `--%` 유지 |
| JSONL 조회 실패 | 게이지는 유지하고 그래프 영역만 비움 |
| 서비스 장애 | 장애 색상의 상태 수직바 표시 |
| 서비스 상태 조회 실패 | 알 수 없음 색상의 상태 수직바 표시 |
| 일부 공급자 복구 | 복구된 영역만 즉시 정상 표시로 전환 |

오류 때문에 반복 팝업이나 Windows 알림을 띄우지 않는다. 마지막 성공값은 프로세스 메모리에만 보관하며 재시작 뒤 오래된 사용량을 복원하지 않는다.

트레이 툴팁에는 비밀값을 제외한 현재 요약 상태만 표시한다. 사용자 조작으로 설정을 저장하지 못한 경우처럼 즉시 알려야 하는 오류는 옵션 창 안에서 한 번 표시하고 창을 유지한다.

## 8. 사용량 오버레이 렌더링

### 8.1 창 속성과 합성 표면

- 작업표시줄 내부: `Shell_TrayWnd`의 `WS_CHILD | WS_EX_LAYERED | WS_EX_TOOLWINDOW` 자식 창
- 작업표시줄 외부: `WS_POPUP | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE` 최상위 창
- 작업표시줄과 Alt+Tab에서 숨김
- 사용량 오버레이 전체가 불투명하고 마우스 입력을 받음
- 기본 배경색 `#18181C`
- 기본 크기 `288 × 68` 물리 픽셀
- 일반 상태에서 단일 클릭과 드래그는 동작 없음
- 더블클릭은 기존 옵션 창을 열거나 앞으로 가져옴

사용량 오버레이는 V1과 같은 GDI 좌표와 그리기 방식을 사용하되, raw Win32 `WM_PAINT` 창에 직접 그리지 않는다. `GdiBitmapSourceRenderer`가 32비트 top-down DIB에 `GdiUsageOverlayPainter`의 사각형·텍스트를 물리 픽셀 정수로 그린 뒤, 사용되지 않는 알파 바이트가 투명도로 해석되지 않도록 `Bgr32`로 변환한다. `WpfOverlaySurface`는 이 비트맵을 `AllowsTransparency` WPF 창에 표시하고 `OverlayWindowHost`가 같은 HWND의 부모·스타일·물리 위치를 전환한다.

WPF 창은 `ShowInTaskbar=false` 상태에서 HWND를 만들고 WPF `Window.Show/Hide` 수명주기로 표시한다. `TaskbarChild`에서 팝업으로 분리할 때 숨은 WPF owner는 `GWLP_HWNDPARENT`로 제거한다. 같은 WPF 합성 HWND를 팝업에서 작업표시줄 자식으로 다시 붙이면 DWM 픽셀이 사라질 수 있으므로, 드래그가 작업표시줄 내부에서 끝난 경우 마지막 물리 좌표와 사용량 오버레이 상태를 보존한 새 `WpfOverlaySurface`를 생성해 결합한다.

`WpfOverlaySurface`는 오버레이 HWND 하나당 네이티브 상태 툴팁 하나를 소유한다. 툴팁은 상태 설명이 있는 `Issue`일 때만 활성화하고, 마우스 이탈은 공용 컨트롤의 표준 동작으로 처리한다. 위치 변경 드래그를 시작할 때는 툴팁을 비활성화하고, 드래그가 끝나면 보존된 오버레이 상태로 다시 활성화한다. 숨김, 표면 재생성, 폐기 시에는 툴팁을 명시적으로 닫아 이전 HWND에 남지 않게 한다.

드래그 분리 뒤 `DesktopPopup`을 같은 모드로 다시 확정할 때 `GetParent`가 이미 `0`이면 `GWLP_HWNDPARENT=0`을 반복하지 않는다. Windows 11 실제 인수테스트에서 이 중복 호출이 살아 있는 WPF HWND에 `ERROR_INVALID_WINDOW_HANDLE(1400)`를 반환해 드래그 완료를 중단시키는 것을 확인했다.

Alt+Tab 제외는 사용량 오버레이의 호스팅 전환에 관계없이 같은 불변식으로 유지한다. 옵션 창은 사용자가 일반 창처럼 전환할 수 있도록 반대 스타일을 적용한다.

- `AltTabWindowStyle.Apply`는 HWND 생성 직후 `WS_EX_TOOLWINDOW`를 설정하고 `WS_EX_APPWINDOW`를 제거한다. Microsoft [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) 문서에 따라 `WS_EX_TOOLWINDOW` 창은 작업표시줄과 Alt+Tab에 표시되지 않는다.
- `OverlayWindowHost`는 `TaskbarChild`와 `DesktopPopup` 모두에서 이 두 플래그를 다시 강제해 드래그 중 부모·스타일 전환 뒤에도 제외 상태를 보존한다.
- 옵션 창은 WPF `ShowInTaskbar=true`와 함께 `OnSourceInitialized`에서 `AltTabWindowStyle.ApplyVisible`로 `WS_EX_TOOLWINDOW`를 제거하고 `WS_EX_APPWINDOW`를 설정한다. 열려 있는 옵션 창은 Alt+Tab과 작업표시줄에 표시된다.
- 단위·실제 HWND·드래그 인수테스트와 게시 검증은 오버레이의 `WS_EX_TOOLWINDOW=설정`, `WS_EX_APPWINDOW=제거`와 옵션 창의 반대 스타일을 각각 검사한다.

### 8.2 기본 배치

- 왼쪽 게이지 영역에 ManaBar, Mana Refresh, HpBar, Hp Refresh를 위에서 아래로 배치한다.
- 오른쪽 그래프 영역에는 기준선, 5분 구분 점선과 60개 토큰 막대를 표시한다.
- 상태 수직바는 장애 또는 알 수 없음일 때만 왼쪽에 표시한다.
- 그래프 높이는 `TokenGraphHeightScaler`의 `10K` knee 완화 로그로 계산해 초기 컨텍스트 스파이크가 있어도 평상시 활동을 구분한다.
- 그래프 색상은 낮음·높음 색상 사이를 토큰 값으로 선형 보간한다.

### 8.3 Overlay Position

- `Overlay Position` 그룹이 선택된 동안 사용량 오버레이 안쪽에 4 물리 픽셀 외곽선을 표시한다.
- 이 모드에서만 마우스 드래그로 창을 이동한다.
- 이동 중 대상 모니터가 바뀌어도 창 크기와 좌표를 DPI로 변환하지 않는다.
- 작업표시줄 경계에 부분 교차하면 `intersection area`와 나머지 창 면적을 비교한다. 작업표시줄 점유 면적이 절반 이상이면 `TaskbarChild`, 절반 미만이면 `DesktopPopup`을 선택한다.
- 부분 교차에서 선택된 `TaskbarChild`의 Y는 `taskbar.Top + (taskbar.Height - overlay.Height) / 2`이고, `DesktopPopup`은 창 하단이 작업표시줄 상단에 맞닿는다. 완전 내부 위치는 재정렬하지 않는다.
- `확인`은 작업 좌표를 저장하고 `취소`, 닫기 버튼과 `Esc`는 옵션 창을 열기 전 좌표로 복원한다.
- 시스템 X 닫기는 `Closing` 처리 안에서 `Close()`를 재호출하지 않는다. 먼저 현재 닫기를 취소하고 Dispatcher 다음 차례에 ViewModel 취소를 실행해 승인된 단일 닫기 경로로 종료한다.

## 9. 트레이와 옵션 창

### 9.1 트레이

Windows Forms를 참조하지 않는다. `WindowsTrayIconView`는 입력을 받지 않는 숨은 WPF `HwndSource`를 만들고 `Shell_NotifyIconW`에 고정 아이콘, tooltip과 callback 메시지를 등록한다. Explorer가 재시작해 `TaskbarCreated` 메시지가 오면 보이는 아이콘을 다시 등록한다.

- 좌클릭: 옵션 창 열기
- 우클릭: `CreatePopupMenu`, `AppendMenuW`, `TrackPopupMenuEx`로 `Options`, `Exit` 메뉴 표시
- `옵션`: 옵션 창 열기
- `종료`: 정상 종료 시작
- 옵션 창이 이미 열려 있으면 새 창을 만들지 않고 기존 창을 활성화한다.
- `TrackPopupMenuEx` P/Invoke는 실제 API와 같은 6개 인수를 사용한다. owner HWND 앞에 `TrackPopupMenu`의 예약 인수를 섞으면 메뉴가 즉시 취소되므로 reflection 회귀 테스트로 서명을 고정한다.
- 종료 시 `NIM_DELETE`를 먼저 보내고 메시지 훅, 숨은 HWND와 아이콘을 순서대로 폐기한다.

트레이와 실행 파일은 `Assets/CodexHp.ico` 고정 자산을 사용한다. `Scripts/Assets/New-CodexHpIcon.ps1`은 개발 시 설치된 공식 `OpenAI.Codex` 패키지의 256px 비도금 문양에 짙은 배경판과 84% 빨간 HP 게이지를 합성하고, `16, 20, 24, 32, 40, 48, 64, 128, 256px` 프레임을 만든다. 실행 중에는 공식 앱을 조회하거나 아이콘을 추출하지 않는다. 32px 프레임의 공식 문양 밝은 픽셀 폭 `26px` 이상을 회귀 테스트로 고정한다.

### 9.2 옵션 창 구조

왼쪽 세로 그룹 목록과 오른쪽 내용 영역, 아래 `확인`·`취소` 버튼으로 구성한다.

- 일반
- 색상
- 모양
- Overlay Position

옵션 창의 시각 계약은 Windows 11에서 실행 중인 픽픽 옵션 창을 200% DPI로 캡처하고 논리 크기로 환산한 값을 기준으로 한다.

- 창 `650 × 502`, 루트 여백 `10`. 높이는 기존 `590 × 0.85 = 501.5`를 가장 가까운 정수로 맞춘 값이다.
- 왼쪽 목록 폭 `130`, 열 간격 `10`, 항목 높이 `30`
- 오른쪽 섹션 헤더 높이 `28`, 본문 패딩 `10`, 얇은 `0.5` 논리 픽셀 외곽선
- `Segoe UI 13`, 섹션 제목 `14 SemiBold`
- 확인·취소 버튼 `92 × 26`, 버튼 간격 `10`
- 옵션 창 자체의 `ThemeMode=System`과 WPF Fluent 의미 기반 리소스를 사용해 밝은·어두운 테마를 자동으로 따른다.
- 선택 행과 섹션 헤더는 픽픽처럼 각진 중립 회색 면을 사용하며 WPF 기본 파란색·둥근 선택 표현을 사용하지 않는다.
- 왼쪽 목록과 오른쪽 페이지 호스트는 같은 Grid 행을 여백 차이 없이 채워 상단·하단 기준선과 높이를 일치시킨다.
- `SystemColors.WindowBrushKey`, `ControlBrushKey`, `WindowTextBrushKey`, `ControlTextBrushKey`는 Windows 다크 모드의 WPF Fluent 팔레트가 아니라 레거시 시스템 색상을 반환할 수 있으므로 사용자 정의 표면에 사용하지 않는다. WPF 공식 Fluent 테마의 `ApplicationBackgroundBrush`, `SolidBackgroundFillColorSecondaryBrush`, `ControlSolidFillColorDefaultBrush`, `ControlFillColorDefaultBrush`, `TextFillColorPrimaryBrush` 같은 의미 기반 리소스를 사용한다.
- 색상 페이지 하단의 `Reset to Defaults`는 작업 사본의 `Colors`만 `ColorSettings.Default`로 바꾸고 즉시 미리보기한다. 나머지 설정은 보존하며 최종 저장·취소는 기존 옵션 트랜잭션을 따른다.
- 색상 견본은 `24 × 24px`, 오른쪽 `Pick` 버튼은 `44 × 24px`로 맞춰 같은 높이와 짧은 동작 라벨을 사용한다.
- 색상 라벨은 `UI 개념: 실제 의미` 형식으로 표시하며 `5 Hours`와 `One Week`는 풀어 쓰고, 짧고 눈에 잘 들어오는 임계값·주기 표기 `10K`, `100K`, `15s`는 유지한다. 초기화까지 남은 시간과 OpenAI 서비스 상태도 결과가 무엇을 뜻하는지 직접 설명한다.
- 숫자 TextBox는 `ControlFillColorDefaultBrush`, `TextFillColorPrimaryBrush`, `SurfaceStrokeColorDefaultBrush`를 명시해 시스템 다크·라이트 테마 모두에서 값 대비를 보장한다. `CaretBrush`도 `TextFillColorPrimaryBrush`에 연결해 입력 위치가 같은 대비로 보이게 한다. 여섯 숫자 항목 모두 화면상의 단위 접미사를 간결하게 `px`로 표시하며 내부 값은 계속 물리 픽셀이다.
- Appearance 숫자 바인딩은 `UpdateSourceTrigger=LostFocus`를 사용한다. 키 입력 중의 빈 문자열·부분 숫자는 작업 사본으로 보내지 않고, 포커스 아웃 또는 Window `PreviewKeyDown`의 TextBox `Enter` 처리에서 해당 바인딩만 `UpdateSource()`해 검증·미리보기한다.
- Appearance 페이지 하단의 `Reset to Defaults`는 작업 사본의 `Appearance`만 `AppearanceSettings.Default`로 바꾸고 즉시 미리보기한다. 나머지 설정과 최종 저장·취소 트랜잭션은 그대로 유지한다.

`SettingsWindowTests.Picpick_reference_contract_uses_compact_settings_layout`이 위 크기·여백·타이포그래피 계약을 실제 WPF 창에서 검사한다. `Custom_settings_surfaces_use_the_active_fluent_theme_resources`는 Window 수준 `ThemeMode=System`, 사용자 정의 표면·글자의 Fluent 리소스와 선택 탭 명암을, `Navigation_and_page_content_share_the_same_vertical_bounds`는 좌우 실제 높이를 검사한다. `Colors_page_reset_button_restores_only_default_colors_and_previews_them`은 하단 버튼 배치, 색상 전용 초기화와 미리보기를 검사하고 `Colors_page_uses_button_height_swatches_and_compact_pick_buttons`는 견본·버튼의 크기와 `Pick` 라벨을 고정한다. `Colors_page_labels_explain_the_UI_concept_and_its_actual_meaning`은 의미 라벨을, `Appearance_text_boxes_use_visible_fluent_colors_and_show_their_values`와 `Appearance_size_units_use_the_short_px_label`은 입력값 대비와 여섯 단위 표기를 검사한다. `Appearance_text_boxes_use_the_theme_text_color_for_the_caret`은 caret 테마색을, `Appearance_text_edit_is_deferred_until_focus_leaves_the_field`와 `Appearance_text_edit_is_applied_when_enter_is_pressed`는 지연 적용 정책을 검사한다. `Appearance_page_reset_button_restores_only_default_appearance_and_previews_it`은 모양 전용 초기화와 나머지 작업 사본 보존을 검사한다. `CODEXHP_SETTINGS_VISUAL_HOLD_MS` 환경 변수는 이 테스트 창을 지정 시간 동안 유지해 ScreenProof 시각 비교에 사용할 수 있으며, 설정하지 않으면 테스트 실행 시간에 영향을 주지 않는다.

외부 MVVM 프레임워크 없이 작은 ViewModel과 명령 구현을 사용한다. 색상 선택은 `IColorPicker` 뒤의 Win32 `ChooseColorW`를 사용한다. `COLORREF`의 `0x00BBGGRR` 변환을 단위 테스트하고, owner HWND를 지정하며 확인 때만 작업 사본에 선택값을 적용한다.

### 9.3 옵션 트랜잭션

옵션 창을 열 때 저장된 설정의 작업 사본과 화면 복원 지점을 만든다.

- 색상·위치: 작업 사본 변경 즉시 화면에 미리보기
- 모양: 숫자 편집을 `Enter` 또는 포커스 아웃으로 확정하면 작업 사본 변경과 화면 미리보기를 즉시 수행
- 시작 프로그램·표시 조건: 미리보기하지 않고 `확인` 시 적용
- `확인`: 유효성 검사, 일반 옵션 적용과 설정 원자 저장을 하나의 사용자 작업으로 처리한 뒤 작업 사본 확정
- `취소`, 닫기 버튼, `Esc`: 모든 미리보기와 위치를 복원하고 저장하지 않음
- 다른 그룹으로 이동: `Overlay Position` 외곽선과 드래그 모드만 해제하며 작업 사본의 위치는 유지

시작 프로그램 등록이나 설정 저장 중 하나가 실패하면 가능한 변경을 이전 상태로 되돌리고 옵션 창을 닫지 않는다. 오류는 옵션 창에 표시하고 비밀값이 제거된 진단만 로그에 기록한다.

## 10. Windows 통합

### 10.1 DPI와 위치

- 애플리케이션 매니페스트를 Per-Monitor DPI Aware V2로 선언한다.
- WPF 트레이와 옵션 창은 Per-Monitor DPI Aware V2 동작을 유지하지만 Win32/GDI 사용량 오버레이의 크기와 좌표는 모두 물리 픽셀로 취급한다.
- 저장 위치는 모니터 식별자와 모니터 전체 영역 좌상단 기준 물리 X/Y 정수다.
- 모니터 DPI가 바뀌어도 사용량 오버레이 크기와 저장 좌표에 DPI 배율을 곱하거나 나누지 않는다.
- 저장된 모니터가 없으면 주 모니터의 제품 기본 위치를 사용한다.
- 제품 기본 위치는 `X = Monitor.Left + 2`, `Y = Monitor.Bottom - 12 - Height`다.
- 개발 비교 위치는 `X = Monitor.Left + 2`, `Y = Monitor.Bottom - 12 - (Height * 2)`다.
- 위치 계산은 `Shell_TrayWnd`를 조회하지 않으며 결과적으로 사용량 오버레이가 작업표시줄 위를 덮는 것을 허용한다.
- 화면이나 크기가 달라져 창이 벗어나면 모니터 작업 영역이 아니라 전체 영역 안쪽으로 보정한다.

### 10.2 ChatGPT 실행 감지

- 프로세스 이름은 `ChatGPT.exe`를 사용한다.
- 임의 프로그램의 같은 파일명을 오인하지 않도록 프로세스의 Windows 패키지 ID도 확인한다.
- 최초 호환 대상 패키지 패밀리는 현재 Windows 앱의 `OpenAI.Codex_2p2nqsd0c76g0`이다.
- 알려진 공식 패키지 ID는 한 호환성 모듈에 모아 이후 제품 이름 변경에 대응한다.
- `codex.exe`나 외부 Codex CLI 프로세스는 표시 조건에 사용하지 않는다.

### 10.3 전체화면 감지

- 활성 전경 창의 DWM 확장 프레임 경계와 해당 모니터 전체 경계를 비교한다.
- 최소화, 숨김, cloaked 창, Windows 셸과 CodexHp 자신의 창은 제외한다.
- 사용량 오버레이와 활성 전체화면 창이 같은 모니터에 있을 때만 사용량 오버레이를 숨긴다.
- 다른 모니터의 전체화면은 현재 사용량 오버레이에 영향을 주지 않는다.
- 전체화면이 끝나면 `항상 표시` 또는 ChatGPT 실행 여부에 따른 원래 조건으로 복원한다.

### 10.4 시작 프로그램

- 레지스트리 위치: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- 값 이름: `CodexHp`
- 값 데이터: 따옴표로 감싼 현재 설치 실행 파일 경로
- 앱을 단순 실행하는 것만으로 레지스트리를 변경하지 않는다.
- 설치 프로그램은 배포 마일스톤에서 기본 등록을 수행한다.
- 옵션 변경을 `확인`하면 선택 상태에 맞춰 사용자별 값을 추가, 갱신 또는 제거한다.

## 11. 설정과 진단 데이터

### 11.1 설정

- 파일: `%LOCALAPPDATA%\settings.json`
- 최상위 `schemaVersion`을 포함한다.
- 파일이 없으면 문서 기본값으로 새 설정 파일을 생성한다.
- 누락된 항목은 해당 기본값으로 보충한다.
- 범위를 벗어난 값은 해당 항목만 기본값으로 바꾼다.
- 저장은 같은 폴더의 임시 파일을 완전히 쓴 뒤 원본과 교체한다.
- 전체 JSON이 손상되면 원본을 타임스탬프가 있는 보존 이름으로 이동하고 기본값으로 복구한다.
- 현재 설정 스키마는 `3`이다. 모양 크기는 `overlayWidth`, `overlayHeight`로 저장한다.
- 스키마 `2`의 기존 `screenWidth`, `screenHeight` 키는 읽기 호환성으로만 유지하고, 다음 로드에서 값 손실 없이 새 키로 다시 저장한다.

저장 내용은 표시 조건, 색상, 모양과 모니터별 위치뿐이다. 인증과 사용량 응답은 저장하지 않는다.

### 11.2 진단 로그

- 폴더: `%LOCALAPPDATA%\Logs`
- 파일당 최대 1MB, 현재 파일을 포함해 최대 3개
- 기록: 시각, 심각도, 구성요소, 비밀이 제거된 오류 종류와 예외 요약
- 금지: 액세스·Refresh 토큰, 인증 헤더, 계정 ID, 원본 인증 JSON, 세션 대화 내용

별도 로깅 프레임워크 없이 작은 제한 로그 작성기를 사용한다.

## 12. 검증 전략

모든 기능과 버그 수정은 실패하는 테스트를 먼저 만든 뒤 최소 구현, 전체 테스트, 정리 순서로 진행한다.

### 12.1 단위 테스트

- 인증 경로 우선순위와 snake_case/camelCase 필드
- 사용량 창 식별, 남은 비율 제한과 초기화 시각
- Refresh 비율 경계값
- 15초 × 60 토큰 버킷, compaction과 초기 컨텍스트 처리
- 실제 관측값 `1K`, `3,677`, `5K`, `10K`, `20K`, `45,172`의 완화 로그 픽셀 높이와 0·최소 1px·상한 경계
- 낮음·높음 색상과 선형 보간
- 공급자별 오류가 최종 사용량 오버레이 상태에 미치는 영향
- 옵션 확인·취소와 미리보기 복원
- 설정 기본값, 부분 누락, 잘못된 값과 손상 복구
- 물리 픽셀 기반 모니터 위치 복원과 전체 화면 안쪽 보정
- 100%, 150%, 200% DPI에서 동일한 `288 × 68px` 창 크기
- 제품 기본 위치와 개발 비교 위치의 전체화면 좌표 공식
- 표시 조건과 같은 모니터 전체화면 우선순위

### 12.2 어댑터 계약 테스트

- 임시 `auth.json`과 JSONL 디렉터리
- 가짜 `HttpMessageHandler`의 정상, 인증 실패, 시간 초과와 변경된 응답
- 가짜 레지스트리 경계의 시작 프로그램 등록·해제
- 가짜 프로세스·창·모니터 정보의 ChatGPT와 전체화면 판정
- 로그 회전과 비밀값 미기록

실제 사용자 계정의 토큰이나 사용량 응답은 테스트 자산에 저장하거나 커밋하지 않는다.

### 12.3 Windows 11 실제 검증

- 최초 실행 즉시 트레이와 작업표시줄 위의 `288 × 68px` 사용량 오버레이 표시
- 실제 로그인 캐시를 통한 ManaBar/HpBar와 로컬 그래프 갱신
- 인증 부재 후 파일 생성 시 재시작 없는 복구
- 트레이 좌클릭, 우클릭 메뉴와 정상 종료
- 사용량 오버레이 더블클릭
- 색상·모양·위치 미리보기와 확인·취소
- 서로 다른 DPI의 다중 모니터 이동과 물리 크기를 유지한 재실행 복원
- 개발 비교 실행에서 V1 바로 위에 배치한 뒤 좌하단 화면 캡처로 외곽과 내부 도형 비교
- 같은 모니터와 다른 모니터의 전체화면 비교
- ChatGPT 실행 조건 전환
- 네트워크 차단과 복구, 장시간 실행 안정성

화면·창·렌더링 검증은 자동 테스트만으로 완료 처리하지 않고 Windows 11에서 실제 창과 스크린샷 증거를 확인한다.

### 12.4 물리 픽셀·이중 호스팅 구현 검증 기록

2026-08-15 검증 결과는 다음과 같다.

- `Scripts/Agent/Verify-Core.ps1`
  - restore, build, test, Release `win-x64` self-contained single-file publish 통과
  - Core `52개`, App `136개`, 합계 `188개` 테스트 통과
  - 빌드 경고 `0개`, 오류 `0개`
- `tests/Windows/Validate-PublishedApp.ps1`
  - 게시 폴더에 `CodexHp.exe` 하나만 있는지 확인
  - 제품 기본 물리 경계 `2,2080,288,68` 확인
  - 부모 `Shell_TrayWnd`, 스타일 `0x56000000`, 확장 스타일 `0x00080000`, WPF `HwndWrapper` 클래스 확인
  - 사용량 오버레이 내부 픽셀 `0x00443E3E`과 주변 작업표시줄 픽셀 `0x001C1C1C`의 차이를 확인해 실제 합성 출력 검증
  - 두 번째 프로세스가 단일 인스턴스 가드에서 종료되는지 확인
- `Scripts/Agent/Run-Development.ps1`
  - 기본 실행 인자 `--compare-v1` 전달 확인
  - 요청 위치 `2,2012,288,68`, 작업표시줄 부분 교차 스냅 뒤 실제 팝업 경계 `2,1996,288,68`
- ScreenProof 증거
  - 제품 기본 전체 화면과 좌하단 확대: `out/screenproof/latest-build-visible/`
  - 캡처와 `WindowFromPoint(150,2100)`에서 화면 픽셀과 CodexHp HWND 적중을 함께 확인
- 합성·수명주기 회귀
  - raw GDI DIB가 `Bgra32` 알파 0으로 해석돼 투명해지는 실패를 재현하고 `Bgr32` 변환 테스트 추가
  - 실제 화면 픽셀로 작업표시줄 자식, 데스크톱 팝업, 새 작업표시줄 자식 표면 왕복 검증
  - 옵션 창 X 닫기의 동기 `Close()` 재진입 예외를 재현하고 비동기 취소·닫기 테스트 추가
  - `UsageOverlayDragAcceptanceTests`에서 AT-DRAG-001~004를 실제 주 작업표시줄과 WPF HWND로 실행하고 부모·스타일·물리 사각형·`WindowFromPoint`·ManaBar 픽셀을 검사
  - Windows GUI 인수테스트 컬렉션은 병렬 실행을 끄고 다음 명령으로 독립 반복 가능

```powershell
dotnet test tests/CodexHp.App.Tests/CodexHp.App.Tests.csproj --filter FullyQualifiedName~UsageOverlayDragAcceptanceTests
```

- 최종 2026-08-15 검증
  - Core `55개`, App `147개`, 합계 `202개` 통과
  - 경고 `0개`, 오류 `0개`, self-contained single-file `CodexHp.exe` 게시
  - 게시 HWND `11210054`, 작업표시줄 HWND `131514`, 물리 사각형 `2,2080,288,68`, 실제 픽셀 차이와 단일 인스턴스 확인
  - Alt+Tab 회귀 게시 검증에서 HWND `396874`, 확장 스타일 `0x00080080`(`WS_EX_LAYERED | WS_EX_TOOLWINDOW`), `WS_EX_APPWINDOW` 제거와 단일 인스턴스를 확인했다. 당시 XnViewMP 전체화면이 사용량 오버레이를 의도대로 숨기고 있어 화면 픽셀 재검사는 생략하고 기존 픽셀 증거를 유지했다.
  - ScreenProof: `out/screenproof/drag-icon-final/`
  - 픽픽 옵션 창과 CodexHp 옵션 창을 같은 `650 × 590` 논리 크기, 같은 General 상태로 캡처해 바깥 여백, 왼쪽 목록 폭, 열 간격, 헤더 높이, 본문 외곽선과 하단 버튼 크기를 나란히 비교했다.
  - 옵션 창 디자인 ScreenProof: `out/screenproof/settings-redesign/reference-picpick-current/`, `out/screenproof/settings-redesign/after-codexhp/`

- 2026-08-16 옵션 창 다크 모드·높이 회귀 검증
  - Windows `AppsUseLightTheme=0`, `SystemUsesLightTheme=0` 환경에서 밝은 본문과 흰 글자가 충돌하는 실제 게시 실행본을 재현했다.
  - 원인은 사용자 정의 XAML이 `ThemeMode=System` 위에 레거시 `SystemColors` 배경·전경을 명시한 것이며, 오른쪽 페이지의 하단 `6` 여백이 좌우 단차의 원인이었다.
  - 변경 전 회귀 테스트는 Window 수준 `ThemeMode=System`과 Fluent 의미 기반 리소스 부재, 실제 높이 `498.5` 대 `492.5`로 실패했고, 의미 기반 테마 적용과 동일 높이 구현 뒤 Core `55개`, App `149개`, 합계 `204개`가 통과했다.
  - 실패 증거: `out/screenproof/settings-theme-diagnosis/actual-dark-system/`
  - 수정 렌더 증거: `out/screenproof/settings-theme-diagnosis/fixed-fluent-dark-system/`, `out/screenproof/settings-theme-diagnosis/final-fluent-dark-colors/`

- 2026-08-16 General 안내 제거·색상 기본값 복원 검증
  - 의미가 모호했던 General 하단 안내 문구를 제거했다.
  - Colors 하단에 `Reset to Defaults`를 추가하고 색상 7개만 `ColorSettings.Default`로 변경하는지, 나머지 작업 사본과 취소·확인 트랜잭션을 유지하는지 검사한다.
  - Core `55개`, App `151개`, 합계 `206개`가 통과했다.
  - ScreenProof 증거: `out/screenproof/settings-colors-reset/final/` (`print-window`, 경고 없음, blank 아님)

- 2026-08-16 색상 의미 라벨·Appearance 입력 대비 검증
  - 색상 7개 라벨을 `UI 개념: 실제 의미` 형식으로 바꾸고 5시간·주간 한도, 초기화 시간, OpenAI 상태와 15초 토큰 임계값을 명시했다.
  - Appearance TextBox의 불투명 흰 배경과 다크 테마 흰 글자 충돌을 재현하고 Fluent 입력 배경·전경·테두리 리소스로 수정했다. 크기 단위 표시는 `px`로 줄였다.
  - Core `55개`, App `154개`, 합계 `209개`가 통과했다.
  - ScreenProof 증거: `out/screenproof/settings-meaning-labels/colors-final/`, `out/screenproof/settings-meaning-labels/appearance-final/` (`print-window`, 경고 없음, blank 아님)

- 2026-08-16 색상 컨트롤·Appearance 초기화 검증
  - 색상 견본과 `Pick` 버튼의 높이를 `24px`로 맞추고 버튼 폭을 `44px`로 줄였다. 라벨은 `5 Hours`만 풀어 쓰고 `10K`, `100K`, `15s`는 유지하면서 상태 설명을 명확하게 했다.
  - Appearance의 여섯 숫자 입력 모두에 `px`를 표시하고, Appearance 값만 기본값으로 바꾸는 `Reset to Defaults`를 추가했다.
  - Core `55개`, App `156개`, 합계 `211개`가 통과했다.
  - ScreenProof 증거: `out/screenproof/settings-color-controls/final/`, `out/screenproof/settings-appearance-controls/final/` (`print-window`, 경고 없음, blank 아님)

- 2026-08-16 Appearance caret·편집 확정 시점 검증
  - 변경 전 여섯 TextBox의 `CaretBrush`가 모두 `null`이고 다크 테마 글자색은 `#FFFFFFFF`인 것을 회귀 테스트로 재현했다. 공통 스타일의 caret을 동적 `TextFillColorPrimaryBrush`에 연결했다.
  - 매 키 입력마다 정수 변환·설정 검증·전체 속성 재통지가 발생하던 바인딩을 `LostFocus`로 변경하고, TextBox에서 `Enter`를 누르면 해당 바인딩만 즉시 확정하도록 했다.
  - Core `55개`, App `159개`, 합계 `214개`가 통과했다.
  - ScreenProof 증거: `out/screenproof/settings-appearance-editing/final/` (`print-window`, 경고 없음, blank 아님, 첫 입력칸의 흰색 caret 확인)

- 2026-08-16 Usage Overlay 용어·설정 마이그레이션 검증
  - 표시 구성요소·위치·크기·사각형·렌더링 표면을 각각 `UsageOverlay…`, `OverlayPosition…`, `OverlayWidth`/`OverlayHeight`, `OverlayBounds`, `OverlaySurface…`로 통일했다.
  - 옵션 UI는 `Usage Overlay Width`, `Usage Overlay Height`, `Overlay Position`을 사용하며 이전 표시 영역 명칭이 노출되지 않는지 WPF 트리 테스트로 검사한다.
  - 설정 스키마 `2`의 기존 크기 키를 읽어 스키마 `3`의 `overlayWidth`, `overlayHeight`로 값 손실 없이 다시 저장하는 회귀 테스트를 추가했다.
  - Core `55개`, App `160개`, 합계 `215개`, 빌드 경고 `0개`, 오류 `0개`와 self-contained single-file 게시를 확인했다.
  - 게시 검증은 `OverlayWindowHandle=9570350`, `OverlayBounds=0,2078,288,68`, `OverlayPixel=0x00443E3E`, `TaskbarPixel=0x001C1C1C`, 단일 인스턴스를 확인했고 최신 게시본을 계속 실행 중으로 유지했다.

- 2026-08-16 Windows 11 전용 게시 크기 최적화 검증
  - 프로젝트와 회귀 테스트 TFM을 `net10.0-windows10.0.22000.0`으로 고정했다.
  - 느슨한 게시에서 `ko` 위성 리소스 디렉터리 하나만 남는 것을 파일 단위로 확인했다.
  - 단일 파일 압축과 불필요 위성 리소스 제외 후 `CodexHp.exe`는 `173,272,964` bytes (`165.25 MiB`)에서 `76,479,585` bytes (`72.94 MiB`)로 `92.31 MiB`, `55.86%` 감소했다.
  - Core `55개`, App `163개`, 합계 `218개`, 빌드 경고 `0개`, 오류 `0개`와 `100 MiB` 게시 상한을 통과했다.
  - 게시 검증은 `OverlayWindowHandle=46927154`, `OverlayBounds=0,2078,288,68`, `OverlayPixel=0x00443E3E`, `TaskbarPixel=0x001C1C1C`, 단일 인스턴스를 확인했고 최신 게시본을 계속 실행 중으로 유지했다.

- 2026-08-19 Windows Forms 제거 검증
  - `NotifyIcon`, `ContextMenuStrip`, `ColorDialog`를 각각 `Shell_NotifyIconW`, Win32 팝업 메뉴, `ChooseColorW`로 교체하고 `<UseWindowsForms>`를 제거했다.
  - 프로젝트와 App 소스 전체에 `UseWindowsForms=true` 또는 `System.Windows.Forms`가 다시 들어오면 실패하는 계약 테스트를 추가했다.
  - Core `55개`, App `176개`, 합계 `231개`, 빌드 경고 `0개`, 오류 `0개`와 `100 MiB` 게시 상한을 통과했다.
  - 실행 파일은 `76,479,585` bytes (`72.94 MiB`)에서 `68,373,702` bytes (`65.21 MiB`)로 `7.73 MiB`, `10.60%` 감소했다. 최초 `165.25 MiB`와 비교한 누적 절감은 `100.04 MiB`, `60.54%`다.
  - 게시 검증은 `OverlayWindowHandle=20843190`, `OverlayBounds=0,2078,288,68`, 실제 픽셀 차이와 단일 인스턴스를 확인했다.
  - Windows 11 실제 UI에서 `CodexHp` 알림 아이콘, 좌클릭 설정 창, 우클릭 네이티브 메뉴 HWND `#32768`, `Options` 선택과 Win32 색상 대화상자 HWND `#32770`을 확인했다.
  - ScreenProof 증거: `out/screenproof/winforms-removal/tray-overflow/`, `tray-left-click-settings/`, `tray-right-click-menu-published/`, `win32-color-picker-published/`.

- 2026-08-19 옵션 창 Alt+Tab 및 완화 로그 그래프 검증
  - 표시용 스타일 테스트가 `WS_EX_TOOLWINDOW` 제거와 `WS_EX_APPWINDOW` 설정을 고정하며, 오버레이의 기존 숨김 스타일 테스트는 그대로 통과했다.
  - 실제 게시 옵션 창 HWND `17108916`은 확장 스타일 `0x00040100`, `WS_EX_TOOLWINDOW=False`, `WS_EX_APPWINDOW=True`였다.
  - `TokenGraphHeightScaler`는 `10K` knee 대표값과 0·최소 1px·상한 경계를 검사한다. 렌더러 연결 RED는 기존 선형 결과 `4px`를 재현했고 C안 적용 후 `10px`로 통과했다.
  - 실제 최신 60개 버킷은 양수 `54개`, 최대 `19,574`, 양수 중앙값 `3,258`이었다. 게시 코드와 같은 레이아웃에서 최대 높이 `58px`, 중앙값 높이 `15px`, 전체 막대 계산 불일치 `0개`를 확인했다.
  - Core `70개`, App `178개`, 합계 `248개`, 빌드 경고 `0개`, 오류 `0개`, `68,374,070` bytes (`65.21 MiB`) 게시와 `100 MiB` 상한을 통과했다.
  - 게시본 PID `36772`, 오버레이 HWND `83103306`, 경계 `0,2078,288,68`, 실제 픽셀 차이와 단일 인스턴스를 확인했다.
  - ScreenProof 증거: `out/screenproof/alt-tab-soft-log/settings-alt-tab-published/`, `overlay-soft-log-published/`. 두 캡처 모두 경고가 없다.

- 2026-08-19 옵션 창 높이 15% 축소 검증
  - 논리 높이 `590 × 0.85 = 501.5`를 사용자 요청에 따라 가장 가까운 정수 `502`로 정했다. 폭 `650`, 내부 여백·행·버튼 크기는 유지했다.
  - 높이 계약의 첫 RED는 기존 `590`, 정수화 추가 RED는 중간값 `501.5`를 각각 검출했고 `502` 적용 후 `SettingsWindowTests` 18개가 통과했다.
  - 전체 Core `70개`, App `178개`, 합계 `248개`, 빌드 경고 `0개`, 오류 `0개`, `68,374,084` bytes (`65.21 MiB`) 게시를 다시 통과했다.
  - 실제 게시본 PID `39024`의 옵션 창 HWND `178195906`은 200% DPI에서 물리 경계 `1270,530,1300,1004`, 확장 스타일 `0x00040100`이었다.
  - UI Automation에서 `OK`와 `Cancel`의 물리 하단은 각각 `1501px`로 창 하단 `1534px` 안에 있었으며, 설정 창을 취소로 닫은 뒤 게시 프로세스가 계속 실행됨을 확인했다.
  - ScreenProof 증거: `out/screenproof/alt-tab-soft-log/settings-height-502-general/`, `settings-height-502-appearance/`. 두 캡처 모두 `print-window`, 경고 `0개`다.

- 2026-08-19 동적 그래프 시간 구간·Appearance 폭 검증
  - `TokenGraphViewport`가 그래프 왼쪽 `GaugePaneWidth + 4`, 오른쪽 `OverlayWidth - 6`, 막대 슬롯 `GraphBarWidth + GraphBarGap`으로 표시 가능한 15초 버킷 수를 계산한다.
  - `ApplicationCoordinator`는 토큰 활동을 폴링할 때마다 현재 Appearance를 읽어 계산된 버킷 수만큼 실제 `.codex` 로그를 집계한다. 기본값은 `89개`, `22분 15초`다.
  - Appearance 탭은 `Visible token history: N min N sec`를 마지막 숫자 항목 다음에 표시하고, 숫자 입력 열은 기존 `80px`의 60%인 `48px`를 사용한다.
  - 용량 계산 RED는 새 형식 부재로 실패했고, 수집 연결 RED는 `Expected [89, 41], Actual [60, 60]`, Appearance RED는 새 속성 부재를 각각 재현했다.
  - Core `73개`, App `182개`, 합계 `255개`, 빌드 경고 `0개`, 오류 `0개`, `68,374,576` bytes (`65.21 MiB`) 단일 파일 게시를 통과했다.
  - 게시본 PID `6604`, 오버레이 HWND `159189624`, 경계 `0,2078,288,68`, 실제 픽셀 차이와 단일 인스턴스를 확인했다.
  - ScreenProof 증거: `out/screenproof/dynamic-graph-history/appearance-48px/` (`print-window`, 물리 `1300 × 1004px`, 경고 `0개`, blank 아님).

- 2026-08-25 Codex JSONL 컨텍스트 압축 형식 호환
  - 현재 Codex 세션은 `compacted → world_state → turn_context → token_count → item_completed` 순서를 기록하며, 일부 세션에서 기존 종료 표식 `event_msg.payload.type = context_compacted`를 남기지 않는다.
  - 스캐너는 첫 압축 토큰 뒤의 `context_compacted`를 기존 형식 종료로 계속 처리한다. 다른 이벤트가 이어지면 첫 압축 토큰까지만 확정하고 압축 상태를 닫아 이후 일반 토큰이 균등 분배되는 것을 막는다.
  - 파일이 첫 압축 토큰에서 끝난 경우에도 미완료 압축 전체를 시간 구간에 분배하지 않고 해당 토큰 시점의 한 버킷에만 반영한다.
  - `CodexTokenUsageScannerTests`는 메타데이터를 사이에 둔 기존 형식, 종료 표식이 없는 현재 형식, 미완료 파일 종료를 각각 회귀 시나리오로 고정한다.

현재 V1 창이 실행 중이지 않아 V1과 V2를 같은 순간 한 화면에 담는 검증은 보류했다. 외곽·게이지·그래프 좌표는 V1 소스의 정수 GDI 계산과 사용자가 제공한 캡처를 기준으로 맞췄다.

## 13. 구현 순서 경계

1. Core 모델과 계산 테스트
2. 인증·사용량·JSONL·서비스 상태 어댑터
3. 설정·로그·시작 프로그램 어댑터
4. 사용량 오버레이와 렌더러
5. 트레이와 옵션 창
6. ChatGPT·전체화면·DPI·위치 통합
7. 실제 Windows 11 기능 검증과 요구사항 문서 갱신
8. 고정 제품 아이콘과 트레이 크기 검증
9. 이후 설치·에이전트용 설치·공개 저장소 마일스톤

상세 파일별 작업과 테스트 순서는 이 설계가 승인된 뒤 별도 구현 계획으로 작성한다.

## 14. 알려진 위험과 대응

| 위험 | 대응 |
| --- | --- |
| ChatGPT 인증 캐시 위치·형식 변경 | 경로와 파서를 한 구성요소에 격리하고 주기적으로 재확인 |
| 비공개 `wham/usage` 계약 변경 | 실제 형식을 복제한 계약 테스트와 부분 실패 상태 제공 |
| Windows 앱 패키지 이름 변경 | 공식 패키지 식별자 목록을 한 호환성 모듈에서 관리 |
| Codex JSONL 형식 변경 | V1 테스트 이식, 모르는 이벤트 무시, 그래프 실패 격리 |
| DPI 전환 시 창 이탈 | 물리 좌표를 유지하고 대상 모니터 전체 영역 안쪽으로 보정 |
| 최상위 창이 전체화면을 가림 | 활성 전체화면과 사용량 오버레이 모니터를 비교해 즉시 숨김 |
| 단일 파일 크기와 시작 지연 | self-contained 단일 파일 압축과 위성 리소스 제한을 적용하고 `100 MiB` 상한을 자동 검사한다. 트리밍은 사용하지 않는다. |

## 15. 범위 밖 또는 연기된 작업

- 초기화 티켓 조회, 표시와 사용
- 로그인 또는 OAuth 갱신
- Codex App Server와 Codex CLI 실행·번들
- OpenCode 지원
- Windows 11 이외 플랫폼 지원
- 런타임 아이콘 추출
- Inno Setup 설치 프로그램과 `Install-CodexHp.ps1`
- 공개 GitHub 저장소, 자동 업데이트와 릴리스 정책
