using UnityEngine;
using UnityEngine.InputSystem;

namespace Parry
{
    // Spectator scene: combat and respawning remain owned by the two existing brains.
    public sealed class DuelArena : MonoBehaviour
    {
        [SerializeField] EnemyBrain first;
        [SerializeField] EnemyBrain second;
        [SerializeField] Camera view;
        [Header("Combat settings")]
        [Tooltip("Show attack range indicators for both fighters.")]
        [SerializeField] bool showAttackRangeIndicators;
        public bool ShowAttackRangeIndicators { get => showAttackRangeIndicators; set => showAttackRangeIndicators = value; }
        Vector3 cameraVelocity;
        float zoomMultiplier = 1f;
        float zoomVelocity;
        float currentCameraSize;
        float initialCameraSize;
        readonly Vector3 cameraOffset = new Vector3(0, 13, -16);

        void Start()
        {
            initialCameraSize = currentCameraSize = view.orthographicSize;
            PracticeTarget.ResetCounter();
            CombatAudio.Initialize(transform);
            first.Initialize(second);
            second.Initialize(first);
        }

        void LateUpdate()
        {
            Vector3 center = (first.transform.position + second.transform.position) * .5f;
            // Automatic framing remains the baseline for the user's zoom.
            Vector3 separation = first.transform.position - second.transform.position;
            float height = Mathf.Abs(Vector3.Dot(separation, view.transform.up)) * .5f + 3;
            float width = Mathf.Abs(Vector3.Dot(separation, view.transform.right)) * .5f + 3;
            float size = Mathf.Max(5, height, width / Mathf.Max(.1f, view.aspect));
            if (Mouse.current != null && Application.isFocused)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
                    && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
                    scroll /= 120f;
                zoomMultiplier = Mathf.Clamp(zoomMultiplier
                    - scroll * .075f, .5f, 2f);
            }
            currentCameraSize = Mathf.SmoothDamp(currentCameraSize,
                Mathf.Clamp(size * zoomMultiplier, 3f, 20f), ref zoomVelocity, .24f);
            Vector3 followOffset = cameraOffset;
            if (view.orthographic) view.orthographicSize = currentCameraSize;
            else followOffset *= currentCameraSize / initialCameraSize;
            view.transform.position = Vector3.SmoothDamp(view.transform.position,
                center + followOffset, ref cameraVelocity, .3f);
        }
    }
}
