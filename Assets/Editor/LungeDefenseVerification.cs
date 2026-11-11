using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class LungeDefenseVerification
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Flags).SetValue(owner, value);
        static object Get(object owner, string field) => owner.GetType().GetField(field, Flags).GetValue(owner);
        static void Call(object owner, string method) => owner.GetType().GetMethod(method, Flags).Invoke(owner, null);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void SetState(EnemyBrain enemy, string state) => Set(enemy, "state",
            Enum.Parse(typeof(EnemyBrain).GetField("state", Flags).FieldType, state));

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            bool done = false;
            EditorApplication.update += () =>
            {
                if (done || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < .25f) return;
                done = true;
                try
                {
                    var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                    player.enabled = false;
                    EnemyBrain enemy = null;
                    foreach (var candidate in player.transform.parent.GetComponentsInChildren<EnemyBrain>())
                        if (candidate.enabled) enemy = candidate;
                    Require(enemy != null, "Missing first island guard.");
                    enemy.Guard.ResetCombat();
                    enemy.transform.position = player.transform.position + Vector3.forward * 2.8f;
                    Set(enemy, "nextRollAt", 0f);
                    Set(enemy, "lungeDodgeChance", 1f);
                    Set(enemy, "stateUntil", 0f);
                    SetState(enemy, "Guard");
                    typeof(PlayerMotor).GetProperty("Style").GetSetMethod(true).Invoke(player, new object[] { WeaponStyle.Thrusting });
                    typeof(PlayerMotor).GetProperty("Facing").GetSetMethod(true).Invoke(player, new object[] { Vector3.forward });
                    ((AttackPreparation)Get(player, "preparation")).Begin(Time.time);
                    Physics.SyncTransforms();
                    Call(enemy, "Update");
                    Require(enemy.PreparingLungeDodge && !enemy.Rolling, "Windup must prepare a dodge without starting it.");
                    Vector3 planned = (Vector3)Get(enemy, "plannedLungeDirection");
                    Require(Mathf.Abs(Vector3.Dot(planned, Vector3.forward)) < .01f
                        || Vector3.Dot(planned, Vector3.forward) > .99f, "Dodge must go sideways or backward from the incoming lunge.");
                    typeof(PlayerMotor).GetProperty("Facing").GetSetMethod(true).Invoke(player, new object[] { Vector3.right });
                    Call(enemy, "Update");
                    Require((Vector3)Get(enemy, "plannedLungeDirection") == planned, "Last-second aim must not rewrite the dodge plan.");
                    enemy.ReactToLungeRelease(player.Guard, Vector3.right);
                    Require(enemy.Rolling && Vector3.Dot((Vector3)Get(enemy, "rollDirection"), planned) > .99f,
                        "Release must execute the original dodge plan.");
                    Require(enemy.Guard.RemainingEndurance <= enemy.Guard.EndurancePoints - 20 + .01f, "Dodge must spend concentration.");
                    foreach (var response in new[] { LungeDodgeDefense.Parry, LungeDodgeDefense.Block, LungeDodgeDefense.Hit })
                    {
                        enemy.Guard.ResetCombat();
                        enemy.Health.ConfigureDurability(5, 3);
                        SetState(enemy, "Roll");
                        Set(enemy, "dodgingLunge", true);
                        Set(enemy, "caughtDodgeParryChance", response == LungeDodgeDefense.Parry ? 1f : 0f);
                        Set(enemy, "caughtDodgeBlockChance", response == LungeDodgeDefense.Block ? 1f : 0f);
                        enemy.ReceiveAttack(1, Vector3.forward, player.Guard, true);
                        Require(enemy.Health.Health == (response == LungeDodgeDefense.Hit ? 4 : 5), "Caught dodge damage result is incorrect: " + response);
                        if (response == LungeDodgeDefense.Parry)
                            Require(enemy.Guard.RemainingEndurance <= enemy.Guard.EndurancePoints - enemy.Guard.State.PowerfulParryCost + .01f
                                && !enemy.Guard.Broken, "Lunge parry must spend a large concentration cost without a forced balance break.");
                        if (response == LungeDodgeDefense.Block)
                            Require(enemy.Guard.Broken, "Caught dodge block must break balance.");
                    }
                    Debug.Log("PARRY LUNGE PREPARATION, COMMITTED DODGE, FEINT AND CAUGHT DEFENSE CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
            EditorApplication.EnterPlaymode();
        }
    }
}
