using UnityEngine;

namespace Parry
{
    // Upright sword poses convey aim through hand placement and depth, not a screen-space pointer.
    public sealed class WeaponVisual : MonoBehaviour
    {
        public float WindupProgress { get; private set; }
        public float SwingProgress { get; private set; } = -1;
        public float RecoveryProgress { get; private set; }
        SpriteRenderer blade;
        float relaxationBlend;
        public float RelaxationBlend => relaxationBlend;
        static Sprite sword;
        static Material chargeMaterial;
        LineRenderer chargeHalo, chargeCore, glintHorizontal, glintVertical;
        Light chargeLight;
        public static WeaponVisual Create(Transform owner)
        {
            var go = new GameObject("Visible sword"); go.transform.SetParent(owner, false);
            return go.AddComponent<WeaponVisual>();
        }
        void Awake()
        {
            if (sword == null)
            {
                var texture = new Texture2D(16, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var pixels = new Color[16 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 16; x++)
                {
                    Color c = Color.clear;
                    if (y >= 18 && y < 61 && Mathf.Abs(x - 7.5f) <= (y > 54 ? (62 - y) * .5f : 3.5f))
                        c = x <= 7 ? new Color(.93f, .98f, 1) : new Color(.4f, .65f, .75f);
                    if (y >= 14 && y <= 18 && x >= 1 && x <= 14) c = new Color(1, .72f, .25f);
                    if (y >= 3 && y < 14 && x >= 6 && x <= 9) c = y % 3 == 0 ? new Color(.8f, .4f, .2f) : new Color(.26f, .16f, .14f);
                    if (y < 4 && x >= 5 && x <= 10) c = new Color(1, .72f, .25f);
                    pixels[y * 16 + x] = c;
                }
                texture.SetPixels(pixels); texture.Apply();
                sword = Sprite.Create(texture, new Rect(0, 0, 16, 64), new Vector2(.5f, .16f), 48);
            }
            blade = gameObject.AddComponent<SpriteRenderer>(); blade.sprite = sword;
            blade.sharedMaterial = DemoArt.LitSpriteMaterial("sword");
            if (chargeMaterial == null)
            {
                chargeMaterial = new Material(Resources.Load<Shader>("Combat/MetalSparks"))
                    { name = "Powerful attack weapon gleam" };
                chargeMaterial.SetFloat("_Intensity", 10);
            }
            chargeHalo = CreateChargeLine("Blade charge halo", .16f);
            chargeCore = CreateChargeLine("White-hot blade edge", .035f);
            glintHorizontal = CreateChargeLine("Charge glint horizontal", .035f);
            glintVertical = CreateChargeLine("Charge glint vertical", .025f);
            var lightObject = new GameObject("Weapon charge light");
            lightObject.transform.SetParent(transform, false);
            chargeLight = lightObject.AddComponent<Light>();
            chargeLight.type = LightType.Point;
            chargeLight.color = Color.white;
            chargeLight.range = 3;
            chargeLight.shadows = LightShadows.None;
            chargeLight.enabled = false;
        }
        LineRenderer CreateChargeLine(string label, float width)
        {
            var line = DemoArt.Line(label, transform, Color.white, width);
            line.sharedMaterial = chargeMaterial;
            line.positionCount = 2;
            line.numCapVertices = 6;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        // Called after Pose so the gleam follows the actual blade in every aim direction.
        public void ChargeGlint(float progress, bool preparing)
        {
            bool visible = blade.enabled && preparing;
            chargeHalo.enabled = chargeCore.enabled = glintHorizontal.enabled = glintVertical.enabled = visible;
            chargeLight.enabled = false;
            if (!visible) return;
            var camera = Camera.main;
            // One short pulse near the start of the windup; never repeat while holding.
            float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .07f, progress));
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .4f, progress));
            float pulse = rise * fade;
            chargeHalo.enabled = chargeCore.enabled = pulse > .001f;
            Vector3 offset = -camera.transform.forward * .035f;
            Vector3 root = transform.TransformPoint(new Vector3(0, (19 - 64 * .16f) / 48, 0)) + offset;
            Vector3 tip = transform.TransformPoint(new Vector3(0, (61 - 64 * .16f) / 48, 0)) + offset;
            chargeHalo.SetPosition(0, root); chargeHalo.SetPosition(1, tip);
            chargeCore.SetPosition(0, root); chargeCore.SetPosition(1, tip);
            chargeHalo.startColor = chargeHalo.endColor = new Color(1, 1, 1, pulse * .04f);
            chargeCore.startColor = chargeCore.endColor = new Color(1, 1, 1, pulse * .12f);
            // Only a small, dim glint remains in the held pose; the strike clears it entirely.
            Vector3 center = Vector3.Lerp(root, tip, Mathf.Lerp(.2f, .9f, Mathf.Clamp01(progress / .4f)));
            float radius = .045f + pulse * .09f;
            glintHorizontal.SetPosition(0, center - camera.transform.right * radius);
            glintHorizontal.SetPosition(1, center + camera.transform.right * radius);
            glintVertical.SetPosition(0, center - camera.transform.up * radius * .65f);
            glintVertical.SetPosition(1, center + camera.transform.up * radius * .65f);
            glintHorizontal.startColor = glintHorizontal.endColor =
                glintVertical.startColor = glintVertical.endColor = new Color(1, 1, 1, .025f + pulse * .1f);
            chargeLight.transform.position = center - camera.transform.forward * .3f;
            chargeLight.intensity = pulse * .8f;
            chargeLight.enabled = pulse > .001f;
        }

        void OnDisable()
        {
            if (chargeHalo == null) return;
            chargeHalo.enabled = chargeCore.enabled = glintHorizontal.enabled = glintVertical.enabled = false;
            chargeLight.enabled = false;
        }

        public void Pose(Vector3 facing, bool visible, bool blocking, bool broken, float swing = -1, float windup = 0,
            float reach = 2.5f, float halfAngle = 35, float recovery = 0, bool circular = false, bool lunge = false, bool powerful = false, bool thrustStance = false, bool relaxing = false)
        {
            blade.enabled = visible;
            WindupProgress = windup;
            SwingProgress = swing;
            RecoveryProgress = recovery;
            if (!visible) { relaxationBlend = 0; return; }
            Camera camera = Camera.main;
            Vector3 right = camera.transform.right;
            facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
            if (facing.sqrMagnitude < .01f) facing = Vector3.forward;
            float side = Vector3.Dot(facing, right);
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            float depth = Vector3.Dot(facing, forward);
            Vector3 hand = facing * (blocking ? .32f : .4f)
                + Vector3.Cross(Vector3.up, facing) * .16f;
            float height = broken ? .66f : .8f + (blocking ? .08f : 0);
            transform.position = transform.parent.position + Vector3.up * height + hand
                - camera.transform.forward * .15f;
            // X and Y tilt the blade in depth; a small Z tilt keeps its tip pointing upward.
            float pitch = depth * 18;
            float yaw = side * 28;
            float roll = -side * (blocking ? 16 : 8);
            if (broken) { pitch = 22; roll = -side * 20; }
            transform.rotation = camera.transform.rotation * Quaternion.Euler(pitch, yaw, roll);
            transform.localScale = Vector3.one;
            if (thrustStance && !blocking && !broken)
            {
                // The thrusting guard aims the blade along the ground-plane heading;
                // the ordinary guard keeps the sword raised for a cutting swing.
                Vector3 projectedDirection = Vector3.ProjectOnPlane(facing, camera.transform.forward).normalized;
                float readyAngle = Mathf.Atan2(-Vector3.Dot(projectedDirection, right),
                    Vector3.Dot(projectedDirection, camera.transform.up)) * Mathf.Rad2Deg;
                transform.rotation = camera.transform.rotation * Quaternion.Euler(0, 0, readyAngle);
                transform.position += facing * .12f;
            }
            if (!blocking && !broken && (windup > 0 || swing >= 0))
            {
                Vector3 readyPosition = transform.position;
                Quaternion readyRotation = transform.rotation;
                float sector = circular ? 180 : halfAngle;
                // Settle into the drawn-back pose early, then briefly hold it as a warning.
                float anticipation = Mathf.SmoothStep(0, 1, Mathf.Clamp01(windup / .78f));
                float t = Mathf.Clamp01(swing);
                // Rapid release followed by a slower follow-through, rather than an even sweep.
                float release = 1 - Mathf.Pow(1 - t, 3);
                float turn = Mathf.Lerp(-sector - (circular ? 0 : 28), sector + (circular ? 0 : 12), release);
                Vector3 attackDirection = lunge ? facing : Quaternion.AngleAxis(turn, Vector3.up) * facing;
                float tension = swing < 0 ? 1 : 1 - release;
                Vector3 drawBack = -facing * (powerful ? .4f : .22f) * tension
                    + Vector3.up * (lunge ? .04f : .16f) * tension;
                Vector3 grip = transform.parent.position + Vector3.up * .8f
                    + attackDirection * .4f + Vector3.Cross(Vector3.up, facing) * .16f + drawBack;
                float extension = Mathf.Lerp(lunge ? .55f : .7f, 1, release);
                Vector3 tip = transform.parent.position + Vector3.up * .8f
                    + attackDirection * Mathf.Max(.5f, reach * extension) + drawBack;
                Vector3 projectedBlade = Vector3.ProjectOnPlane(tip - grip, camera.transform.forward);
                float attackAngle = Mathf.Atan2(-Vector3.Dot(projectedBlade, right),
                    Vector3.Dot(projectedBlade, camera.transform.up)) * Mathf.Rad2Deg;
                Quaternion attackRotation = camera.transform.rotation * Quaternion.Euler(0, 0, attackAngle);
                // Keep the finishing pose briefly before easing back into the current guard.
                float settle = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1, recovery));
                float blend = swing >= 0 ? 1 - settle : anticipation;
                transform.position = Vector3.Lerp(readyPosition, grip - camera.transform.forward * .15f, blend);
                transform.rotation = Quaternion.Slerp(readyRotation, attackRotation, blend);
                // Sprite pivot is at 16% of 64 pixels; the blade tip is at pixel 61 (48 pixels/unit).
                float bladeLength = (61 - 64 * .16f) / 48;
                float lengthScale = projectedBlade.magnitude / bladeLength;
                transform.localScale = new Vector3(1, Mathf.Lerp(1, lengthScale, blend), 1);
            }
            relaxationBlend = broken || !visible ? 0 : Mathf.MoveTowards(relaxationBlend, relaxing ? 1 : 0, Time.deltaTime * 6);
            if (relaxationBlend > 0)
            {
                Vector3 lateral = Vector3.Cross(Vector3.up, facing).normalized;
                Vector3 stretchDirection = (lateral - facing * .25f).normalized;
                Vector3 projected = Vector3.ProjectOnPlane(stretchDirection, camera.transform.forward);
                float angle = Mathf.Atan2(-Vector3.Dot(projected, right), Vector3.Dot(projected, camera.transform.up)) * Mathf.Rad2Deg;
                Vector3 grip = transform.parent.position + Vector3.up * .75f + lateral * .65f - camera.transform.forward * .15f;
                transform.position = Vector3.Lerp(transform.position, grip, relaxationBlend);
                transform.rotation = Quaternion.Slerp(transform.rotation, camera.transform.rotation * Quaternion.Euler(0, 0, angle), relaxationBlend);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, relaxationBlend);
            }
            blade.color = broken ? new Color(.5f, .5f, .5f) : powerful ? Color.white
                : blocking ? new Color(1, .85f, .55f) : Color.white;
        }
    }
}
