using System.Collections.Generic;
using UnityEngine;

namespace Parry
{
    // Small original placeholder sprites. Replace the SpriteRenderer sprite with production art.
    public static class DemoArt
    {
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Material spriteMaterial;
        static readonly Dictionary<string, Material> litMaterials = new Dictionary<string, Material>();
        public static Material SpriteMaterial => spriteMaterial != null ? spriteMaterial :
            spriteMaterial = new Material(Shader.Find("Sprites/Default"));

        public static Material LitSpriteMaterial(string key)
        {
            if (litMaterials.TryGetValue(key, out var cached) && cached != null) return cached;
            // Resources keeps the custom shader and its matching linear RGB maps in builds.
            var material = new Material(Resources.Load<Shader>("Combat/SpriteNormalLit")) { name = key + " — normal mapped 3D lighting" };
            material.SetTexture("_NormalMap", Resources.Load<Texture2D>("Combat/" + key + "_normal"));
            material.SetFloat("_NormalStrength", 1);
            material.SetFloat("_Specular", key == "sword" ? 1.1f : .16f);
            material.SetFloat("_Gloss", key == "sword" ? 96 : 36);
            litMaterials[key] = material;
            return material;
        }

        public static Sprite Character(bool target)
        {
            string key = target ? "target" : "player";
            if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = key };
            Color[] pixels = new Color[size * size];
            Color ink = new Color(.09f, .08f, .14f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - 31.5f;
                Color c = Color.clear;
                if (target)
                {
                    if (Mathf.Abs(dx) < 4 && y < 35) c = ink;
                    if (dx * dx / 370 + (y - 37) * (y - 37) / 440 < 1) c = ink;
                    if (dx * dx / 280 + (y - 37) * (y - 37) / 340 < 1) c = new Color(.66f, .38f, .24f);
                    float ring = Mathf.Sqrt(dx * dx + (y - 38) * (y - 38));
                    if (ring < 15 && ring > 11 || ring < 6) c = new Color(.94f, .72f, .4f);
                }
                else
                {
                    if (y < 30 && y > 5 && Mathf.Abs(dx) < 10 + (30 - y) * .35f) c = ink;
                    if (y < 27 && y > 8 && Mathf.Abs(dx) < 7 + (27 - y) * .32f) c = new Color(.64f, .16f, .27f);
                    if (y > 2 && y < 9 && (Mathf.Abs(x - 23) < 5 || Mathf.Abs(x - 41) < 5)) c = ink;
                    if (dx * dx / 440 + (y - 41) * (y - 41) / 270 < 1) c = ink;
                    if (dx * dx / 340 + (y - 42) * (y - 42) / 190 < 1) c = new Color(.92f, .88f, .74f);
                    if (y > 49 && y < 60 && (Mathf.Abs(x - 16) < 5 || Mathf.Abs(x - 47) < 5)) c = ink;
                    if (y > 38 && y < 46 && (Mathf.Abs(x - 25) < 3 || Mathf.Abs(x - 39) < 3)) c = ink;
                    if (y > 29 && y < 33 && Mathf.Abs(dx) < 4) c = ink;
                    if (y > 18 && y < 23 && Mathf.Abs(dx) < 4) c = new Color(1, .77f, .35f);
                }
                pixels[y * size + x] = c;
            }
            texture.SetPixels(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 38);
            sprite.name = key;
            sprites[key] = sprite;
            return sprite;
        }
        public static Sprite Disc()
        {
            if (sprites.TryGetValue("disc", out var s) && s != null) return s;
            Texture2D t = new Texture2D(64, 64) { filterMode = FilterMode.Bilinear };
            Color[] p = new Color[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                p[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01((1 - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31) * 6));
            t.SetPixels(p); t.Apply();
            s = Sprite.Create(t, new Rect(0, 0, 64, 64), Vector2.one * .5f, 64);
            sprites["disc"] = s; return s;
        }
        public static SpriteRenderer Billboard(string name, Transform parent, Sprite sprite, Vector3 position)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.transform.rotation = Camera.main.transform.rotation;
            var r = go.AddComponent<SpriteRenderer>(); r.sprite = sprite;
            r.sharedMaterial = sprite.name == "player" || sprite.name == "target" ? LitSpriteMaterial(sprite.name) : SpriteMaterial;
            return r;
        }
        public static void Shadow(Transform parent)
        {
            var r = Billboard("Ground shadow", parent, Disc(), Vector3.up * .025f);
            r.transform.rotation = Quaternion.Euler(90, 0, 0);
            r.transform.localScale = new Vector3(1.1f, .65f, 1);
            r.color = new Color(.05f, .04f, .09f, .38f);
        }
        public static LineRenderer Line(string name, Transform parent, Color color, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = SpriteMaterial;
            line.startColor = line.endColor = color; line.startWidth = line.endWidth = width;
            line.useWorldSpace = true; line.numCapVertices = 4;
            return line;
        }
    }
}
