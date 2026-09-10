using Godot;
using MyGame.Combat;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The keyboard, in one place. Unity had two input paths behind
    /// <c>ENABLE_INPUT_SYSTEM</c> / <c>ENABLE_LEGACY_INPUT_MANAGER</c> - the <c>InputSystem_Actions</c>
    /// asset with a hand-written <c>KeyCode</c> fallback underneath it - and every property here read
    /// both and OR'd the answers. Godot's InputMap is one path: the actions listed in
    /// <c>project.godot</c> carry the same bindings the two Unity paths agreed on, so the split and its
    /// private <c>Keys</c> enum are gone.
    ///
    /// The public surface is unchanged, because <see cref="PlayerInputReceiver"/> and the pause
    /// controller are written against it.
    /// </summary>
    public static class GameplayInput
    {
        public static float Horizontal => Input.GetAxis(MoveLeft, MoveRight);

        /// <summary>
        /// **The axis flip point.** Unity read a Move vector in Unity space, where +Y is up; every
        /// consumer of this property - <see cref="PlayerInputReceiver"/> feeding
        /// <c>PlayerController2D.SetMoveInput</c> above all - now works in Godot space, where +Y is
        /// down. So "up" is negative here: <c>move_down</c> is the positive action of the pair. This is
        /// the one line where the port turns the stick over, and nothing downstream flips it again.
        /// </summary>
        public static float Vertical => Input.GetAxis(MoveUp, MoveDown);

        public static bool JumpPressed => WasActionPressed("jump");

        /// <summary>Unity read both Dash and Sprint; the InputMap folds them into one <c>dodge</c> action.</summary>
        public static bool DodgePressed => WasActionPressed("dodge");

        public static bool DashPressed => DodgePressed;
        public static bool AttackPressed => WasActionPressed("attack");

        /// <summary>
        /// Unity spelled this out as "the heavy action, or Alt held while the left mouse button goes
        /// down". The Alt modifier is part of the <c>heavy_attack</c> binding in <c>project.godot</c>,
        /// so the modifier test lives in the InputMap now rather than here.
        /// </summary>
        public static bool HeavyAttackPressed => WasActionPressed("heavy_attack");

        public static bool ParryPressed => WasActionPressed("parry");
        public static bool WrathPressed => SinPressed(SinState.Wrath);
        public static bool SlothPressed => SinPressed(SinState.Sloth);
        public static bool PridePressed => SinPressed(SinState.Pride);

        /// <summary>Unity read both Pause and Menu; one <c>pause</c> action covers them.</summary>
        public static bool PausePressed => WasActionPressed("pause");

        public static bool InteractPressed => WasActionPressed("interact");
        public static bool HealPressed => WasActionPressed("heal");
        public static bool LockOnPressed => WasActionPressed("lock_on");

        private const string MoveLeft = "move_left";
        private const string MoveRight = "move_right";
        private const string MoveUp = "move_up";
        private const string MoveDown = "move_down";

        /// <summary>
        /// The seven sins in the order they are bound to the number row, so adding a sin is one entry
        /// here plus one action in <c>project.godot</c>. Kept as one table rather than seven properties
        /// because the receiver only ever asks "which sin was pressed" - see <see cref="PressedSin"/>.
        /// </summary>
        private static readonly (SinState Sin, string Action)[] SinBindings =
        {
            (SinState.Wrath, "sin_wrath"),
            (SinState.Sloth, "sin_sloth"),
            (SinState.Pride, "sin_pride"),
            (SinState.Gluttony, "sin_gluttony"),
            (SinState.Greed, "sin_greed"),
            (SinState.Envy, "sin_envy"),
            (SinState.Lust, "sin_lust")
        };

        public static bool SinPressed(SinState sin)
        {
            foreach ((SinState boundSin, string action) in SinBindings)
            {
                if (boundSin == sin)
                    return WasActionPressed(action);
            }

            return false;
        }

        /// <summary>
        /// The first sin key held this frame, or <see cref="SinState.None"/>. First rather than all,
        /// because activating a sin is exclusive anyway - two keys in one frame would only mean the
        /// second request bounces off the first.
        /// </summary>
        public static SinState PressedSin()
        {
            foreach ((SinState sin, string action) in SinBindings)
            {
                if (WasActionPressed(action))
                    return sin;
            }

            return SinState.None;
        }

        /// <summary>
        /// One frame's edge. <c>InputMap.HasAction</c> guards it because a headless test run loads
        /// project.godot's actions, but a fixture that builds its own tree may not - and Godot logs an
        /// error for an unknown action rather than returning false.
        /// </summary>
        private static bool WasActionPressed(string action)
        {
            return InputMap.HasAction(action) && Input.IsActionJustPressed(action);
        }
    }
}
