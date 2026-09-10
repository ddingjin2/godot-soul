using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// A portal back down the road. Stand in it, press Interact, and every gate this save has ever opened
    /// is offered - the seven gates are passed in order, but nothing in the fiction says a passed one
    /// closes behind you ([[WorldSetting]]).
    ///
    /// Shaped like <see cref="CheckpointZone"/> on purpose: same trigger, same Interact route through
    /// <see cref="PlayerInputReceiver"/>, same inert-until-Initialize rule. A scene with no portal keeps
    /// exactly the behaviour it has today.
    /// </summary>
    /// <remarks>
    /// Travelling writes the slot before it loads, with the gate being travelled to rather than the one
    /// being left. Without that, quitting after a trip would resume at the chapter the player walked out
    /// of, which is the one place a portal could silently undo progress.
    /// </remarks>
    public sealed partial class GateTravelZone : Area2D
    {
        private const string MarkerObjectName = "GateTravelZoneMarker";

        /// <summary>
        /// Reach of the portal, in Unity metres. Smaller than the bonfire's so the two can share a
        /// corner of the arena.
        /// </summary>
        private const float ZoneRadius = 1.4f;

        private GameplayPlayerContext _player;
        private PlayerInputReceiver _input;
        private GameplayHud _hud;
        private bool _playerInside;
        private bool _isOpen;

        /// <summary>
        /// The portal whose panel is up, or null. Static because <see cref="GameplayPauseController"/>
        /// has to answer "is a menu already up" every frame and is handed its collaborators once at
        /// Initialize - a per-frame tree walk would be a scene scan on every _Process. Godot does not
        /// null a reference to a freed node the way Unity did, so the read goes through
        /// <see cref="GodotObject.IsInstanceValid"/> and a zone destroyed with its scene still reads as
        /// no panel.
        /// </summary>
        private static GateTravelZone _openZone;

        /// <summary>True while a travel panel is up anywhere in the scene.</summary>
        public static bool AnyPanelOpen => _openZone != null && IsInstanceValid(_openZone);

        public bool PlayerInside => _playerInside;
        public bool IsOpen => _isOpen;

        public static void InitializeAll(GameplayPlayerContext player, GameplayHud hud)
        {
            foreach (GateTravelZone zone in SceneQuery.FindAll<GateTravelZone>())
                zone.Initialize(player, hud);
        }

        public void Initialize(GameplayPlayerContext player, GameplayHud hud)
        {
            Unsubscribe();

            _player = player;
            _hud = hud;
            _input = player.GameObject?.GetComponent<PlayerInputReceiver>();

            EnsureTrigger();
            EnsureMarker();

            if (_input != null)
                _input.OnInteract += TryOpen;
        }

        public override void _Ready()
        {
            EnsureTrigger();
            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
        }

        public override void _ExitTree()
        {
            Unsubscribe();

            // Leaving the scene with the panel up would carry a zero timescale into whatever loads next -
            // the same trap GameplayPauseController guards.
            if (!_isOpen)
                return;

            SetOpen(false);
            SetFrozen(false);
        }

        private void Unsubscribe()
        {
            if (_input != null)
                _input.OnInteract -= TryOpen;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (IsPlayerBody(body))
                _playerInside = true;
        }

        private void OnBodyExited(Node2D body)
        {
            if (IsPlayerBody(body))
                _playerInside = false;
        }

        /// <summary>Only the solid body counts, for the reason <see cref="CheckpointZone"/> spells out: reach is not feet.</summary>
        private static bool IsPlayerBody(Node body)
        {
            return body != null && body.IsInGroup(World.Group.Player);
        }

        public void TryOpen()
        {
            if (!_playerInside || _isOpen)
                return;

            // Travelling out of the spirit state would leave the death sequence running in a scene that
            // is about to be unloaded, and the respawn would land in the wrong arena.
            if (_player.DeathController != null && _player.DeathController.IsInSpiritState)
                return;

            Open();
        }

        public void Open()
        {
            if (_hud == null)
            {
                GD.PushWarning($"GateTravelZone on {Name} has no HUD to open; ignoring.");
                return;
            }

            SetOpen(true);
            SetFrozen(true);

            string[] gates = ChapterRoute.Unlocked(GameSave.Read());

            _hud.SetGateTravelVisible(
                true,
                gates,
                TitlesFor(gates),
                GameplayBuildShim.ActiveSceneName,
                Travel,
                Close);
        }

        /// <summary>
        /// What each gate is called. Resolved here rather than in the HUD because the name lives on
        /// <see cref="RainbowChapterBossData"/>, which is the Enemy namespace - UI does not reference it,
        /// and Gameplay already does.
        /// </summary>
        public static string[] TitlesFor(string[] gates)
        {
            if (gates == null)
                return System.Array.Empty<string>();

            var titles = new string[gates.Length];
            for (var i = 0; i < gates.Length; i++)
                titles[i] = TitleFor(gates[i]);

            return titles;
        }

        /// <summary>
        /// A chapter's authored name, or the scene's own name when it has none. Chapter one is the one
        /// gate with no <c>RainbowChapterBossData</c> to read - it predates the authored path - so it
        /// carries the colour the rest of the road names it by ([[RainbowChapterBossDesign]]).
        /// </summary>
        /// <remarks>
        /// Falling back to the scene name is deliberate: a bossDataFile renamed out from under a layout
        /// would otherwise put an empty row in the panel, and a raw scene name is ugly in a way somebody
        /// notices. <c>GameplayChapterBossTests</c> is what fails loudly for that case.
        /// </remarks>
        public static string TitleFor(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return string.Empty;

            if (sceneName == ChapterRoute.FirstChapterScene)
                return FirstChapterTitle;

            GameplaySceneDefaults scene = GameplaySceneDefaults.CreateForScene(sceneName, null);
            if (string.IsNullOrWhiteSpace(scene.BossDataFile))
                return sceneName;

            RainbowChapterBossData boss = GameplayTuningCatalog.Load()?.ChapterBoss(scene.BossDataFile);
            return boss != null && !string.IsNullOrWhiteSpace(boss.ChapterName) ? boss.ChapterName : sceneName;
        }

        /// <summary>Chapter one's name, which no data file carries: the Crimson Warden's gate is the red one.</summary>
        public const string FirstChapterTitle = "Red Chapter";

        public void Close()
        {
            SetOpen(false);
            SetFrozen(false);
            _hud?.SetGateTravelVisible(false, null, null, null, null, null);
        }

        /// <summary>
        /// Walks to a gate that is already open. Refuses one that is not, because the panel is not the
        /// only possible caller and an unlocked-gate check that lives only in the UI is not a check.
        /// </summary>
        public void Travel(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || !ChapterRoute.IsChapter(sceneName))
                return;

            GameSaveData save = GameSave.Read();
            var unlocked = System.Array.IndexOf(ChapterRoute.Unlocked(save), sceneName) >= 0;
            if (!unlocked)
            {
                GD.PushWarning($"GateTravelZone: {sceneName} has not been opened yet; refusing to travel.");
                return;
            }

            if (_player.GameObject != null)
            {
                GameSaveData captured = GameplaySaveBridge.Capture(_player);

                // Through ChapterRoute rather than by assigning chapterScene here: moving the slot to
                // another chapter also drops that chapter's bonfire index and shortcut, and passing a
                // gate has to answer that the same way a portal does. Travelling to the chapter already
                // recorded is a no-op there, so a trip home to where you stand keeps your rest.
                ChapterRoute.SetChapter(captured, sceneName);
                GameSave.Write(captured);

                // The run travels with the player: health, humanity and souls come back on the far side.
                GameSave.LoadOnNextGameplayStart = true;
            }

            SetOpen(false);
            SetFrozen(false);
            GetTree().ChangeSceneToFile(ChapterRoute.ScenePath(sceneName));
        }

        private void SetOpen(bool open)
        {
            _isOpen = open;

            if (open)
                _openZone = this;
            else if (_openZone == this)
                _openZone = null;
        }

        /// <summary>
        /// Stops play while the panel is up. Hit stop drives the timescale too, so it has to be told
        /// first or its _Process writes 1 back on the next frame - the same order the pause menu uses.
        ///
        /// Taking the freeze is unconditional; handing it back is not. Whoever releases a shared hold
        /// asks first whether anyone else still has one, the way <see cref="GameplayPauseController"/>
        /// asks the cutscene director before it hands the controls back.
        /// </summary>
        private void SetFrozen(bool frozen)
        {
            if (!frozen && PauseMenuHoldsTheFreeze())
                return;

            if (HitStopManager.Instance != null)
                HitStopManager.Instance.Suppress = frozen;

            GameClock.TimeScale = frozen ? 0f : 1f;

            if (_input != null)
                _input.Enabled = !frozen;
        }

        /// <summary>
        /// Whether the pause menu is holding the freeze this panel is about to release. Rare enough to
        /// pay for a scene scan: it runs on closing or travelling, not per frame.
        /// </summary>
        private static bool PauseMenuHoldsTheFreeze()
        {
            foreach (GameplayPauseController pause in SceneQuery.FindAll<GameplayPauseController>())
            {
                if (pause.IsPaused)
                    return true;
            }

            return false;
        }

        private void EnsureTrigger()
        {
            CollisionLayer = World.Layer.Trigger;
            CollisionMask = World.Layer.Player;
            SetDeferred(Area2D.PropertyName.Monitoring, true);

            if (this.GetComponent<CollisionShape2D>() != null)
                return;

            AddChild(new CollisionShape2D
            {
                Name = "Trigger",
                Shape = new CircleShape2D { Radius = World.U(ZoneRadius) }
            });
        }

        /// <summary>
        /// Greybox marker, built from the existing disc sprite in the arena-gate colour so it reads as a
        /// gate rather than as a second bonfire. No new art.
        /// </summary>
        private void EnsureMarker()
        {
            if (GetNodeOrNull(MarkerObjectName) != null)
                return;

            var shape = this.GetComponent<CollisionShape2D>()?.Shape as CircleShape2D;
            float radius = shape != null ? shape.Radius : World.U(ZoneRadius);
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();

            var sprite = new Sprite2D
            {
                Name = "Disc",
                Texture = GameplayVisualFactory.CreateDiscSprite(),
                Modulate = readability.ArenaGateColor,
                ZIndex = readability.SpiritPlatformSortingOrder
            };
            sprite.SetSpriteSize(new Vector2(radius * 2f, radius * 2f));

            var marker = new GameplayTelegraphPulse { Name = MarkerObjectName, Position = Vector2.Zero };
            marker.AddChild(sprite);
            AddChild(marker);
        }
    }
}
