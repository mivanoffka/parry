using UnityEngine;

namespace Parry
{
    // Upright sword poses convey aim through hand placement and depth, not a screen-space pointer.
    public sealed class WeaponVisual : MonoBehaviour
    {
        SpriteRenderer blade;
        static Sprite sword;
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
        }
        public void Pose(Vector3 facing, bool visible, bool blocking, bool broken, float swing = -1, float windup = 0,
            float reach = 2.5f, float halfAngle = 35, float recovery = 0, bool circular = false, bool lunge = false)
        {
            blade.enabled = visible;
            if (!visible) return;
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
            if (!blocking && !broken && (windup > 0 || swing >= 0))
            {
                Vector3 readyPosition = transform.position;
                Quaternion readyRotation = transform.rotation;
                float sector = circular ? 180 : halfAngle;
                float progress = swing >= 0 ? Mathf.SmoothStep(0, 1, swing) : 0;
                Vector3 attackDirection = lunge ? facing : Quaternion.AngleAxis(Mathf.Lerp(-sector, sector, progress), Vector3.up) * facing;
                Vector3 grip = transform.parent.position + Vector3.up * .8f
                    + attackDirection * .4f + Vector3.Cross(Vector3.up, facing) * .16f;
                float extension = lunge ? (swing >= 0 ? Mathf.Lerp(.55f, 1, Mathf.Clamp01(swing * 3)) : .55f) : 1;
                Vector3 tip = transform.parent.position + Vector3.up * .8f + attackDirection * Mathf.Max(.5f, reach * extension);
                Vector3 projectedBlade = Vector3.ProjectOnPlane(tip - grip, camera.transform.forward);
                float attackAngle = Mathf.Atan2(-Vector3.Dot(projectedBlade, right),
                    Vector3.Dot(projectedBlade, camera.transform.up)) * Mathf.Rad2Deg;
                Quaternion attackRotation = camera.transform.rotation * Quaternion.Euler(0, 0, attackAngle);
                float blend = swing >= 0 ? 1 - Mathf.SmoothStep(0, 1, recovery) : Mathf.SmoothStep(0, 1, windup);
                transform.position = Vector3.Lerp(readyPosition, grip - camera.transform.forward * .15f, blend);
                transform.rotation = Quaternion.Slerp(readyRotation, attackRotation, blend);
                // Sprite pivot is at 16% of 64 pixels; the blade tip is at pixel 61 (48 pixels/unit).
                float bladeLength = (61 - 64 * .16f) / 48;
                float lengthScale = projectedBlade.magnitude / bladeLength;
                transform.localScale = new Vector3(1, Mathf.Lerp(1, lengthScale, blend), 1);
            }
            blade.color = broken ? new Color(.5f, .5f, .5f) : lunge ? new Color(1, .35f, .12f)
                : blocking ? new Color(1, .85f, .55f) : Color.white;
        }
    }
}
