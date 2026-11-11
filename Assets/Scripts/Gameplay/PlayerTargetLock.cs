using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Parry
{
    public sealed class PlayerTargetLock : MonoBehaviour
    {
        [SerializeField, Min(1)] float acquisitionRange = 12;
        [SerializeField, Min(1)] float releaseRange = 16;
        public EnemyBrain Target { get; private set; }
        PlayerMotor player;
        LineRenderer marker;
        readonly List<EnemyBrain> candidates = new List<EnemyBrain>();

        public void Initialize(PlayerMotor owner) => player = owner;

        public void Tick()
        {
            if (!player.IsAlive) { Clear(); return; }
            if (Target != null && !Eligible(Target, Mathf.Max(acquisitionRange, releaseRange)))
            {
                bool defeated = Target.Health != null && !Target.Health.IsAlive;
                Clear();
                if (defeated) Acquire();
            }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if ((keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                || (mouse != null && mouse.middleButton.wasPressedThisFrame)
                || (pad != null && pad.rightStickButton.wasPressedThisFrame))
            {
                if (Target != null) Clear();
                else Acquire();
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Clear();
            if (Target == null) return;
            if ((keyboard != null && keyboard.qKey.wasPressedThisFrame)
                || (pad != null && pad.dpad.left.wasPressedThisFrame)) Cycle(-1);
            else if ((keyboard != null && keyboard.eKey.wasPressedThisFrame)
                || (pad != null && pad.dpad.right.wasPressedThisFrame)) Cycle(1);
        }

        bool Eligible(EnemyBrain enemy, float range)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.Health == null || !enemy.Health.IsAlive
                || enemy.gameObject.scene != gameObject.scene) return false;
            Vector3 delta = enemy.transform.position - transform.position;
            if (delta.sqrMagnitude > range * range || SmokeCloud.Obscures(transform.position, enemy.transform.position)) return false;
            return !Physics.Raycast(transform.position + Vector3.up * .65f, delta.normalized,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }

        void Gather()
        {
            candidates.Clear();
            foreach (var enemy in FindObjectsByType<EnemyBrain>())
                if (Eligible(enemy, acquisitionRange)) candidates.Add(enemy);
            // Screen order makes Q/E select the visually adjacent enemy.
            var camera = Camera.main;
            candidates.Sort((a, b) =>
            {
                float ax = camera.WorldToViewportPoint(a.transform.position).x;
                float bx = camera.WorldToViewportPoint(b.transform.position).x;
                int order = ax.CompareTo(bx);
                return order != 0 ? order : (a.transform.position - transform.position).sqrMagnitude
                    .CompareTo((b.transform.position - transform.position).sqrMagnitude);
            });
        }

        public bool Acquire()
        {
            Gather();
            Target = null;
            float nearest = float.PositiveInfinity;
            foreach (var enemy in candidates)
            {
                float distance = (enemy.transform.position - transform.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                Target = enemy;
            }
            return Target != null;
        }

        public void Cycle(int direction)
        {
            Gather();
            if (candidates.Count == 0) { Clear(); return; }
            int index = candidates.IndexOf(Target);
            if (index < 0) { Target = candidates[direction < 0 ? candidates.Count - 1 : 0]; return; }
            Target = candidates[(index + (direction < 0 ? -1 : 1) + candidates.Count) % candidates.Count];
        }

        public void Clear()
        {
            Target = null;
            if (marker != null) marker.enabled = false;
        }

        public bool TryGetDirection(out Vector3 direction)
        {
            direction = Target != null ? Vector3.ProjectOnPlane(Target.transform.position - transform.position, Vector3.up) : Vector3.zero;
            if (direction.sqrMagnitude < .001f) return false;
            direction.Normalize();
            return true;
        }

        void LateUpdate()
        {
            if (Target == null || !player.IsAlive) { if (marker != null) marker.enabled = false; return; }
            if (marker == null)
            {
                marker = DemoArt.Line("Target lock", transform, new Color(1, .8f, .15f), .055f);
                marker.loop = true;
                marker.positionCount = 32;
            }
            marker.enabled = true;
            Vector3 center = Target.transform.position + Vector3.up * .04f;
            for (int i = 0; i < marker.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / marker.positionCount;
                marker.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .65f);
            }
        }
    }
}
