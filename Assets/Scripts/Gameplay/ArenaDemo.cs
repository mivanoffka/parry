using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Parry
{
    [DefaultExecutionOrder(-100)]
    public sealed class ArenaDemo : MonoBehaviour
    {
        [SerializeField] PlayerMotor player;
        [SerializeField] EnemyBrain enemyFighter;
        [SerializeField] Camera view;
        [SerializeField] Volume feedbackVolume;
        [Header("Combat settings")]
        [Tooltip("Show attack range indicators for the player and enemies.")]
        [SerializeField] bool showAttackRangeIndicators;
        public bool ShowAttackRangeIndicators { get => showAttackRangeIndicators; set => showAttackRangeIndicators = value; }
        [Tooltip("Enable 360-degree attacks for both the player and enemy.")]
        [SerializeField] bool circularAttacks;
        [Tooltip("Base attack radius in world units for both fighters, including their attack indicators.")]
        [SerializeField, Range(.5f, 8f)] float baseAttackRadius = 2.5f;
        public float BaseAttackRadius => Mathf.Clamp(baseAttackRadius, .5f, 8f);
        public bool CircularAttacks { get => circularAttacks; set => circularAttacks = value; }
        Vector3 cameraVelocity;
        float targetCameraSize;
        float currentCameraSize;
        float initialCameraSize;
        float zoomVelocity;
        Vector3 cameraPosition, cameraFocus;
        [Header("Zoom tilt")]
        [SerializeField, Range(5, 80)] float closeZoomPitch = 28;
        [SerializeField, Range(5, 80)] float distantZoomPitch = 58;
        float normalCameraPitch;
        VolumeProfile feedbackProfile;
        ColorAdjustments healthFilter;
        LensDistortion concentrationDistortion;
        ChromaticAberration concentrationDoubleVision;
        Vignette smokeVignette;
        float smokeEffect;
        float healthEffect, concentrationEffect;
        readonly Vector3 cameraOffset = new Vector3(0, 13, -16);
        GUIStyle controlsLabel;
        void Start()
        {
            PracticeTarget.ResetCounter();
            CombatAudio.Initialize(transform);
            cameraPosition = view.transform.position;
            cameraFocus = player.transform.position;
            normalCameraPitch = view.transform.eulerAngles.x;
            targetCameraSize = view.orthographicSize;
            initialCameraSize = currentCameraSize = targetCameraSize;
            feedbackProfile = feedbackVolume.profile;
            feedbackProfile.TryGet(out healthFilter);
            feedbackProfile.TryGet(out concentrationDistortion);
            feedbackProfile.TryGet(out concentrationDoubleVision);
            if (!feedbackProfile.TryGet(out smokeVignette)) smokeVignette = feedbackProfile.Add<Vignette>();
            smokeVignette.color.Override(Color.black);
            smokeVignette.intensity.Override(0);
            smokeVignette.smoothness.Override(.9f);
            healthFilter.postExposure.Override(0);
        }

        public void BuildScene()
        {
            PracticeTarget.ResetCounter();
            view = Camera.main;
            view.orthographic = true; view.orthographicSize = 7;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.035f, .045f, .065f);
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
            for (int i = 0; i < 4; i++)
            {
                var prefab = Resources.Load<GameObject>("Combat/Enemy");
                if (prefab != null)
                {
#if UNITY_EDITOR
                    var instance = Application.isPlaying ? Instantiate(prefab, transform)
                        : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
#else
                    var instance = Instantiate(prefab, transform);
#endif
                    instance.name = "Enemy — melee pursuer " + (i + 1);
                    instance.transform.position = new Vector3((i % 2 == 0 ? -1 : 1) * (i < 2 ? 2 : 4), 0, i < 2 ? 6 : 3);
                    var brain = instance.GetComponent<EnemyBrain>();
                    brain.Initialize(player);
                    if (i == 0) enemyFighter = brain;
                    continue;
                }
                var enemy = new GameObject("Enemy — melee pursuer " + (i + 1));
                enemy.transform.SetParent(transform);
                enemy.transform.position = new Vector3((i % 2 == 0 ? -1 : 1) * (i < 2 ? 2 : 4), 0, i < 2 ? 6 : 3);
                var enemyCollider = enemy.AddComponent<CapsuleCollider>();
                enemyCollider.center = Vector3.up * .75f;
                enemyCollider.height = 1.5f;
                enemyCollider.radius = .35f;
                enemyCollider.isTrigger = true;
                var enemySprite = DemoArt.Billboard("Enemy sprite", enemy.transform, DemoArt.Character(false), Vector3.up * .85f);
                enemySprite.color = new Color(1, .35f, .25f);
                enemy.AddComponent<PracticeTarget>();
                DemoArt.Shadow(enemy.transform);
                var fighter = enemy.AddComponent<EnemyBrain>();
                fighter.Initialize(player);
                if (i == 0) enemyFighter = fighter;
            }
        }
        void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.075f, .09f, .12f);
            var key = GameObject.Find("Directional Light");
            if (key != null && key.TryGetComponent<Light>(out var light))
            {
                light.intensity = .28f;
                light.color = new Color(1, .94f, .84f);
            }
            AddFillLight("Warm arena light", new Vector3(-4, 3, -3), new Color(1, .61f, .3f), 3.5f, 10);
            AddFillLight("Cool arena light", new Vector3(4, 2.5f, -1), new Color(.32f, .64f, 1), 3, 9);
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
            smokeEffect = Mathf.Lerp(smokeEffect, player.IsAlive ? SmokeCloud.Exposure(player.transform.position) : 0, blend);
            healthFilter.postExposure.value = -1.5f * smokeEffect;
            smokeVignette.intensity.value = .72f * smokeEffect;
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
            // Level surfaces receive the same real-time clash lights as the fighters.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var floorMaterial = new Material(shader); floorMaterial.color = new Color(.22f, .27f, .25f);
            floorMaterial.SetFloat("_Smoothness", .18f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Flat XZ arena";
            floor.transform.SetParent(transform); floor.transform.position = new Vector3(0, -.2f, 0);
            floor.transform.localScale = new Vector3(24, .4f, 20); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var wallMaterial = new Material(shader); wallMaterial.color = new Color(.13f, .17f, .18f);
            wallMaterial.SetFloat("_Smoothness", .12f);
            Wall(new Vector3(-12, .5f, 0), new Vector3(.5f, 1, 20), wallMaterial);
            Wall(new Vector3(12, .5f, 0), new Vector3(.5f, 1, 20), wallMaterial);
            Wall(new Vector3(0, .5f, 10), new Vector3(24, 1, .5f), wallMaterial);
            Wall(new Vector3(0, .5f, -10), new Vector3(24, 1, .5f), wallMaterial);
            for (int i = -5; i <= 5; i++)
            {
                var line = DemoArt.Line("Floor grid", transform, new Color(.18f, .22f, .2f, .22f), .018f);
                line.positionCount = 2; line.SetPosition(0, new Vector3(i * 2, .012f, -9.7f)); line.SetPosition(1, new Vector3(i * 2, .012f, 9.7f));
            }
            for (int i = -4; i <= 4; i++)
            {
                var line = DemoArt.Line("Floor grid", transform, new Color(.18f, .22f, .2f, .22f), .018f);
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
            if (Mouse.current != null && Application.isFocused)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                // Input System 1.20 normalizes wheel ticks unless native ranges are requested.
                if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
                    && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
                    scroll /= 120f;
                targetCameraSize = Mathf.Clamp(targetCameraSize
                    - scroll * .25f, 3f, 14f);
            }
            currentCameraSize = Mathf.SmoothDamp(currentCameraSize,
                targetCameraSize, ref zoomVelocity, .4f);
            // Perspective cameras zoom by changing follow distance; orthographic cameras change size.
            float pitch = CameraZoom.Pitch(currentCameraSize, initialCameraSize, 14,
                normalCameraPitch, closeZoomPitch, distantZoomPitch);
            Quaternion cameraRotation = Quaternion.Euler(pitch, 0, 0);
            Vector3 followOffset = -(cameraRotation * Vector3.forward) * cameraOffset.magnitude;
            if (view.orthographic) view.orthographicSize = currentCameraSize;
            else followOffset *= currentCameraSize / initialCameraSize;
            cameraFocus = Vector3.SmoothDamp(cameraFocus, player.transform.position, ref cameraVelocity, .16f);
            cameraPosition = cameraFocus + followOffset;
            view.transform.rotation = cameraRotation;
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                player.Respawn();
            }
            UpdateScreenFeedback();
        }
        void OnGUI()
        {
            if (player == null) return;
            if (controlsLabel == null)
            {
                controlsLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                controlsLabel.normal.textColor = new Color(.85f, .88f, .83f);
            }
            GUI.Box(new Rect(12, 12, 355, 88), GUIContent.none);
            GUI.Label(new Rect(22, 19, 335, 18), "WASD / Arrows: move  •  Mouse: aim  •  LMB / J: attack", controlsLabel);
            GUI.Label(new Rect(22, 37, 335, 18), "Hold RMB / K / LB: block  •  Tap before impact: parry", controlsLabel);
            GUI.Label(new Rect(22, 55, 335, 18), "Space: dodge  •  R: revive / heal  •  Scroll: zoom", controlsLabel);
            GUI.Label(new Rect(22, 73, 335, 18), "1: slash  •  2: lunge  •  3: sweep  •  Alt: relax", controlsLabel);
            GUI.Box(new Rect(12, 194, 420, 28), "Tab / MMB: target lock  •  Q / E: switch  •  Esc: release");
            GUI.Label(new Rect(22, 224, 500, 24), "Lunge feint: aim before release (hold Shift to override target lock)", controlsLabel);
        }
    }
}
