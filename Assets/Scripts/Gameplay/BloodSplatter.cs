using UnityEngine;

namespace Parry
{
    public sealed class BloodSplatter : MonoBehaviour
    {
        const int DropletCount = 192;
        const int StainCount = 384;
        public const int ParticlesPerHit = 64;
        const float StainLifetime = 25;
        readonly SpriteRenderer[] droplets = new SpriteRenderer[DropletCount];
        readonly Vector3[] velocities = new Vector3[DropletCount];
        readonly float[] dropletUntil = new float[DropletCount];
        readonly SpriteRenderer[] stains = new SpriteRenderer[StainCount];
        readonly float[] stainUntil = new float[StainCount];
        readonly RaycastHit[] surfaceHits = new RaycastHit[32];
        int nextDroplet, nextStain;
        Camera view;

        public static BloodSplatter Create(Transform arena)
        {
            if (arena != null)
            {
                var existing = arena.GetComponentInChildren<BloodSplatter>();
                if (existing != null) return existing;
            }
            var effects = new GameObject("Blood splashes and stains");
            effects.transform.SetParent(arena, false);
            return effects.AddComponent<BloodSplatter>();
        }

        void Awake()
        {
            view = Camera.main;
            for (int index = 0; index < droplets.Length; index++) droplets[index] = CreateSprite("Blood droplet");
            for (int index = 0; index < stains.Length; index++) stains[index] = CreateSprite("Blood stain");
        }

        SpriteRenderer CreateSprite(string label)
        {
            var sprite = new GameObject(label);
            sprite.transform.SetParent(transform, false);
            var renderer = sprite.AddComponent<SpriteRenderer>();
            renderer.sprite = DemoArt.Disc();
            renderer.sharedMaterial = DemoArt.SpriteMaterial;
            renderer.enabled = false;
            return renderer;
        }

        public void Burst(Vector3 contact, Vector3 attackDirection)
        {
            Vector3 forward = Vector3.ProjectOnPlane(attackDirection, Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            for (int index = 0; index < ParticlesPerHit; index++)
            {
                int slot = nextDroplet++ % droplets.Length;
                var droplet = droplets[slot];
                droplet.transform.position = contact + Random.insideUnitSphere * .12f;
                droplet.transform.rotation = view.transform.rotation;
                float size = Random.Range(.045f, .11f);
                droplet.transform.localScale = new Vector3(size, size * Random.Range(1.2f, 2.2f), 1);
                droplet.color = new Color(Random.Range(.45f, .7f), .015f, .035f, .95f);
                droplet.enabled = true;
                velocities[slot] = forward * Random.Range(1.2f, 4.5f) + side * Random.Range(-2, 2)
                    + Vector3.up * Random.Range(1, 3);
                dropletUntil[slot] = Time.time + 1.5f;
            }
            if (TrySurfaceHit(contact, Vector3.down, 2, out RaycastHit ground))
                AddStain(ground, Random.Range(.3f, .5f));
        }

        bool TrySurfaceHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit contact)
        {
            contact = default;
            int count = Physics.RaycastNonAlloc(origin, direction, surfaceHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                var candidate = surfaceHits[index];
                if (candidate.collider.GetComponentInParent<PlayerMotor>() != null || candidate.distance >= nearest) continue;
                nearest = candidate.distance;
                contact = candidate;
                found = true;
            }
            return found;
        }

        void AddStain(RaycastHit contact, float size)
        {
            int slot = nextStain++ % stains.Length;
            var stain = stains[slot];
            stain.transform.position = contact.point + contact.normal * Random.Range(.018f, .022f);
            stain.transform.rotation = Quaternion.AngleAxis(Random.Range(0, 360), contact.normal)
                * Quaternion.FromToRotation(Vector3.forward, contact.normal);
            stain.transform.localScale = new Vector3(size, size * Random.Range(.55f, 1.1f), 1);
            stain.color = new Color(Random.Range(.24f, .4f), .008f, .018f, .85f);
            stain.enabled = true;
            stainUntil[slot] = Time.time + StainLifetime;
        }

        void Update()
        {
            for (int index = 0; index < droplets.Length; index++)
            {
                var droplet = droplets[index];
                if (!droplet.enabled) continue;
                if (Time.time >= dropletUntil[index]) { droplet.enabled = false; continue; }
                velocities[index] += Physics.gravity * Time.deltaTime;
                Vector3 displacement = velocities[index] * Time.deltaTime;
                if (displacement.sqrMagnitude > .000001f && TrySurfaceHit(droplet.transform.position,
                    displacement.normalized, displacement.magnitude, out RaycastHit contact))
                {
                    AddStain(contact, Random.Range(.08f, .23f));
                    droplet.enabled = false;
                }
                else
                {
                    droplet.transform.position += displacement;
                    droplet.transform.rotation = view.transform.rotation;
                }
            }
            for (int index = 0; index < stains.Length; index++)
            {
                var stain = stains[index];
                if (!stain.enabled) continue;
                float remaining = stainUntil[index] - Time.time;
                if (remaining <= 0) { stain.enabled = false; continue; }
                Color color = stain.color;
                color.a = .85f * Mathf.Clamp01(remaining / 5);
                stain.color = color;
            }
        }
    }
}
