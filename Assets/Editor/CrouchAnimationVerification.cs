using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Parry.Editor
{
    public static class CrouchAnimationVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static PlayerMotor player;
        static SpriteRenderer body;
        static Vector3 standingPosition, standingWeapon;
        static Keyboard keyboard;
        static float stageAt;
        static int stage;
        static bool walking;
        static Vector3 movementStart;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EditorApplication.playModeStateChanged += Entered;
            EditorApplication.EnterPlaymode();
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static void Entered(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= Entered;
            try
            {
                player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                    enemy.enabled = false;
                body = (SpriteRenderer)typeof(PlayerMotor).GetField("visual", Private).GetValue(player);
                Require(body != null && !body.forceRenderingOff, "Primitive player sprite must be visible.");
                Require(body.sprite.name == "player" || body.sprite.name == "player-back", "Player must keep primitive artwork.");
                Require(player.transform.Find("Skeleton") == null, "Player must not create a skeletal rig.");
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                stage = 0;
                stageAt = Time.time;
                InputSystem.onBeforeUpdate += InjectInput;
                EditorApplication.update += Check;
            }
            catch (Exception error) { Fail(error); }
        }

        static void InjectInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null) return;
            var keys = stage == 1 ? new[] { Key.LeftCtrl } : stage == 3 ? new[] { Key.LeftMeta }
                : stage == 4 ? new[] { Key.LeftCtrl, Key.D } : Array.Empty<Key>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }

        static void Check()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (stage == 0)
                {
                    if (Time.time - stageAt < .3f) return;
                    standingPosition = body.transform.localPosition;
                    standingWeapon = player.GetComponentInChildren<WeaponVisual>().transform.position;
                    Next();
                }
                else if (stage == 1 || stage == 3)
                {
                    if (Time.time - stageAt < .4f) return;
                    Require(player.Crouching, "Held Ctrl/Cmd did not enter crouch.");
                    Require(Mathf.Abs(body.transform.localScale.y - .55f) < .01f, "Primitive sprite did not compress into crouch.");
                    Require(Vector3.Dot(body.transform.localPosition - standingPosition, Camera.main.transform.up) < -.25f,
                        "Crouch did not lower the visible sprite.");
                    Require(Vector3.Dot(player.GetComponentInChildren<WeaponVisual>().transform.position - standingWeapon,
                        Camera.main.transform.up) < -.25f, "Sword did not lower with crouch.");
                    Next();
                }
                else if (stage == 2 || stage == 5)
                {
                    if (Time.time - stageAt < .4f) return;
                    Require(!player.Crouching, "Releasing key did not exit crouch.");
                    Require(Mathf.Abs(body.transform.localScale.y - 1) < .01f, "Release did not restore standing sprite.");
                    if (stage == 5)
                    {
                        InputSystem.onBeforeUpdate -= InjectInput;
                        InputSystem.RemoveDevice(keyboard);
                        EditorApplication.update -= Check;
                        Debug.Log("PARRY CROUCH ANIMATION CHECK PASSED: visible primitive sprite, no skeleton, Ctrl and Cmd input, lowered sprite and sword, crouched movement and standing reset");
                        EditorApplication.Exit(0);
                        return;
                    }
                    Next();
                }
                else if (stage == 4)
                {
                    if (Time.time - stageAt < .2f) return;
                    walking |= player.Crouching && body.transform.localScale.y < .6f
                        && Vector3.Distance(player.transform.position, movementStart) > .1f;
                    if (Time.time - stageAt < 1.8f) return;
                    Require(walking, "Moving crouch did not preserve the lowered sprite.");
                    Next();
                }
            }
            catch (Exception error) { Fail(error); }
        }

        static void Next() { stage++; stageAt = Time.time; movementStart = player.transform.position; }
        static void Fail(Exception error)
        {
            InputSystem.onBeforeUpdate -= InjectInput;
            EditorApplication.update -= Check;
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
