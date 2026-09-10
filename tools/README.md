# tools

세 개의 PowerShell 스크립트뿐이다. Unity 쪽의 `run-p0-tests.ps1` / `run-playmode-tests.ps1`과
그 둘이 다투던 에디터 락은 이식에서 사라졌다 — headless 실행은 아무것도 점유하지 않으므로
여러 개를 동시에 돌려도 된다.

## `godot.ps1`

Godot 4.7 .NET 실행 파일을 찾아 이 프로젝트에 대고 실행한다. winget 설치는 관리자 권한 없이는
PATH 별칭을 만들지 못하므로 경로 해석을 여기 한 곳에 모았다. `$env:GODOT_BIN`으로 덮어쓸 수
있다.

```powershell
tools/godot.ps1                                                    # 에디터
tools/godot.ps1 --headless --import                                # 에셋 임포트만
tools/godot.ps1 --headless --quit-after 300 res://Scenes/GameplayScene.tscn
```

`--quit-after`로 중간에 끊은 실행은 "정상 종료"가 아니다. 검증으로 쓰지 않는다.

## `build.ps1`

`dotnet build`를 돌리고 오류 줄만 추린다. Godot 없이 컴파일 상태를 보는 가장 빠른 길이며,
C# 프로젝트라 **에디터를 열기 전에 성공해야** 스크립트가 노드에 붙는다.

```powershell
tools/build.ps1          # 실패 시 최대 40줄 + exit 1
tools/build.ps1 -Quiet   # 카운트만
```

## `run-tests.ps1`

빌드한 뒤 `res://Tests/TestMain.tscn`을 headless로 실행한다. exit 0 = green.

```powershell
tools/run-tests.ps1
tools/run-tests.ps1 -Filter Checkpoint     # 클래스 또는 메서드 이름 부분 일치
```

**필터는 반드시 이 스크립트를 거친다.** `godot.ps1`에 `-- --test-filter=X`를 직접 넘기면
PowerShell이 `--`를 삼켜 전체 스위트(약 11분)가 돈다.

전체 실행은 `user://playerprefs.cfg`를 건드린다. 실제 저장으로 쓰던 슬롯이 있으면 먼저
백업한다.

## 없는 것

에셋 생성 도구는 여기가 아니라 에디터 도크(`addons/mygame_tools/`)에 있다 — 디자인 CSV 왕복,
스프라이트 베이킹, 챕터 씬 생성, 임포트 설정. Godot 에디터를 열어야 쓸 수 있고, 그러려면
`build.ps1`이 먼저 green이어야 한다.
