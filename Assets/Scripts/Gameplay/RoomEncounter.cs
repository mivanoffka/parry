using UnityEngine;

namespace Parry
{
    // Scene references keep the encounter editable without spawning a second arena at runtime.
    public sealed class RoomEncounter : MonoBehaviour
    {
        [SerializeField] PlayerMotor player;
        [SerializeField] Transform keyCarrier;
        [SerializeField] Transform[] secondRoomEnemies;
        [SerializeField] GameObject lockedDoor;
        [SerializeField] Bounds secondRoomBounds = new Bounds(new Vector3(32, 1, 0), new Vector3(23, 4, 19));
        PracticeTarget carrierHealth;
        Transform droppedKey;
        bool keyDropped, hasKey, doorOpened, secondRoomEntered;

        void Start()
        {
            carrierHealth = keyCarrier.GetComponent<PracticeTarget>();
            foreach (var enemy in secondRoomEnemies)
                enemy.GetComponent<EnemyBrain>().enabled = false;
        }

        void Update()
        {
            if (!keyDropped && !carrierHealth.IsAlive)
            {
                keyDropped = true;
                DropKey(keyCarrier.position);
            }
            if (droppedKey != null)
            {
                droppedKey.rotation = Camera.main.transform.rotation;
                if (player.IsAlive && FlatDistance(player.transform.position, droppedKey.position) < 1.15f)
                {
                    hasKey = true;
                    Destroy(droppedKey.gameObject);
                }
            }
            if (!player.IsAlive) return;
            if (hasKey && !doorOpened && FlatDistance(player.transform.position, lockedDoor.transform.position) < 2.2f)
            {
                doorOpened = true;
                lockedDoor.SetActive(false);
            }
            if (doorOpened && !secondRoomEntered && secondRoomBounds.Contains(player.transform.position))
            {
                secondRoomEntered = true;
                foreach (var enemy in secondRoomEnemies)
                    enemy.GetComponent<EnemyBrain>().enabled = true;
            }
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;

        void DropKey(Vector3 position)
        {
            droppedKey = new GameObject("Key to room 2").transform;
            droppedKey.SetParent(transform);
            droppedKey.position = position + Vector3.up * .65f;
            var ring = DemoArt.Line("Golden key ring", droppedKey, new Color(1, .8f, .15f), .085f);
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 24;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2 / 24;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * .18f, Mathf.Sin(angle) * .18f + .18f, 0));
            }
            var shaft = DemoArt.Line("Key shaft and tooth", droppedKey, new Color(1, .8f, .15f), .085f);
            shaft.useWorldSpace = false;
            shaft.positionCount = 4;
            shaft.SetPositions(new[] { Vector3.zero, new Vector3(0, -.36f, 0),
                new Vector3(.17f, -.36f, 0), new Vector3(.17f, -.23f, 0) });
        }

        void OnGUI()
        {
            string objective;
            if (!keyDropped) objective = "Room 1: defeat the guard to get the key";
            else if (!hasKey) objective = "Pick up the golden key dropped by the guard";
            else if (!doorOpened) objective = "Key acquired: approach the door to room 2";
            else if (!secondRoomEntered) objective = "Door unlocked: enter room 2";
            else
            {
                int alive = 0;
                foreach (var enemy in secondRoomEnemies)
                    if (enemy.GetComponent<PracticeTarget>().IsAlive) alive++;
                objective = alive == 0 ? "Both rooms cleared!" : "Room 2: enemies remaining — " + alive;
            }
            GUI.Box(new Rect(12, 106, 355, 30), objective);
        }
    }
}
