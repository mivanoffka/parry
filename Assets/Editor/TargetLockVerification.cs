using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class TargetLockVerification
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            bool checkedLock = false;
            EditorApplication.update += () =>
            {
                if (checkedLock || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < .25f) return;
                checkedLock = true;
                try
                {
                    var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                    player.enabled = false;
                    var targeting = player.TargetLock;
                    var enemies = player.transform.parent.GetComponentsInChildren<EnemyBrain>();
                    EnemyBrain first = null;
                    foreach (var enemy in enemies) if (enemy.enabled) first = enemy;
                    if (first == null) throw new InvalidOperationException("Missing first room guard.");
                    first.transform.position = player.transform.position + Vector3.forward * 3;
                    var other = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Combat/Enemy"),
                        player.transform.position + new Vector3(4, 0, 2), Quaternion.identity, player.transform.parent)
                        .GetComponent<EnemyBrain>();
                    other.Initialize(player);
                    Physics.SyncTransforms();
                    if (!targeting.Acquire() || targeting.Target != first)
                        throw new InvalidOperationException("Lock did not select the nearest visible enemy.");
                    targeting.Cycle(1);
                    if (targeting.Target != other) throw new InvalidOperationException("Could not cycle targets.");
                    targeting.Cycle(-1);
                    if (targeting.Target != first) throw new InvalidOperationException("Could not cycle back.");
                    // Commit a buffered slash through the real player update, without mouse aiming.
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(PlayerMotor).GetField("buffered", flags).SetValue(player, true);
                    typeof(PlayerMotor).GetField("bufferedAttack", flags).SetValue(player, AttackChoice.Slash);
                    typeof(PlayerMotor).GetField("bufferedUntil", flags).SetValue(player, Time.time + 1);
                    typeof(PlayerMotor).GetField("bufferedDirection", flags).SetValue(player, Vector3.back);
                    typeof(PlayerMotor).GetMethod("Update", flags).Invoke(player, null);
                    if (!player.Attacking || Vector3.Dot(player.AttackFacing, Vector3.forward) < .99f)
                        throw new InvalidOperationException("Attack did not face the locked enemy.");
                    first.Health.TakeHit(100, Vector3.zero);
                    targeting.Tick();
                    if (targeting.Target != other) throw new InvalidOperationException("Lock did not transfer after death.");
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = (player.transform.position + other.transform.position) * .5f + Vector3.up * .65f;
                    Physics.SyncTransforms();
                    targeting.Tick();
                    if (targeting.Target != null || targeting.Acquire())
                        throw new InvalidOperationException("Lock tracked an enemy through a solid wall.");
                    UnityEngine.Object.DestroyImmediate(wall);
                    Physics.SyncTransforms();
                    if (!targeting.Acquire()) throw new InvalidOperationException("Visible enemy could not be reacquired.");
                    other.transform.position = player.transform.position + Vector3.forward * 18;
                    targeting.Tick();
                    if (targeting.Target != null) throw new InvalidOperationException("Lock ignored its release distance.");
                    other.transform.position = player.transform.position + Vector3.forward * 4;
                    Physics.SyncTransforms();
                    targeting.Acquire();
                    player.Respawn();
                    if (targeting.Target != null) throw new InvalidOperationException("Respawn retained a target lock.");
                    Debug.Log("PARRY TARGET LOCK CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }
    }
}
