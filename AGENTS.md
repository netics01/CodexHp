# CodexHp 저장소 운영명령

## 릴리즈

사용자가 `릴리즈` 또는 `릴리스`를 요청하면 해당 버전을 GitHub Release로 게시한다. 이 요청은 릴리즈 대상 커밋을 `origin/main`에 푸시하고 해당 버전 태그를 푸시하라는 명시적 승인도 포함하므로 푸시 승인을 다시 묻지 않는다.

1. 앱과 설치 프로그램의 버전 및 `CHANGELOG.md`를 확인하고 릴리즈 준비 변경을 커밋한다.
2. 로컬 `main`이 릴리즈 대상 커밋만큼 앞서 있으면 `git push origin main`으로 푸시한다. 작업 트리가 깨끗하고 `main`과 `origin/main`이 같은 커밋을 가리키는 상태에서 공식 스크립트를 실행한다.
3. 저장소 루트에서 `pwsh -NoProfile -File .\scripts\Publish-LocalRelease.ps1 -AllowUnsignedRelease`를 실행한다. 스크립트가 공식 빌드와 테스트, 태그 푸시, GitHub Release 게시를 수행하고, 게시한 파일을 다시 내려받아 SHA-256과 이 PC에서의 설치 및 실행을 검증한다.
