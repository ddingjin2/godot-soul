# 인수인계 — 2026-09-10

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, 옆 프로젝트(`C:\dev\project-godot`) 수준의 품질로
끌어올리는 작업이 **진행 중**이다.

- 이식 자체: 완료. C# 221파일, 스위트 206 passed / 1 failed / 1 skipped.
  실패 1건은 이식 결함이 아니다 (`Tests/README.md` 참고).
- 저장소 위생: 완료. `.gitattributes`·`.editorconfig`·`README`·`AGENTS.md`·
  `export_presets.cfg`·`tools/README.md`.
- 로컬라이제이션: 완료. 코드에 박혀 있던 한글 34개가 `localization/ui.csv`(ko/en)로.
- 제작 규칙 감사: 완료. 위반 씬/UI 61건, 수치 약 530건, **실제 결함 5건**.
- 씬·데이터 전환: **시작함.** Stage 1(컷씬 오버레이) 착지. 나머지는 계획 문서의 순서대로.

정본 계획은 [docs/migrations/scene-data/PLAN.md](docs/migrations/scene-data/PLAN.md)다.
감사 근거는 같은 폴더의 `AUDIT_SCENES_UI.md`, `AUDIT_NUMBERS.md`.

## 브랜치

`master`가 이식 본체, `refactor/godot-scene-data`가 전환 작업 브랜치다. **아직 병합하지
않았고 push한 적도 없다.** 원격은 없다.

## 다음에 할 일

계획 문서의 순서를 그대로 따른다. 남은 것 중 가장 앞:

1. **결함 D4·D5** — 템플릿 `Duplicate()`가 만든 `ProcessMode` 각인과 중복 투사체 빌더.
   Stage 7과 같은 작업이라 함께 처리된다.
2. **Stage 2 → 3 → 4** — HUD 낱개 컴포넌트와 `MenuTheme.tres`, HUD 패널, 타이틀 화면.
   S2가 S3·S4보다 먼저다 (둘 다 Theme을 쓴다).
3. **Stage 5 → 6** — 공용 마커가 아레나 지오메트리보다 먼저다.
4. **Stage 8(액터)** 가 마지막이자 가장 위험하다. 문서화된 `_Ready`/순서 함정 다섯 중 넷을
   한 번에 안는다.
5. **Stage 9(아레나 authoring)는 일정에 넣지 않는다** — 기획자 소유 레이아웃 데이터의
   소유권이 옮겨가는 문제라 제품 판단이 필요하다. 물어본 뒤에 한다.

수치 쪽은 `AUDIT_NUMBERS.md`의 S4 이후가 남았고 전부 추가적이다. 예외는 S10 하나.

## 이번 세션에서 물린 것 — 같은 데서 또 미끄러지지 말 것

- **`run-tests.ps1`이 PowerShell 5.1에서 죽었다.** 네이티브 stderr 한 줄이 `ErrorRecord`가
  되고 `ErrorActionPreference = 'Stop'`이 그걸 치명적 오류로 만든다. 고쳤지만, 새 스크립트를
  쓸 때 같은 함정이 있다.
- **읽기 전용 감사 에이전트가 병렬로 돌던 다른 에이전트의 정당한 작업을 침범으로 오인해
  되돌렸다.** 결과물은 보존돼 있어 복구했지만 10분 넘게 날렸다. 병렬로 돌릴 때는 각 담당에게
  **소유 파일 목록을 명시**하고, 감사 담당에게는 "네 것이 아닌 변경을 보면 보고만 하고 손대지
  마라"를 못박는다.
- **공유 빌드가 red면 다른 에이전트가 막힌다.** 한 에이전트가 시그니처를 바꾸는 중간 상태를
  트리에 남기면 나머지가 전부 검증을 못 한다.

## 확인하지 않은 것

- 사람이 직접 플레이한 재미 검증. 안 했다.
- 육안 UI 확인. headless 실행은 구조 검증이지 화면 확인이 아니다.
- 배포. Windows export template이 없어 EXE를 만든 적이 없다.
- Stage 1의 씬이 **에디터에서** 의도대로 보이는지. import와 테스트는 통과했지만 사람이 본 적은
  없다. `.tscn` 안의 주석은 에디터에서 한 번 저장하면 사라진다 (형식이 주석을 보존하지 않음).
