# 규칙 완전 준수 전환 계획 (2차) — 초안, 미승인

[PLAN.md](PLAN.md)의 단계가 전부 착지한 뒤(2026-09-10) 정본 스킬 `godot-cli-control`의 **완료 기준**에
현재 코드를 대조한 결과다. 1차 전환은 "정본 JSON + 소비 경로"까지 갔고, 완료 기준의 나머지 절반 —
**"코드 fallback 하드코딩이 없다"**, **"같은 값을 여러 체계에 중복 보관하지 않는다"** — 는 의도적으로
남겨 뒀다("파일 없으면 같은 게임" 원칙). 이 문서는 그 절반을 닫는 계획이다.

이 문서는 **계획이다. 코드는 한 줄도 바꾸지 않았다.** 아래 결정 D1~D8에 답이 나오기 전에는 시작하지
않는다.

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

## 결정 필요 — 시작 전에 답이 있어야 한다

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
| **K0** | D1 정책 코드화: `Res.LoadJson` 누락 `PushError`; `GameplayTuningCatalog.Load()` null 항목 `PushError`; `.Shared` 3종 `?? new` 삭제 → null 시 `PushError`+중단. **행동 변화 없음**(파일 전부 존재) | 5 | 낮음 | 단독 |
| **K1** | **테스트 fixture 경로 확보 — 선행.** 맨손 액터를 만드는 12 테스트 파일이 `SetTuningData(XData.Load())`·씬 인스턴스·실제 JSON을 쓰게. `GameplayTuningDefaults.CreateX` 소비 3 테스트 전환. 이 단계 뒤 스위트가 fallback을 **한 번도 안 타야** K2~K5가 안전 | 12 테스트 + `GameplayTuningDefaults.cs` 삭제 | **최고** — 어서션 숫자가 바뀌는 곳이 나올 수 있다. 바뀌면 그 차이가 fallback 드리프트의 증거이므로 기록 후 출하 값으로 | 단독 |
| **K2** | 아키타입 `?? 리터럴` 63건 제거. `tuningData` 필수, 미설정 시 `_Ready`에서 `PushError` + `SetProcess(false)`. 프로퍼티 `DetectionRange => tuningData.detectionRange` | `Scripts/Enemy` 5 | 높음 | K3·K4와 병렬 가능(파일 서로소) |
| **K3** | Data 클래스 `[Export]` 기본값 232개 삭제. **선행 소단계 K3a:** `SceneLayout*.json` 8파일에 빠진 키 3개(`shortcutGatePosition`·`shortcutGateSize`·`shortcutOpensFromRight`)를 현재 기본값으로 추가 — 추가적, diff 증명. 그 뒤 기본값 제거 | 13 클래스 + 8 JSON | 중간 — K3a를 빼먹으면 챕터 7개의 숏컷 게이트가 (0,0) 크기 0으로 | K2·K4와 병렬 |
| **K4** | `GameplaySceneDefaults.Create()` 100 + `CreateBase()` 79 삭제. `Create()`는 빈 객체 + JSON 필수; `CreateBase`는 정렬 순서 20개만 남기고 `Create`로 흡수. `ReadabilityThemeWriter` 삭제(D7). identity 테스트 2개 → D6의 완전성 테스트로 교체 | 3 + 도구 1 + 테스트 2 | 중간 | K2·K3와 병렬 |
| **K5** | 컴포넌트 초기화값 25개 제거 + HUD: 팔레트 5색 → Theme 토큰, alpha 4 → Theme item, 타이밍 3 → `UiTuning.json`(신규, 등록). `WorldHealthBar` 색 2 → `Art/Readability.json` 추가 키(아티스트 파일, 추가적) | 약 10 + JSON 1 + tres 1 | 중간 | K6과 병렬(씬 vs 코드) |
| **K6** | 씬 거울 제거(D4): 액터 씬 7개에서 스포너가 덮어쓰는 값 삭제. `GameplaySceneDefaultsAsset` 기본값 4 → 0(override 플래그 false면 안 읽으므로 무해) | tscn 7 + 1 | 낮음 — 스포너가 항상 덮어씀 | K5와 병렬 |
| **K7** | 규칙 2·3 잔여: `CheckpointZone`·`GateTravelZone`·`Camera2D` fallback 생성 삭제(씬이 authoring, 테스트는 K1에서 씬 인스턴스로); `EnsureComponent` → `GetComponent` + null이면 `PushError`; `SceneryPiece.tscn`(D8); `CutsceneDirector`를 `GameplayScene.tscn`과 챕터 셸 8개에 authoring; `GameplayBuildShim`은 `SceneRoot`·`ActiveSceneName`·`SetActive`만 남기고 `NewObject`/`AddComponent`/`EnsureComponent` 삭제 — 테스트가 쓰는 건 `Tests/Framework/NodeBuild.cs`로 이동 | 약 8 + tscn 9 + shim + 테스트 | 높음 — 씬 셸 8개 동시 편집 | 단독, 마지막 |
| **K8** | D6 완전성 테스트 1개. 문서: `AGENTS.md` "현재 준수 상태"를 "준수"로, `PLAN.md`·`PORTING_GUIDE.md` 종결 표기, `PORT_STATUS`·`INTEGRATION_NOTES`(시그니처 다수), 인수인계 | 문서 | 낮음 | 단독 |

**순서:** K0 → K1 → (K2 ∥ K3 ∥ K4) → (K5 ∥ K6) → K7 → K8. 병렬은 파일 서로소일 때만, 담당별 소유 파일
목록 명시, `run-tests.ps1`은 한 명만, 풀 스위트는 착지마다 코디네이터가 한 번(11분).

**K1이 문이다.** K1 없이 K2~K4를 하면 스위트의 맨손 액터가 전부 `PushError`로 죽는다. K1은 스위트가 정본
파일만으로 도는 상태를 만드는 단계라 가장 크고 가장 먼저다.

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
| K1 | 테스트 12파일 (가장 큼, 사이트 수 미측정 — 시작 전 세어서 이 표를 갱신) |
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
