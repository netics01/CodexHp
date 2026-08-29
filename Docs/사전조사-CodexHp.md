# CodexHp 구현 가능성 사전조사

> 이 문서는 구현 전 선택지와 조사 근거를 보존한다. 이후 확정된 제품 요구사항은 `요구사항-CodexHp.md`를 정본으로 삼는다.

## 1. 문서 성격

- 상태: 공개 소스 조사와 구현 방향 결정 완료, 역사적 대안 검토 기록 유지
- 기준일: 2026-08-15
- 대상 환경: Windows 11
- 목적: CodexHp의 구현을 시작하기 전에 핵심 요구의 실현 가능성, 후보 접근법, 위험, 추가 검증 항목을 정리한다.
- 이 문서는 최종 설계서나 구현 계획서가 아니다. 현재 결정과 충돌하는 초기 권고는 조사 당시의 대안으로만 읽는다.

## 2. 조사 대상 요구

CodexHp는 다음 제품 경험을 목표로 한다.

- Windows 11만 지원한다.
- Codex만 지원하고 OpenCode 지원은 제거한다.
- Windhawk에 의존하지 않는다.
- CodexHp 자체는 배포 파일 하나인 `.exe`로 제공한다.
- 실행하면 트레이 아이콘과 사용량 그래프 오버레이가 함께 나타난다.
- 오버레이는 초기에는 화면 좌하단에 나타나며 드래그해서 위치를 바꿀 수 있다.
- ChatGPT 데스크톱 앱에서 Codex를 정상적으로 사용하는 사람이 별도 설정 없이 바로 사용량을 볼 수 있어야 한다.
- CodexHp는 로그인을 직접 진행하지 않으며, 인증이 아직 없으면 주기적으로 다시 확인한다.

여기서 가장 큰 불확실성은 UI가 아니라 **Codex 계정의 사용 한도 데이터를 안정적으로 얻는 방법**이다.

## 3. 현재 결론

CodexHp는 **구현 가능하며 V1 연결 방식을 유지하기로 결정했다.**

| 영역 | 가능성 | 현재 판단 |
| --- | --- | --- |
| 트레이 아이콘 | 높음 | Windows 데스크톱 기술로 일반적인 구현 범위다. |
| 독립 그래프 오버레이 | 높음 | Windhawk 없이 불투명한 최상위 독립 창으로 구현할 수 있다. |
| 드래그 이동과 위치 저장 | 높음 | 창 이동 API와 로컬 설정 파일로 구현할 수 있다. |
| 배포 파일 하나인 `.exe` | 높음 | .NET의 Windows 단일 파일 게시를 사용할 수 있다. 다만 런타임에 임시 추출이 생길 수 있다. |
| 로컬 Codex 활동 그래프 | 높음 | V1처럼 로컬 Codex JSONL 로그를 읽을 수 있다. |
| 세션/주간 한도와 초기화 시각 | 높음 | V1처럼 기존 Codex 인증 캐시와 `wham/usage`를 이용한다. |
| 사용량 초기화 티켓 잔여 개수 | 높음 | 조회 가능성만 확인했다. 같은 인증 정보로 `available_count`를 얻을 수 있지만 CodexHp에는 당분간 관련 기능을 구현하지 않는다. |
| ChatGPT 데스크톱 앱 정상 사용자의 무설정 연동 | 중간~높음 | 인증 캐시가 존재하면 즉시 동작하고, 없으면 로그인 없이 주기적으로 재확인한다. 실제 배포본 호환성 검증은 필요하다. |

채택된 방향은 다음과 같다.

1. 사용량 한도 조회는 V1의 `auth.json` 읽기와 `backend-api/wham/usage` 직접 호출을 유지한다.
2. 그래프의 짧은 시간 간격 활동량은 로컬 Codex JSONL 로그에서 계산한다.
3. Codex App Server와 Codex CLI에 의존하지 않는다.
4. CodexHp는 로그인이나 OAuth 갱신을 직접 시작하지 않는다.
5. 지원 사용자는 ChatGPT 데스크톱 앱과 Codex가 이미 온라인에서 정상 동작하는 사용자로 한정한다.

공개 소스 비교 결과 App Server도 한도 조회 시 같은 ChatGPT 백엔드와 같은 인증 정보를 사용했다. 근본적 차이는 데이터 접근 권한이 아니라 공식 인증 수명 관리와 응답 정규화 책임에 있었다. CodexHp는 설치 마찰과 외부 실행 파일 의존성을 줄이기 위해 이 책임 중 필요한 최소 범위만 자체적으로 다룬다.

## 4. `codex app-server`란 무엇인가

### 4.1 쉬운 설명

`codex app-server`는 인터넷에 별도로 설치하거나 가입하는 서버가 아니다. 설치된 Codex 명령줄 프로그램을 다음처럼 실행해서 띄우는 **로컬 보조 프로세스**다.

```text
CodexHp.exe
  └─ codex app-server
       ├─ Codex 로그인 상태와 토큰 관리
       ├─ ChatGPT Codex 사용 한도 조회
       └─ JSON 메시지로 결과 전달
```

CodexHp가 `codex app-server`를 숨김 상태의 자식 프로세스로 시작한 뒤 표준 입출력으로 JSON 메시지를 주고받는 형태다. OpenAI는 이 인터페이스를 Codex를 이용하는 풍부한 클라이언트용 공식 통합 지점으로 설명한다.

공식 문서에 정의된 이 조사와 직접 관련된 기능은 다음과 같다.

- `account/read`: 현재 로그인 계정을 읽는다.
- `account/login/start`: ChatGPT 브라우저 로그인 또는 장치 코드 로그인을 시작한다.
- `account/rateLimits/read`: Codex 사용 한도, 사용률, 윈도 길이, 초기화 시각을 읽는다.
- `account/usage/read`: 계정의 토큰 활동 요약과 일별 구간을 읽는다.
- `account/rateLimits/updated`: 한도 정보가 바뀌었을 때 알림을 받을 수 있다.

출처: [OpenAI Codex App Server 공식 문서](https://learn.chatgpt.com/docs/app-server)

### 4.2 App Server가 해결하는 것

App Server를 사용하면 CodexHp가 다음 민감한 책임을 직접 맡지 않아도 된다.

- `access_token`과 `refresh_token` 파일 직접 읽기
- 토큰 만료 확인과 갱신
- Windows 자격 증명 저장소와 파일 저장소의 차이 처리
- ChatGPT 로그인 브라우저 흐름 직접 구현
- 내부 사용량 HTTP 주소와 응답 형식 추측

Codex는 CLI와 IDE 확장 사이에서 로그인 캐시를 공유하며, 설정에 따라 인증 정보를 Windows의 `%USERPROFILE%\.codex\auth.json` 또는 운영체제 자격 증명 저장소에 보관한다. ChatGPT 로그인 세션의 토큰도 Codex가 사용하는 동안 자동으로 갱신한다. 이 동작을 CodexHp가 재구현하는 것보다 App Server에 맡기는 편이 안전하고 유지보수 가능성이 높다.

출처: [OpenAI Codex 인증 공식 문서](https://learn.chatgpt.com/docs/auth)

### 4.3 App Server가 해결하지 못하는 것

App Server는 다음 문제를 자동으로 없애지는 않는다.

- 사용자 PC에서 외부 프로그램이 실행할 수 있는 `codex` CLI를 찾는 문제
- Codex CLI 버전별 프로토콜 차이를 다루는 문제
- App Server 프로세스가 종료되거나 응답하지 않을 때 재시작하는 문제
- 오프라인 상태와 서비스 장애를 UI에서 구분하는 문제
- 15초 단위의 로컬 작업 그래프를 만드는 문제

특히 **Codex 데스크톱 앱 설치와 외부 실행 가능한 Codex CLI 설치는 동일하다고 단정할 수 없다.** 공식 Windows 앱 문서는 데스크톱 앱 설치법을 설명하지만, 제3자 앱이 데스크톱 앱 내부 App Server에 접속하거나 내부 `codex.exe`를 호출하는 계약은 제공하지 않는다.

출처: [Codex Windows 앱 공식 문서](https://learn.chatgpt.com/docs/windows/windows-app), [Codex CLI 공식 문서](https://learn.chatgpt.com/docs/codex/cli)

## 5. V1은 사용량을 어떻게 얻는가

### 5.1 V1 데이터 흐름

현재 ManaBar V1은 App Server를 사용하지 않는다.

```text
%USERPROFILE%\.codex\auth.json
  └─ access_token, refresh_token, account_id 읽기
       └─ https://chatgpt.com/backend-api/wham/usage 직접 호출
            └─ 세션/주간 사용률과 초기화 시각

%USERPROFILE%\.codex\sessions\...\*.jsonl
  └─ token_count 이벤트 집계
       └─ 짧은 구간 활동 그래프
```

확인한 주요 구현 파일은 다음과 같다.

- `ManaBar/src/ManaBar.Backend/OpenCodeCredentialLocator.cs`
- `ManaBar/src/ManaBar.Backend/OpenAiUsageClient.cs`
- `ManaBar/src/ManaBar.Backend/CodexTokenUsageScanner.cs`
- `ManaBar/src/ManaBar.Backend/Program.cs`

클래스 이름 일부에는 `OpenCode`가 남아 있지만 실제 인증 파일은 Codex의 `.codex/auth.json`이다.

### 5.2 V1 방식의 장점

- 별도 자식 프로세스 없이 HTTP 요청 하나로 한도를 얻는다.
- 이미 파일 기반 Codex 로그인이 되어 있으면 사용자 조작이 없다.
- 구현이 작고 동작 원리를 추적하기 쉽다.

### 5.3 V1 방식의 한계

- OpenAI가 공개 API로 보장하지 않는 `backend-api/wham/usage` 주소와 응답 구조에 의존한다.
- ManaBar가 원본 Bearer 토큰과 계정 ID를 직접 다룬다.
- V1은 `refresh_token`을 읽지만 직접 갱신하지 않는다.
- 토큰이 만료되면 Codex가 인증 파일을 다시 갱신해 줄 때까지 조회가 실패할 수 있다.
- Codex가 인증 정보를 Windows 자격 증명 저장소에 보관하면 단순한 `auth.json` 읽기가 작동하지 않는다.
- 데스크톱 앱의 인증 상태가 해당 파일과 항상 공유된다는 보장이 없다.

즉 V1 방식은 비공식 의존성과 보안 책임이 있지만, 지원 대상을 ChatGPT 데스크톱 앱과 Codex가 이미 정상 동작하는 사용자로 좁힌 뒤 CodexHp의 연결 방식으로 채택했다. 이 위험은 요구사항과 호환성 검증에서 명시적으로 관리한다.

### 5.4 사용량 초기화 티켓 조회 가능성

초기화 티켓의 잔여 개수는 조회할 수 있다. 현재 공개된 공식 Codex 소스는 ChatGPT 백엔드 스타일에서 다음 읽기 전용 주소를 사용한다.

```text
GET https://chatgpt.com/backend-api/wham/rate-limit-reset-credits
```

V1의 사용량 조회와 같은 Bearer 액세스 토큰 및 선택적 `ChatGPT-Account-Id` 헤더를 사용할 수 있다. 응답의 `available_count`가 남은 티켓 수의 정본이며, 서버가 세부 정보를 제공하면 각 티켓의 ID, 상태, 지급 시각, 만료 시각도 함께 받을 수 있다.

따라서 이 조회를 위해 App Server를 실행할 필요는 없다. 다만 `wham/usage`와 마찬가지로 공개 고객용 REST API가 아니라 ChatGPT 내부 백엔드 계약이므로 응답 변경 가능성을 감수해야 한다. 이 조사는 가능성 확인으로 종료하며 CodexHp는 해당 주소를 호출하지 않는다. 티켓의 조회, 화면 표시, 사용 기능은 모두 당분간 제품 범위에서 제외한다.

## 6. 검토 기록: App Server를 사용하면 어떻게 바뀌는가

이 장은 채택하지 않은 대안의 구조와 판단 근거를 보존한다. 현재 제품 요구사항은 App Server와 Codex CLI에 의존하지 않는다.

### 6.1 예상 사용자 흐름

#### 경우 A: 접근 가능한 Codex CLI가 있고 이미 로그인됨

1. 사용자가 `CodexHp.exe`를 실행한다.
2. CodexHp가 `codex` 실행 파일을 찾는다.
3. `codex app-server`를 숨김 자식 프로세스로 시작한다.
4. 초기화 후 `account/read`를 호출한다.
5. 기존 로그인 캐시가 확인되면 `account/rateLimits/read`를 호출한다.
6. 트레이 아이콘과 그래프에 사용량을 바로 표시한다.

이 경우에는 추가 로그인 없이 목표한 경험을 만들 수 있다.

#### 경우 B: Codex CLI는 있지만 로그인되지 않음

1. `account/read`로 로그아웃 상태를 확인한다.
2. `account/login/start`로 ChatGPT 로그인을 시작한다.
3. 기본 브라우저를 로그인 URL로 연다.
4. 로그인 완료 알림을 받은 뒤 한도를 조회한다.
5. 이후에는 Codex의 로그인 캐시를 재사용한다.

이 경우 사용자는 최초 한 번만 브라우저 로그인을 하면 된다.

#### 경우 C: Codex 데스크톱 앱만 설치되고 외부 CLI는 없음

이 경우가 현재 가장 중요한 미확정 영역이다. 조사 PC의 Microsoft Store 패키지에는 내부 `codex.exe`가 있었지만 다음 제약을 확인했다.

- 셸에서 사용할 수 있는 공식 App Execution Alias가 선언되어 있지 않았다.
- 패키지 내부 `codex.exe --version`과 `codex app-server --help` 직접 실행은 `Access denied`로 실패했다.

이는 2026-08-15 현재 조사 PC의 특정 설치 버전에 대한 관찰이며, 모든 배포 형태에 대한 공식 결론은 아니다. 본 작업 전에 다른 설치 경로와 독립 Codex CLI 설치 환경을 추가로 확인해야 한다.

### 6.2 내부 구성 변화

| 책임 | V1 | App Server 대안 경로 |
| --- | --- | --- |
| 로그인 상태 확인 | `auth.json` 존재와 토큰 필드 확인 | `account/read` 호출 |
| 최초 로그인 | ManaBar가 제공하지 않음 | `account/login/start` 후 브라우저 또는 장치 코드 로그인 |
| 인증 저장 | Codex 파일을 ManaBar가 직접 읽음 | Codex가 파일 또는 Windows 자격 증명 저장소 관리 |
| 토큰 갱신 | ManaBar는 하지 않음 | Codex가 처리 |
| 한도 조회 | 비공개 `wham/usage` 직접 호출 | `account/rateLimits/read` |
| 토큰 노출 | ManaBar 프로세스가 원본 토큰을 보유 | ManaBar는 계정/한도 결과만 수신 |
| 실패 복구 | 다음 폴링 때 파일과 HTTP 재시도 | 프로세스 재시작, 재초기화, 계정 상태 재확인 필요 |
| 외부 의존 | 로그인 파일과 내부 HTTP 계약 | 실행 가능한 호환 Codex CLI와 App Server 계약 |

### 6.3 그래프 데이터는 별도로 유지해야 한다

App Server의 `account/rateLimits/read`는 세션/주간 한도 표시에는 적합하다. `account/usage/read`는 일별 활동 요약에 유용할 수 있다. 그러나 V1처럼 최근 수 분의 토큰 증가를 촘촘히 보여주는 그래프에는 로컬 JSONL 로그가 더 직접적이다.

App Server를 선택했다면 후보 구조는 다음과 같았다.

```text
Codex App Server ── 세션/주간 한도, 초기화 시각, 로그인 상태
Codex JSONL 로그 ── 최근 로컬 토큰 활동 그래프
OpenAI 상태 API ── 필요성이 확정될 경우 서비스 상태
```

V2는 Codex만 지원하므로 V1의 OpenCode SQLite 스캐너와 `Microsoft.Data.Sqlite` 의존성은 제거할 수 있다.

## 7. App Server 외의 후보와 비교

| 후보 | 세션/주간 한도 | 로그인 경험 | 보안/안정성 | 판단 |
| --- | --- | --- | --- | --- |
| Codex App Server | 가능 | 캐시 재사용 또는 최초 1회 공식 로그인 | 원본 토큰을 직접 다루지 않으며 공식 통합 지점 | 조사 후보였으나 외부 실행 파일 의존성 때문에 미채택 |
| V1 방식: `auth.json` + `wham/usage` | 가능 | 파일 기반 캐시가 있으면 무조작 | 비공개 HTTP 계약, 토큰 직접 취급, 갱신 미지원 | 지원 사용자 범위를 좁힌 뒤 채택 |
| Codex 로컬 JSONL만 읽기 | 불가능 | 로그인 불필요 | 단순하고 로컬 한정 | 그래프용 보조 경로 |
| OpenAI Platform 조직 Usage API | 목적이 다름 | 조직 관리자 키 필요 | 공식 API지만 ChatGPT 구독 Codex 한도가 아님 | 대체 불가 |
| Codex UI/웹 화면 스크래핑 | 가능할 수 있음 | 화면 세션에 종속 | UI 변경과 브라우저 상태에 매우 취약 | 비권장 |
| CLI 출력 텍스트 파싱 | 제한적 | CLI 실행 필요 | 출력 형식 변경에 취약 | App Server보다 열등 |
| 기존 데스크톱 앱 내부 프로세스에 연결 | 현재 불명 | 이상적으로는 무조작 | 공개된 접속 계약을 찾지 못함 | 공식 지원 전에는 가정 금지 |
| Codex/App Server 코드를 V2에 내장 | 이론상 가능 | 자체 관리 가능 | 크기, 업데이트, 보안, 유지보수 부담이 큼 | 초기 범위에서 제외 |

OpenAI Platform의 조직 Usage API는 API 조직의 요청량과 비용을 집계하는 관리자용 인터페이스다. ChatGPT 구독에 포함된 Codex의 세션/주간 남은 한도를 표시하는 요구와 대상 계정 및 권한이 다르므로 App Server의 대체물이 아니다.

출처: [OpenAI 조직 Usage API 공식 문서](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [OpenAI Admin API Keys 공식 문서](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/admin_api_keys)

## 8. 단일 `.exe`와 외부 의존성의 의미

`.exe` 하나라는 요구는 두 가지로 나눠 판단해야 한다.

### 8.1 배포 파일 하나

CodexHp 자체는 .NET self-contained single-file 게시로 배포 파일을 하나로 만들 수 있다. 사용자 PC에 별도의 .NET 런타임 설치를 요구하지 않는 구성도 가능하다.

단, 일부 네이티브 라이브러리는 실행 시 임시 디렉터리에 추출될 수 있다. 따라서 요구가 “사용자가 받는 파일이 하나”인지, “실행 중에도 어떤 보조 파일도 생성하지 않음”인지 본 설계에서 구분해야 한다.

출처: [Microsoft .NET 단일 파일 배포 공식 문서](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

### 8.2 설치된 Codex를 외부 기능으로 이용

App Server 경로에서는 CodexHp 배포물이 한 파일이어도, PC에 외부 실행 가능한 Codex CLI가 있어야 한다. 이는 Windhawk처럼 ManaBar UI를 구성하는 런타임 의존성은 아니지만, 계정/한도 데이터를 제공하는 기능 의존성이다.

App Server 대안을 검토하던 당시에는 제품 문구를 다음 중 하나로 확정해야 했다.

- “Codex CLI가 설치된 사용자는 바로 사용 가능”
- “Codex 데스크톱 앱 또는 CLI가 설치된 사용자는 바로 사용 가능”
- “CodexHp가 필요한 공식 Codex CLI 설치를 안내한다”

이 선택지는 이후 폐기했다. 현재 설계는 App Server나 CLI 실행 파일을 사용하지 않고 ChatGPT 앱이 갱신하는 Codex 인증 캐시를 직접 읽는다.

## 9. 독립 오버레이 UI 구현 가능성

Windhawk 없이도 Windows 11에서 다음 구성이 가능하다.

- WPF의 테두리 없는 불투명 최상위 창으로 그래프를 그린다.
- 작업 표시줄 버튼은 숨기고 트레이 아이콘으로 옵션 열기와 종료 같은 명령을 제공한다.
- 옵션 창의 `위치 변경` 그룹이 선택된 동안에만 마우스 드래그로 위치를 옮긴다.
- 이동이 끝나면 모니터 식별자와 좌표를 로컬 설정에 저장한다.
- 다음 실행 시 저장 위치를 복원하되, 모니터가 사라졌으면 기본 좌하단으로 복귀한다.

WPF에는 비표준 최상위 창과 `DragMove`가 있고, Windows Forms의 `NotifyIcon`을 함께 사용할 수 있다.

출처: [Microsoft WPF 창 공식 문서](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/), [Window.DragMove 공식 문서](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.dragmove?view=windowsdesktop-10.0), [NotifyIcon 공식 문서](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0)

기술은 **.NET 10 + WPF**로 확정했다. WinUI 3도 가능하지만, unpackaged 단일 파일 게시와 종속성 추출이 더 복잡해 현재 목표에는 이점이 적다.

추가로 검증할 Windows 11 UI 항목은 다음과 같다.

- 멀티 모니터와 모니터 분리 후 위치 복구
- 서로 다른 배율의 모니터 사이 이동
- 작업 표시줄 위치와 자동 숨김 상태
- 전체 화면 앱 위에 표시할지 숨길지
- 클릭 가능한 이동 모드와 평소 클릭 통과 모드의 전환 방식
- 절전/로그오프/Explorer 재시작 뒤 트레이 아이콘 복구

## 10. 설치 상태별 목표 경험

| 사용자 상태 | 목표 경험 | 현재 가능성 |
| --- | --- | --- |
| ChatGPT 데스크톱 앱 + Codex 정상 로그인 | 실행 즉시 표시 | 목표 동작, 배포본 호환성 검증 필요 |
| ChatGPT 데스크톱 앱은 있으나 인증 캐시 없음 | 트레이에서 대기하며 주기적으로 자동 재확인 | 높음 |
| ChatGPT 데스크톱 앱 또는 Codex 미사용 | 로그인하지 않고 대기 상태 유지 | 높음 |
| Codex CLI만 설치 | 지원 범위 아님 | 의도적으로 제외 |
| 네트워크 일시 장애 | 마지막 성공 값과 오류 상태를 구분해서 표시 | 높음 |

최종 성공 기준은 ChatGPT 데스크톱 앱에서 Codex가 정상 동작하는 사용자가 CodexHp용 설정이나 로그인 없이 기본 기능을 사용할 수 있는 것이다.

## 11. 사전조사 계획과 종료 상태

이 단계에서는 제품 코드를 본격 구현하지 않고, 아래 질문을 작은 실험으로 닫는다.

### R0. V1 동작과 데이터 계약 확인 — 완료

- V1의 인증 파일, 내부 HTTP 호출, 로컬 로그 스캔 경로를 코드에서 추적했다.
- App Server 전환 시 달라지는 책임을 식별했다.

완료 기준: 이 문서의 5장과 6장에 근거와 차이가 기록되어 있어야 한다.

### R1. Windows Codex 설치 형태 조사 — 제품 경계 결정으로 종료

- Microsoft Store 데스크톱 앱 설치 상태를 기록한다.
- 공식 Codex CLI 설치 상태와 실행 파일 탐색 경로를 확인한다.
- 데스크톱 앱과 CLI의 로그인 캐시 공유 여부를 확인한다.
- 앱 업데이트 뒤에도 사용할 수 있는 공개 실행 경로인지 확인한다.

결정: Codex CLI와 App Server 실행 가능성은 제품 전제에서 제외하고 ChatGPT 데스크톱 앱 정상 사용자만 지원한다.

### R2. App Server 최소 통신 실험 — 공개 소스 비교로 대체 완료

- `codex app-server` 시작, `initialize`, `account/read`를 검증한다.
- 로그인된 상태에서 `account/rateLimits/read` 응답을 수집한다.
- 로그아웃 상태에서 `account/login/start`와 완료 알림을 검증한다.
- 프로세스 종료, 잘못된 메시지, 네트워크 장애 시 동작을 기록한다.
- 설치된 Codex 버전에 맞는 JSON Schema 생성과 호환 전략을 검토한다.

결과: 공식 App Server도 한도 조회 시 V1과 같은 ChatGPT 백엔드 경로와 같은 인증 정보를 사용하며, 차이는 인증 수명 관리와 응답 정규화 책임임을 확인했다. App Server 경로는 채택하지 않았다.

### R3. 표시 데이터 매핑 검증 — 구현 테스트로 이관

- `usedPercent`, `windowDurationMins`, `resetsAt`을 V1의 세션/주간 표시와 비교한다.
- 여러 rate-limit ID가 반환될 때 어떤 버킷을 표시할지 조사한다.
- `account/usage/read`와 로컬 JSONL 그래프의 용도를 분리한다.
- 다른 PC나 클라우드 작업이 로컬 그래프와 한도 값에 만드는 차이를 설명한다.

완료 기준: CodexHp 화면의 각 숫자와 그래프가 어느 데이터 원천에서 오는지 결정할 근거가 마련되어야 한다.

### R4. 단일 실행 파일 UI 기술 검증 — 구현 설계로 이관

- WPF 오버레이와 Windows Forms 트레이 아이콘을 최소 프로토타입으로 검증한다.
- 제한된 드래그, 위치 저장, DPI, 멀티 모니터와 마우스 입력 차단을 검증한다.
- self-contained single-file 게시 크기와 시작 시간을 측정한다.
- 실행 시 임시 추출 파일 유무와 위치를 확인한다.

완료 기준: Windows 11에서 독립 `.exe`가 V1과 비슷한 표시를 안정적으로 유지할 수 있음을 확인한다.

### R5. 제품 경계 결정 — 완료

사용자 결정으로 다음 경계를 확정했다.

- Codex CLI는 지원 조건도 의존성도 아니다.
- V1 직접 조회 방식을 주 연결 경로로 사용한다.
- 로그인은 직접 수행하지 않고 인증 캐시를 주기적으로 확인한다.
- Windhawk와 OpenCode를 제거한다.
- 서비스 상태 수직바를 유지한다.
- 표준 설치 프로그램과 에이전트 친화적 설치 경로를 제공한다.

완료 기준: 미확정 항목이 구현 중 임의 결정으로 넘어가지 않고, 선택지와 근거가 사용자에게 제시되어야 한다.

## 12. 현재 위험 목록

| 위험 | 영향 | 사전 대응 |
| --- | --- | --- |
| 데스크톱 앱의 인증 캐시 위치 또는 형식 변경 | 인증된 사용자에게도 사용량 조회 실패 | 보수적 파서, 주기적 재확인, 명확한 호환성 상태 표시 |
| 비공개 `wham/usage` 응답 변경 | Codex 업데이트 후 한도 조회 실패 | 샘플 기반 계약 테스트와 오류 표시 |
| 복수 rate-limit 버킷의 의미 변화 | 세션/주간 값을 잘못 표시 | 실제 응답과 공식 스키마 비교 |
| 로컬 JSONL 포맷 변경 | 그래프 중단 | V1 테스트 자산 재사용과 샘플 기반 파서 테스트 |
| 멀티 모니터/DPI 위치 오류 | 오버레이가 화면 밖으로 사라짐 | 좌표 정규화와 안전 복귀 규칙 실험 |
| 단일 파일 크기 또는 시작 지연 | 설치·실행 경험 저하 | framework-dependent와 self-contained 수치 비교 |

## 13. 채택된 구현 방향

조사와 사용자 결정을 반영한 구현 방향은 다음과 같다.

```text
CodexHp.exe
  ├─ Tray/Overlay UI: .NET 10 + WPF
  ├─ Usage provider: Codex 인증 캐시 + `wham/usage`
  ├─ Activity provider: 로컬 Codex JSONL
  ├─ Settings: 사용자별 로컬 JSON
  └─ Diagnostics: 토큰을 기록하지 않는 로컬 로그
```

핵심 원칙은 다음과 같다.

- CodexHp는 기존 Codex 인증 캐시를 읽되 토큰을 자체 저장소나 로그에 복제하지 않는다.
- 인증 캐시가 없으면 로그인 화면을 열지 않고 주기적으로 다시 확인한다.
- Codex CLI와 App Server에 의존하지 않는다.
- 한도 조회 실패와 로컬 그래프 조회 실패를 분리해, 가능한 정보는 계속 표시한다.
- V1의 모양과 사용 감각은 참고하되 Windhawk와 작업 표시줄 내부 주입 구조는 계승하지 않는다.

세부 제품 요구사항과 이후 인터뷰 결정은 `요구사항-CodexHp.md`를 따른다.

## 14. 조사 출처

### 공식 문서

- [OpenAI Codex App Server](https://learn.chatgpt.com/docs/app-server)
- [OpenAI Codex App Server README의 rateLimits/read 계약](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md#7-rate-limits-chatgpt)
- [OpenAI Codex backend-client 초기화 티켓 구현](https://github.com/openai/codex/blob/main/codex-rs/backend-client/src/client/rate_limit_resets.rs)
- [OpenAI Codex 인증](https://learn.chatgpt.com/docs/auth)
- [OpenAI Codex Windows 앱](https://learn.chatgpt.com/docs/windows/windows-app)
- [OpenAI Codex CLI](https://learn.chatgpt.com/docs/codex/cli)
- [OpenAI 조직 Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage)
- [OpenAI Admin API Keys](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/admin_api_keys)
- [Microsoft .NET 단일 파일 배포](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Microsoft WPF 창](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)
- [Microsoft Window.DragMove](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.dragmove?view=windowsdesktop-10.0)
- [Microsoft NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0)

### 저장소 근거

- `ManaBar/Docs/사용자가이드-ManaBar.md`
- `ManaBar/Docs/Agent-Development-ManaBar.md`
- `ManaBar/src/ManaBar.Backend/OpenCodeCredentialLocator.cs`
- `ManaBar/src/ManaBar.Backend/OpenAiUsageClient.cs`
- `ManaBar/src/ManaBar.Backend/CodexTokenUsageScanner.cs`
- `ManaBar/src/ManaBar.Backend/Program.cs`

## 15. 갱신 이력

| 날짜 | 내용 |
| --- | --- |
| 2026-08-15 | V1 코드 경로, App Server와 대안, Windows 독립 UI, 단일 파일 배포 가능성을 1차 조사했다. 데스크톱 앱 전용 설치 환경의 CLI 접근성은 미확정으로 분리했다. |
| 2026-08-15 | 공개 App Server 소스와 V1을 비교한 뒤 V1 직접 연결 유지, App Server·CLI 비의존, 로그인 미구현으로 결정을 갱신했다. 제품명을 CodexHp로 변경했다. |
| 2026-08-15 | 공개 Codex backend-client 소스에서 초기화 티켓의 잔여 개수와 세부 정보를 같은 인증 기반의 읽기 전용 HTTP 호출로 조회할 수 있음을 확인했다. |
| 2026-08-15 | 초기화 티켓은 가능성 조사만 보존하고 조회·표시·사용 기능을 당분간 구현하지 않기로 범위를 확정했다. |
