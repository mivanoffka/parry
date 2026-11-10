using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Parry
{
    // Rigid cutout skeleton. Sprite parts rotate about actual joints; physics stays on the owner.
    public sealed class CharacterSkeleton
    {
        public readonly Transform Root;
        public readonly Animation Animator;
        public readonly Transform SwordShoulder, SwordWrist;
        public const float ArmLength = .29f;
        readonly List<SpriteRenderer> parts = new List<SpriteRenderer>();
        static readonly Dictionary<string, Sprite[]> atlases = new Dictionary<string, Sprite[]>();
        readonly Sprite[] sprites;
        readonly Material material;
        readonly bool sentinel;
        readonly Transform rollHead;

        public void ResetRollShape()
        {
            rollHead.localPosition = new Vector3(0, .43f, -.01f);
            rollHead.localScale = Vector3.one;
        }

        // Rotate around the tucked silhouette's centre, never around the feet.
        public void PoseRoll(float progress, Vector3 ground, Quaternion billboard, float direction)
        {
            float t = Mathf.Clamp01(progress);
            float tuck = Mathf.SmoothStep(0, 1, t / .18f)
                * (1 - Mathf.SmoothStep(0, 1, (t - .82f) / .18f));
            rollHead.localPosition = new Vector3(0, Mathf.Lerp(.43f, .08f, tuck), -.01f);
            rollHead.localScale = Vector3.one * Mathf.Lerp(1, .85f, tuck);
            Root.position = Vector3.zero;
            Root.rotation = billboard;
            Bounds shape = LocalBounds();
            Vector3 centre = shape.center;
            float spin = Mathf.Clamp01((t - .18f) / .64f);
            Root.rotation = billboard * Quaternion.Euler(0, 0, -360 * direction * spin);
            Root.position = ground + billboard * Vector3.up * shape.extents.y
                - Root.TransformVector(centre);
            // Account for every rotated sprite corner, including the oversized head.
            float bottom = float.PositiveInfinity;
            foreach (var part in parts) bottom = Mathf.Min(bottom, part.bounds.min.y);
            Root.position += Vector3.up * (ground.y + .025f - bottom);
        }

        Bounds LocalBounds()
        {
            var result = new Bounds();
            bool first = true;
            foreach (var part in parts)
            {
                Bounds b = part.sprite.bounds;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 p = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x,
                        (corner & 2) == 0 ? b.min.y : b.max.y, 0);
                    p = Root.InverseTransformPoint(part.transform.TransformPoint(p));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        // Enter Play Mode can preserve static managed fields while destroying native Sprites.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAtlasCache() => atlases.Clear();

        static bool Valid(Sprite[] parts)
        {
            if (parts == null || parts.Length != 12) return false;
            foreach (Sprite part in parts) if (part == null) return false;
            return true;
        }

        public CharacterSkeleton(Transform owner, bool sentinel, Material material)
        {
            this.material = material;
            this.sentinel = sentinel;
            string key = sentinel ? "sentinel-rig" : "duelist-rig";
            if (!atlases.TryGetValue(key, out sprites) || !Valid(sprites))
            {
                sprites = Slice(Resources.Load<Texture2D>("Combat/Characters/" + key));
                atlases[key] = sprites;
            }
            Root = Bone("Skeleton", owner, Vector3.zero);
            Root.gameObject.AddComponent<SortingGroup>();
            Transform hips = Bone("Hips", Root, new Vector3(0, .45f, 0));
            Transform spine = Bone("Spine", hips, Vector3.zero);
            Draw(spine, 1, .48f, false, 4);
            Transform cape = Bone("Cape", spine, new Vector3(0, .40f, .01f));
            Draw(cape, 2, sentinel ? .43f : .27f, true, sentinel ? 0 : 5);
            Transform head = Bone("Head", spine, new Vector3(0, .43f, -.01f));
            rollHead = head;
            Draw(head, 0, .91f, false, 9);
            Arm(spine, "Left", -.20f, 2, out _, out _);
            Arm(spine, "Right", .20f, 6, out SwordShoulder, out SwordWrist);
            Leg(hips, "Left", -.145f, 1);
            Leg(hips, "Right", .145f, 3);
            if (sentinel)
            {
                var shoulder = Bone("Pauldron", spine, new Vector3(-.20f, .38f, 0));
                Draw(shoulder, 3, .22f, true, 8);
            }
            Animator = Root.gameObject.AddComponent<Animation>();
            Animator.playAutomatically = false;
            Animator.cullingType = AnimationCullingType.AlwaysAnimate;
            CharacterAnimationClips.Populate(Animator);
        }

        static Transform Bone(string name, Transform parent, Vector3 position)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = position;
            return bone;
        }

        void Draw(Transform bone, int index, float height, bool topPivot, int order, float width = 0)
        {
            Transform art = Bone("Art", bone, Vector3.zero);
            var renderer = art.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprites[index];
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            float size = height / renderer.sprite.bounds.size.y;
            art.localScale = new Vector3(width > 0 ? width / renderer.sprite.bounds.size.x : size, size, 1);
            art.localPosition = Vector3.up * (topPivot ? -height * .5f : height * .5f);
            parts.Add(renderer);
        }

        void Arm(Transform parent, string side, float x, int order, out Transform arm, out Transform grip)
        {
            arm = Bone(side + "Arm", parent, new Vector3(x, .29f, 0));
            // The atlas mitten already contains its short stem: one rigid sprite, one joint.
            Draw(arm, 6, .34f, true, order, sentinel ? .18f : .16f);
            grip = Bone(side + "Grip", arm, Vector3.down * ArmLength);
        }

        void Leg(Transform parent, string side, float x, int order)
        {
            Transform leg = Bone(side + "Leg", parent, new Vector3(x, 0, 0));
            Draw(leg, 9, .29f, true, order, sentinel ? .21f : .19f);
        }

        public void Tint(Color color)
        {
            foreach (var renderer in parts) renderer.color = color;
        }

        // Aim the entire rigid arm. Unreachable grips never stretch or subdivide it.
        public void AimSwordHand(Vector3 grip)
        {
            Vector3 target = SwordShoulder.parent.InverseTransformPoint(grip) - SwordShoulder.localPosition;
            Vector2 delta = new Vector2(target.x, target.y);
            if (delta.sqrMagnitude < .000001f) return;
            float aim = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            SwordShoulder.localRotation = Quaternion.Euler(0, 0, aim + 90);
        }

        static Sprite[] Slice(Texture2D texture)
        {
            if (texture == null) throw new System.InvalidOperationException("Missing character rig atlas");
            Color32[] pixels = texture.GetPixels32();
            float[] columns = { 0, .29f, .5f, .75f, 1 };
            float[] rows = { 0, .39f, .69f, 1 };
            var result = new Sprite[12];
            for (int i = 0; i < result.Length; i++)
            {
                int col = i % 4, row = i / 4;
                int left = Mathf.RoundToInt(columns[col] * texture.width);
                int right = Mathf.RoundToInt(columns[col + 1] * texture.width);
                int bottom = Mathf.RoundToInt((1 - rows[row + 1]) * texture.height);
                int top = Mathf.RoundToInt((1 - rows[row]) * texture.height);
                int minX = right, minY = top, maxX = left, maxY = bottom;
                for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                {
                    if (pixels[y * texture.width + x].a < 32) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                if (maxX <= minX || maxY <= minY) throw new System.InvalidOperationException("Empty rig part " + i);
                result[i] = Sprite.Create(texture, new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                    Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
                result[i].name = texture.name + "-part-" + i;
            }
            return result;
        }
    }
}
