# MyGame (Godot) 작업 규칙

이 저장소에서 작업하는 사람과 에이전트가 지키는 규칙이다. 코드베이스 지도와 함정은
[CLAUDE.md](CLAUDE.md)에, 왜 이런 모양인지는 [PORTING_GUIDE.md](PORTING_GUIDE.md)에 있다.

## 새 세션 시작

1. `README.md`의 **현재 상태**와 루트 `SESSION_HANDOFF.md`를 먼저 읽는다. 과거 문서의
   중간 진행 상태보다 최신 인수인계가 우선이다.
2. 실제 상태를 다시 조회한다 — `git status`, 현재 branch, 실행 중인 Godot 프로세스,
   `tools/build.ps1`. 문서에 적힌 수치를 확인 없이 인용하지 않는다.
3. 이미 끝난 작업을 자동으로 재실행하지 않는다.

## Godot 제작 규칙

전역 스킬 `godot-cli-control`이 정본이다
(`C:/Users/pshye/.claude/skills/godot-cli-control/SKILL.md`). 스킬이 없는 환경에서도 아래
네 규칙은 적용한다. 다른 Godot 스킬의 일반 예제 — 숫자 `@export` 기본값, 코드로 만드는 Theme,
TDD GREEN 단계의 하드코딩 허용 — 보다 이 규칙이 우선한다.

1. **수치 하드코딩 금지·데이터 에셋 사용.** 이 프로젝트의 정본은 `Resources/Design/*.json`이며,
   각 타입의 `Load()`가 읽는다. 새 수치는 JSON 필드를 먼저 만들고 단위·범위·누락 시 동작을
   정한 뒤 코드를 연결한다. `const`·Dictionary·기본 인자로 옮기는 것은 준수가 아니다.
   **`.tres`로 이중화하지 않는다** — JSON이 이미 정본이다.
2. **재사용 단위는 재사용 씬.** Godot의 프리팹은 `.tscn`/`PackedScene`이다. 노드 생성 코드
   복사나 분리된 템플릿 노드 `Duplicate()`는 재사용이 아니다.
3. **UI는 씬에 배치.** `Button.new()` 류의 런타임 조립 금지. 스크립트는 바인딩·상태 갱신·
   입력/signal·동작만 맡는다. 목록은 미리 만든 항목 씬을 `instantiate()`한다.
4. **일회성 테스트는 구현 중에만.** 임시 probe는 `Tests/` 밖 임시 영역에 두고 회귀 스위트에
   등록하지 않는다. 기존 회귀 테스트는 보존한다.

### 현재 준수 상태 — 숨기지 않고 기록한다

**2026-09-12 기준 네 규칙을 준수한다.** 이식이 옮겨 온 Unity의 런타임 생성 구조는 1차
([PLAN.md](docs/migrations/scene-data/PLAN.md))와 2차
([PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md), K0~K8) 전환으로 걷혔다. 남은 것은
전부 결정으로 남긴 것이고 그 문서의 §종결에 숫자와 이름이 있다:

- 규칙 1: 코드에 남은 숫자는 정렬 순서 20(D2), `CombatTuningData.Shared`를 읽는 초기화값 13(데이터 소스),
  테스트 드라이버의 진단 주기 2(D2). 디자인 파일이 빠지면 부팅이 멈추고 오류가 파일을 지명한다(D1).
  설정 없이 트리에 들어간 컴포넌트는 첫 프레임 끝에 오류를 내고 멈춘다(`Scripts/Core/TuningGuard.cs`).
- 규칙 2·3: 런타임 노드 생성은 면제 8곳뿐(콘텐츠 분기 3, `CutsceneRigMove`, `GameplayDebugSceneJump`,
  `PlayerController2D`의 deferred add, `AudioFeedback` 가드, `Phys2D` 쿼리 모양). `GameplayBuildShim`은
  조회 5멤버. 챕터 셸 8개는 `Scenes/World/GameplayShell.tscn`을 상속한다.
- 규칙 4: 영구 테스트 추가는 D6 완전성 1개. 임시 probe는 단계마다 커밋 전에 지웠다.

- **신규 구현과 변경하는 기능에도 같은 규칙.** 숫자는 JSON 필드부터, 재사용은 씬부터, UI는 `.tscn`부터.
  새 컴포넌트의 숫자 필드는 초기화값 없이 두고 `ApplyTuning`/`Configure`로 받으며 `_Ready`에서
  `TuningGuard`를 지연 호출한다(`StaminaSystem`이 본보기). 테스트가 맨손으로 만들면
  `Tests/Framework/PlayerFixture.cs`·`EnemyFixture.cs`로 채운다.
- 계획 밖 발견은 고치기 전에 [PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md) 검토
  이력에 적고 결정을 받는다. 씬 S9(아레나를 셸에 authoring)는 기획자 데이터의 소유권 결정이라 열려 있다.

## 단위와 축 — 틀리면 조용히 망가진다

- **1 Unity 미터 = 100 픽셀** (`World.Ppu`). 변환은 경계에서 **한 번만** 한다 —
  각 `*Data.Load()` 안, 그리고 레이아웃은 `GameplaySceneDefaults.Create()`와
  `GameplayReadabilityDefaults`. 그 아래 코드는 다시 스케일하지 않는다.
- **+Y는 아래.** 중력은 `Velocity.Y`에 더하고 점프는 뺀다. "아래"는 더 큰 값이며
  수직 min/max 쌍은 부호가 바뀌는 동시에 순서가 뒤집힌다.
- 수치를 JSON으로 옮길 때 그 값이 미터인지 픽셀인지 반드시 확정한다. 틀리면 100배 어긋난다.

## 작업과 검증

- 개발 기준 branch는 `master`(전환 브랜치 `refactor/godot-scene-data`는 2026-09-12 병합됨). 작업 시작 시 실제
  branch/HEAD/변경을 조회한다. **요청 없이 commit/push/branch 병합을 하지 않는다.**
- 변경 전 범위를 백업하고, 실제 사용자 저장 파일(`user://playerprefs.cfg`)을 테스트로
  덮어쓰지 않는다. 저장을 건드리는 스위트는 `[SetUp]`에서 지운다.
- 검증은 `tools/run-tests.ps1`. 필터를 쓸 때 `godot.ps1`을 직접 부르지 않는다 — PowerShell이
  `--` 뒤 인자를 잃는다.
- **실패를 합격으로 처리하지 않는다.** 어서션을 약화시켜 green을 만들지 않는다. 이식이 정말
  할 수 없는 것이면 red로 두고 이유를 적는다.
- 자동 검증 통과와 사람의 재미 검증을 구분해서 보고한다.
- 한국어를 PowerShell 파이프로 다른 프로세스에 넘기지 않는다. UTF-8 파일이나 직접 편집을 쓴다.

## 기록

- 동작이 바뀌면 `PORT_STATUS.md`에 이유와 함께 남긴다. 시그니처가 바뀌면
  `INTEGRATION_NOTES.md`.
- 검증을 하면 무엇을 어디까지 확인했고 **무엇을 확인하지 않았는지**를 함께 적는다. 남길 가치가
  있는 검증 기록은 `docs/`에, 굴러가는 작업 메모는 `docs/work/`에 둔다 (후자는 gitignore 대상).
- 세션을 끝낼 때 루트 `SESSION_HANDOFF.md`를 갱신한다.
- 채택하지 않은 제안은 지우지 말고 검토 이력으로 남긴다.
