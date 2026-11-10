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

        public static SmokeCloud Deploy(Vector3 position, float radius = 8, float duration = 8)
        {
            var cloud = new GameObject("Smoke cover").AddComponent<SmokeCloud>();
            cloud.transform.position = position;
            cloud.radius = Mathf.Max(.5f, radius);
            cloud.duration = Mathf.Max(2, duration);
            cloud.deployedAt = Time.time;
            clouds.Add(cloud);
            cloud.BuildParticles();
            return cloud;
        }

        public static float Exposure(Vector3 position)
        {
            float result = 0;
            foreach (var cloud in clouds)
            {
                Vector3 delta = position - cloud.transform.position;
                float distance = new Vector2(delta.x, delta.z).magnitude;
                if (distance >= cloud.radius || Mathf.Abs(delta.y) > 2.5f) continue;
                if (Physics.Linecast(cloud.transform.position + Vector3.up * .8f,
                    position + Vector3.up * .8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                result = Mathf.Max(result, cloud.Density * Mathf.Clamp01((cloud.radius - distance) / .65f));
            }
            return result;
        }

        public static bool Obscures(Vector3 from, Vector3 to)
        {
            // Fighters can still notice an opponent within arm's reach in the smoke.
            if ((to - from).sqrMagnitude < 1.2f * 1.2f) return false;
            foreach (var cloud in clouds)
            {
                if (cloud.Density < .35f) continue;
                Vector3 a = from - cloud.transform.position, segment = to - from;
                a.y = segment.y = 0;
                float t = segment.sqrMagnitude > .001f ? Mathf.Clamp01(-Vector3.Dot(a, segment) / segment.sqrMagnitude) : 0;
                if ((a + segment * t).sqrMagnitude < cloud.radius * cloud.radius) return true;
            }
            return false;
        }

        void BuildParticles()
        {
            material = new Material(Resources.Load<Shader>("Combat/Smoke"));
            var particles = gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.duration = duration;
            main.startLifetime = duration; main.startSpeed = 0; main.maxParticles = 150;
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
            float puffScale = radius / 3f;
            for (int i = 0; i < 140; i++)
            {
                Vector2 offset = Random.insideUnitCircle * (radius - .65f * puffScale);
                particles.Emit(new ParticleSystem.EmitParams {
                    position = new Vector3(offset.x, Random.Range(.35f, 1.9f), offset.y),
                    startSize = Random.Range(1, 1.3f) * puffScale, rotation = Random.Range(0, Mathf.PI * 2),
                    startLifetime = duration, startColor = Color.white
                }, 1);
            }
            particles.Play();
        }
        void Update() { if (Time.time - deployedAt >= duration) Destroy(gameObject); }
        void OnDisable() { clouds.Remove(this); }
        void OnDestroy() { if (material != null) Destroy(material); }
    }

    public sealed class SmokeGrenade : MonoBehaviour
    {
        Vector3 start, end;
        float thrownAt, radius, duration;
        Material material;
        public static void Throw(Vector3 from, Vector3 to, float radius, float duration)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Thrown smoke grenade";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = from; go.transform.localScale = Vector3.one * .18f;
            var grenade = go.AddComponent<SmokeGrenade>();
            grenade.start = from; grenade.end = to; grenade.radius = radius; grenade.duration = duration;
            grenade.thrownAt = Time.time;
            grenade.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grenade.material.color = new Color(.16f, .2f, .16f);
            go.GetComponent<Renderer>().sharedMaterial = grenade.material;
        }
        void Update()
        {
            float t = Mathf.Clamp01((Time.time - thrownAt) / .45f);
            transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.2f);
            if (t < 1) return;
            SmokeCloud.Deploy(end, radius, duration);
            Destroy(gameObject);
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
