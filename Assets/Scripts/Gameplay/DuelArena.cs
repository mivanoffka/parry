using UnityEngine;
using UnityEngine.InputSystem;

namespace Parry
{
    // Spectator scene: combat and respawning remain owned by the two existing brains.
    [DefaultExecutionOrder(-100)]
    public sealed class DuelArena : MonoBehaviour
    {
        [SerializeField] EnemyBrain first;
        [SerializeField] EnemyBrain second;
        [SerializeField] Camera view;
        [Header("Combat settings")]
        [Tooltip("Show attack range indicators for both fighters.")]
        [SerializeField] bool showAttackRangeIndicators;
        public bool ShowAttackRangeIndicators { get => showAttackRangeIndicators; set => showAttackRangeIndicators = value; }
        Vector3 cameraVelocity, cameraFocus;
        [Header("Zoom tilt")]
        [SerializeField, Range(5, 80)] float closeZoomPitch = 28;
        [SerializeField, Range(5, 80)] float distantZoomPitch = 58;
        float normalCameraPitch;
        float zoomMultiplier = 1f;
        float zoomVelocity;
        float currentCameraSize;
        float initialCameraSize;
        readonly Vector3 cameraOffset = new Vector3(0, 13, -16);

        void Start()
        {
            initialCameraSize = currentCameraSize = view.orthographicSize;
            normalCameraPitch = view.transform.eulerAngles.x;
            cameraFocus = (first.transform.position + second.transform.position) * .5f;
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
                    - scroll * .0375f, .5f, 2f);
            }
            currentCameraSize = Mathf.SmoothDamp(currentCameraSize,
                Mathf.Clamp(size * zoomMultiplier, 3f, 20f), ref zoomVelocity, .4f);
            float pitch = CameraZoom.Pitch(currentCameraSize, initialCameraSize, 20,
                normalCameraPitch, closeZoomPitch, distantZoomPitch);
            Quaternion cameraRotation = Quaternion.Euler(pitch, 0, 0);
            Vector3 followOffset = -(cameraRotation * Vector3.forward) * cameraOffset.magnitude;
            if (view.orthographic) view.orthographicSize = currentCameraSize;
            else followOffset *= currentCameraSize / initialCameraSize;
            cameraFocus = Vector3.SmoothDamp(cameraFocus, center, ref cameraVelocity, .3f);
            view.transform.SetPositionAndRotation(cameraFocus + followOffset, cameraRotation);
        }
    }
}
