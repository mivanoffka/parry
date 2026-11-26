using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Parry.Editor
{
    public static class SmokeVerification
    {
        public static void Run()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
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
                Vector3 origin = new Vector3(100, 0, 100);
                var cloud = SmokeCloud.Deploy(origin, 3, 8);
                typeof(SmokeCloud).GetField("deployedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cloud, Time.time - 1);
                Require(SmokeCloud.Exposure(origin) > .99f, "Inside cloud must blind");
                Require(SmokeCloud.Exposure(origin + Vector3.right * 4) == 0, "Outside cloud must be clear");
                Require(SmokeCloud.Obscures(origin - Vector3.right * 4, origin + Vector3.right * 4), "Smoke must block distant vision");
                Require(SmokeCloud.Obscures(origin + Vector3.right, origin), "Even close opponents inside smoke must be concealed");
                Require(cloud.GetComponent<ParticleSystem>().particleCount > 0, "Smoke particles missing");
                Require(cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported, "Smoke shader unsupported");
                typeof(SmokeCloud).GetField("deployedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cloud, Time.time - 8);
                Require(SmokeCloud.Exposure(origin) == 0, "Expired smoke must stop blinding");
                UnityEngine.Object.DestroyImmediate(cloud.gameObject);
                Require(!SmokeCloud.Obscures(origin - Vector3.right * 4, origin + Vector3.right * 4), "Destroyed smoke must stop occlusion");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = origin + new Vector3(1, 1, 0);
                wall.transform.localScale = new Vector3(.2f, 2, 20);
                Physics.SyncTransforms();
                var bounded = SmokeCloud.Deploy(origin, 4, 8, 100);
                typeof(SmokeCloud).GetField("deployedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bounded, Time.time - 1);
                for (int i = 0; i < 30; i++) typeof(SmokeCloud).GetMethod("Spread", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bounded, null);
                Require(SmokeCloud.Exposure(origin + Vector3.right * 2) == 0, "Wall must stop smoke propagation");
                Require(SmokeCloud.Exposure(origin + Vector3.left * 2) > .99f, "Smoke must spread in unobstructed directions");
                Require(SmokeCloud.EscapeDirection(origin).sqrMagnitude > .5f, "Smoke must provide an escape route");
                UnityEngine.Object.DestroyImmediate(bounded.gameObject);
                UnityEngine.Object.DestroyImmediate(wall);
                var arena = UnityEngine.Object.FindFirstObjectByType<ArenaDemo>();
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
                var enemy = UnityEngine.Object.FindFirstObjectByType<EnemyBrain>();
                Require(arena != null && player != null && enemy != null, "Arena fighters missing");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                int stock = player.SmokeGrenades;
                Require(stock > 0 && player.TrySelectConsumable(), "G must select a stocked consumable");
                Require(player.SmokeGrenades == stock && !player.TryUseConsumable(), "Selection alone must not throw or spend stock");
                Require(player.BeginConsumableThrow(), "Attack hold must start throw windup");
                Require(player.SmokeGrenades == stock && player.ChargeVisible && !player.Attacking,
                    "Windup must show charge without throwing or starting a melee attack");
                player.InterruptAttack();
                Require(!player.ConsumableSelected && !player.ChargingConsumable && !player.Charge.Active
                    && player.SmokeGrenades == stock, "Cancelling must clear throw preparation without spending stock");
                Require(player.TrySelectConsumable() && player.BeginConsumableThrow(), "A cancelled throw must be reusable");
                float shortThrow = player.ConsumableThrowDistance;
                player.Charge.Begin(Time.time - player.ChargeDuration, player.ChargeDuration, 0,
                    player.SustainedChargeStrength, player.PerfectChargeStrength);
                Require(player.ConsumableThrowDistance > shortThrow, "Windup strength must increase throw distance");
                Require(player.TryUseConsumable(), "Release must throw the selected consumable");
                Require(player.SmokeGrenades == stock - 1, "Throw must consume exactly one grenade");
                Require(!player.TryUseConsumable(), "Throw cooldown must prevent double use");
                foreach (var grenade in UnityEngine.Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(grenade.gameObject);
                var playerSmoke = SmokeCloud.Deploy(player.transform.position);
                typeof(SmokeCloud).GetField("deployedAt", flags).SetValue(playerSmoke, Time.time - 1);
                var feedback = typeof(ArenaDemo).GetMethod("UpdateScreenFeedback", flags);
                for (int i = 0; i < 200; i++) feedback.Invoke(arena, null);
                var vignette = (Vignette)typeof(ArenaDemo).GetField("smokeVignette", flags).GetValue(arena);
                var filter = (ColorAdjustments)typeof(ArenaDemo).GetField("healthFilter", flags).GetValue(arena);
                Require(vignette.intensity.value > .6f && filter.postExposure.value < -1, "Smoke must narrow and darken view");
                UnityEngine.Object.DestroyImmediate(playerSmoke.gameObject);
                for (int i = 0; i < 200; i++) feedback.Invoke(arena, null);
                Require(vignette.intensity.value < .01f && filter.postExposure.value > -.01f, "View must recover outside smoke");
                enemy.transform.position = player.transform.position + Vector3.forward * 3;
                var retreat = typeof(EnemyBrain).GetMethod("BeginRetreat", flags);
                int before = UnityEngine.Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None).Length;
                retreat.Invoke(enemy, null);
                retreat.Invoke(enemy, null);
                Require(UnityEngine.Object.FindObjectsByType<SmokeGrenade>(FindObjectsSortMode.None).Length == before,
                    "Enemy smoke ability must remain disabled");
                Require(!enemy.Guard.State.Relaxing, "Enemy must not relax while retreating near its opponent");
                Debug.Log("PARRY SMOKE PARTICLES, RADIUS, VISIBILITY AND EXPIRATION CHECK PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
