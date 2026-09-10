# 인수인계 — 2026-09-10 (2차)

새 세션은 이 파일부터 읽는다. 그다음 `README.md`의 현재 상태, 그다음 `AGENTS.md`.

## 지금 어디까지 왔나

Unity 6 → Godot 4.7.2 이식이 끝났고, Godot 제작 규칙 4개로의 전환도 **일정에 있던 단계는 전부
끝났다.**

| 축 | 상태 |
|---|---|
| 이식 자체 | 완료. 스위트 211 passed / 1 failed / 1 skipped (실패 1건은 이식 전부터 red였던 레이아웃 개수 어서션) |
| 규칙 3 — UI는 씬에 배치 | 완결 |
| 규칙 2 — 재사용 단위는 씬 | 완결. `GameplayBuildShim` 퇴역은 **하지 않기로 결정** — 아래 |
| 규칙 1 — 수치 데이터화 | **완결.** S1·S4~S10·S12·S13 전부 착지. 누계 키 190개 / 디자인 파일 27개 |
| 규칙 4 — 일회성 테스트 격리 | 지켜지는 중 |

정본 계획은 [docs/migrations/scene-data/PLAN.md](docs/migrations/scene-data/PLAN.md). 감사 §3의
결정 19건 결과는 `AUDIT_NUMBERS.md` §3 첫머리 표에 있다.

## 브랜치

`master`가 이식 본체, `refactor/godot-scene-data`가 전환 브랜치. **병합하지 않았고 push한 적도
없다.** 원격 없음. 이번 세션 커밋 8개, 각 커밋이 풀 스위트 green으로 착지했다.

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
  `PLAN.md` "남겨둔 작은 빚"과 클래스 doc-comment.

## 다음에 할 일

1차 전환의 단계는 끝났다. 정본 스킬의 완료 기준("코드 fallback 하드코딩 없음", "중복 보관 없음")에
대조하면 잔여가 있고, 그 2차 계획이 [docs/migrations/scene-data/PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md)에
있다. 결정 D1~D8은 전부 추천안으로 **승인됐다.** K0(누락 = 오류 + 부팅 중단)는 착지했다 — 다음은
**K1**(테스트 12파일을 fallback 경로에서 떼어내기, 최고 위험, 단독). K1 전에 K2~K4를 시작하면 스위트의
맨손 액터가 전부 오류로 죽는다. 그 대조에서 나온 S10의
실수(맥동·바운스 6값이 씬 소유라 적었으나 코드 기본값뿐이었음)는 `0ef1d88`에서 고쳤다.

그 밖에 남은 것은 전부 **사람의 판단이 필요한 것**이다.

1. **씬 S9 — 아레나를 챕터 셸에 배치.** 아레나를 씬에 넣으면 `SceneLayout_*.json`이 레이아웃
   정본 자리를 잃는다. 기획자 소유 데이터의 소유권 이동이라 **물어본 뒤에** 한다.
2. **`master`로의 병합 여부.** 커밋 28개가 전환 브랜치에만 있다.
3. **씬이 거울로 들고 있는 값.** 액터 씬은 콜라이더 반지름·비주얼 스케일·바 오프셋을 authoring
   하지만 스포너가 `ReadabilityLayout.json` 값으로 매 스폰 덮어쓴다 (씬 S8의 의도된 결정). 씬 값은
   절대 이기지 않는 거울이다. 지금은 기록만 했다 — 씬에서 지우든 스포너 덮어쓰기를 멈추든 결정이
   필요하다.

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
