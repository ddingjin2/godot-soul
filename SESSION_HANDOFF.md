# 인수인계 — 2026-09-10 (3차)

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, Godot 제작 규칙 4개로의 전환도 **일정에 있던 단계는 전부
끝났다.**

| 축 | 상태 |
|---|---|
| 이식 자체 | 완료. 스위트 211 passed / 1 failed / 1 skipped (실패 1건은 이식 전부터 red였던 레이아웃 개수 어서션) |
| 규칙 3 — UI는 씬에 배치 | 완결 |
| 규칙 2 — 재사용 단위는 씬 | 완결. `GameplayBuildShim` 퇴역은 **하지 않기로 결정** — 아래 |
| 규칙 1 — 수치 데이터화 | 1차 **완결**(S1·S4~S10·S12·S13, 키 190개 / 파일 27개). 2차([PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md)) **K0 착지, K1부터 남음** |
| 규칙 4 — 일회성 테스트 격리 | 지켜지는 중 |

1차 정본 계획은 [docs/migrations/scene-data/PLAN.md](docs/migrations/scene-data/PLAN.md), 2차는
[PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md) — 결정 D1~D8 **전부 승인됨**, 단계 K0~K8.
감사 §3의 결정 19건 결과는 `AUDIT_NUMBERS.md` §3 첫머리 표에 있다.

## 브랜치

`master`가 이식 본체, `refactor/godot-scene-data`가 전환 브랜치. **병합하지 않았고 push한 적도
없다.** 원격 없음. 이번 세션 커밋 11개, 각 커밋이 풀 스위트 green으로 착지했다.

## 이번 세션에 착지한 것

- **S12** `DifficultyTuning.json` — 난이도·NG+ 승수 5개.
- **S9** 카메라 감각 4키 → `WorldTuning.json`. `cameraLookAhead`는 스케일 + Y 반전 둘 다 — probe로
  `-330` 확인.
- **S8** `CutsceneTuning.json` — 샷 4개의 키프레임 표 + 비트 7개. 카메라 트랙은 절대값이 아니라
  **휴식 크기의 비율**로 저장했다. 챕터 8(ortho 7.2)만 동작이 바뀐다 (`PORT_STATUS.md`).
  키프레임 표는 C# fallback이 없다 — 파일이 없으면 기존 missing-shot 경로.
- **S10** `Resources/Design/ReadabilityLayout.json` 46키. 감사가 예고한 `Readability.json` 재생성
  대신 형제 파일. 죽은 속성 4개 삭제. `WorldTuning.json`에 벽·포탈 3키.
- **S13** 결정 19건 기록, `GameplayEnemy2D` 삭제(씬·테스트·스포너 어디도 안 씀),
  `GameplaySceneDefaultsAsset`의 낡은 기본값 수정.
- **shim 퇴역 → 안 함.** 세어 보니 제품 코드의 `NewObject`는 의도된 일회성 2곳, `AddComponent`
  0곳, `EnsureComponent`는 씬 액터엔 아무것도 안 더하는 get-or-add. 퇴역할 빌더가 없다. 근거는
  `PLAN.md` "남겨둔 작은 빚"과 클래스 doc-comment. (2차 K7이 결국 축소한다 — 이 결정은 1차 기준.)
- **컴플라이언스 점검 → fix `0ef1d88`.** S10이 "씬 소유"라 적은 맥동 4값·바운스 2값은 씬이 authoring한
  적 없는 `[Export]` 기본값이었다. JSON으로. `ShortcutGate.openAlpha`도 tscn에 값 기록.
- **2차 계획 `PLAN_CLOSEOUT.md`** 작성·승인(`5c804f4`, `32584d2`). 잔여 측정값: Data 클래스 `[Export]`
  기본값 232, 아키타입 `?? 리터럴` 63, `Create`/`CreateBase` 리터럴 179, 컴포넌트 초기화값 약 25, 씬 거울
  약 42, 누락 시 처리 미정의.
- **K0 `be8fb52`.** 누락 = `PushError` + 부팅 중단. `Res.LoadJson(path, required)`, `.Shared` 3종 null,
  `GameplayTuningCatalog.IsComplete`, `GameplayBootstrap`이 확인. 파일 전부 있으면 행동 변화 없음.

## 다음에 할 일 — K1부터

**K1: 테스트를 fallback 경로에서 떼어낸다.** 이게 문이다 — K1 없이 K2~K4를 하면 스위트의 맨손 액터가 전부
`PushError`로 죽는다. 단독 작업, 최고 위험.

측정(2026-09-10, 착지 직전 갱신할 것):

| 테스트 파일 | `AddComponent<` | `new 아키타입` | `GameplayTuningDefaults.` |
|---|---|---|---|
| `GameplayBossAttackGrammarTests` | 7 | | |
| `UnityTestAgentPlayModeSmokeTests` | 6 | | |
| `GameplayChapterBossTests` | 5 | | |
| `GameplayHazardStripRegressionTests` | 5 | | |
| `GameplayDifficultyAndGateTravelTests` | 4 | | |
| `GameplayBossChapterSystemsTests` | 1 | | |
| `GameplayChapterProgressionRegressionTests` | 1 | | |
| `GameplayDifficultySaveMergeRegressionTests` | 1 | | |
| `P0CombatStabilityTests.Gameplay` | | 3 (`MeleeGrunt`, `WrathMiniBoss`×2) | 3 |
| `P0CombatStabilityTests.Systems` | | 2 (`LeapingAttacker`, `WrathMiniBoss`) | 3 (`SetTuningData` 1곳만 있음) |
| `GameplaySoulsPoisePauseTests` | | | 1 |
| `P0CombatStabilityTests` (본체) | `new Node2D {` 빌더 | | |

방법:
1. `new MeleeGrunt` 류 5곳 → 생성 직후 `SetTuningData(MeleeGruntData.Load())` (`EnemyTuningData.Load<T>`가
   실제 JSON을 읽고 스케일까지 한다). K2가 `tuningData` 없는 아키타입을 `_Ready`에서 죽이므로 **반드시**.
2. `GameplayTuningDefaults.CreateX` 소비 7곳 → 같은 `XData.Load()`. 그 뒤 `GameplayTuningDefaults.cs`의
   팩토리 4개 삭제(D3). `const` 4개(`CheckpointZoneRadius` 등)는 K5가 없앤다 — K1에서는 두되 테스트가
   그 상수를 어서션 값으로 쓰면 `WorldTuningData.Load()` 값으로 바꾼다.
3. `AddComponent<Health>()` 같은 컴포넌트 합성은 K1에서 그대로 둔다 — 그건 fallback이 아니라 조립이고
   K7이 `Tests/Framework`로 옮긴다. K1의 대상은 **숫자를 코드 사본에서 받는 경로**뿐이다.
4. 착지 조건: 스위트가 fallback을 **한 번도 안 탄다.** 증명은 rule-4 probe로 — 아키타입 `??` 우변과
   `GameplayTuningDefaults` 팩토리에 임시 `GD.PushError("FALLBACK")`를 심고 풀 스위트를 돌려 로그에 0건,
   그 다음 심은 줄을 전부 지운다. `Tests/`에 아무것도 남기지 않는다.
5. 어서션 숫자가 바뀌는 테스트가 나오면 그 테스트는 출하하지 않는 숫자를 검증하고 있었다는 뜻이다.
   출하 값으로 바꾸고 `PORT_STATUS.md`에 어느 테스트가 얼마나 어긋나 있었는지 적는다. 약화가 아니라
   발견이다.

K1이 green으로 착지하면 K2 ∥ K3 ∥ K4 (파일 서로소, 담당별 소유 목록 명시, `run-tests.ps1`은 한 명만).
K3는 **K3a 선행**: `SceneLayout*.json` 8파일에 `shortcutGatePosition`·`shortcutGateSize`·
`shortcutOpensFromRight`를 현재 기본값으로 추가 — 빼먹으면 챕터 7개의 숏컷 게이트가 크기 0.

그 밖에 남은 것은 전부 **사람의 판단이 필요한 것**이다.

1. **씬 S9 — 아레나를 챕터 셸에 배치.** 아레나를 씬에 넣으면 `SceneLayout_*.json`이 레이아웃
   정본 자리를 잃는다. 기획자 소유 데이터의 소유권 이동이라 **물어본 뒤에** 한다.
2. **`master`로의 병합 여부.** 커밋 34개가 전환 브랜치에만 있다.
3. ~~씬이 거울로 들고 있는 값~~ → D4로 결정됨(씬에서 제거). 2차 K6.

## 사람이 봐야 하는 것 — 자동 검증으로 못 잡는다

1. **타이틀 설정 패널.** 공유 Theme이 붙으면서 `CheckBox`가 테마 판 위에 그려진다. 의도한 변경이나
   **아무도 눈으로 본 적이 없다.**
2. **공격 예고 맥동 결함.** `AttackReadout` 안의 `GameplayTelegraphPulse`가 렌더러를 못 찾는다
   (이식 결함). 고치면 여섯 개가 눈에 띄게 뛰기 시작하므로 기록만 했다.
3. **챕터 8 보스 인트로.** S8 이후 7.2 → 6.336 푸시. 숫자상 12%로 같지만 눈으로 본 적 없다.
4. **재미 검증.** 여전히 안 했다.

## 이번 세션에서 물린 것 — 같은 데서 또 미끄러지지 말 것

- **`git add <paths>` 뒤 pathspec 없는 `git commit`은 인덱스 전체를 커밋한다.** 다음 단계용으로
  `git rm`해 둔 파일 삭제가 앞 단계 커밋에 딸려 들어가 **빌드 안 되는 커밋**이 생겼다. amend로
  되돌렸다. 커밋할 때는 `git commit -- <paths>`로 경로를 명시하거나 인덱스를 깨끗이 둔다.
- **병렬 에이전트 2명(S8 ∥ S9)은 파일 서로소로 잘 굴러갔다.** 규칙: 소유 파일 목록 명시, 남의 파일
  건드리지 않기, `run-tests.ps1`은 **한 명만** 쓰기(다른 한 명은 build + headless smoke만),
  풀 스위트는 코디네이터가 착지 시점에 한 번. `user://playerprefs.cfg` 공유 문제는 worktree를
  파도 그대로다 — `config/name`에서 경로가 나온다.
- **풀 스위트가 11분이다.** 병렬 편집으로 얻는 시간보다 검증 직렬화가 병목이다. 스위트가 Godot
  단계에 들어간 뒤(로그 첫 줄 `BUILD OK`)에는 소스를 편집해도 안전하지만 **빌드는 돌리지 않는다**
  — DLL을 Godot이 잡고 있다.
- **에이전트가 남긴 doc-comment 위치 실수 1건.** S9가 새 메서드를 기존 메서드의 `<summary>`와
  본체 사이에 끼워 넣어 summary가 두 개 쌓였다. 빌드는 통과한다. 병합 전 diff를 사람이 본다.

## 확인하지 않은 것

- 육안 UI 확인 전반. headless 실행은 구조 검증이지 화면 확인이 아니다.
- 배포. Windows export template이 없어 EXE를 만든 적이 없다. `export_presets.cfg`는 있다.
- 오디오. `AudioFeedback`은 겹치는 큐를 끊는다. 귀로 확인한 적 없다.
- `ShortcutGate.tscn`에 `openAlpha = 0.25`를 손으로 써 넣었다(`0ef1d88`). 헤드리스 스위트는
  통과했지만 에디터에서 열어 인스펙터에 값이 보이는지 확인한 적은 없다.
