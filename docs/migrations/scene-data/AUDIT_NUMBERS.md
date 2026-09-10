# Rule 1 compliance audit — hardcoded numbers vs. data assets

Read-only audit of `C:\dev\godot-soul` against **rule 1** of the user-approved Godot production rule
set (`C:\Users\pshye\.claude\skills\godot-cli-control\SKILL.md` §필수지침 1, mirrored as project rules
in `C:\dev\project-godot\AGENTS.md`): *numbers that decide behaviour live in a data asset the runtime
reads; moving a value into a `const`, a Dictionary, a default argument or an `@export` default is not
compliance.*

No code was changed. Nothing outside this file was created, edited or deleted.

Branch at audit time: `refactor/godot-scene-data` (HEAD `54ea16f`).

## Headline

| | |
|---|---|
| **Genuine violations** | **~530 distinct tuning values** in 6 meaning-groups, plus 1 structural finding (an orphaned data class) and 2 live defects |
| **Needs a decision** | **19 items** |
| **Verdict, `GameplaySceneDefaults.cs`** | **Compliant** — a real fallback behind a JSON layer the runtime actually consumes |
| **Verdict, `GameplayReadabilityDefaults.cs`** | **Split** — its 33 colours are compliant; its 64 non-colour fields (~118 literals) are a violation |
| **Verdict, `GameplayTuningDefaults.cs`** | **Violation** — a second source of truth that has already drifted from the first on 7 values |

The single largest block is not gameplay at all: **251 presentation literals in `Scripts/UI/`**, where
zero values are data-sourced and no `Theme`, `StyleBox` or `.tres` exists anywhere in the repository.

---

# 1. What is already compliant

Establish this first: the project has a working, well-documented data system. It is not absent, it is
**incomplete**, and everything below should join it rather than replace it.

## 1.1 The design folder and its loaders

`Resources/Design/*.json` (35 files) is the source of truth, byte-identical to the Unity project's.
Every entry is reached through its own type's `Load()`, which is where the unit conversion lives.

| Design file | Loaded by | Scaled at load (metres→px) | Left alone |
|---|---|---|---|
| `PlayerMovement.json` (8 keys) | `Scripts/Player/PlayerMovementData.cs:35` | `moveSpeed`, `acceleration`, `deceleration`, `gravity`, `maxFallSpeed`, `jumpForce` | `coyoteTime`, `jumpBufferTime` |
| `PlayerCombat.json` (25 keys) | `Scripts/Player/PlayerCombatData.cs:68` | `dodgeSpeed`, `attackKnockback` | all durations, windows, multipliers, stamina costs |
| `PlayerResources.json` (19 keys) | `Scripts/Player/PlayerResourceData.cs:61` | nothing (no spatial field) | all |
| `ProgressionTuning.json` (7 keys) | `Scripts/Player/ProgressionTuningData.cs:68` | nothing | all |
| `SinTuning.json` (8 keys + `sins[]`) | `Scripts/Combat/SinTuningData.cs:42` | nothing | all |
| `WorldTuning.json` (3 keys) | `Scripts/Gameplay/WorldTuningData.cs:56` → `ScaleToPixels()` | all three | — |
| `MeleeGrunt.json` (21), `LeapingAttacker.json` (22), `RangedCaster.json` (23), `WrathMiniBoss.json` (41) | `Scripts/Enemy/*Data.cs` → `EnemyTuningData.ScaleToPixels()` | `moveSpeed`, `attackKnockback`, `detectionRange`, `attackRange`, `bodySize` (+ per-archetype extras) | health, damage, every time, the poise block, `soulReward`, `enemyColor` |
| `WrathEncounter.json`, `Chapter0[1-8]_*_Encounter.json` (7+1 keys) | `Scripts/Enemy/BossEncounterData.cs:150` | `arenaLeftOffset`, `arenaRightOffset`, `detectionRange` | intro/recovery/victory times, phase weights |
| `Chapter0[1-8]_*_<Boss>.json` (22 keys + `attacks[]`) | `Scripts/Enemy/RainbowChapterBossData.cs:265` | boss stats + per-attack `knockback`, `range`, `forwardOffset`, `lungeSpeed`, `hazardRadius`, `hazardForwardOffset` | damage, times, weights, colours |
| `SceneLayout.json` + 7× `SceneLayout_Chapter0*.json` (43–45 keys) | `Scripts/Gameplay/GameplaySceneLayoutData.cs:199` → `ApplyTo()` | every position/size, via `GameplaySceneDefaults.ToGodot*` | `cameraOrthographicSize` (deliberately unconverted) |
| `Resources/Art/Readability.json` (33 colour keys) | `Scripts/Gameplay/GameplayReadabilityThemeData.cs:67` → `ApplyTo()` | n/a — a colour has no unit | all 33 |

`Scripts/Gameplay/GameplayTuningCatalog.cs:143` composes these into one cached object and, correctly,
**converts nothing itself** — every entry goes through its own type's `Load()`, so the catalog cannot
double-scale. `Reload()` drops the cache so an edited file is picked up without a restart.

## 1.2 The unit boundary is real and correctly documented

`Scripts/Core/World.cs:15` — `Ppu = 100f`, `U()`, `V()` (which also flips +Y). Every `*Data` class
carries a `UNITS:` doc block naming exactly which of its fields it scaled and which it deliberately
did not. `EnemyTuningData` and `WorldTuningData` both guard against a second pass with a
`_scaledToPixels` flag, because the scaling rewrites fields in place.

**Verified clean:** in `Scripts/Player/` every spatial literal is wrapped in `World.U`/`World.V` —
no raw metre value is used as a pixel distance. The same discipline holds in `Scripts/Enemy/` and
`Scripts/Gameplay/`, with the two exceptions listed in §2.7.

## 1.3 Numbers that are correctly sourced today

- **Player movement, combat, resources, progression** — all 59 authored keys reach the runtime.
  `Scripts/UI/GameplayHud.cs:615-618` reads per-level gains straight from `ProgressionTuningData`;
  its doc comment at `:572-577` explicitly forbids a literal there, and there is none. This is the
  one place in the UI layer that does it right, and it proves the pattern is available.
- **Sin resonance** — fully data-driven. `SinResonanceController.cs:76 ApplyTuning()` overwrites all
  8 economy fields and the whole 22-value modifier table from `SinTuning.json`; the code fallbacks
  match the file exactly.
- **Boss attack grammar** — a chapter boss's attacks are rows in `Chapter0*_<Boss>.json`
  (`attackId`, `damage`, `knockback`, `telegraphTime`, `activeTime`, `recoveryTime`, `range`,
  `damageType`, `phaseTwoWeight`, `forwardOffset`, `lungeSpeed`, `lungeDuration`, `telegraphColor`).
  `RainbowChapterBossBehaviour` runs a profile without knowing what it represents. A new attack is a
  JSON row, not a code change. This is the best-executed area in the project.
- **Arena layout** — all 8 chapters. See the verdict in §5.1.
- **The world palette** — all 33 colours, see §5.2.
- **`Poise` and `Health`** — no `Load()` of their own, but injected from the design data by
  `GameplayPlayerSpawner.cs:95,193` and `GameplayEnemySpawner.cs:327,496,545`.

## 1.4 The extension mechanism already exists

`addons/mygame_tools/DesignDataFiles.cs:43` is a single table mapping a designer-facing file name to
its runtime type; `GameplayTuningJsonValidator.cs` walks it, checks every file parses and names only
fields the Data class declares, and rewrites it pretty-printed. **Adding a tuning group is one line
plus one JSON file.** Every stage in §5 uses this path.

One gap in the validator, relevant below: it catches a *JSON key the class does not have*, but not a
*class field the JSON omits* — which is exactly how the three missing `PlayerCombat.json` keys in
§2.1 went unnoticed.

---

# 2. Violations

Grouped by meaning. "Scale state" says which side of the `World.Ppu` boundary the literal sits on
today — get this wrong when moving it into JSON and the game changes by a factor of 100.

**The rule for every row below:** if the literal is written `World.U(x)` / `World.Ppu * x` /
`World.V(...)`, the authored number `x` is what goes in the JSON, and the conversion moves into the
`Load()`. If the literal is a bare number already treated as pixels, it must be divided by 100 before
it is written to a design file. If it is a time, a multiplier, a count or a colour, it crosses
untouched.

## 2.1 Design-JSON keys that are missing from a file that exists (6) — cheapest to fix

`PlayerCombatData` declares 28 fields; `PlayerCombat.json` has 25 keys. The three orphans are the
shipped values, hardcoded twice (once on the data class, once mirrored on the consuming node).

| file:line | value | unit | controls | scale state | joins |
|---|---|---|---|---|---|
| `Scripts/Player/PlayerCombatData.cs:55` | `0.15f` | seconds | `inputBufferTime` — how long a dodge/attack/heavy/parry/heal press survives | non-spatial | `PlayerCombat.json` |
| `Scripts/Player/PlayerCombatData.cs:58` | `3` | count | `maxComboSteps` — combo chain ceiling | non-spatial | `PlayerCombat.json` |
| `Scripts/Player/PlayerCombatData.cs:59` | `1.15f` | multiplier | `comboStepDamageMultiplier`, applied as `Mathf.Pow(m, comboStep)` | non-spatial | `PlayerCombat.json` |
| `Scripts/Player/PlayerActionController.cs:67` | `0.15f` | seconds | mirror of the above | non-spatial | same |
| `Scripts/Player/PlayerActionController.cs:70` | `3` | count | mirror | non-spatial | same |
| `Scripts/Player/PlayerActionController.cs:71` | `1.15f` | multiplier | mirror | non-spatial | same |

## 2.2 Combat feel and feedback — no design file exists at all (41 live + 30 orphaned)

This is the systems most likely to need iteration on feel, and it has zero data representation.

### 2.2a The orphan — `Scripts/Combat/CombatTuningData.cs` (structural finding)

A 30-field designer-owned tuning class with a `Load()` at line 74 pointing at `Design/CombatTuning`.
Verified: **`Resources/Design/CombatTuning.json` does not exist**, **nothing in the repository calls
`CombatTuningData.Load()`** (the only other mention is a doc-comment cross-reference in
`CameraShake.cs:21`), and it is **not registered in `addons/mygame_tools/DesignDataFiles.cs`**.

It therefore reads as tuning coverage that does not exist, and several of its values **contradict**
the literals actually running:

| CombatTuningData | live value elsewhere |
|---|---|
| `hitStopDurationLight = 0.04f` (line 25) | `CombatResultBroadcaster.cs:17` `0.08f` |
| `shakeDurationMedium = 0.2f` (line 34) | `CameraShake.cs:80` `0.12f` |
| `attackCost/dodgeCost/parryCost/maxStamina/staminaRegenRate/staminaRegenDelay` (53-58) | already authored in `PlayerResources.json` — this is a **third** copy |

**Do not seed `CombatTuning.json` from this class.** The live literals are what shipped; the orphan's
values were never exercised. See stage S4.

### 2.2b Hit-stop (4) — no JSON field exists

| file:line | value | unit | controls | scale |
|---|---|---|---|---|
| `Scripts/Combat/CombatResultBroadcaster.cs:17` | `0.08f` | seconds | light-hit freeze | non-spatial |
| `Scripts/Combat/CombatResultBroadcaster.cs:18` | `0.12f` | seconds | `DamageType.Heavy` freeze | non-spatial |
| `Scripts/Combat/CombatResultBroadcaster.cs:19` | `0.08f` | seconds | parry freeze | non-spatial |
| `Scripts/Combat/HitStopManager.cs:12` | `0.05f` | multiplier | `_pauseScale` — how *hard* the freeze bites | non-spatial |

Plus `Scripts/Gameplay/GameplayCutsceneTriggers.cs:109` `0.12f` (player-death freeze) and
`Scripts/Enemy/WrathMiniBoss.cs:246` `0.15f` / `:472` `0.1f` (boss phase + intro impact).

### 2.2c Camera shake (13) — no JSON field exists

All intensities are **raw authored metres**; the single conversion is `CameraShake.cs:118`
`World.U(_currentIntensity * decay)`. Durations are seconds.

`Scripts/Combat/CameraShake.cs:69` `0.2f` (default duration) · `:77` `0.15f, 0.12f` (LightHit) ·
`:80` `0.25f, 0.12f` (HeavyHit) · `:83` `0.05f, 0.08f` (Invulnerable) · `:86` `0.5f, 0.5f`
(BossPhase) · `:89` `0.4f, 0.25f` (BossSlam) · `:120` and `:121` `GameClock.Time * 25f` (noise
sampling rate = shake frequency, unitless).

### 2.2d Flash, squash, audio (6) — no JSON field exists

`Scripts/Combat/CombatFeedback.cs:13` `0.08f` s hit-flash · `:14` `Colors.White` flash tint · `:15`
`new Color(0.5f, 0.5f, 1f, 1f)` invuln tint · `:18` `0.1f` s squash duration · `:19` `1.3f` squash
peak (multiplier) · `Scripts/Combat/AudioFeedback.cs:34` `0.75f` linear gain fallback.

### 2.2e Enemy group steering (4) — no JSON field exists

`Scripts/Combat/EnemyGroupCombat.cs:15` `World.Ppu * 2f` preferred spacing (**pre-scaled**, 2 m) ·
`:16` `World.Ppu * 2f` spacing force (**pre-scaled**) · `:17` `World.Ppu * 3f` neighbour query radius
(**pre-scaled**) · `:18` `0.5f` alignment force (multiplier, non-spatial).

### 2.2f Humanity — the whole system is uncovered (5)

`PlayerResources.json` carries `startingHumanity` and nothing else about humanity.

| file:line | value | unit | controls | joins |
|---|---|---|---|---|
| `Scripts/Combat/HumanityController.cs:8` | `100f` | points | `maxHumanity` — no key exists | `PlayerResources.json` |
| `Scripts/Combat/HumanityController.cs:10` | `30f` | points | `lowHumanityThreshold` — fires the HUD hollow warning | `PlayerResources.json` |
| `Scripts/Combat/HumanityController.cs:11` | `5f` | points | humanity burned per hit taken | `PlayerResources.json` |
| `Scripts/Combat/HumanityController.cs:12` | `0.5f` | points/sec | regen rate | `PlayerResources.json` |
| `Scripts/Combat/HumanityController.cs:13` | `5f` | seconds | quiet time before regen starts | `PlayerResources.json` |

### 2.2g The sin-resonance half-formula (1)

`Scripts/Combat/SinResonanceController.cs:136` — `AddResonance(resonancePerDamage * (damage / 10f))`.
`resonancePerDamage` is authored in `SinTuning.json`; the `10f` divisor (HP per resonance tick) is
not, so half the formula is untunable. Unit: HP. Non-spatial. Joins `SinTuning.json` as
`resonanceDamageDivisor`.

## 2.3 Death, spirit walk and pickup (7) — no JSON field exists

| file:line | value | unit | controls | scale | joins |
|---|---|---|---|---|---|
| `Scripts/Player/DeathStateController.cs:14` | `3f` | seconds | spirit-walk length before respawn | non-spatial | `PlayerResources.json` |
| `Scripts/Player/DeathStateController.cs:15` | `new Color(0.486…, 0.510…, 0.565…, 0.55f)` | colour | spirit-form sprite tint | non-spatial | `Readability.json` |
| `Scripts/Player/DeathStateController.cs:17` | `0.5f` | fraction of max | health restored on respawn | non-spatial | `PlayerResources.json` |
| `Scripts/Player/DeathStateController.cs:18` | `0.5f` | fraction of max | humanity restored on respawn | non-spatial | `PlayerResources.json` |
| `Scripts/Player/DeathStateController.cs:120` | `0.25f` | fraction of max | health on *entering* spirit state — a bare literal, not even an `[Export]` | non-spatial | `PlayerResources.json` |
| `Scripts/Player/DeathStateController.cs:154` | `World.V(new Vector2(-3f, 0.5f))` | metres | fallback respawn position, no checkpoint | **pre-scaled** | `SceneLayout*.json` |
| `Scripts/Gameplay/SoulPickup.cs:30` | `0.6f` | metres | soul-stain pickup reach (scaled at `:72`) | **raw metres** | `PlayerResources.json` (which has `soulStainPickupDelay` but no radius) |

## 2.4 Enemy AI behaviour — 105 values with no design field

The archetype *stats* are authored; the *behaviour that spends them* is not.

### 2.4a Gravity (2)

`Scripts/Enemy/EnemyStateMachine.cs:60` and `Scripts/Enemy/GameplayEnemy2D.cs:50` — both
`World.U(9.81f)` (**pre-scaled**, px/s²). `project.godot` sets `default_gravity = 0`, so this is the
only gravity enemies have. The player's is authored (`PlayerMovement.json.gravity = 20`), so
enemies and the player fall at different rates and only one of the two is tunable. See §3.4.

### 2.4b Shared state-machine timing and the disengage leash (4) — no field on `EnemyTuningData`

`EnemyStateMachine.cs:36` `3f` s Idle→Patrol dwell · `:37` `2f` s Investigate duration · `:38` `1f` s
Recovery duration · `:39` `World.U(8f)` (**pre-scaled**) the distance past which Combat drops to
Recovery.

### 2.4c Ledge and ground probes (6)

`EnemyStateMachine.cs:66` `new Vector2(World.U(0.3f), World.U(0.5f))` fallback half-extents ·
`:74` `World.U(0.35f)` forward probe start · `:75` `World.U(1.1f)` probe depth · `:370` `World.U(0.05f)`
ray-origin lift · `LeapingAttacker.cs:45` (duplicate half-extents) · `LeapingAttacker.cs:407`
`World.U(0.08f)` landing-probe skin. All **pre-scaled**. See §3.5 — the last two are borderline.

### 2.4d The perfect-parry reward, hardcoded five times (5)

`MeleeGrunt.cs:369`, `LeapingAttacker.cs:491`, `WrathMiniBoss.cs:990`,
`RainbowChapterBossBehaviour.cs:282`, `GameplayEnemy2D.cs:260` — all `(perfect ? 1.6f : 1f)`.

This one **already has a home**: `PlayerCombat.json.perfectParryStunMultiplier = 1.6`. It is simply
not routed to the enemy side. Fix by consumption, not by a new field.

### 2.4e Telegraph readability — pulse rate, amplitude, blend, base colour (19)

Rates: `MeleeGrunt.cs:132` `?? 8f` (*covered* by `MeleeGrunt.json.telegraphPulseSpeed`),
`LeapingAttacker.cs:358` `8f`, `RangedCaster.cs:343` `6f`, `WrathMiniBoss.cs:703` `8f`,
`RainbowChapterBossBehaviour.cs:1187` `8f`, `GameplayEnemy2D.cs:201` `8f`.
Amplitudes (**no archetype has a field**): `MeleeGrunt.cs:135` `0.15f`, `LeapingAttacker.cs:360`
`0.15f`, `RangedCaster.cs:345` `0.1f`, `WrathMiniBoss.cs:705` `0.2f`,
`RainbowChapterBossBehaviour.cs:1187` `0.2f`, `GameplayEnemy2D.cs:202` `0.15f`.
Colour blend factors: `MeleeGrunt.cs:293` `0.7f`, `LeapingAttacker.cs:339` `0.7f`,
`RangedCaster.cs:325` `0.8f`, `WrathMiniBoss.cs:659` `0.7f`.
Frozen base telegraph colours: `MeleeGrunt.cs:39`, `LeapingAttacker.cs:43`, `RangedCaster.cs:44`,
`WrathMiniBoss.cs:114`. All non-spatial. Joins the archetype JSON (rate/amplitude/blend) and
`Readability.json` (colours).

`MeleeGrunt.json` already has `telegraphPulseSpeed: 8.0` — the concept is JSON-owned for one
archetype and code-owned for the other five.

### 2.4f State-feedback and identity colours (15) — joins `Readability.json`

Stun / recovery / defeated / punish-window / phase-two tints at `MeleeGrunt.cs:387,395`,
`LeapingAttacker.cs:510,514,522`, `RangedCaster.cs:502,510,116`, `WrathMiniBoss.cs:117,276,328,1012,1026`;
`BossAfterimage.cs:142` `0.45f` afterimage alpha (chapter six's entire mechanic);
`RainbowChapterColor.cs:30-37` the eight fallback chapter colours. `WrathMiniBoss.cs:1022`
`GameClock.Time * 3f` rage flicker rate.

### 2.4g Patrol geometry for the leaper and caster (8)

`LeapingAttackerData` and `RangedCasterData` have **no `patrolDistance` or `patrolIdleTime` at all**,
while `MeleeGruntData` does.

`LeapingAttacker.cs:64` `World.U(3f)` `PatrolHalfWidth` — **also the hard leash in
`ClampHomewardDirection`, so it bounds combat movement too** · `:222` `World.U(0.3f)` arrival
threshold · `:232` `0.5f` s idle at each turn · `RangedCaster.cs:64` `World.U(3f)` · `:261`
`World.U(0.3f)` · `:271` `0.5f` · `MeleeGrunt.cs:238` `World.U(0.2f)` arrival threshold ·
`GameplayEnemy2D.cs:153` `World.U(0.2f)`. All spatial ones **pre-scaled**.

### 2.4h Leaper and caster combat behaviour (8)

`LeapingAttacker.cs:246`/`:250` `± World.U(1f)` — the deadband around `maintainDistance` inside which
the leaper stands still; **this is the spacing of the whole fight** (**pre-scaled**).
`LeapingAttacker.cs:545` `damage *= 2f` — the landing-vulnerability punish payoff, the archetype's
core trade (multiplier, non-spatial; **not** a "doubling" math exclusion).
`RangedCaster.cs:61` `1.5f` s first reposition cooldown · `:398` `GD.Randf() < 0.01f` per-frame
strafe-flip chance (frame-rate dependent) · `:423` `GD.RandRange(1.2f, 2f)` s reposition cooldown ·
`:446` `0.5f` s reposition duration.

### 2.4i Values that ignore an authored field sitting right next to them (4)

| file:line | value | the field it ignores |
|---|---|---|
| `Scripts/Enemy/RangedCaster.cs:377` | `World.U(3f)` projectile knockback | `RangedCaster.json.attackKnockback = 3.0` — speed and damage on the two lines above *are* read from tuning |
| `Scripts/Enemy/WrathMiniBoss.cs:766` | `World.U(6f)` slash knockback | `WrathMiniBoss.json.attackKnockback = 5.0` — unconditional literal, tuning never consulted |
| `Scripts/Enemy/WrathMiniBoss.cs:903` | `World.U(5f)` rush knockback | same |
| `Scripts/Enemy/RainbowChapterBossBehaviour.cs:164` | `DefaultStunDuration = 1f` | **`RainbowChapterBossData` has no `stunDuration` field at all** — verified. Every authored chapter boss is stunned for exactly 1 s regardless of its JSON |

### 2.4j Projectile flight (5)

`Scripts/Enemy/EnemyProjectile.cs:22` `5f` s lifetime (how far a shot actually reaches) · `:23`
`World.U(0.5f)` arc height (**pre-scaled**) · `:43` `World.U(0.3f)` hit radius when the spawner gives
no Area2D (**pre-scaled**) · `:120` `0.2f` arc frequency · `:129` `5f` arc lift gain.
Joins `RangedCaster.json`.

### 2.4k Attack-origin offsets and the rush that has no range field (4)

`MeleeGrunt.cs:317`, `GameplayEnemy2D.cs:223`, `WrathMiniBoss.cs:756` — all
`Vector2.Right * _facingDir * World.U(0.5f)`, where the swing's overlap circle is centred when no
`attackPoint` node exists (Wrath has none at all, so `:756` always applies). **Pre-scaled.**
`BossAttackProfile.forwardOffset` covers exactly this for chapter bosses; `MeleeGruntData` and
`WrathMiniBossData` have no equivalent.
`WrathMiniBoss.cs:891` `World.U(1f)` rush hit radius — slash and slam have `slashRange`/`slamRange`;
the rush has nothing.
`WrathMiniBoss.cs:860` `rushDuration * 0.5f` — the charge does not move until half a rush-duration
after the telegraph ends. A design timing fraction, not a halving.

### 2.4l Wrath intro and phase-transition beats (6)

`WrathMiniBoss.cs:246` `0.15f` hit-stop · `:261` `0.1f` red-flash hold · `:269` `0.05f` white-flash
hold · `:466` `0.5f` intro blackout · `:472` `0.1f` intro impact hit-stop · `:481` `0.8f` intro
settle. Seconds, non-spatial. `BossEncounterData` covers `introHoldTimeout` and
`victoryPresentationDelay` but none of the intro's internal beats.

### 2.4m Chain cap (1)

`RainbowChapterBossBehaviour.cs:124` `MaxChainSteps = 4` — how many links a chain may run before the
player is let back in. Count, non-spatial. See §3.9.

### 2.4n The legacy `GameplayEnemy2D` tuning block (11) — no design file exists for this class

`Scripts/Enemy/GameplayEnemy2D.cs` lines 23, 24, 25, 28, 29, 32, 33, 34, 35, 36, 38, 41, 107 —
`moveSpeed World.U(2f)`, `patrolDistance World.U(3f)`, `patrolIdleTime 1f`, `detectionRange World.U(5f)`,
`attackRange World.U(1.5f)`, `telegraphTime 0.6f`, `attackTime 0.3f`, `attackCooldown 1.5f`,
`attackDamage 15f`, `attackKnockback World.U(5f)`, `attackRadius World.U(0.8f)`, `stunDuration 0.8f`,
and `detectionRange * 0.7f` disengage hysteresis. Every one is `[Export]`-only; there is no
`GameplayEnemy2D.json` and no `*Data` class. See §3.10.

## 2.5 Gameplay layer — 55 values with no design field

### 2.5a The cutscene layer is entirely uncovered (21)

There is no cutscene design file. All four shots are inline keyframe tables in
`Scripts/Gameplay/CutsceneDirector.cs:105-119`:

| line | shot | values |
|---|---|---|
| 105 | GameplayEnter fade | `(0,1)(1.2,0)(1.8,0)` — seconds + alpha, non-spatial |
| 106 | GameplayEnter letterbox | `(0,64)(1.2,64)(1.6,0)(1.8,0)` — seconds + **UI pixels, deliberately not `World.U`** |
| 109 | BossIntro letterbox | `(0,0)(0.25,96)(2.3,96)(2.6,0)` |
| 110 | BossIntro camera push | `(0,6.8)(0.5,5.984)(1.3,5.984)(2.3,6.8)(2.6,6.8)` — **raw authored metres** (ortho half-height), converted at `:324` |
| 113 | PlayerDeath fade | `(0,0)(0.45,0.55)(0.8,0.55)(1.2,0)` |
| 114 | PlayerDeath letterbox | `(0,0)(0.45,48)(0.8,48)(1.2,0)` |
| 115 | PlayerDeath camera push | `(0,6.8)(0.45,6.8)(0.8,6.392)(1.2,6.8)` — raw metres |
| 118 | Victory letterbox | `(0,0)(0.3,96)(1.7,96)(2,0)` |
| 119 | Victory camera push | `(0,6.8)(0.3,6.8)(1.3,6.12)(1.7,6.12)(2,6.8)` — raw metres |

**Cross-file coupling with no compile-time link:** the `6.8f` rest value duplicates
`SceneLayout.json.cameraOrthographicSize = 6.8` but is not read from it — a chapter that overrides
that field will have its cutscenes snap. The `64f` bar height at `:106` must stay in step with
`GameplayBootstrap.cs:24 EnterLetterboxHeight = 64f`, in a different file.

Trigger beats: `GameplayCutsceneTriggers.cs:26` `0.5f` s BossIntro rig ease · `:29` `0.45f` s death
move delay · `:30` `0.35f` s death move duration · `:33` `World.U(0.6f)` death camera creep
(**pre-scaled**) · `:109` `0.12f` s hit-stop.
Bootstrap: `GameplayBootstrap.cs:24` `64f` UI px · `:28` `1f` **raw metres** settle height (scaled at
`:192`) · `:29` `1.2f` s settle duration.
Overlay: `CutsceneOverlay.cs:26` `Ink950` and `:27` `Bone100` colours (not among Readability.json's
33 keys) · `:132` `24` caption font size · `:141-144` `-450/450/-170/-90` caption box offsets.

### 2.5b Camera follow feel is code-owned while its bounds are JSON-owned (4)

`Scripts/Gameplay/GameplayCameraFollow2D.cs:22` `new(World.Ppu * 1.2f, World.Ppu * 0.6f)` dead-zone
half-extents (**pre-scaled**) · `:23` `World.V(new Vector2(1.4f, 1.2f))` look-ahead (**pre-scaled and
Y-flipped**) · `:24` `0.25f` s `_smoothTime` — the single biggest knob on how the camera feels ·
`:25` `World.Ppu * 12f` follow-speed cap (**pre-scaled**).

None is ever overwritten — `Initialize` writes only the four bound fields. A designer can move the
walls (`SceneLayout.json.cameraHorizontalBounds` / `.cameraVerticalBounds`) but not the feel.

### 2.5c Interaction reach — level-design numbers in disguise (6)

| file:line | value | unit | controls | scale | joins |
|---|---|---|---|---|---|
| `Scripts/Gameplay/GateTravelZone.cs:31` | `1.4f` | metres | portal trigger reach (scaled at `:304`, `:318`) | **raw metres** | `WorldTuning.json` |
| `Scripts/Gameplay/ShortcutGate.cs:55` | `5f` | metres | shortcut-door Interact reach (scaled at `:293`) | **raw metres** | `WorldTuning.json` |
| `Scripts/Gameplay/ShortcutGate.cs:58` | `0.25f` | alpha | how visible an opened door stays | non-spatial | `Readability.json` |
| `Scripts/Gameplay/GameplayLockOnMarker.cs:19` | `0.45f` | metres | lock-on disc size (scaled at `:101`) | **raw metres** | `Readability.json` (layout half) |
| `Scripts/Gameplay/GameplayLockOnMarker.cs:20` | `1.3f` | metres | hover height (scaled at `:71`, `:81` via `World.V`) | **raw metres** | same |
| `Scripts/Gameplay/SoulPickup.cs:33` | `0.55f` | metres | stain disc size (scaled at `:52`) | **raw metres** | same |

`WorldTuning.json` already authors `checkpointZoneRadius` — the bonfire's reach. The portal's and the
door's are the exact same kind of value, sitting three files away, in code. `ShortcutGate.cs:52-55`'s
own doc-comment argues its value from chapter-two's geometry (3.47 / 2.79 units), i.e. a level-design
number reasoned about in a code comment.

### 2.5d Ambient animation feel (6)

`Scripts/Gameplay/GameplayTelegraphPulse.cs:13` `4f` rad/s pulse rate — the breathing of every bonfire
disc, portal, soul stain and danger readout · `:14` `0.12f` scale swing · `:15` `0.16f` alpha floor ·
`:16` `0.34f` alpha ceiling. `Scripts/Gameplay/ActorIdleBob.cs:31` `1.2f` s breathing period · `:32`
`0.04f` depth (a fraction of rest height, explicitly not a distance). All non-spatial.

Note `MeleeGrunt.json` authors `telegraphPulseSpeed` — the same concept, JSON-owned there and
code-owned here.

### 2.5e Animation thresholds (2)

`Scripts/Gameplay/ActorAnimationDriver.cs:25` `World.U(0.15f)` `MoveEpsilon` (**pre-scaled**) — the
horizontal speed at which *every* actor, player and enemy, switches between the idle and run clip.
Visible tuning despite the name.
`Scripts/Gameplay/SpriteFrameAnimator.cs:31` `0.10f` `HeightTolerance` — the 90–110% band before the
sprite/collider mismatch warning fires. Diagnostic; low priority.

### 2.5f World health bar (5)

`Scripts/Gameplay/GameplayWorldHealthBar.cs:29` frame colour (never overwritten) · `:32`
`LowHealthTint` (never overwritten) · `:35` `0.08f` **raw metres** frame margin (scaled at `:141`) ·
`:165` `normalized <= 0.3f` low-health threshold **and** `Lerp(..., 0.35f)` tint travel.
`Readability.json` authors the three *fill* colours but not the frame, the tint or the threshold.

### 2.5g Arena geometry authored in code rather than in SceneLayout (5)

`Scripts/Gameplay/GameplayEnvironmentBuilder.cs:172` `0.5f` **raw metres** world-edge wall thickness ·
`:173` `40f` **raw metres** wall height (its comment reasons about chapter eight's 9-unit climbs) —
the file's own doc admits these are *"the only two spatial literals in this file that are not on the
defaults."* · `:203` `platform.Size.Y * 0.58f` platform-rim placement (operates on already-pixel
values) · `:204` `World.U(0.05f)` platform-rim thickness (**pre-scaled**; `SceneLayout.json` has
`groundRimSize` but nothing for platform rims) · `:225` `GatePortalOffsetX = 3f` **raw metres**.

### 2.5h Spawner rig offsets (2)

`Scripts/Gameplay/GameplayEnemySpawner.cs:128` `new Vector2(World.U(0.6f), 0f)` — the MeleeGrunt's
`AttackPoint` offset, i.e. where its swing originates. **Pre-scaled.** `MeleeGrunt.json` carries
`attackRange`, `attackRadius` and `bodySize` but no attack-point offset, so the grunt's reach is
split across two owners.
`GameplayEnemySpawner.cs:439` `new Vector2(World.U(0.42f), World.U(0.42f))` caster projectile sprite
size. **Pre-scaled.** No field in `RangedCaster.json`.

### 2.5i Fall-death lockout (1)

`Scripts/Gameplay/GameplayFallDeath.cs:18` `respawnLockout = 1f` s — how long after a pit death the
kill plane refuses to fire again. Nothing calls a setter; `Initialize` writes only `deathY`.

## 2.6 Readability layout — 64 fields, no asset (§5.2 for the verdict)

`Scripts/Gameplay/GameplayReadabilityDefaults.cs:227-296`. `Readability.json` covers the 33 colours
and nothing else, by explicit design decision (see the doc comment at `:161-166`). The remaining
**64 fields** — approximately **118 numeric literals** — have no asset anywhere:

| block | fields | example |
|---|---|---|
| Collider and visual sizes | 10 | `:227` `PlayerColliderSize = S(0.58f, 1.28f)` |
| Hitbox anchor, radius, offset, sword rig | 6 | `:239` `PlayerHitboxRadius = World.U(0.4f)`, `:243` `SwordLocalRotation = Mathf.DegToRad(28f)` |
| Health-bar sizes and offsets | 8 | `:246` `PlayerHealthBarSize = S(1.2f, 0.12f)` |
| Danger-readout positions and sizes | 12 | `:266` `BossSlamDangerSize = S(4.2f, 1.45f)` |
| Role-marker positions | 4 | `:271` `BossRoleLocalPosition = P(0f, -1.35f)` |
| World-label / role-marker character and font sizes | 4 | `:272-275` `World.U(0.22f)`, `42`, `World.U(0.16f)`, `34` |
| Sorting orders | 20 | `:277-296` `-100` … `30` — see §3.6 |

All spatial values are **pre-scaled** at the field via `S()`, `P()` or `World.U()`; the authored
metre number is the one visible in the source and the one that goes in JSON. `WorldLabelFontSize`
and `RoleMarkerFontSize` are point sizes and cross untouched.

## 2.7 Two live defects the audit surfaced

These are not merely rule violations — they are wrong today.

**D1 — `Scripts/Combat/DamageHitbox2D.cs:21`, `knockbackForce = 3f`.**
The two lines above it are pre-scaled (`:18` `radius = World.Ppu * 0.5f`, `:19`
`offset = new Vector2(World.Ppu, 0f)`); this one is not. There is **no setter and nothing writes it**
— verified: the only other `knockbackForce` in the repository is the parameter name on
`Health.ApplyDamage`. Meanwhile `PlayerCombat.json.attackKnockback = 4.0` is loaded, scaled to 400 px
by `PlayerCombatData.Load`, exposed as `PlayerActionController.AttackKnockback` — and never routed
into the hitbox. Every hit this hitbox builds therefore carries **3 px** of knockback, which is
effectively zero, while the authored value is 400.

**D2 — `Scripts/UI/GameplayHud.cs:928`, `$"Resonance: {…} / 100"`.**
The max-resonance denominator is a literal while the real value is
`SinResonanceController.maxResonance`, overwritten from `SinTuning.json` at
`SinResonanceController.cs:81`. The two agree today (both 100), so this is latent, not broken. Every
sibling readout does it correctly — `:869` `_health.MaxHealth`, `:922` `_humanity.MaxHumanity`,
`:974` `_stamina.MaxStamina`, `:998` `_poise.MaxPoise`. Change `maxResonance` in the design file and
the HUD silently lies.

## 2.8 Fallback tables that have drifted from the source of truth

Every one of these is a `[Export]` initialiser or `const` that a `Load()`/`Configure()` overwrites on
a real spawn — so the shipped game is fine. They matter because **a synthetic actor built without its
tuning file runs a different game**, and the whole test suite builds synthetic actors.

**`GameplayTuningDefaults` vs. the archetype JSON** (verified against the shipped files):

| constant / factory field | code | `Resources/Design/*.json` |
|---|---|---|
| `CreateMeleeGrunt.maxPoise` (`:79`) | `25f` | `MeleeGrunt.json` **35.0** |
| `CreateMeleeGrunt.soulReward` (`:80`) | `20` | `MeleeGrunt.json` **7** |
| `CreateLeapingAttacker.maxPoise` (`:107`) | `20f` | `LeapingAttacker.json` **30.0** |
| `CreateLeapingAttacker.soulReward` (`:108`) | `25` | `LeapingAttacker.json` **8** |
| `CreateRangedCaster.maxPoise` (`:137`) | `15f` | `RangedCaster.json` **25.0** |
| `CreateRangedCaster.soulReward` (`:138`) | `25` | `RangedCaster.json` **8** |
| `SoulStainPickupDelay` (`:31`) | `0.2f` | `PlayerResources.json` **0.35** |

`CreateWrathMiniBoss` matches its file exactly, and the three `World.Ppu`-scaled reach constants
match `WorldTuning.json`.

**Player data classes and their mirrors:**

| file:line | code | authored |
|---|---|---|
| `Scripts/Player/PlayerMovementData.cs:26` / `PlayerMotor2D.cs:22` | `World.U(25f)` | `PlayerMovement.json.deceleration` **50.0** |
| `Scripts/Player/PlayerResourceData.cs:51` | `0.2f` | `PlayerResources.json.soulStainPickupDelay` **0.35** |
| `Scripts/Player/ProgressionTuningData.cs:30` | `20` | `ProgressionTuning.json.maxLevelPerStat` **12** |
| `Scripts/Player/ProgressionTuningData.cs:36` | `8f` | `ProgressionTuning.json.endurancePerLevel` **12.0** |
| `Scripts/Player/ProgressionTuningData.cs:42` | `6f` | `ProgressionTuning.json.resolvePerLevel` **8.0** |
| `Scripts/Combat/Poise.cs:16` | `50f` | `PlayerResources.json.maxPoise` **60.0** |
| `Scripts/Combat/Poise.cs:20` | `25f` | `PlayerResources.json.poiseRegenRate` **30.0** |

**Enemy archetype inline fallbacks:** ~72 `tuningData != null ? … : <literal>` sites across
`MeleeGrunt.cs`, `LeapingAttacker.cs`, `RangedCaster.cs` and `WrathMiniBoss.cs`, of which roughly
**50 no longer match** the shipped JSON. Representative: `RangedCaster.cs:33` `World.U(1.5f)`
attack-range fallback vs. authored **5.0**; `WrathMiniBoss.cs:606` `0.5f` slash telegraph vs.
authored **0.85**; `LeapingAttacker.cs:451` `10f` damage vs. authored **15.0**.

**Dead fallbacks that also disagree:**
`Scripts/Gameplay/GameplayFallDeath.cs:15` `World.Ppu * 6f` (600) vs. `SceneLayout.json.fallDeathY`
(−8 → **800**). `Scripts/Gameplay/GameplayCameraFollow2D.cs:26-29` bound fallbacks written
**un-flipped**, contrary to the convention the doc-comment describes.

## 2.9 UI presentation — 251 literals, 0 data-sourced

`Scripts/UI/GameplayHud.cs` (208 numeric literals, 96 distinct values) and
`Scripts/UI/TitleMenuBootstrap.cs` (83 literals, 53 distinct). Verified:
`grep -rn "GameplayTuningCatalog\|GameplayReadabilityDefaults\|Res.LoadJson\|Readability" Scripts/UI/`
returns **no matches**; neither file even has `using MyGame.Gameplay;`.

**There is no `Theme`, `StyleBox` or settings `Resource` anywhere in the repository** — a repo-wide
search for `*.tres` / `*.theme` returns zero files, `project.godot` sets no `default_theme`, and the
ten `.tscn` files contain no `theme`, `stylebox` or `font` token. `Scenes/TitleScene.tscn` is six
lines: a bare `Node` with a script. Every StyleBox is built at runtime by `GameplayHud.Plate()`
(`:304`) and applied per-button via `AddThemeStyleboxOverride` (`:288-292`,
`TitleMenuBootstrap.cs:398-402`) — five `StyleBoxFlat` allocations per button, no sharing.

| category | GameplayHud | TitleMenuBootstrap | total |
|---|---|---|---|
| (a) layout / size / spacing | 86 | 34 | **120** |
| (b) colour | 66 | 29 | **95** |
| (c) font size | 21 | 9 | **30** |
| (d) animation / feedback timing | 5 | 0 | **5** |
| (e) gameplay threshold in a UI file | 1 (D2) | 0 | **1** |
| (f) technical, excluded | 30 | 11 | (41) |

Highest-risk detail, by meaning:

- **Duplicated palette.** `Bone100/200/300` are typed twice (`GameplayHud.cs:22-24` and
  `TitleMenuBootstrap.cs:18-20`). Four of the six HUD tokens are **byte-identical** to keys already in
  `Resources/Art/Readability.json` under world-facing names: `Cold200` = `checkpointLabelColor` /
  `playerHealthBarColor`; `Ember300` = `wrathAltarLabelColor` / `bossHealthBarColor`; `Bone300` =
  `groundRimColor` / `duelFloorLabelColor` / all four `*RoleColor`. They were re-typed, not read.
- **Duplicated button-state maths.** The four multipliers `0.5686275f` / `0.32156864f` /
  `0.36078432f, 0.6f` / `0.6f` appear identically at `GameplayHud.cs:288,291,292,301` and
  `TitleMenuBootstrap.cs:398,401,402,411`; the plate colour at `GameplayHud.cs:287` and
  `TitleMenuBootstrap.cs:25`. A contrast change means editing both files identically.
- **Implicit spacing.** The 12 HUD readout rows (`GameplayHud.cs:159-173`) step by `-30` px, which
  exists only as the difference between twelve hand-typed Y values. The backing plate at `:218`
  (`280×350` at `(10, -30)`) must be re-derived by hand whenever a row moves — its own comment at
  `:217` records that the previous value was wrong.
- **Literals fighting literals.** `GameplayHud.cs:501-507` builds a button at `280×56` / 24 pt and
  overwrites it to `520×56` / 20 pt one line later.
- **Five hand-picked panel alphas** for what the code itself calls "the same shape" (`:234`):
  `0.94f`, `0.88f`, `0.92f`, `0.92f`, `0.96f`.
- **Timings (5).** `GameplayHud.cs:56` `GhostHoldSeconds = 0.4f`, `:57` `GhostDrainSeconds = 0.5f`,
  `:58` `HitFlashSeconds = 0.15f` (all scaled time), and `:683` / `:1091` `UnscaledTime + 2f` — the
  same 2-second warning dwell typed twice.
- **Font size is chosen by an `int` literal at every call site.** The font resource
  (`Resources/UI/NotoSerifKR-Regular.otf`, loaded at `GameplayHud.cs:114-118`) carries no size; there
  is no base size and no type ramp. 30 font-size literals across the two files, plus one more at
  `Scripts/Gameplay/CutsceneOverlay.cs:132`.
- **Modal stacking is positional.** No `z_index` literal exists; draw order is the order four lines
  appear in `CreateUi` (`:176-181`), documented in a comment and tested by nothing.

Note this block is simultaneously a **rule 3** finding (UI must be authored in `.tscn`, never built by
`Control.new()` in `_Ready`). The two are fixed by the same work; see stage S11.

---

# 3. Needs a decision

Nineteen items where the honest answer is "this could go either way". Each carries the reasoning so a
later reader can accept or overturn it. **None should be moved without a decision recorded here.**

## Outcomes (S13, 2026-09-10)

Every item below is settled. The reasoning that follows each heading is kept as written; this table
is what was actually done and where.

| # | Outcome | Where |
|---|---|---|
| 3.1 | **Keep** `GameplaySceneDefaults.Create()` as the fallback. Unreachable in a shipped run, diagnosable when a layout file fails to parse. | - |
| 3.2 | **Accepted as the deserialiser's missing-value behaviour, not reconciled.** The validator reports a missing key as an error, so every shipped file is complete and the defaults are never read; and there is no single right default for a field eight chapter files each author differently. | - |
| 3.3 | **Kept the type, fixed the numbers.** `GameplaySceneDefaultsAsset` stays as the per-scene override hook (two tests pin it); its stale camera defaults (8, 2) / 7.0 became the shipped (0, 2) / 6.8, so a freshly created asset changes nothing. No `.tres` ships. | S13 |
| 3.4 | **Tuning.** `WorldTuning.json.enemyGravity`. | S6 |
| 3.5 | **Split as recommended.** Forward reach and depth moved; the 0.05 / 0.08 insets stayed. | S6 |
| 3.6 | **Kept in code.** The 20 sorting orders. The sizes beside them moved as one block. | S10 |
| 3.7 | **Theme resource.** `Resources/UI/MenuTheme.tres`. | scene S2 |
| 3.8 | **Deleted, not moved.** `SwordLocalRotation` had no reader once `Player.tscn` authored the sword; a key nothing reads would look like tuning. | S10 |
| 3.9 | **Tuning, per chapter.** `<chapter>_Encounter.json.maxChainSteps`. | S7 |
| 3.10 | **Retired (option b, without the repointing).** `GameplayEnemy2D` had no scene, no test and no spawner building it - only an optional lookup in `MyGameStateProbe`, which is gone with it. 292 lines and 11 unowned numbers deleted. | S13 |
| 3.11 | **Tuning.** `CombatTuning.json.shakeFrequency`. | S4 |
| 3.12 | **Moved as-is, per frame.** `RangedCaster.json.strafeFlipChance`; the frame-rate dependency stays a separate bug. | S7 |
| 3.13 | **Keep in code.** Diagnostic. | - |
| 3.14 | **Exempt.** Off-by-default X-ray. | - |
| 3.15 | **Exempt.** Baked-asset authoring data. | - |
| 3.16 | **Compliant.** Fallbacks behind `Resources/PixelActors/*/anim.json`. | - |
| 3.17 | **Keep in code.** Hardware enum and an inert settings list. | - |
| 3.18 | **Tuning.** `LeapingAttacker.json.landingPunishMultiplier`. | S7 |
| 3.19 | **Tuning.** `CombatTuning.json.hitStopPauseScale`. | S4 |

**3.1 — `GameplaySceneDefaults.Create()`'s 100 literals: keep as fallback, or delete?**
They are unreachable in every shipped run (§5.1), so they are not a behaviour risk. But they are a
third copy of numbers that already exist twice (the JSON, and the `[Export]` defaults on
`GameplaySceneLayoutData`). *Recommendation:* keep. A parse failure with no fallback is a black
screen; a fallback arena is a diagnosable one. *Alternative:* delete `Create()`'s body and make a
missing layout file a hard error. Not free — see 3.2.

**3.2 — The `[Export]` defaults on `GameplaySceneLayoutData.cs:78-177` are a second fallback layer.**
The rule names `@export` defaults as explicitly *not* compliance. They are however the mechanism that
makes a partially-written design file work (JSON omits a key → the class default stands → `ApplyTo`
converts it). *Recommendation:* accept them as the deserialiser's missing-value behaviour, not as
tuning — but they must be **reconciled with the JSON**, because today several disagree (e.g.
`cameraPosition` default `(0,2,-10)` matches, `shortcutGatePosition` default `(12,1.35,0)` is a value
no shipped file names).

**3.3 — `Scripts/Gameplay/GameplaySceneDefaultsAsset.cs` is dead weight carrying stale numbers.**
`Resources/Gameplay/` does not exist, so `Res.Load<GameplaySceneDefaultsAsset>("Gameplay/SceneDefaults")`
(`GameplayCutsceneTriggers.cs:175`) always returns null. Its four `[Export]` defaults (`:29,32,38,41`)
disagree with the shipped layout (`orthographicSize 7f` vs 6.8, `cameraPosition (8,2)` vs `(0,2)`).
*Recommendation:* delete the file, or create the `.tres` — but not leave it as a null path with wrong
numbers in it. Ported deliberately from Unity; deleting is a port decision, not an audit one.

**3.4 — Enemy gravity `9.81` (`EnemyStateMachine.cs:60`, `GameplayEnemy2D.cs:50`).**
Argument for *not tuning:* it is earth gravity and it is what Unity's `Physics2D.gravity` default
gave these bodies; carrying it as a constant preserves the port. Argument for *tuning:* the player's
gravity **is** authored (`PlayerMovement.json.gravity = 20`, more than double), so the two halves of
the same fall are owned by different people, and a designer who wants floatier enemies cannot have
them. *Leaning:* tuning — add `enemyGravity` to `WorldTuning.json` at the authored 9.81. Flagged
rather than asserted because "match Unity's physics default" is a legitimate reason to freeze it.

**3.5 — Ledge-probe geometry (`EnemyStateMachine.cs:74,75,370`, `LeapingAttacker.cs:407`).**
`World.U(0.35f)` forward, `World.U(1.1f)` down, `World.U(0.05f)` origin lift, `World.U(0.08f)` landing
skin. The forward/down pair is genuinely tunable (it decides whether an enemy walks off a ledge). The
`0.05f` lift and `0.08f` skin are collision-geometry insets that exist to keep a ray from starting
inside its own collider. *Recommendation:* move the first two, keep the last two as technical.

**3.6 — The 20 sorting orders in `GameplayReadabilityDefaults.cs:277-296`.**
Argument for *technical:* they are a z-order contract; the values only mean anything against each
other and a wrong one is a bug, not a taste. Argument for *tuning:* the class's own doc calls them
"readability engineering — what overlaps what", which is a design judgement, and the `Vector2` sizes
beside them are being moved. *Leaning:* keep in code, move the sizes. If they move, they must move as
one block or not at all — a partial move guarantees a layering bug.

**3.7 — Should the UI palette join `Resources/Art/Readability.json`, or get its own file?**
Four of the six HUD tokens are already in that file under world-facing names (§2.9). But
`GameplayTuningCatalog.cs:30-33` records a deliberate ownership split: `Resources/Art` belongs to the
artist, `Resources/Design` to the designer. Folding UI colour into the artist's file is defensible;
so is a `Resources/Art/UiTheme.tres`. *Recommendation:* a Godot `Theme` resource, since rule 1's own
wording puts UI colour in "Theme/StyleBox" rather than JSON — and that also satisfies rule 3.

**3.8 — `Mathf.DegToRad(28f)` sword rotation (`GameplayReadabilityDefaults.cs:243`).**
It is art placement, sitting in a block otherwise all `Vector2`s. Moving it means a design file needs
a degrees-vs-radians unit note (the port already flipped its sign for +Y-down). Small, but it is the
kind of field that silently reads as zero if the converter is missed (`CLAUDE.md` "Traps").

**3.9 — `MaxChainSteps = 4` (`RainbowChapterBossBehaviour.cs:124`).**
A count that decides how long the player is locked out of the fight — clearly tuning by the rule's
own list. But it is also a safety cap on a loop. *Leaning:* tuning, into `BossEncounterData` (which
is per-chapter, which is the right granularity).

**3.10 — `Scripts/Enemy/GameplayEnemy2D.cs` (11 values, §2.4n).**
Its own doc calls it legacy, but it is still built by the earliest scenes and tests. Giving a legacy
class a new design file is arguably worse than leaving it. *Options:* (a) create `GameplayEnemy2D.json`,
(b) retire the class and repoint its callers at `MeleeGrunt`, (c) leave it and note the exemption
here. *Recommendation:* (c) now, (b) later — but decide, do not drift.

**3.11 — `CameraShake.cs:120-121`, `GameClock.Time * 25f`.**
Shake frequency. It is a `FastNoiseLite` sampling rate, which reads technical, but it is also
straightforwardly "how fast does the screen rattle". *Leaning:* tuning, into the same file as the
intensity/duration pairs it belongs with.

**3.12 — `RangedCaster.cs:398`, `GD.Randf() < 0.01f`.**
A per-frame strafe-flip probability, so it is frame-rate dependent — the "correct" fix is a per-second
rate, which changes behaviour. *Recommendation:* move the number as-is into JSON first (no behaviour
change), and treat the frame-rate dependency as a separate bug with its own decision.

**3.13 — `SpriteFrameAnimator.cs:31`, `HeightTolerance = 0.10f`.**
Purely diagnostic — it decides when a warning prints, never what the game does. *Recommendation:*
keep in code. Named here so a future audit does not re-litigate it.

**3.14 — `DebugVisualization.cs` (~25 spatial literals).**
An off-by-default designer X-ray (`Visible = false`, `:48`). No gameplay effect.
*Recommendation:* exempt entirely. Named so the exemption is on the record.

**3.15 — `GameplayVisualFactory.cs` (~40 literals).**
Procedural placeholder-art generation: texture dimensions, shape pixel coordinates, luminance ramps,
sprite pivots. Superseded whenever a real PNG exists in `Resources/Art/`. This is baked-asset
authoring data, not runtime tuning. *Recommendation:* exempt. If the bake ever becomes designer-facing,
it belongs in the editor tool's own config, not in `Resources/Design`.

**3.16 — `PixelActorAnim.cs:32,33` (`DefaultPixelsPerUnit = 32f`, `DefaultFps = 8f`).**
These *do* have an authored home — `Resources/PixelActors/<key>/anim.json` (`ppu`, `states.<s>.fps`).
So they are already fallbacks behind a real asset, just not one under `Resources/Design`.
*Recommendation:* compliant. Note that `PORT_STATUS.md` records a live content mismatch here (the
player's frames read at 136% of its collider), which is a data problem, not a rule-1 problem.

**3.17 — `GraphicsOptions.cs:160-161`, `MsaaSteps = {1,2,4,8}` and `RenderScaleSteps = {0.6f,0.8f,1f}`.**
MSAA values are hardware enum values — technical. Render-scale steps are a UI choice list, and
`PORT_STATUS.md` records that render scale is **inert** in Godot 2D. *Recommendation:* keep both in
code; the render-scale list is a settings-menu contract, and moving an inert value into a design file
advertises a knob that does nothing.

**3.18 — `LeapingAttacker.cs:545`, `damage *= 2f`.**
Listed as a violation in §2.4h, but it sits exactly on the "is `2f` a doubling or a multiplier?" line.
The reason it is called a violation: the surrounding block is the landing-vulnerability window, whose
*duration* is authored (`landingVulnerabilityTime`), so the window's length is tunable and its payoff
is not. *Overturnable* if the payoff is considered a fixed rule of the archetype.

**3.19 — `HitStopManager.cs:12`, `_pauseScale = 0.05f`.**
Not a duration but a time-scale — arguably a technical constant of "what freeze means". But 0.05 vs
0.0 vs 0.2 is a visibly different game. *Leaning:* tuning, alongside the durations.

---

# 4. Not tuning — the keep-in-code list

Named explicitly so a future audit does not re-litigate them.

**Unit conversion.** `World.Ppu = 100f` (`Scripts/Core/World.cs:15`) and every `World.U` / `World.V` /
`World.ToUnits` call site. `GameplayReadabilityDefaults.cs:135,138` `* 0.1f` — Unity's
`TextMesh` `characterSize / 10` mesh-scale formula. `EnemyProjectile.cs:120`'s `/ World.Ppu` divisor.
`WorldTuningData.cs:23,26,29` — the documented in-code twins of `WorldTuning.json`, scaled once.

**Save-format contract.** `Scripts/Combat/GameSave.cs:12` `version = 1`; the `-1f` / `-1` "unset"
sentinels at `:13,14,21,58` (0 is a legal value, so the sentinel cannot be 0); `:89`
`Key = "MyGame.Save"`; `:36` `furthestChapter` as an index into `ChapterRoute.Scenes()`; `:72-75`
level *counts* rather than stat values (deliberate — it keeps the cost curve in `ProgressionTuning.json`
and lets a re-tune apply retroactively). `Scripts/Combat/DifficultySettings.cs:12-14`
`Easy = 0, Normal = 1, Hard = 2` — explicit values because `save.difficulty` persists them.
`GraphicsOptions.cs:163` `Prefix = "MyGame.Graphics."`; `:298,303` bool-as-int encoding.
`Scripts/Testing/Package/Replay/ReplaySerializer.cs` format constants.
Per `AGENTS.md`: past fixtures and saves are **not** regenerated to fit the new rule.

**Technical identifiers and engine contracts.** `Scripts/Core/World.cs:31-40` — all seven collision
layer bits and `GroundProbe`. `CameraShake.cs:52` `ProcessPriority = 1000` and
`ActorAnimationDriver.cs:170` `ProcessPriority = 100` (ports of Unity's `[DefaultExecutionOrder]` /
LateUpdate). `CutsceneOverlay.cs:46` `Layer = 100`; `GameplayWorldHealthBar.cs:119,124` `40`/`41`;
`SoulPickup.cs:50` `ZIndex = 40`; `DebugVisualization.cs:50` `ZIndex = 4096`.
`GameplayWorldHealthBar.cs:193` `Image.CreateEmpty(8, 8, …)` — the shared white pixel.
`ChapterRoute.cs` `".tscn"` / `".remap"` suffix arithmetic and `"res://Scenes/"`.
`GameplayDebugSceneJump.cs:37` `SlotCount = 12` — the function-key count, `#if DEBUG` only.

**Mathematical constants and identity.** Every `* 0.5f` that halves a size into a half-extent
(`GameplayEnvironmentBuilder.cs:97-98,181-182,319`, `GameplayEnemySpawner.cs:411`,
`GameplayPlayerSpawner.cs:226`, `WrathMiniBoss.cs:213`, `RainbowChapterBossBehaviour.cs:386`);
`RainbowChapterBossBehaviour.cs:799` `HazardRadius * 2f` (radius→diameter);
`ActorIdleBob.cs:88,103` `Mathf.Pi * 2f`; `GameplayCameraFollow2D.cs:90` `0.48f` / `0.235f` —
Unity's `SmoothDamp` Padé approximation coefficients, which are not knobs;
`PlayerMotor2D.cs:215` `hit.GetRemainder().X * 0.5f` — the equal-mass split of blocked motion the
class comment derives (see `PORT_STATUS.md` "the one defect only a playthrough could find").
Every `= 0f`, `> 0f`, `<= 0f`, `* 1f`, `Vector2.One`, `Colors.White` reset, `Mathf.Max(0, …)` clamp
floor, `Mathf.Max(1f, …)` divide-by-zero guard, and `_lastKnownHealth = -1f` sentinel.

**Epsilons.** `Poise.cs:49` and `HumanityController.cs:37` `0.001f`; `AudioFeedback.cs:77` `0.0001f`
(log-domain guard); `EnemyGroupCombat.cs:62` and `PlayerLockOn.cs:187` `World.U(0.01f)`;
`GameplayCameraFollow2D.cs:87` `Mathf.Max(0.0001f, smoothTime)`.
Exception: `RainbowChapterBossBehaviour.cs:1144` `World.U(0.01f)` is load-bearing per its own remark
at `:1118-1127`, and is listed in §2.4 rather than here.

**Indices, signs and collection arithmetic.** Every loop bound and `Length - 1`; `_facingDir *= -1`
and `Mathf.Sign(...)` at every site; `RainbowChapterBossBehaviour.cs:960,993,994` `(i / 2) + 1` and
`i % 2`; `WrathMiniBoss.cs:579` `% 3` — the three `AttackType` enum cases, where changing the number
requires a new enum member, not a tuning edit; `RangedCaster.cs:427` `GD.Randf() < 0.5f` — a fair
coin whose result is overridden by the facing test on the next line.

**Validation floors standing in for Unity `[Min]` attributes.** `BossEncounterData.cs:118-128`,
`RainbowChapterBossData.cs:223-230`, `BossAttackProfile.cs:170-201`, `BossHazardStrip.cs:228-231`,
`BossAfterimage.cs:145`, `ProgressionTuningData.cs:56-60`, `GraphicsOptions.cs:230`,
`DifficultySettings.cs:88` (a trust-boundary clamp on user-editable `PlayerPrefs`).
Footnote: `BossAttackProfile.cs:198` `Mathf.Max(0.01f, hazardRadius)` is compared against an
already-pixel-scaled value, so the floor is effectively inert (0.01 px). Harmless, worth knowing.

**Display strings and formatting.** `DifficultySettings.cs:105-107` Korean difficulty names;
`TitleMenuBootstrap.cs:312` `steps[i] <= 1 ? "끔"`; `:320` `* 100f` for a percent label.

---

# 5. The verdicts on the three `*Defaults.cs` tables

## 5.1 `GameplaySceneDefaults.cs` — **COMPLIANT**

100 numeric literals in `Create()`. It is a legitimate shipped-defaults layer behind a JSON override
that the runtime **actually consumes**, and here is the consumption path:

1. `GameplayBootstrap.cs:101` calls `GameplaySceneDefaults.CreateForScene(activeSceneName, null)`.
2. `CreateForScene` (`:289`) builds `Create()`, then applies
   `GameplayTuningCatalog.Load()?.SceneLayoutFor(sceneName)?.ApplyTo(defaults)`.
3. `SceneLayoutFor` (`GameplayTuningCatalog.cs:102`) resolves `SceneLayout_<SceneName>.json`, falling
   back to the shared `SceneLayout.json`.
4. `ApplyTo` (`GameplaySceneLayoutData.cs:211`) writes each section behind its own flag.

**Verified:** all seven override flags (`overrideCamera`, `overrideSpawn`, `overrideTerrain`,
`overrideMarkers`, `overrideEnemyPlacement`, `overridePlatforms`, `overrideScenery`) are `true` in
`SceneLayout.json` **and** in all seven `SceneLayout_Chapter0*.json`. Every field of the defaults
object is therefore overwritten in every shipped run, and `ShortcutGateSize` / `ShortcutOpensFromRight`
— the only two properties with an initialiser outside `Create()` — are written whenever
`hasShortcutGate` is true, which is the only case they are read.

**No literal in this file reaches the shipped game.** It is the fallback the rule permits, correctly
built and correctly documented (`:9-20` names the conversion boundary precisely, and it is the *only*
place a position flips). Caveats live in §3.1 and §3.2, not here.

## 5.2 `GameplayReadabilityDefaults.cs` — **SPLIT: colours compliant, layout a violation**

213 numeric literals.

**Compliant half (95 literals, 33 fields).** `Create()` (`:168`) applies
`GameplayTuningCatalog.Load()?.ReadabilityTheme?.ApplyTo(defaults)`;
`GameplayReadabilityThemeData.ApplyTo` (`:76`) writes all 33 colours from
`Resources/Art/Readability.json`, which exists and has exactly those 33 keys. There is a round-trip
`CopyFrom` and a test (`GameplayReadabilityThemeTests`) proving a mismapped field cannot survive. This
is a model implementation.

**Violating half (~118 literals, 64 fields).** The sizes, offsets, radii, sword rotation, font sizes
and sorting orders (`:227-296`) have **no asset of any kind**. The class comment at `:161-166` states
the exclusion deliberately: *"Sizes, offsets and sorting orders are deliberately not in that file:
they are readability engineering … and moving them would hand out a knob that silently breaks the
reads the whole slice is built to prove."*

That is a real argument, and it is why the sorting orders are in §3.6 as needs-a-decision rather than
here. But it does not survive contact with rule 1 for the rest: the hitbox radius, the collider and
visual sizes, every health-bar and danger-readout geometry, and the world-label font sizes are exactly
"sizes, spacing and fonts", which the rule names. The consequence is concrete — `MeleeGrunt.json`
authors `bodySize`, and this file authors `MeleeColliderSize`, and they are two numbers for one thing
owned by two people.

**Verdict:** violation for the 44 non-sorting fields; the 20 sorting orders are a decision (§3.6).

## 5.3 `GameplayTuningDefaults.cs` — **VIOLATION**

87 numeric literals: 4 `const`s and 4 `Create*` factories that reproduce, in C#, the entire contents
of `MeleeGrunt.json`, `LeapingAttacker.json`, `RangedCaster.json` and `WrathMiniBoss.json`.

The file's own doc defends it: *"Every number here is what a caller gets when the design JSON it
wanted is not on disk — which is every synthetic actor the test runners build."* That would be a
legitimate fallback, exactly like §5.1 — **except that it has already drifted.** Verified against the
shipped files: 7 values disagree (§2.8), including every archetype's `maxPoise` and `soulReward`.

A grunt built from `MeleeGrunt.json` has 35 poise and pays 7 souls. A grunt built by
`GameplayTuningDefaults.CreateMeleeGrunt` has 25 poise and pays 20. **The ~208-test suite builds the
second one**, so a large part of the automated verification is exercising an enemy that does not
exist in the game, and a `soulReward` regression test can pass at nearly three times the shipped
payout. This is the failure mode the rule's clause about hiding missing data behind a hardcoded
fallback exists to prevent, and it has already happened.

The three `World.Ppu`-scaled reach constants (`CheckpointZoneRadius`, `LockOnRange`,
`LockOnBreakRange`) still match `WorldTuning.json` — they are the fallback working. But
`SoulStainPickupDelay = 0.2f` has drifted from `PlayerResources.json`'s `0.35`.

**Verdict:** violation. Not because a fallback is forbidden, but because this one is a second source
of truth that has demonstrably diverged from the first, and the divergence is what the test suite
runs. The fix is not to delete the file — it is to make the fixtures load the real JSON, and reduce
this to a genuinely last-resort layer that is asserted to match (stage S3).

---

# 6. Migration plan

Ordered by blast radius, smallest first. **Every stage lands with `tools/run-tests.ps1` green** —
which today means *206 passed, 1 failed, 1 skipped*, the failure being
`GameplayLayoutIntegrityTests.EveryChapterLayout_KeepsItsPlacementsAndBonfires`, which
`PORT_STATUS.md` documents as red on the same data before the port. **Do not "fix" it by rewriting a
design file.** Any stage that changes that baseline says so before it lands.

## Ground rules for the whole migration

1. **Extend, never rewrite.** No stage changes an existing key's *value* in
   `Resources/Design/*.json`. Every stage adds keys. The files stay byte-identical to the Unity
   project's for every key that already exists.
2. **The live literal wins.** Where a code value and an orphaned/dead default disagree, the value
   that the shipped game actually runs is the one that goes in the JSON. Behaviour must not change.
3. **One `.json`, no `.tres` twin.** JSON is the established source of truth; nothing gets duplicated
   into a `Resource` file. The one exception is the UI, where rule 1 itself names Theme/StyleBox
   (stage S11).
4. **Register every new file** in `addons/mygame_tools/DesignDataFiles.cs` so the validator covers it.
5. **State the unit and the side of the boundary** in the field's doc comment, and put the conversion
   in the type's `Load()` / `ScaleToPixels()` — never at the use site.
6. **Every stage is one commit**, verifiable in isolation.

## S0 — Close the validator hole (no behaviour change)

`addons/mygame_tools/GameplayTuningJsonValidator.cs` catches a JSON key the Data class lacks, but not
a Data-class field the JSON omits. Add the reverse check as a warning. This is what would have caught
S1 on its own.
*New fields:* none. *Suite:* unchanged.

## S1 — Fill the three missing `PlayerCombat.json` keys (no behaviour change)

`inputBufferTime: 0.15` (seconds, range 0.05–0.5, missing → 0.15),
`maxComboSteps: 3` (count, ≥1, missing → 3),
`comboStepDamageMultiplier: 1.15` (multiplier, 1.0–2.0, missing → 1.15).
Values identical to the code, so nothing moves. Delete the mirrored literals at
`PlayerActionController.cs:67,70,71` in favour of the tuning path already wired there.
*Suite:* unchanged.

## S2 — The two live defects

**D1:** route `PlayerActionController.AttackKnockback` into `DamageHitbox2D` (a `SetKnockbackForce`
beside the existing `SetDamageHitbox` path, called from the same place). This **changes behaviour** —
player knockback goes from 3 px to 400 px, i.e. from nothing to the authored value. Land it alone,
with a play check, and record it in `PORT_STATUS.md`.
**D2:** `GameplayHud.cs:928` reads `_sinResonance.MaxResonance` instead of the literal `100`. No
behaviour change today.
*New fields:* none — both are consumption fixes for values already authored.

## S3 — Stop the fallback tables lying (test-facing)

Reconcile `GameplayTuningDefaults` and the `*Data` `[Export]` initialisers against the shipped JSON
(§2.8: 7 + 5 + 2 values, plus the ~50 enemy inline fallbacks). Preferred shape: have the test
fixtures build actors through the real `*Data.Load()` path, so there is one source of truth, and keep
`GameplayTuningDefaults` only where a test genuinely needs a file-less actor — with an added test
asserting each constant equals its JSON counterpart.
**This changes test behaviour** (poise, soul rewards, deceleration, progression curve). Expect
assertions to move; that is the point. Land alone.

## S4 — `CombatTuning.json` (new file)

Create it from the **live** literals, not from the orphaned `CombatTuningData` defaults (§2.2a), then
wire the consumers: `CombatResultBroadcaster`, `HitStopManager`, `CameraShake`, `CombatFeedback`,
`AudioFeedback`, `EnemyGroupCombat`. Register in `DesignDataFiles.cs`. Drop the six stamina fields
from `CombatTuningData` — they are already `PlayerResources.json`'s and must not become a third copy.

*New fields (unit · range · missing → ):*
`hitStopLight: 0.08` · s · 0–0.5 → 0.08 ·
`hitStopHeavy: 0.12` ·
`hitStopParry: 0.08` ·
`hitStopDeath: 0.12` ·
`hitStopBossPhase: 0.15` ·
`hitStopPauseScale: 0.05` · multiplier · 0–1 (§3.19) ·
`shakeIntensityLight/Medium/Heavy/Invulnerable/BossPhase: 0.15 / 0.25 / 0.4 / 0.05 / 0.5` ·
**Unity metres — scaled in `Load()`**, because `CameraShake.cs:118` currently applies `World.U` at
write time; move the conversion to the boundary and delete it there ·
`shakeDurationLight/Medium/Heavy/Invulnerable/BossPhase: 0.12 / 0.12 / 0.25 / 0.08 / 0.5` · s ·
`shakeDefaultDuration: 0.2` · s ·
`shakeFrequency: 25` · unitless (§3.11) ·
`hitFlashDuration: 0.08` · s ·
`hitFlashColor` / `invulnFlashColor` · colour ·
`impactScaleDuration: 0.1` · s · `impactScaleAmount: 1.3` · multiplier ·
`audioFallbackVolume: 0.75` · linear gain · 0–1 ·
`groupPreferredSpacing: 2` / `groupSpacingForce: 2` / `groupSpacingRadius: 3` · **metres, scaled in
`Load()`** (they are `World.Ppu * x` today) · `groupAlignmentForce: 0.5` · multiplier.

## S5 — Extend `PlayerResources.json` (humanity, death, pickup)

*New fields:* `maxHumanity: 100` · points ·
`lowHumanityThreshold: 30` · points · 0–maxHumanity ·
`humanityLossOnHit: 5` · points ·
`humanityRegenRate: 0.5` · points/s ·
`humanityRegenDelay: 5` · s ·
`spiritStateDuration: 3` · s ·
`spiritEntryHealthPercent: 0.25` · fraction · 0–1 ·
`respawnHealthPercent: 0.5` · fraction ·
`respawnHumanityPercent: 0.5` · fraction ·
`soulStainPickupRadius: 0.6` · **metres, scaled in `Load()`** (raw metres today, scaled at
`SoulPickup.cs:72`).
`PlayerResourceData.Load()` currently scales nothing; it gains its first `ScaleToPixels` for the last
field — document it in the class's `UNITS:` block.
Missing-value behaviour: each keeps its current literal, which becomes the `[Export]` default.

## S6 — Extend `WorldTuning.json` (shared reach and shared AI timing)

`WorldTuning.json` is already the home for "reach numbers that belong to no single actor", and its
`ScaleToPixels()` is the right boundary.

*New fields:* `gateTravelZoneRadius: 1.4` · metres · **scaled** ·
`shortcutGateZoneRadius: 5` · metres · **scaled** ·
`enemyGravity: 9.81` · m/s² · **scaled** (pending §3.4) ·
`enemyDisengageDistance: 8` · metres · **scaled** ·
`enemyIdleToPatrolTime: 3` · s ·
`enemyInvestigateDuration: 2` · s ·
`enemyRecoveryDuration: 1` · s ·
`enemyLedgeProbeForward: 0.35` · metres · **scaled** ·
`enemyLedgeProbeDepth: 1.1` · metres · **scaled** ·
`actorMoveAnimThreshold: 0.15` · m/s · **scaled** ·
`fallDeathRespawnLockout: 1` · s.
Also route `PlayerCombat.json.perfectParryStunMultiplier` to the five enemy sites (§2.4d) — a
consumption fix, no new field. Do **not** move the `0.05f`/`0.08f` probe insets (§3.5).

## S7 — Extend the four archetype JSONs and the chapter-boss JSONs

Additive only; every shipped key keeps its value.

*Per archetype (`MeleeGrunt`, `LeapingAttacker`, `RangedCaster`, `WrathMiniBoss`):*
`telegraphPulseSpeed` (already on MeleeGrunt; add to the other three at their live values 8/6/8) ·
rad/s · `telegraphPulseAmplitude: 0.15 / 0.15 / 0.1 / 0.2` · multiplier ·
`telegraphBlend: 0.7 / 0.7 / 0.8 / 0.7` · multiplier ·
`telegraphColor` · colour ·
`attackPointOffset: 0.5` (grunt, Wrath) · metres · **scaled** ·
`patrolDistance` / `patrolIdleTime` for `LeapingAttackerData` and `RangedCasterData` (3 / 0.5) ·
metres **scaled** and s.
*Leaper:* `maintainDistanceDeadband: 1` · metres · **scaled** ·
`landingPunishMultiplier: 2` · multiplier (pending §3.18).
*Caster:* `repositionCooldownMin/Max: 1.2 / 2` · s · `repositionDuration: 0.5` · s ·
`strafeFlipChance: 0.01` · per-frame probability (§3.12) ·
`projectileLifetime: 5` · s · `projectileArcHeight: 0.5` · metres **scaled** ·
`projectileRadius: 0.3` · metres **scaled** · `projectileSize: 0.42` · metres **scaled**.
*Wrath:* consume the existing `attackKnockback` at `:766` and `:903` instead of the literals
(§2.4i) — a consumption fix; `rushRange: 1` · metres · **scaled**;
`rushWindupFraction: 0.5` · multiplier; the six intro/phase beats (§2.4l) · s.
*`RainbowChapterBossData` (all 8 chapter files):* `stunDuration: 1` · s — the field does not exist
today, so this is the one place a boss gains a knob it never had. `BossEncounterData`:
`maxChainSteps: 4` · count (pending §3.9).

## S8 — `CutsceneTuning.json` (new file)

Folds §2.5a entirely: four named shots, each a list of `(time, value)` rows for fade, letterbox and
camera, plus the trigger beats and the bootstrap's entry settle. Register in `DesignDataFiles.cs`.

*Units, and the reason this stage needs care:* fade values are **alpha 0–1**, letterbox heights are
**UI pixels and must NOT be scaled** (`CutsceneOverlay` works in screen space), camera values are
**Unity metres and must be scaled at the boundary** — `CutsceneDirector.cs:324` applies `* World.Ppu`
today and that multiply moves into `Load()`. Getting these three apart is the whole risk of the stage.
Fixing the two cross-file couplings (`6.8` ← `SceneLayout.cameraOrthographicSize`, `64` shared with
`GameplayBootstrap.cs:24`) is the payoff.

## S9 — Camera feel

*New fields, into `WorldTuning.json` or a `CameraTuning.json` — one or the other, not both:*
`cameraDeadZone: {x: 1.2, y: 0.6}` · metres · **scaled, no flip** (a half-extent) ·
`cameraLookAhead: {x: 1.4, y: 1.2}` · metres · **scaled AND Y-flipped** — this is the one field in
the whole migration most likely to be got wrong, because `World.V` is what the code uses today ·
`cameraSmoothTime: 0.25` · s ·
`cameraMaxFollowSpeed: 12` · m/s · **scaled**.
Also delete the stale un-flipped bound fallbacks at `GameplayCameraFollow2D.cs:26-29` (§2.8).

## S10 — Readability layout

Extend the readability asset with the 44 non-sorting fields (§5.2). **This stage regenerates
`Resources/Art/Readability.json`** via `addons/mygame_tools/ReadabilityThemeWriter.cs` — flagged
loudly per the ground rules. It is acceptable only because the regeneration must be a **pure
superset**: verify with a diff that the 33 existing colour keys are byte-identical afterwards and the
only change is additions. If the writer cannot guarantee that, add the fields to a sibling file
instead and leave `Readability.json` untouched.

*Ownership question to settle first:* colours belong to the artist (`Resources/Art`), sizes to the
designer (`Resources/Design`) — `GameplayTuningCatalog.cs:30-33` records that split deliberately. A
sibling `Resources/Design/ReadabilityLayout.json` may be the more honest home for the layout half.

*New fields:* the 10 collider/visual sizes, 6 hitbox/sword fields, 8 health-bar geometries, 12
danger-readout geometries, 4 role-marker positions, 4 character/font sizes. Sizes are **metres,
scaled by `World.U`, no flip**; local positions and offsets are **metres, scaled AND flipped by
`World.V`** — a health bar 1.24 m above the actor is 124 px *below* its origin. Font point sizes and
sorting orders cross untouched. The 20 sorting orders stay in code pending §3.6.
Also: `GameplayWorldHealthBar` frame colour, low-health tint, frame margin and the `0.3` threshold
(§2.5f); `GameplayLockOnMarker` size and height; `SoulPickup` stain size and colour; `ShortcutGate`
open alpha; `GameplayTelegraphPulse` and `ActorIdleBob` (§2.5d); the two `GameplayEnvironmentBuilder`
wall literals and the platform-rim pair (§2.5g); the grunt attack-point offset (§2.5h).

## S11 — The UI (largest stage; also closes rule 3)

251 literals, no Theme, no scene-authored hierarchy. Three sub-stages, each landing green:

**S11a — `Theme` resource.** Author `Resources/UI/MyGame.theme` (or `.tres`) holding the six palette
tokens, the five button `StyleBoxFlat` states, the font, and a font-size ramp. Set it as
`project.godot`'s `default_theme`. Deletes the duplicated palette and the duplicated four-multiplier
state maths in one move (~125 of the 251 literals). *This is the stage the rule's own wording asks
for — "UI 크기·간격·색·폰트·연출은 씬/Theme/StyleBox/Animation 리소스에 저장한다".*

**S11b — Scene-authored layout.** Replace the runtime builders with real `.tscn` hierarchies:
`Scenes/UI/Hud.tscn`, `Scenes/UI/PausePanel.tscn`, `Scenes/UI/VictoryPanel.tscn`,
`Scenes/UI/GateTravelPanel.tscn`, `Scenes/UI/LevelUpPanel.tscn`, plus a `GateRow.tscn` /
`StatRow.tscn` item scene each for the two variable-length lists. Anchors, offsets, `VBoxContainer`
separation and size flags move into the scenes; the twelve hand-typed HUD row Y values become one
container with one separation value; the `280×350` backing plate becomes a `PanelContainer` that
sizes itself. Modal stacking becomes explicit tree order in a saved scene rather than four lines in
`CreateUi`. `TitleScene.tscn` gains its real hierarchy. (~120 literals.)
This contradicts `PORTING_GUIDE.md` §"Scenes and prefabs", which froze the Unity project's
build-everything-in-code shape. That was a *porting* rule; rule 3 is a *production* rule and is newer
and user-approved. **Record the supersession explicitly in `PORTING_GUIDE.md` as part of this stage**
rather than letting the two documents disagree.

**S11c — Timings.** The five `GameplayHud` timings (§2.9) into an `AnimationPlayer` or a small
`UiTuning.json`; the two `2f` warning dwells become one field. `CutsceneOverlay`'s caption box and
font size fold into S8/S11a.

## S12 — Difficulty and New Game+

`DifficultyTuning.json` (new): `easyPlayerDamageTaken: 0.7`, `hardPlayerDamageTaken: 1.4`,
`easyEnemyHealth: 0.85`, `hardEnemyHealth: 1.25`, `newGamePlusEnemyHealthPerCycle: 0.25` — all
multipliers, non-spatial, missing → the current literal. `DifficultySettings` is `static`, so it
needs a `Load()` called from the same place `Apply()` is (`GameplayBootstrap`).
This resolves the `ponytail:` note already standing at `Scripts/Combat/DifficultySettings.cs:30-32`,
which says exactly this: *"move them to Resources/Design when a designer asks to tune them without a
build."*

## S13 — Decisions and cleanup

Settle §3.1–3.19 and record each outcome in this file. Delete or populate
`GameplaySceneDefaultsAsset` (§3.3). Decide the fate of `GameplayEnemy2D` (§3.10). Delete the six
duplicated stamina fields from `CombatTuningData` if S4 has not already.

## Stage summary

| Stage | Scope | New/changed files | Behaviour change? |
|---|---|---|---|
| S0 | validator reverse check | 1 tool file | no |
| S1 | 3 missing `PlayerCombat.json` keys | 1 design file (+3 keys) | no |
| S2 | D1 knockback routing, D2 resonance readout | 2 code files | **yes (D1)** |
| S3 | fallback-table drift | test fixtures + defaults | **yes (tests)** |
| S4 | `CombatTuning.json` | 1 new design file, 6 consumers | no |
| S5 | humanity / death / pickup | `PlayerResources.json` (+10) | no |
| S6 | shared reach + AI timing | `WorldTuning.json` (+11) | no |
| S7 | archetype + chapter-boss fields | 4 + 8 design files (additive) | no |
| S8 | `CutsceneTuning.json` | 1 new design file | no |
| S9 | camera feel | `WorldTuning.json` or new | no |
| S10 | readability layout | **regenerates `Readability.json`** — must be a verified superset | no |
| S11 | Theme + scene-authored UI + timings | new `.theme`, ~7 new `.tscn`, 2 code files | no (visual parity check required) |
| S12 | `DifficultyTuning.json` | 1 new design file | no |
| S13 | decisions + dead code | this file, 2–3 code files | per decision |

Only S2, S3 and S10 carry risk. S2 and S3 change behaviour on purpose and land alone; S10 is the only
stage that touches an existing design file's contents and must prove it added nothing but keys.
