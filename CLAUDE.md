# MyGame (Godot)

Godot 4.7.2 (.NET), 2D soulslike vertical slice. A port of the Unity 6 project at
https://github.com/ddingjin2/MyGame. That checkout is no longer kept here; clone it if you need to
check what a piece of this code was before the port. `PORT_STATUS.md` records every behavioural
difference, so most such questions are answered without it.

**Read `AGENTS.md` before writing code here.** It carries the four user-approved Godot production
rules this project is held to, and an honest statement of where this codebase currently breaks them.
`SESSION_HANDOFF.md` is the checkpoint a new session starts from.

## Layout

```
Scripts/Core/       shims that stand in for Unity APIs - read these before writing anything
Scripts/Combat/     damage, poise, health, sin resonance, saves   (depends on nothing)
Scripts/Player/     motor, actions, stamina, progression, death   -> Combat
Scripts/Enemy/      archetypes, chapter bosses, projectiles       -> Combat
Scripts/UI/         HUD and title menu, both built in code        -> Combat, Player
Scripts/Gameplay/   the arena builders, spawners, zones, cutscenes, camera, input
Scripts/Testing/    scripted agents that play the game for the tests
Tests/Framework/    the test harness (attributes, Assert, runner)
Tests/PlayMode/     ported PlayMode suites
Tests/Unit/         ported edit-mode P0 suite
addons/mygame_tools/ the editor dock: design CSV round trip, sprite baking, scene creation
Resources/Design/   *.json - the tuning source of truth, owned by the designer
Resources/Art/, Resources/PixelActors/  generated placeholder art
localization/       ui.csv - all product text, ko + en, keyed
Scenes/             nine chapter shells, plus the authored scenes migration adds under Scenes/UI etc.
docs/migrations/scene-data/  the production-rule audits and the plan of record
```

Godot has no assembly definitions, so the Unity dependency rules above are convention now. Keep them:
`Combat` must not learn about `Player` - it asks through `IDamageGuard`, `IStaggerable` and the
`SinResonanceController` events.

## Commands

```
tools/build.ps1               # dotnet build, error count only
tools/run-tests.ps1           # headless test run; exit 0 = green
tools/run-tests.ps1 -Filter X # one class or method
tools/godot.ps1               # open the editor (resolves the winget install itself)
tools/godot.ps1 --headless --quit-after 200 res://Scenes/GameplayScene.tscn   # smoke run
```

Nothing takes a lock. The Unity editor mutex, the two serialised suite runners and UnityMCP are all
gone: a headless run owns nothing, so several can run at once.

## The conventions that matter

Full detail in `PORTING_GUIDE.md`; the two that break things silently:

- **1 Unity metre = 100 pixels** (`World.Ppu`). Authored distances are scaled once, at the boundary -
  inside each `*Data.Load()`, and in `GameplaySceneDefaults.Create()` / `GameplayReadabilityDefaults`
  for layout. Downstream code never re-scales. Always load tuning through the type's own `Load()`;
  a raw `Res.LoadJson` skips the conversion and leaves the numbers in metres.
- **+Y is down.** Gravity adds to `Velocity.Y`, a jump subtracts, "below" is a larger number, and a
  vertical min/max pair swaps as well as changing sign.

`Resources/Design/*.json` outranks any number quoted in a doc, exactly as in the Unity project.

**All product text goes through the translation table**, never a literal: `localization/ui.csv` with
ko and en columns, read as `Tr("UI_...")` (or `TranslationServer.Translate` from a static). Never
name a node after translated text - a change of locale renames the node and every lookup by name
breaks with it.

## Traps

- **`_Ready` fires the moment a node enters the tree**, not when a component is added. Build an actor
  detached, wire every child, and parent it last - `GameplayEnemySpawner` and `GameplayPlayerSpawner`
  both do. Adding a child *during* a parent's ready propagation is refused outright; use
  `CallDeferred(Node.MethodName.AddChild, child)` (see `PlayerController2D`).
- **`Visible = false` is not `SetActive(false)`.** An invisible node still ticks and still collides.
  `GameplayBuildShim.SetActive` does all three; `DeathStateController` has the same pattern inline.
- **`PlayerController2D._Ready` re-runs `actions.Initialize` with its own null hitbox**, clobbering one
  wired from outside. This is deliberate and carried over from Unity. The spawner calls
  `SetDamageHitbox` *after* the player is in the tree; do not "fix" the trap.
- **Design JSON is matched case-sensitively**, like Unity's `JsonUtility`. Godot's `Vector2`/`Vector3`/
  `Color` have explicit converters in `Scripts/Core/UnityCompat.cs` because their members are
  uppercase and the JSON's are not. A new struct shape in the design files needs a converter too, or
  it will silently read as zero.
- **A freed Godot object is not null in C#.** Use `GodotObject.IsInstanceValid` where Unity leaned on
  its overloaded `==`.
- **`PlayerPrefs` is a real file** at `user://playerprefs.cfg` and outlives a headless run. Any test
  touching saves clears it in `[SetUp]`.
- **Enemy gravity is applied in code** (`World.U(9.81f)`), because `project.godot` sets
  `default_gravity = 0` - the motors write their own authored gravity.

## What the port dropped

Unity Timeline (cutscenes are coded `async` sequences in `CutsceneDirector`), prefabs and
ScriptableObject `.asset` files (the JSON is the source of truth and the spawners build actors in
code), `.anim`/`.controller`, the Build Settings scene list (`ChapterRoute` enumerates
`res://Scenes/*.tscn`), `.meta` files, and UnityMCP. `PORT_STATUS.md` lists every behavioural
difference and every bug the port introduced and fixed.

## Where this is going

The port deliberately kept Unity's build-the-world-in-code shape, which puts it at odds with the
production rules in `AGENTS.md` - reusable things should be saved scenes and UI should be authored in
`.tscn`. Two audits and an ordered plan live in `docs/migrations/scene-data/`; the plan is being
worked stage by stage on `refactor/godot-scene-data`, each stage landing with the suite green.
`PORTING_GUIDE.md`'s "build everything in code" section is marked superseded and kept only as the
explanation of why existing code looks the way it does.

**New code follows the production rules now.** Do not add another runtime-built screen beside the
existing ones.
