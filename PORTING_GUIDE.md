# Unity -> Godot porting guide

How this project was carried over from Unity 6, kept as the record of the conventions the whole
codebase is written to. The source it was ported from was
https://github.com/ddingjin2/MyGame (`Assets/_Project/Scripts/...`); that checkout is not stored
here any more, so paths below of the form `Assets/_Project/...` name files in that repository.

Written while several people ported several folders in parallel, which is why it reads as a
contract - and why it is still the answer to "why is this code shaped like that".

## Ground rules

1. **Port behaviour, not Unity.** Keep class names, public members, method names and signatures
   identical to the Unity source unless this guide says otherwise. Other folders are being ported at
   the same time against those same names - that is the only thing keeping the seams closed.
2. **Namespaces stay.** `MyGame.Combat`, `MyGame.Player`, `MyGame.Enemy`, `MyGame.UI`,
   `MyGame.Gameplay`, `MyGame.Testing`. Godot has no assembly definitions, so the Unity assembly
   dependency rules (`Combat` must not reference `Player`) are now convention. Keep them anyway.
3. **File layout mirrors the source.** `Assets/_Project/Scripts/Combat/Health.cs` becomes
   `Scripts/Combat/Health.cs`.
4. **Keep the comments.** The Unity source carries hard-won explanations (friction, spawn sweep,
   private-reflection tests). Carry them over, and update any that describe Unity-only behaviour.
5. **Do not invent systems.** If a Unity file does something awkwardly, port the awkward thing. This
   is a port, not a redesign.

## Units and axes - read this twice

Unity 2D: metres, **+Y up**. Godot 2D: pixels, **+Y down**. Both conversions are mandatory.

- **Distance / speed / acceleration:** multiply authored Unity numbers by `World.Ppu` (100).
  Use `World.U(6f)`.
- **Where to convert:** in the `*Data` tuning classes, at the point the JSON is loaded. Spatial
  fields (`moveSpeed`, `gravity`, `jumpForce`, `attackRadius`, `detectionRange`, ...) get scaled
  once there; time, damage, poise, counts and multipliers do **not**.
  Document each Data class with which fields it scaled.
- **Literal distances in gameplay code** (a `0.1f` ground probe) get wrapped: `World.U(0.1f)`.
- **Vertical sign flips.** Unity `velocity.y += jumpForce` (up) becomes `Velocity.Y -= jumpForce`
  in Godot. Gravity *adds* to `Velocity.Y`. `Vector2.up` is `Vector2.Up` in Godot and already
  points at `(0, -1)` - so `Vector2.Up * force` is still "upwards", but a raw `+y` literal is not.
- A "grounded" ray points **down** = `Vector2.Down` = `(0, 1)`.

### Camera

Unity used an orthographic camera whose `orthographicSize` is the visible **half-height in world
units**. Godot's `Camera2D` has no such field - it has `Zoom`. Convert once:

```csharp
float halfHeightPx = orthographicSize * World.Ppu;          // 6.8 units -> 680 px
camera.Zoom = Vector2.One * (viewportHeight * 0.5f / halfHeightPx);
```

Camera bounds (`cameraHorizontalBounds`, `cameraVerticalBounds`) scale by `World.Ppu` and the
vertical pair flips sign **and swaps order** - a Unity min/max of `(0.5, 7.5)` becomes a Godot
min/max of `(-750, -50)`. The same is true of `fallDeathY`: Unity `-8` is Godot `+800`, and the
"fell below" test flips from `<` to `>`.

## Type and API mapping

| Unity | Godot |
|---|---|
| `MonoBehaviour` (has a transform) | `partial class X : Node2D` |
| `MonoBehaviour` (pure manager, no transform) | `partial class X : Node` |
| `ScriptableObject` data asset | `partial class X : Resource`, fields `[Export]` |
| `Rigidbody2D` written every FixedUpdate | `CharacterBody2D` + `Velocity` + `MoveAndSlide()` |
| `Collider2D` (solid) | `CollisionShape2D` under the body |
| `Collider2D` with `isTrigger` | `Area2D` + `CollisionShape2D` |
| `OnTriggerEnter2D(Collider2D)` | `Area2D.BodyEntered` / `AreaEntered` signal |
| `Awake` / `Start` | `_Ready()` |
| `Update(Time.deltaTime)` | `_Process(double delta)` |
| `FixedUpdate(Time.fixedDeltaTime)` | `_PhysicsProcess(double delta)` |
| `OnDestroy` | `_ExitTree()` |
| `Instantiate(prefab)` | `packedScene.Instantiate<T>()` then `AddChild` |
| `Destroy(go)` | `node.QueueFree()` |
| `gameObject.SetActive(b)` | `node.Visible = b` and/or `ProcessMode` |
| `transform.position` | `GlobalPosition` |
| `SpriteRenderer` | `Sprite2D` |
| `SpriteRenderer.sortingOrder` | `ZIndex` |
| `Camera.main` | `GetViewport().GetCamera2D()` |
| `UnityEvent` / `UnityEvent<T>` | plain C# `event Action` / `event Action<T>` |
| `Coroutine` / `IEnumerator` | `async` method + `await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout)` |
| `Debug.Log/LogWarning/LogError` | `GD.Print` / `GD.PushWarning` / `GD.PushError` |
| `Mathf.*` | `Godot.Mathf.*` (see exceptions below) |
| `Random.value` / `Random.Range(a,b)` | `GD.Randf()` / `GD.RandRange(a, b)` |
| `Time.deltaTime` etc. | the `delta` argument, else `MyGame.Core.GameClock.*` |
| `Time.timeScale` | `GameClock.TimeScale` (wraps `Engine.TimeScale`) |
| `Physics2D.OverlapCircle(All)` / `Raycast` | `MyGame.Core.Phys2D.*` |
| `LayerMask.GetMask("Enemy")` | `World.Layer.Enemy` (bit constants) |
| `CompareTag("Player")` | `node.IsInGroup(World.Group.Player)` |
| `GameObject.FindGameObjectWithTag("Player")` | `GetTree().GetFirstNodeInGroup(World.Group.Player)` |
| `PlayerPrefs` | `MyGame.Core.PlayerPrefs` (same API) |
| `JsonUtility.FromJson<T>` | `MyGame.Core.JsonData.FromJson<T>` |
| `Resources.Load<T>("Design/X")` | `MyGame.Core.Res.LoadJson<T>("Design/X")` or `Res.Load<T>` |
| `SceneManager.LoadScene` | `GetTree().ChangeSceneToFile("res://Scenes/X.tscn")` |
| `Canvas` / uGUI `Image` / `Text` | `CanvasLayer` / `ColorRect` or `TextureRect` / `Label` |
| `RectTransform` anchors | `Control` anchors + `OffsetLeft/Top/Right/Bottom` |

Mathf exceptions: `Mathf.Approximately(a, b)` -> `Mathf.IsEqualApprox(a, b)`;
`Mathf.MoveTowards` -> `Mathf.MoveToward`; `Mathf.PI` -> `Mathf.Pi`;
`Mathf.PerlinNoise` -> a `FastNoiseLite` field with `NoiseType = TypeValue`.

Godot C# node classes **must** be `partial` and inherit a Godot type; a class the engine never
instantiates (a pure data holder, a resolver, a state machine with no node) should stay a plain C#
class and be *composed* into a node, exactly as it was in Unity.

## Shared code already written - use it, do not re-implement

`Scripts/Core/`:
- `World.cs` - `Ppu`, `U()`, `V()`, `Layer.*` bit constants, `Group.*` names.
- `GameClock.cs` - autoload; `DeltaTime`, `Time`, `UnscaledTime`, `FixedDeltaTime`, `TimeScale`.
- `Phys2D.cs` - `OverlapCircle`, `OverlapCircleAll`, `Raycast`, `IsOnLayer`, `FindActorInGroup`,
  and the `RayHit2D` struct (implicitly converts to `bool`, like Unity's `RaycastHit2D`).
- `UnityCompat.cs` - `PlayerPrefs`, `JsonData`, `Res`.

If you need another shim, put it in `Scripts/Core/` and say so in your report so the other agents
learn about it. Do not add a shim for something Godot already does natively.

## Input

Unity's `InputSystem_Actions.inputactions` plus its legacy-keyboard fallback are collapsed into one
Godot InputMap, already written into `project.godot`. Ported code calls
`Input.IsActionPressed(...)` / `Input.IsActionJustPressed(...)` with these names:

| Action | Keys | Unity origin |
|---|---|---|
| `move_left` / `move_right` | A/D, Left/Right | Move, legacy axis |
| `move_up` / `move_down` | W/S, Up/Down | Move, legacy axis |
| `jump` | Space | Jump |
| `dodge` | Left Shift | Dash / Sprint |
| `attack` | J, left mouse | Attack |
| `heavy_attack` | Alt + left mouse | HeavyAttack (Unity used Alt as a modifier) |
| `parry` | K, right mouse | Parry |
| `sin_wrath` ... `sin_lust` | 1 ... 7 | Wrath/Sloth/Pride/Gluttony/Greed/Envy/Lust |
| `pause` | Escape | Pause / Menu |
| `interact` | E | Interact |
| `heal` | R | Heal |
| `lock_on` | Tab, middle mouse | LockOn |

`GameplayInput` keeps its Unity public surface (`Horizontal`, `JumpPressed`, `PressedSin()`, ...) and
reads these actions underneath. Do not add new action names without adding them to `project.godot`.

## Collision layers

Set in `project.godot`, mirrored in `World.Layer`:

| Bit | Layer | Who sits on it |
|---|---|---|
| 1 | World | static geometry, the old Unity `Default` |
| 2 | Player | the player body |
| 3 | Enemy | enemy bodies |
| 4 | Ground | ground/platform colliders |
| 5 | PlayerHitbox | the player's damage areas |
| 6 | EnemyHitbox | enemy damage areas and projectiles |
| 7 | Trigger | checkpoints, gates, pickups, kill planes |

Unity's `LayerMask.GetMask("Default", "Ground")` is `World.Layer.GroundProbe`.

Actors join Godot groups in `_Ready`: `AddToGroup(World.Group.Player)` / `...Enemy`.

## Scenes and prefabs

> **Superseded, 2026-09-10.** The rule below was the right instruction *for the port* and it is why
> the code looks the way it does - but it is not how this project is built from here on. The
> user-approved Godot production rules (`AGENTS.md`, and the `godot-cli-control` skill) require
> reusable things to be saved scenes and UI to be authored in `.tscn`. The migration that brought
> this codebase into line landed in two phases (`docs/migrations/scene-data/PLAN.md`, then
> `PLAN_CLOSEOUT.md`, complete 2026-09-12): every screen, actor, world piece and the chapter shell
> is a scene now, the spawners instance and configure rather than build, and no runtime node
> creation remains outside the exemptions those documents list. **New work follows the production
> rules, not this section.** What follows is kept because it explains why the spawners and
> builders are shaped the way they are.

The Unity project builds essentially everything from code - its scenes are 314-line shells and only
six prefabs exist. **That stayed true through the port.** The instruction at the time was: do not
author `.tscn` content that the Unity source built at runtime; port the builder
(`GameplayEnvironmentBuilder`, `GameplayVisualFactory`, `GameplayEnemySpawner`,
`GameplayPlayerSpawner`, `GameplayHud`) and let it build nodes in `_Ready`.

That kept the port honest - a faithful port of a code-built world is a code-built world - but it also
made this project *less* asset-driven than its source in one place: Unity's HUD was an authored
`Canvas` with uGUI prefabs, and it arrived here as 1,100 lines of `new Label()`. That is the gap the
migration closes.

At the end of the port only these `.tscn` files existed, each a near-empty shell with a root node and
a script: `Scenes/TitleScene.tscn`, `Scenes/GameplayScene.tscn`, and one per chapter
(`Chapter02_Orange` ... `Chapter08_White`).

## Data and tuning

`Resources/Design/*.json` came over unchanged and stays the source of truth, exactly as
`CLAUDE.md` in the Unity project said. `Resources/Gameplay/*.asset` (Unity ScriptableObject YAML)
is **not** ported - the JSON supersedes it. `Resources/Prefabs/*.prefab` is not ported either; the
actors are `Scenes/Actors/*.tscn` (since scene stage S8) and the spawners instance and configure them.

Art (`Resources/Art`, `Resources/PixelActors`) came over as PNGs and Godot imports them directly.

## What is being dropped, and what replaces it

- **Timeline / `PlayableDirector` / `.playable`** has no Godot equivalent. `CutsceneDirector` is
  reimplemented as a small sequencer over `AnimationPlayer` and `await`; the four `.playable` files
  are read for their intent only, not converted.
- **Unity editor tooling** (`Scripts/Editor/`) becomes `@tool`-style `EditorPlugin` scripts under
  `addons/mygame_tools/` - only the generators that still make sense (sprite baking, design JSON
  seeding). The prefab extractor has nothing to extract to and is dropped.
- **Unity Test Framework** becomes plain C# test nodes under `Tests/`, driven by a headless Godot
  run (`tools/run-tests.ps1`). Reflection-based private-member pokes in the P0 runner get rewritten
  against the ported types.
- **UnityMCP, the Unity lock, `.meta` files** are all gone; nothing replaces them.

## Definition of done for a folder

- Every `.cs` file in the Unity folder has a counterpart under `Scripts/<Folder>/`, or a line in
  your report saying why it was dropped.
- The code compiles on its own terms (you cannot build the whole project until every folder lands -
  the integration pass does that).
- No `using UnityEngine;` survives anywhere.
- Behaviour differences you had to introduce are listed in your report.
