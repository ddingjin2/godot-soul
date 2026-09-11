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

  **Finished later: `Resources/Design/CombatTuning.json` now exists.** The fix above left the
  authored 4 m resting on a C# field, because the file `CombatTuningData.Load()` had always pointed
  at was never written. It is there now, registered in `addons/mygame_tools/DesignDataFiles.cs`, and
  it owns hit stop, screen shake, the hit flash and squash, the audio fallback gain, the pack spacing
  and that knockback - 27 keys, every one seeded from the literal the game was actually running, not
  from the orphaned class's own defaults, which disagreed on three values (hit stop 0.04/0.08 vs the
  live 0.08/0.12, medium shake duration 0.2 vs the live 0.12). The class's six stamina fields were
  deleted rather than authored: `PlayerResources.json` owns those and a third copy is what the fix
  was for. So were `hitStopDurationBoss`, `knockbackMedium`, `knockbackHeavy`, `knockbackDecayRate`
  and the three `telegraph*` fields - nothing read them, and a key nothing reads looks like tuning
  without being any. **No shipped value changed.**

  One conversion moved: the five shake intensities are authored in metres and are now scaled inside
  `CombatTuningData.Load()` instead of at the far end in `CameraShake._Process`, which is where the
  project's boundary rule puts it. `CameraShake.TriggerShake(float, float)` therefore takes **pixels**
  now; every caller in the repository goes through `CameraShakePreset`, so nothing else moved.
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

### What authoring the actors as scenes changed

Stage 8 of the scene migration. `Scenes/Actors/Player.tscn` plus `EnemyBase.tscn` and the five
archetype scenes that inherit it replace `GameplayPlayerSpawner.BuildPlayer` / `CreatePlayerSword` /
`CreateAttackReadout` and `GameplayEnemySpawner.CreateEnemyRoot` / `AddCapsuleCollider` /
`AddActorVisual` / `CreateAttackReadout` / `CreateRoleMarker`. Both spawners keep every tuning call
and the order of it, which is where the three ordering traps live.

- **Actor and sword textures are hard `ext_resource` references** to `Resources/Art/Actor*.png` and
  `Sword.png`, where `GameplayVisualFactory.CreateActorSprite` / `CreateSwordSprite` preferred the
  baked PNG and fell back to the procedural generator. Delete a baked actor PNG now and the actor is
  invisible rather than greybox. Same trade the marker and effect scenes already took.
- **`GameplayVisualFactory.Dress` has no caller left on the actor path.** The Unity pivot it turned
  into an `Offset` is authored per sprite; the size and colour it wrote are still bound per spawn, by
  `DressSprite` and `DressActorVisual`, because both are designer-owned.
- **`EnemyGroupCombat` and `CombatFeedback` are authored on `EnemyBase.tscn`.** The guard blocks in
  `Scripts/Enemy` (`MeleeGrunt:85,90`, `LeapingAttacker:84,89`, `RangedCaster:99,104`,
  `WrathMiniBoss:189`) now always find one and build nothing. They are left standing - that folder
  was not this stage's to edit - and are dead code the next Enemy-owning stage can remove.
- **The player's `AudioSource` is authored** rather than created by the first cue that has a clip.
  `AudioFeedback.Play` looks it up by type before creating one, so only the allocation moved.
- **`EnemyDeathCleanup` is authored on the three mob scenes and absent from both boss scenes**, which
  is what `EnsureEnemyHealthRig`'s `destroyOnDeath` flag decided before. Flag and `EnsureComponent`
  both stay: a test that hands the spawner a bare `Node2D` still gets one built.
- **A latent bug found and fixed inside the stage.** `CreateChapterBoss` used `AddComponent<Poise>`
  and `AddComponent<SoulsWallet>`, which on an authored boss added a *second* of each.
  `GetComponent<SoulsWallet>()` kept answering with the authored, empty one, so every chapter boss
  kill was worth 0 souls. `GameplayChapterBossTests` caught it; both are `EnsureComponent` now.
- **`GameplayWorldHealthBar` did not fold into the actor scenes** the way the plan's debt note
  expected. The component is still a child node reached by `EnsureComponent`, with
  `WorldHealthBar.tscn` instanced *under* it as `HealthBar`; making the component the bar's root would
  break `AddHealthBar`, which both spawners and several tests call. The debt stands, and
  `PLAN.md` is corrected to say so.
- **`GameplayBuildShim.NewObject` / `AddComponent` / `EnsureComponent` survive.** The audit expected
  Stage 8 to retire them. `Scripts/Core` was out of scope, `EnsureComponent` is still what lets a test
  hand either spawner a bare `Node2D`, and seven tests build actors through `AddComponent` directly.
  Retiring the shim is its own stage.
- **Six more scene-path `const`s** join the six the plan already counts as debt.

### Numbers stages S6 and S7 - shared reach, AI timing, archetype and chapter-boss fields

Additive throughout: **79 new keys (35 distinct fields) across 22 shipped design files, no existing
key's value changed**, and the whole suite landed on its usual baseline. Proven read rather than asserted - a throwaway probe
outside `Tests/` edited every one of the 22 files, watched each changed number arrive with the
metre-to-pixel conversion applied where it belongs and *not* applied to a time or a multiplier, then
restored all 36 design files byte-identical (SHA-256 compared) and, with `WorldTuning.json` and
`MeleeGrunt.json` moved away, watched `GameplayScene` boot and exit 0 on the fallbacks.

- **`WorldTuning.json` gained the shared enemy block** (9 keys): `enemyGravity` 9.81 m/s^2,
  `enemyDisengageDistance` 8 m, `enemyLedgeProbeForward` 0.35 m, `enemyLedgeProbeDepth` 1.1 m and
  `actorMoveAnimThreshold` 0.15 m/s are scaled in `Load()`; `enemyIdleToPatrolTime` 3,
  `enemyInvestigateDuration` 2, `enemyRecoveryDuration` 1 and `fallDeathRespawnLockout` 1 are seconds
  and cross untouched. Enemy gravity was the audit's open question (§3.4): it is tuning now, because
  the player's half of the same fall has been authored since the port and a designer could tune only
  one of the two.
- **The perfect-parry reward is consumed, not re-typed.** `PlayerCombat.json.perfectParryStunMultiplier`
  already existed; the four archetypes each carried their own `1.6f`. The spawner pushes it now.
  `GameplayEnemy2D` still types its own - it is the legacy class the audit exempted (§3.10).
- **Two knockbacks stopped ignoring the file beside them.** The caster's shot passed a literal
  `World.U(3f)` while `RangedCaster.json.attackKnockback` said 3.0, and Wrath's rush passed
  `World.U(5f)` while `WrathMiniBoss.json.attackKnockback` said 5.0. Both read the authored number
  now. **Same values, so nothing moved.** Wrath's *slash* is the exception: it passed `World.U(6f)`
  against an authored 5.0, so consuming the shared key would have quietly weakened the fight. It got
  its own new key, `attackKnockbackSlash: 6.0`, seeded from the live literal.
- **Four decisions the audit left open, settled.** §3.4 enemy gravity - tuning. §3.5 ledge probes -
  the forward reach and the depth moved; the `0.05`/`0.08` collision insets stayed, they exist to keep
  a ray out of its own collider. §3.9 `maxChainSteps` - tuning, per chapter, where the pacing is.
  §3.18 the leaper's landing punish - tuning; the window's *length* was already authored and its
  payoff was not. §3.12 was followed as written: `strafeFlipChance` moved as-is, per frame, and the
  frame-rate dependency stays a separate bug.
- **Nothing was migrated that a scene already owns.** Five audit rows were skipped for that reason:
  `gateTravelZoneRadius` and `shortcutGateZoneRadius` (`GatePortal.tscn` / `ShortcutGate.tscn` author
  radius 140 and 500, and `EnsureTrigger` leaves an authored shape alone), the caster's
  `projectileRadius` and `projectileSize` (`EnemyProjectile.tscn` authors both), and the grunt's
  `attackPointOffset` (`MeleeGrunt.tscn` authors `AttackPoint` at 60 px, so the `World.U(0.5f)` branch
  only ever runs for a synthetic actor). Wrath's `attackPointOffset` **was** migrated - that boss has
  no `AttackPoint` node, so its offset is what every slash actually uses.
- **The telegraph blend targets stayed in code.** Each archetype's telegraph *base* colour is authored
  now (`telegraphColor`), but the yellow and red it blends *toward* are the shared danger language and
  belong with the rest of the palette; authoring half of the pair here would make two owners of one
  read. Named so a later stage does not think it was missed.

### Numbers stage S12 - difficulty and New Game+

A new file, `Resources/Design/DifficultyTuning.json`, with the five multipliers `DifficultySettings`
used to type inline: `easyPlayerDamageTaken` 0.7, `hardPlayerDamageTaken` 1.4, `easyEnemyHealth`
0.85, `hardEnemyHealth` 1.25, `newGamePlusEnemyHealthPerCycle` 0.25. Nothing spatial, nothing
scaled; Normal stays the implicit 1.0 with no key. `DifficultySettings` keeps its public surface and
reads `DifficultyTuningData.Shared` - a lazily read singleton, the same shape as `CombatTuningData` -
so no bootstrap call was needed. Proven read: `easyPlayerDamageTaken` set to 0.5 turned the shipped
`100 - 10 * 0.7 = 93` assertion into `95`, then the file was restored. The `ponytail:` note that
asked for exactly this migration is retired with it.

### Numbers stage S9 - camera feel

Four keys appended to `WorldTuning.json`, no existing value changed (the previous last key gained its
trailing comma, nothing else): `cameraDeadZone` {1.2, 0.6} m, a half-extent, so both components are
scaled by `World.U` and neither flips; `cameraLookAhead` {1.4, 1.2} m, an offset, scaled **and**
Y-flipped through `World.V` - the one field in the migration the plan flagged as most likely to be got
wrong; `cameraSmoothTime` 0.25 s, untouched; `cameraMaxFollowSpeed` 12 m/s, scaled. All four were
literals on `GameplayCameraFollow2D`, which kept its Unity-metre initialisers as the fallback and gained
`ApplyTuning(WorldTuningData)`; `GameplaySystemBootstrapper.ConfigureCameraFollow` pushes the file in
right before `Initialize`. Proven read: `cameraLookAhead.y` set to 3.3 printed `lookAhead=(140, -330)`
from a headless boot of `GameplayScene` (dead zone `(120, 60)`, max speed `1200`), then the file was
restored. Seeded from the live literals, so a scene run without the file moves the same.

The four stale bound fallbacks at `GameplayCameraFollow2D.cs:26-29` (§2.8 - written un-flipped,
belonging to no arena) are gone; the rig is unbounded until `Initialize` hands it the arena's own,
which every code path does before a target is set. `WorldTuning.json` was chosen over a new
`CameraTuning.json` because the file already owns "reach numbers that belong to no single actor" and
the registry stays untouched.

### Numbers stage S8 - cutscene tuning

A new file, `Resources/Design/CutsceneTuning.json`: the four shots' keyframe tables (one array per
Timeline track - `fade`, `letterbox`, `cameraSize`), the trigger beats (`bossIntroMoveDuration` 0.5,
`deathHitStop` 0.12, `deathMoveDelay` 0.45, `deathMoveDuration` 0.35, `deathMoveDistance` 0.6 m) and
the bootstrap's entry settle (`enterSettleHeight` 1 m, `enterSettleDuration` 1.2). Three units cross
the boundary and only one is scaled: the two metres above become pixels in
`CutsceneTuningData.Load()`; letterbox rows are UI pixels and are deliberately not scaled; fade is
alpha and every time is seconds. The `* World.Ppu` that `CutsceneDirector` used to apply at the far
end is gone with the channel that needed it (next paragraph). Proven read: renaming the `BossIntro`
key in the file turned `Cutscene_PutsTheZoomBackToTheSceneDefault_OnCompletionAndOnSkip` red with
`No sequence named 'Cutscenes/BossIntro'`, then the file was restored byte-identical.

- **The camera track is a fraction of the resting size, not an absolute size.** Unity's clips animated
  `orthographicSize` from 6.8 to 5.984 / 6.392 / 6.12 - the same 6.8 that `SceneLayout.json` owns,
  copied by hand with no compile-time link, so a chapter that framed its room wider would have had its
  cutscenes snap to the shipped number. The file authors the ratios instead (0.88 / 0.94 / 0.9 - the
  Unity numbers divide exactly) and the director applies them to the zoom it found the camera at when
  the shot started. Same push on every chapter, no `6.8` anywhere in the cutscene layer, and the
  metre-to-pixel conversion the audit asked to move into `Load()` has nothing left to convert.
  **One chapter moves:** `SceneLayout_Chapter08_White.json` frames at 7.2, so its boss intro used to
  snap from 7.2 to the absolute 5.984 (a 17% push) and end the shot at 6.8 before `Restore` put 7.2
  back; it now pushes to 6.336 (the same 12%) and ends where it started. The other seven chapters
  frame at 6.8 and are unchanged to the pixel.
- **The entry bars are the shot's own opening height.** `GameplayBootstrap`'s `64f` and the first
  `GameplayEnter` letterbox row were the same number in two files. The bootstrap now stages
  `CutsceneTuningData.OpeningLetterbox("GameplayEnter")` - one key, read by both.
- **The shots have no C# fallback.** The scalar beats keep their shipped literals as `[Export]`
  defaults; the four keyframe tables exist only in the file, like `SinTuningData.sins`. A missing file
  means every `Play` takes the director's existing missing-shot path (clear the overlay, hand the
  continuation back, carry on) rather than a second copy of forty numbers drifting in code - the
  audit's §2.8 problem. A test keeps the file honest: all four keys present, every channel ending on
  neutral, because `Skip` writes neutral rather than evaluating the last keyframe.
- **Not migrated:** `CutsceneOverlay`'s two colours, caption font size and caption box offsets
  (§2.5a's last row). The overlay is a saved scene since S1 and authors all of them; a design key
  would be a second owner.

### Numbers stage S10 - readability layout

A new file, `Resources/Design/ReadabilityLayout.json`, beside the artist's `Resources/Art/Readability.json`
and deliberately not inside it: colour is the artist's, size is the designer's, and
`GameplayTuningCatalog` already loaded the palette from its owner's folder for exactly that reason.
The audit planned to regenerate `Readability.json` as a superset; a sibling file costs nothing and
keeps the artist's file byte-identical, so that is what landed. 46 keys - the ten collider and visual
sizes, the hitbox radius and offset, the sword and arc sizes, the eight health-bar geometries, the ten
danger-readout positions and sizes, the four role-marker positions, the four label sizes - plus the
lock-on hover height, the health bar's frame margin and low-health read, and the platform rim. All
transcribed from `GameplayReadabilityDefaults.CreateBase`, which keeps the same numbers as the
fallback. Conversion happens once, in `GameplayReadabilityLayoutData.ApplyTo`: sizes, radii and
thicknesses through `World.U`; offsets and local positions through `World.V`, scaled and flipped;
point sizes and fractions untouched. Proven read: `bossVisualSize.x` set to 9.75 turned the identity
test red with `Expected <175>, was <975>`; the file was restored. Three tests hold the file to the
code the way the palette's do, including one that hands every field a distinct metre value and
demands the pixel value its kind implies.

- **Four dead properties deleted rather than migrated.** `PlayerHitboxAnchorLocalPosition`,
  `SwordLocalPosition`, `SwordLocalRotation` and `AttackArcLocalPosition` had no reader since the
  actor scenes (stage 8) started authoring those transforms in `Player.tscn`. A key nothing reads is
  worse than a literal - it looks like tuning.
- **The spawners still overwrite the scenes.** Every actor scene also authors a collider radius, a
  visual scale, a bar offset and the readout positions, and `GameplayEnemySpawner` / `GameplayPlayerSpawner`
  write the readability numbers over them on every spawn - stage 8's deliberate "designer-owned, bound
  per spawn" decision. So the file is the live owner and the scene values are mirrors that never win.
  That is a standing debt from stage 8, not new here, and it is named so the mirrors are not mistaken
  for tuning.
- **`WorldTuning.json` gained the arena furniture** (3 keys): `worldEdgeWallThickness` 0.5 m,
  `worldEdgeWallHeight` 40 m and `gatePortalOffsetX` 3 m, all scaled in `ScaleToPixels`, consumed by
  `GameplayEnvironmentBuilder` with the shipped metres kept there as the fallback for a run with no
  file. Proven read: the portal offset set to 7 printed 700 px on a headless boot. The platform rim
  (`platformRimThickness` 0.05 m, `platformRimHeightFraction` 0.58) went to the readability file
  instead, beside the rim colour it dresses.
- **`ShortcutGate.OpenAlpha` is an `[Export]`** at its shipped 0.25, so `ShortcutGate.tscn` owns it
  the way the rule asks for presentation numbers. The scene file is unchanged - Godot writes only
  non-default values.
- **Corrected on review: the six `GameplayTelegraphPulse` / `ActorIdleBob` feel numbers were not
  scene-owned.** The stage first recorded them as "authored on the scenes"; a compliance pass found
  that the five scenes carrying the pulse node set none of its `[Export]`s and that the bob is
  attached from code, so all six lived only as code defaults - exactly the `@export`-default shape
  the rule names as non-compliance. The pulse's four (`markerPulse*`) are `ReadabilityLayout.json`'s
  now, beside the marks they breathe on; the bob's two (`actorIdleBob*`) are `WorldTuning.json`'s,
  beside `actorMoveAnimThreshold`. Both nodes lost their `[Export]`s and read the files at `_Ready`.
  `ShortcutGate.openAlpha`, an `[Export]` from the same stage, is now written in `ShortcutGate.tscn`
  for the same reason.
- **Not migrated, because a scene already owns them:** `GameplayLockOnMarker`'s size and
  `SoulPickup`'s stain (both authored in their scenes), the grunt attack-point offset and the caster
  projectile size (S7 already skipped them for the same reason), and `GameplayWorldHealthBar`'s frame
  colour and low-health tint (the frame colour is authored on `WorldHealthBar.tscn`; both are palette,
  and the palette file was not to be touched). The 20 sorting orders stay in code pending §3.6.

### Numbers stage S13 - decisions and dead code

The nineteen open items in `AUDIT_NUMBERS.md` §3 are settled; the table at the top of that section
says what was done for each and by which stage. Two touched code:

- **`GameplayEnemy2D` is deleted.** Its own comment said it was kept because "the earliest scenes and
  tests still build one"; by S13 no scene, no test and no spawner did, and the one reader left was an
  optional `GetComponentInChildren` in `MyGameStateProbe`, which now reads the state machine alone.
  The 11 unowned tuning numbers the audit counted against it (§2.4n) go with it. A probe that
  observes a synthetic enemy built on the legacy class would have reported its stun; none exists.
- **`GameplaySceneDefaultsAsset` keeps its stale numbers no longer.** The camera defaults (8, 2) /
  7.0 named a framing no shipped layout has; they are the shipped (0, 2) / 6.8 now, so an asset a
  designer creates in the inspector starts as a no-op. The type stays - two tests pin it and it is
  the per-scene override hook - and still no `.tres` ships, which the class now says out loud.

And one decision the plan had scheduled as its own stage: **`GameplayBuildShim` is not retired.**
Measured rather than assumed - product code calls `NewObject` twice (the cutscene director node and
the deliberately bare decoration sprite), `AddComponent` never, and `EnsureComponent` only as a
get-or-add that adds nothing to a scene-built actor. What is left is not a world builder, so there is
nothing to retire; the class comment and `PLAN.md` carry the reasoning.

### Second phase K0 - a missing design file is an error

Until now a missing file under `Resources/Design` was silent: `Res.LoadJson` returned null and every
loader either fell back to its `[Export]` defaults or conjured a `new XData()`, so the game ran on
numbers nobody had authored and nothing said so. Now `Res.LoadJson` pushes an error naming the file
(the two lookups that miss by design - a scene's own `SceneLayout_<Name>` and a spawn row's variant
file - pass `required: false` and report in their own words), the three `.Shared` loaders return null
instead of an empty object, and `GameplayBootstrap._Ready` checks `GameplayTuningCatalog.IsComplete`
plus the three `.Shared` before building anything. One file missing means two error lines and no
arena. Proven by moving `WorldTuning.json` aside for one headless boot, then restoring it. With every
file present - which is every shipped build, since they travel in the PCK - nothing changed.

### Second phase K2-K4 - the code copies are gone

Three stages landed together once K1 had taken the suite off the fallbacks.

K2: the four archetypes (`MeleeGrunt`, `LeapingAttacker`, `RangedCaster`, `WrathMiniBoss`) read every
number from `tuningData`; the 67 `??` sites, 39 ternaries and two null early-outs are gone, as are the
two `bossData?.` reads in `RainbowChapterBossBehaviour.PulseTelegraph`. An archetype that enters the
tree without `SetTuningData` logs one error and freezes (`SetProcess(false)`, `SetPhysicsProcess(false)`)
instead of playing on inline numbers. The spawner and the tests both hand the file over, so nothing
shipped changed.

K3: the 17 tuning Data classes carry no `[Export]` initialiser any more (309 to 1; the survivor is
`RainbowChapterBossData.attacks = Array.Empty`, a null guard). A key a design file omits now reads as
zero. The files were completed first, additively and at the former default in every case: the three
shortcut-gate keys in the eight `SceneLayout*.json` (only `SceneLayout_Chapter02_Orange` has
`hasShortcutGate: true`, so the earlier "seven gates at size 0" warning was overstated - the one live
key was `shortcutOpensFromRight` there), four phase-two weights in `Chapter01_Red_Encounter.json`, 360
keys across the eight chapter-boss files (top-level hop/chant/afterimage numbers and the attack rows
against `BossAttackProfile`), and the four zero-valued switches (`approachHopInterval`,
`chantInterval`, `afterimageCount`, `stanceRotationInterval`) written as explicit zeros where a boss
does not use them. `ProgressionTuningData.Load` returns null for a missing file like every other
loader; `PlayerProgression` then reports every stat as capped and applies nothing, with one error,
instead of running the shipped curve from code.

K4: `GameplaySceneDefaults.Create()` and `CreateForScene()` build from the layout file only (92
literals gone) and return null when it is missing. `GameplayReadabilityDefaults.CreateBase()` is gone;
`Create()` applies `Art/Readability.json` and `Design/ReadabilityLayout.json` over the twenty sorting
orders that stay in code by decision D2 (197 literals gone). The editor dock lost
`ReadabilityThemeWriter` (D7). The two identity tests that compared the files against the code copy
are replaced by `DesignFileCompletenessTests`, which fails naming the file and the keys whenever a
design file does not name every field of its type (D6).

Test fixtures: six chapter-boss test files gained the keys their inline JSON used to inherit from
class defaults (`stunDuration`, `bodySize`, the poise block, `telegraphPulse*`), all at the former
default. No expected value changed in those fixtures; one assertion did, below.

Discovery, the kind the plan predicted: `P0CombatStabilityTests.GameplaySceneIncludesCheckpointRunSection`
looked for a platform named `CheckpointRunPlatform`. That name existed only in the code copy of the
layout that K4 deleted; `SceneLayout.json` sets `overridePlatforms: true` with its own list (`FirstStep`
... `KilnStep`) and no such platform ever shipped, so the test was verifying a fixture the game did not
use. It is replaced by `GameplaySceneNamesEveryPlatformItShips`: the shipped list is non-empty and every
platform carries a name, which is the contract the instanced platforms and two other tests rely on.

### Second phase K5-K6 - the components, the HUD, the bosses and the scenes

No shipped behaviour changed in any of the three. Every value the components read back after the move was
probed against the literal it replaced and was identical (camera feel, idle bob, telegraph pulse, health bar
margin and colours, HUD timings and palette); every constant deleted from the two bosses, `BossAttackProfile`
and `EnemyStateMachine` equalled the value its design file already carried, verified across all nine encounter
files and all 36 attack rows before the strip; and the seven actor scenes lost only values the spawner writes
on every spawn, proved by a 493-value dump of the spawned actors before and after.

What changed is a build with a design file missing (decision D1): the camera stops following, the idle bob and
telegraph pulses stop, the world edges and gate portal are not built, lock-on has no reach, the bonfire has no
radius, the HUD ghost gauge has no hold, and a chapter boss or mini-boss logs one error and stops ticking - each
naming the file - instead of running on numbers nobody authored.

Editor-view trade-off accepted with D4: the actor scenes now open in the Godot editor at type defaults - an
unscaled body on a 10 x 20 capsule, role label and health bar on the origin - because every size, colour,
sorting order and offset lives only in `ReadabilityLayout.json` and `Readability.json`. Run the scene to see the
real proportions. Nothing about a spawned actor changed.
