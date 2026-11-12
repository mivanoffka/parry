using System.Collections.Generic;
using UnityEngine;

namespace Parry
{
    // Separate movement bodies from damage capsules: rolling never removes personal space.
    [DefaultExecutionOrder(50)]
    public sealed class FighterSpacing : MonoBehaviour
    {
        const float RestDistance = FighterSpacingRules.RestDistance;
        static readonly List<FighterSpacing> bodies = new List<FighterSpacing>();
        PlayerMotor player;
        PracticeTarget target;
        CharacterController controller;
        Vector3 springVelocity;
        bool Alive => isActiveAndEnabled && (player != null ? player.IsAlive : target != null && target.IsAlive);

        public static void Install(GameObject owner)
        {
            if (owner.GetComponent<FighterSpacing>() == null) owner.AddComponent<FighterSpacing>();
        }
        void OnEnable()
        {
            player = GetComponent<PlayerMotor>();
            target = GetComponent<PracticeTarget>();
            controller = GetComponent<CharacterController>();
            if (!bodies.Contains(this)) bodies.Add(this);
            springVelocity = Vector3.zero;
        }
        void OnDisable() { bodies.Remove(this); springVelocity = Vector3.zero; }

        public static Vector3 Constrain(Transform owner, Vector3 displacement)
        {
            Vector3 vertical = Vector3.up * displacement.y;
            Vector3 movement = Vector3.ProjectOnPlane(displacement, Vector3.up);
            Vector3 position = owner.position;
            // Sweep the entire move, including fast lunges, then slide along the contact.
            for (int iteration = 0; iteration < 4 && movement.sqrMagnitude > .0000001f; iteration++)
            {
                float earliest = 1;
                Vector3 normal = Vector3.zero;
                foreach (var body in bodies)
                {
                    if (body == null || body.transform == owner || !body.Alive
                        || Mathf.Abs(position.y - body.transform.position.y) > 1.5f) continue;
                    Vector3 offset = Vector3.ProjectOnPlane(position - body.transform.position, Vector3.up);
                    float fraction = FighterSpacingRules.ContactFraction(offset.x, offset.z, movement.x, movement.z);
                    if (fraction >= earliest) continue;
                    earliest = fraction;
                    normal = (offset + movement * fraction).normalized;
                }
                position += movement * earliest;
                if (normal == Vector3.zero) break;
                movement *= 1 - earliest;
                movement -= normal * Mathf.Min(0, Vector3.Dot(movement, normal));
            }
            return position - owner.position + vertical;
        }

        void LateUpdate()
        {
            if (!Alive) { springVelocity = Vector3.zero; return; }
            Vector3 force = Vector3.zero;
            foreach (var other in bodies)
            {
                if (other == null || other == this || !other.Alive
                    || Mathf.Abs(transform.position.y - other.transform.position.y) > 1.5f) continue;
                Vector3 away = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                float distance = away.magnitude;
                if (distance >= RestDistance) continue;
                // A stable direction also separates characters spawned at exactly the same point.
                Vector3 direction = distance > .0001f ? away / distance
                    : bodies.IndexOf(this) < bodies.IndexOf(other) ? Vector3.left : Vector3.right;
                force += direction * ((RestDistance - distance) * 90);
            }
            float dt = Mathf.Min(Time.deltaTime, .05f);
            springVelocity = (springVelocity + force * dt) / (1 + 14 * dt);
            springVelocity = Vector3.ClampMagnitude(springVelocity, 3);
            if (springVelocity.sqrMagnitude < .00001f) return;
            if (controller != null && controller.enabled)
                controller.Move(Constrain(transform, springVelocity * dt));
            else if (target != null) target.Move(springVelocity * dt);
        }
    }
}
