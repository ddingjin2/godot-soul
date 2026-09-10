# Port status

Running log of what has landed, what each agent changed away from the Unity source, and what the
integration pass still has to reconcile. Written as the port happens; the closing summary is at the
bottom once everything compiles and runs.

## Landed

| Area | Files | State |
|---|---|---|
| Project scaffold | `project.godot`, `MyGame.csproj`, `icon.svg`, `tools/*.ps1` | done |
| Shared shims | `Scripts/Core/` (World, GameClock, Phys2D, UnityCompat) | done |
| Test harness | `Tests/Framework/` (attributes, Assert, TestRunner), `Tests/TestMain.tscn` | done |
| Resources | Design JSON (35), PixelActors + Art PNG (104), Korean font | copied |
| Input | 20 actions in `project.godot` `[input]` | done |
| UI | `Scripts/UI/GameplayHud.cs`, `TitleMenuBootstrap.cs` | done |
| Combat | `Scripts/Combat/` (26 files) | done |
| Player | `Scripts/Player/` (13 files) | done |
| Enemy | `Scripts/Enemy/` (22 files) | done |
| Gameplay | `Scripts/Gameplay/` (45 files) | done |
| Editor tools | `addons/mygame_tools/` (12 files, dock plugin) | done |
| Scenes | `Scenes/*.tscn` (9 shells) | done |
| Tests | `Tests/PlayMode/`, `Tests/Unit/` (32 suites, 208 tests) | done |
| Agent layer | `Scripts/Testing/` (11 adapters + the agent package) | done |

## Deliberate differences from the Unity source

### UI
- `UnityAction` parameters are `System.Action` throughout, so `GameplayHud.SetPauseVisible`,
  `SetGateTravelVisible`, `ShowVictory` and `SetLevelUpVisible` take `Action` / `Action<string>`.
- `CreateUi(CanvasLayer canvas = null)` defaults to the HUD itself - the HUD *is* the `CanvasLayer`.
- `LoadUiFont()` returns `Godot.Font`.
- Unity's `EventSystem` and the HUD's own camera are dropped: Godot's viewport owns focus, and the
  camera clear colour became a full-rect `ColorRect` backdrop (`#06070A`).
- The title menu calls `GrabFocus()` on the first button. uGUI leaned on the EventSystem's implicit
  first selectable; without an explicit focus the menu is unusable on a gamepad.
- Two `GetComponent<T>()` lookups (`PlayerProgression`->`SoulsWallet`, player->`PlayerActionController`)
  became a `FindComponent<T>` that checks the node, its children, then its siblings, because Unity
  components on one GameObject are separate nodes here. **Integration must confirm these two match
  the node layout the Player agent actually built.**
- HUD runs with `ProcessMode = Always` so the unscaled warning timer and menus survive a pause; the
  ghost gauge and hit flash stay on scaled time and freeze at `TimeScale 0`, as before.
- **Product text is a translation table, not literals.** Unity hardcoded all 34 Korean UI strings;
  they now live in `localization/ui.csv` (`keys,ko,en`, imported to `ui.ko.translation` /
  `ui.en.translation`, registered under `[internationalization]` with `ko` as the fallback) and the
  screens read them through `Tr(key)` - `TranslationServer.Translate(key)` where the caller is static
  (`DifficultySettings.DisplayName`, `TitleMenuBootstrap.PresetName`/`StepLabels`). Two sentences
  that were C# interpolations are format keys filled with `string.Format`:
  `UI_TITLE_DIFFICULTY_VALUE` and `UI_OPTION_PRESET_VALUE`. The settings panel's checkbox and
  segmented-row nodes are now named after the key rather than the caption, so a change of locale does
  not rename them; `TitleGraphicsOptionsTests` looks rows up by key for the same reason. Both UI
  fixtures assert against `Tr(key)` with a guard that the key actually resolved, so re-wording the
  table moves the tests with it without weakening them.

### Combat
- `HitStopManager` no longer writes a physics step: `Engine.TimeScale` already scales physics, so
  writing `fixedDeltaTime` too would apply the slowdown twice.
- `GraphicsOptions.RenderScale` has no 2D counterpart in Godot (URP's render scale maps to a 3D-only
  viewport setting). It is still loaded, clamped, cycled and saved - it just does not reach the
  renderer. MSAA and VSync do.
- `AudioFeedback` cuts a ringing cue off instead of layering: Godot has no `PlayOneShot`.
- `EnemyGroupCombat` adds spacing force to `Velocity` by hand at unit mass - `CharacterBody2D` has no
  `AddForce`.
- `CombatFeedback` squash scales the found `Sprite2D`, not its own node, because the sprite is a
  separate node here; `Invoke`/coroutine became `_Process` countdowns.
- `DamageHitbox2D` still polls every frame rather than using `Area2D` signals: the sweep moves each
  frame and has to report everything overlapping, not just new entries.
- Dropped: `OnDrawGizmosSelected` (no gizmo system) and `SinResonanceController.OnValidate` (its job
  was repairing an empty inspector table; the table is JSON now).

### Runtime findings from the first headless boot

The arena builds and the game boots. Two real defects surfaced, both invisible to the compiler:

1. **JSON binding collided on every data class that added a Godot-style property over its Unity
   field.** `RainbowChapterBossData` has `chapterColor` (field, `[Export]`) and `ChapterColor`
   (get-only property); with case-insensitive matching those fold onto one JSON name and
   `System.Text.Json` refuses to build the contract at all - `Load()` threw, the boss came back null,
   and a `NullReferenceException` then fired every frame. Fixed in `JsonData` by matching
   **case-sensitively**, exactly as Unity's `JsonUtility` did, plus a `FieldsOnly` contract modifier
   so properties never enter the payload in either direction. The modifier alone was not enough: the
   collision is thrown while the contract is still being populated, before any modifier runs.
2. **`SpriteFrameAnimator` reports the player's frames at 136% of its collider** (`anim.json` says
   ppu 34; a 59px frame wants ppu 46.1). This is the animator's own sanity check firing, not a crash -
   it means the pixel-art frame size and the authored body size disagree, which in Unity was hidden by
   sprite-import ppu. Content-level, left as a warning.

### Bugs the port introduced, found by running it

Every one of these compiled clean and would have shipped silently.

1. **Design JSON bound to zeros.** `System.Text.Json` had to be switched to case-sensitive matching to
   stop each data class's Unity field colliding with the Godot-style property beside it - but Godot's
   `Vector2`/`Vector3`/`Color` spell their members `X`/`Y` and `R`/`G`, while the design files (written
   by Unity's `JsonUtility`) spell them lowercase. Every vector and colour in the design folder read as
   zero: the arena collapsed to a zero-sized floor at the origin and all 33 readability colours became
   transparent black. Fixed with explicit converters for the three struct shapes
   (`Scripts/Core/UnityCompat.cs`), leaving everything else case-sensitive as Unity was.
2. **Enemies parented before they were finished.** `GameplayEnemySpawner` put the grunt, leaper, caster
   and Wrath boss into the scene and *then* gave them their `Health`. Unity tolerated that - `Awake`
   fired per `AddComponent` and `Start` came a frame later - but Godot runs `_Ready` the instant a node
   enters the tree, so `EnemyStateMachine._Ready` found no health and every `_Process` threw a
   `NullReferenceException`. The four builders now parent last, which is what `PlaceInWorld`'s own
   comment already claimed the spawner did.
3. **The player's action controller was never parented.** `PlayerController2D._Ready` called
   `motor.AddChild(actions)` while the motor was still propagating ready; Godot refuses that outright,
   leaving a live but orphaned `PlayerActionController` - attacking, parrying, dodging and healing all
   read false forever, and the node leaked. Now added with `CallDeferred`.
4. **The test harness freed itself.** `TestRunner` was the boot scene's root, so the first
   `LoadScene` in any test called `ChangeSceneToFile` and freed the runner mid-test. A `TestBoot` scene
   now parents the runner to the root window, where it outlives every scene change. The runner also
   waits one frame before the first test, since its `async void _Ready` otherwise ran fixtures inside
   the tree's ready propagation, where `AddChild` is refused.
5. **Fall-death fired at spawn** - a downstream symptom of (1): with the floor at zero size the player
   fell past the kill line in under a second. The comparison itself was already correctly flipped.

### The one defect only a playthrough could find

`CharacterBody2D` exchanges no impulse on contact. In Unity both the player and every enemy were
dynamic `Rigidbody2D` at mass 1, so walking into a passive enemy - one mid-telegraph, not writing its
own X velocity - simply shoved it along at half speed. In Godot that enemy is an immovable wall, and
it was throttling *every* chapter run-back to roughly enemy walking pace; chapter two's leaper
happened to stand under a ledge low enough that the player could not hop over it either, so that run
never finished. `PlayerMotor2D.ShoveBlockingBodies()` now pushes a blocking `CharacterBody2D` by half
the blocked horizontal remainder after `MoveAndSlide`, through `MoveAndCollide` so it cannot be shoved
into geometry, and deliberately without stickiness - an enemy that writes its own velocity next step
overrides it, exactly as it overrode Unity's impulse. Chapter two's run-back went from timing out at
233.7 of 280 units to arriving in 34.3 s.

### One failure that is not the port's

`GameplayLayoutIntegrityTests.EveryChapterLayout_KeepsItsPlacementsAndBonfires` asserts every chapter
carries at least 20 enemy placements; every shipped layout carries 7 (4 grunts, 2 leapers, 1 caster).
The design files here are byte-identical to the Unity project's, and the ported test is a faithful
copy of the Unity one, so this test was red on the same data before the port. Its own comment says a
"2026-08-17 density pass rewrote all eight" chapters - that pass is not in the shipped JSON. Filling
those layouts in is design work, not porting work, so it is left red and named here rather than
quietly relaxed.

## Open integration questions

- `PlayerProgression.EnsureOn` takes a `Node` here, not a Unity `GameObject`.
- `PlayerStat`, `PlayerController2D.{IsStaggered, IsGrounded, HealCharges, MaxHealCharges}` and
  `SinResonanceController.{CurrentResonance, CurrentSin, OnSinStateChanged}` are called by the UI and
  must exist with those exact names once Player and Combat land.

## Dropped, with the reason

- **Unity Timeline / `.playable` / `PlayableDirector`** - no Godot counterpart; cutscenes are
  rebuilt over `AnimationPlayer`.
- **Prefabs and ScriptableObject `.asset` files** - the port keeps the Unity project's own rule that
  `Resources/Design/*.json` is the source of truth, and the spawners build actors in code.
- **`.anim` / `.controller`** - replaced by Godot animation resources generated from the same frames.
- **The `InitTestScene` build-settings workaround** - Godot has no build scene list to inject into,
  so `ChapterRoute` enumerates `res://Scenes/*.tscn` directly.
- **UnityMCP, the Unity editor lock, `.meta` files, the two PowerShell Unity suite runners** - all
  Unity plumbing. A headless Godot test run takes no lock, so runs no longer serialise.

## Where it ended up

- `dotnet build`: 0 errors.
- `tools/run-tests.ps1`: **206 passed, 1 failed, 1 skipped** in 689s.
  - The failure is `EveryChapterLayout_KeepsItsPlacementsAndBonfires`, documented above as red on the
    same data in Unity.
  - The skip is `ActorPrefabBodyColorsMatchReadabilityDefaults`, which compared two sources of body
    colour where only one survives the port.
- `res://Scenes/GameplayScene.tscn` and `res://Scenes/TitleScene.tscn` both boot clean headless. The
  only runtime warning left is `SpriteFrameAnimator` reporting the player's pixel frames at 136% of
  its collider - a content mismatch Unity's sprite-import ppu used to hide, not a crash.
- The scripted agent walks the real player through the real chapter and kills the first grunt through
  the InputMap, so the synthetic-input path is proven end to end rather than merely compiled.

## After the port: bringing the codebase up to the production rules

Tracked on `refactor/godot-scene-data`, planned in `docs/migrations/scene-data/PLAN.md`.

### A defect the template pattern was causing

`SetActive(false)` on a detached template baked `ProcessMode = Disabled` into every node copied from
it, and the re-arm path restored only `Visible`. A boss hazard strip or afterimage produced that way
never processed: measured side by side in one headless loop, the old path's `Remaining` timer sat at
5.000 after 30 frames while a `PackedScene` instance had burned down to 4.737. It never burned and
never expired. Instancing a scene cannot inherit a deactivated template's state, so the fix is the
migration itself rather than a patch.

The same change deleted `RangedCaster.CreateDefaultProjectile`, a second builder for the same
two-node tree that left the sprite with no texture and no size - so a caster nobody wired fired an
invisible, unsized projectile.

### Behaviour deliberately preserved

The projectile pool still parks a shot with `Visible = false`, `ProcessMode = Disabled` and
`Monitoring = false` (deferred, because Godot refuses that write while the area is emitting its own
signal) and re-arms all three on get. That is recycling, not the defect above; only the redundant
re-arm on a freshly instanced shot was dropped.

### Tuning defects fixed after the port

- **Knockback shipped at 3 px where 4 metres was authored.** `DamageHitbox2D.knockbackForce` was a
  literal `3f` sitting between two pre-scaled fields, with no setter and no caller - so the value
  reaching `DamageRequest` was three pixels rather than 400. It now reads
  `CombatTuningData.Load().knockbackLight`, authored in metres and converted inside `Load()`, never
  re-scaled at the use site. It reads the Combat-owned loader rather than `PlayerCombat.json`
  directly, because `Combat` must not learn about `Player`; both carry the same authored 4 m.
  Measured before and after through the spawner's own `Configure` path: 3 px, then 400 px.
  `PlayerController2D.AttackKnockback` (400, x1.4 heavy) exists and has **zero consumers** - that
  dead property is the shape the bug really had, and it is still dead.
- **The HUD divided by a literal.** The resonance readout used `/ 100` instead of the tuned maximum.
  It now reads `SinResonanceController.MaxResonance`, a new accessor beside the existing
  `ResonancePerHit` / `PerParry` / `PerDamage`. Same number today; it stops lying the moment a
  designer retunes it.
- **The fallback table had drifted from the design files on seven values** - every archetype's
  `maxPoise` and `soulReward`, plus `soulStainPickupDelay`. `Resources/Design/*.json` won; the C#
  literals moved to match and each now carries a comment naming the field it mirrors. The factories
  cannot read the JSON, because being reachable only when the file is missing is their whole purpose.

  Worth recording precisely, because the audit overstated it: **no test expectation had to move.**
  Every fixture that cares about poise or souls reaches the real JSON, and the design files are
  present in a headless run, so the suite essentially never takes the fallback path. The drift was
  real and would have bitten the first time a file went missing - but the suite was not asserting it.

  The same value is still drifted in a second place, `PlayerResourceData.soulStainPickupDelay`, along
  with five more `[Export]` mirrors listed in the numbers audit. Left for the stage that owns
  `Scripts/Player/`.

### A port defect the scene work surfaced

`GameplayTelegraphPulse` inside an attack readout **never pulses.** Its `_Ready` resolves the renderer
through `GetComponent<Sprite2D>()`, which searches the node and its *children* - and in that tree the
pulse is the leaf, so the lookup returns null and it breathes an empty transform. Unity's
`GetComponent` searched the same GameObject, where the `SpriteRenderer` was, so this is a port defect
rather than inherited behaviour.

It is recorded rather than fixed. Fixing it makes six attack readouts visibly start pulsing, which is
a change no test covers and which someone should see before it ships. The same component works
correctly in `MarkerDisc`, where the pulse is the parent - that asymmetry is why the plan keeps the
two as separate scenes for now.

### Trade-offs the scene migration accepted

- **A malformed authored tree is no longer repaired at runtime.** `GameplayWorldHealthBar.Build` used
  to rebuild missing children; it is lookup-only now. There is one source for that tree, so no such
  case exists - but the robustness is gone deliberately.
- **`MarkerDisc`, `SoulPickup` and `LockOnMarker` hard-reference `Resources/Art/Disc.png`** instead of
  falling back to the procedural generator if the baked PNG went missing. Same class of trade as the
  effect scenes.
- **`GameplayHud.Plate` and `Mul` were `internal` and called across files.** Both are deleted with
  the Theme; `TitleMenuBootstrap` no longer styles buttons in code at all.
- **`TitleMenuBootstrap.CreateButton` named nodes after translated text.** That was the hazard the
  localization work warned about - a change of locale renames the node. Stage 4 closed it: the
  authored screen names every widget after its localisation key plus a role
  (`UI_TITLE_QUITButton`, `UI_OPTION_VSYNCToggle`, `UI_OPTION_MSAARow`) and the test addresses them
  that way rather than by caption.

### What authoring the HUD as scenes changed

- **The four modal panels share `Title` and `Subtitle`.** Godot forbids renaming an inherited node, so
  seven per-panel label names retire (`VictoryTitle`, `VictorySubtitle`, `PauseTitle`, `PauseStatus`,
  `GateTravelTitle`, `LevelUpTitle`, `LevelUpSouls`). Only one was reachable from a test, and it is
  now addressed by full path - which is stricter than the name search it replaced, because a bare
  name would match whichever panel's `Subtitle` came first.
- **Static panel text is authored as localisation keys** and translated by Godot's own
  auto-translation, rather than assigned through `Tr()` when the panel is built. No Korean literal
  survives anywhere and no test reads those strings.
- **`CreateUi`'s `canvas` parameter is now ignored.** The HUD is instanced from a scene that already
  carries its own root, so there is nothing to build into. The method stays because the spawner and
  several tests call it.
- `PlaceRect` and `Face` survived in `GameplayHud` only because `TitleMenuBootstrap` still called
  them. Stage 4 authored that screen and both are deleted - 33 lines, no caller left anywhere.

### What authoring the title screen changed

- **One visual change, deliberate: the four option checkboxes are drawn on the shared menu plate.** A
  `CheckBox` is a `Button`, so with `MenuTheme.tres` on the screen's root it resolves that theme's
  five state plates where the code-built box resolved Godot's default `CheckBox` styleboxes - four
  empty ones and a focus ring. An option row now lights on hover and on focus like every other row on
  the screen, where before it did neither. Kept rather than overridden back, because restoring the
  flat look means overriding four of the five slots with an empty box and reproducing the default
  theme's focus ring for the fifth, or a gamepad loses track of where it is. Four
  `theme_override_styles` lines per box put it back. **Not verified by eye.**
- **Every other pixel is unchanged**, checked with a throwaway probe against the shipped arithmetic:
  the segments come out 53px wide for the four MSAA steps and 72px for the three render scales, which
  is exactly what `(408 - 180 - values * 4) / values` produced. The formula is gone - a
  `size_flags_horizontal = ExpandFill` on the segment strip does it - and so is the last of
  `PanelInnerWidth` / `SegmentCaptionWidth` / `SegmentSpacing`.
- **Static title text is authored as localisation keys**, the same as the HUD panels. The three lines
  that carry a value are still written at runtime: the difficulty button, the preset line, and the
  segment captions (`4x`, `100%`, the translated off).
- **`Scenes/TitleScene.tscn` inherits `Scenes/UI/TitleScreen.tscn`** and adds nothing. The split is
  what lets `TitleGraphicsOptionsTests` instantiate the screen without booting the game's main scene -
  changing scenes in a test frees the runner, which is the current scene.
- **The lit segment is still the `SegmentActive` type variation**, not an instance override.
  `TitleGraphicsOptionsTests.AssertLit` reads it back through `GetThemeColor("font_color")`, which
  resolves the variation ahead of the base type; losing the theme chain lights all four segments and
  fails the "exactly one" assertion rather than passing quietly.
