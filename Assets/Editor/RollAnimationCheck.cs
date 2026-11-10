using System;
using UnityEditor;
using UnityEngine;

namespace Parry.Editor
{
    public static class RollAnimationCheck
    {
        [MenuItem("Parry/Validate And Preview Roll")]
        public static void Run()
        {
            CharacterRigCheck.RebuildClips();
            CharacterRigCheck.Run();
            var stage = new GameObject("Roll preview");
            var material = new Material(Shader.Find("Sprites/Default"));
            var cameraObject = new GameObject("Roll camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.6f;
            camera.transform.position = new Vector3(5.4f, 1.8f, -10);
            camera.backgroundColor = new Color(.18f, .19f, .22f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var render = new RenderTexture(1800, 720, 24);
            try
            {
                foreach (bool sentinel in new[] { false, true })
                foreach (float direction in new[] { -1f, 1f })
                {
                    var rig = new CharacterSkeleton(stage.transform, sentinel, material);
                    rig.Root.localScale = new Vector3(direction, 1, 1);
                    rig.Animator.Play("Idle");
                    rig.Animator.Sample();
                    float standing = Height(rig.Root);
                    for (int frame = 0; frame <= 60; frame++)
                    {
                        float t = frame / 60f;
                        rig.ResetRollShape();
                        rig.Animator.Play("Roll");
                        rig.Animator["Roll"].normalizedTime = t;
                        rig.Animator.Sample();
                        rig.PoseRoll(t, Vector3.zero, Quaternion.Euler(39, 0, 0), direction);
                        foreach (var part in rig.Root.GetComponentsInChildren<SpriteRenderer>())
                            if (part.bounds.min.y < .024f) throw new Exception("Roll intersects the floor at " + t);
                        if (frame == 30)
                        {
                            rig.Root.rotation = Quaternion.identity;
                            if (Height(rig.Root) >= standing * .85f) throw new Exception("Roll does not tuck into a compact silhouette");
                        }
                    }
                    rig.ResetRollShape();
                    if (rig.Root.Find("Hips/Spine/Head").localScale != Vector3.one)
                        throw new Exception("Roll shape did not reset");
                    UnityEngine.Object.DestroyImmediate(rig.Root.gameObject);
                }
                float[] times = { 0, .15f, .32f, .5f, .68f, .85f, 1 };
                for (int row = 0; row < 2; row++)
                for (int col = 0; col < times.Length; col++)
                {
                    var rig = new CharacterSkeleton(stage.transform, row == 1, material);
                    rig.Animator.Play("Roll");
                    rig.Animator["Roll"].normalizedTime = times[col];
                    rig.Animator.Sample();
                    rig.PoseRoll(times[col], new Vector3(col * 1.8f, row * 2.1f, 0), Quaternion.identity, 1);
                }
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                var image = new Texture2D(render.width, render.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(Application.dataPath + "/../character-roll-preview.png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("ROLL CHECK PASSED: both characters, both directions, 61 grounded frames each, compact tuck and pose reset");
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(stage);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
        static float Height(Transform root)
        {
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (var part in root.GetComponentsInChildren<SpriteRenderer>())
            {
                if (first) { bounds = part.bounds; first = false; }
                else bounds.Encapsulate(part.bounds);
            }
            return bounds.size.y;
        }
    }
}
