using System.Collections.Generic;
using Godot;
using MyGame.Player;
using UnityTestAgent.Core;
using UnityTestAgent.Input;

namespace MyGame.Testing
{
    /// <summary>
    /// Turns an <see cref="AgentAction"/> into keys held down, so a scripted agent plays the game
    /// through the same input path a person does.
    /// </summary>
    /// <remarks>
    /// **Why the Unity Input System plumbing is gone.** Unity had two input paths behind
    /// <c>ENABLE_INPUT_SYSTEM</c> / <c>ENABLE_LEGACY_INPUT_MANAGER</c> and no supported way to push a
    /// synthetic press into either from a play-mode test, so this driver bypassed input entirely: it
    /// called <c>PlayerController2D.RequestAttack()</c> and friends directly, underneath the keyboard.
    /// That is why every Unity fixture had to switch <c>PlayerInputReceiver</c> off first - left on, the
    /// receiver wrote <c>SetMoveInput</c> from a dead keyboard once a frame and overwrote the agent's
    /// move input one step ahead of every FixedUpdate, so the driven player kept swinging and stopped
    /// walking. Three separate files carried a comment about that trap.
    ///
    /// Godot has a first-class answer: <see cref="Input.ActionPress(StringName, float)"/> and
    /// <see cref="Input.ActionRelease(StringName)"/> set an InputMap action's state directly, and
    /// <c>Input.ParseInputEvent(new InputEventAction { ... })</c> feeds the same thing through the event
    /// queue. So this driver presses <c>move_right</c>, <c>attack</c>, <c>dodge</c> - the action names in
    /// the porting guide - and <c>GameplayInput</c> reads them back exactly as it reads a keyboard.
    /// The trap goes with it: <see cref="MyGame.Gameplay.PlayerInputReceiver"/> must now be **left
    /// enabled**, because it is the thing the agent is talking to.
    ///
    /// A held action stays held across frames until something releases it, which is what a walk is. A
    /// one-shot (jump, attack, parry) is a <see cref="Tap"/>: pressed now, released on the next
    /// <see cref="Apply"/>, so <c>Input.IsActionJustPressed</c> sees one clean edge per tick rather than
    /// a key welded down.
    /// </remarks>
    public sealed class MyGameAgentInputDriver : IAgentInputDriver
    {
        // Action names as bound in project.godot. Named here rather than reached for through
        // GameplayInput because GameplayInput only reads them; nothing else in the project writes them.
        private const string MoveLeft = "move_left";
        private const string MoveRight = "move_right";
        private const string Jump = "jump";
        private const string Dodge = "dodge";
        private const string Attack = "attack";
        private const string HeavyAttack = "heavy_attack";
        private const string Parry = "parry";
        private const string Heal = "heal";

        /// <summary>How many more <see cref="Apply"/> ticks each pressed action survives. -1 is "until released".</summary>
        private readonly Dictionary<string, int> _held = new();

        private readonly PlayerController2D _player;

        public MyGameAgentInputDriver(PlayerController2D player)
        {
            _player = player;
        }

        /// <summary>Holds an action down until <see cref="Release"/> or <see cref="ReleaseAll"/>.</summary>
        public void Press(string action, float strength = 1f)
        {
            if (!InputMap.HasAction(action))
                return;

            // Only on a fresh press: Input.ActionPress re-stamps the "pressed this frame" marker, so
            // re-pressing an already-held action every tick would make IsActionJustPressed fire every
            // tick - a held jump key would then jump forever.
            if (!_held.ContainsKey(action))
                Input.ActionPress(action, strength);

            _held[action] = -1;
        }

        public void Release(string action)
        {
            if (!_held.Remove(action))
                return;

            if (InputMap.HasAction(action))
                Input.ActionRelease(action);
        }

        public void ReleaseAll()
        {
            foreach (string action in new List<string>(_held.Keys))
                Release(action);
        }

        /// <summary>Holds an action down for the next <paramref name="frames"/> calls to <see cref="Apply"/>.</summary>
        public void HoldForFrames(string action, int frames, float strength = 1f)
        {
            if (frames <= 0 || !InputMap.HasAction(action))
                return;

            if (!_held.ContainsKey(action))
                Input.ActionPress(action, strength);

            _held[action] = frames;
        }

        /// <summary>One clean press/release edge: down now, up at the start of the next tick.</summary>
        public void Tap(string action) => HoldForFrames(action, 1);

        public void Apply(AgentAction action)
        {
            if (_player == null)
                return;

            ExpireHolds();

            switch (action.Type)
            {
                case AgentActionType.Idle:
                    ReleaseMovement();
                    break;
                case AgentActionType.Move:
                    HoldMovement(action.Direction);
                    break;
                case AgentActionType.Jump:
                    Tap(Jump);
                    break;
                case AgentActionType.Dodge:
                    HoldMovement(action.Direction);
                    Tap(Dodge);
                    break;
                case AgentActionType.LightAttack:
                    Tap(Attack);
                    break;
                case AgentActionType.HeavyAttack:
                    Tap(HeavyAttack);
                    break;
                case AgentActionType.Guard:
                case AgentActionType.Parry:
                    Tap(Parry);
                    break;
                // The action type existed from the start and nothing mapped it, so no agent could ever
                // drink - which is most of why a driven run dies to a cluster it could have survived.
                case AgentActionType.UseItem:
                    Tap(Heal);
                    break;
            }
        }

        /// <summary>Walk in <paramref name="direction"/> (+1 right, -1 left, 0 stop), releasing the other way.</summary>
        private void HoldMovement(float direction)
        {
            if (direction > 0f)
            {
                Release(MoveLeft);
                Press(MoveRight);
            }
            else if (direction < 0f)
            {
                Release(MoveRight);
                Press(MoveLeft);
            }
            else
            {
                ReleaseMovement();
            }
        }

        private void ReleaseMovement()
        {
            Release(MoveLeft);
            Release(MoveRight);
        }

        private void ExpireHolds()
        {
            List<string> expired = null;
            foreach (string action in new List<string>(_held.Keys))
            {
                int remaining = _held[action];
                if (remaining < 0)
                    continue; // held until something releases it

                _held[action] = --remaining;
                if (remaining <= 0)
                    (expired ??= new List<string>()).Add(action);
            }

            if (expired == null)
                return;

            foreach (string action in expired)
                Release(action);
        }
    }
}
