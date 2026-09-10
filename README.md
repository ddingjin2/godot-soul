# MyGame (Godot)

Godot 4.7.2 (.NET) / Windows / 싱글플레이 2D 소울라이크 버티컬 슬라이스.
Unity 6 프로젝트 [ddingjin2/MyGame](https://github.com/ddingjin2/MyGame)를 이식한 것으로,
**감정 없는 이식이 아니라 동작 보존 이식**입니다. 원본의 설계 의도와 주석을 함께 옮겼고,
바뀐 부분은 전부 [PORT_STATUS.md](PORT_STATUS.md)에 이유와 함께 기록했습니다.

상용 출시 상태가 아니며 사람이 직접 플레이해 재미를 확인한 단계도 아닙니다.

## 바로 실행

Godot 4.7.2 **.NET(mono) 빌드**로 `project.godot`을 열고 **F5**. `main_scene`은
`res://Scenes/TitleScene.tscn`입니다.

에디터 없이:

```powershell
tools/build.ps1                                                          # dotnet build
tools/godot.ps1 --headless --quit-after 300 res://Scenes/GameplayScene.tscn   # 스모크 실행
tools/godot.ps1                                                          # 에디터 열기
```

`tools/godot.ps1`이 winget 설치 경로를 스스로 찾습니다. 다른 위치에 설치했다면
`$env:GODOT_BIN`으로 덮어씁니다. C# 프로젝트라 **`dotnet build`가 먼저 성공해야** 에디터가
스크립트를 붙입니다.

## 조작

| 입력 | 동작 |
|---|---|
| A/D, ←/→ | 이동 |
| Space | 점프 |
| Left Shift | 회피 |
| J, 좌클릭 | 공격 |
| Alt + 좌클릭 | 강공격 |
| K, 우클릭 | 패링 |
| 1 ~ 7 | 죄악 공명 (분노·나태·교만·탐식·탐욕·질투·색욕) |
| E | 상호작용 (화톳불·지름길 문·관문) |
| R | 회복 |
| Tab, 휠클릭 | 대상 고정 |
| Esc | 일시정지 |

바인딩 정본은 `project.godot`의 `[input]` 20개 액션이며, 코드는 `GameplayInput`을 통해서만
읽습니다. 자세한 유래는 [PORTING_GUIDE.md](PORTING_GUIDE.md#input).

## 저장 위치

진행·설정 모두 `user://playerprefs.cfg` (Windows에서는
`%APPDATA%\Godot\app_userdata\MyGame\`). Unity `PlayerPrefs`를 Godot `ConfigFile`로 옮긴
것이라 키 이름과 API가 원본과 같습니다. **테스트도 같은 파일을 씁니다** — 저장을 건드리는
스위트는 `[SetUp]`에서 지웁니다.

## 검증

```powershell
tools/run-tests.ps1            # 전체, exit 0 = green
tools/run-tests.ps1 -Filter X  # 클래스 또는 메서드 이름 부분 일치
```

필터를 쓸 때는 반드시 `run-tests.ps1`을 거칩니다 — `godot.ps1`을 직접 부르면 PowerShell이
`--` 뒤 인자를 잃습니다.

**한 번에 하나씩 돌립니다.** Godot 에디터 락은 없지만 `user://playerprefs.cfg`가 실행 전체가
공유하는 단일 파일이고, 저장을 건드리는 스위트는 `[SetUp]`에서 그걸 지웁니다. 동시에 돌리면
서로의 픽스처를 무너뜨립니다.

**최신 결과: 206 passed / 1 failed / 1 skipped (약 11분).**

- 실패 1건 `EveryChapterLayout_KeepsItsPlacementsAndBonfires`는 **이식 결함이 아닙니다.**
  챕터당 적 배치 20개 이상을 요구하는데 디자인 JSON은 7개이고, 그 JSON은 Unity 원본과
  바이트 동일합니다. 같은 데이터에서 Unity에서도 red였습니다. 채우는 것은 기획 작업입니다.
- 스킵 1건은 프리팹을 이식하지 않아 비교 대상이 사라진 테스트입니다.
- 자동 검증 통과와 사람의 재미 검증은 다릅니다. 후자는 아직입니다.

## 현재 상태

- C# 224파일 / 42,008줄. Unity 원본 169파일 34,315줄 전량 이식.
- 게임이 headless로 부팅해 아레나를 만들고, 스크립트 에이전트가 실제 InputMap을 눌러
  챕터를 주파하고 첫 적을 처치하는 것까지 확인했습니다.
- 남은 런타임 경고 1건: `SpriteFrameAnimator`가 플레이어 프레임을 콜라이더의 136%로 보고합니다.
  Unity에서는 스프라이트 임포트 ppu가 가려주던 콘텐츠 불일치이며 크래시가 아닙니다.
- **Godot 제작 규칙 전환이 일정에 있던 단계까지 끝났습니다.** 규칙 3(UI는 씬에 배치)과 규칙 2
  (재사용 단위는 씬)는 제품 코드에서 완결됐습니다 — 런타임 UI 조립이 남아 있지 않고, 씬 22개와
  Theme 1개가 약 1,000줄의 생성 코드를 대신합니다. 규칙 1(수치 데이터화)도 완결됐습니다 — 디자인
  파일 27개, 키 190개가 코드 리터럴을 대신하며 값은 출하 당시 그대로입니다. 그 뒤 완료 기준의 나머지
  절반(코드 fallback 사본 제거, 중복 보관 제거)을 닫는 2차 전환이
  [docs/migrations/scene-data/PLAN_CLOSEOUT.md](docs/migrations/scene-data/PLAN_CLOSEOUT.md)에 승인돼
  있고 K0까지 착지했습니다. 다음 시작점은 `SESSION_HANDOFF.md`입니다.
- **전환이 실제 결함 6건을 찾아냈습니다.** 넉백이 400px 대신 3px로 나가던 것, 챕터 보스 처치
  보상이 0 소울이던 것 등. 전부 컴파일러도 기존 스위트도 잡지 못하던 것들이며 내역은
  [PORT_STATUS.md](PORT_STATUS.md)에 있습니다.
- Windows export template과 배포용 EXE는 없습니다. 다른 PC 검증도 남아 있습니다.

## 문서

| 파일 | 내용 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | 코드베이스 지도, 지켜야 할 규약, 함정 |
| [AGENTS.md](AGENTS.md) | 이 저장소에서 일할 때의 작업 규칙 |
| [SESSION_HANDOFF.md](SESSION_HANDOFF.md) | 다음 세션이 가장 먼저 읽을 체크포인트 |
| [PORTING_GUIDE.md](PORTING_GUIDE.md) | Unity → Godot 변환 계약 (단위·축·API 대응표) |
| [PORT_STATUS.md](PORT_STATUS.md) | 동작 차이 전량, 이식이 만든 버그와 수정 내역 |
| [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md) | 원본과 달라진 시그니처 |
| [docs/migrations/scene-data/](docs/migrations/scene-data/) | 제작 규칙 감사와 전환 계획 |
| [tools/README.md](tools/README.md) | 스크립트 사용법 |

## 소유 구분

`Resources/Design/*.json`은 기획자 소유의 수치 정본이며 문서에 적힌 어떤 숫자보다 우선합니다.
`Resources/Art/`, `Resources/PixelActors/`의 PNG는 전부 생성된 플레이스홀더입니다.
