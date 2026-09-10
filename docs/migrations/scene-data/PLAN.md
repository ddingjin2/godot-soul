# 씬·데이터 전환 계획 (정본)

이 프로젝트를 사용자 승인 Godot 제작 규칙 4개에 맞추는 작업의 **정본 계획**이다.
근거는 같은 폴더의 두 감사 문서이고, 이 문서는 그 둘을 하나의 순서로 합친 것이다.

| 문서 | 다루는 규칙 | 발견 |
|---|---|---|
| [AUDIT_SCENES_UI.md](AUDIT_SCENES_UI.md) | 규칙 2(재사용 씬)·3(씬 배치 UI) | 위반 61건 / 21파일. `Scripts/` 안에 `PackedScene` 인스턴스 **0건** |
| [AUDIT_NUMBERS.md](AUDIT_NUMBERS.md) | 규칙 1(수치 데이터 에셋) | 수치 위반 약 530건 / 6개 의미군, 결정 필요 19건, **실제 결함 2건** |

## 왜 하는가

이식은 Unity의 런타임 생성 구조를 의도적으로 그대로 옮겼다. 그 판단은 이식으로서는 옳았고
`PORTING_GUIDE.md`에 지시로 남아 있다. 하지만 결과적으로 **한 지점에서는 원본보다 덜
에셋 주도적**이 됐다 — Unity의 HUD는 authored `Canvas`와 uGUI 프리팹이었는데 여기서는
`new Label()` 1,100줄이 됐다. 그 간극을 메우는 것이 이 전환이다.

`PORTING_GUIDE.md`의 "전부 코드로 생성" 항목은 2026-09-10부로 **대체 표기**를 달았다.
문서 두 개가 조용히 다른 말을 하게 두지 않는다.

## 원칙

- **각 단계는 스위트 green으로 착지한다.** 착지 못 하면 다음 단계로 가지 않는다.
- **어서션을 약화시켜 green을 만들지 않는다.** 드리프트를 고쳐 red가 나면 그것이 발견이지
  숨길 대상이 아니다.
- **`Resources/Design/*.json`은 Unity 원본과 바이트 동일하다.** 기본은 확장이고, 기존 파일의
  내용을 다시 쓰는 단계는 단 하나(S10)뿐이며 그 단계는 키만 늘고 값은 그대로임을 diff로
  증명해야 한다.
- **단위 경계를 매번 명시한다.** JSON으로 옮기는 값이 미터인지 픽셀인지 틀리면 100배 어긋나고
  아무 테스트도 잡지 못한다.
- 일회성 probe는 `Tests/` 밖에 두고 끝나면 지운다 (규칙 4).

## 순서

먼저 고칠 것은 리팩터링이 아니라 **이미 잘못 도는 것**이다.

### 즉시 — 실제 결함

| # | 결함 | 출처 |
|---|---|---|
| D1 | `DamageHitbox2D.knockbackForce = 3f`가 스케일되지 않음. 출하 넉백이 400px가 아니라 3px | AUDIT_NUMBERS S2 |
| D2 | `GameplayHud`가 최대 공명을 리터럴 `/100`으로 나눔. 재조정하는 순간 거짓말 | AUDIT_NUMBERS S2 |
| D3 | `GameplayTuningDefaults`가 디자인 파일과 7개 값에서 드리프트 | AUDIT_NUMBERS S3 |
| D4 | `SetActive(false)` 템플릿이 모든 복제본에 `ProcessMode = Disabled`를 각인 | AUDIT_SCENES_UI §4 |
| D5 | `RangedCaster.CreateDefaultProjectile`이 같은 트리를 두 번째로, 텍스처 없이 만듦 | AUDIT_SCENES_UI §4 |

**D1~D5 완료 (2026-09-10).** 각 항목의 결과는 `PORT_STATUS.md`에 있다.

D3에 대해 감사가 "약 208개 테스트가 출하하지 않는 수치를 검증 중"이라고 적었으나 **과장이었다.**
수정 담당이 직접 확인한 결과, 포이즈·소울을 보는 픽스처는 전부 실제 JSON에 닿고 headless 실행에는
디자인 파일이 존재하므로 스위트는 fallback 경로를 사실상 타지 않는다. 드리프트는 실재했고 파일이
빠지는 순간 물었을 문제지만, 스위트가 그것을 검증하고 있던 것은 아니다. 감사 문서는 당시 기록으로
보존하고 정정은 여기에 남긴다.

같은 값이 `PlayerResourceData.soulStainPickupDelay`에 한 번 더 드리프트돼 있고 `[Export]` 미러가
5개 더 있다. `Scripts/Player/`를 소유하는 단계에서 처리한다.

### 씬 전환 — 9단계

`AUDIT_SCENES_UI.md` §6이 각 단계의 노드 트리와 사라지는 코드를 담는다.

| 단계 | 내용 | 파일 | 위험 |
|---|---|---|---|
| S1 | 컷씬 오버레이 | 4 | 낮음 · **완료** |
| S2 | HUD 낱개 컴포넌트 + `MenuTheme.tres` | 5 | 낮음 · **완료** |
| S3 | HUD 패널과 화면 | 9 | 중간 · **완료** |
| S4 | 타이틀 화면 | 5 | 중간 · **완료** |
| S5 | 월드 UI와 공용 마커 | 15 | 중간 · **완료** |
| S6 | 아레나 지오메트리 | 7 | 중간 · **완료** |
| S7 | 이펙트와 템플릿 3종 | 10 | 중간 · **완료** |
| S8 | 액터 (플레이어 + 적 6종) | 7 | **최고** · **완료** |
| S9 | 아레나를 챕터 셸에 배치 | ~20 | **보류 — 제품 판단 필요** |

**씬 전환은 S9를 제외하고 전부 착지했다 (2026-09-10).** 씬 22개 + Theme 1개가 약 1,000줄의
런타임 구성 코드를 대신한다. 규칙 3(UI는 씬에 배치)은 제품 코드에서 완결됐고 규칙 2도
`GameplayBuildShim` 퇴역만 남았다 — 감사는 S8이 그걸 없앨 거라 봤지만 테스트가 아직 쓰므로
별도 단계가 필요하다.

S8이 가장 위험했다. 문서화된 `_Ready`/순서 함정 다섯 중 넷을 한 번에 안고 간다 —
`AddChild` 전 위치 지정(스폰 스윕), `PlayerController2D._Ready`의 의도적 히트박스 덮어쓰기,
`PlayerProgression.EnsureOn` 순서, `"ReadableSword"` 이름 결합. 리플렉션 테스트도 가장 많다.

S9는 **아직 일정에 넣지 않는다.** 아레나를 씬에 authoring하면 `SceneLayout_*.json`이 레이아웃
정본 자리를 잃는다. 기획자 소유 데이터의 소유권 이동이라 기술 판단이 아니다.

### 수치 전환 — 13단계

`AUDIT_NUMBERS.md`가 S0~S13으로 담는다. S2·S3는 위 D1~D3으로 앞당겼다.

| 단계 | 내용 | 상태 |
|---|---|---|
| S1 | `PlayerCombat.json` 누락 키 3개 | **완료** |
| S4 | `CombatTuning.json` 신규 27키 | **완료** |
| S5 | `PlayerResources.json` +9키 (인간성·죽음) | **완료** |
| S6 | `WorldTuning.json` +9키 (공용 사거리·AI 타이밍) | **완료** |
| S7 | 아키타입·챕터 보스 70키 / 21파일 | **완료** |
| S8 | `CutsceneTuning.json` | 남음 |
| S9 | 카메라 감각 | 남음 |
| S10 | 가독성 레이아웃 (`Readability.json` 재생성) | 남음 · **유일한 비추가적 단계** |
| S12 | `DifficultyTuning.json` | 남음 |
| S13 | 결정 사항과 죽은 코드 | 남음 |

완료분 누계 **키 118개 / 파일 24개**. 전부 추가적이었고 출하된 키의 값은 하나도 바뀌지 않았다.
각 단계가 디자인 파일을 수정한 뒤 바이트 동일 복원을 확인했고, 새 파일이 실제로 읽히는지는
값을 바꿔 읽어보는 방식으로 증명했다 — 아무도 읽지 않는 JSON은 리터럴보다 나쁘다. 튜닝처럼
보이기 때문이다.

남은 S10만 기존 디자인 파일의 내용을 다시 쓴다. 그 단계는 키만 늘고 값은 그대로임을 diff로
증명해야 한다.

## 의존과 병렬

- S5(공용 마커)는 S6·S7보다 **먼저** 끝나야 한다. 둘 다 `MarkerDisc`/`WorldLabel`/
  `AttackReadout`/`WorldHealthBar`를 인스턴스화한다.
- S2는 S3·S4보다 먼저. 둘 다 `MenuTheme.tres`를 쓴다.
- 같은 파일을 두 단계가 동시에 건드리지 않게 한다. 이 계획을 병렬로 돌리다 한 번 사고가
  났다 — 읽기 전용 감사 에이전트가 다른 에이전트의 정당한 작업을 침범으로 오인해 되돌렸다.
  **동시 작업 시 각 담당에게 소유 파일 목록을 명시한다.**

## 남겨둔 작은 빚

전환 중 나온 것들. 지금 고치면 범위를 넘거나 검증되지 않은 시각적 변화를 만든다.

- **씬 경로 `const`가 호출부마다 중복 선언**돼 있다(6개). 작은 static 클래스 하나면 정리된다.
  그 파일이 어느 단계의 소유도 아니라 미뤘다.
- **`AttackReadout` 안의 `GameplayTelegraphPulse`가 작동하지 않는다** — 이식 결함이다.
  `PORT_STATUS.md`에 기록. 고치면 여섯 개가 눈에 띄게 뛰기 시작하므로 사람이 먼저 봐야 한다.
- ~~**`TitleMenuBootstrap.CreateButton`이 번역된 텍스트로 노드 이름을 짓는다.**~~ S4에서 해결.
  이름은 이제 `<현지화 키> + 역할`(`UI_TITLE_QUITButton`, `UI_OPTION_VSYNCToggle`,
  `UI_OPTION_MSAARow`)이고 테스트도 캡션이 아니라 그 이름으로 찾는다.
- **`WorldHealthBar.tscn`의 루트는 `HealthBar`다** (감사 §6은 컴포넌트를 루트로 그렸다).
  바 컴포넌트를 스포너가 `EnsureComponent`로 붙이는 구조라 컴포넌트를 루트로 하면 인스턴스화
  자체가 불가능하다. ~~S8이 액터 트리를 씬으로 옮기면 그때 접힌다.~~ **S8에서 접히지 않았다.**
  액터 씬은 `GameplayWorldHealthBar` 컴포넌트 노드 아래에 `HealthBar`를 인스턴스로 두는 모양을
  그대로 authoring 했다. 컴포넌트를 루트로 올리면 두 스포너와 여러 테스트가 부르는
  `AddHealthBar`가 깨진다. 빚은 남는다.
- **씬 경로 `const`가 6개 늘었다** (S8의 액터 씬 7종). 위의 중복 `const` 항목과 같은 빚이다.
- **`GameplayBuildShim.NewObject`/`AddComponent`/`EnsureComponent`가 살아남았다.** 감사는 S8에서
  없어진다고 봤지만 `Scripts/Core`는 S8의 소유가 아니었고, `EnsureComponent`는 스포너에 맨
  `Node2D`를 넘기는 테스트가 여전히 의존하며, 테스트 7건이 `AddComponent`로 직접 액터를 만든다.
  shim 은퇴는 별도 단계다.

## 알려진 마찰

- **테스트 19개가 런타임 생성 계층을 이름·경로·리플렉션으로 파고든다** (AUDIT_SCENES_UI §8).
  씬으로 옮기면 이름이 바뀌는 순간 깨진다. 인스턴스는 씬 루트 이름이 아니라 레이아웃이 준
  이름을 가져야 한다.
- **`TitleGraphicsOptionsTests`가 `button.GetThemeColor("font_color")`로 켜진 칸을 식별한다.**
  스타일이 Theme으로 가는 순간 깨진다 — 켜짐 표시만은 인스턴스별 override로 남겨야 한다.
- `GameplayVisualFactory`와 `DebugVisualization`은 감사에서 **무혐의**다. 전자는 `Texture2D`
  팩토리이고 후자는 즉시 모드 `_Draw`다. 씬으로 만들 대상이 아니다.

## 하지 않는 것

- `Resources/Design/*.json`의 값을 코드에 맞추려고 고치지 않는다. JSON이 틀렸다고 판단되면
  기록만 남긴다 — 기획 판단이다.
- 에디터 도구(`addons/mygame_tools/`)의 코드 생성 UI는 제품 범위 밖이다. 기록만 한다.
- 감사를 읽었다는 이유로 전체 리팩터링을 시작하지 않는다. 이 문서의 단계만 따른다.
