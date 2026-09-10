# Scene / UI compliance audit — `godot-soul`

Read-only audit, 2026-09-10. Nothing in this repository was changed except this file.

**Standard audited against** — the user-approved Godot production rules, held as
`C:\Users\pshye\.claude\skills\godot-cli-control\SKILL.md` (§2, §3) and as project rules in
`C:\dev\project-godot\AGENTS.md` ("Godot 제작 규칙 — 2026-09-09 사용자 승인", items 2 and 3).

- **Rule 2** — anything reusable is a saved `.tscn` / `PackedScene`, instanced. Copying node-building
  code or duplicating a detached template node is not reuse. Non-visual shared data → Resources;
  pure logic → shared scripts; do not wrap plain data or functions in empty nodes.
- **Rule 3** — the Control hierarchy of every screen and UI component lives in a `.tscn`, viewable in
  the editor without running the game. `Button.new()` / `Label.new()` / `Control.new()` and runtime
  builders are forbidden, `@tool` generation included. Anchors, offsets, Containers, size flags,
  Theme and default focus belong in the scene/resource. Scripts do binding, state/text updates,
  signals and behaviour. Variable-length lists instantiate a pre-authored item `.tscn`. A single
  empty root scene with the real screen generated in code is a violation, not compliance.

**Scope note.** Rules 1 (data assets) and 4 (one-off tests) are out of scope and were not assessed.

---

## 0. Headline

| | Count |
|---|---|
| Rule 3 violations (UI built in code), product code | **32 constructs** across 6 files |
| Rule 3 violations, editor tooling (`addons/`) | 5 constructs across 2 files (separate verdict, §5) |
| Rule 2 violations (reusable hierarchy built in code / duplicated) | **47 constructs** across 21 files |
| Constructs that break **both** rules | 18 |
| Distinct violating constructs | **61** (product code) |
| `PackedScene` instances anywhere in `Scripts/` | **0** |
| `.tscn` files in the repository | 10, total **60 lines** (9 shells + the test boot scene) |
| `.tscn` files the migration plan calls for | **28 new scenes + 1 Theme resource** (+ an optional Stage 9 that rewrites the 9 existing shells) |

The project contains no `PackedScene` type reference, no `ResourceLoader.Load<PackedScene>`, no
`.Instantiate()` and no `preload` of a `.tscn` anywhere under `Scripts/`. Every node tree in the
game — two screens, one overlay, one arena, six actor archetypes, three effects — is produced
imperatively at runtime. The only matches for "prefab" and "Instantiate" in the source are
doc-comments describing the *Unity* original (`Scripts/Core/GameplayBuildShim.cs:66`,
`Scripts/Gameplay/GameplayEnemySpawner.cs:395`, `Scripts/Gameplay/SoulPickup.cs:76`,
`Scripts/Enemy/EnemyProjectilePool.cs:18`, `:31`, `Scripts/Enemy/EnemyStateMachine.cs:388`,
`Scripts/Gameplay/GameplayEnvironmentBuilder.cs:61`).

### Deliberate port decision *and* non-compliant

This is the most important framing in the audit, and it applies to almost every finding below.

`PORTING_GUIDE.md:159-168` ("Scenes and prefabs") is an explicit, written instruction:

> The Unity project builds essentially everything from code - its scenes are 314-line shells and only
> six prefabs exist. **That stays true here.** Do not author `.tscn` content that the Unity source
> built at runtime; port the builder … and let it build nodes in `_Ready`.

`PORT_STATUS.md` repeats it ("Prefabs and ScriptableObject `.asset` files — the port keeps the Unity
project's own rule … the spawners build actors in code"). So none of these constructs is an accident
or an oversight: **every one of them is a faithful port executing a documented decision, and every
one of them is a violation of Rules 2 and 3 as they stand today.** The conflict is between two
approved documents, not between the code and its author's intent. Resolving it is a product
decision, not a bug fix — which is why the plan in §6 is staged and why Stage 9 is marked as needing
an explicit ruling rather than being scheduled.

Two consequences worth stating plainly:

1. Rule 3 has **no** Unity-fidelity defence. The Unity original used uGUI prefabs and a serialised
   `Canvas`; the port replaced authored uGUI assets with `new Label()` because Godot's `.tscn` was
   deemed out of scope, not because Unity built its HUD in code. `GameplayHud.cs:10-12` says so
   itself: *"The gameplay HUD, built entirely in code. The Unity original was a MonoBehaviour on the
   screen-space Canvas."* The Canvas was authored. The port's HUD is not. This is the one area where
   the port is *less* asset-driven than its source.
2. Rule 2 partly is defensible — but not for the three `Duplicate()` templates (§4). Those are
   prefabs by another name, implemented worse than a `PackedScene` would be.

---

## 1. `Scenes/*.tscn` — what each shell contains today

Nine scenes, all six lines, 54 lines total. Eight of them are byte-identical except for the filename.

| File | Contents |
|---|---|
| `Scenes/GameplayScene.tscn` | `[gd_scene load_steps=2 format=3]`; one ext_resource, `Scripts/Gameplay/GameplayBootstrap.cs`; one node `GameplayRoot` (`Node2D`) with that script. Nothing else. |
| `Scenes/Chapter02_Orange.tscn` … `Scenes/Chapter08_White.tscn` (7 files) | Identical to `GameplayScene.tscn` — same script, same root node name `GameplayRoot`, same type. The chapter identity lives entirely in the *filename*, which `GameplayBuildShim.ActiveSceneName` (`Scripts/Core/GameplayBuildShim.cs:45-58`) turns back into a string and `GameplaySceneDefaults.CreateForScene` uses to pick `Resources/Design/SceneLayout_<Name>.json`. |
| `Scenes/TitleScene.tscn` | One ext_resource, `Scripts/UI/TitleMenuBootstrap.cs`; one node `TitleRoot` (`Node` — not even a `Control`) with that script. |
| `Tests/TestMain.tscn` | The headless test boot scene (out of scope). |

`addons/mygame_tools/ChapterSceneCreator.cs:84-89` is the generator that emits these shells; its
`Shell()` returns exactly that six-line string. Under Rule 3's closing sentence — *"빈 루트 씬 하나만
두고 실제 화면을 코드로 생성하면 위반이다"* / "A single empty root scene with the real screen generated
in code is a violation, not compliance" — `Scenes/TitleScene.tscn` is the textbook case: a bare root
node whose `_Ready` (`Scripts/UI/TitleMenuBootstrap.cs:59-62`) builds the entire title screen.

---

## 2. Rule 3 — UI built in code

### 2.1 `Scripts/UI/GameplayHud.cs` (1134 lines) — 15 constructs

Entry point: `GameplayHudSpawner.Spawn` at `Scripts/Gameplay/GameplayHudSpawner.cs:19` does
`new GameplayHud { Name = "HUD" }`, parents it to the scene root (`:20`) and calls `hud.CreateUi()`
(`:23`). `CreateUi` is `Scripts/UI/GameplayHud.cs:132`.

**The node tree it produces** — this is what a `.tscn` would have to contain:

```
HUD  (GameplayHud : CanvasLayer, ProcessMode = Always)          GameplayHudSpawner.cs:19
├─ GameplayVictoryController                                    GameplayHudSpawner.cs:29
├─ GameplayPauseController                                      GameplayHudSpawner.cs:33
└─ HudRoot  (Control, FullRect, MouseFilter=Ignore)             GameplayHud.cs:136-141
   ├─ HUDBackground   (ColorRect #000 a0.45, 280x350 @ 10,-30)  GameplayHud.cs:209 / PlaceRect :218
   ├─ HealthGauge     (ColorRect #0A0B0E a0.85, 240x26)         GameplayHud.cs:148 → CreateBar :720-736
   │  ├─ GhostFill    (ColorRect Bone100 a0.30, FullRect)       GameplayHud.cs:156, MoveChild(...,0) :157
   │  └─ Fill         (ColorRect Ember300 a0.55, FullRect)      GameplayHud.cs:735 → CreateFill :739-750
   ├─ StaminaGauge    (ColorRect) └─ Fill (ColorRect Cold200)   GameplayHud.cs:149
   ├─ PoiseGauge      (ColorRect) └─ Fill (ColorRect Bone300)   GameplayHud.cs:150-151
   ├─ HealthText, HumanityText, ResonanceText, ActiveSinText,
   │  DeathCountText, SpiritStateText, StaminaText, PoiseText,
   │  SoulsText, FlaskText, ActionText   (Label ×11)            GameplayHud.cs:159-169 → CreateText :765-780
   ├─ WarningText     (Label, centre-anchored, Ember300 28pt)   GameplayHud.cs:173
   ├─ VictoryPanel    (ColorRect #0A0B0E a0.94, FullRect, Stop, hidden)   :223 → CreatePanel :235-248
   │  ├─ VictoryTitle     (Label 58pt #B8A57A)                  :225-226 → CreateVictoryText :250-264
   │  ├─ VictorySubtitle  (Label 24pt)                          :227
   │  ├─ RestartButton    (Button 280x56)                       :229 → CreateVictoryButton :266-273
   │  └─ TitleButton      (Button 280x56)                       :230
   ├─ LevelUpPanel    (ColorRect a0.92, hidden)                 :482
   │  ├─ LevelUpTitle     (Label 44pt)                          :484
   │  ├─ LevelUpSouls     (Label 24pt Cold200)                  :487
   │  ├─ LevelUpVitalityButton / …Endurance… / …Strength… /
   │  │  …ResolveButton   (Button ×4, 520x56, font_size 20)     :495-513
   │  └─ LevelUpCloseButton (Button)                            :515
   ├─ PausePanel      (ColorRect a0.88, hidden)                 :320
   │  ├─ PauseTitle (Label 48pt) / PauseStatus (Label 20pt)     :322, :324
   │  └─ ResumeButton / SaveButton / PauseTitleButton (Button)  :327-329
   └─ GateTravelPanel (ColorRect a0.92, hidden)                 :382
      ├─ GateTravelTitle   (Label 44pt)                         :384
      ├─ GateList          (Control, FullRect, Ignore)          :387-389
      │  └─ Gate0Button … GateNButton (Button, 0..8, rebuilt)   :452 in BuildGateRows :437-473
      └─ GateTravelCloseButton (Button)                         :391
```

Screens/components produced: **one screen (the HUD) plus four modal panels**, 45–53 nodes depending
on how many gates the save has opened.

**Violating constructs** (file:line → what it is → why it breaks Rule 3):

| # | Site | Construct | Breach |
|---|---|---|---|
| 1 | `GameplayHud.cs:132` | `CreateUi(CanvasLayer)` — the whole-screen runtime builder | "런타임 빌더로 화면 계층을 만드는 방식을 금지" |
| 2 | `:136` | `new Control { Name = "HudRoot" }` + `SetAnchorsPreset` at `:137` | `Control.new()` + anchors in code |
| 3 | `:191-202` | `PlaceRect(...)` writes `AnchorLeft/Top/Right/Bottom` and all four `Offset*` | "앵커, offset … 은 씬/리소스에서 설정한다" — this method exists *only* to do in code what the scene inspector does |
| 4 | `:207-219` | `CreateBackground` → `new ColorRect` | Control built in code |
| 5 | `:235-248` | `CreatePanel` → `new ColorRect`, `MouseFilter`, `SetAnchorsPreset(FullRect)` | The modal plate, built 4× |
| 6 | `:250-264` | `CreateVictoryText` → `new Label` + `LabelSettings` + `PlaceRect` | Built 7× |
| 7 | `:266-273` | `CreateVictoryButton` → `new Button` | `Button.new()`; 9 call sites |
| 8 | `:285-302` | `StyleMenuButton` — five `AddThemeStyleboxOverride` + font + five `AddThemeColorOverride` | "Theme … 은 씬/리소스에서 설정한다"; this is a Theme resource written as code |
| 9 | `:304` | `Plate(Color) => new StyleBoxFlat { … }` | StyleBox constructed at runtime |
| 10 | `:318-331` | `CreatePausePanel` | Screen built in code |
| 11 | `:221-232` | `CreateVictoryPanel` | Screen built in code |
| 12 | `:378-393` | `CreateGateTravelPanel` + `new Control { Name = "GateList" }` at `:387` | Screen built in code |
| 13 | `:437-473` | `BuildGateRows` — the variable-length gate list, each row `new Button` via `:452` | Directly against "목록처럼 개수가 변하는 UI는 미리 작성한 항목 `.tscn`을 `PackedScene.instantiate()`하여 데이터만 바인딩한다" |
| 14 | `:480-518` | `CreateLevelUpPanel`, four rows in a loop, `PlaceRect` override at `:506`, `AddThemeFontSizeOverride` at `:507` | Screen + list built in code |
| 15 | `:720-780` | `CreateBar` / `CreateFill` / `CreateText` — the three leaf builders | Control components built in code |

Not a violation, for the record: `_levelUpButtons = new Button[LevelUpStats.Length]` at `:490` is a
C# array allocation, not node construction.

**Same component built more than once** (the reusable-scene candidates):

| Component | Built by | Instances |
|---|---|---|
| Full-screen modal ink plate | `CreatePanel` `:235` | 4 (`:223`, `:320`, `:382`, `:482`) |
| Centred panel text line | `CreateVictoryText` `:250` | 7 (`:225`, `:227`, `:322`, `:324`, `:384`, `:484`, `:487`) |
| Menu button on an ink plate | `CreateVictoryButton` `:266` | 9 (`:229`, `:230`, `:327`, `:328`, `:329`, `:391`, `:452`, `:501`, `:515`) — **and a tenth, independent implementation in `TitleMenuBootstrap.cs:377`** |
| HUD gauge (strip + fill) | `CreateBar` `:720` | 3 (`:148`, `:149`, `:150`) |
| Bar fill layer | `CreateFill` `:739` | 4 (3 live fills + the ghost at `:156`) |
| HUD text row | `CreateText` `:765` | 12 (`:159`–`:173`) |
| Level-up stat row | loop `:495-513` | 4 |
| Gate travel row | loop `:445-472` | 0–8, save-dependent |

**Data the script binds at runtime — must stay as binding, must not move into the scene:**
`Health.CurrentHealth/MaxHealth` (`:869`, fill ratio `:871`), `HumanityController.CurrentHumanity`
(`:922`), `SinResonanceController.CurrentResonance/CurrentSin` (`:928`, `:942`) plus the per-sin
colour table `GetSinColor` (`:1057-1076`), `DeathStateController.DeathCount/IsInSpiritState`
(`:950`, `:957`), `StaminaSystem.CurrentStamina/MaxStamina` (`:974`), `Poise.CurrentPoise/MaxPoise/
CanBreak` and the staggered recolour (`:978-1009`), `SoulsWallet.Souls` (`:1017`),
`PlayerController2D.HealCharges/MaxHealCharges/IsGrounded/IsStaggered` (`:1028-1054`),
`PlayerActionController.CurrentAttackPhase` (`:1052`), and every string on the level-up panel, all of
which is read back out of `PlayerProgression` and `ProgressionTuningData` (`:578-621`) — the file's
own comment at `:572-577` correctly insists none of those numbers may be literals. The gate list's
`gates` / `titles` / `currentScene` arrays (`:407`) are runtime data too. The ghost-gauge and
hit-flash timers (`:56-61`, `:893-917`) are behaviour and stay in script.

**Data that must move into the scene/Theme:** every anchor and offset passed to `PlaceRect`, all 6
palette colours (`:22-27`), the 5 stylebox states and 5 font colours per button (`:285-302`), font
sizes 18/20/22/24/28/44/48/58, the `280x350` plate, `240x26` bar, `560x70` text, `280x56` button and
`520x56` level-up row rectangles, the `62f`/`70f` row steps (`:442`, `:492`), and the four panels'
alphas (0.94 / 0.92 / 0.92 / 0.88).

### 2.2 `Scripts/UI/TitleMenuBootstrap.cs` (556 lines) — 12 constructs

`_Ready` (`:59-62`) calls `BuildTitleUi` (`:82`). Root shell is `Scenes/TitleScene.tscn`, a bare
`Node`.

```
TitleRoot  (Node, script = TitleMenuBootstrap)              Scenes/TitleScene.tscn
└─ TitleCanvas  (CanvasLayer)                               TitleMenuBootstrap.cs:86
   └─ TitleRoot (Control, FullRect, Ignore)                 :92-95
      ├─ Backdrop      (ColorRect #06070A, FullRect)        :72-79
      ├─ Title         (Label "MyGame" 54pt Bone100, 520x72 @ 0,165)   :113-116
      ├─ Subtitle      (Label "Wrath altar" 18pt Bone300, 520x32)      :118-121
      ├─ MenuButtons   (VBoxContainer 260x250, separation 14, Centre)  :126-130
      │  ├─ 새로하기Button   (Button 260x48)   → StartNewGame          :132
      │  ├─ 이어하기Button   (Button, Disabled = !GameSave.Exists)     :135-136
      │  ├─ 난이도Button     (Button, text rewritten at :513)          :142
      │  ├─ 뉴게임+Button    (Button, Disabled = !save.hardUnlocked)   :147-148
      │  ├─ 설정Button       (Button) → ToggleSettings                 :152
      │  └─ 종료Button       (Button) → QuitGame                      :153
      └─ SettingsPanel (ColorRect #0A0B0E a0.96, 440x620, hidden)      :163-169
         └─ SettingsColumn (VBoxContainer, 16px inset all round, sep 8) :174-181
            ├─ Row  (Label "그래픽 옵션" 24pt)                          :183
            ├─ Row  (Label "프리셋: …" 18pt)   ← duplicate node name    :184
            ├─ 상 (High)Button / 하 (Low)Button  (Button ×2)            :186-187
            ├─ 화면 흔들림Toggle / 히트스톱Toggle /
            │  타격 연출Toggle / 수직 동기화Toggle   (CheckBox ×4)      :191-194
            ├─ 안티에일리어싱Row (HBoxContainer 408x48, sep 4)          :196
            │  ├─ Caption (Label 20pt, 180x48, ExpandFill)             :269-272
            │  └─ 끔Button / 2xButton / 4xButton / 8xButton (Button ×4) :282
            ├─ 렌더 스케일Row  (HBoxContainer)                          :200
            │  ├─ Caption (Label)
            │  └─ 60%Button / 80%Button / 100%Button (Button ×3)       :282
            └─ 닫기Button  (Button)                                    :204
```

Screens/components produced: **one screen (title) + one settings panel + 2 segmented rows + 4 toggle
rows**, 27–28 nodes. Note the duplicate node name `Row` emitted twice at `:183`/`:184` (both go
through `CreateRowText` → `CreateText(parent, "Row", …)` at `:370`); Godot silently renames the
second. An authored scene makes that impossible.

| # | Site | Construct | Breach |
|---|---|---|---|
| 16 | `:82-107` | `BuildTitleUi` — whole-screen builder called from `_Ready` | Runtime screen builder |
| 17 | `:86` | `new CanvasLayer { Name = "TitleCanvas" }` | Screen root in code |
| 18 | `:92-94` | `new Control` + `SetAnchorsPreset(FullRect)` + `MouseFilter` | `Control.new()` |
| 19 | `:70-80` | `CreateBackdrop` → `new ColorRect` | Control in code |
| 20 | `:111-122` | `CreateTitle` → 2 Labels + 2 `PlaceRect` | Control + anchors in code |
| 21 | `:124-159` | `CreateButtonStack` → `new VBoxContainer` (`:126`), `AddThemeConstantOverride("separation", 14)` (`:129`), `Alignment` (`:130`), 6 buttons, `newGame.GrabFocus()` (`:158`) | Container, size flags **and the default focus path** in code — Rule 3 names "기본 포커스 경로" explicitly |
| 22 | `:161-208` | `CreateSettingsPanel` → `new ColorRect` (`:163`), `new VBoxContainer` (`:174`) + four hand-written offsets (`:177-180`) + separation (`:181`) | Panel + Container + padding in code |
| 23 | `:210-217` | `CreatePresetButton` | Button in code, ×2 |
| 24 | `:225-250` | `CreateToggleRow` → `new CheckBox` + `CustomMinimumSize` + font/colour overrides | CheckBox + size flags + theme in code, ×4 |
| 25 | `:258-290` | `CreateSegmentedRow` → `new HBoxContainer` (`:260`), caption Label (`:269`), **segment width arithmetic at `:276`**, N buttons in a loop (`:279-287`) | Container layout computed in code; a variable-length row of value buttons built inline instead of instancing an item scene |
| 26 | `:377-385` | `CreateButton` → `new Button` + `CustomMinimumSize` | `Button.new()`, 13 call sites |
| 27 | `:396-412` | `StyleButton` — 5 styleboxes + font + 5 font colours | Theme written as code; **a near-verbatim copy of `GameplayHud.StyleMenuButton:285-302`** (identical multipliers 0.5686275 / 0.32156864 / 0.36078432, identical five slots) |

Not violations: `new Button[values.Length]` (`:278`) is a C# array; `SegmentedRow`
(`:325-353`) is a plain non-node C# class holding button references and a `Func<int>` — correct per
Rule 2's "데이터나 함수 자체를 불필요한 빈 노드로 포장하지 않는다".

**Same component built more than once:** menu button (`CreateButton` `:377`) ×13; toggle row ×4;
segmented row ×2 (which between them instance 7 segment buttons); preset button ×2; row label ×2.
And the cross-file duplication: the *same* menu-button widget is implemented twice, once in each UI
file, with two different names (`StyleMenuButton` / `StyleButton`) and two different plate colours.

**Data bound at runtime (keep as binding):** `GameSave.Exists` (`:136`), `GameSaveData.hardUnlocked`
/ `souls` / `difficulty` / `newGamePlus` (`:146-148`, `:452-471`, `:502-507`),
`DifficultySettings.DisplayName` (`:513`), `GraphicsOptions.{Preset, ScreenShake, HitStop, HitFlash,
VSync, Msaa, RenderScale}` and `MsaaSteps` / `RenderScaleSteps` (`:196-202`, `:294-305`),
`ChapterRoute.Resume/FirstChapterScene` (`:466`, `:527`), the `newGameSceneName` export (`:37`).
`StepLabels` (`:308-322`) produces the segment captions from the step arrays — genuinely dynamic,
stays in script.

**Data that must move into the scene/Theme:** the 5 palette colours (`:18-26`), backdrop `#06070A`
(`:29`), the three layout constants `PanelInnerWidth = 408` / `SegmentCaptionWidth = 180` /
`SegmentSpacing = 4` (`:33-35`) **including the width formula at `:276`, which a Container's size
flags do for free**, every `PlaceRect` rectangle, all `CustomMinimumSize` values, separations 14 and
8, the 16px panel inset, font sizes 18/20/22/24/54, and both button plates (`SegmentIdle`,
`SegmentActive`).

### 2.3 `Scripts/Gameplay/CutsceneOverlay.cs` (184 lines) — 2 constructs

```
CutsceneOverlay  (CanvasLayer, Layer = 100, ProcessMode = Always)   :40-47
├─ Fade          (ColorRect #06070A, FullRect, Ignore)              :109-116
├─ LetterboxTop  (ColorRect, anchors L0 R1 T0 B0)                   :118 → CreateBar :150-163
├─ LetterboxBottom (ColorRect, anchors L0 R1 T1 B1)                 :119
└─ Line          (Label, bottom-centre, 900x80 at −90px)            :121-145
```

| # | Site | Construct | Breach |
|---|---|---|---|
| 28 | `:107-148` | `Build()` — `new ColorRect` (`:109`), `new Label` (`:121`), font/size/colour overrides (`:131-133`), eight hand-written anchor/offset assignments (`:137-144`) | Runtime UI builder; anchors, offsets and theme in code |
| 29 | `:150-163` | `CreateBar` → `new ColorRect` + 6 anchor/offset writes, called twice | Same component built twice |

The doc-comment at `:10-15` is important: in Unity this node held **no logic at all** — Timeline
Animation Tracks addressed its children *by path name*, so the hierarchy was authored and the names
were a contract. The port dropped Timeline and rebuilt the hierarchy in code while keeping the names.
This is the clearest case in the repository of an authored Unity asset becoming runtime code, and it
is also the easiest to reverse: `SetFade`/`SetLetterbox`/`SetLine` (`:73`, `:80`, `:91`) are already
pure binding, and `SetBarHeight` (`:165-182`) is pure behaviour.

**Bound at runtime:** fade alpha, letterbox height and the caption string, all written by
`CutsceneDirector`'s coded sequences. **Moves into the scene:** `Ink950`/`Bone100` (`:26-27`), the
900×80 line rect and its −90px inset, font size 24, `Layer = 100`.

### 2.4 `Scripts/Gameplay/GameplayHudSpawner.cs` — 1 construct

| # | Site | Construct | Breach |
|---|---|---|---|
| 30 | `:19-23` | `new GameplayHud { Name = "HUD" }` → `SceneRoot.AddChild` → `hud.CreateUi()` | The screen's own root node is constructed in code. This is the single call site that a `PackedScene.Instantiate<GameplayHud>()` replaces. |

`:29` and `:33` additionally add `GameplayVictoryController` and `GameplayPauseController` as child
nodes of the HUD; those are behaviour scripts with no Control hierarchy, so under Rule 2 they belong
in the HUD scene as authored child nodes rather than being wrapped separately.

### 2.5 World-space Control nodes — 2 constructs

Two places build a `Control` (`Label`) hierarchy in code for in-world text. They are UI components by
node type even though they live in world space, and they are byte-for-byte the same builder written
twice.

| # | Site | Construct |
|---|---|---|
| 31 | `Scripts/Gameplay/GameplayEnvironmentBuilder.cs:326-351` | `CreateWorldLabel` → `NewObject<Node2D>(label + "Label")` then `new Label { … GrowHorizontal/GrowVertical = Both, Size = Vector2.Zero }` + `AddThemeFontSizeOverride` + `AddThemeColorOverride`. Tree: `<Name>Label (Node2D) └─ Text (Label)`. 3 instances (`:143-145`: CHECKPOINT, DUEL FLOOR, WRATH ALTAR). |
| 32 | `Scripts/Gameplay/GameplayEnemySpawner.cs:586-609` | `CreateRoleMarker` → `new Node2D { Name = "RoleMarker" }` + the *identical* Label block. Tree: `RoleMarker (Node2D) └─ Text (Label)`. 5 instances (`:132`, `:156`, `:179`, `:316`, `:351`). |

Eight instances of one component, two independent implementations, differing only in the node name
and which readability field supplies the font size (`WorldLabelFontSizePx` vs
`RoleMarkerFontSizePx`). Both comments even say the same thing ("Unity's TextAnchor.MiddleCenter…").

---

## 3. Rule 2 — reusable things built in code instead of instanced

### 3.1 `Scripts/Core/GameplayBuildShim.cs` — the enabler (4 constructs, #33–36)

This 142-line file is what makes runtime construction ergonomic enough to be the default everywhere.
It is not itself a Rule 2 breach of the "copy a node tree" kind, but every construct in §3.2–§3.6
routes through it, so retiring it is the measure of the migration being finished.

| # | Site | API | Callers |
|---|---|---|---|
| 33 | `:68-73` | `NewObject<T>(string name, Vector2 position)` — `new T { Name, Position }` then `SceneRoot.AddChild`. The doc-comment at `:60-67` states its whole purpose: *"Same trap Unity's `Instantiate(prefab, position, rotation)` existed to avoid."* It is a hand-rolled `Instantiate` with no prefab. | `GameplayEnvironmentBuilder.cs:184`, `:219`, `:266`, `:311`, `:328`, `:361`; `CutsceneDirector.cs:135` |
| 34 | `:76-81` | `NewObject<T>(string name)` | as above |
| 35 | `:87-92` | `AddComponent<T>(this Node parent, string name = null)` — `new T { Name }` + `AddChild`. Unity's `AddComponent` with no component system underneath: it makes a child *node* per Unity component. | 20 sites in `GameplayPlayerSpawner.cs`, 6 in `GameplayEnemySpawner.cs`, 3 in `GameplayEnvironmentBuilder.cs`, plus 7 in `Tests/` |
| 36 | `:95-98` | `EnsureComponent<T>` — `GetComponent<T>() ?? AddComponent<T>()`. The `?? Add` half is exactly the "does this actor already have the node the prefab would have given it" question that a `.tscn` answers statically. | `GameplayPlayerSpawner.cs:143`, `:147`, `:179`, `:185`, `:190`, `:201`, `:287`, `:293`, `:300`; `GameplayEnemySpawner.cs:517`, `:521`, `:529`, `:544`, `:547`, `:553`, `:559`, `:566` |

`SetActive` (`:105-125`) is *not* a violation — it is a correct three-part shim for
`gameObject.SetActive` and has no scene equivalent. Keep it.

### 3.2 `Scripts/Gameplay/GameplayPlayerSpawner.cs` — 4 constructs (#37–40)

`BuildPlayer` (`:211-283`) produces the player actor detached, and `Spawn` (`:73-171`) parents it at
`:90` and then tunes it. The tree:

```
Player  (PlayerMotor2D : CharacterBody2D, layer Player, mask GroundProbe|Enemy)  :216-221
├─ Collider              (CollisionShape2D, CapsuleShape2D r=size.X/2 h=size.Y)  :223-228
├─ Visual                (Sprite2D, ActorPlayer texture, Modulate PlayerColor)   :230-237
├─ Health                                                                        :239
├─ StaminaSystem                                                                 :240
├─ Poise                                                                         :241
├─ SoulsWallet                                                                   :242
├─ CombatFeedback                                                                :243
├─ AudioFeedback                                                                 :244
│  └─ AudioSource        (AudioStreamPlayer, added on demand)      Combat/AudioFeedback.cs:68
├─ HitboxAnchor          (DamageHitbox2D : Area2D, layer PlayerHitbox)           :250-257
│  ├─ ReadableSword      (Sprite2D, Sword texture, rotated)        CreatePlayerSword :308-333
│  │  └─ PlayerAttackAnimator2D                                                  :326
│  │     └─ AnimationPlayer  (empty — no clip in this port)                      :332
│  └─ AttackArc          (Sprite2D, Disc texture)                  CreateAttackReadout :335-345
│     └─ GameplayTelegraphPulse                                                  :344
├─ PlayerActionController                                                        :259
├─ PlayerController2D                                                            :262
├─ HumanityController                                                            :273
├─ SinResonanceController                                                        :274
├─ PlayerInputReceiver                                                           :275
├─ GameplayFallDeath                                                             :276
├─ GameplayWorldHealthBar                                                        :277
│  └─ HealthBar → Frame, Fill → FillSprite      GameplayWorldHealthBar.cs:96-129
├─ DeathStateController                                                          :278
├─ DebugVisualization                                                            :279
├─ GameplaySoulDrop                                                              :280
├─ PlayerLockOn                    (added in Spawn)                              :179
├─ GameplayLockOnMarker            (added in Spawn)                              :185
├─ DamageReceiver                  (EnsureComponent)                             :293
├─ CombatResultBroadcaster         (EnsureComponent)                             :300
└─ ActorAnimationDriver            (attached last)      ActorAnimationDriver.cs:64-65
```

~30 nodes, one instance per run. Every one of them is fixed structure — nothing about the shape
varies with data. This is a `Player.tscn` written as 70 lines of C#.

| # | Site | Construct |
|---|---|---|
| 37 | `:211-283` | `BuildPlayer` — the whole hierarchy. Comment at `:206-210` is explicit: *"Unity built this only when no player prefab existed; there are no prefabs in this port, so it is the only path."* |
| 38 | `:308-333` | `CreatePlayerSword` — 3-node sub-hierarchy (`Sprite2D` → `PlayerAttackAnimator2D` → `AnimationPlayer`) |
| 39 | `:335-345` | `CreateAttackReadout` — `Sprite2D` + `GameplayTelegraphPulse`. **Duplicated verbatim at `GameplayEnemySpawner.cs:569-579`** (identical body, identical parameter list). |
| 40 | `:285-289` | `AddHealthBar` — **duplicated verbatim at `GameplayEnemySpawner.cs:551-555`**. `EnsureDamageReceiver` (`:291-296` vs `Enemy:557-562`) and `EnsureCombatResultBridge` (`:298-301` vs `Enemy:564-567`) are two further verbatim pairs. |

**Bound at runtime (must survive as binding, not move into the scene):** `PlayerResourceData`
(`maxHealth`, `startingHealth`, `startingHumanity`, poise fields, `soulStainPickupDelay`) at
`:80-101`, `:193`, `:164`; `PlayerMovementData` / `PlayerCombatData` via `ApplyTuning` (`:111-114`);
`WorldTuningData.lockOnRange/lockOnBreakRange` (`:181-183`); `scene.PlayerSpawnPosition` (`:89`) and
`scene.FallDeathY` (`:139`); `readability.*` colours and the checkpoint/spirit-platform references
(`:144`, `:148`). **Moves into the scene:** collision layer/mask (`:219-220`), capsule radius/height
derivation (`:224-228`), sprite pivot/size/z (`:231-237`), hitbox local position and layers
(`:250-256`), sword local position/rotation/size/colour/z (`:310-324`), attack-arc rect.

The load-bearing *order* in `Spawn` — `SetDamageHitbox` after the player is in the tree (`:125`,
because `PlayerController2D._Ready` clobbers it; see `CLAUDE.md` Traps), and `PlayerProgression.
EnsureOn` dead last (`:160`, so it captures tuned bases) — is behaviour and **must stay in the
spawner script** after the hierarchy moves to a scene. A `.tscn` fixes the *construction* order trap
(an instanced root arrives complete and detached, exactly as `BuildPlayer` intends) but does not fix
the *tuning* order.

### 3.3 `Scripts/Gameplay/GameplayEnemySpawner.cs` — 9 constructs (#41–49)

| # | Site | Construct | Instances |
|---|---|---|---|
| 41 | `:378-386` | `CreateEnemyRoot<T>` — `new T { Name, CollisionLayer, CollisionMask }` | 5 archetypes |
| 42 | `:405-414` | `AddCapsuleCollider` | 5 |
| 43 | `:416-423` | `AddActorVisual` | 5 |
| 44 | `:122-147` | `CreateMeleeGruntAt` — root + Collider + Visual + `AttackPoint` (`new Node2D` at `:128`) + SlashDanger readout + RoleMarker + Health rig + Poise + SoulsWallet + health bar | 4 per chapter |
| 45 | `:149-170` | `CreateLeapingAttackerAt` — same shape minus AttackPoint | 2 per chapter |
| 46 | `:172-195` | `CreateRangedCasterAt` — same shape plus the projectile template | 1 per chapter |
| 47 | `:307-341` | `CreateChapterBoss` — root + Collider + Visual + RoleMarker + Health rig + Poise + Wallet + hazard template + afterimage template + bar | 1 |
| 48 | `:343-370` | `CreateWrathMiniBoss` — root + Collider + Visual + **two** readouts + RoleMarker + rig | 1 (fallback) |
| 49 | `:515-532` / `:539-549` | `EnsureEnemyHealthRig` / `ApplyEnemyPoiseAndReward` — the shared component set every enemy gets, assembled with `EnsureComponent` | 6 |

The four archetype builders are the same 8-step recipe written four times with different readability
fields. A grunt's produced tree:

```
MeleeGrunt (MeleeGrunt : CharacterBody2D, layer Enemy, mask GroundProbe|Player)
├─ Collider (CollisionShape2D, CapsuleShape2D)
├─ Visual   (Sprite2D, ActorGrunt)
├─ AttackPoint (Node2D @ World.U(0.6), 0)
├─ SlashDanger (Sprite2D, Disc) └─ GameplayTelegraphPulse
├─ RoleMarker (Node2D) └─ Text (Label "Melee")
├─ Health, DamageReceiver, CombatFeedback, CombatResultBroadcaster, EnemyDeathCleanup
├─ Poise, SoulsWallet
├─ GameplayWorldHealthBar └─ HealthBar → Frame, Fill → FillSprite
├─ EnemyGroupCombat        (added by MeleeGrunt.cs:92 if absent)
└─ ActorAnimationDriver
```

**Bound at runtime:** `MeleeGruntData` / `LeapingAttackerData` / `RangedCasterData` /
`WrathMiniBossData` / `RainbowChapterBossData` / `BossEncounterData` from `Resources/Design/*.json`
(`:134`, `:158`, `:183`, `:353`, `:255`, `:280-294`), the per-spawn variant file mechanism
(`SpawnTuning<T>` `:214-245`), spawn positions from `SceneLayout_*.json`, and
`DifficultySettings.EnemyHealthMultiplier` (`:494`). All of that stays exactly where it is — it is
already Rule-1 compliant and is the reason a single `.tscn` per archetype suffices.

### 3.4 `Scripts/Gameplay/GameplayEnvironmentBuilder.cs` — 7 constructs (#50–56)

| # | Site | Construct | Produced tree | Instances per chapter |
|---|---|---|---|---|
| 50 | `:309-324` | `CreateSolidBox` | `StaticBody2D (layer Ground) ├─ Shape (CollisionShape2D, RectangleShape2D) └─ Sprite (Sprite2D, white square, tinted)` | 1 ground + N platforms + spirit platform + shortcut gate — typically 8–20 |
| 51 | `:359-365` | `CreateSceneryPiece` | bare `Sprite2D` | backdrop + N scenery + rims + 2 arena gates — typically 10–20 |
| 52 | `:170-190` | `CreateWorldEdge` | `StaticBody2D └─ Shape` — same shape as #50 minus the sprite | 2 |
| 53 | `:264-273` | `CreateCheckpointAt` | `Node2D ├─ Checkpoint └─ CheckpointZone` (whose `_Ready` then adds `Trigger` + `Marker → Disc`) | 1–N bonfires |
| 54 | `:217-222` | `CreateGatePortal` | `GatePortal (Node2D) └─ GateTravelZone` (which then adds `Trigger` + `Marker → Disc`) | 1 |
| 55 | `:279-291` | `CreateShortcutGate` | `CreateSolidBox` + `ShortcutGate` child (which adds its own `Area2D` trigger) | 0–1 |
| 56 | `:293-302` | `CreateSpiritPlatform` | `CreateSolidBox` + `SetActive(false)` | 1 |

`WarnIfTheCameraCannotFollowTheLevel` (`:95-113`) and the colour/sorting switch helpers
(`:367-385`) are logic, not construction — no finding.

### 3.5 Zones, markers and pickups — 6 constructs (#57–62)

| # | Site | Construct | Tree |
|---|---|---|---|
| 57 | `Scripts/Gameplay/CheckpointZone.cs:213-227` | `EnsureTrigger` → `new CollisionShape2D { Name = "Trigger", Shape = new CircleShape2D }` | on the `CheckpointZone` Area2D |
| 58 | `Scripts/Gameplay/CheckpointZone.cs:233-261` | `EnsureMarker` → `new Sprite2D { Name = "Disc" }` (`:245`) under `new GameplayTelegraphPulse { Name = Marker }` (`:254`), `AddChild(marker)` (`:260`) | `Marker (GameplayTelegraphPulse) └─ Disc (Sprite2D)` |
| 59 | `Scripts/Gameplay/GateTravelZone.cs:292-306` | `EnsureTrigger` — **identical body to #57** | same |
| 60 | `Scripts/Gameplay/GateTravelZone.cs:312-333` | `EnsureMarker` — **identical body to #58**, differing only in `Modulate` (`ArenaGateColor` vs `CheckpointLabelColor`) | same |
| 61 | `Scripts/Gameplay/ShortcutGate.cs:282-296` | `new Area2D { Name = Trigger }` + `new CollisionShape2D { Name = "Reach", CircleShape2D }` | `Trigger (Area2D) └─ Reach (CollisionShape2D)` |
| 62 | `Scripts/Gameplay/SoulPickup.cs:43-81` | `Create` → `new Sprite2D "Stain"` (`:45`) under `new GameplayTelegraphPulse "Pulse"` (`:55`), plus `new CollisionShape2D "Reach"` (`:69`) on a `new SoulPickup` Area2D (`:58`) | `SoulPickup (Area2D) ├─ Reach (CollisionShape2D) └─ Pulse (GameplayTelegraphPulse) └─ Stain (Sprite2D)` |

#58 and #60 are the clearest small-scale Rule 2 breach in the repository: one component ("a pulsing
disc marker on a trigger zone"), two identical implementations 80 lines apart in two files, both
carrying the same explanatory comment. #62 is the same component a third time, with a different child
name.

### 3.6 Health bar, lock-on marker, camera, animation drivers — 5 constructs (#63–67)

| # | Site | Construct |
|---|---|---|
| 63 | `Scripts/Gameplay/GameplayWorldHealthBar.cs:96-129` + `CreateSpriteChild:168-180` | Builds `HealthBar (Node2D) ├─ Frame (Sprite2D) └─ Fill (Node2D) └─ FillSprite (Sprite2D)`. **Note `:103-109` already looks the tree up by name first and only builds it when absent** — the file is written to accept an authored hierarchy and never gets one. Instanced on every actor (6+ per chapter). |
| 64 | `Scripts/Gameplay/GameplayLockOnMarker.cs:84-105` | `BuildMarker` → `new Sprite2D { Name = "LockOnMarker" }` parented to the *scene root*, not the player (`:103`), freed in `_ExitTree` (`:51`) |
| 65 | `Scripts/Gameplay/GameplaySystemBootstrapper.cs:26-27` | `new Camera2D { Name = "Main Camera" }` |
| 66 | `Scripts/Gameplay/ActorAnimationDriver.cs:64-65` | `new ActorAnimationDriver` + `actor.AddChild`; couples to the spawner-built hierarchy by node name — `VectorSwordName = "ReadableSword"` at `:28`, tested at `:120` |
| 67 | `Scripts/Gameplay/SpriteFrameAnimator.cs:69` | `actor.AddChild(animator)` |
| — | `Scripts/Gameplay/CutsceneDirector.cs:135` | `NewObject<CutsceneDirector>` — a single bare node, no hierarchy. Borderline; listed for completeness, not counted. |
| — | `Scripts/Combat/AudioFeedback.cs:68-69` | `new AudioStreamPlayer { Name = "AudioSource" }` on demand. One node; folds into the actor scenes at Stage 8. Not counted separately. |

`Scripts/Gameplay/DebugVisualization.cs` was named in the brief as a suspect. **It is not a
violation.** It constructs no nodes at all: it is a `Node2D` that draws in `_Draw()` (`:58`) with
`DrawArc` / `DrawRect` / `DrawCircle` and calls `QueueRedraw()` each frame (`:52-56`). The `new
Color(...)` / `new Vector2(...)` / `new Rect2(...)` occurrences at `:84`–`:211` are value types.
Immediate-mode drawing is the correct Godot answer to Unity's `OnDrawGizmos` and needs no scene.

`Scripts/Gameplay/GameplayVisualFactory.cs` was also named. **It is not a Rule 2 or 3 violation
either.** It produces `Texture2D` / `ImageTexture` resources (`CreateSquareSprite:117`,
`CreateActorSprite:242`, `CreateDiscSprite:360`, `CreateSwordSprite:395`, `CreateArchSprite:433`),
with baked PNGs from `Resources/Art/` winning over the generator (`LoadBaked:59`). That is a resource
factory, which is what Rule 2 asks for. Its one method that touches nodes, `Dress(Sprite2D, …)`
(`:92-116`), writes `Texture`/`Offset`/`Scale` on a sprite handed to it — those three property writes
are exactly what an authored `.tscn` would carry instead, so `Dress` shrinks to nothing as the actor
scenes land, but the texture generation stays.

---

## 4. The `Duplicate()` templates — prefab by another name (3 constructs, #68–70)

Three detached template nodes are built in code and copied with `Node.Duplicate()`. This is the
pattern Rule 2 names explicitly — *"노드 트리나 생성 코드를 복사해서 재사용을 대신하지 않는다"* — and
unlike §3 it has **no** Unity-fidelity defence: the Unity original used real prefabs here and the
port replaced them with hand-rolled duplication.

| # | Template built | Duplicated at | Template tree |
|---|---|---|---|
| 68 | `GameplayEnemySpawner.cs:430-445` `CreateProjectilePrefab` — `new EnemyProjectile { Name = "EnemyProjectile" }` (`:432`), never added to the tree | `Scripts/Enemy/EnemyProjectilePool.cs:64` `(Node2D)_prefab.Duplicate()` | `EnemyProjectile └─ Sprite (Sprite2D, Disc, World.U(0.42) square, ProjectileColor, ProjectileSortingOrder)`. **No `Area2D`/collider on the template** — because it never enters the tree, `EnemyProjectile._Ready` never runs, so every duplicate builds its own `HitArea` at `Scripts/Enemy/EnemyProjectile.cs:42-51`. |
| 69 | `GameplayEnemySpawner.cs:458-469` `CreateHazardPrefab` — `new BossHazardStrip { Name = "BossHazardStripTemplate" }` (`:460`), parented to the boss (`:461`), `SetActive(false)` (`:467`) | `Scripts/Enemy/RainbowChapterBossBehaviour.cs:772` | `BossHazardStrip └─ Sprite (Sprite2D, Disc, size Vector2.Zero, EnemyReadoutSortingOrder)`. No collider — the strip sweeps with `Phys2D.OverlapCircleAll`. |
| 70 | `GameplayEnemySpawner.cs:477-484` `CreateAfterimagePrefab` — `new BossAfterimage { Name = "BossAfterimageTemplate" }` (`:479`), parented to the boss (`:480`), `SetActive(false)` (`:482`) | `Scripts/Enemy/RainbowChapterBossBehaviour.cs:997` | Single node — `BossAfterimage` *is* a `Sprite2D` subclass. No children, no texture until `Configure`. |

Wiring: `SetProjectilePrefab` at `:181`, `SetHazardPrefab` at `:333`, `SetAfterimagePrefab` at `:334`;
export fields at `Scripts/Enemy/RangedCaster.cs:21`, `RainbowChapterBossBehaviour.cs:37` and `:43`.
The fields are literally named `*Prefab` and typed `Node2D` where a `PackedScene` `[Export]` belongs.

**Two live defects this pattern causes, which a `PackedScene` removes for free:**

1. **State leaks through the copy.** `SetActive(false)` (`GameplayBuildShim.cs:105-125`) sets
   `Visible = false`, deferred `ProcessMode = Disabled` and deferred `CollisionShape2D.Disabled =
   true` recursively. Templates 69 and 70 therefore carry `ProcessMode = Disabled` into every
   duplicate; `RainbowChapterBossBehaviour.cs:773` and `:1014` restore `Visible` **only**. Template 68
   avoids it only because the pool re-arms `ProcessMode` explicitly at `EnemyProjectilePool.cs:60`
   and `:77`.
2. **A second, divergent builder for the same tree.** `Scripts/Enemy/RangedCaster.cs:110-121`
   `CreateDefaultProjectile` builds the *same* `EnemyProjectile └─ Sprite` pair as #68 when
   `projectilePrefab` is null (`:83-86`), but sets only `Modulate` and `ZIndex = 1` and leaves the
   sprite **textureless and unsized**. Same for the hazard fallback at
   `RainbowChapterBossBehaviour.cs:779` (bare strip, no sprite, so the re-dressing at `:795-800` is a
   no-op) and the afterimage fallback at `:1003`.

Plus a fourth duplicated builder family: `EnemyGroupCombat` + `CombatFeedback` are added by four
near-identical guarded blocks — `MeleeGrunt.cs:86-97`, `LeapingAttacker.cs:85-92`,
`RangedCaster.cs:89-97`, and `WrathMiniBoss.cs:190-191` (which adds only `CombatFeedback`, the
divergence). Four instances of one component set. *(Counted within #49 rather than separately.)*

---

## 5. Editor tooling — noted, not counted against the product

`addons/mygame_tools/MyGameToolsPlugin.cs:20-84` builds the whole editor dock in `_EnterTree`:
`new VBoxContainer` (`:20`), `AddThemeConstantOverride` (`:21`), `Heading` → `new HSeparator` (`:71`)
+ `new Label` (`:74`) + `AddThemeColorOverride` (`:75`), `Button` → `new Button` (`:81`).
`addons/mygame_tools/ProjectSceneSelector.cs:36-58` adds `new Label` (`:43`) and one `new Button`
(`:59`) per `.tscn` found on disk.

Verdict: the rule set targets 제품 구현 (product implementation). An `EditorPlugin` dock is neither a
game screen nor a shipped component, and its scene list is genuinely dynamic. Making it a `.tscn` is
possible and would be marginally better, but it is ceremony against zero risk. **Recommend: leave
it.** Listed here so the audit is complete, not counted in the 61.

---

## 6. Migration plan

28 new `.tscn` files and 1 Theme resource, in nine stages. Each stage is chosen so the test suite can
go green before the next one starts. Sizes are files touched (new + edited), including tests.

### Stage 1 — Cutscene overlay · 4 files · low risk

| Scene | Node tree | Script that binds it | Code that disappears |
|---|---|---|---|
| `Scenes/UI/CutsceneOverlay.tscn` | `CutsceneOverlay (CanvasLayer, layer 100, ProcessMode Always)` ├─ `Fade (ColorRect, FullRect)` ├─ `LetterboxTop (ColorRect)` ├─ `LetterboxBottom (ColorRect)` └─ `Line (Label, 900x80, bottom −90)` | `CutsceneOverlay.cs` — `SetFade`/`SetLetterbox`/`SetLine`/`SetBlackout`/`SetBarHeight` unchanged | `Build()` `:107-148`, `CreateBar()` `:150-163`; `Create()` `:38-58` becomes `_scene.Instantiate<CutsceneOverlay>()` |

Smallest possible first cut: one screen, no gameplay dependency, no test reads its node names. Proves
the instancing path end to end.

### Stage 2 — HUD leaf components + Theme · 5 files · low risk

| Asset | Contents | Replaces |
|---|---|---|
| `Resources/UI/MenuTheme.tres` | `Theme`: Button styleboxes for normal/hover/focus/pressed/disabled, font + font sizes, font colours per state; Label default font/colour | `GameplayHud.StyleMenuButton:285-302`, `GameplayHud.Plate:304`, `GameplayHud.Mul:307`, `TitleMenuBootstrap.StyleButton:396-412` — **all four deleted** |
| `Scenes/UI/HudBar.tscn` | `Gauge (ColorRect 240x26)` ├─ `GhostFill (ColorRect, hidden by default)` └─ `Fill (ColorRect)` | `CreateBar:720-736`, `CreateFill:739-750`; `SetFill:753-763` stays (it is binding) |
| `Scenes/UI/GateTravelRow.tscn` | `GateRow (Button, 280x56, MenuTheme)` | the `CreateVictoryButton` call inside `BuildGateRows:452` — the row loop `:445-472` becomes instantiate + `.Text = …` + `.Pressed +=` |

Note a second Theme variation is needed for the title screen's *lit* segment
(`SegmentActive`) — author it as a theme type variation on the same `.tres`, not a second file.

### Stage 3 — HUD panels and the HUD screen · 9 files · medium risk

| Scene | Node tree | Binds | Disappears |
|---|---|---|---|
| `Scenes/UI/ModalPanel.tscn` | `Panel (ColorRect, FullRect, MouseFilter Stop)` ├─ `Title (Label)` ├─ `Subtitle (Label)` | — | `CreatePanel:235-248`, `CreateVictoryText:250-264` |
| `Scenes/UI/VictoryPanel.tscn` | inherits ModalPanel; + `RestartButton`, `TitleButton` | `ShowVictory:694-712` | `CreateVictoryPanel:221-232` |
| `Scenes/UI/PausePanel.tscn` | inherits; + `PauseStatus`, `ResumeButton`, `SaveButton`, `PauseTitleButton` | `SetPauseVisible:355-371`, `ShowPauseSaved:660` | `CreatePausePanel:318-331` |
| `Scenes/UI/GateTravelPanel.tscn` | inherits; + `GateList (Control)`, `GateTravelCloseButton` | `SetGateTravelVisible:407-430`, `BuildGateRows:437-473` (reduced to instantiate + bind) | `CreateGateTravelPanel:378-393` |
| `Scenes/UI/LevelUpPanel.tscn` | inherits; + `LevelUpSouls`, 4 stat rows (fixed count → authored, **not** a list scene), `LevelUpCloseButton` | `SetLevelUpVisible:537-551`, `RefreshLevelUp:578-608` | `CreateLevelUpPanel:480-518` |
| `Scenes/UI/GameplayHud.tscn` | `HUD (CanvasLayer, script GameplayHud)` ├─ `GameplayVictoryController` ├─ `GameplayPauseController` └─ `HudRoot (Control)` ├─ `HUDBackground` ├─ 3 × `HudBar.tscn` ├─ 12 × `Label` ├─ 4 panel instances | `Initialize:787`, `BindPlayer:825`, `BindCombatResources:835`, `ForceRefresh:850`, all `Update*` | `CreateUi:132-183`, `CreateBackground:207-219`, `CreateText:765-780`, **`PlaceRect:191-202`** |

`GameplayHudSpawner.cs:19-23` becomes `_hudScene.Instantiate<GameplayHud>()`; `:29-35` bind the two
controllers already present in the scene instead of adding them.

**Tests touched:** `Tests/PlayMode/GameplayHealItemTests.cs:351` (`FindChild("FlaskText")`),
`Tests/PlayMode/GameplayVictoryPanelTests.cs:87/90/93` (`"VictorySubtitle"`, `"RestartButton"`).
Both keep working if the authored node names are preserved exactly; the `owned: false` argument at
`GameplayHealItemTests.cs:351` becomes unnecessary but stays harmless.

### Stage 4 — Title screen · 5 files · medium risk

| Scene | Node tree | Binds | Disappears |
|---|---|---|---|
| `Scenes/UI/SegmentButton.tscn` | `Segment (Button, MenuTheme)` | — | the inner loop of `CreateSegmentedRow:279-287` |
| `Scenes/UI/SegmentedRow.tscn` | `Row (HBoxContainer, 408x48, sep 4)` ├─ `Caption (Label, 180x48, ExpandFill)` └─ `Segments (HBoxContainer, ExpandFill)` | `SegmentedRow.Refresh:342-352` | `CreateSegmentedRow:258-290` incl. **the width formula at `:276`**, which `SizeFlags.ExpandFill` replaces |
| `Scenes/TitleScene.tscn` (rewritten) | `TitleRoot (Node)` └─ `TitleCanvas (CanvasLayer)` └─ `TitleRoot (Control)` ├─ `Backdrop` ├─ `Title` ├─ `Subtitle` ├─ `MenuButtons (VBox)` with the 6 buttons ├─ `SettingsPanel (ColorRect, hidden)` └─ `SettingsColumn (VBox)` with 2 Labels, 2 preset Buttons, 4 CheckBoxes, 2 `SegmentedRow.tscn` instances, close Button. **`focus_neighbor`/`focus_next` authored; `새로하기` is the scene's `focus` default.** | `TitleMenuBootstrap.cs` — `RefreshOptions:292-306`, `StepLabels:308-322`, `CycleDifficulty:482`, `SeedDifficultyFromSave:500`, `RefreshDifficultyLabel:510`, `StartNewGame/Plus`, `ContinueGame`, `LoadGameplay`, `ToggleSettings`, `QuitGame` all unchanged | `BuildTitleUi:82-107`, `CreateBackdrop:70-80`, `CreateTitle:111-122`, `CreateButtonStack:124-159`, `CreateSettingsPanel:161-208`, `CreatePresetButton:210-217`, `CreateToggleRow:225-250`, `CreateButton:377-385`, `CreateText:414-425`, `CreateRowText:368-375`, `GrabFocus():158` |

**Tests touched:** `Tests/PlayMode/TitleGraphicsOptionsTests.cs` — `:91/94/97` (`"TitleCanvas"`,
`"TitleRoot"`, `"SettingsPanel"`), `:144` (`"SettingsColumn"`), `:175` (`node.Name == option +
"Row"`). All survive if names are preserved. **The one that genuinely needs care** is `AssertLit`
(`:185-193`), which identifies the chosen segment by `button.GetThemeColor("font_color").R > 0.7f`.
Moving styling into a Theme changes where that colour comes from; a per-instance
`AddThemeColorOverride` for the lit segment (kept in `SegmentedRow.Refresh`) preserves the assertion,
whereas a theme *type variation* would not.

### Stage 5 — World UI and shared markers · 15 files · medium risk

| Scene | Node tree | Instances | Replaces |
|---|---|---|---|
| `Scenes/World/WorldLabel.tscn` | `Label (Node2D)` └─ `Text (Label, Grow Both, Size 0)` | 8 | `GameplayEnvironmentBuilder.CreateWorldLabel:326-351` **and** `GameplayEnemySpawner.CreateRoleMarker:586-609` — two implementations collapse to one scene |
| `Scenes/World/AttackReadout.tscn` | `Readout (Sprite2D, Disc)` └─ `GameplayTelegraphPulse` | 6 | `GameplayPlayerSpawner.CreateAttackReadout:335-345` **and** `GameplayEnemySpawner.CreateAttackReadout:569-579` |
| `Scenes/World/MarkerDisc.tscn` | `Marker (GameplayTelegraphPulse)` └─ `Disc (Sprite2D)` | 2–N | `CheckpointZone.EnsureMarker:233-261` **and** `GateTravelZone.EnsureMarker:312-333` |
| `Scenes/World/WorldHealthBar.tscn` | `GameplayWorldHealthBar (Node2D)` └─ `HealthBar` ├─ `Frame (Sprite2D)` └─ `Fill (Node2D)` └─ `FillSprite (Sprite2D)` | 6+ | `GameplayWorldHealthBar.Build:96-129`, `CreateSpriteChild:168-180` — the lookup half at `:103-109` already exists and simply starts succeeding |
| `Scenes/World/SoulPickup.tscn` | `SoulPickup (Area2D)` ├─ `Reach (CollisionShape2D)` └─ `Pulse (GameplayTelegraphPulse)` └─ `Stain (Sprite2D)` | dynamic | `SoulPickup.Create:43-81` (keeps the `souls`/`pickupDelay` binding and the position-before-tree write) |
| `Scenes/World/LockOnMarker.tscn` | `LockOnMarker (Sprite2D, Disc, hidden)` | 1 | `GameplayLockOnMarker.BuildMarker:84-105` |

Keep `AttackReadout.tscn` and `MarkerDisc.tscn` as **two** scenes at this stage. The pulse is the
*child* in one and the *parent* in the other, and `GameplayTelegraphPulse` caches the colour and scale
it finds when readied (`CheckpointZone.cs:257-259` says so); unifying them is a behaviour change that
belongs in its own commit.

**Tests touched:** `Tests/Unit/P0CombatStabilityTests.Gameplay.cs:312` calls
`InvokeNonPublic(bar, "Build")` and asserts `"HealthBar"` (`:314`) and `"HealthBar/Frame"` (`:317`).
`Build` must survive as a method (it becomes lookup-only) and the names must be preserved.

### Stage 6 — Arena geometry · 7 files · medium risk

| Scene | Node tree | Instances | Replaces |
|---|---|---|---|
| `Scenes/World/SolidBox.tscn` | `Body (StaticBody2D, layer Ground, mask 0)` ├─ `Shape (CollisionShape2D, RectangleShape2D)` └─ `Sprite (Sprite2D)` | 8–20 | `CreateSolidBox:309-324`, `CreateWorldEdge:170-190` (same scene, sprite hidden), `CreateSpiritPlatform:293-302` |
| `Scenes/World/Checkpoint.tscn` | `Checkpoint (Node2D)` ├─ `Checkpoint (script)` └─ `CheckpointZone (Area2D)` ├─ `Trigger (CollisionShape2D)` └─ `MarkerDisc.tscn` | 1–N | `CreateCheckpointAt:264-273`, `CheckpointZone.EnsureTrigger:213-227` |
| `Scenes/World/GatePortal.tscn` | `GatePortal (Node2D)` └─ `GateTravelZone (Area2D)` ├─ `Trigger` └─ `MarkerDisc.tscn` | 1 | `CreateGatePortal:217-222`, `GateTravelZone.EnsureTrigger:292-306` |
| `Scenes/World/ShortcutGate.tscn` | `SolidBox.tscn` inherited + `ShortcutGate` script + `Trigger (Area2D)` └─ `Reach (CollisionShape2D)` | 0–1 | `CreateShortcutGate:279-291`, `ShortcutGate.cs:282-296` |

`GameplayEnvironmentBuilder.Build:44-81` stays: it is the loop that reads `SceneLayout_*.json` and
positions instances. Only the *construction* moves.

**Tests touched:** `Tests/Unit/P0CombatStabilityTests.Gameplay.cs:79` reflects on the private
`GameplayEnvironmentBuilder.CreateCheckpoint` **by name with one `GameplaySceneDefaults` parameter** —
the method's own comment at `:246-253` warns that renaming it breaks a test the compiler cannot show.
Three more tests find platforms by the name `CreateSolidBox` gave them:
`Tests/PlayMode/GameplayChapterTwoTraversalTests.cs:954`,
`Tests/PlayMode/GameplayEnemyFootingTests.cs:217`,
`Tests/Unit/P0CombatStabilityTests.Gameplay.cs:373` (`"CheckpointRunPlatform"`). The instanced node
must take the layout's `platform.Name`, not the scene's root name.

`CreateSceneryPiece:359-365` — **do not make a scene**; see §7.

### Stage 7 — Effects and the three templates · 10 files · medium risk

| Scene | Node tree | Replaces |
|---|---|---|
| `Scenes/Effects/EnemyProjectile.tscn` | `EnemyProjectile (Node2D)` ├─ `Sprite (Sprite2D, Disc)` └─ `HitArea (Area2D, layer EnemyHitbox, mask Player\|Ground)` └─ `CollisionShape2D (CircleShape2D r=World.U(0.3))` | `CreateProjectilePrefab:430-445` + `EnemyProjectilePool.cs:64` `Duplicate()` + **`RangedCaster.CreateDefaultProjectile:110-121`, the divergent second builder** + the per-instance `HitArea` build at `EnemyProjectile.cs:42-51` |
| `Scenes/Effects/BossHazardStrip.tscn` | `BossHazardStrip (Node2D)` └─ `Sprite (Sprite2D, Disc)` | `CreateHazardPrefab:458-469` + `RainbowChapterBossBehaviour.cs:772` `Duplicate()` + the bare fallback at `:779` |
| `Scenes/Effects/BossAfterimage.tscn` | `BossAfterimage (Sprite2D)` | `CreateAfterimagePrefab:477-484` + `:997` `Duplicate()` + the fallback at `:1003` |

The three `Node2D`-typed `*Prefab` exports (`RangedCaster.cs:21`,
`RainbowChapterBossBehaviour.cs:37`, `:43`) become `[Export] PackedScene`. This stage also removes the
`ProcessMode = Disabled` leak described in §4 — an instance from a `PackedScene` never inherits a
deactivated template's state.

**Tests touched:** `Tests/PlayMode/GameplayHazardStripRegressionTests.cs:274` (`new BossHazardStrip`)
and `Tests/Unit/P0CombatStabilityTests.Gameplay.cs:549` (`new EnemyProjectile`) build these by hand;
they keep compiling but should switch to the scene to stay meaningful.

### Stage 8 — Actors · 15 files · **highest risk**

| Scene | Node tree | Binds |
|---|---|---|
| `Scenes/Actors/Player.tscn` | the ~30-node tree in §3.2, with `WorldHealthBar.tscn` and `AttackReadout.tscn` instanced | `GameplayPlayerSpawner.Spawn:73-171` keeps every tuning call and, critically, the **order** |
| `Scenes/Actors/EnemyBase.tscn` | `CharacterBody2D` ├─ `Collider` ├─ `Visual` ├─ `Health` ├─ `DamageReceiver` ├─ `CombatFeedback` ├─ `CombatResultBroadcaster` ├─ `Poise` ├─ `SoulsWallet` ├─ `EnemyGroupCombat` ├─ `RoleMarker` (`WorldLabel.tscn`) └─ `WorldHealthBar.tscn` | — |
| `Scenes/Actors/MeleeGrunt.tscn` | inherits EnemyBase; + `AttackPoint (Node2D)`, `SlashDanger` (`AttackReadout.tscn`), `EnemyDeathCleanup` | `CreateMeleeGruntAt` reduced to instantiate + `SetTuningData` + `SetAttackPoint` + place |
| `Scenes/Actors/LeapingAttacker.tscn` | inherits; + `LeapLandingDanger` | `CreateLeapingAttackerAt` |
| `Scenes/Actors/RangedCaster.tscn` | inherits; + `CastRange`, `[Export] PackedScene projectileScene` | `CreateRangedCasterAt` |
| `Scenes/Actors/WrathMiniBoss.tscn` | inherits; + `BossSlashDanger`, `BossSlamDanger` | `CreateWrathMiniBoss` |
| `Scenes/Actors/ChapterBoss.tscn` | inherits; + hazard/afterimage `PackedScene` exports | `CreateChapterBoss` — still takes its `RainbowChapterBossData` at runtime |

Code that disappears: `BuildPlayer:211-283`, `CreatePlayerSword:308-333`, `CreateEnemyRoot:378-386`,
`AddCapsuleCollider:405-414`, `AddActorVisual:416-423`, the construction half of all five `Create*At`
/ `Create*Boss` methods, both copies of `AddHealthBar` / `EnsureDamageReceiver` /
`EnsureCombatResultBridge`, the four `EnemyGroupCombat`+`CombatFeedback` guard blocks, and finally
`GameplayBuildShim.NewObject` / `AddComponent` / `EnsureComponent` (`:68-98`). `SetActive` stays.

**Why this stage is the riskiest — five reasons, all documented in `CLAUDE.md` and `PORT_STATUS.md`:**

1. **`_Ready` fires on tree entry, not on component add.** `PORT_STATUS.md` records this as port bug
   #2 (enemies parented before they had `Health` → `NullReferenceException` every frame) and #3 (the
   player's action controller never parented). An instanced `.tscn` arrives complete and detached,
   which *fixes* the class of bug — but only if `PlaceInWorld` (`GameplayEnemySpawner.cs:399-403`) and
   the "position before `AddChild`" ordering survive verbatim. Getting that wrong reintroduces the
   first-physics-step sweep that hits the player every spawn.
2. **The deliberate `PlayerController2D._Ready` trap.** It re-runs `actions.Initialize` with its own
   null hitbox, so `GameplayPlayerSpawner.cs:125` must still call `SetDamageHitbox` **after** the
   player is in the tree. `CLAUDE.md` says explicitly: *do not "fix" the trap.*
3. **Tuning order is load-bearing and invisible.** `PlayerProgression.EnsureOn` at `:160` must run
   after `SetMaxHealth`, `ApplyTuning`, `ApplyResourceTuning` and `poise.Configure`, or every level is
   measured from the wrong base — with nothing failing to compile.
   Likewise `EnsureEnemyHealthRig` vs `SetBossData` (`GameplayEnemySpawner.cs:504-514`): two writers of
   max health whose order decides whether Hard and New Game+ reach the boss.
4. **Node-name coupling from production code.** `ActorAnimationDriver.cs:28` (`"ReadableSword"`,
   compared at `:120`) and `GameplayPlayerSpawner.cs:127` (`hitbox.GetNode("ReadableSword")`) read the
   hierarchy by name.
5. **The most tests.** `Tests/Unit/P0CombatStabilityTests.Gameplay.cs:43/48/61/66` reflect on the
   private `EnsureDamageReceiver` / `EnsureCombatResultBridge` on **both** spawners by name, and seven
   tests build actors by hand through the shim being retired —
   `GameplayBossAttackGrammarTests.cs:255`, `:282`; `GameplayChapterBossTests.cs:644`;
   `GameplayHazardStripRegressionTests.cs:301`; `GameplayLockOnTests.cs:191`;
   `UnityTestAgentPlayModeSmokeTests.cs:275`, `:284`, `:314`.

Suggested split if 15 files is too wide: land the five enemy scenes first (Stage 8a) and `Player.tscn`
alone (Stage 8b). The player carries four of the five risks by itself.

### Stage 9 — arena authoring · ~20 files · **needs a product decision before it is scheduled**

Rewriting the nine `Scenes/*.tscn` shells so each chapter's ground, platforms, scenery, bonfires and
gates are authored nodes rather than `GameplayEnvironmentBuilder` output. This is the only stage that
contradicts `PORTING_GUIDE.md:159-168` head-on, and it would change what
`Resources/Design/SceneLayout_*.json` is *for* (from "the level" to "tuning of an authored level"),
which has design ownership implications well beyond this audit. It also directly touches the one
currently-failing test (`GameplayLayoutIntegrityTests.EveryChapterLayout_KeepsItsPlacementsAndBonfires`,
red on shipped data since before the port). **Recommend deferring it and asking the user**; Stages 1–8
bring the project into compliance with Rules 2 and 3 without it, because after Stage 8 the chapter
shells instance authored scenes rather than fabricating nodes.

### Stage summary

| Stage | What | Files | Risk |
|---|---|---|---|
| 1 | Cutscene overlay | 4 | low |
| 2 | HUD leaf components + Theme | 5 | low |
| 3 | HUD panels + `GameplayHud.tscn` | 9 | medium |
| 4 | Title screen | 5 | medium |
| 5 | World UI + shared markers | 15 | medium |
| 6 | Arena geometry | 7 | medium |
| 7 | Effects + the 3 `Duplicate()` templates | 10 | medium |
| 8 | Actors (Player + 6 enemy scenes) | 15 | **highest** |
| 9 | Arena authoring into the 9 chapter scenes | ~20 | deferred — needs a ruling |
| | **Stages 1–8** | **70 files, 28 new `.tscn` + 1 `.tres`** | |

---

## 7. Do not migrate

**Genuinely dynamic — must stay code-built:**

- **The gate-travel row *count*.** `GameplayHud.SetGateTravelVisible:407-430` rebuilds 0–8 rows per
  open from the save. Instancing `GateTravelRow.tscn` per row **is** the compliant answer; the loop
  itself stays. Same for the segmented rows' segment count, which follows
  `GraphicsOptions.MsaaSteps` (4 values) and `RenderScaleSteps` (3).
- **Enemy and checkpoint *placement*.** How many grunts, where, and with which variant data file comes
  from `Resources/Design/SceneLayout_*.json` and `SpawnTuning<T>` (`GameplayEnemySpawner.cs:214-245`).
  The actor is a scene; the placement loop stays code.
- **Projectiles, hazards, afterimages, soul stains.** Spawn count and lifetime are combat state. Scene
  + `Instantiate()` per spawn; the pool (`EnemyProjectilePool`) stays.
- **All tuning binding.** Everything listed under "bound at runtime" in §2 and §3. Rule 1 requires
  those numbers to come from `Resources/Design/*.json`, so they must not be baked into a `.tscn`.

**Not a violation — leave alone:**

- **`Scripts/Gameplay/DebugVisualization.cs`** — immediate-mode `_Draw` (`:58-211`), constructs no
  nodes. The correct Godot replacement for Unity's `OnDrawGizmos`.
- **`Scripts/Gameplay/GameplayVisualFactory.cs`** — a `Texture2D` factory with baked-PNG override
  (`:44`, `:59`). Resources, which is what Rule 2 asks for. Its `Dress()` (`:92-116`) shrinks away as
  actor scenes land, but the generators stay.
- **`GameplayBuildShim.SetActive` (`:105-125`)** — a correct three-part shim for
  `gameObject.SetActive`; Godot has no single equivalent and a scene cannot express it.
- **`TitleMenuBootstrap.SegmentedRow` (`:325-353`)** — a plain C# class holding button references and
  a `Func<int>`. Rule 2 explicitly forbids wrapping data or functions in an empty node; converting it
  to a `Node` would be the violation.
- **Plain data holders generally** — `GameplayPlayerContext`, `GameplayEnemyContext`,
  `GameplayEnvironment` (structs), `PlayerStateMachine`, `PlayerProgression`, `EnemyProjectilePool`
  (plain classes). All correctly *not* nodes.
- **`addons/mygame_tools/`** — editor tooling, not product UI; the scene list is genuinely dynamic
  (§5).

**Would be pointless ceremony:**

- **`CreateSceneryPiece` (`GameplayEnvironmentBuilder.cs:359-365`)** — a bare `Sprite2D` with no
  children. A one-node scene whose only content is "a Sprite2D" adds a file and a load without adding
  structure. Author the decorations directly in the chapter scene if Stage 9 ever happens; otherwise
  leave the code.
- **A `HudTextRow.tscn`** — the 11 HUD readouts are single `Label` nodes with different colours and
  positions. Author them in `GameplayHud.tscn` directly; do not make a scene per row.
- **A `ToggleRow.tscn`** — a bare `CheckBox` with a min size and a caption. Author the four directly
  in the title scene.
- **`GameplaySystemBootstrapper.EnsureCamera` (`:26-27`)** — one `Camera2D`, no children. Author it in
  the chapter scene root if Stage 9 happens; a `Camera2D.tscn` is a file for nothing.
- **`CutsceneDirector.Create` (`:135`)** — a single bare node.
- **`Scripts/Combat/AudioFeedback.cs:68-69`** — one on-demand `AudioStreamPlayer`; it becomes an
  authored child inside the actor scenes at Stage 8, and needs no scene of its own before then.

---

## 8. Tests that a scene migration breaks

The suite currently runs **206 passed / 1 failed / 1 skipped** (`PORT_STATUS.md`). The failure —
`GameplayLayoutIntegrityTests.EveryChapterLayout_KeepsItsPlacementsAndBonfires` — is design data, red
before the port, and unrelated to this migration. The skip is
`ActorPrefabBodyColorsMatchReadabilityDefaults`.

Tests that reach into a runtime-built hierarchy by node name, node path, or private builder name — the
ones a scene migration can break silently:

| Test | Line | Reaches for | Breaks at stage |
|---|---|---|---|
| `Tests/PlayMode/TitleGraphicsOptionsTests.cs` | `:91`, `:94`, `:97` | `"TitleCanvas"`, `"TitleRoot"`, `"SettingsPanel"` by path | 4 |
| `Tests/PlayMode/TitleGraphicsOptionsTests.cs` | `:144` | `"SettingsColumn"` + `GetCombinedMinimumSize()` | 4 |
| `Tests/PlayMode/TitleGraphicsOptionsTests.cs` | `:175` | `node.Name == option + "Row"` | 4 |
| `Tests/PlayMode/TitleGraphicsOptionsTests.cs` | `:185-193` | `button.GetThemeColor("font_color")` — **the most fragile assertion in the suite** once styling moves to a Theme | 2 / 4 |
| `Tests/PlayMode/GameplayVictoryPanelTests.cs` | `:87`, `:90`, `:93` | `"VictorySubtitle"`, `"RestartButton"` via `FindNamed` (`:108-121`) | 3 |
| `Tests/PlayMode/GameplayHealItemTests.cs` | `:351` | `hud.FindChild("FlaskText", recursive, owned: false)` — the comment names the cause: *"the HUD builds its panels in code, so none of them has a scene owner"* | 3 |
| `Tests/Unit/P0CombatStabilityTests.Gameplay.cs` | `:312`, `:314`, `:317` | `InvokeNonPublic(bar, "Build")`, `"HealthBar"`, `"HealthBar/Frame"` | 5 |
| `Tests/Unit/P0CombatStabilityTests.Gameplay.cs` | `:79-90` | private `GameplayEnvironmentBuilder.CreateCheckpoint` by reflection | 6 |
| `Tests/Unit/P0CombatStabilityTests.Gameplay.cs` | `:373` | platform named `"CheckpointRunPlatform"` | 6 |
| `Tests/PlayMode/GameplayChapterTwoTraversalTests.cs` | `:954` | platform by `Name` | 6 |
| `Tests/PlayMode/GameplayEnemyFootingTests.cs` | `:217` | candidate by `Name` | 6 |
| `Tests/Unit/P0CombatStabilityTests.Gameplay.cs` | `:43`, `:48`, `:61`, `:66` | private `EnsureDamageReceiver` / `EnsureCombatResultBridge` on both spawners, by reflection | 8 |
| `Tests/PlayMode/GameplayBossAttackGrammarTests.cs` | `:255`, `:282` | `AddComponent<CollisionShape2D>("Collider")` — depends on the shim surviving | 8 |
| `Tests/PlayMode/GameplayChapterBossTests.cs` | `:644` | hand-built actor with `Name = "Collider"` | 8 |
| `Tests/PlayMode/GameplayHazardStripRegressionTests.cs` | `:274`, `:301` | `new BossHazardStrip`, `Name = "Collider"` | 7 / 8 |
| `Tests/PlayMode/GameplayLockOnTests.cs` | `:191` | `Name = "Collider"` | 8 |
| `Tests/PlayMode/UnityTestAgentPlayModeSmokeTests.cs` | `:275`, `:284`, `:314` | `"Collider"`, `"HitboxAnchor"` | 8 |
| `Tests/Unit/P0CombatStabilityTests.Gameplay.cs` | `:549` | `new EnemyProjectile` | 7 |
| `Tests/Unit/P0CombatStabilityTests.Systems.cs` | `:294` | `new Node2D { Name = "SpiritPlatform" }` | 6 |

**Safe** (no migration exposure): every `SceneQuery.FindFirst<T>` / `FindAll<T>` lookup — roughly 40
sites across the PlayMode suite — and the group-based lookups in
`Scripts/Testing/MyGameRuntimeAgentDriver.cs:172`, `:187` and
`Tests/PlayMode/UnityTestAgentPlayModeSmokeTests.cs:351`. Type- and group-based queries survive a
scene migration untouched; name- and path-based ones do not.

**Production code with the same exposure** (not a test, but the same breakage class):
`Scripts/Gameplay/ActorAnimationDriver.cs:28` + `:120` (`"ReadableSword"`),
`Scripts/Gameplay/GameplayPlayerSpawner.cs:127` (`hitbox.GetNode("ReadableSword")`),
`Scripts/Gameplay/GameplayWorldHealthBar.cs:103-108` (`"HealthBar"`, `"Frame"`, `"Fill"`,
`"FillSprite"`).
