using UnityEngine;

namespace Parry
{
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PracticeTarget : MonoBehaviour
    {
        [SerializeField] int maxHealth = 3;
        [SerializeField] float respawnDelay = 2.5f;
        [SerializeField] float hitKnockback = 5;
        public static int Hits { get; private set; }
        public bool IsAlive => health > 0;
        public int Health => health;
        public CombatHitbox Hitbox { get; private set; }
        public int MaxHealth => maxHealth;
        public float LastHitTime { get; private set; } = float.NegativeInfinity;
        int health;
        float flash, respawnAt;
        Vector3 home, knockback;
        SpriteRenderer visual;
        LineRenderer healthBar;
        CapsuleCollider hitbox;
        readonly Collider[] obstacles = new Collider[64];
        Color baseColor;
        public void Push(Vector3 direction, float speed)
        {
            knockback = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * speed;
        }

        void Awake()
        {
            health = maxHealth;
            home = transform.position;
            visual = GetComponentInChildren<SpriteRenderer>();
            baseColor = visual != null ? visual.color : Color.white;
            hitbox = GetComponent<CapsuleCollider>();
            Hitbox = gameObject.AddComponent<CombatHitbox>();
            healthBar = DemoArt.Line("Health", transform, new Color(.9f, .4f, .3f), .065f);
            healthBar.positionCount = 2;
        }
        public static void ResetCounter() => Hits = 0;
        public void SetBodyColor(Color color) { baseColor = color; if (visual != null) visual.color = color; }
        public void ConfigureDurability(int hitPoints, float knockbackSpeed)
        {
            maxHealth = Mathf.Max(1, hitPoints);
            health = maxHealth;
            hitKnockback = Mathf.Max(0, knockbackSpeed);
        }
        public void TakeHit(int amount, Vector3 direction)
        {
            if (health <= 0 || amount <= 0) return;
            Hits++;
            LastHitTime = Time.time;
            health = Mathf.Max(0, health - amount);
            CombatAudio.Play(health == 0 && GetComponent<EnemyBrain>() != null ? CombatSound.Death : CombatSound.Hit,
                transform.position);
            flash = .15f;
            knockback = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * hitKnockback;
            if (health == 0)
            {
                if (visual != null && GetComponent<EnemyBrain>() != null)
                    EnemyCorpse.Create(visual, baseColor, transform.position, direction, transform.parent);
                respawnAt = Time.time + respawnDelay;
                if (visual != null) visual.enabled = false;
                hitbox.enabled = healthBar.enabled = false;
            }
        }
        void Update()
        {
            if (health <= 0)
            {
                var enemy = GetComponent<EnemyBrain>();
                if ((enemy != null && !enemy.AutoRespawn) || Time.time < respawnAt) return;
                health = maxHealth;
                transform.position = home;
                knockback = Vector3.zero;
                if (visual != null) visual.enabled = true;
                hitbox.enabled = healthBar.enabled = true;
            }
            Vector3 displacement = knockback * Time.deltaTime;
            if (displacement.sqrMagnitude > 0) Move(displacement);
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 18 * Time.deltaTime);
            flash -= Time.deltaTime;
            if (visual != null)
            {
                visual.color = flash > 0 ? Color.white : baseColor;
                visual.transform.rotation = Camera.main.transform.rotation;
            }
            healthBar.enabled = SmokeCloud.Exposure(transform.position) <= .01f;
            healthBar.SetPosition(0, transform.position + new Vector3(-.4f, 1.8f, 0));
            healthBar.SetPosition(1, transform.position + new Vector3(-.4f + .8f * health / maxHealth, 1.8f, 0));
        }

        void Capsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            Vector3 scale = transform.lossyScale;
            radius = hitbox.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float offset = Mathf.Max(0, hitbox.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 center = transform.TransformPoint(hitbox.center);
            bottom = center - transform.up * offset;
            top = center + transform.up * offset;
        }

        void ResolveOverlaps()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Physics.SyncTransforms();
                Capsule(out Vector3 bottom, out Vector3 top, out float radius);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, obstacles, ~0, QueryTriggerInteraction.Ignore);
                bool corrected = false;
                for (int index = 0; index < count; index++)
                {
                    Collider obstacle = obstacles[index];
                    if (obstacle == hitbox || obstacle.transform.IsChildOf(transform)) continue;
                    if (!Physics.ComputePenetration(hitbox, transform.position, transform.rotation,
                        obstacle, obstacle.transform.position, obstacle.transform.rotation, out Vector3 direction, out float distance)) continue;
                    transform.position += direction * (distance + .01f);
                    corrected = true;
                }
                if (!corrected) break;
            }
        }

        public bool Move(Vector3 displacement)
        {
            ResolveOverlaps();
            float distance = displacement.magnitude;
            if (distance < .0001f) return false;
            Capsule(out Vector3 bottom, out Vector3 top, out float radius);
            Vector3 direction = displacement / distance;
            if (Physics.CapsuleCast(bottom, top, radius, direction, out RaycastHit contact,
                distance + .01f, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Min(distance, Mathf.Max(0, contact.distance - .01f));
            transform.position += direction * distance;
            ResolveOverlaps();
            return distance > .0001f;
        }

        public bool CanMove(Vector3 displacement)
        {
            Physics.SyncTransforms();
            float distance = displacement.magnitude;
            if (distance < .0001f) return false;
            Capsule(out Vector3 bottom, out Vector3 top, out float radius);
            return !Physics.CapsuleCast(bottom, top, radius, displacement / distance, distance + .01f,
                ~0, QueryTriggerInteraction.Ignore)
                && !Physics.CheckCapsule(bottom + displacement, top + displacement, Mathf.Max(.01f, radius - .01f),
                    ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
