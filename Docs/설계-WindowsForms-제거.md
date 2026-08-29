# CodexHp Windows Forms 제거 설계

## 1. 목표

Windows 11 빌드 22000 이상에서 기존 사용자 동작을 유지하면서 `System.Windows.Forms` 참조를 완전히 제거한다. WPF 옵션 창과 사용량 오버레이 합성 표면은 유지하고, Windows Forms가 담당하던 세 기능만 운영체제 Win32 API로 교체해 self-contained 단일 실행 파일 크기를 줄인다.

## 2. 범위

- 트레이 아이콘: `NotifyIcon`을 숨은 WPF 메시지 HWND와 `Shell_NotifyIconW`로 교체한다.
- 트레이 메뉴: `ContextMenuStrip`과 `ToolStripMenuItem`을 `CreatePopupMenu`, `AppendMenuW`, `TrackPopupMenuEx`로 교체한다.
- 색상 선택: `ColorDialog`를 `ChooseColorW`로 교체한다.
- `UseWindowsForms` 프로젝트 속성과 모든 `System.Windows.Forms` 소스 참조를 제거한다.
- 트레이 좌클릭 옵션 열기, 우클릭 `Options`/`Exit`, Explorer 재시작 뒤 아이콘 복구, 색상 선택 확인·취소 동작을 유지한다.

## 3. 구성요소

### 3.1 `WindowsTrayIconView`

기존 `ITrayIconView` 계약과 `TrayIconController`는 유지한다. 구현 내부에 입력을 받지 않는 `HwndSource`를 만들고 다음 메시지를 처리한다.

- 등록한 tray callback 메시지의 `WM_LBUTTONUP`: `MouseClicked(Left)` 발생.
- `WM_RBUTTONUP`: `MouseClicked(Right)` 발생 뒤 현재 포인터 위치에서 네이티브 메뉴 표시.
- `TaskbarCreated`: Explorer 재시작으로 사라진 아이콘을 다시 등록.

아이콘 등록은 `NIF_MESSAGE | NIF_ICON | NIF_TIP`으로 수행한다. 종료 시 먼저 `NIM_DELETE`를 보내고 메시지 훅, HWND, 아이콘 순서로 폐기한다.

### 3.2 `TrayIconMessageRouter`

Win32 메시지와 메뉴 명령 ID를 기존 도메인 enum으로 바꾸는 순수 내부 구성요소다. 운영체제 UI 없이 단위 테스트하여 좌클릭·우클릭·기타 입력과 `Options`·`Exit` 순서를 고정한다.

### 3.3 `Win32ColorPicker`

`IColorPicker`는 owner HWND와 현재 `ColorValue`를 받아 선택된 값 또는 취소를 반환한다. `Win32ColorPicker`는 `CHOOSECOLORW`와 16개 사용자 정의 색상 버퍼를 관리한다. `COLORREF`는 `0x00BBGGRR` 형식으로 변환하며 확인 때만 ViewModel에 적용한다.

## 4. 오류와 수명주기

- 초기 트레이 아이콘 등록 실패는 영어 예외로 시작 실패 경로에 전달한다.
- 메뉴 선택 취소는 명령을 발생시키지 않는다.
- 색상 대화상자 취소와 대화상자 생성 실패는 현재 색상을 보존한다.
- `Dispose`는 여러 번 호출해도 안전하며 숨겨진 아이콘과 HWND를 남기지 않는다.
- 새 외부 패키지나 별도 프로세스를 추가하지 않는다.

## 5. 검증

- 프로젝트와 전체 App 소스에 Windows Forms 참조가 없음을 자동 검사한다.
- 메시지·명령 라우팅과 `COLORREF` 왕복을 단위 테스트한다.
- 기존 트레이·옵션 창 전체 회귀 테스트를 통과한다.
- `Verify-Core.ps1`로 `win-x64` self-contained single-file을 게시하고 이전 `72.94 MiB`와 비교한다.
- 게시본에서 트레이 아이콘, 좌클릭 옵션, 우클릭 메뉴, 사용량 오버레이, 단일 인스턴스를 Windows 11에서 검증한다.

## 6. 설계 자체 검토

- WPF 제거 또는 UI 재설계는 범위에 포함하지 않는다.
- 모든 기존 사용자 동작의 대응 구현과 검증 경로가 정의되어 있다.
- 미확정 항목이나 후속 구현을 전제로 하는 항목은 없다.
