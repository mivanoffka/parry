using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class EnemyLungeVerification
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
        static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Flags).Invoke(owner, args);
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
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
        static void Check()
        {
            EditorApplication.update -= Check;
            try
            {
                var enemy = UnityEngine.Object.FindFirstObjectByType<EnemyBrain>();
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
                enemy.enabled = player.enabled = false;
                player.Respawn(); Set(player, "invulnerableUntil", 0f); player.InterruptAttack(); enemy.InterruptAttack();
                enemy.Guard.State.ConfigureEndurance(500); enemy.Guard.State.Reset();
                enemy.transform.position = player.transform.position + Vector3.forward * 2.8f;
                Physics.SyncTransforms();
                Set(enemy, "powerfulSweepChance", 0f);
                Set(enemy, "lungeChance", 1f); Set(enemy, "nextAttackAt", 0f); Set(enemy, "nextLungeAt", 0f);
                ((EnemyTactics)typeof(EnemyBrain).GetField("tactics", Flags).GetValue(enemy)).Reengage(0, Time.time);
                Require(!(bool)Call(enemy, "CanConsiderLunge", player.transform.position - enemy.transform.position),
                    "Enemy must not open combat with an uncharged lunge");
                for (int i = 0; i < 3; i++) enemy.Guard.State.RecordOrdinaryContact();
                Call(enemy, "BeginWindup", true, false);
                Require(enemy.PowerfulThreat && !enemy.UnblockableAttack && !enemy.AttackThreatening, "Preparation must be a lunge, without an active threat");
                Require((float)typeof(EnemyBrain).GetProperty("CurrentWindup", Flags).GetValue(enemy) >= .9f, "Lunge windup too fast");
                Require(Mathf.Abs(enemy.Guard.RemainingEndurance - 500) < .01f, "Lunge cost must wait until release");
                int hp = player.Health;
                Set(enemy, "stateUntil", Time.time - .01f);
                Call(enemy, "Update");
                Require(enemy.Guard.State.StrongAttackCharges == 0, "Lunge must consume its warmup charge at release");
                Require(enemy.Guard.RemainingEndurance <= 110.01f, "Lunge must spend 390 concentration");
                if (enemy.UnblockableAttack) Call(enemy, "AdvanceLunge", .12f);
                Require(player.Health == hp - 1, "Lunge must hit along the forward path");
                Require(!enemy.UnblockableAttack, "Lunge must end after its active window");
                Require(enemy.transform.position.z < player.transform.position.z + 2.8f, "Lunge must move forward");
                float cooldown = (float)typeof(EnemyBrain).GetField("nextLungeAt", Flags).GetValue(enemy);
                Require(cooldown > Time.time, "Lunge must have cooldown");
                Call(enemy, "BeginRetreat");
                Require(UnityEngine.Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None).Length == 0, "Enemy must not throw smoke");
                enemy.Guard.State.Reset(); enemy.InterruptAttack();
                for (int i = 0; i < 3; i++) enemy.Guard.State.RecordOrdinaryContact();
                Set(enemy, "nextLungeAt", 0f); Set(enemy, "nextAttackAt", 0f);
                player.Respawn(); enemy.transform.position = player.transform.position + Vector3.forward * 2.8f;
                ((EnemyTactics)typeof(EnemyBrain).GetField("tactics", Flags).GetValue(enemy)).Reengage(0, Time.time);
                Call(enemy, "BeginWindup", true, false);
                Require(enemy.PowerfulThreat, "Second lunge setup failed");
                enemy.InterruptAttack();
                Require(!enemy.PowerfulThreat && !enemy.UnblockableAttack, "Interrupted lunge must stop");
                Debug.Log("PARRY ENEMY LUNGE SELECTION, WINDUP, COST, TRAVEL, HIT, COOLDOWN, INTERRUPTION AND NO SMOKE CHECK PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
