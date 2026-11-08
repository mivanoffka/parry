using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Parry
{
    public sealed class ArenaDemo : MonoBehaviour
    {
        [SerializeField] PlayerMotor player;
        [SerializeField] EnemyBrain enemyFighter;
        [SerializeField] Camera view;
        [SerializeField] Volume feedbackVolume;
        [Header("Combat settings")]
        [Tooltip("Enable 360-degree attacks for both the player and enemy.")]
        [SerializeField] bool circularAttacks;
        [Tooltip("Base attack radius in world units for both fighters, including their attack indicators.")]
        [SerializeField, Range(.5f, 8f)] float baseAttackRadius = 2.5f;
        public float BaseAttackRadius => Mathf.Clamp(baseAttackRadius, .5f, 8f);
        public bool CircularAttacks { get => circularAttacks; set => circularAttacks = value; }
        Vector3 cameraVelocity;
        Vector3 cameraPosition;
        VolumeProfile feedbackProfile;
        ColorAdjustments healthFilter;
        LensDistortion concentrationDistortion;
        ChromaticAberration concentrationDoubleVision;
        float healthEffect, concentrationEffect;
        readonly Vector3 cameraOffset = new Vector3(0, 13, -16);
        GUIStyle title, label;
        void Start()
        {
            PracticeTarget.ResetCounter();
            CombatAudio.Initialize(transform);
            cameraPosition = view.transform.position;
            feedbackProfile = feedbackVolume.profile;
            feedbackProfile.TryGet(out healthFilter);
            feedbackProfile.TryGet(out concentrationDistortion);
            feedbackProfile.TryGet(out concentrationDoubleVision);
        }

        public void BuildScene()
        {
            PracticeTarget.ResetCounter();
            view = Camera.main;
            view.orthographic = true; view.orthographicSize = 7;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.085f, .095f, .12f);
            view.transform.rotation = Quaternion.Euler(39, 0, 0);
            view.transform.position = cameraOffset;
            cameraPosition = cameraOffset;
            view.transparencySortMode = TransparencySortMode.CustomAxis;
            view.transparencySortAxis = view.transform.forward;
            view.allowHDR = true;
            SetupScreenFeedback();
            SetupLighting();
            Ground();
            var hero = new GameObject("Player — sprite in 3D");
            hero.transform.SetParent(transform);
            var capsule = hero.AddComponent<CharacterController>();
            capsule.height = 1.4f; capsule.radius = .32f; capsule.center = Vector3.up * .7f;
            capsule.stepOffset = .15f; capsule.minMoveDistance = 0;
            DemoArt.Billboard("Character sprite", hero.transform, DemoArt.Character(false), Vector3.up * .85f);
            player = hero.AddComponent<PlayerMotor>();
            DemoArt.Shadow(hero.transform);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2 / 5;
                var target = new GameObject("Training dummy " + (i + 1)); target.transform.SetParent(transform);
                target.transform.position = new Vector3(Mathf.Cos(a) * 4, 0, Mathf.Sin(a) * 3.5f + 1);
                var collider = target.AddComponent<CapsuleCollider>(); collider.center = Vector3.up * .75f;
                collider.height = 1.5f; collider.radius = .35f; collider.isTrigger = true;
                DemoArt.Billboard("Dummy sprite", target.transform, DemoArt.Character(true), Vector3.up * .85f);
                target.AddComponent<PracticeTarget>(); DemoArt.Shadow(target.transform);
            }
            var enemy = new GameObject("Enemy — melee pursuer");
            enemy.transform.SetParent(transform);
            enemy.transform.position = new Vector3(0, 0, 6);
            var enemyCollider = enemy.AddComponent<CapsuleCollider>();
            enemyCollider.center = Vector3.up * .75f;
            enemyCollider.height = 1.5f;
            enemyCollider.radius = .35f;
            enemyCollider.isTrigger = true;
            var enemySprite = DemoArt.Billboard("Enemy sprite", enemy.transform, DemoArt.Character(false), Vector3.up * .85f);
            enemySprite.color = new Color(1, .35f, .25f);
            enemy.AddComponent<PracticeTarget>();
            DemoArt.Shadow(enemy.transform);
            enemyFighter = enemy.AddComponent<EnemyBrain>();
            enemyFighter.Initialize(player);
        }
        void SetupLighting()
        {
            var key = GameObject.Find("Directional Light");
            if (key != null && key.TryGetComponent<Light>(out var light))
            {
                light.intensity = .85f;
                light.color = new Color(1, .94f, .84f);
            }
            AddFillLight("Warm arena light", new Vector3(-4, 3, -3), new Color(1, .61f, .3f), 12, 10);
            AddFillLight("Cool arena light", new Vector3(4, 2.5f, -1), new Color(.32f, .64f, 1), 10, 9);
        }
        void SetupScreenFeedback()
        {
            var feedback = new GameObject("Player screen feedback");
            feedback.transform.SetParent(transform, false);
            var volume = feedback.AddComponent<Volume>();
            feedbackVolume = volume;
            volume.isGlobal = true;
            volume.priority = 100;
            feedbackProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = feedbackProfile;
            healthFilter = feedbackProfile.Add<ColorAdjustments>();
            healthFilter.saturation.Override(0);
            concentrationDistortion = feedbackProfile.Add<LensDistortion>();
            concentrationDistortion.intensity.Override(0);
            concentrationDistortion.center.Override(new Vector2(.5f, .5f));
            concentrationDistortion.xMultiplier.Override(1);
            concentrationDistortion.yMultiplier.Override(.65f);
            concentrationDistortion.scale.Override(1);
            concentrationDoubleVision = feedbackProfile.Add<ChromaticAberration>();
            concentrationDoubleVision.intensity.Override(0);
            view.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }
        void UpdateScreenFeedback()
        {
            float missingHealth = 1 - Mathf.Clamp01((float)player.Health / player.MaxHealth);
            float pressure = player.IsAlive ? Mathf.Clamp01(player.Guard.Pressure / PostureState.Maximum) : 0;
            if (player.IsAlive && player.Health == player.MaxHealth && pressure == 0)
                healthEffect = concentrationEffect = 0;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * 7);
            healthEffect = Mathf.Lerp(healthEffect, missingHealth, blend);
            concentrationEffect = Mathf.Lerp(concentrationEffect, pressure * pressure, blend);
            healthFilter.saturation.value = -100 * healthEffect;
            float time = Time.time;
            float pulse = .85f + Mathf.Sin(time * 5) * .15f;
            concentrationDistortion.intensity.value = concentrationEffect * pulse * -.35f;
            concentrationDistortion.scale.value = 1 + concentrationEffect * .08f;
            concentrationDoubleVision.intensity.value = concentrationEffect * pulse;
            Vector3 shake = view.transform.right * (Mathf.PerlinNoise(time * 23, 0) * 2 - 1)
                + view.transform.up * (Mathf.PerlinNoise(0, time * 27) * 2 - 1);
            view.transform.position = cameraPosition + shake * (concentrationEffect * .12f);
        }
        void OnDestroy()
        {
            if (feedbackProfile == null || (feedbackVolume != null && feedbackProfile == feedbackVolume.sharedProfile)) return;
            foreach (var component in feedbackProfile.components) Destroy(component);
            Destroy(feedbackProfile);
        }
        void AddFillLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.position = position;
            var light = go.AddComponent<Light>(); light.type = LightType.Point;
            light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.None;
        }
        void Ground()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var floorMaterial = new Material(shader); floorMaterial.color = new Color(.22f, .27f, .25f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Flat XZ arena";
            floor.transform.SetParent(transform); floor.transform.position = new Vector3(0, -.2f, 0);
            floor.transform.localScale = new Vector3(24, .4f, 20); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var wallMaterial = new Material(shader); wallMaterial.color = new Color(.13f, .17f, .18f);
            Wall(new Vector3(-12, .5f, 0), new Vector3(.5f, 1, 20), wallMaterial);
            Wall(new Vector3(12, .5f, 0), new Vector3(.5f, 1, 20), wallMaterial);
            Wall(new Vector3(0, .5f, 10), new Vector3(24, 1, .5f), wallMaterial);
            Wall(new Vector3(0, .5f, -10), new Vector3(24, 1, .5f), wallMaterial);
            for (int i = -5; i <= 5; i++)
            {
                var line = DemoArt.Line("Floor grid", transform, new Color(.32f, .38f, .32f, .3f), .018f);
                line.positionCount = 2; line.SetPosition(0, new Vector3(i * 2, .012f, -9.7f)); line.SetPosition(1, new Vector3(i * 2, .012f, 9.7f));
            }
            for (int i = -4; i <= 4; i++)
            {
                var line = DemoArt.Line("Floor grid", transform, new Color(.32f, .38f, .32f, .3f), .018f);
                line.positionCount = 2; line.SetPosition(0, new Vector3(-11.7f, .012f, i * 2)); line.SetPosition(1, new Vector3(11.7f, .012f, i * 2));
            }
            Wall(new Vector3(-6, .45f, 3), new Vector3(1.5f, .9f, 1.5f), wallMaterial);
            Wall(new Vector3(6, .45f, -3), new Vector3(1.5f, .9f, 1.5f), wallMaterial);
        }
        void Wall(Vector3 position, Vector3 scale, Material material)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Solid obstacle";
            wall.transform.SetParent(transform); wall.transform.position = position; wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }
        void LateUpdate()
        {
            if (player == null) return;
            cameraPosition = Vector3.SmoothDamp(cameraPosition, player.transform.position + cameraOffset, ref cameraVelocity, .16f);
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                player.Respawn();
            }
            UpdateScreenFeedback();
        }
        void OnGUI()
        {
            if (player == null) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
                title.normal.textColor = new Color(.98f, .85f, .62f);
                label = new GUIStyle(GUI.skin.label) { fontSize = 15 }; label.normal.textColor = new Color(.85f, .88f, .83f);
            }
            GUI.Box(new Rect(18, 18, 475, 148), GUIContent.none);
            GUI.Label(new Rect(34, 27, 440, 35), "PARRY / TRAINING GROUNDS", title);
            GUI.Label(new Rect(34, 65, 440, 25), "WASD / Arrows  •  Mouse aim  •  LMB / J attack", label);
            GUI.Label(new Rect(34, 91, 440, 25), "Hold RMB / K / LB: block  •  Tap before impact: parry", label);
            GUI.Label(new Rect(34, 117, 440, 25), "Space: dodge  •  R: revive / heal  •  Avoid the red sector", label);
            DrawFighterHUD(new Rect(18, Screen.height - 125, 345, 110), "PLAYER", player.Health, player.MaxHealth, player.Guard);
            if (enemyFighter != null)
                DrawFighterHUD(new Rect(Screen.width >= 870 ? Screen.width - 355 : 18, Screen.width >= 870 ? 18 : 180, 337, 137), "ENEMY", enemyFighter.Health.Health, enemyFighter.Health.MaxHealth, enemyFighter.Guard, enemyFighter.TacticalStatus);
            GUI.Label(new Rect(380, Screen.height - 45, 550, 28),
                $"{(!player.IsAlive ? "DEFEATED — press R" : player.Guard.Broken ? "GUARD BROKEN" : player.Rolling ? "ROLL" : player.Guard.Blocking ? "BLOCK" : player.Attacking ? "ATTACK" : "READY")}   /   Roll: {Mathf.RoundToInt(player.RollReady * 100)}%", label);
        }

        void DrawFighterHUD(Rect rect, string name, int health, int maxHealth, CombatGuard guard, string status = null)
        {
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 25), $"{name}   HP {health}/{maxHealth}   {guard.Feedback}", label);
            GUI.Label(new Rect(rect.x + 12, rect.y + 36, rect.width - 24, 25),
                $"STA {Mathf.RoundToInt(guard.RemainingEndurance)}/{guard.EndurancePoints}   Load: {Mathf.RoundToInt(guard.Pressure)}%", label);
            var bar = new Rect(rect.x + 12, rect.y + 71, rect.width - 24, 13);
            Color previous = GUI.color;
            GUI.color = new Color(.12f, .12f, .14f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = guard.Broken ? Color.red : new Color(1, .65f, .2f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * guard.Pressure / PostureState.Maximum, bar.height), Texture2D.whiteTexture);
            GUI.color = previous;
            if (status != null) GUI.Label(new Rect(rect.x + 12, rect.y + 94, rect.width - 24, 25), status, label);
        }
    }
}
