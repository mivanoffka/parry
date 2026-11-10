using UnityEngine;

namespace Parry
{
    // Reusable, world-space emitters: one per fighter, no Instantiate/Destroy on each parry.
    public sealed class ParrySparks : MonoBehaviour
    {
        [SerializeField, Range(8, 80)] int sparkCount = 42;
        [SerializeField] float speed = 7;
        ParticleSystem sparks, flare;
        Light flashLight;
        float flashUntil;
        float flashPeak, flashDuration;
        static Material sparkMaterial;
        public static Color ImpactColor(bool parry) =>
            parry ? new Color(1, .46f, .12f) : new Color(.85f, .28f, .035f);

        void Awake()
        {
            if (sparkMaterial == null)
            {
                sparkMaterial = new Material(Resources.Load<Shader>("Combat/MetalSparks")) { name = "HDR metallic sparks" };
                var glow = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { name = "Soft spark core", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[64 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31;
                    pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Exp(-radius * radius * 5) * Mathf.Clamp01(1 - radius));
                }
                glow.SetPixels(pixels); glow.Apply();
                sparkMaterial.SetTexture("_MainTex", glow);
                sparkMaterial.SetFloat("_Intensity", 16);
            }
            sparks = CreateEmitter("Metal fragments", true);
            flare = CreateEmitter("White-hot impact flash", false);
            var lightObject = new GameObject("Parry flash light");
            lightObject.transform.SetParent(transform, false);
            flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.renderMode = LightRenderMode.ForcePixel;
            flashLight.color = ImpactColor(true);
            flashLight.range = 5;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        ParticleSystem CreateEmitter(string label, bool streaks)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            // Start inactive so AddComponent cannot start the default emitter for one frame.
            go.SetActive(false);
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = streaks ? 256 : 16;
            main.startSpeed = 0;
            main.startLifetime = streaks ? .5f : .12f;
            main.startSize = streaks ? .045f : .8f;
            main.gravityModifier = streaks ? .7f : 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] {
                new GradientColorKey(Color.white, 0),
                new GradientColorKey(new Color(1, .9f, .7f), .35f),
                new GradientColorKey(new Color(1, .75f, .4f), 1)
            }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .3f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, streaks ? .1f : 1.7f));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkMaterial;
            renderer.renderMode = streaks ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = streaks ? 2.5f : 1;
            renderer.velocityScale = streaks ? .055f : 0;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            return particles;
        }

        public void Burst(Vector3 contact, Vector3 attackDirection, bool parry = true)
        {
            // Keep individual block fragments HDR-bright for bloom; reduce the burst's size and
            // particle count instead of dimming each spark into a flat red fleck.
            Color impactColor = ImpactColor(parry);
            var camera = Camera.main;
            Vector3 right = camera.transform.right, up = camera.transform.up;
            // Offset the hot core slightly toward the camera, clear of the sprite planes.
            contact -= camera.transform.forward * .18f;
            for (int i = 0; i < (parry ? sparkCount : sparkCount / 2); i++)
            {
                float angle = Random.Range(0, Mathf.PI * 2);
                Vector3 spread = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var particle = new ParticleSystem.EmitParams
                {
                    position = contact,
                    velocity = (spread + attackDirection * Random.Range(-.18f, .32f)).normalized
                        * Random.Range(speed * .45f, speed) * (parry ? 1 : .7f),
                    startLifetime = parry ? Random.Range(.22f, .6f) : Random.Range(.2f, .42f),
                    startSize = parry ? Random.Range(.025f, .06f) : Random.Range(.035f, .065f),
                    startColor = impactColor
                };
                sparks.Emit(particle, 1);
            }
            flare.Emit(new ParticleSystem.EmitParams { position = contact, startLifetime = parry ? .16f : .12f, startSize = parry ? 1.5f : .6f, startColor = impactColor }, 1);
            // Emit() works on a stopped system; Play keeps its particle simulation advancing.
            if (!sparks.isPlaying) sparks.Play();
            if (!flare.isPlaying) flare.Play();
            flashLight.transform.position = contact - camera.transform.forward * .8f + Vector3.up * .2f;
            flashPeak = parry ? 18 : 5.5f;
            flashDuration = parry ? .18f : .13f;
            flashLight.range = parry ? 5 : 3.5f;
            flashLight.color = impactColor;
            flashLight.intensity = flashPeak;
            flashLight.enabled = true;
            flashUntil = Time.time + flashDuration;
        }
        void Update()
        {
            if (!flashLight.enabled) return;
            float remaining = flashUntil - Time.time;
            flashLight.intensity = flashPeak * Mathf.Clamp01(remaining / flashDuration);
            if (remaining <= 0) flashLight.enabled = false;
        }
        void OnDisable()
        {
            if (flashLight != null) flashLight.enabled = false;
            if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (flare != null) flare.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
