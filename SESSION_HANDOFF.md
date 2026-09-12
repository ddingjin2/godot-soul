# 인수인계 — 2026-09-12 (7차)

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, Godot 제작 규칙 4개로의 전환도 **계획에 있던 단계가 전부 끝났다.**
1차([PLAN.md](docs/migrations/scene-data/PLAN.md)) S1~S13, 2차([PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md))
K0~K8 착지. `AGENTS.md`의 "현재 준수 상태"는 **준수**로 바뀌었고 남은 것은 전부 결정으로 남긴 면제이며
PLAN_CLOSEOUT §종결에 숫자로 있다.

| 축 | 상태 |
|---|---|
| 이식 자체 | 완료. 스위트 210 passed / 1 failed / 1 skipped (실패 1건은 이식 전부터 red였던 레이아웃 개수 어서션), 34픽스처 212건 |
| 규칙 1 — 수치 데이터화 | **완결.** 코드에 남은 숫자: 정렬 순서 20(D2), `CombatTuningData.Shared`를 읽는 초기화값 13(데이터 소스), 테스트 드라이버 진단 주기 2(D2). 튜닝 `??` fallback 0, 씬 거울 0. 디자인 파일 누락 = 부팅 중단, 미설정 컴포넌트 = 첫 프레임 끝 오류 + 정지(`Scripts/Core/TuningGuard.cs`) |
| 규칙 2 — 재사용 단위는 씬 | **완결.** 챕터 셸 8개가 `Scenes/World/GameplayShell.tscn` 상속(각 1줄). 런타임 노드 생성은 면제 8곳뿐, `GameplayBuildShim`은 조회 5멤버 |
| 규칙 3 — UI는 씬에 배치 | 완결 |
| 규칙 4 — 일회성 테스트 격리 | 지켜지는 중. 영구 테스트 추가는 D6 완전성 1개뿐. 이번 세션 probe 3개 전부 커밋 전 삭제 |

## 브랜치

`refactor/godot-scene-data`는 2026-09-12 `524f8be`로 `master`에 병합됐다(`--no-ff`, 이력 보존). **이제 작업 브랜치는 `master`.** 원격은 2026-09-12부터 `origin` = https://github.com/ddingjin2/godot-soul (public, 기본 브랜치 `master`). 병합 뒤 `refactor/godot-scene-data`는 로컬·원격 모두 삭제했다 — **브랜치는 `master` 하나뿐**, 전환 이력은 머지 커밋 `524f8be` 아래에 있다.
이번 세션(09-12) 커밋 23개: K5b 3, K7 4, K7b 2, 수정 1, 문서·잡무 7, .gitignore 2(브랜치별), 원격·병합 문서 3, 머지 1. 풀 스위트 3회(K5b, K7, K7b 각 착지 뒤),
전부 210 / 1 / 1.

## 착지한 것 — 이번 세션

- **K5b `60c98b9` `e64ba27` `78affd9`.** SinState 색 7 → `MenuTheme.tres` `sin_*`; 시작 자원 상수 2 삭제; 씬 거울 8값 삭제
  (465값 probe 동일); `EnemyStateMachine` 초기화값 4 삭제 + `Configure` 필수, `GetDetectionRange` abstract; `Create()` null 가드 2.
  발견: 맨손 enemy fixture 6곳이 `Configure`를 안 부르고 있었다 → `Tests/Framework/EnemyFixture.cs`.
- **K7 `012f55b` `33bac69` `1861ce3` `4e80584`.** 셸 8 → `GameplayShell.tscn` 상속; 카메라 리그·카메라·셰이크·히트스톱·디렉터·
  트리거·리스포너 authoring(카메라를 authoring한 씬이 **없었다** — 삭제가 아니라 신규); `EnsureComponent` 20 → `RequireComponent`
  (`Player.tscn`에 노드 4 추가); 존 fallback 4 + `ZoneRadius` 삭제; `SceneryPiece.tscn`; 아키타입 가드 7 → K2 모양; shim 축소,
  `Tests/Framework/NodeBuild.cs`. P0 테스트 2개 출하 계약으로 재설계. 네 커밋은 **세트로만 빌드된다**.
- **수정 `fac9889`.** K7이 디렉터를 셸 앞쪽에 authoring → 씬 해체 시 HUD보다 늦게 `_ExitTree` → 승리 패널 `GrabFocus`가 트리 밖
  버튼에 닿아 엔진 오류 2줄(테스트는 green). `ShowVictory`가 `IsInsideTree()`를 확인. **풀 스위트 로그를 grep해서만 잡힌 것.**
- **K7b `17d2d50` `d237fcf`.** 감사 A1~A8이 세지 않은 Combat·Player 컴포넌트 `[Export]` 초기화값 — 측정 107/20파일, 삭제 81,
  `spiritTint` → `Art/Readability.json` 키 1. 11컴포넌트 전부 지연 검사(플레이어 스포너가 `AddChild` **뒤**에 튜닝하므로
  `_Ready` 가드 불가). `Tests/Framework/PlayerFixture.cs` 오버로드 11. 발견: `UnityTestAgentPlayModeSmokeTests`의 히트박스가
  `hitLayers = 0`으로 통과하고 있었다 — 그 테스트들은 히트를 검증한 적이 없다(지금은 `Enemy`, 여전히 green).
- **문서·잡무.** K7 명세 `cb2fd5a`(읽기 전용 측정 뒤), K5b 기록 `f4de633`, `.uid` 3 `9dae190`, K7b 명세 `4fce132`, K7 기록 `d564bc6`,
  K8 1부 `6fd1a6b`, K8 2부(이 파일 포함).

## 검증 — 무엇을 어디까지

- 풀 스위트 3회 전부 210 / 1 / 1. `Handle is not initialized`(엔진 바인딩 레이스, `GameplayChapterTraversalTests` 안) 회당 1·3·3건 —
  전부 `SwapGCHandleForType`, 테스트 실패 없음, 확률이 올랐는지는 모른다. `Disabling a CollisionObject node during a physics callback`
  9건/회는 트래버설 스위트의 기존 소음. 마지막 run에서 `is_inside_tree` 0, `TuningGuard` 보고 0.
- 에이전트 각자: 빌드 + 헤드리스 스모크 8셸 오류 0(알려진 `SpriteFrameAnimator` 경고, 챕터 7·8은 `'Boss'` ppu 경고 1 추가 — 기존 아트
  문제) + 필터 테스트(K5b 68건, K7 전 클래스 개별 210/1/1, K7b 18클래스) + 값·트리 probe 동일.
- 안 한 것: 육안 확인 전부(아래).

## 다음에 할 일 — 계획은 끝났다, 남은 것은 사람의 판단

1. **씬 S9 — 아레나를 챕터 셸에 배치.** 기획자 소유 데이터의 소유권 이동이라 **물어본 뒤에** 한다. 셸이 하나가 됐으니 이제는
   상속 씬 8개 각각에 아레나를 authoring하는 모양이 된다.
2. ~~`master`로의 병합 여부.~~ 2026-09-12 병합 완료.
3. (작은 것) `PlayerController2D:135`의 deferred `PlayerActionController` 생성은 fixture만 도달한다 — 면제로 남겼다. 지우려면
   fixture 3곳이 이미 직접 만들고 있으니 한 줄 삭제 + 필터 3개.

병렬 방식은 검증됨: 에이전트마다 `isolation: worktree` + `model: opus`, 소유 파일 목록 명시, 각자 브랜치에 경로 명시 커밋,
코디네이터가 `cherry-pick`(파일 서로소 → 충돌 0) 후 풀 스위트 1회, 병합 뒤 `git worktree remove --force` + `git branch -D`.
이번엔 **읽기 전용 측정 에이전트를 먼저 띄워 명세를 쓰고** 구현 에이전트를 띄웠다(K7, K7b) — 명세의 사실이 코드와 어긋난 곳이
회당 3~7개씩 나왔고 구현 에이전트가 전부 잡았다. 걸린 시간: 측정 9~10분, 구현 K5b 22분·K7 40분·K7b 23분, 풀 스위트 11분.

## 사람이 봐야 하는 것 — 자동 검증으로 못 잡는다

1. **타이틀 설정 패널.** 공유 Theme이 붙으면서 `CheckBox`가 테마 판 위에 그려진다. 아무도 눈으로 본 적 없다.
2. **공격 예고 맥동 결함.** `AttackReadout` 안의 `GameplayTelegraphPulse`가 렌더러를 못 찾는다(이식 결함).
3. **챕터 8 보스 인트로.** S8 이후 7.2 → 6.336 푸시. 눈으로 본 적 없다.
4. **재미 검증.** 여전히 안 했다.
5. **에디터에서 액터 씬이 기본 크기로 열린다(K6·K5b, D4 비용).** 흰 몸통 + 10×20 캡슐, 라벨·헬스바는 원점, `Player.tscn`엔
   이제 노드 4개가 더 있다. 실제 비율은 씬을 실행해야 보인다.
6. **HUD 팔레트·죄 색이 Theme에서 온다(K5·K5b).** probe로 동일 확인, 화면은 안 봤다.
7. **상속 셸이 에디터에서 여는 모양.** `Chapter02_Orange.tscn`을 열면 `GameplayShell.tscn`의 노드 7개가 회색으로 보여야 한다.
   본 적 없다. 편집기 도크의 챕터 생성기(`ChapterSceneCreator`)가 상속 형태를 쓰는 것도 코드로만 확인.
8. **`spiritTint`가 파일에서 온다(K7b).** 값 동일 probe만. 영혼 상태 색을 화면에서 본 적 없다.
9. **Godot .NET 바인딩 레이스, 간헐.** 위 검증 절 참조.
10. **LICENSE 없음.** 저장소가 public이 됐는데 라이선스 파일이 없다(폰트는 OFL). 사람이 고른다.

## 이번 세션에서 물린 것 — 같은 데서 또 미끄러지지 말 것

- **명세는 코드로 다시 잰다.** K7 명세는 "카메라 fallback 삭제"였는데 authoring한 씬이 0이었다. shim 4멤버 → 5, `Ensure*` →
  `Find*`(만들지 않으니), 테스트 `AddComponent<` 28 → 29, 데이터 소스 14 → 13. 읽기 전용 에이전트로 먼저 재고 명세를 쓰는
  것이 이번엔 맞았다 — 그래도 구현 에이전트가 또 3~7개를 잡았다.
- **`_Ready` 가드는 스포너 순서를 먼저 본다.** 플레이어 스포너는 리그 전체를 `AddChild`한 뒤 튜닝한다(형제를 찾는 `_Ready` 때문).
  K5b의 `EnemyStateMachine` 모양(트리 전 `Configure` + `_Ready` 가드)은 플레이어엔 안 맞았고 `CallDeferred` 검사로 갔다.
- **authoring 순서 = 해체 순서.** 셸 앞쪽에 authoring한 노드는 런타임 노드보다 **늦게** 나간다. `CutsceneDirector._ExitTree`가
  이미 나간 HUD를 건드렸다. 새 시스템 노드를 셸에 넣으면 해체 경로를 본다.
- **풀 스위트 로그의 `ERROR` 줄을 grep한다.** 210/1/1로 green이어도 새 `ERROR`가 있을 수 있다(이번 `grab_focus`). K5b 로그와
  비교해서 잡았다 — 로그를 scratchpad에 남기고 다음 run과 `grep -c`로 비교.
- **`cat > file`로 시작하는 Bash 명령은 stdin을 기다리다 걸린다.** 메시지 파일은 `printf` 또는 Write 도구로.
- **에이전트 커밋의 attribution이 다를 수 있다.** K7 에이전트는 하네스가 준 `Claude Opus 5` 줄을 썼다(브리프의 `Fable 5.1`과
  다름). 사실이라 두었다.
- **`.uid`가 빠진 `.cs`가 있었다(3개).** `--headless --import` 한 번이 만든다. 새 스크립트를 커밋할 때 `.uid`도 같이.
- **읽기 전용 에이전트의 셸이 stdin 대기로 걸린 채 1h44m 남아 있었다** — 파일명이 잘린 `sed`. 에이전트는 보고를 마쳤는데 자식 셸만 살아 있었다. 세션 끝에 `ListAgents`/`/tasks`를 보고 남은 것은 `TaskStop`.
- Orca는 원격을 등록 시점에만 읽는다. `git remote add` 뒤 Orca를 껐다 켜야 `gitRemoteIdentity`가 찬다.
- 6차에서 물린 것(재직렬화 금지, `GameSave|PlayerPrefs` grep, pathspec 없는 commit, 경고는 측정 뒤, worktree `.godot` 복사,
  풀 스위트 11분)은 그대로 유효하다.

## 확인하지 않은 것

- 육안 UI 확인 전반. headless 실행은 구조 검증이지 화면 확인이 아니다.
- 배포. Windows export template이 없어 EXE를 만든 적이 없다. `export_presets.cfg`는 있다.
- 오디오. 귀로 확인한 적 없다.
- `TuningGuard`·`RequireComponent`·`Find*`의 오류 경로를 **출하 씬에서** 실제로 밟아 본 적 없다(fixture와 코드 읽기로만).
  파일이 빠진 빌드의 실제 로그 모양도 K0의 `WorldTuning.json` 하나뿐.
- `MenuTheme.tres`의 `Palette` 항목 16개(K5 9 + K5b 7)가 에디터 Theme 편집기에서 보이는지.
- `ShortcutGate.tscn`의 `openAlpha = 0.25`가 인스펙터에 보이는지(이제 코드 초기화값이 없으니 씬 값이 유일).
