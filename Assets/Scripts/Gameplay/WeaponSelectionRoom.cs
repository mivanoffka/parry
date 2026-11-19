using UnityEngine;
using UnityEngine.InputSystem;

namespace Parry
{
    // Built once for both saved arena scenes, keeping their existing encounters intact.
    public sealed class WeaponSelectionRoom : MonoBehaviour
    {
        PlayerMotor player;
        readonly Vector3 swordPosition = new Vector3(-2, 0, -24);
        readonly Vector3 axePosition = new Vector3(2, 0, -24);
        Material floorMaterial, wallMaterial;

        public void Initialize(PlayerMotor owner)
        {
            player = owner;
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.22f, .27f, .25f);
            wallMaterial = new Material(floorMaterial) { color = new Color(.13f, .17f, .18f) };
            // Open a four-unit doorway through the first room's south boundary.
            foreach (var wall in transform.parent.GetComponentsInChildren<BoxCollider>())
            {
                Bounds b = wall.bounds;
                if (wall.isTrigger || b.size.y < .6f || b.size.x < 5 || b.size.z > 1
                    || Mathf.Abs(b.center.z + 10) > .5f || Mathf.Abs(b.center.x) > .5f) continue;
                wall.gameObject.SetActive(false);
                float width = (b.size.x - 4) * .5f;
                Box("Entrance wall", new Vector3(-2 - width * .5f, b.center.y, b.center.z), new Vector3(width, b.size.y, b.size.z), wallMaterial);
                Box("Entrance wall", new Vector3(2 + width * .5f, b.center.y, b.center.z), new Vector3(width, b.size.y, b.size.z), wallMaterial);
            }
            Box("Armory floor", new Vector3(0, -.2f, -23), new Vector3(10, .4f, 10), floorMaterial);
            Box("Armory west wall", new Vector3(-5, .5f, -23), new Vector3(.5f, 1, 10), wallMaterial);
            Box("Armory east wall", new Vector3(5, .5f, -23), new Vector3(.5f, 1, 10), wallMaterial);
            Box("Armory south wall", new Vector3(0, .5f, -28), new Vector3(10, 1, .5f), wallMaterial);
            Box("Armory north wall", new Vector3(-3.5f, .5f, -18), new Vector3(3, 1, .5f), wallMaterial);
            Box("Armory north wall", new Vector3(3.5f, .5f, -18), new Vector3(3, 1, .5f), wallMaterial);
            Box("Armory to arena corridor floor", new Vector3(0, -.2f, -14), new Vector3(4, .4f, 8), floorMaterial);
            Box("Armory corridor west wall", new Vector3(-2, .5f, -14), new Vector3(.5f, 1, 8), wallMaterial);
            Box("Armory corridor east wall", new Vector3(2, .5f, -14), new Vector3(.5f, 1, 8), wallMaterial);
            Stand("Sword — 2 HP / guard / lunge", swordPosition, WeaponKind.Sword);
            Stand("Axe — 2 HP / heavy concentration damage", axePosition, WeaponKind.Axe);
            player.SetSpawnPosition(new Vector3(0, 0, -26));
        }

        void Box(string label, Vector3 position, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label; box.transform.SetParent(transform, false);
            box.transform.position = position; box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        void Stand(string label, Vector3 position, WeaponKind kind)
        {
            Box(label, position + Vector3.up * .2f, new Vector3(1.2f, .4f, 1.2f), wallMaterial);
            var holder = new GameObject(label + " display");
            holder.transform.SetParent(transform, false); holder.transform.position = position;
            var display = WeaponVisual.Create(holder.transform);
            display.SetKind(kind);
            display.Pose(Vector3.back, true, false, false);
            var text = new GameObject(label + " label"); text.transform.SetParent(transform, false);
            text.transform.position = position + Vector3.up * 2.1f;
            text.transform.rotation = Camera.main.transform.rotation;
            var mesh = text.AddComponent<TextMesh>(); mesh.text = kind.ToString();
            mesh.characterSize = .12f; mesh.fontSize = 40; mesh.anchor = TextAnchor.MiddleCenter;
        }

        void Update()
        {
            if (!player.IsAlive || player.Attacking || player.Rolling || player.Guard.Disrupted) return;
            bool select = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame
                || Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
            if (!select) return;
            if (Near(swordPosition) && (!Near(axePosition) || (player.transform.position - swordPosition).sqrMagnitude <= (player.transform.position - axePosition).sqrMagnitude)) player.Equip(WeaponKind.Sword);
            else if (Near(axePosition)) player.Equip(WeaponKind.Axe);
        }
        bool Near(Vector3 position) => Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up).sqrMagnitude < 2.5f * 2.5f;
        void OnGUI()
        {
            if (player == null || !player.IsAlive || player.transform.position.z > -18) return;
            string hint = Near(swordPosition) ? "[E / Y] Take sword: guard, parry, lunge, sweep"
                : Near(axePosition) ? "[E / Y] Take axe: slow windup, fast strike, sweep; no guard"
                : "Choose a weapon at a stand, or enter the arena with fists";
            GUI.Box(new Rect(Screen.width * .5f - 300, Screen.height - 65, 600, 38), hint);
        }
    }
}
