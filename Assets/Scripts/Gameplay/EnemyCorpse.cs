using UnityEngine;

namespace Parry
{
    public sealed class EnemyCorpse : MonoBehaviour
    {
        SpriteRenderer body;
        Camera view;
        float fallAngle;
        float createdAt;
        Transform skeleton;
        Vector3 deathDirection;
        float slideProgress;
        const float SlideDistance = .65f;
        const float FallDuration = .35f;

        public static EnemyCorpse Create(SpriteRenderer source, Color color, Vector3 position, Vector3 direction, Transform arena)
        {
            var remains = new GameObject("Fallen enemy");
            remains.transform.SetParent(arena, false);
            remains.transform.position = position;
            var corpse = remains.AddComponent<EnemyCorpse>();
            corpse.view = Camera.main;
            corpse.createdAt = Time.time;
            corpse.deathDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            corpse.fallAngle = Vector3.Dot(direction, corpse.view.transform.right) >= 0 ? -90 : 90;
            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(remains.transform, false);
            bodyObject.transform.localPosition = Vector3.up * .32f;
            bodyObject.transform.localScale = new Vector3(.45f, .9f, 1);
            corpse.body = bodyObject.AddComponent<SpriteRenderer>();
            corpse.body.sprite = source.sprite;
            corpse.body.sharedMaterial = source.sharedMaterial;
            corpse.body.flipX = source.flipX;
            corpse.body.color = Color.Lerp(color, new Color(.18f, .13f, .16f), .5f);
            var presentation = source.GetComponentInParent<CharacterPresentation>();
            if (presentation != null) corpse.skeleton = presentation.CreateCorpseSkeleton(remains.transform);
            if (corpse.skeleton != null)
            {
                corpse.body.enabled = false;
                foreach (var part in corpse.skeleton.GetComponentsInChildren<SpriteRenderer>()) part.color = corpse.body.color;
            }
            DemoArt.Shadow(remains.transform);
            corpse.LateUpdate();
            return corpse;
        }

        void LateUpdate()
        {
            float progress = Mathf.Clamp01((Time.time - createdAt) / FallDuration);
            // Ease out over the fall: a short initial shove, then the body comes to rest.
            float slide = 1 - (1 - progress) * (1 - progress);
            float distance = (slide - slideProgress) * SlideDistance;
            if (distance > 0 && deathDirection.sqrMagnitude > 0)
            {
                if (Physics.SphereCast(transform.position + Vector3.up * .35f, .2f, deathDirection,
                    out RaycastHit contact, distance + .01f, ~0, QueryTriggerInteraction.Ignore))
                {
                    distance = Mathf.Min(distance, Mathf.Max(0, contact.distance - .01f));
                    deathDirection = distance > 0 ? deathDirection : Vector3.zero;
                }
                transform.position += deathDirection * distance;
            }
            slideProgress = slide;
            body.transform.rotation = view.transform.rotation * Quaternion.Euler(0, 0, fallAngle * Mathf.SmoothStep(0, 1, progress));
            if (skeleton != null)
            {
                float fall = Mathf.SmoothStep(0, 1, progress);
                skeleton.rotation = view.transform.rotation * Quaternion.Euler(0, 0, fallAngle * fall);
                skeleton.localPosition = Vector3.up * Mathf.Lerp(.35f, .15f, fall);
            }
        }
    }
}
