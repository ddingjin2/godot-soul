// UnityTestAgent package runtime, copied verbatim from the Unity project's
// Packages/com.ddingjin.unity-test-agent/Runtime/.
//
// It is not "ported": every file under Scripts/Testing/Package/ is pure BCL C# with no UnityEngine
// reference at all, so it compiles here unchanged and keeps its own UnityTestAgent.* namespaces. The
// eleven MyGame* adapters next to this folder are the port; this is the library they plug into, and
// it lives under Scripts/Testing/ because a Godot project has no package manager to fetch it from.
// Fakes/ and the package's own EditMode tests were left behind - nothing in this project uses them.
//
// One file is not verbatim: Scenario/SafetyConditions.cs, whose OutOfBoundsCondition tested "fell
// below" with a `<` on a +Y-up axis. That flips to `>` here, and the comment on the class says so.
using System;

namespace UnityTestAgent.Core
{
    [Serializable]
    public struct AgentAction
    {
        public AgentActionType Type;
        public float Direction;
        public float Strength;
        public float DurationSeconds;
        public string Metadata;

        public static AgentAction Idle(float durationSeconds = 0f)
        {
            return new AgentAction
            {
                Type = AgentActionType.Idle,
                DurationSeconds = durationSeconds
            };
        }

        public static AgentAction Move(float direction, float durationSeconds = 0f)
        {
            return new AgentAction
            {
                Type = AgentActionType.Move,
                Direction = ClampDirection(direction),
                Strength = Math.Abs(direction),
                DurationSeconds = durationSeconds
            };
        }

        public static AgentAction Jump()
        {
            return Simple(AgentActionType.Jump);
        }

        public static AgentAction Dodge(float direction)
        {
            return new AgentAction
            {
                Type = AgentActionType.Dodge,
                Direction = ClampDirection(direction),
                Strength = Math.Abs(direction)
            };
        }

        public static AgentAction Attack()
        {
            return Simple(AgentActionType.LightAttack);
        }

        public static AgentAction HeavyAttack()
        {
            return Simple(AgentActionType.HeavyAttack);
        }

        public static AgentAction Guard()
        {
            return Simple(AgentActionType.Guard);
        }

        public static AgentAction Interact()
        {
            return Simple(AgentActionType.Interact);
        }

        public static AgentAction UseItem(string itemId = "")
        {
            var action = Simple(AgentActionType.UseItem);
            action.Metadata = itemId;
            return action;
        }

        private static AgentAction Simple(AgentActionType type)
        {
            return new AgentAction
            {
                Type = type,
                Strength = 1f
            };
        }

        private static float ClampDirection(float direction)
        {
            if (direction > 0f)
            {
                return 1f;
            }

            if (direction < 0f)
            {
                return -1f;
            }

            return 0f;
        }
    }
}
