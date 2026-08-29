# CodexHp

[English](README.md)

CodexHp는 Codex 사용량을 확인하는 Windows 11 데스크톱 오버레이입니다. 작업 표시줄 위에서 세션·주간 사용량 게이지, 최근 로컬 토큰 활동, OpenAI 서비스 상태 표시를 제공합니다.

> CodexHp는 독립적인 비공식 프로젝트이며 OpenAI와 제휴·보증·지원 관계가 없습니다.

## 현재 상태

이 저장소에는 애플리케이션 소스 코드가 있습니다. 현재 설치 프로그램, 릴리스 절차, 지원되는 바이너리 다운로드는 제공하지 않습니다.

## 요구 사항

- Windows 11 빌드 22000 이상(x64)
- 개발 빌드용 .NET 10 SDK
- 설치 및 로그인되어 있고 Codex를 사용할 수 있는 ChatGPT 데스크톱 앱

CodexHp는 ChatGPT 데스크톱 앱의 Codex 환경을 대상으로 합니다. 다른 운영 체제, OpenCode, 일반 ChatGPT 대화 사용량은 지원하지 않습니다.

## 주요 기능

- 남은 세션·주간 Codex 사용량과 초기화 진행 상태 표시
- 최근 로컬 Codex 토큰 활동을 작은 그래프로 표시
- 알려진 OpenAI 서비스 문제 표시 및 같은 모니터의 전체 화면 앱 감지 시 오버레이 숨김
- 모양, 위치, 표시 조건, 시작 프로그램 동작을 설정하는 트레이 아이콘과 설정 창 제공

오버레이를 두 번 클릭하거나 트레이 아이콘을 클릭하면 설정 창을 엽니다. 사용량 데이터를 아직 가져오지 못한 상태에서도 앱은 일반적으로 알림 영역과 오버레이를 유지합니다.

## 데이터와 개인정보

CodexHp는 기존 Codex 인증 캐시 `%CODEX_HOME%\auth.json` 또는 `%USERPROFILE%\.codex\auth.json`과 로컬 Codex 활동 데이터를 읽어 사용량을 표시합니다. 표시를 위해 필요한 사용량 요청에만 기존 인증 토큰을 전송합니다.

CodexHp는 로그인 과정을 수행하지 않으며, 인증 토큰을 자체 설정에 저장하거나 의도적으로 로그에 기록하지 않습니다. 사용량 엔드포인트와 로컬 데이터 형식은 공개 호환성 계약이 아니므로 예고 없이 바뀔 수 있으며, 이 경우 앱이 동작하지 않을 수 있습니다. 계정과 함께 사용하기 전에 소스 코드를 검토하세요.

## 개발 및 검증

개발 빌드를 실행합니다.

```powershell
pwsh -NoProfile -File .\Scripts\Run-Development.ps1
```

빌드·테스트·자체 포함 단일 파일 `win-x64` 게시를 실행합니다.

```powershell
pwsh -NoProfile -File .\Scripts\Verify-Core.ps1
```

로컬 게시 결과는 `out\win-x64\CodexHp.exe`에 생성됩니다. `out` 디렉터리는 의도적으로 추적하지 않습니다.

## 라이선스

[Apache License, Version 2.0](LICENSE)로 배포됩니다.
