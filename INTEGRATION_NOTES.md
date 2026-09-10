# Integration notes - signatures that changed in the port

Anyone porting a folder that calls into an already-landed folder reads this first. It lists only the
places where the Godot signature is **not** identical to the Unity one. Everything not listed here
kept its Unity name and shape.

## Core shims (Scripts/Core)

- `World` - `Ppu` (100), `U(float)`, `V(Vector2)`, `Layer.{World,Player,Enemy,Ground,PlayerHitbox,EnemyHitbox,Trigger,GroundProbe}` (uint bit masks), `Group.{Player,Enemy}`.
- `GameClock` - `DeltaTime`, `UnscaledDeltaTime`, `Time`, `UnscaledTime`, `FixedDeltaTime`, `TimeScale`.
- `Phys2D` - `OverlapCircle`, `OverlapCircleAll`, `Raycast`, `IsOnLayer`, `FindActorInGroup`, `RayHit2D`.
- `PlayerPrefs`, `JsonData`, `Res` - Unity-compatible.
- `NodeExt` (in `CombatShim.cs`) - **use this instead of Unity's `GetComponent`**:
  `node.GetComponent<T>()` (this node or a direct child), `GetComponentInParent<T>()`,
  `GetComponentInChildren<T>()`. Interfaces work. This is how "components on one GameObject"
  translate: a component is now a child node of the actor root.

## MyGame.Combat

- `DamageRequest` / `DamageResult`: `GameObject Attacker/Target` is now **`Godot.Node`**, same
  positions in the constructors. This is the change that touches every caller.
- `DamageHitbox2D.Initialize(Transform owner)` -> `Initialize(Node2D owner)`.
- `DamageHitbox2D.Configure(float radius, Vector2 offset, LayerMask)` -> `Configure(float, Vector2, uint)`;
  pass `World.Layer.Enemy` / `World.Layer.Player`.
- Every `UnityEvent` field is a C# `event Action` / `event Action<T>`: subscribe with `+=`, never
  `.AddListener`. Affects `Health.{OnHealthChanged,OnHealthDepleted,OnDamaged,OnHealed}`,
  `Poise.{OnPoiseChanged,OnPoiseBroken}`, `SoulsWallet.OnSoulsChanged`, `HumanityController.*`,
  `CombatFeedback.{OnHitFeedback,OnInvulnerableHit}`, `CombatResultBroadcaster.OnResult`,
  `SinResonanceController.{OnSinActivated,OnSinDeactivated,OnSinStateChanged}`.
  The fields that were already `System.Action` are unchanged
  (`DamageHitbox2D.{OnHit,OnParry,OnPerfectParry}`, `SinResonanceController.{OnApplyModifiers,OnResetModifiers}`).
- New public `AudioFeedbackCueClip : Resource` - was AudioFeedback's private nested struct; Godot
  cannot export an array of a nested type.
- `GraphicsOptions.LoadAndApply()` lost Unity's `[RuntimeInitializeOnLoadMethod]`. **A bootstrap must
  call it once at startup** or graphics options stay at defaults.
- New `ChapterRoute.ScenePath(string chapterName)` -> `"res://Scenes/<name>.tscn"`. Use it for
  `ChangeSceneToFile`; chapter names themselves are still bare, as in Unity.
- `ChapterRoute.Scenes()` now enumerates `res://Scenes/*.tscn` (top level), drops `TitleScene`, sorts
  ordinal and pulls `GameplayScene` to the front (it is chapter one but sorts after `Chapter02_*`).
- Node types: `DamageHitbox2D` is an `Area2D`; `CombatFeedback` and `EnemyGroupCombat` are `Node2D`;
  `Health`, `Poise`, `DamageReceiver`, `SoulsWallet`, `HumanityController`, `SinResonanceController`,
  `HitStopManager`, `AudioFeedback`, `CombatResultBroadcaster`, `CameraShake` are `Node`;
  `CombatTuningData` and `SinTuningData` are `Resource`.
- `CameraShake` drives `GetViewport().GetCamera2D().Offset`, so it no longer needs to be parented to
  the camera, and converts its intensities to pixels itself - do not scale them again.

## MyGame.UI

- `GameplayHud` is a `CanvasLayer`; `CreateUi(CanvasLayer canvas = null)` defaults to itself, so a
  spawner does `AddChild(hud); hud.CreateUi();`.
- `UnityAction` parameters are `System.Action`: `SetPauseVisible(bool, Action, Action, Action)`,
  `SetGateTravelVisible(bool, string[], string[], string, Action<string>, Action)`,
  `ShowVictory(Action, Action, string, string)`, `SetLevelUpVisible(bool, Node player, Action closeAction = null)`.
- `LoadUiFont()` returns `Godot.Font`.
- The HUD expects `PlayerProgression.EnsureOn(Node player)` (Unity took a `GameObject`) and a
  `PlayerStat` enum with `Vitality/Endurance/Strength/Resolve`.

## MyGame.Player

- **`PlayerMotor2D` IS the player `CharacterBody2D`.** `Velocity` is the inherited property, and
  `MoveAndSlide()` runs inside `FixedTick`, driven from `PlayerController2D._PhysicsProcess`.
- `PlayerMotor2D.Initialize(Rigidbody2D, Collider2D)` -> **`Initialize()`** (it only sets `UpDirection`).
- `PlayerActionController.Initialize(...)` now takes **`(PlayerMotor2D, StaminaSystem, DamageHitbox2D)`** -
  the motor replaces the rigidbody the dodge used to write velocity into.
- **The spawner must set the player body's `CollisionMask` to
  `World.Layer.World | World.Layer.Ground | World.Layer.Enemy`.** Unity's
  `LayerMask.GetMask("Default","Ground","Enemy")` moved from a motor field onto the body itself; without
  it the player stands on nothing.
- **The `_Ready` trap is preserved**: `PlayerController2D._Ready` re-runs `actions.Initialize(...)` with
  its own (usually null) `damageHitbox`, so a spawner must still call `SetDamageHitbox(hitbox)` *after*
  the player is in the tree.
- `SetMoveInput` on both the controller and the action controller takes **Godot-space axes (+Y down)**.
  `GameplayInput.Vertical` is the flip point.
- `ApplyKnockback(direction, force)` expects `force` already in px/s.
- `PlayerProgression` and `PlayerStateMachine` are **plain classes** composed into `PlayerController2D`.
  `PlayerProgression.EnsureOn(Node)` resolves the controller and returns `controller.Progression`;
  the progression exposes `Owner` (the player node) and the controller exposes `Progression`.
- `PlayerAttackAnimator2D` drives an **`AnimationPlayer`**, not `SpriteFrameAnimator`. `AttackTriggerName`
  is the clip name; `Stop()` + `Play()` replaces `ResetTrigger`/`SetTrigger`. The `AnimationEvent_*`
  hooks keep their names for a method track.
- `DeathStateController` moves the **body's** `GlobalPosition` (the body is a child node now) and has a
  local `SetNodeActive` that hides, sets `ProcessMode` **and disables descendant `CollisionShape2D`s** -
  `ProcessMode` alone leaves an "inactive" node solid. Copy that pattern anywhere "SetActive(false)"
  has to mean "not solid".
- `PlayerLockOn` clears in `_ExitTree`; a caller that parks the node must call `Clear()` itself.
- Tuning loaders: call **`PlayerMovementData.Load()` / `PlayerCombatData.Load()` /
  `PlayerResourceData.Load()` / `ProgressionTuningData.Load()`**, never a generic `LoadDesign<T>` - the
  generic path would skip the `World.Ppu` scaling those `Load()` methods apply.

## MyGame.Enemy

- `IBossEncounter`: `event Action IntroStarted` / `event Action Defeated` (events, not properties) and
  **`Node2D BossObject`** (was `GameObject`).
- **Enemy "prefabs" are detached template `Node2D`s duplicated with `Duplicate()`**, not `PackedScene`.
  The Unity source built them at runtime with `new GameObject` + `SetActive(false)`; a never-parented
  node is the exact equivalent. `SetProjectilePrefab(Node2D)`, `SetHazardPrefab(Node2D)`,
  `SetAfterimagePrefab(Node2D)`.
- `EnemyProjectilePool(Node2D prefab, Node parent)` - the `parent` argument replaces what Unity's
  `Instantiate` did for free.
- Tuning loaders: **`MeleeGruntData.Load()`, `LeapingAttackerData.Load()`, `RangedCasterData.Load()`,
  `WrathMiniBossData.Load()`, `BossEncounterData.Load(path)`, `RainbowChapterBossData.Load(path)`.**
  A raw `Res.LoadJson` leaves the data in metres and skips `OnValidate`.
- **Knockback that reaches `DamageRequest` is already in pixels/second.** Do not scale it again;
  `PlayerMotor2D.ApplyKnockback` expects px/s and matches.
- Enemies are `CharacterBody2D`; the Unity `_rb.linearVelocity` is `Velocity`. A defeated boss's
  `bodyType = Static` is `protected bool _bodyFrozen` in `EnemyStateMachine`, which skips gravity and
  `MoveAndSlide`.
- Enemy gravity is applied in `EnemyStateMachine` / `GameplayEnemy2D` at `World.U(9.81f)`, because
  `project.godot` sets `default_gravity = 0`.
- No `MyGame.Player` type is referenced from Enemy any more: the player is found with
  `Phys2D.FindActorInGroup(hit, World.Group.Player)`.

### A second component shim exists

`Scripts/Core/EnemyShim.cs` adds `Find*`-named extensions on `Node` (`FindComponent<T>`,
`FindComponentInParent<T>`, `FindComponentsInChildren<T>`), deliberately named differently from
`CombatShim.cs`'s `GetComponent<T>` family so the two cannot become ambiguous overloads. It also has
`Sprite2D.SetSpriteSize(Vector2 px)` (Unity's sliced `SpriteRenderer.size`) and
`Node2D.BodyBounds(Vector2 fallbackHalfExtents)` (Unity's `Collider2D.bounds` as a `Rect2`).
Both shims are fine to use; prefer whichever name reads better at the call site.

## Scene migration — signatures that moved (branch `refactor/godot-scene-data`)

Changed by the move from runtime-built node trees to authored scenes. See
`docs/migrations/scene-data/PLAN.md` for why.

### Effects are `PackedScene` now, not template nodes

The three "prefab" fields were `[Export] Node2D` holding a detached template that callers copied with
`Duplicate()`. They are `[Export] PackedScene` and callers instance them:

- `RangedCaster.SetProjectilePrefab(PackedScene scene, Color? tint = null, int? sortingOrder = null)` -
  the two optional arguments carry the designer's `projectileColor` and sorting order from
  `Resources/Art/Readability.json` through to each instance. Baking the colour into the `.tscn` alone
  would have silently killed a live override; the scene ships the code default so an unwired caster
  still works.
- `RainbowChapterBossBehaviour.SetHazardPrefab(PackedScene)` and `SetAfterimagePrefab(PackedScene)`.
- `EnemyProjectilePool(PackedScene scene, Node parent)`.
- `EnemyProjectile.ScenePath`, `BossHazardStrip.ScenePath`, `BossAfterimage.ScenePath` - the
  `res://Scenes/Effects/*.tscn` constants, used as the fallback when nothing wired a scene.

`GameplayEnemySpawner.CreateProjectilePrefab` / `CreateHazardPrefab` / `CreateAfterimagePrefab` are
**deleted**. `RangedCaster.CreateDefaultProjectile` is **deleted** - it was a second, divergent
builder for the same tree whose sprite had no texture and no size.

`GameplayPrefabNames.EnemyProjectile` is now unused. The scene root keeps that node name, so nothing
depends on the constant; it is left in place rather than removed in a stage that did not own the file.

### Cutscene overlay

`CutsceneOverlay.Create()` keeps its signature but instances
`res://Scenes/UI/CutsceneOverlay.tscn` instead of constructing nodes. `Build()` and `CreateBar()` are
gone. The child names (`Fade`, `LetterboxTop`, `LetterboxBottom`, `Line`) are the contract the script
binds against - they were the Unity Timeline track paths and they stay fixed names.

### Arena geometry is instanced, not built

`GameplayEnvironmentBuilder.Build` is unchanged - it still reads `SceneLayout_*.json` and places what
it makes. Only the construction moved:

- `CreateSolidBox` gained a sixth parameter, `string scenePath = "res://Scenes/World/SolidBox.tscn"`,
  and now instances that scene instead of building a `StaticBody2D` node by node. It also parents the
  body itself, which `GameplayBuildShim.NewObject` used to do. Every caller still passes the layout's
  own name (`platform.Name`, `"Ground"`, `"SpiritPlatform"`, `"ShortcutGate"`, the two world edges),
  so the built arena carries the same node names it always did - the scene root's name never survives.
- `CreateWorldEdge` builds no nodes: it is `CreateSolidBox` plus `Sprite.Visible = false`.
- `CreateShortcutGate` passes `res://Scenes/World/ShortcutGate.tscn`, an inherited scene, and reaches
  the gate with `GetNode<ShortcutGate>("ShortcutGate")` instead of `AddComponent`.
- `CreateCheckpointAt` and `CreateGatePortal` instance `Checkpoint.tscn` / `GatePortal.tscn`.
  `CreateCheckpoint(GameplaySceneDefaults)` keeps its exact name and one-parameter signature - the P0
  runner reflects on it.

**New public API**, and the reason it is public rather than folded into `_Ready`:

- `CheckpointZone.BindAuthoredTriggerAndMarker()` and `GateTravelZone.BindAuthoredTriggerAndMarker()`.
  The builder calls these while the instance is still detached. `GameplayTelegraphPulse` caches the
  marker colour it finds when it readies and writes that cached colour back every frame afterwards, so
  the designer's `Readability.json` tint has to land before the marker enters the tree. `Initialize`
  still calls the same two helpers, so a zone built by hand - which is every fixture - is unaffected.

`CheckpointZone.EnsureTrigger` now *binds* `WorldTuning.checkpointZoneRadius` onto the authored shape
rather than only creating one when absent; without that, authoring the trigger would have taken the
radius away from the design file. `CheckpointZone.EnsureMarker` and `GateTravelZone.EnsureMarker` bind
the authored `MarkerDisc.tscn` instance when there is one and instance it when there is not.

`ShortcutGate.ZoneRadius` (5 m) is **deleted**; the reach and the whole argument for its size are
authored in `Scenes/World/ShortcutGate.tscn`. `ShortcutGate.EnsureTrigger` no longer builds the
`Area2D` - a gate with no authored `ShortcutGateTrigger` child now warns loudly, because on this one
component a missing trigger means a chapter nobody can finish.

`CreateSceneryPiece` is deliberately **not** a scene; see `docs/migrations/scene-data/AUDIT_SCENES_UI.md` §7.

### HUD is a scene now

`GameplayHud.CreateUi(CanvasLayer canvas = null)` keeps its signature but **ignores the argument** -
the HUD is instanced from `res://Scenes/UI/GameplayHud.tscn`, which already has its own root, so
there is nothing left to build into. `GameplayHudSpawner` instances the scene, renames the root and
resolves the two authored controllers instead of adding them.

`GameplayHud.Plate` and `Mul` are **deleted** with the Theme; they were `internal` and called from
`TitleMenuBootstrap`, which no longer styles buttons in code at all. `PlaceRect` and `Face` remain
only for that file's remaining call sites.
