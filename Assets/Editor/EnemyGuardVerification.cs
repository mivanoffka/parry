using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class EnemyGuardVerification
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Flags).SetValue(owner, value);
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
                    var enemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
                    enemy.enabled = false;
                    int checks = 0;
                    foreach (string state in new[] { "Guard", "Chase", "Windup", "Recovery" })
                    foreach (string rest in new[] { "none", "relaxing", "left", "raising" })
                    foreach (float pressure in new[] { 0f, 30f })
                    foreach (bool parry in new[] { false, true })
                    {
                        enemy.Guard.ResetCombat();
                        enemy.Health.ConfigureDurability(2, 3);
                        player.Guard.ResetCombat();
                        SetState(enemy, state);
                        Set(enemy, "stateUntil", Time.time + 1);
                        Set(enemy, "facing", Vector3.back);
                        Set(enemy, "parryChance", parry ? 1f : 0f);
                        Set(enemy, "performingLunge", false);
                        Set(enemy, "performingSweep", false);
                        enemy.Guard.State.AddPressure(enemy.Guard.EndurancePoints * pressure / 100, Time.time);
                        if (rest != "none") enemy.Guard.State.BeginRelaxation(Time.time);
                        if (rest == "left") enemy.Guard.State.LeaveRelaxation();
                        if (rest == "raising") enemy.Guard.State.SetBlocking(true, Time.time);
                        enemy.ReceiveAttack(2, Vector3.forward, player.Guard);
                        if (enemy.Health.Health != 2)
                            throw new Exception($"Healthy enemy missed ordinary block: state={state}, rest={rest}, pressure={pressure}, parry={parry}, remaining={enemy.Guard.RemainingEndurance}");
                        checks++;
                    }
                    // Concentration must not grant immunity while actually staggered.
                    enemy.Guard.ResetCombat();
                    enemy.Health.ConfigureDurability(2, 3);
                    SetState(enemy, "Guard");
                    enemy.Guard.State.Recoil(Time.time);
                    enemy.ReceiveAttack(2, Vector3.forward, player.Guard);
                    if (enemy.Health.IsAlive) throw new Exception("Recoil incorrectly grants guard.");
                    Debug.Log($"PARRY ENEMY GUARD CHECK PASSED: {checks} healthy defense cases and recoil vulnerability");
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
            EditorApplication.EnterPlaymode();
        }
    }
}
