# 인수인계 — 2026-09-12 (5차)

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, Godot 제작 규칙 4개로의 전환도 **일정에 있던 단계는 전부
끝났다.** 2차(완전 준수) 계획은 K0~K4가 착지했고 K5~K8이 남았다.

| 축 | 상태 |
|---|---|
| 이식 자체 | 완료. 스위트 210 passed / 1 failed / 1 skipped (실패 1건은 이식 전부터 red였던 레이아웃 개수 어서션; K4가 identity 2개를 완전성 1개로 바꿔 총 212건) |
| 규칙 3 — UI는 씬에 배치 | 완결 |
| 규칙 2 — 재사용 단위는 씬 | 완결. `GameplayBuildShim` 퇴역은 **하지 않기로 결정** — 2차 K7이 축소한다 |
| 규칙 1 — 수치 데이터화 | 1차 **완결**. 2차([PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md)) **K0~K4 착지, K5 ∥ K6부터 남음**. 코드 fallback: 아키타입 0, Data 클래스 `[Export]` 초기화값 1(null 가드), 레이아웃·가독성 리터럴 0(정렬 순서 20은 D2 면제) |
| 규칙 4 — 일회성 테스트 격리 | 지켜지는 중. D6 완전성 테스트 1개 추가(승인된 영구 테스트) |

1차 정본 계획은 [docs/migrations/scene-data/PLAN.md](docs/migrations/scene-data/PLAN.md), 2차는
[PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md) — 결정 D1~D8 **전부 승인됨**, 단계 K0~K8.
감사 §3의 결정 19건 결과는 `AUDIT_NUMBERS.md` §3 첫머리 표에 있다.

## 브랜치

`master`가 이식 본체, `refactor/godot-scene-data`가 전환 브랜치. **병합하지 않았고 push한 적도
없다.** 원격 없음. 이번 세션(09-11~12) 커밋 11개: K1 2, K2~K4 7, 문서 2. 풀 스위트는 K1 착지 시점,
K2~K4 병합 트리, 최종 트리에서 각 1회 — 아래 "검증" 참조.

## 착지한 것 — 이번 세션(09-11~12)

- **K1 `5912697`.** 스위트를 fallback에서 뗐다. 맨손 아키타입 5곳 `SetTuningData(XData.Load())`, 팩토리
  소비 6곳 `XData.Load()`, 스테인 유예 1곳 `PlayerResourceData.Load()`. 팩토리 4개 삭제, 스포너는
  `catalog.X`. 어서션 변화 0. 증명: 아키타입 `_Ready` 4곳 + 챕터 보스 `PulseTelegraph`에 임시
  `PushError("FALLBACK")`, 풀 스위트 0건, 제거.
- **K2 `3b1605d`.** 아키타입 4개의 `??` 67곳(감사 63 + `TelegraphBase` 색 4)·삼항 39곳·null 조기 반환 2곳
  제거, 챕터 보스 `PulseTelegraph` 2곳. `tuningData` 없이 트리 진입 → `PushError` + `SetProcess(false)`
  + `SetPhysicsProcess(false)` + return(`_health` 배선 전).
- **K3a `7ac5ee7`.** `SceneLayout*.json` 8파일에 숏컷 게이트 3키. **측정 결과 `hasShortcutGate: true`는
  챕터 2뿐** — 지난 인수인계의 "7개 게이트 크기 0" 경고는 과장이었고, 실제로 기본값에서 읽히던 키는
  챕터 2의 `shortcutOpensFromRight` 하나.
- **K3 `5c24413`.** 17개 Data 클래스 `[Export]` 초기화값 309 → 1(`RainbowChapterBossData.attacks =
  Array.Empty`, null 가드). JSON 추가 22 + 360, 전부 기존 기본값. `ProgressionTuningData.Load` 누락 시
  null, `PlayerProgression`은 캡 처리 + `PushError` 1줄. 챕터 보스 테스트 fixture 6파일에 빠진 키 보충
  (`stunDuration`·`bodySize`·poise 블록·`telegraphPulse*`), 기대값 변화 0.
- **K3 후속 `766b448`.** 기본값 0인 스위치 키 4개(`approachHopInterval`·`chantInterval`·`afterimageCount`·
  `stanceRotationInterval`)를 챕터 보스 8파일에 명시적 0으로 25곳 추가 — D6 테스트가 "모든 필드"를
  요구하므로.
- **K4 `87c0245`.** `GameplaySceneDefaults.Create()` 92 리터럴 → 0(파일 적용만), `GameplayReadabilityDefaults.CreateBase()`
  217 → 정렬 순서 20(D2). `ReadabilityThemeWriter` + 독 버튼 삭제(D7). identity 테스트 2개 →
  `Tests/Unit/DesignFileCompletenessTests` 1개(D6): 파일→타입 표 전수, 누락 키를 이름으로 보고.
  부수 삭제: `GameplayReadabilityThemeData.CopyFrom`, `internal ToGodot(float,float)` 2개.
- **발견 `fd09e27`.** `P0CombatStabilityTests.GameplaySceneIncludesCheckpointRunSection`은 코드 사본에만
  있던 플랫폼 이름을 확인하고 있었다 — `SceneLayout.json`은 `overridePlatforms: true`로 자기 목록을
  쓰고 그 이름은 출하된 적 없다. `GameplaySceneNamesEveryPlatformItShips`(목록 비어 있지 않고 전부
  이름 있음)로 교체. `PORT_STATUS.md` "Second phase K2-K4".
- **정리 `a4dc6e3`.** `GameplayTuningCatalog`가 `ProgressionTuningData.Load()`를 다른 항목과 같이 부른다.

## 검증 — 무엇을 어디까지

- K1 착지: 풀 스위트 211 / 1 / 1 (probe 0건).
- K2~K4 병합 트리 `87c0245`: 풀 스위트 **209 / 2 / 1** — 기존 레이아웃 개수 1건 + 위 "발견" 1건.
- 발견 수정 뒤 `P0CombatStabilityTests` 필터 53 / 0 / 1, `DesignFileCompleteness` 1 / 0 / 0.
- 최종 트리 `a4dc6e3` 풀 스위트: **210 / 1 / 1**, `Handle is not initialized` 0건, 종료 크래시 0. 총 212건 = 213 − identity 2 + 완전성 1.
- 에이전트 각자: build + 헤드리스 스모크(GameplayScene + 챕터 2, K3는 챕터 2~8 전부) 0 오류, 필터 테스트
  (K2: BossAttackGrammar 4, ChapterBoss 13; K3: 10개 클래스 61건; K4: Readability 5, Completeness 1).

## 다음에 할 일 — K5 ∥ K6, 그 전에 결정 1건

**결정 필요 — 계획 밖 발견 3건을 어디에 넣을지.** `PLAN_CLOSEOUT.md` 검토 이력 2026-09-12 항목.
(a) `encounterData == null` 뒤의 `Default*` 상수 — `WrathMiniBoss` 7 + `DefaultPhaseTwoPattern()` +
`_postAttackRecoveryTime`, `RainbowChapterBossBehaviour` 6 — 그리고 `bossData` 숫자 fallback 2곳
(`DefaultStunDuration`, `ChantInterval : 0f`). 테스트가 `WrathMiniBoss`를 encounter 없이 만들므로 K1처럼
테스트 선행 필요(`SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"))`). (b) `BossAttackProfile`
`[Export]` 초기화값 24 — 출하 행 36개는 이미 완전하지만 인라인 fixture 행이 `damageType`(Standard =
enum 1)과 `afterimageCountOverride = -1`에 기대므로 fixture 보충 필요. (c) `EnemyStateMachine` 기본
클래스 `[Export]` 초기화값 — A7, 이미 K5 범위. **제안: (a)(b)를 K5에 편입.**

- **K5** 컴포넌트 초기화값 약 25(`GameplayCameraFollow2D` 4, `GameplayWorldHealthBar` 3 + 색 2,
  `ActorIdleBob` 2, `GameplayTelegraphPulse` 4, `GameplayEnvironmentBuilder` 3, `GameplayEnemySpawner` 1,
  `GameplayHud ??` 5) + HUD 팔레트 5색 → `MenuTheme.tres` 토큰(`GetThemeColor`), alpha 4 → Theme item,
  타이밍 3 → 신규 `Resources/Design/UiTuning.json`(등록 + D6 테이블에 추가). `WorldHealthBar` 색 2 →
  `Resources/Art/Readability.json` 추가 키. `GameplayTuningDefaults` 상수 4개 + 소비자(`GameplayPlayerSpawner`
  3, `CheckpointZone` 1, `GameplaySoulDrop` 1) 삭제. 새 JSON 키는 **D6 테스트가 파일→타입 표에 없으면
  실패**하므로 `DesignFileCompletenessTests`의 표에 넣는다.
- **K6** 씬 거울 제거(D4): 액터 씬 7개에서 스포너가 매 스폰 덮어쓰는 값 삭제, `GameplaySceneDefaultsAsset`
  기본값 4 → 0. 파일 서로소라 K5와 병렬.
- 그 뒤 **K7**(규칙 2·3 잔여, 씬 셸 8개 동시 편집, 단독) → **K8**(D6 테스트는 이미 있음 — 문서 종결).

병렬 방식은 이번에 검증된 그대로: 에이전트마다 `isolation: worktree`, 소유 파일 목록 명시, 각자 브랜치에
경로 명시 커밋, 코디네이터가 `cherry-pick` 후 풀 스위트 1회. 에이전트는 `model: opus`(사용자 지시).

그 밖에 남은 것은 전부 **사람의 판단이 필요한 것**이다.

1. **씬 S9 — 아레나를 챕터 셸에 배치.** 기획자 소유 데이터의 소유권 이동이라 **물어본 뒤에** 한다.
2. **`master`로의 병합 여부.** 커밋 47개가 전환 브랜치에만 있다.

## 사람이 봐야 하는 것 — 자동 검증으로 못 잡는다

1. **타이틀 설정 패널.** 공유 Theme이 붙으면서 `CheckBox`가 테마 판 위에 그려진다. 아무도 눈으로 본 적 없다.
2. **공격 예고 맥동 결함.** `AttackReadout` 안의 `GameplayTelegraphPulse`가 렌더러를 못 찾는다(이식 결함).
3. **챕터 8 보스 인트로.** S8 이후 7.2 → 6.336 푸시. 눈으로 본 적 없다.
4. **재미 검증.** 여전히 안 했다.
5. **Godot .NET 바인딩 레이스, 간헐.** K2~K4 병합 트리 풀 스위트에서 `System.InvalidOperationException:
   Handle is not initialized`(`ScriptManagerBridge.SwapGCHandleForType`, 호출처
   `GameplayEnvironmentBuilder.CreateWorldLabel`의 `GD.Load<PackedScene>`) 4건 — 테스트는 통과, 로그만.
   같은 트리 `P0CombatStabilityTests` 필터 1회는 53 통과 뒤 종료 시 `0xC000001D`(`GodotObject.Finalize` →
   `godotsharp_internal_refcounted_disposed`). HEAD 필터 재실행 2회·기준 커밋 `5112989` 필터 2회는 0건.
   K3 에이전트는 기준 커밋 `CutsceneTests`에서 종료 크래시 2/3 관측. **K2~K4가 안 건드린 코드에서 나며
   기준 커밋에도 같은 계열이 있어 엔진 쪽 GC/바인딩 수명 문제로 보이나, 확률이 올랐는지는 모른다.**
   Resource 객체 수가 늘긴 했다(K1/K3: 테스트가 `XData.Load()`로 매번 새 Resource).

## 이번 세션에서 물린 것 — 같은 데서 또 미끄러지지 말 것

- **기획자 JSON을 재직렬화하지 말 것.** `json.dumps(indent=4)`가 한 줄짜리 색상 객체를 펼쳐 150줄 diff를
  만들었다. 되돌리고 **텍스트 줄 삽입**으로 다시 했다(`766b448`, 25줄 순수 추가). K3 에이전트도 같은
  이유로 직접 줄 삽입 스크립트를 썼다.
- **`PlayerPrefs` grep은 저장 파일 접촉 판별에 부족하다.** `GameSave.Read/Write/Clear`가 래퍼다.
  `GameplaySoulsPoisePauseTests`는 `PlayerPrefs` 문자열이 없지만 저장 슬롯을 건드린다. 필터 병렬 실행
  전 `GameSave|PlayerPrefs`로 grep.
- **`git add` 뒤 pathspec 없는 `git commit`은 인덱스 전체를 커밋한다.** `git commit -F msg -- <paths>`.
- **경고는 측정한 뒤 적을 것.** "7개 게이트 크기 0"은 `hasShortcutGate`를 세어 보지 않은 경고였다.
- **worktree 에이전트 3명 병렬은 잘 굴러갔다.** 각자 `.godot`을 본 체크아웃에서 복사해 import 없이 부팅,
  각자 브랜치에 커밋, 코디네이터가 `cherry-pick`(파일 서로소라 충돌 0). `.claude/worktrees/`는 untracked로
  남으니 병합 뒤 `git worktree remove --force` + `git branch -D`. `user://playerprefs.cfg`는 worktree
  사이에도 공유 — 저장 테스트는 코디네이터만.
- **풀 스위트 11분.** Godot 단계(로그 첫 줄 `BUILD OK`) 뒤엔 소스 편집 안전, 빌드는 금지.
- **에이전트가 `--editor` 헤드리스 부팅을 하면 남의 파일의 `.uid`가 생긴다.** K4가 K3 파일의 `.uid` 2개를
  지우고 자기 것만 커밋했다. 병합 전 `git status`를 본다.

## 확인하지 않은 것

- 육안 UI 확인 전반. headless 실행은 구조 검증이지 화면 확인이 아니다.
- 배포. Windows export template이 없어 EXE를 만든 적이 없다. `export_presets.cfg`는 있다.
- 오디오. 귀로 확인한 적 없다.
- `ShortcutGate.tscn`의 `openAlpha = 0.25`가 인스펙터에 보이는지.
- K4 이후 `GameplayReadabilityDefaults.Create()`/`GameplaySceneDefaults.Create()`가 null을 줄 수 있는데
  소비자 6곳(`GateTravelZone.TitleFor`, `GameplayCutsceneTriggers.MoveRigToBoss`, `CheckpointZone`,
  `GameplayLockOnMarker`, `GameplayTelegraphPulse`, `GameplayWorldHealthBar`)은 null 체크가 없다. 부트스트랩
  `IsComplete` 게이트 뒤라 파일 있는 빌드에선 안 닿는다는 K4 판단 — 파일 없는 빌드에서 오류 줄 뒤에
  NRE 한 줄이 더 날 수 있다. 확인 안 함.
- Data 클래스의 `[Export]` 초기화값이 사라져 에디터 인스펙터에서 새 Resource가 전부 0으로 보인다. JSON이
  정본이라 의도된 트레이드오프(D4·K3)지만 눈으로 본 적 없다.
