using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// A barred door that opens once and stays open. Reach it from the far side of the level, press
    /// Interact, and the wall drops out of the way for the rest of the chapter - which is the difference
    /// between a 300-unit level and a 300-unit punishment, because every death after that costs the run
    /// back and not the walk in.
    ///
    /// Shaped like <see cref="GateTravelZone"/>: same Interact route through
    /// <see cref="PlayerInputReceiver"/>. A chapter that authors no shortcut builds none, so nothing
    /// changes for an arena that has never needed one.
    ///
    /// "From the far side" is enforced and not advice: the trigger reaches both faces of the door, so a
    /// gate that opened from either one would be opened by the player walking past it on the way out,
    /// and the long route it exists to fold up would never be walked. Which side is far is
    /// <see cref="SetOpensFromRight"/>, from the layout file.
    /// </summary>
    /// <remarks>
    /// One per chapter, and that is a save-shape decision rather than a taste one:
    /// <see cref="GameSaveData.shortcutOpened"/> is a single bool, so a second door in the same chapter
    /// would share the first one's memory. A chapter that wants two needs a field that can count.
    ///
    /// Unlike the bonfire and the portal this one does NOT stay inert until something wires it. Those two
    /// do nothing when unwired; an unwired door is a wall with no way through, which is a level nobody
    /// can finish. So it wires itself off the body that walks into it, and <see cref="Initialize"/> is
    /// only the earlier, explicit way to do the same thing.
    ///
    /// In Unity one GameObject carried the door's solid collider, its sprite and this component. Here the
    /// whole door is <c>Scenes/World/ShortcutGate.tscn</c> - <c>SolidBox.tscn</c> inherited, so the body
    /// is the same <see cref="StaticBody2D"/> every platform is - and the gate is a child node of it,
    /// the port's standard shape for "another component on the same object", so the body and the sprite
    /// it drives are reached with <c>GetComponentInParent</c>. A Godot node cannot be solid and a
    /// trigger at once either, so the Interact reach is a child <see cref="Area2D"/> of its own, and
    /// that reach is authored in the same scene.
    /// </remarks>
    public sealed partial class ShortcutGate : Node2D
    {
        private const string TriggerObjectName = "ShortcutGateTrigger";

        /// <summary>How much of the door is left visible once it is open. Not zero: the way through is worth seeing. Authored on <c>Scenes/World/ShortcutGate.tscn</c>, which is the only copy of the number - no initialiser here to drift from it (K7b).</summary>
        [Export] private float openAlpha;

        /// <summary>Raised when the door opens, as the seam for a HUD line or a sound.</summary>
        public event Action OnOpened;

        public bool IsOpen => _isOpen;
        public bool PlayerInside => _playerInside;

        /// <summary>Which side the door has to be reached from to open it. See <see cref="SetOpensFromRight"/>.</summary>
        public bool OpensFromRight => _opensFromRight;

        private PlayerInputReceiver _input;
        private DeathStateController _death;
        private CollisionShape2D _body;
        private Sprite2D _renderer;
        private Area2D _trigger;
        private bool _isOpen;
        private bool _playerInside;
        private bool _opensFromRight = true;
        private Node2D _playerBody;

        /// <summary>
        /// Interact comes off <see cref="PlayerInputReceiver"/> rather than out of
        /// <see cref="GameplayInput"/>, for the reason <see cref="CheckpointZone"/> spells out: a zero
        /// timescale does not stop _Process, so a gate that read the key itself would open through the
        /// pause menu.
        /// </summary>
        /// <remarks>
        /// Not what wires a shipped gate. The trigger's <c>BodyEntered</c> subscribes off the body that
        /// arrives, which is why the environment builder never calls this and the door still opens; a
        /// static InitializeAll alongside it had zero callers in production and in the tests, and was
        /// removed rather than left as a second way to do the one thing that already happens by itself.
        /// This stays for a fixture that wants the subscription before the player has ever touched the
        /// trigger.
        /// </remarks>
        public void Initialize(GameplayPlayerContext player)
        {
            Unsubscribe();

            _death = player.DeathController;
            Subscribe(player.GameObject?.GetComponent<PlayerInputReceiver>());
            EnsureTrigger();
        }

        /// <summary>
        /// Names the far side - the one the level makes the player walk the long way round to reach.
        /// True is the right (+x) side, which is every shipped arena: the door sits between the last
        /// bonfire and the boss, so the far side is the boss's. False is a level laid out the other way.
        /// </summary>
        public void SetOpensFromRight(bool opensFromRight)
        {
            _opensFromRight = opensFromRight;
        }

        public override void _Ready()
        {
            EnsureTrigger();
        }

        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Subscribe(PlayerInputReceiver input)
        {
            _input = input;
            if (_input != null)
                _input.OnInteract += TryOpen;
        }

        private void Unsubscribe()
        {
            if (_input != null)
                _input.OnInteract -= TryOpen;

            _input = null;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!IsPlayerBody(body))
                return;

            _playerInside = true;

            // Held so TryOpen can ask where the player is standing at the moment they press Interact.
            // The side used to be latched here instead, on the reasoning that a solid door makes the
            // entry frame unambiguous - but the door is only solid, not tall: the player clears chapter
            // two's by dropping onto it from a ledge, entering the trigger mid-flight from the west and
            // landing east of it. Latching at entry refused the Interact they then pressed from the far
            // side, which made the shortcut unopenable by any route through the level.
            _playerBody = body;

            // Wired off the body that arrived, so a gate nobody called Initialize on still opens. Cheap:
            // it runs once, on the frame the player first reaches the door.
            if (_input == null)
            {
                _death = body.GetComponentInParent<DeathStateController>();
                Subscribe(body.GetComponentInParent<PlayerInputReceiver>());
            }
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

            // The far side only. A door that opens from the near side is not a shortcut - the player
            // meets it on the way out, opens it, walks through, and the climb it was meant to fold up
            // never gets climbed once. The saving has to be paid for on the first trip to exist at all.
            // Read now rather than at trigger entry, so that clearing the door from above and pressing
            // Interact where you land counts as the far side, which is what it plainly is. The collider
            // centre rather than the node origin, so a body whose shape is offset from its pivot is
            // measured where it actually is.
            if (_playerBody == null || !IsInstanceValid(_playerBody))
                return;

            bool standingOnTheRight = _playerBody.BodyBounds(Vector2.Zero).GetCenter().X >= GlobalPosition.X;
            if (standingOnTheRight != _opensFromRight)
                return;

            // Opening out of the spirit state would spend the interaction on a body that is about to be
            // moved to a checkpoint, the same case the bonfire and the portal both refuse.
            if (_death != null && _death.IsInSpiritState)
                return;

            Open();
        }

        /// <summary>Opens the door and remembers it. Idempotent: a second Interact on an open gate does nothing.</summary>
        public void Open()
        {
            if (_isOpen)
                return;

            SetOpen(true);
            Remember();

            GD.Print($"ShortcutGate: opened {Name}.");
            OnOpened?.Invoke();
        }

        /// <summary>
        /// Puts the door in a state without recording it - how a resumed chapter starts with its shortcut
        /// already open, and the only way to close one again.
        /// </summary>
        public void SetOpen(bool open)
        {
            _isOpen = open;
            Cache();

            // The door's own shape is disabled; the interaction reach is a separate Area2D and stays
            // live, which is also why Unity disabled the collider rather than turning it into a trigger.
            // Deferred because an open can land inside a physics callback, and Godot forbids flushing
            // a shape change from there.
            _body?.SetDeferred(CollisionShape2D.PropertyName.Disabled, open);

            if (_renderer == null)
                return;

            Color color = _renderer.Modulate;
            color.A = open ? openAlpha : 1f;
            _renderer.Modulate = color;
        }

        /// <summary>
        /// Merged into the slot rather than captured off the player. A shortcut is not a rest: writing a
        /// full capture here would quietly bank the health and souls the player is holding, which is the
        /// bonfire's job and the only thing that makes a bonfire worth walking to.
        /// </summary>
        /// <remarks>
        /// A run that has not written a slot yet still gets one. Returning on a null read dropped the door
        /// on exactly the player most likely to reach it before their first rest - a first-time one - and
        /// the loss was invisible until they quit and came back to a locked shortcut. The record left here
        /// carries nothing but the chapter and the door: health, humanity and souls stay at their unset
        /// sentinels, which <see cref="GameplaySaveBridge.Apply"/> already knows to leave alone.
        /// </remarks>
        private void Remember()
        {
            GameSaveData slot = GameSave.Read() ?? new GameSaveData();

            // Stamped through ChapterRoute, and before the flag rather than after. Capture only carries
            // the door forward while the slot still names this chapter, so a fresh record's empty
            // chapterScene would lose it again at the very next rest - and a slot pointing somewhere else
            // has a bonfire index that means nothing here, which is the one thing SetChapter clears.
            ChapterRoute.SetChapter(slot, GameplayBuildShim.ActiveSceneName);

            slot.shortcutOpened = true;
            GameSave.Write(slot);
        }

        private void Cache()
        {
            // Both live on the door body this node hangs under, so the lookup walks up rather than down.
            _renderer ??= this.GetComponentInParent<Sprite2D>();

            // The door's own shape, not the interaction reach: the reach sits under this node's child
            // Area2D, and GetComponentInParent finds the parent body's shape first.
            _body ??= GetParent().GetComponent<CollisionShape2D>();
        }

        /// <summary>
        /// Subscribes to the reach <c>Scenes/World/ShortcutGate.tscn</c> authors - the Area2D and its
        /// 5 m circle, whose size and the reasoning behind it now live in that file. The solid body is
        /// left alone; it is the door.
        /// </summary>
        /// <remarks>
        /// A gate with no trigger never answers Interact and gives no sign why, which on this component
        /// means a level nobody can finish - so the missing case is a warning rather than silence.
        /// Nothing in the project builds a <see cref="ShortcutGate"/> by hand; the arena instances the
        /// scene, which is what carries the reach.
        /// </remarks>
        private void EnsureTrigger()
        {
            if (_trigger != null)
                return;

            _trigger = GetNodeOrNull<Area2D>(TriggerObjectName);
            if (_trigger == null)
            {
                GD.PushWarning(
                    $"ShortcutGate on {Name} has no {TriggerObjectName} child, so the door can never be opened and the " +
                    "chapter behind it cannot be finished. Instance Scenes/World/ShortcutGate.tscn rather than adding " +
                    "the component to a bare body.");
                return;
            }

            _trigger.BodyEntered += OnBodyEntered;
            _trigger.BodyExited += OnBodyExited;
        }
    }
}
