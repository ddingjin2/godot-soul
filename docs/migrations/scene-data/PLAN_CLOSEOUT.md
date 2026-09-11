# 규칙 완전 준수 전환 계획 (2차) — 승인 2026-09-10

[PLAN.md](PLAN.md)의 단계가 전부 착지한 뒤(2026-09-10) 정본 스킬 `godot-cli-control`의 **완료 기준**에
현재 코드를 대조한 결과다. 1차 전환은 "정본 JSON + 소비 경로"까지 갔고, 완료 기준의 나머지 절반 —
**"코드 fallback 하드코딩이 없다"**, **"같은 값을 여러 체계에 중복 보관하지 않는다"** — 는 의도적으로
남겨 뒀다("파일 없으면 같은 게임" 원칙). 이 문서는 그 절반을 닫는 계획이다.

결정 D1~D8은 2026-09-10에 전부 답이 나왔다 — 여덟 항목 모두 추천안. 아래 표의 "추천" 열이 곧 결정이다.
착지 순서는 K0부터.

## 대조 기준 (스킬 원문)

- 수치의 정본 에셋과 소비 경로가 확인되고 **코드 fallback 하드코딩이 없다.**
- 누락 데이터를 임의의 하드코딩 fallback 값으로 **숨기지 않는다.** 새 수치는 단위·허용 범위·**누락 시
  처리**를 정의한다.
- `const`·Dictionary·기본 인자·`@export` 기본값으로 숫자를 옮기는 것은 준수가 아니다. 실제 에셋 파일에
  값이 있고 런타임이 그 에셋을 참조해야 한다.
- 같은 값을 여러 체계에 중복 보관하지 않는다.
- UI 크기·간격·색·폰트·연출 시간도 씬 속성 / Theme / StyleBox / Animation / 설정 Resource에 저장한다.
- 재사용 요소는 씬으로 분리되어 실제 인스턴스로 사용된다. 노드 트리나 생성 코드를 복사해서 재사용을
  대신하지 않는다.
- 일회성 테스트가 회귀 스위트에 들어가지 않는다. 유지할 계약의 영구 테스트는 승인된 범위로 설계한다.

## 현재 위반 인벤토리 — 측정값 (2026-09-10, `78617da` + 미커밋 fix 기준)

### A. 코드 fallback — 정본은 JSON이지만 코드가 사본을 들고 있다

| # | 층 | 규모 | 파일 |
|---|---|---|---|
| A1 | Data 클래스의 `[Export]` 기본값 = 출하 리터럴 | 13클래스 **232필드** | `SceneLayout` 36/55, `RainbowChapterBoss` 28, `PlayerResource` 28, `PlayerCombat` 28, `CombatTuning` 27, `WorldTuning` 21, `EnemyTuning` 17, `BossEncounter` 12, `PlayerMovement` 8, `SinTuning` 8, `Progression` 7, `CutsceneTuning` 7, `Difficulty` 5 |
| A2 | `Res.LoadJson` 누락 → **조용히 null**; `?? new XData()` | 1 + 3 | `UnityCompat.cs`, `CombatTuningData`, `DifficultyTuningData`, `CutsceneTuningData` |
| A3 | 아키타입 `tuningData?.x ?? 리터럴` | **63사이트** | `WrathMiniBoss` 23, `RangedCaster` 14, `MeleeGrunt` 14, `LeapingAttacker` 10, `RainbowChapterBossBehaviour` 2 |
| A4 | `GameplayTuningDefaults` 테이블 | `CreateMeleeGrunt` 등 4 팩토리 + `const` 4 | 소비: `CheckpointZone`, 스포너 2, `GameplaySoulDrop`, 테스트 3 |
| A5 | `GameplaySceneDefaults.Create()` | **100 리터럴** | 소비: `CreateForScene` 시작점, 테스트 7파일 |
| A6 | `GameplayReadabilityDefaults.CreateBase()` | **79 리터럴** (색 33 + 레이아웃 46) | `ReadabilityThemeWriter`가 이걸로 파일 생성, identity 테스트 2개의 기준 |
| A7 | 컴포넌트 초기화값·`??` | 약 25 | `GameplayCameraFollow2D` 4, `GameplayWorldHealthBar` 3(+색 2), `ActorIdleBob` 2, `GameplayTelegraphPulse` 4, `GameplayEnvironmentBuilder` 3, `GameplayEnemySpawner` 1, `GameplayHud` `??` 5 |
| A8 | UI 코드 리터럴 | 타이밍 `const` 3 + 팔레트 색 5(MenuTheme.tres와 중복) + alpha 4 | `GameplayHud.cs` |

이 사본들은 **한 번도 출하 경로에서 읽히지 않는다** — 디자인 파일이 전부 존재하므로. 읽히는 곳은 (a) 테스트가
`new MeleeGrunt`처럼 액터를 맨손으로 만들 때, (b) 파일이 빠진 빌드. (a)가 이 계획의 실제 비용이고 (b)는 정책
결정이다.

### B. 중복 보관 — 같은 값이 두 체계에

| # | 내용 | 규모 |
|---|---|---|
| B1 | 액터 씬 7개가 콜라이더 반지름/높이·Visual 스케일·HealthBar 위치·리드아웃 위치/스케일·RoleMarker 위치를 authoring, 스포너가 `ReadabilityLayout.json`으로 매 스폰 덮어씀 | 씬 7 × 약 6값 |
| B2 | `GameplayHud`의 팔레트 5색 = `MenuTheme.tres` 토큰 | 5 |
| B3 | `GameplayWorldHealthBar.FrameColor`가 코드 const이고 `WorldHealthBar.tscn`도 같은 색 authoring, 코드가 매번 재적용 | 1 (+`LowHealthTint` 코드 전용 1) |
| B4 | `GameplaySceneDefaultsAsset` `[Export]` 기본값 = SceneLayout.json 카메라/스폰 | 4 (S13이 값을 맞춰 둔 상태) |

### C. 규칙 2·3 잔여 — 코드가 만드는 노드

| # | 내용 | 성격 |
|---|---|---|
| C1 | `CheckpointZone`·`GateTravelZone`이 씬에 trigger 없으면 `new CollisionShape2D` | 합성 액터용 fallback |
| C2 | `GameplaySystemBootstrapper`가 카메라 없으면 `new Camera2D` | 합성 씬용 fallback |
| C3 | `EnsureComponent` 21곳의 add 분기, `GameplayBuildShim.AddComponent`/`NewObject` | 합성 액터용 |
| C4 | `GameplayEnvironmentBuilder.CreateSceneryPiece` — 장식 `Sprite2D`를 코드로 | 파일 스스로 "노드 하나짜리 씬은 구조 없는 파일"이라 방어. 규칙 문자와 충돌 |
| C5 | `CutsceneDirector`를 `NewObject`로 생성 | 씬 셸에 authoring 가능 |

### D. 누락 시 처리가 정의돼 있지 않다

파일 누락 = null = 조용히 코드 사본. 어떤 Data 클래스도 경고를 내지 않는다. 스킬의 "누락 시 처리 정의"가
비어 있다.

### 면제 — 이 계획이 건드리지 않는 것

`AUDIT_NUMBERS.md` §3.6(정렬 순서 20개 — 기술 계약), §3.13~3.17(진단·X-ray·베이크·픽셀 ppu·MSAA 단계), §4
keep-in-code 목록, `GameplayVisualFactory`, `DebugVisualization`. 이들은 "정본이 코드인 값"이지 fallback이
아니다. **D2에서 확인받는다.**

## 결정 — 2026-09-10 확정, 전부 추천안

| # | 결정 |
|---|---|
| D1 | 누락 = `PushError` + 부팅 중단 |
| D2 | 면제 목록 확정 (정렬 순서 20, §3.13~3.17, §4) |
| D3 | `GameplayTuningDefaults` 팩토리 삭제, 테스트는 실제 JSON |
| D4 | 씬에서 숫자 제거, JSON 정본 |
| D5 | 색·alpha → `MenuTheme.tres`, 타이밍 → `UiTuning.json` |
| D6 | identity 테스트 2개 → 완전성 테스트 1개 (승인됨) |
| D7 | `ReadabilityThemeWriter` 삭제 |
| D8 | `SceneryPiece.tscn` 생성 |

원안의 질문·추천·대안은 검토 이력으로 아래에 그대로 둔다.

### 원안

| # | 질문 | 추천 | 대안 |
|---|---|---|---|
| D1 | **디자인 파일 누락 시 처리.** | `Res.LoadJson` 누락 → `GD.PushError` + null. 각 `Load()`는 null 전파. 소비자(부트스트랩·스포너·`.Shared`)는 null이면 **부팅 중단**(`PushError` 후 return, 아레나 미생성). 테스트는 실제 파일로 돈다(이미 그렇다). | 예외 throw. headless에서 스택은 더 잘 보이나 Godot C#은 `_Ready` 안 예외를 삼키므로 비추 |
| D2 | **정본이 코드인 값의 면제 목록** 확정. | 위 "면제" 그대로 | 정렬 순서도 JSON으로(§3.6 "한 블록으로 아니면 말고") |
| D3 | **`GameplayTuningDefaults.CreateX` 4개의 운명.** 테스트만 쓴다. | **삭제**, 테스트는 실제 JSON `Load()` 사용. fixture 값이 출하 값과 같아지므로 어서션 숫자가 바뀔 수 있다 → 그건 발견이지 약화가 아님 | `Tests/Framework/DesignFixtures.cs`로 이동(제품 밖) |
| D4 | **씬 거울(B1).** | 씬에서 숫자 제거 — 씬은 구조, 숫자는 JSON. 에디터에서 열면 형태가 기본 크기로 보이는 트레이드오프 기록 | 스포너 덮어쓰기 중단 + JSON 키 삭제(씬이 정본). JSON 정본 원칙과 충돌, 기획자 파일에서 46키가 사라짐 — 비추 |
| D5 | **HUD 팔레트·타이밍(A8, B2).** | 색은 `MenuTheme.tres` 토큰을 `GetThemeColor`로 읽어 코드 사본 삭제; alpha 변형 4개는 Theme의 별도 color item; 타이밍 3개는 신규 `Resources/Design/UiTuning.json` | 타이밍을 `Hud.tscn`의 `AnimationPlayer`로 — 코드 tween을 전부 뜯어야 해 범위 큼 |
| D6 | **영구 테스트.** identity 테스트 2개(`AppliedTheme_…`, `AppliedLayout_…`)는 코드 사본이 기준이라 A6과 함께 기준을 잃는다. | "모든 디자인 파일이 타입의 모든 필드를 채운다" 영구 테스트 **1개**로 교체(validator를 스위트로 승격). 규칙 4상 승인 필요 | identity 테스트를 값 하드코딩으로 유지 — 테스트가 새 fallback이 됨. 비추 |
| D7 | **`ReadabilityThemeWriter` 삭제.** 코드 사본에서 파일을 생성하는 도구 — 사본이 없어지면 할 일이 없다. | 삭제 | "파일 → 파일" 재포맷 도구로 개조 — 용도 없음 |
| D8 | **장식 스프라이트 씬(C4).** | `Scenes/World/SceneryPiece.tscn`(Sprite2D 1개) 생성, 규칙 문자대로. 파일의 반론은 이력으로 보존 | 면제로 기록 |

## 단계 — 각 단계는 풀 스위트 green으로 착지

| 단계 | 내용 | 파일 | 위험 | 병렬 |
|---|---|---|---|---|
| **K0** | **완료.** `Res.LoadJson(path, required)`가 누락을 `PushError`로 말한다(선택적 조회 2곳만 `required: false`); `.Shared` 3종은 `?? new` 대신 null; `GameplayBootstrap`이 `GameplayTuningCatalog.IsComplete` + `.Shared` 3종을 확인하고 하나라도 없으면 아레나를 만들지 않는다. 파일이 전부 있으므로 행동 변화 없음 — `WorldTuning.json`을 치우고 부팅해 오류 2줄과 빈 아레나를 확인한 뒤 복원 | 8 | 낮음 | 단독 |
| **K1** | **완료.** 맨손 아키타입 5곳이 `SetTuningData(XData.Load())`, 팩토리 소비 6곳이 `XData.Load()`, 스테인 유예 1곳이 `PlayerResourceData.Load()`(상수 fallback 제거). `GameplayTuningDefaults` 팩토리 4개 삭제(D3), 스포너 4곳은 `catalog.X` — K0이 완전성을 보장한다. 어서션 숫자 변화 0: 팩토리 값이 JSON과 같았다. 증명: 아키타입 4클래스 `_Ready`와 챕터 보스 `PulseTelegraph`에 임시 `PushError("FALLBACK")`를 심고 풀 스위트 — 0건, 211/1/1 유지 — 뒤 제거. `AddComponent<` 조립 30곳은 K7 | 테스트 3 + `GameplayTuningDefaults.cs` + 스포너 + 카탈로그 doc | 낮음 (측정 뒤) | 단독 |
| **K2** | **완료.** 아키타입 4개의 `??` 67곳(감사 63 + `TelegraphBase` 색 4)·삼항 39곳·null 조기 반환 2곳 제거, 챕터 보스 `PulseTelegraph` 2곳. `tuningData` 없이 트리 진입 → `PushError` + `SetProcess(false)`/`SetPhysicsProcess(false)` + return. 계획 밖 발견: `encounterData == null` 뒤의 `Default*` 상수(`WrathMiniBoss` 7 + `DefaultPhaseTwoPattern()`·`_postAttackRecoveryTime`, `RainbowChapterBossBehaviour` 6)와 `bossData` 숫자 fallback 2곳(`DefaultStunDuration`, `ChantInterval : 0f`) — 검토 이력 | `Scripts/Enemy` 5 | 높음 | 완료 |
| **K3** | **완료.** K3a: `SceneLayout*.json` 8파일에 숏컷 게이트 3키 (`hasShortcutGate: true`는 챕터 2뿐 — "7개 게이트 크기 0" 경고는 과장, 실제로 살아 있던 키는 챕터 2의 `shortcutOpensFromRight` 하나). K3: 17클래스 초기화값 309 → 1(`attacks = Array.Empty`, null 가드). JSON 추가 22 + 360 + 0값 스위치 25, 전부 기존 기본값. `ProgressionTuningData.Load` 누락 시 null, `PlayerProgression`은 캡 처리 + `PushError`. 테스트 fixture 6파일에 빠진 키 보충, 기대값 변화 0. 계획 밖 발견: `BossAttackProfile` 초기화값 24 (`damageType = Standard`가 enum 1이라 인라인 fixture 36행 보충 필요) — 검토 이력 | 17클래스 + JSON 17 + 테스트 6 | 중간 | 완료 |
| **K4** | **완료.** `Create()` 92 리터럴 → 0(파일 적용만), `CreateBase()` 217 → 정렬 순서 20(D2). `ReadabilityThemeWriter` + 독 버튼 삭제(D7). identity 테스트 2개 → `DesignFileCompletenessTests` 1개(D6) — 파일→타입 표 전수, 누락 키를 이름으로 보고. 부수 삭제: `GameplayReadabilityThemeData.CopyFrom`, `internal ToGodot(float,float)` 2개. 발견: `P0CombatStabilityTests`의 `CheckpointRunPlatform` 어서션은 코드 사본에만 있던 이름 — 출하 계약(이름 있는 플랫폼 목록)으로 교체, `PORT_STATUS.md` | 3 + addons 2 + 테스트 4 | 중간 | 완료 |
| **K5** | **완료(두 명).** core: A7 24곳 중 19 제거(5는 옵셔널 컴포넌트 null 가드, 숫자 아님), HUD 팔레트 5 + alpha 4 → `MenuTheme.tres` `Palette` 항목 9, 타이밍 3 → `UiTuning.json`(카탈로그 등록, D6 표 추가), B3 색 2 → `Art/Readability.json`, `GameplayTuningDefaults` 삭제. boss: encounter 상수 12 + 패턴 메서드 + 필드 → `encounterData` 필수(`WrathMiniBoss` `_Ready`, 챕터 보스 `_Process` 첫 프레임 가드), `BossAttackProfile` 초기화값 24 → 0, `EnemyStateMachine` 4 → 0 — 값은 이미 `WorldTuning.json`에 한 번 있어 `Configure`로만 들어옴(브리프의 "12파일에 복사"는 중복이라 하지 않음). JSON 추가 0. 기대값 변화 0. 발견은 검토 이력 | core 23 + boss 13 | 중간 | 완료 |
| **K6** | **완료.** 액터 씬 7개에서 스포너가 매 스폰 쓰는 속성 60줄 + 빈 override 블록 8 삭제; 스폰 결과 493값 전후 동일(probe). `GameplaySceneDefaultsAsset` 초기화값 4 → 0. 발견: `Scenes/World/AttackReadout.tscn`·`WorldHealthBar.tscn`·`Checkpoint.tscn`이 같은 거울 패턴(K6 범위 밖) | tscn 7 + 1 | 낮음 | 완료 |
| **K5b** | **승인, 명세는 아래 §K5b.** K5·K6 잔여: SinState 색 7 → Theme, 플레이어 시작 자원 상수 2, `Scenes/World` 거울 3파일(WorldHealthBar·AttackReadout·Checkpoint), `EnemyStateMachine` 죽은 사본 4 + 도달 불가 메서드 1, `Create()` null 미처리 소비자 2. 기록만 1 | 약 9 + tscn 3 | 낮음 | 단독, K7 전 |
| **K7** | 규칙 2·3 잔여: `CheckpointZone`·`GateTravelZone`·`Camera2D` fallback 생성 삭제(씬이 authoring, 테스트는 K1에서 씬 인스턴스로); `EnsureComponent` → `GetComponent` + null이면 `PushError`; `SceneryPiece.tscn`(D8); `CutsceneDirector`를 `GameplayScene.tscn`과 챕터 셸 8개에 authoring; `GameplayBuildShim`은 `SceneRoot`·`ActiveSceneName`·`SetActive`만 남기고 `NewObject`/`AddComponent`/`EnsureComponent` 삭제 — 테스트가 쓰는 건 `Tests/Framework/NodeBuild.cs`로 이동 | 약 8 + tscn 9 + shim + 테스트 | 높음 — 씬 셸 8개 동시 편집 | 단독, 마지막 |
| **K8** | D6 완전성 테스트 1개. 문서: `AGENTS.md` "현재 준수 상태"를 "준수"로, `PLAN.md`·`PORTING_GUIDE.md` 종결 표기, `PORT_STATUS`·`INTEGRATION_NOTES`(시그니처 다수), 인수인계 | 문서 | 낮음 | 단독 |

**순서:** K0 → K1 → (K2 ∥ K3 ∥ K4) → (K5 ∥ K6) → K5b → K7 → K8. 병렬은 파일 서로소일 때만, 담당별 소유 파일
목록 명시, `run-tests.ps1`은 한 명만, 풀 스위트는 착지마다 코디네이터가 한 번(11분).

**K1이 문이다.** K1 없이 K2~K4를 하면 스위트의 맨손 액터가 전부 `PushError`로 죽는다. K1은 스위트가 정본
파일만으로 도는 상태를 만드는 단계라 가장 크고 가장 먼저다.

## K5b — 잔여 정리 명세 (2026-09-12 승인, 다음 세션의 시작점)

목적: K5·K6가 남긴 코드 사본·씬 거울·null 미처리 소비자를 한 번에 닫는다. 한 명, `isolation: worktree`,
`model: opus`. 여섯 항목이 파일 서로소라 병렬이 필요 없다. 착지 = 풀 스위트 210 / 1 / 1 유지,
`DesignFileCompletenessTests` green, 값 동일 probe. 측정치는 전부 `ada3269` 기준 — 에이전트가 다시 잰다
(브리프는 방향, 사실은 현장).

### 소유 파일

- `Scripts/UI/GameplayHud.cs`, `Resources/UI/MenuTheme.tres`
- `Scripts/Gameplay/GameplayPlayerSpawner.cs`
- `Scripts/Gameplay/GameplayWorldHealthBar.cs`, `Scenes/World/WorldHealthBar.tscn`, `Scenes/World/AttackReadout.tscn`,
  `Scenes/World/Checkpoint.tscn`
- `Scripts/Enemy/EnemyStateMachine.cs` (+ `Scripts/Gameplay/GameplayEnemySpawner.cs`와 `Tests/Unit/P0CombatStabilityTests.cs`는
  `Configure`/`DetectPlayer` 시그니처가 바뀔 때만)
- `Scripts/Gameplay/GameplayLockOnMarker.cs`, `Scripts/Gameplay/GateTravelZone.cs`
- 테스트: 위 변경이 깨뜨리는 파일만. 문서는 코디네이터.

### 항목

1. **SinState 색 7 → Theme.** `GameplayHud.cs:917-933` `GetSinColor`: Wrath `#8C3428`, Sloth `#3F4A63`, Pride `#7A6A3C`,
   Gluttony `#8C5A28`, Greed `#8C7A28`, Envy `#3F6347`, Lust `#7A3F5C`. §4 면제 아님(§2.9 UI 표현 블록).
   먼저 `SinTuning.json`·`Resources/Art/Readability.json`에 같은 색이 이미 있는지 확인 — 있으면 그쪽을 읽고 Theme에
   중복을 만들지 않는다. 없으면 K5가 만든 `MenuTheme.tres`의 `Palette` 타입에 `sin_wrath` … `sin_lust` 7항목을
   추가하고 기존 `Hue(token)`으로 읽는다.
2. **플레이어 시작 자원 상수 2.** `GameplayPlayerSpawner.cs:78-79` `StartingHealth = 100f`, `StartingHumanity = 100f`;
   사이트 `:89-91` `resources != null ? resources.x : Const` 3곳. 정본 `PlayerResources.json` `maxHealth`·`startingHealth`·
   `startingHumanity` = 100. 상수 삭제, `resources` null이면 D1(`PushError` + 스폰 중단). 부트스트랩이 카탈로그 완전성을
   보장하므로 출하 경로 변화 없음.
3. **B1 잔여 거울 — 코드가 매번 덮어쓰는 씬 값.** K6 방식 그대로: 어느 쪽이 이기는지 코드 줄로 증명한 뒤 지는 쪽만
   지운다. 부팅 후 값 dump 전후 동일.
   - `GameplayWorldHealthBar.cs:27,30,32` `_size`·`_offset`·`_fillColor` 초기화값 ↔ `Scenes/World/WorldHealthBar.tscn:33,39,52,55`
     (Frame position `(0,-120)`·scale `(16,2.5)`, Fill scale `(15,1.5)`·modulate `(0.85,0.08,0.08)`). `Initialize(:67-69)`가
     세 필드를 쓰고 `ApplyLayout`이 `_barRoot.Position = _offset`, `_fillRenderer.Modulate = _fillColor`, `_fill.Scale`을 쓴다.
     `Initialize` 없이 쓰는 경로가 있으면 그 경로는 D1(오류)로 만든다. z_index 40/41은 코드가 안 쓰면 씬에 남긴다.
   - `Scenes/World/AttackReadout.tscn:23,26,28` scale `(1.640625,1.171875)`·modulate `(0.82,0.9,1,0.16)`·z_index 8 ↔
     `GameplayEnemySpawner.DressAttackReadout`(`:594-595` Modulate·ZIndex + 크기)과 플레이어 스포너가 매 스폰 덮어씀.
     단 `GameplayTelegraphPulse`가 ready 시점의 scale을 캐시한다(tscn 주석 `:7`, `:32-33`) — 순서(스포너 dress → 트리
     진입 → `_Ready` 캐시)를 확인하고 지운다.
   - `Scenes/World/Checkpoint.tscn:31` `radius = 150.0` ↔ `CheckpointZone.cs:257-258` 씬 원형이어도
     `circle.Radius = AuthoredRadius()`로 매 인스턴스 덮어씀(`resource_local_to_scene`). 삭제. `:249-253`의
     `new CollisionShape2D` fallback은 K7(C1) — 건드리지 않는다.
4. **`EnemyStateMachine` 죽은 사본 4 + 도달 불가 메서드 1.** `:72` `gravity = World.U(9.81f)`, `:87-88`
   `_ledgeProbeAhead = World.U(0.35f)`·`_ledgeProbeDepth = World.U(1.1f)`, `:96` `perfectParryStunMultiplier = 1.6f`.
   `Configure(:109-126)`이 넷 다 덮어쓰고 스포너(`GameplayEnemySpawner:447-448` 등)와 P0 fixture(`ConfigureFromDesign<T>`)가
   항상 부른다. 초기화값 삭제. 정본: `WorldTuning.json` `enemyGravity`·`enemyLedgeProbeForward`·`enemyLedgeProbeDepth`,
   `PlayerCombat.json` `perfectParryStunMultiplier`. `Configure`를 안 부른 액터는 중력 0으로 떠 있게 되므로 D1대로
   `_Ready`에서 미호출을 감지(플래그) + `PushError` + 정지. `GetDetectionRange() => World.U(5f)`(`:365`): 아키타입 4개는
   `DetectPlayer`를 override(`MeleeGrunt:206`, `LeapingAttacker:195`, `RangedCaster:213`, `WrathMiniBoss:519`)하고 챕터
   보스는 `GetDetectionRange`를 override(`:1197`)하므로 base 본체는 도달 불가 → `abstract`. 시그니처 변경은
   `INTEGRATION_NOTES`용으로 보고.
5. **`GameplayReadabilityDefaults.Create()` null 미처리 소비자 2.** `GameplayLockOnMarker.cs:93`, `GateTravelZone.cs:357`.
   (`GameplayCutsceneTriggers`는 K5-core 보고와 달리 `Create()`를 부르지 않는다 — 확인 완료.) null이면 `PushError` +
   return, `CheckpointZone.EnsureMarker`가 이미 하는 모양 그대로.
6. **기록만.** 챕터 보스 테스트 fixture 6파일의 인라인 공격 행 14개는 `telegraphColor`/`hazardColor` 없이(투명)
   예고한다. 테스트가 안 읽고 출하 행 36개는 전부 갖고 있다. 손대지 않는다.

### 검증

- `tools/build.ps1` BUILD OK; 헤드리스 스모크 GameplayScene + 챕터 2~8 오류 0(알려진 `SpriteFrameAnimator` 경고 제외).
- 값 동일 probe: 항목 1(색 7), 2(자원 3), 3(씬 값 8), 4(넷) — 삭제 전 리터럴 = 삭제 후 읽는 값. 임시 `GD.Print`,
  `Tests/` 밖, 커밋 전 제거.
- 필터 테스트는 `GameSave|PlayerPrefs` 없는 클래스만; `DesignFileCompleteness` 필수. 풀 스위트는 코디네이터 1회.
- 어서션 약화 금지. 기대값이 바뀌면 그 값은 출하되지 않던 것 — 출하 값으로 바꾸고 `PORT_STATUS.md`용으로 old → new 보고.
- 커밋은 worktree 브랜치에 경로 명시(`git commit -F msg -- <paths>`), 본문 끝에 attribution 2줄.

### 착지 후 남는 코드 fallback

0이어야 한다. `grep -rnE '\?\? (new Color|World\.U|[0-9])' Scripts`와 `[Export]` 초기화값 grep으로 재측정해 K8 문서에
숫자로 남긴다. 남는 것은 면제(D2)뿐: 정렬 순서 20, §3.13~3.17, §4.

## 이 계획으로 바뀌는 행동

- **출하 경로: 없음.** 모든 파일이 존재하므로 fallback은 한 번도 실행되지 않았다. 유일한 예외는 K3a가
  숏컷 게이트 키를 추가하는 것인데 값이 현재 기본값과 같다.
- **파일이 빠진 빌드:** "같은 게임"에서 "부팅 중단 + 오류 로그"로. 이것이 D1의 본질이다.
- **테스트:** K1에서 fixture 숫자가 출하 값과 같아진다. 어서션이 바뀌는 테스트가 나오면 그 테스트는 지금까지
  출하하지 않는 숫자를 검증하고 있었다는 뜻이다 — PLAN.md의 D3 정정과 같은 종류의 발견.

## 규모

| 단계 | 편집 |
|---|---|
| K0 | 5파일, 약 20줄 |
| K1 | 테스트 12파일: `AddComponent<` 30사이트(조립 — K1은 손대지 않음), `new 아키타입` 5, `GameplayTuningDefaults.` 7. 상세 표는 `SESSION_HANDOFF.md` |
| K2 | 5파일, 63사이트 |
| K3 | 13파일 232필드 + JSON 8파일 +3키 |
| K4 | 3파일 179리터럴 삭제 + 도구 1 삭제 + 테스트 2 재설계 |
| K5 | 약 10파일 + `UiTuning.json` + `MenuTheme.tres` |
| K6 | tscn 8 |
| K7 | 약 8파일 + tscn 9 + shim + 테스트 이동 |
| K8 | 문서 6 |

풀 스위트 착지 최소 8회 = 약 90분의 순수 대기. 병렬 3자리를 써도 검증은 직렬이다.

## 하지 않는 것

- `Resources/Design/*.json`의 **값**을 바꾸지 않는다. K3a와 K5의 색 2키는 추가뿐이다.
- 저장 파일·replay 계약을 건드리지 않는다.
- 면제 목록(D2)의 값을 옮기지 않는다.
- 씬 S9(아레나 authoring)는 이 계획 밖이다 — 별개의 소유권 결정.
- 어서션을 약화시켜 green을 만들지 않는다. K1에서 red가 나면 그것이 발견이다.

## 검토 이력

- 2026-09-10: 초안. 1차 전환 완료 직후 스킬 완료 기준 대조에서 나온 잔여를 전부 담았다. 미승인.
- 2026-09-10: D1~D8 전부 추천안으로 승인. 시작 가능.
- 2026-09-12: K2·K3·K4 착지(병렬, worktree 3개, cherry-pick). 계획 밖 발견 3건 — (a) `encounterData == null` 뒤의 `Default*` 상수 13개 + `DefaultPhaseTwoPattern()` + `_postAttackRecoveryTime`, 그리고 `bossData` 숫자 fallback 2곳; (b) `BossAttackProfile` `[Export]` 초기화값 24; (c) `EnemyStateMachine` 기본 클래스 `[Export]` 초기화값(A7 범위). 제안: (a)(b)를 K5에 편입, (c)는 이미 K5. **승인 대기.**
- 2026-09-12: 위 (a)(b) K5 편입 **승인**. K5는 두 명이 나눠 든다 — core(컴포넌트·HUD·상수 4·`UiTuning.json`)와 boss(encounter 상수·`BossAttackProfile`·`EnemyStateMachine`). K6 병렬. 에이전트는 opus.
- 2026-09-12: K5(core·boss)·K6 착지. 남은 발견, 전부 작고 서로소 — 제안: **K5b** 한 명, K7 전에. (1) `GameplayHud.GetSinColor` SinState 색 7 → Theme(§4 면제 아님). (2) `GameplayPlayerSpawner` `StartingHealth`/`StartingHumanity` 상수 2, 사이트 3 → `PlayerResources.json` 값. (3) `GameplayWorldHealthBar` `_size`/`_offset`/`_fillColor` 초기화값 3 + `Scenes/World/WorldHealthBar.tscn` 같은 값 3, `Scenes/World/AttackReadout.tscn` scale/modulate/z_index 3, `Scenes/World/Checkpoint.tscn` `radius = 150` — 스포너/코드가 매번 덮어쓰는 거울(B1 잔여). (4) `EnemyStateMachine` 죽은 사본 4(`gravity`, `_ledgeProbeAhead`, `_ledgeProbeDepth`, `perfectParryStunMultiplier`) + `GetDetectionRange() => World.U(5f)`(전 아키타입이 override, 죽은 코드). (5) `GameplayReadabilityDefaults.Create()` null 미체크 소비자 3(`GameplayLockOnMarker`, `GateTravelZone.TitleFor`, `GameplayCutsceneTriggers.MoveRigToBoss`). (6) `AUDIT_NUMBERS.md:297` `stunDuration` 문장 낡음(K8). **같은 날 승인 — 명세는 §K5b, 다음 세션이 시작한다.**
