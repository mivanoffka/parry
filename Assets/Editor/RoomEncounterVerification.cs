using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class RoomEncounterVerification
    {
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
