using System;
using UnityEditor;
using UnityEngine;

namespace Parry.Editor
{
    public static class CharacterRigCheck
    {
        [MenuItem("Parry/Validate Character Skeletons")]
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var owner = new GameObject("Rig validation");
            var material = new Material(Shader.Find("Sprites/Default"));
            try
            {
                foreach (bool sentinel in new[] { false, true })
                {
                    var rig = new CharacterSkeleton(owner.transform, sentinel, material);
                    if (rig.Root.GetComponentsInChildren<SpriteRenderer>().Length != (sentinel ? 8 : 7))
                        throw new Exception("Skeleton must have one drawable per whole limb");
                    foreach (var bone in rig.Root.GetComponentsInChildren<Transform>())
                        if (bone.name.Contains("Forearm") || bone.name.Contains("Shin") || bone.name.Contains("Thigh"))
                            throw new Exception("Segmented limb found: " + bone.name);
                    foreach (string name in new[] { "Idle", "Walk", "Guard", "GuardWalk", "Windup", "Strike", "Block", "Parry", "Stagger", "Roll" })
                    {
                        rig.Animator.Play(name);
                        rig.Animator[name].normalizedTime = .35f;
                        rig.Animator.Sample();
                        var head = rig.Root.Find("Hips/Spine/Head");
                        if (!float.IsFinite(head.position.x) || !float.IsFinite(head.position.y))
                            throw new Exception("Invalid sampled skeleton: " + name);
                    }
                    rig.Animator.Play("Idle");
                    rig.Animator.Sample();
                    foreach (float direction in new[] { 1f, -1f })
                    {
                        rig.Root.localScale = new Vector3(direction, 1, 1);
                        Vector3 target = rig.SwordShoulder.position + new Vector3(.8f * direction, -.6f, 0) * CharacterSkeleton.ArmLength;
                        rig.AimSwordHand(target);
                        if (Vector3.Distance(rig.SwordWrist.position, target) > .01f)
                            throw new Exception("Rigid arm failed to aim at a reachable grip");
                        rig.AimSwordHand(target + Vector3.right * direction * 10);
                        if (Mathf.Abs(Vector3.Distance(rig.SwordWrist.position, rig.SwordShoulder.position)
                            - CharacterSkeleton.ArmLength) > .001f)
                            throw new Exception("Rigid arm stretched toward an unreachable grip");
                    }
                    rig.Animator.Play("Walk");
                    rig.Animator["Walk"].normalizedTime = 0;
                    rig.Animator.Sample();
                    Transform thigh = rig.Root.Find("Hips/LeftLeg");
                    Quaternion start = thigh.localRotation;
                    rig.Animator["Walk"].normalizedTime = .5f;
                    rig.Animator.Sample();
                    rig.Animator["Walk"].normalizedTime = .25f;
                    rig.Animator.Sample();
                    if (thigh.localPosition.y < .09f)
                        throw new Exception("Walking leg does not lift off the ground");
                    if (Quaternion.Angle(start, thigh.localRotation) > 10)
                        throw new Exception("Walking foot rocks onto its heel");
                    if (Mathf.Abs(rig.Root.Find("Hips/RightLeg").localPosition.y) > .001f)
                        throw new Exception("Supporting leg does not stay planted");
                    rig.Animator["Walk"].normalizedTime = .75f;
                    rig.Animator.Sample();
                    if (rig.Root.Find("Hips/RightLeg").localPosition.y < .09f || Mathf.Abs(thigh.localPosition.y) > .001f)
                        throw new Exception("Walking legs do not alternate swing and support");
                    Transform sleeve = rig.Root.Find("Hips/Spine/LeftArm/Art");
                    if (sleeve.GetComponent<SpriteRenderer>().sprite.bounds.size.x * sleeve.localScale.x < .15f)
                        throw new Exception("Arm silhouette is too thin");
                    for (int frame = 0; frame < 32; frame++)
                    {
                        rig.Animator["Walk"].normalizedTime = frame / 32f;
                        rig.Animator.Sample();
                        Transform leftFoot = rig.Root.Find("Hips/LeftLeg");
                        Transform rightFoot = rig.Root.Find("Hips/RightLeg");
                        if (rig.Root.InverseTransformPoint(leftFoot.TransformPoint(Vector3.down * .29f)).x
                            >= rig.Root.InverseTransformPoint(rightFoot.TransformPoint(Vector3.down * .29f)).x)
                            throw new Exception("Walk cycle crosses the legs");
                    }
                    UnityEngine.Object.DestroyImmediate(rig.Root.gameObject);
                    // Reproduce a retained static cache after Unity destroys its native sprite objects.
                    var firstRun = new CharacterSkeleton(owner.transform, sentinel, material);
                    var cachedPart = firstRun.Root.GetComponentInChildren<SpriteRenderer>().sprite;
                    UnityEngine.Object.DestroyImmediate(firstRun.Root.gameObject);
                    UnityEngine.Object.DestroyImmediate(cachedPart);
                    var nextRun = new CharacterSkeleton(owner.transform, sentinel, material);
                    foreach (var part in nextRun.Root.GetComponentsInChildren<SpriteRenderer>())
                        if (part.sprite == null) throw new Exception("Destroyed sprite reused on repeated startup");
                    UnityEngine.Object.DestroyImmediate(nextRun.Root.gameObject);
                }
                Debug.Log("CHARACTER SKELETON CHECK PASSED: both atlases, 10 clips per rig, whole-limb hierarchy, rigid sword arm and repeated startup with destroyed cached sprites");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [MenuItem("Parry/Rebuild Character Animation Clips")]
        public static void RebuildClips()
        {
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources/Combat/Animations/Primitive");
            AssetDatabase.Refresh();
            var owner = new GameObject("Clip authoring");
            try
            {
                var animation = owner.AddComponent<Animation>();
                CharacterAnimationClips.Populate(animation, false);
                foreach (AnimationState state in animation)
                {
                    string path = "Assets/Resources/Combat/Animations/Primitive/" + state.name + ".anim";
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(state.clip, existing);
                        EditorUtility.SetDirty(existing);
                    }
                    else AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(state.clip), path);
                }
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [MenuItem("Parry/Render Skeleton Preview")]
        public static void RenderPreview()
        {
            RebuildClips();
            Run();
            var stage = new GameObject("Skeleton preview");
            var material = new Material(Shader.Find("Sprites/Default"));
            var cameraObject = new GameObject("Preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.5f;
            camera.transform.position = new Vector3(3.45f, 2.15f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.24f, .25f, .28f);
            var render = new RenderTexture(1440, 900, 24);
            try
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources/Combat/Animations/Primitive");
                AssetDatabase.Refresh();
                string[] poses = { "Idle", "Walk", "Guard", "Strike" };
                for (int row = 0; row < 2; row++)
                for (int col = 0; col < poses.Length; col++)
                {
                    var rig = new CharacterSkeleton(stage.transform, row == 1, material);
                    rig.Root.position = new Vector3(col * 2.3f, row * 2.15f, 0);
                    rig.Animator.Play(poses[col]);
                    rig.Animator[poses[col]].normalizedTime = col == 1 ? .2f : .4f;
                    rig.Animator.Sample();
                    if (row == 0 && col == 0)
                    {
                        foreach (AnimationState state in rig.Animator)
                        {
                            string path = "Assets/Resources/Combat/Animations/Primitive/" + state.name + ".anim";
                            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) == null)
                            {
                                var saved = UnityEngine.Object.Instantiate(state.clip);
                                saved.name = state.name;
                                AssetDatabase.CreateAsset(saved, path);
                            }
                        }
                        AssetDatabase.SaveAssets();
                    }
                }
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                var image = new Texture2D(render.width, render.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(Application.dataPath + "/../character-rig-preview.png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("CHARACTER RIG PREVIEW RENDERED");
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(stage);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
