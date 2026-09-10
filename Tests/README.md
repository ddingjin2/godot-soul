# 검증 — 이식 스위트

Unity Test Framework와 NUnit이 없는 자리를 `Tests/Framework/`의 자작 하네스가 대신한다.
32개 픽스처 208개 테스트가 headless Godot 한 프로세스에서 돈다.

## 실행

프로젝트 루트에서:

```powershell
tools/run-tests.ps1
$LASTEXITCODE
```

성공 기준: 마지막 줄이 `N passed, 0 failed, ...`, 종료 코드 0.

한 픽스처만:

```powershell
tools/run-tests.ps1 -Filter GameplayChapterBossTests
```

**필터는 반드시 이 스크립트를 거친다.** `tools/godot.ps1`에 `-- --test-filter=X`를 직접 넘기면
PowerShell이 `--`를 삼켜 전체 스위트가 돈다. 11분을 잃는 흔한 실수라 여기 적어둔다.

Unity의 에디터 락과 그 락을 두고 다투던 두 러너는 사라졌다. headless 실행은 아무것도 점유하지
않으므로 여러 개를 동시에 돌려도 된다.

## 최신 결과 — 2026-09-10

```
206 passed, 1 failed, 1 skipped in 678.5s
```

Godot `4.7.2.stable.mono.official.ed1daf0bf`, `dotnet build` 0 errors.

**208은 서로 다른 검사의 수가 아니라 테스트 메서드의 수다.** 픽스처 하나가 여러 어서션을
갖는다.

| 픽스처 | 테스트 |
|---|---|
| `P0CombatStabilityTests` (+`.Systems`, `.Gameplay`) | 54 |
| `GameplayChapterTraversalTests` | 17 |
| `GameplayChapterBossTests` | 13 |
| `GameplayCutsceneTests` | 11 |
| `GameplayHealItemTests`, `...DifficultyAndGateTravel`, `...CombatCost`, `...CheckpointZone` | 각 7 |
| `GameplaySoulsPoisePauseTests`, `GameplayChapterTwoTraversalTests` | 각 6 |
| 나머지 22개 픽스처 | 1~5 |

### 실패 1건 — 이식 결함이 아니다

`GameplayLayoutIntegrityTests.EveryChapterLayout_KeepsItsPlacementsAndBonfires`

챕터당 적 배치 20개 이상을 요구하는데 출하된 `Resources/Design/SceneLayout*.json`은 8개 챕터
전부 7개(근접 4·도약 2·원거리 1)를 담고 있다. 그 JSON은 Unity 원본과 **바이트 동일**이고
테스트도 원본의 충실한 사본이므로, 같은 데이터에서 Unity에서도 red였다. 테스트 자신의 주석이
"2026-08-17 density pass가 여덟 챕터를 다시 썼다"고 말하지만 그 작업은 출하 JSON에 없다.

**약화시키지 않고 red로 둔다.** 채우는 것은 기획 작업이며 이식 작업이 아니다.

### 스킵 1건

`P0CombatStabilityTests.ActorPrefabBodyColorsMatchReadabilityDefaults` — 프리팹과
`GameplayReadabilityDefaults` 두 출처가 같은 색을 말하는지 비교하던 테스트다. 프리팹을
이식하지 않아 비교 대상 한쪽이 사라졌다.

## 범위

- **P0 (`Tests/Unit/`)** — Unity 에디터 모드 스위트의 이식. 문자열 리플렉션으로 private 필드·
  메서드·중첩 enum을 찌른다. 계층을 `DeclaredOnly`로 한 단계씩 걷는 것은 의도적이다.
  `MeleeGrunt._sr`, `MeleeGrunt.MoveTowards`, `LeapingAttacker.MoveTowards`가 여전히 shadowing
  멤버라 평범한 `GetMethod`는 `AmbiguousMatchException`을 던진다.
- **PlayMode (`Tests/PlayMode/`)** — 실제 씬을 로드하고 실제 물리 스텝을 돌린다. 챕터 주파
  테스트는 스크립트 에이전트가 실제 InputMap을 눌러 화톳불에서 보스까지 걸어간다.
- **에이전트 계층 (`Scripts/Testing/`)** — 합성 입력은 `Input.ActionPress`/`ActionRelease`로
  진짜 액션을 누른다. Unity처럼 입력을 우회해 `RequestAttack()`을 직접 부르지 않으므로
  `PlayerInputReceiver`가 **켜져 있어야** 한다.

## 하네스가 아는 함정

이식 중에 실제로 물린 것들이다. 새 픽스처를 쓸 때 같은 곳에서 미끄러진다.

- **`ChangeSceneToFile`은 현재 씬을 free한다.** 러너가 부트 씬의 루트면 첫 `LoadScene`이 자기를
  지운다. `TestBoot`가 러너를 루트 윈도우에 붙이는 이유다.
- **러너는 첫 테스트 전에 한 프레임 쉰다.** `async void _Ready`가 트리의 ready 전파 안에서
  픽스처를 돌리면 `AddChild`가 거부된다.
- **테스트 사이에도 한 프레임 쉰다.** `QueueFree`는 지연 처리라, 프레임을 주지 않으면 다음
  테스트가 이전 테스트의 노드를 트리에서 발견한다. 픽스처 단독으로는 통과하고 전체 실행에서만
  깨지는 차이가 여기서 난다.
- **`SceneTree.ProcessFrame`은 노드 `_Process`보다 **먼저** 발화한다.** `IsActionJustPressed`는
  눌린 프레임의 처리 패스에서만 참이므로, 한 번 누르고 다음 프레임에 떼면 수신자는 영영 보지
  못한다. 여러 프레임에 걸쳐 다시 누른다.
- **`Area2D`의 exit는 enter보다 한 스텝을 더 먹는다.** 고정 프레임 수를 세지 말고
  `WaitUntil`로 기다린다.
- **`user://playerprefs.cfg`는 실행 사이에 살아남는다.** 저장을 건드리는 픽스처는 `[SetUp]`에서
  지운다.

## 확인하지 않은 것

- **사람의 재미 검증.** 자동 검증 통과와 다른 것이며 아직 하지 않았다.
- **육안 UI 확인.** headless 실행은 구조를 검증하지 확인된 화면이 아니다.
- **배포 실행.** Windows export template이 없어 EXE를 만든 적이 없고 다른 PC에서 돌린 적도 없다.
- **오디오.** `AudioFeedback`은 Godot에 `PlayOneShot`이 없어 겹치는 큐를 끊는다. 그 차이를
  귀로 확인하지 않았다.
