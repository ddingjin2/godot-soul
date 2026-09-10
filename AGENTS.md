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

이식이 Unity의 런타임 생성 구조를 그대로 옮겼으므로 **규칙 2·3을 대규모로 위반한다.**
`Scripts/UI/`의 두 파일과 `Scripts/Gameplay/`의 빌더·스포너가 화면과 세계를 코드로 만든다.
감사와 단계별 전환 계획은 [docs/migrations/scene-data/](docs/migrations/scene-data/)에 있다.

- **신규 구현과 변경하는 기능에는 규칙을 지금부터 적용한다.** 새 UI를 기존 파일 옆에
  코드로 더 만들지 않는다.
- 기존 미준수를 발견했다고 전체 리팩터링을 시작하지 않는다. 전환은 계획 문서의 단계를 따르고
  각 단계는 스위트 green으로 착지한다.

## 단위와 축 — 틀리면 조용히 망가진다

- **1 Unity 미터 = 100 픽셀** (`World.Ppu`). 변환은 경계에서 **한 번만** 한다 —
  각 `*Data.Load()` 안, 그리고 레이아웃은 `GameplaySceneDefaults.Create()`와
  `GameplayReadabilityDefaults`. 그 아래 코드는 다시 스케일하지 않는다.
- **+Y는 아래.** 중력은 `Velocity.Y`에 더하고 점프는 뺀다. "아래"는 더 큰 값이며
  수직 min/max 쌍은 부호가 바뀌는 동시에 순서가 뒤집힌다.
- 수치를 JSON으로 옮길 때 그 값이 미터인지 픽셀인지 반드시 확정한다. 틀리면 100배 어긋난다.

## 작업과 검증

- 개발 기준 branch는 `master`, 전환 작업은 `refactor/godot-scene-data`. 작업 시작 시 실제
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
