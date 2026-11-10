using UnityEngine;

namespace Parry
{
    // One rotation track per whole limb; no elbow, knee or ankle articulation.
    public static class CharacterAnimationClips
    {
        const string Spine = "Hips/Spine";
        static readonly string[] joints = {
            Spine, Spine + "/Head", Spine + "/Cape", Spine + "/LeftArm",
            Spine + "/RightArm", "Hips/LeftLeg", "Hips/RightLeg"
        };
        public static void Populate(Animation animation, bool useSavedClips = true)
        {
            void Clip(string name, float duration, bool loop, Track[] tracks, params float[] hips) =>
                Add(animation, name, duration, loop, useSavedClips, tracks, hips);
            Clip("Idle", 1.8f, true, new[] {
                K(0, -1, 1, -1), K(1, 1, -1, 1), K(2, -2, 2, -2),
                K(3, -8, -11, -8), K(4, 8, 11, 8)
            }, .31f, .32f, .31f);
            Clip("Walk", .8f, true, new[] {
                K(0, 2, 0, -2, 0, 2), K(1, -2, 0, 2, 0, -2), K(2, -5, 0, 5, 0, -5),
                K(3, -18, 0, 18, 0, -18), K(4, 18, 0, -18, 0, 18),
                K(5, 0, -5, 0, 0, 0), K(6, 0, 0, 0, -5, 0)
            }, .31f, .31f, .31f, .31f, .31f);
            Clip("Relax", 2.4f, true, new[] {
                K(0, -3, -5, -3), K(1, 3, 5, 3), K(2, -2, 2, -2),
                K(3, -15, -19, -15), K(4, 78, 84, 78), K(5, -3, -3, -3), K(6, 3, 3, 3)
            }, .31f, .32f, .31f);
            Clip("RelaxWalk", 1.2f, true, new[] {
                K(0, -3, -4, -3, -4, -3), K(1, 3, 4, 3, 4, 3), K(2, -2, 2, -2, 2, -2),
                K(3, -15, -19, -15, -19, -15), K(4, 78, 84, 78, 84, 78),
                K(5, 0, -3, 0, 0, 0), K(6, 0, 0, 0, -3, 0)
            }, .31f, .31f, .31f, .31f, .31f);
            Clip("Guard", 1, true, new[] {
                K(0, -4, -5, -4), K(1, 4, 5, 4), K(2, 4, 6, 4),
                K(3, -55, -58, -55), K(4, 55, 58, 55), K(5, -8, -8, -8), K(6, 8, 8, 8)
            }, .30f, .31f, .30f);
            Clip("GuardWalk", .9f, true, new[] {
                K(0, -4, -5, -4, -5, -4), K(1, 4, 5, 4, 5, 4), K(2, 4, 7, 4, 7, 4),
                K(3, -55, -58, -55, -58, -55), K(4, 55, 58, 55, 58, 55),
                K(5, 0, -3, 0, 0, 0), K(6, 0, 0, 0, -3, 0)
            }, .30f, .30f, .30f, .30f, .30f);
            Clip("Windup", .3f, false, new[] {
                K(0, 0, -5, -10), K(1, 0, 3, 6), K(2, 0, 8, 14),
                K(3, -8, -18, -28), K(4, 8, 50, 95), K(5, 0, -6, -12), K(6, 0, 6, 12)
            }, .31f, .30f, .29f);
            Clip("Strike", .45f, false, new[] {
                K(0, -10, 12, 5, 0), K(1, 6, -6, -3, 0), K(2, 14, -16, -6, 0),
                K(3, -28, 18, 5, -8), K(4, 95, -55, -20, 8),
                K(5, -12, 12, 5, 0), K(6, 12, -12, -5, 0)
            }, .29f, .33f, .32f, .31f);
            Clip("Block", .24f, false, new[] {
                K(0, -4, -12, -7, -4), K(1, 4, 9, 6, 4), K(2, 4, 14, 8, 4),
                K(3, -55, -68, -60, -55), K(4, 55, 65, 60, 55),
                K(5, -8, -12, -10, -8), K(6, 8, 12, 10, 8)
            }, .30f, .28f, .29f, .30f);
            Clip("Parry", .24f, false, new[] {
                K(0, -4, 9, 3, 0), K(1, 4, -5, -2, 0), K(2, 4, -14, -5, 0),
                K(3, -55, -75, -30, -8), K(4, 55, 90, 30, 8)
            }, .30f, .33f, .32f, .31f);
            Clip("Stagger", .6f, true, new[] {
                K(0, -12, -16, -10, -12), K(1, 10, 15, 8, 10), K(2, 8, 14, 5, 8),
                K(3, -22, -15, -20, -22), K(4, 22, 15, 20, 22),
                K(5, -14, -10, -14, -14), K(6, 14, 18, 14, 14)
            }, .28f, .27f, .29f, .28f);
            Clip("Roll", .3f, false, new[] {
                K(0, 0, 18, 18, 18, 18, 0), K(1, 0, -35, -35, -35, -35, 0),
                K(2, 0, -25, -25, -25, -25, 0),
                K(3, -8, 145, 145, 145, 145, -8),
                K(4, 8, -145, -145, -145, -145, 8),
                K(5, 0, 155, 155, 155, 155, 0),
                K(6, 0, -155, -155, -155, -155, 0)
            }, .31f, .28f, .28f, .28f, .28f, .31f);
        }
        struct Track { public int joint; public float[] angles; }
        static Track K(int joint, params float[] angles) => new Track { joint = joint, angles = angles };
        static void Add(Animation animation, string name, float duration, bool loop, bool useSavedClips, Track[] tracks, params float[] hips)
        {
            var saved = useSavedClips ? Resources.Load<AnimationClip>("Combat/Animations/Primitive/" + name) : null;
            if (saved != null)
            {
                animation.AddClip(saved, name);
                animation[name].speed = 0;
                return;
            }
            var clip = new AnimationClip { name = name, legacy = true, frameRate = 60,
                wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever };
            for (int j = 0; j < joints.Length; j++)
            {
                float[] angles = new float[4];
                foreach (var track in tracks) if (track.joint == j) angles = track.angles;
                for (int axis = 0; axis < 4; axis++)
                {
                    var curve = new AnimationCurve();
                    for (int k = 0; k < angles.Length; k++)
                    {
                        Quaternion q = Quaternion.Euler(0, 0, angles[k]);
                        curve.AddKey(new Keyframe(k * duration / (angles.Length - 1), q[axis]));
                    }
                    clip.SetCurve(joints[j], typeof(Transform), "localRotation." + "xyzw"[axis], curve);
                }
            }
            var height = new AnimationCurve();
            for (int k = 0; k < hips.Length; k++) height.AddKey(k * duration / (hips.Length - 1), hips[k]);
            clip.SetCurve("Hips", typeof(Transform), "localPosition.y", height);
            // Translate each whole leg through a swing arc; the support foot stays flat.
            bool walking = name == "Walk" || name == "GuardWalk" || name == "RelaxWalk";
            float stride = name == "Walk" ? .055f : .035f;
            float lift = name == "Walk" ? .11f : .075f;
            for (int side = 0; side < 2; side++)
            {
                var horizontal = new AnimationCurve();
                var vertical = new AnimationCurve();
                for (int frame = 0; frame <= 32; frame++)
                {
                    float t = frame / 32f;
                    float phase = (t + side * .5f) % 1;
                    // First half: lift and carry forward. Second half: grounded support.
                    float x = walking ? -stride * Mathf.Cos(phase * Mathf.PI * 2) : 0;
                    float y = walking && phase < .5f ? lift * Mathf.Sin(phase * Mathf.PI * 2) : 0;
                    horizontal.AddKey(t * duration, (side == 0 ? -.145f : .145f) + x);
                    vertical.AddKey(t * duration, y);
                }
                string leg = side == 0 ? "Hips/LeftLeg" : "Hips/RightLeg";
                clip.SetCurve(leg, typeof(Transform), "localPosition.x", horizontal);
                clip.SetCurve(leg, typeof(Transform), "localPosition.y", vertical);
            }
            clip.EnsureQuaternionContinuity();
            animation.AddClip(clip, name);
            animation[name].speed = 0; // Combat time and travelled distance drive sampling explicitly.
        }
    }
}
