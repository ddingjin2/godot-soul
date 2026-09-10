using System;
using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// The player body. In Unity this was a MonoBehaviour writing <c>Rigidbody2D.linearVelocity</c>
    /// outright every FixedUpdate; here it <i>is</i> the <see cref="CharacterBody2D"/>, and the same
    /// authored velocity is handed to <see cref="CharacterBody2D.MoveAndSlide"/>.
    ///
    /// Every vertical number in this file is flipped from the Unity source: Godot's +Y points down, so
    /// gravity adds to <c>Velocity.Y</c>, a jump subtracts, and the fall clamp is an upper bound rather
    /// than a lower one. Each flip is marked.
    /// </summary>
    public partial class PlayerMotor2D : CharacterBody2D
    {
        // Movement. Authored in Unity metres, stored in pixels - PlayerMovementData.Load does the
        // scaling for the tuned case, these defaults are pre-scaled for the untuned one.
        [Export] private float moveSpeed = World.U(6f);
        [Export] private float acceleration = World.U(30f);
        [Export] private float deceleration = World.U(25f);
        [Export] private float gravity = World.U(20f);
        [Export] private float maxFallSpeed = World.U(15f);

        // Jump.
        [Export] private float jumpForce = World.U(12f);
        [Export] private float coyoteTime = 0.1f;
        [Export] private float jumpBufferTime = 0.12f;

        /// <summary>Unity <c>UnityEvent OnJump</c>.</summary>
        public event Action OnJump;

        /// <summary>Raised with the new facing sign whenever it changes.</summary>
        public event Action<float> OnFacingChanged;

        // Velocity is CharacterBody2D's own property - the Unity motor's read-only Velocity accessor is
        // the same member, so it is not redeclared here.
        public bool IsGrounded => _isGrounded;
        public float FacingDirection => _facingDir;
        public float CoyoteTime => coyoteTime;
        public bool IsInKnockback => _knockbackTimer > 0f;

        private Vector2 _moveInput;
        private float _facingDir = 1f;
        private bool _hasFacingOverride;
        private float _speedModifier = 1f;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private bool _isGrounded;
        private bool _isJumping;
        private bool _jumpRequested;
        private bool _canMoveAndJump;
        private float _knockbackTimer;
        private Vector2 _knockbackVel;

        /// <summary>
        /// Godot needs no frictionless surface material, which is why the Unity source's
        /// <c>FrictionlessSurface</c> static is gone rather than ported.
        ///
        /// <see cref="CharacterBody2D.MoveAndSlide"/> moves an authored velocity and slides it along
        /// contacts; it never runs a friction solver against it, so a contact cannot quietly subtract
        /// speed the way Unity's default 0.4 friction did. That mattered: cancelling this motor's custom
        /// gravity cost Unity's solver a normal impulse every step and up to 0.16 m/s of horizontal speed
        /// went back out with it, so ground movement settled near 4.8 against an authored moveSpeed of 6
        /// while the air - no contact, no friction - ran the full 6, and hopping was the fastest way to
        /// cross a level. Here the authored 6 (600 px/s) is simply the speed, on the ground and in the air.
        ///
        /// There is no built-in gravity to switch off either: a CharacterBody2D applies none, so the
        /// Unity <c>gravityScale = 0</c> call has nothing to correspond to.
        /// </summary>
        public void Initialize()
        {
            // Up is -Y in Godot; IsOnFloor tests contact normals against this.
            UpDirection = Vector2.Up;
        }

        public override void _Ready()
        {
            Initialize();
            AddToGroup(World.Group.Player);
        }

        public void SetMoveInput(Vector2 input)
        {
            _moveInput = input.LimitLength(1f);
        }

        public void SetSpeedModifier(float speedModifier)
        {
            _speedModifier = speedModifier;
        }

        /// <summary>
        /// Points facing at something other than the move input, and keeps it there until cleared.
        /// Lock-on is the only caller: without it, walking away from an enemy turns the player away
        /// from it, and every swing goes the wrong way.
        /// </summary>
        public void SetFacingOverride(float direction)
        {
            if (direction == 0f)
            {
                return;
            }

            _hasFacingOverride = true;

            float sign = Mathf.Sign(direction);
            if (Mathf.IsEqualApprox(sign, _facingDir))
            {
                return;
            }

            _facingDir = sign;
            OnFacingChanged?.Invoke(_facingDir);
        }

        public void ClearFacingOverride()
        {
            _hasFacingOverride = false;
        }

        public void ApplyTuning(PlayerMovementData tuning)
        {
            if (tuning == null)
            {
                return;
            }

            moveSpeed = tuning.moveSpeed;
            acceleration = tuning.acceleration;
            deceleration = tuning.deceleration;
            gravity = tuning.gravity;
            maxFallSpeed = tuning.maxFallSpeed;
            jumpForce = tuning.jumpForce;
            coyoteTime = tuning.coyoteTime;
            jumpBufferTime = tuning.jumpBufferTime;
        }

        public void RequestJump()
        {
            _jumpRequested = true;
            _jumpBufferTimer = jumpBufferTime;
        }

        public bool Tick(float deltaTime, bool canMoveAndJump)
        {
            _canMoveAndJump = canMoveAndJump;
            return _knockbackTimer > 0f;
        }

        /// <summary>
        /// The old FixedUpdate body. Called from <see cref="PlayerController2D"/>'s
        /// <c>_PhysicsProcess</c>, which is where <see cref="CharacterBody2D.MoveAndSlide"/> below has to
        /// run, and which is also where the dodge flag it needs is known.
        /// </summary>
        public void FixedTick(float fixedDeltaTime, bool isDodging)
        {
            if (_knockbackTimer > 0f)
            {
                _knockbackTimer -= fixedDeltaTime;
                Velocity = _knockbackVel;
            }
            else if (_canMoveAndJump)
            {
                HandleMovement(_moveInput.X, fixedDeltaTime);
                HandleJump();
                _jumpRequested = false;
                UpdateJumpTimers(fixedDeltaTime);
            }

            if (!isDodging && _knockbackTimer <= 0f)
            {
                // Y FLIP: Unity subtracted gravity from a +Y-up velocity and floored it at -maxFallSpeed.
                // Falling is +Y here, so gravity adds and the clamp is a ceiling.
                float fallVel = Velocity.Y + gravity * fixedDeltaTime;
                fallVel = Mathf.Min(fallVel, maxFallSpeed);
                Velocity = new Vector2(Velocity.X, fallVel);
            }

            MoveAndSlide();
            ShoveBlockingBodies();
            CheckGround();
        }

        /// <summary>
        /// PORT: the player and every enemy were dynamic <c>Rigidbody2D</c> at the default mass of 1, so
        /// walking into an enemy shoved it along - Unity's contact solver cancelled the approach velocity
        /// by splitting it between the two bodies, and an enemy that was not writing its own X velocity
        /// that step (mid-telegraph, mid-attack, stunned) simply kept the half it was given and slid.
        /// Two <see cref="CharacterBody2D"/>s exchange no impulse at all, so without this an enemy that
        /// has stopped moving is an immovable wall. That is only a nuisance in the open, where a jump
        /// clears it; under a low platform - Chapter02_Orange's leaper at x=232.8 stands under
        /// ColonnadeStepA, whose underside leaves 1.0 unit of headroom against the 1.0 unit of lift
        /// needed to clear a body - there is no way past at all, and the run ends there.
        /// </summary>
        /// <remarks>
        /// Half the blocked motion, which is where two equal masses meet, and horizontal only: the
        /// vertical half of a Unity contact was a normal impulse holding one body up on another, which
        /// <see cref="CharacterBody2D.MoveAndSlide"/> already does. The push goes through
        /// <c>MoveAndCollide</c> rather than a position write so an enemy cannot be shoved into geometry,
        /// and it is deliberately not sticky: an enemy that writes its own X velocity next step overrides
        /// it, exactly as it overrode the solver's impulse in Unity.
        /// </remarks>
        private void ShoveBlockingBodies()
        {
            for (int i = 0; i < GetSlideCollisionCount(); i++)
            {
                KinematicCollision2D hit = GetSlideCollision(i);

                // The only CharacterBody2D this body's mask reaches is an enemy; world and ground are
                // StaticBody2D, so this needs no layer test of its own.
                if (hit.GetCollider() is CharacterBody2D body)
                {
                    body.MoveAndCollide(new Vector2(hit.GetRemainder().X * 0.5f, 0f));
                }
            }
        }

        public void ApplyKnockback(Vector2 velocity, float duration)
        {
            _knockbackVel = velocity;
            _knockbackTimer = duration;
        }

        public void ResetMotion()
        {
            _moveInput = Vector2.Zero;
            _jumpRequested = false;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _isJumping = false;
            _knockbackTimer = 0f;
            _knockbackVel = Vector2.Zero;
            _hasFacingOverride = false;
            Velocity = Vector2.Zero;
        }

        private void HandleMovement(float h, float deltaTime)
        {
            float targetSpeed = h * moveSpeed * _speedModifier;
            float accel = h != 0 ? acceleration : deceleration;

            // MoveToward, not Lerp toward a fraction of the gap. An exponential approach only ever
            // asymptotes, so the player never actually reached moveSpeed and - with no surface friction
            // to finish the job - would never actually reach a standstill either, creeping forever after
            // the stick was released. Linear arrives, exactly, at both ends. acceleration and deceleration
            // read as units per second squared, which is what those authored numbers already looked like.
            float newVel = Mathf.MoveToward(Velocity.X, targetSpeed, accel * deltaTime);
            Velocity = new Vector2(newVel, Velocity.Y);

            // Under a lock-on override the move input still drives movement; it just stops driving
            // which way the player is turned.
            if (h != 0f && !_hasFacingOverride)
            {
                _facingDir = Mathf.Sign(h);
                OnFacingChanged?.Invoke(_facingDir);
            }
        }

        private void HandleJump()
        {
            bool wantsJump = _jumpRequested;

            if (wantsJump && (_isGrounded || _coyoteTimer > 0f))
            {
                // Y FLIP: Unity wrote +jumpForce into a +Y-up velocity. Up is -Y here.
                Velocity = new Vector2(Velocity.X, -jumpForce);
                _isJumping = true;
                _coyoteTimer = 0f;
                _jumpBufferTimer = 0f;
                OnJump?.Invoke();
            }
            else if (wantsJump)
            {
                _jumpBufferTimer = jumpBufferTime;
            }

            // Y FLIP: the "the rise is over" test was velocity.y < 0 in Unity. Falling is +Y here.
            // Halving the downward velocity at the apex is the same short-hop cut it always was.
            if (_isJumping && Velocity.Y > 0f)
            {
                Velocity = new Vector2(Velocity.X, Velocity.Y * 0.5f);
                _isJumping = false;
            }
        }

        private void UpdateJumpTimers(float deltaTime)
        {
            _coyoteTimer -= deltaTime;
            _jumpBufferTimer -= deltaTime;

            if (_jumpBufferTimer > 0f && _isGrounded)
            {
                // Y FLIP: same jump as above, same reason.
                Velocity = new Vector2(Velocity.X, -jumpForce);
                _isJumping = true;
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                OnJump?.Invoke();
            }
        }

        /// <summary>
        /// Unity swept three downward rays from the collider's feet against Default/Ground/Enemy.
        /// <see cref="CharacterBody2D.IsOnFloor"/> answers the same question from the contacts
        /// <see cref="CharacterBody2D.MoveAndSlide"/> just resolved, against this body's CollisionMask -
        /// so the mask the spawner gives the body is what the old LayerMask argument used to be.
        /// The coyote timer is armed exactly as it was.
        /// </summary>
        private void CheckGround()
        {
            _isGrounded = IsOnFloor();

            if (_isGrounded)
            {
                _coyoteTimer = coyoteTime;
            }
        }
    }
}
