using System.Collections.Generic;
using UnityEngine;

namespace Parry
{
    // Smoke obstructs perception, never weapon hits or physical movement.
    public sealed class SmokeCloud : MonoBehaviour
    {
        static readonly List<SmokeCloud> clouds = new List<SmokeCloud>();
        float radius, duration, deployedAt;
        Material material;
        public float Density => Mathf.Min(Mathf.Clamp01((Time.time - deployedAt) / .4f),
            Mathf.Clamp01((duration - (Time.time - deployedAt)) / 1.5f));

        public static SmokeCloud Deploy(Vector3 position, float radius = 8, float duration = 8, int units = 200)
        {
            var cloud = new GameObject("Smoke cover").AddComponent<SmokeCloud>();
            cloud.transform.position = position;
            cloud.radius = Mathf.Max(.5f, radius);
            cloud.duration = Mathf.Max(2, duration);
            cloud.deployedAt = Time.time;
            clouds.Add(cloud);
            cloud.unitLimit = Mathf.Clamp(units, 1, 1000);
            cloud.BuildParticles();
            cloud.visited.Add(Vector2Int.zero);
            cloud.frontier.Enqueue(Vector2Int.zero);
            cloud.Spread();
            return cloud;
        }

        static PlayerMotor viewer;
        public static bool PlayerObscured
        {
            get
            {
                if (viewer == null) viewer = Object.FindAnyObjectByType<PlayerMotor>();
                return viewer != null && viewer.IsAlive && Exposure(viewer.transform.position) > .01f;
            }
        }
        const float CellSize = 1f;
        readonly List<Vector3> cells = new List<Vector3>();
        readonly Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        static readonly Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        int unitLimit;
        float nextSpread;
        ParticleSystem particles;

        // Only solid scenery stops smoke; fighter controllers and hitboxes do not.
        public static bool Blocked(Vector3 from, Vector3 to, float clearance = .12f)
        {
            foreach (var hit in Physics.SphereCastAll(from + Vector3.up * .65f, clearance,
                (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<CombatGuard>() == null) return true;
            foreach (var hit in Physics.OverlapSphere(to + Vector3.up * .65f, clearance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.GetComponentInParent<CombatGuard>() == null) return true;
            return false;
        }

        void Spread()
        {
            int budget = 12;
            while (frontier.Count > 0 && cells.Count < unitLimit && budget-- > 0)
            {
                var cell = frontier.Dequeue();
                Vector3 position = transform.position + new Vector3(cell.x, 0, cell.y) * CellSize;
                // Recheck paths when a door has closed since the cell was queued.
                if (cell != Vector2Int.zero && !cells.Exists(p => Vector3.Distance(p, position) < 1.01f && !Blocked(p, position))) continue;
                cells.Add(position);
                // Keep logical coverage stable while breaking up the visible lattice.
                Vector2 jitter = Random.insideUnitCircle * .28f;
                Vector3 puffPosition = position + new Vector3(jitter.x, 0, jitter.y);
                if (Blocked(position, puffPosition)) puffPosition = position;
                float size = Random.Range(1.5f, 2.05f);
                float shade = Random.Range(.87f, 1f);
                particles.Emit(new ParticleSystem.EmitParams {
                    position = puffPosition - transform.position + Vector3.up * Random.Range(.65f, 1.2f),
                    startSize3D = new Vector3(size * Random.Range(.85f, 1.15f), size * Random.Range(.85f, 1.15f), size),
                    startLifetime = Mathf.Max(.1f, duration - (Time.time - deployedAt)),
                    startColor = new Color(shade, shade, shade, Random.Range(.85f, 1f)),
                    rotation = Random.Range(0, 360)
                }, 1);
                foreach (var direction in directions)
                {
                    var next = cell + direction;
                    if (next.magnitude * CellSize > radius || visited.Contains(next)) continue;
                    var destination = transform.position + new Vector3(next.x, 0, next.y) * CellSize;
                    if (Blocked(position, destination)) continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }
        }

        public static float Exposure(Vector3 position)
        {
            float result = 0;
            foreach (var cloud in clouds)
            {
                if (Mathf.Abs(position.y - cloud.transform.position.y) > 2.5f) continue;
                foreach (var cell in cloud.cells)
                {
                    Vector3 delta = position - cell; delta.y = 0;
                    if (delta.sqrMagnitude > .85f * .85f || Blocked(cell, position, .02f)) continue;
                    result = Mathf.Max(result, cloud.Density);
                }
            }
            return result;
        }

        public static bool Obscures(Vector3 from, Vector3 to)
        {
            if (Exposure(to) > .01f) return true;
            if (Exposure(from) > .01f && Vector3.Distance(from, to) > 1.2f) return true;
            foreach (var cloud in clouds)
            {
                if (cloud.Density < .01f) continue;
                foreach (var cell in cloud.cells)
                {
                    Vector3 a = from - cell, segment = to - from;
                    a.y = segment.y = 0;
                    float t = segment.sqrMagnitude > .001f ? Mathf.Clamp01(-Vector3.Dot(a, segment) / segment.sqrMagnitude) : 0;
                    if ((a + segment * t).sqrMagnitude < .72f * .72f) return true;
                }
            }
            return false;
        }

        public static Vector3 EscapeDirection(Vector3 position)
        {
            var queue = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int> { Vector2Int.zero };
            var firstSteps = new Dictionary<Vector2Int, Vector3>();
            queue.Enqueue(Vector2Int.zero);
            while (queue.Count > 0 && seen.Count < 1600)
            {
                var cell = queue.Dequeue();
                Vector3 point = position + new Vector3(cell.x, 0, cell.y);
                if (cell != Vector2Int.zero && Exposure(point) <= .01f) return firstSteps[cell];
                foreach (var d in directions)
                {
                    var next = cell + d;
                    if (seen.Contains(next) || Blocked(point, position + new Vector3(next.x, 0, next.y), .35f)) continue;
                    seen.Add(next);
                    firstSteps[next] = cell == Vector2Int.zero ? new Vector3(d.x, 0, d.y) : firstSteps[cell];
                    queue.Enqueue(next);
                }
            }
            return Vector3.zero;
        }

        void BuildParticles()
        {
            material = new Material(Resources.Load<Shader>("Combat/Smoke"));
            particles = gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.duration = duration;
            main.startLifetime = duration; main.startSpeed = 0; main.startSize3D = true; main.maxParticles = unitLimit;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.36f, .39f, .4f), 0), new GradientColorKey(new Color(.24f, .27f, .28f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.65f, .05f), new GradientAlphaKey(.65f, .8f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var rotation = particles.rotationOverLifetime; rotation.enabled = true; rotation.z = .18f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            particles.Play();
        }
        void Update()
        {
            if (Time.time - deployedAt >= duration) { Destroy(gameObject); return; }
            if (Time.time >= nextSpread) { nextSpread = Time.time + .08f; Spread(); }
        }
        void OnDisable() { clouds.Remove(this); }
        void OnDestroy() { if (material != null) Destroy(material); }
    }

    public sealed class SmokeGrenade : MonoBehaviour
    {
        Vector3 start, end;
        float thrownAt, radius, duration;
        int units;
        Vector3 previous;
        Material material;
        public static void Throw(Vector3 from, Vector3 to, float radius, float duration, int units = 200)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Thrown smoke grenade";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = from; go.transform.localScale = Vector3.one * .18f;
            var grenade = go.AddComponent<SmokeGrenade>();
            grenade.start = from; grenade.end = to; grenade.radius = radius; grenade.duration = duration;
            grenade.thrownAt = Time.time;
            grenade.units = units; grenade.previous = from;
            grenade.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grenade.material.color = new Color(.16f, .2f, .16f);
            go.GetComponent<Renderer>().sharedMaterial = grenade.material;
        }
        void Update()
        {
            float t = Mathf.Clamp01((Time.time - thrownAt) / .45f);
            Vector3 next = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.2f);
            if (SmokeCloud.Blocked(new Vector3(previous.x, 0, previous.z), new Vector3(next.x, 0, next.z)))
            { end = new Vector3(previous.x, end.y, previous.z); t = 1; }
            transform.position = next; previous = next;
            if (t < 1) return;
            SmokeCloud.Deploy(end, radius, duration, units);
            Destroy(gameObject);
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
