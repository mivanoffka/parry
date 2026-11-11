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
                Require(!SmokeCloud.Obscures(origin, origin + Vector3.right), "Close opponents remain visible");
                Require(cloud.GetComponent<ParticleSystem>().particleCount > 0, "Smoke particles missing");
                Require(cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported, "Smoke shader unsupported");
                typeof(SmokeCloud).GetField("deployedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cloud, Time.time - 8);
                Require(SmokeCloud.Exposure(origin) == 0, "Expired smoke must stop blinding");
                UnityEngine.Object.DestroyImmediate(cloud.gameObject);
                Require(!SmokeCloud.Obscures(origin - Vector3.right * 4, origin + Vector3.right * 4), "Destroyed smoke must stop occlusion");
                var arena = UnityEngine.Object.FindFirstObjectByType<ArenaDemo>();
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
                var enemy = UnityEngine.Object.FindFirstObjectByType<EnemyBrain>();
                Require(arena != null && player != null && enemy != null, "Arena fighters missing");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
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
