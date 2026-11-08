using UnityEngine;

namespace Parry
{
    // An original code-drawn 2D sword sprite; all poses live on the camera's sprite plane.
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
        public void Pose(Vector3 facing, bool visible, bool blocking, bool broken, float swing = -1, float windup = 0)
        {
            blade.enabled = visible;
            if (!visible) return;
            Camera camera = Camera.main;
            Vector3 right = camera.transform.right;
            float side = Vector3.Dot(facing, right) >= 0 ? 1 : -1;
            float angle = blocking ? -side * 65 : side * -25;
            if (windup > 0) angle = side * Mathf.Lerp(-25, 105, windup);
            if (swing >= 0) angle = side * Mathf.Lerp(110, -120, swing);
            if (broken) angle = side * 120;
            // Small camera-facing offset keeps the sword visible in front of the body sprite.
            transform.position = transform.parent.position + Vector3.up * .75f + right * side * .38f - camera.transform.forward * .15f;
            transform.rotation = camera.transform.rotation * Quaternion.Euler(0, 0, angle);
            blade.color = broken ? new Color(.5f, .5f, .5f) : blocking ? new Color(1, .85f, .55f) : Color.white;
        }
    }
}
