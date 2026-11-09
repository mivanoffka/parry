using UnityEngine;

namespace Parry
{
    // Damage geometry is independent of the body used for movement and wall collisions.
    public sealed class CombatHitbox : MonoBehaviour
    {
        CapsuleCollider shape;
        PlayerMotor player;
        EnemyBrain enemy;
        PracticeTarget target;
        Vector3 standingCenter;
        float standingHeight, standingRadius;
        bool wasRolling;
        public CapsuleCollider Shape { get { Refresh(); return shape; } }

        void Awake()
        {
            player = GetComponent<PlayerMotor>();
            enemy = GetComponent<EnemyBrain>();
            target = GetComponent<PracticeTarget>();
            var body = GetComponent<CapsuleCollider>();
            var controller = GetComponent<CharacterController>();
            standingCenter = body != null ? body.center : controller.center;
            standingHeight = body != null ? body.height : controller.height;
            standingRadius = body != null ? body.radius : controller.radius;
            var child = new GameObject("Melee damage hitbox");
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            shape = child.AddComponent<CapsuleCollider>();
            shape.isTrigger = true;
            SetShape(false);
        }
        void SetShape(bool rolling)
        {
            shape.radius = standingRadius * (rolling ? .5f : 1);
            shape.height = Mathf.Max(shape.radius * 2, standingHeight * (rolling ? .6f : 1));
            shape.center = standingCenter - Vector3.up * ((standingHeight - shape.height) * .5f);
            wasRolling = rolling;
        }
        void Refresh()
        {
            if (shape == null) return;
            // Arena construction can add EnemyBrain after PracticeTarget has initialized this component.
            if (player == null && enemy == null) enemy = GetComponent<EnemyBrain>();
            bool rolling = player != null ? player.Rolling : enemy != null && enemy.Rolling;
            if (rolling != wasRolling) SetShape(rolling);
            shape.enabled = isActiveAndEnabled && (player != null ? player.IsAlive : target != null && target.IsAlive);
        }
        void LateUpdate() => Refresh();
        void OnDisable() { if (shape != null) shape.enabled = false; }

        public bool IntersectsAttack(Vector3 origin, Vector3 facing, float reach, float halfAngle,
            bool circular = false, bool thrust = false, float width = .12f)
        {
            Refresh();
            if (!shape.enabled) return false;
            // Queries may run before the target's Update, including immediately after a roll starts/ends.
            Physics.SyncTransforms();
            Vector3 delta = Vector3.ProjectOnPlane(shape.bounds.center - origin, Vector3.up);
            Vector3 direction = facing.normalized;
            if (!thrust && delta.sqrMagnitude > .0001f)
            {
                float angle = Vector3.SignedAngle(direction, delta, Vector3.up);
                direction = circular ? delta.normalized
                    : Quaternion.AngleAxis(Mathf.Clamp(angle, -halfAngle, halfAngle), Vector3.up) * direction;
            }
            Vector3 point = origin + direction * Mathf.Clamp(Vector3.Dot(delta, direction), 0, reach);
            return (shape.ClosestPoint(point) - point).sqrMagnitude <= width * width;
        }
    }
}
