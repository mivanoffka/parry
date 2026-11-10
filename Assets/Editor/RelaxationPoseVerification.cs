using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class RelaxationPoseVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EditorApplication.playModeStateChanged += Entered;
            EditorApplication.EnterPlaymode();
        }
        static void Entered(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= Entered;
            EditorApplication.update += Check;
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Check()
        {
            EditorApplication.update -= Check;
            try
            {
                foreach (var presentation in UnityEngine.Object.FindObjectsByType<CharacterPresentation>(FindObjectsSortMode.None))
                {
                    var player = presentation.GetComponent<PlayerMotor>();
                    var enemy = presentation.GetComponent<EnemyBrain>();
                    if (player != null) player.enabled = false;
                    if (enemy != null) enemy.enabled = false;
                    var guard = presentation.GetComponent<CombatGuard>();
                    var rig = (CharacterSkeleton)typeof(CharacterPresentation).GetField("skeleton", Private).GetValue(presentation);
                    var body = (SpriteRenderer)typeof(CharacterPresentation).GetField("body", Private).GetValue(presentation);
                    var weapon = presentation.GetComponentInChildren<WeaponVisual>();
                    var update = typeof(CharacterPresentation).GetMethod("LateUpdate", Private);
                    guard.State.Reset();
                    if (player != null) player.InterruptAttack();
                    if (enemy != null) enemy.InterruptAttack();
                    Require(guard.State.BeginRelaxation(Time.time), "Cannot enter relaxation");
                    foreach (bool mirror in new[] { false, true })
                    foreach (Vector3 facing in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        typeof(CharacterPresentation).GetField("motion", Private).SetValue(presentation, 0f);
                        typeof(CharacterPresentation).GetField("previousPosition", Private).SetValue(presentation, presentation.transform.position);
                        body.flipX = mirror;
                        rig.Animator.Play("Relax");
                        typeof(WeaponVisual).GetField("relaxationBlend", Private).SetValue(weapon, 0f);
                        // Include the glint update between motor pose and rig update, as in gameplay.
                        for (int frame = 0; frame < 200; frame++)
                        {
                            weapon.Pose(facing, true, false, false, relaxing: true);
                            weapon.ChargeGlint(0, false);
                            update.Invoke(presentation, null);
                        }
                        Require(weapon.RelaxationBlend > .99f, "Inactive charge glint must not reset relaxation");
                        Require((string)typeof(CharacterPresentation).GetField("currentClip", Private).GetValue(presentation) == "Relax", "Wrong rest clip: " + typeof(CharacterPresentation).GetField("currentClip", Private).GetValue(presentation));
                        Vector3 arm = (rig.SwordWrist.position - rig.SwordShoulder.position).normalized;
                        Vector3 outward = rig.Root.TransformVector(Vector3.right).normalized;
                        Require(Vector3.Dot(arm, outward) > .8f, "Rest arm must stretch outward for either facing");
                        Require(Vector3.Distance(weapon.transform.position, rig.SwordWrist.position - rig.Root.up * .075f) < .001f,
                            "Sword detached from hand");
                        Require(Vector3.Dot(weapon.transform.up, outward) > .9f, "Sword must point outward, not across body");
                    }
                    rig.Animator.Play("RelaxWalk");
                    typeof(CharacterPresentation).GetField("motion", Private).SetValue(presentation, 1f);
                    typeof(CharacterPresentation).GetField("previousPosition", Private).SetValue(presentation,
                        presentation.transform.position - Vector3.right * Mathf.Max(.001f, Time.deltaTime) * 3);
                    update.Invoke(presentation, null);
                    Require((string)typeof(CharacterPresentation).GetField("currentClip", Private).GetValue(presentation) == "RelaxWalk", "Moving rest must use rest walk");
                }
                Debug.Log("PARRY RELAXATION RIG, MIRRORED FACING, SWORD GRIP AND WALK CHECK PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
