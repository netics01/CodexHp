# CodexHp 드래그 호스팅 인수테스트 결과

## 목적

이 문서는 실제 Windows 11 작업표시줄 안과 밖을 오가는 CodexHp 위치 변경 인수테스트의 실행 결과와 실패 수정 루프를 기록한다.

## 요약

| 항목 | 결과 |
| --- | --- |
| 대상 | CodexHp 사용량 오버레이 드래그 호스팅 |
| 실행 일시 | 2026-08-15 23:35 KST |
| 구현 기준 | 드래그 안정화와 작업표시줄 경계 면적 과반·수직 중앙 스냅 |
| 전체 결과 | AT-DRAG-001~004 모두 통과 |

## 코드 기반 검증

| 검증 | 결과 | 증거 |
| --- | --- | --- |
| 드래그 인수테스트 2회 반복 | 통과 | 각 실행에서 4개 모두 통과 |
| 전체 솔루션 테스트 | 통과 | Core 55개, App 146개, 합계 201개 |
| 빌드 | 통과 | 경고 0개, 오류 0개 |
| 단일 파일 게시 | 통과 | `out/win-x64/CodexHp.exe` 한 개 |
| 게시 실행 검증 | 통과 | HWND 25362450, 작업표시줄 부모 131514, 물리 사각형 `2,2082,288,68`, 확장 스타일 `0x00080080`과 단일 인스턴스 확인. 현재 데스크톱 상태와 독립적인 이전 실제 픽셀 증거도 유지 |

## 인수테스트 실행 결과

| ID | 결과 | 증거 | 실패 또는 미검증 사유 | 후속 조치 |
| --- | --- | --- | --- | --- |
| AT-DRAG-001 | 통과 | child 시작→outside popup, 부모 0, `WS_POPUP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, 정확한 물리 사각형, 최상단 적중 HWND와 ManaBar 픽셀 | 없음 | 상시 회귀 테스트 유지 |
| AT-DRAG-002 | 통과 | outside popup→taskbar child, 새 HWND, 실제 작업표시줄 부모, `WS_CHILD`, 최상단 적중 HWND와 ManaBar 픽셀 | 없음 | 상시 회귀 테스트 유지 |
| AT-DRAG-003 | 통과 | taskbar child→경계 과반 위치, 새 child HWND와 작업표시줄 수직 중앙 스냅 사각형, 최상단 적중 HWND와 ManaBar 픽셀 | 없음 | 상시 회귀 테스트 유지 |
| AT-DRAG-004 | 통과 | outside popup→같은 경계 과반 위치, 새 child HWND와 동일한 수직 중앙 스냅 사각형, 최상단 적중 HWND와 ManaBar 픽셀 | 없음 | 상시 회귀 테스트 유지 |

## 실패 수정 루프

| 차수 | 실패 테스트 | 원인 분류 | 수정 내용 | 재검증 결과 |
| --- | --- | --- | --- | --- |
| 1 | AT-DRAG-001 | 구현 결함 | 작업표시줄에서 분리한 뒤 이미 owner가 0인 팝업에 `GWLP_HWNDPARENT=0`을 다시 적용해 `ERROR_INVALID_WINDOW_HANDLE(1400)`가 발생했다. owner가 남은 경우에만 제거하도록 수정했다. | AT-DRAG-001~004 4개가 2회 연속 통과 |
| 2 | Core 경계 스냅 테스트 | 요구 변경 | 후보 좌표까지의 이동거리 비교와 하단 2px 앵커를 작업표시줄 점유 면적 비교와 수직 중앙 앵커로 교체했다. | 50% 전후 Core 테스트와 AT-DRAG-001~004 통과 |

## 증거 위치

| 증거 | 경로 또는 요약 |
| --- | --- |
| Windows GUI 캡처 | `out/screenproof/drag-icon-final/screenshot.png` |
| 캡처 메타데이터 | `out/screenproof/drag-icon-final/meta.json` |
| 자동 인수테스트 | `tests/CodexHp.App.Tests/Presentation/UsageOverlayDragAcceptanceTests.cs` |
| 게시 산출물 | `out/win-x64/CodexHp.exe` |

## 최종 판정

- 통과한 인수테스트: AT-DRAG-001, AT-DRAG-002, AT-DRAG-003, AT-DRAG-004
- 실패한 인수테스트: 없음
- 실행하지 못한 인수테스트: 실제 마우스 커서 이동 자동화는 사용자 입력 방해와 Windows 시스템 이동 루프의 타이밍 불안정 때문에 제외했다. 생산 코드의 동일한 드래그 분리·완료 경로를 실제 HWND로 실행했다.
- 남은 위험: 저장소 지침에 따라 다른 에이전트를 사용하지 않아 Acceptance-Test-Plan의 독립 Review Loop를 수행하지 못했다.
- 사용자 확인 필요 항목: 없음
