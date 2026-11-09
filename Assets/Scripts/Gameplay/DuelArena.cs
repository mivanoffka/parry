using UnityEngine;

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
        readonly Vector3 cameraOffset = new Vector3(0, 13, -16);

        void Start()
        {
            PracticeTarget.ResetCounter();
            CombatAudio.Initialize(transform);
            first.Initialize(second);
            second.Initialize(first);
        }

        void LateUpdate()
        {
            Vector3 center = (first.transform.position + second.transform.position) * .5f;
            view.transform.position = Vector3.SmoothDamp(view.transform.position,
                center + cameraOffset, ref cameraVelocity, .3f);
            // Keep both fighters and their overhead bars visible during retreats.
            Vector3 separation = first.transform.position - second.transform.position;
            float height = Mathf.Abs(Vector3.Dot(separation, view.transform.up)) * .5f + 3;
            float width = Mathf.Abs(Vector3.Dot(separation, view.transform.right)) * .5f + 3;
            float size = Mathf.Max(5, height, width / Mathf.Max(.1f, view.aspect));
            view.orthographicSize = Mathf.Lerp(view.orthographicSize, size, 1 - Mathf.Exp(-Time.deltaTime * 3));
        }
    }
}
