# 인수인계 — 2026-09-12 (6차)

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, Godot 제작 규칙 4개로의 전환도 **일정에 있던 단계는 전부
끝났다.** 2차(완전 준수) 계획은 K0~K6이 착지했고 K7·K8이 남았다 — 그 전에 작은 잔여 묶음 **K5b**가
승인 대기다.

| 축 | 상태 |
|---|---|
| 이식 자체 | 완료. 스위트 210 passed / 1 failed / 1 skipped (실패 1건은 이식 전부터 red였던 레이아웃 개수 어서션), 총 212건 |
| 규칙 3 — UI는 씬에 배치 | 완결 |
| 규칙 2 — 재사용 단위는 씬 | 완결. `GameplayBuildShim` 축소는 K7 |
| 규칙 1 — 수치 데이터화 | 1차 **완결**. 2차([PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md)) **K0~K6 착지**. 코드 fallback: 아키타입 0, Data 클래스 초기화값 1(null 가드), 레이아웃·가독성 리터럴 0(정렬 순서 20은 D2 면제), 컴포넌트 초기화값 0, HUD 팔레트·타이밍 0, `GameplayTuningDefaults` 삭제, encounter 상수 0, 씬 거울(액터 7) 0. 잔여는 K5b 목록 |
| 규칙 4 — 일회성 테스트 격리 | 지켜지는 중. 영구 테스트 추가는 D6 완전성 1개뿐 |

1차 정본 계획은 [docs/migrations/scene-data/PLAN.md](docs/migrations/scene-data/PLAN.md), 2차는
[PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md) — 결정 D1~D8 승인, 단계 K0~K8, 검토 이력에
2026-09-12 항목 3개(발견 → K5 편입 승인 → K5b 제안).

## 브랜치

`master`가 이식 본체, `refactor/godot-scene-data`가 전환 브랜치. **병합하지 않았고 push한 적도
없다.** 원격 없음. 이번 세션(09-11~12) 커밋 17개: K1 2, K2~K4 7, K5~K6 4, 문서 4. 풀 스위트 4회(K1,
K2~K4 병합, K2~K4 최종, K5~K6 최종).

## 착지한 것 — 이번 세션(09-11~12)

- **K1 `5912697`.** 스위트를 fallback에서 뗐다(`SetTuningData(XData.Load())` 5곳, 팩토리 소비 6곳). 팩토리 4개 삭제.
- **K2 `3b1605d`.** 아키타입 4개의 `??` 67·삼항 39·null 조기 반환 2 제거. `tuningData` 없이 트리 진입 = `PushError` + 정지.
- **K3a `7ac5ee7` / K3 `5c24413` / 후속 `766b448`.** Data 클래스 17개 `[Export]` 초기화값 309 → 1. JSON 키 추가 22 + 360 + 0값 스위치 25, 전부 기존 기본값. `hasShortcutGate: true`는 챕터 2뿐.
- **K4 `87c0245`.** `Create()` 92 → 0, `CreateBase()` 217 → 정렬 순서 20. `ReadabilityThemeWriter` 삭제. identity 테스트 2 → `DesignFileCompletenessTests` 1.
- **발견 `fd09e27`.** `CheckpointRunPlatform` 어서션은 코드 사본에만 있던 이름 → 출하 계약으로 교체.
- **K6 `eaa19ec`.** 액터 씬 7개에서 스포너가 매 스폰 쓰는 속성 60줄 삭제; 스폰 결과 493값 전후 동일(probe). `GameplaySceneDefaultsAsset` 초기화값 4 → 0.
- **K5 boss `13c3258`.** encounter 상수 12 + `DefaultPhaseTwoPattern()` + `_postAttackRecoveryTime` → `encounterData` 필수(`WrathMiniBoss` `_Ready` 가드, 챕터 보스는 `_Process` 첫 프레임 가드 — 스포너가 parent 뒤에 바인딩하므로). `BossAttackProfile` 초기화값 24 → 0, `EnemyStateMachine` 4 → 0. JSON 추가 0 — 값이 이미 파일에 있었다(encounter 9파일·공격 행 36개 완전 확인). 브리프의 "12파일에 복사"는 중복이라 하지 않고 `WorldTuning.json`의 4키를 `Configure`로만 받게 함. P0 fixture에 `ConfigureFromDesign<T>` + `SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"))`.
- **K5 core `a6c6cb7`.** 컴포넌트 초기화값 24곳 중 19 제거(5는 옵셔널 컴포넌트 null 가드). HUD 팔레트 5 + alpha 4 → `MenuTheme.tres` `Palette` 항목 9(`Theme.GetColor`), 타이밍 3 → `Resources/Design/UiTuning.json`(카탈로그 등록, `IsComplete` 포함, D6 표 추가). 헬스바 색 2 → `Art/Readability.json`. `GameplayTuningDefaults` 삭제. 값 동일 probe 증명. 범위 밖 4줄: `GameplayReadabilityDefaults`에 색 속성 2(테마 테스트 계약 때문).
- **문구 `d9a7cee`.** 삭제된 클래스를 부르던 테스트 메시지·tscn 주석 2곳.

## 검증 — 무엇을 어디까지

- K5~K6 병합 트리(`a6c6cb7` + 문구) 풀 스위트 **210 / 1 / 1**, `DesignFileCompleteness` 단독 1 / 0 / 0, `Handle is not initialized` 0, 종료 크래시 0.
- 에이전트 각자: build + 헤드리스 스모크(GameplayScene + 챕터 2~8) 0 오류, 필터 테스트 K6 31건, K5-boss 31건, K5-core 31건 전부 통과. 저장 접촉 클래스(`GameSave|PlayerPrefs`)는 코디네이터 풀 스위트로만.
- 안 한 것: 육안 확인 전부(아래).

## 다음에 할 일 — K5b(승인 대기) → K7 → K8

**결정 필요 — K5b.** `PLAN_CLOSEOUT.md` 검토 이력 마지막 항목. 전부 작고 서로소, 한 명이 한 번에:
1. `GameplayHud.GetSinColor` SinState 색 7 → `MenuTheme.tres` 항목(§4 면제 아님).
2. `GameplayPlayerSpawner` `StartingHealth`/`StartingHumanity` 상수 2(사이트 3) → `PlayerResources.json` 값.
3. B1 잔여 거울: `GameplayWorldHealthBar` `_size`/`_offset`/`_fillColor` 초기화값 3 + `Scenes/World/WorldHealthBar.tscn` 같은 값 3, `Scenes/World/AttackReadout.tscn` scale/modulate/z_index 3, `Scenes/World/Checkpoint.tscn` `radius = 150` — 전부 코드가 매번 덮어씀.
4. `EnemyStateMachine` 죽은 사본 4(`gravity`, `_ledgeProbeAhead`, `_ledgeProbeDepth`, `perfectParryStunMultiplier` — 이제 `Configure`가 항상 덮어씀) + `GetDetectionRange() => World.U(5f)`(전 아키타입이 override).
5. `GameplayReadabilityDefaults.Create()` null 미체크 소비자 3(`GameplayLockOnMarker`, `GateTravelZone.TitleFor`, `GameplayCutsceneTriggers.MoveRigToBoss`) → D1 처리.
6. fixture 공격 행 14개는 `telegraphColor`/`hazardColor` 없이 투명하게 예고한다 — 테스트가 안 읽으니 그대로 둠(기록만).

- **K7** (단독, 마지막 큰 것): `CheckpointZone`·`GateTravelZone`·`Camera2D` fallback 생성 삭제, `EnsureComponent` → `GetComponent` + null이면 `PushError`, `SceneryPiece.tscn`(D8), `CutsceneDirector`를 `GameplayScene.tscn` + 챕터 셸 8개에 authoring, `GameplayBuildShim`은 `SceneRoot`·`ActiveSceneName`·`SetActive`만 남기고 `NewObject`/`AddComponent`/`EnsureComponent` 삭제 — 테스트가 쓰는 `AddComponent<` 30곳은 `Tests/Framework/NodeBuild.cs`로. 씬 셸 8개 동시 편집이라 한 명.
- **K8** 문서 종결: `AGENTS.md` "현재 준수 상태" → 준수, `PLAN.md`·`PORTING_GUIDE.md` 종결 표기, `AUDIT_NUMBERS.md:297` `stunDuration` 문장 갱신, `INTEGRATION_NOTES`·`PORT_STATUS` 정리.

병렬 방식은 검증됨: 에이전트마다 `isolation: worktree` + `model: opus`, 소유 파일 목록 명시, 각자 브랜치에
경로 명시 커밋, 코디네이터가 `cherry-pick`(파일 서로소 → 충돌 0) 후 풀 스위트 1회, 병합 뒤
`git worktree remove --force` + `git branch -D`.

그 밖에 남은 것은 전부 **사람의 판단이 필요한 것**이다.

1. **씬 S9 — 아레나를 챕터 셸에 배치.** 기획자 소유 데이터의 소유권 이동이라 **물어본 뒤에** 한다.
2. **`master`로의 병합 여부.** 커밋 53개가 전환 브랜치에만 있다.

## 사람이 봐야 하는 것 — 자동 검증으로 못 잡는다

1. **타이틀 설정 패널.** 공유 Theme이 붙으면서 `CheckBox`가 테마 판 위에 그려진다. 아무도 눈으로 본 적 없다.
2. **공격 예고 맥동 결함.** `AttackReadout` 안의 `GameplayTelegraphPulse`가 렌더러를 못 찾는다(이식 결함).
3. **챕터 8 보스 인트로.** S8 이후 7.2 → 6.336 푸시. 눈으로 본 적 없다.
4. **재미 검증.** 여전히 안 했다.
5. **에디터에서 액터 씬이 기본 크기로 열린다(K6, D4 비용).** 흰 몸통 + 10×20 캡슐, 라벨·헬스바는 원점.
   실제 비율은 씬을 실행해야 보인다. 의도된 것이지만 아무도 에디터에서 열어 본 적 없다.
6. **HUD 팔레트가 Theme에서 온다(K5).** 값은 probe로 동일 확인했으나 화면은 안 봤다.
7. **Godot .NET 바인딩 레이스, 간헐.** K2~K4 병합 트리 풀 스위트 1회에서 `Handle is not initialized`
   (`SwapGCHandleForType`, `GameplayEnvironmentBuilder.CreateWorldLabel`의 `GD.Load<PackedScene>`) 4건,
   같은 트리 P0 필터 1회 종료 시 `0xC000001D`(`GodotObject.Finalize`). 이후 풀 스위트 2회·필터 다수 0건,
   기준 커밋 필터 0건. K3 에이전트는 기준 커밋 `CutsceneTests`에서 종료 크래시 2/3 관측. 엔진 쪽
   GC/바인딩 수명 문제로 보이나 확률이 올랐는지는 모른다.

## 이번 세션에서 물린 것 — 같은 데서 또 미끄러지지 말 것

- **기획자 JSON을 재직렬화하지 말 것.** `json.dumps(indent=4)`가 한 줄짜리 색상 객체를 펼쳐 150줄 diff.
  되돌리고 텍스트 줄 삽입으로(`766b448`, 25줄 순수 추가).
- **`PlayerPrefs` grep은 저장 접촉 판별에 부족하다.** `GameSave.Read/Write/Clear`가 래퍼. `GameSave|PlayerPrefs`로 grep.
- **`git add` 뒤 pathspec 없는 `git commit`은 인덱스 전체를 커밋한다.** `git commit -F msg -- <paths>`.
- **경고는 측정한 뒤 적을 것.** "7개 게이트 크기 0"은 세어 보지 않은 경고였다.
- **브리프의 사실은 에이전트가 다시 잰다.** K5-boss 브리프에 "K3가 Encounter 가중치를 챕터 1에만 더했다"고
  썼는데 9파일 전부 있었고, "12파일에 4키 복사"는 `WorldTuning.json`에 이미 있는 값의 중복이었다.
  에이전트가 둘 다 잡아 거절했다 — 브리프는 방향, 사실은 현장.
- **worktree 에이전트 3명 병렬 2회 모두 충돌 0.** `.godot`은 본 체크아웃에서 복사, `.claude/worktrees/`는
  untracked. `--editor` 헤드리스 부팅은 남의 `.uid`를 만든다 — 커밋 전 `git status`.
- **풀 스위트 11분.** Godot 단계(로그 첫 줄 `BUILD OK`) 뒤엔 소스 편집 안전, 빌드는 금지.

## 확인하지 않은 것

- 육안 UI 확인 전반. headless 실행은 구조 검증이지 화면 확인이 아니다.
- 배포. Windows export template이 없어 EXE를 만든 적이 없다. `export_presets.cfg`는 있다.
- 오디오. 귀로 확인한 적 없다.
- `ShortcutGate.tscn`의 `openAlpha = 0.25`가 인스펙터에 보이는지.
- 파일이 빠진 빌드의 실제 로그 모양. D1 경로는 K0에서 `WorldTuning.json` 하나로만 확인했다; K4~K5 뒤에는
  `Create()` null 소비자(K5b 5번)에서 NRE 한 줄이 더 날 수 있다.
- `MenuTheme.tres`의 새 `Palette` 타입 항목 9개가 에디터 Theme 편집기에서 보이는지.
