using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class RoomEncounterVerification
    {
        public static void RunArmoryCorridor()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/0.unity");
            int stage = 0;
            double started = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckArmory;
            void CheckArmory()
            {
                if (!EditorApplication.isPlaying || Time.timeSinceLevelLoad < .2f) return;
                try
                {
                    if (EditorApplication.timeSinceStartup - started > 60)
                        throw new InvalidOperationException("Armory corridor verification timed out.");
                    var rooms = UnityEngine.Object.FindAnyObjectByType<RoomEncounter>();
                    var data = new SerializedObject(rooms);
                    var player = (PlayerMotor)data.FindProperty("player").objectReferenceValue;
                    var carrier = (Transform)data.FindProperty("keyCarrier").objectReferenceValue;
                    var guard = carrier.GetComponent<EnemyBrain>();
                    if (stage == 0)
                    {
                        if (guard.enabled || player.transform.position.z > -18)
                            throw new InvalidOperationException("Player must start in the armory with an inactive arena guard.");
                        Physics.SyncTransforms();
                        foreach (float z in new[] { -18f, -16f, -14f, -12f, -10f })
                        {
                            if (!Physics.Raycast(new Vector3(0, .2f, z), Vector3.down, .5f, ~0, QueryTriggerInteraction.Ignore))
                                throw new InvalidOperationException("Corridor floor has a gap at " + z);
                        }
                        if (Physics.Raycast(new Vector3(0, .7f, -19), Vector3.forward, 10, ~0, QueryTriggerInteraction.Ignore))
                            throw new InvalidOperationException("Corridor doorway is blocked.");
                        if (!Physics.Raycast(new Vector3(0, .7f, -14), Vector3.right, 2.1f, ~0, QueryTriggerInteraction.Ignore)
                            || !Physics.Raycast(new Vector3(0, .7f, -14), Vector3.left, 2.1f, ~0, QueryTriggerInteraction.Ignore))
                            throw new InvalidOperationException("Corridor side walls are missing.");
                        player.enabled = false;
                        Move(player, new Vector3(0, 0, -11));
                    }
                    else if (stage == 1)
                    {
                        if (guard.enabled) throw new InvalidOperationException("Corridor activated arena guard too early.");
                        Move(player, new Vector3(0, 0, -9));
                    }
                    else
                    {
                        if (!guard.enabled) throw new InvalidOperationException("Entering arena did not activate its guard.");
                        Debug.Log("PARRY ARMORY CORRIDOR AND ARENA ACTIVATION PASSED");
                        EditorApplication.update -= CheckArmory;
                        EditorApplication.Exit(0);
                    }
                    stage++;
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.update -= CheckArmory;
                    EditorApplication.Exit(1);
                }
            }
            EditorApplication.EnterPlaymode();
        }

        public static void RunSpawnInside()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var rooms = UnityEngine.Object.FindAnyObjectByType<RoomEncounter>();
            var data = new SerializedObject(rooms);
            var player = (PlayerMotor)data.FindProperty("player").objectReferenceValue;
            Move(player, data.FindProperty("secondRoomBounds").boundsValue.center);
            bool done = false;
            EditorApplication.update += () =>
            {
                if (done || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < .2f) return;
                done = true;
                try
                {
                    var activeRooms = UnityEngine.Object.FindAnyObjectByType<RoomEncounter>();
                    var runtimeData = new SerializedObject(activeRooms);
                    var door = (GameObject)runtimeData.FindProperty("lockedDoor").objectReferenceValue;
                    if (!door.activeSelf)
                        throw new InvalidOperationException("Starting inside room two must not unlock the door.");
                    var guards = runtimeData.FindProperty("secondRoomEnemies");
                    for (int i = 0; i < guards.arraySize; i++)
                    {
                        var guard = (Transform)guards.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (!guard.GetComponent<EnemyBrain>().enabled)
                            throw new InvalidOperationException("Room two guard stayed inactive when the player spawned inside.");
                    }
                    Debug.Log("PARRY ROOM TWO SPAWN INSIDE PASSED");
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

        public static void Run()
        {
            foreach (string path in new[] { "Assets/Scenes/0.unity", "Assets/Scenes/SampleScene.unity" })
            {
                EditorSceneManager.OpenScene(path);
                var encounter = UnityEngine.Object.FindAnyObjectByType<RoomEncounter>();
                if (encounter == null || encounter.GetComponentsInChildren<EnemyBrain>().Length != 3)
                    throw new InvalidOperationException("Expected three enemies and a room encounter: " + path);
                var data = new SerializedObject(encounter);
                foreach (string field in new[] { "player", "keyCarrier", "lockedDoor" })
                    if (data.FindProperty(field).objectReferenceValue == null)
                        throw new InvalidOperationException("Missing room reference: " + field);
                if (data.FindProperty("secondRoomEnemies").arraySize != 2)
                    throw new InvalidOperationException("Room two must contain two enemies.");
                Physics.SyncTransforms();
                foreach (var position in new[] { Vector3.zero, new Vector3(32, 0, 0), new Vector3(16, 0, 0) })
                    if (!Physics.Raycast(position + Vector3.up * .1f, Vector3.down, .5f, ~0, QueryTriggerInteraction.Ignore))
                        throw new InvalidOperationException("Missing island or corridor floor: " + position);
                if (Physics.Raycast(new Vector3(16, .1f, 6), Vector3.down, .5f, ~0, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("The islands must be separated by empty space beside the corridor.");
            }
            int stage = 0;
            double started = EditorApplication.timeSinceStartup;
            RoomEncounter rooms = null;
            PlayerMotor player = null;
            Transform carrier = null;
            Transform[] guards = null;
            GameObject door = null;
            EditorApplication.update += () =>
            {
                if (!EditorApplication.isPlaying || !EditorApplication.isPlayingOrWillChangePlaymode) return;
                try
                {
                    if (Time.timeSinceLevelLoad < .2f) return;
                    if (EditorApplication.timeSinceStartup - started > 120)
                        throw new InvalidOperationException("Room encounter check timed out.");
                    if (stage == 0)
                    {
                        rooms = UnityEngine.Object.FindAnyObjectByType<RoomEncounter>();
                        var data = new SerializedObject(rooms);
                        player = (PlayerMotor)data.FindProperty("player").objectReferenceValue;
                        carrier = (Transform)data.FindProperty("keyCarrier").objectReferenceValue;
                        door = (GameObject)data.FindProperty("lockedDoor").objectReferenceValue;
                        var array = data.FindProperty("secondRoomEnemies");
                        guards = new[] { (Transform)array.GetArrayElementAtIndex(0).objectReferenceValue,
                            (Transform)array.GetArrayElementAtIndex(1).objectReferenceValue };
                        if (!door.activeSelf || door.GetComponent<BoxCollider>().isTrigger
                            || guards[0].GetComponent<EnemyBrain>().enabled || guards[1].GetComponent<EnemyBrain>().enabled)
                            throw new InvalidOperationException("Room two was not locked and waiting.");
                        player.enabled = false;
                        carrier.GetComponent<EnemyBrain>().enabled = false;
                        carrier.GetComponent<PracticeTarget>().TakeHit(100, Vector3.zero);
                    }
                    else if (stage == 1)
                    {
                        if (rooms.transform.Find("Key to room 2") == null)
                            throw new InvalidOperationException("Defeated guard did not drop the key.");
                        Move(player, carrier.position);
                    }
                    else if (stage == 2) Move(player, door.transform.position + Vector3.left + Vector3.down * door.transform.position.y);
                    else if (stage == 3)
                    {
                        if (door.activeSelf) throw new InvalidOperationException("Key did not unlock the door.");
                        Move(player, new Vector3(16, 0, 0));
                    }
                    else if (stage == 4)
                    {
                        if (guards[0].GetComponent<EnemyBrain>().enabled || guards[1].GetComponent<EnemyBrain>().enabled)
                            throw new InvalidOperationException("Corridor must not activate the second island's guards.");
                        Move(player, new Vector3(22, 0, 0));
                    }
                    else if (stage == 5)
                    {
                        if (!guards[0].GetComponent<EnemyBrain>().enabled || !guards[1].GetComponent<EnemyBrain>().enabled)
                            throw new InvalidOperationException("Room two guards did not enter combat.");
                        player.Respawn();
                        if (player.transform.position.x >= 0)
                            throw new InvalidOperationException("Revive placed player inside the dividing wall.");
                        Debug.Log("PARRY TWO ROOM KEY PROGRESSION PASSED");
                        EditorApplication.Exit(0);
                    }
                    stage++;
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        static void Move(PlayerMotor player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
