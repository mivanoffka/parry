using UnityEngine;

namespace Parry
{
    [RequireComponent(typeof(PracticeTarget), typeof(CombatGuard))]
    public sealed class EnemyBrain : MonoBehaviour
    {
        [Header("Durability")]
        [SerializeField] int maxHealth = 2;
        [SerializeField] float hitKnockback = 3;
        [Header("Endurance (baseline 100)")]
        [SerializeField, Min(1)] int endurance = 100;
        [Header("Chase")]
        [SerializeField] float moveSpeed = 4.2f;
        [SerializeField] float attackDistance = 2.1f;
        [SerializeField] float stunDuration = .23f;
        [Header("Attack")]
        [SerializeField] float windup = .2f;
        [SerializeField] float recovery = .8f;
        [SerializeField] float attackInterval = 1f;
        [SerializeField] float reach = 2.5f;
        [SerializeField, Range(1, 180)] float halfAngle = 35;
        [SerializeField] int damage = 1;
        [Header("Emergency defense")]
        [SerializeField] float emergencyGuardPressure = 45;
        [SerializeField, Range(0, 1)] float parryChance = .5f;

        [Header("Concentration tactics")]
        [SerializeField] float retreatSpeed = 9f;
        [SerializeField] float safeDistance = 5.2f;
        [SerializeField] float aggressiveSpeedMultiplier = 1.25f;
        [Header("Tactical rolls")]
        [SerializeField] float rollSpeed = 14f;
        [SerializeField] float rollDuration = .28f;
        [SerializeField] float rollCooldown = 3f;
        [SerializeField] float rollPressure = 20f;
        [SerializeField] float cornerProbeDistance = .9f;
        [SerializeField] float approachRollRange = 7f;

        enum State { Chase, Guard, Windup, Recovery, Retreat, Roll }
        State state;
        [SerializeField] PlayerMotor player;
        [SerializeField] EnemyBrain opponent;
        Transform TargetTransform => opponent != null ? opponent.transform : player != null ? player.transform : null;
        bool TargetAlive => opponent != null ? opponent.Health.IsAlive : player != null && player.IsAlive;
        bool TargetThreatening => opponent != null ? opponent.AttackThreatening : player != null && player.AttackThreatening;
        float TargetReach => opponent != null ? opponent.AttackReach : player != null ? player.ThreatReach : 0;
        bool TargetUnblockable => player != null && player.UnblockableAttack && player.AttackThreatening;
        CombatGuard TargetGuard => opponent != null ? opponent.Guard : player.Guard;
        public bool AttackThreatening => health.IsAlive && state == State.Windup && !Guard.Disrupted;
        public void Initialize(EnemyBrain target) { opponent = target; player = null; }
        void AttackTarget(int amount, Vector3 direction)
        {
            if (opponent != null) opponent.ReceiveAttack(amount, direction, Guard);
            else if (player != null) player.ReceiveAttack(amount, direction, Guard);
        }
        PracticeTarget health;
        SpriteRenderer visual;
        Transform shadow;
        LineRenderer warning;
        Vector3 facing = Vector3.back;
        float stateUntil, observedHitTime = float.NegativeInfinity;
        bool wasAlive = true;
        bool observedPlayerAttack;
        readonly EnemyTactics tactics = new EnemyTactics();
        int comboRemaining;
        float retreatSide = 1, nextSideChange;
        float lastSwingAt = float.NegativeInfinity;
        float nextAttackAt;
        float nextRollAt, rollRemaining;
        float currentRollSpeed;
        bool closingRoll;
        Vector3 rollDirection;
        public bool Rolling => state == State.Roll;
        public string TacticalStatus => !health.IsAlive ? "DEFEATED" : Guard.Disrupted ? "STAGGERED"
            : Rolling ? (closingRoll ? "CLOSING" : "ESCAPING")
            : tactics.Retreating ? "RECOVERING" : tactics.Aggressive ? "PRESSING" : "CAUTIOUS";
        public CombatGuard Guard { get; private set; }
        public PracticeTarget Health => health;
        WeaponVisual weapon;
        BloodSplatter blood;
        ArenaDemo arena;
        DuelArena duelArena;
        bool ShowAttackRangeIndicators => arena != null ? arena.ShowAttackRangeIndicators
            : duelArena == null || duelArena.ShowAttackRangeIndicators;
        bool CircularAttacks => arena != null && arena.CircularAttacks;
        public float AttackReach => arena != null ? arena.BaseAttackRadius : reach;
        float AttackDistance => attackDistance * AttackReach / Mathf.Max(.1f, reach);
        static readonly float[] SteeringAngles = { 0, 45, -45, 90, -90, 135, -135 };
        static readonly float[] ApproachAngles = { 0, 22.5f, -22.5f, 45, -45 };
        bool CanGuard => !Guard.Disrupted && !Rolling;
        bool CanStartRoll => CanGuard && Time.time >= nextRollAt && state != State.Windup
            && (state != State.Recovery || Time.time >= stateUntil)
            && Guard.State.CanSpendForRoll(rollPressure, Time.time);

        public void Initialize(PlayerMotor target) { player = target; opponent = null; }

        void Awake()
        {
            arena = GetComponentInParent<ArenaDemo>();
            duelArena = GetComponentInParent<DuelArena>();
            health = GetComponent<PracticeTarget>();
            health.ConfigureDurability(maxHealth, hitKnockback);
            Guard = GetComponent<CombatGuard>();
            Guard.State.ConfigureEndurance(endurance);
            visual = GetComponentInChildren<SpriteRenderer>();
            warning = DemoArt.Line("Enemy attack warning", transform, new Color(1, .23f, .18f), .07f);
            warning.enabled = false;
            shadow = transform.Find("Ground shadow");
            weapon = WeaponVisual.Create(transform);
            blood = BloodSplatter.Create(transform.parent);
        }

        void Update()
        {
            warning.enabled = false;
            if (shadow != null) shadow.gameObject.SetActive(health.IsAlive);
            if (!health.IsAlive)
            {
                wasAlive = false;
                observedPlayerAttack = false;
                nextAttackAt = 0;
                nextRollAt = rollRemaining = 0;
                tactics.Reset();
                comboRemaining = 0;
                state = State.Chase;
                Guard.Tick(false, facing);
                return;
            }
            if (!wasAlive)
            {
                wasAlive = true;
                Guard.ResetCombat();
                tactics.Reset();
                comboRemaining = 0;
                state = State.Recovery;
                stateUntil = Time.time + .8f;
            }
            if (!TargetAlive)
            {
                state = State.Chase;
                comboRemaining = 0;
                observedPlayerAttack = false;
                tactics.Reset();
                Guard.Tick(false, facing);
                return;
            }
            if (health.LastHitTime > observedHitTime)
            {
                observedHitTime = health.LastHitTime;
                state = State.Recovery;
                stateUntil = Time.time + stunDuration;
            }
            Vector3 delta = Vector3.ProjectOnPlane(TargetTransform.position - transform.position, Vector3.up);
            if (Rolling)
            {
                Guard.Tick(false, facing);
                float elapsed = Mathf.Min(Time.deltaTime, rollRemaining);
                bool moved = health.Move(rollDirection * currentRollSpeed * elapsed);
                rollRemaining = Mathf.Max(0, rollRemaining - elapsed);
                if (!moved || rollRemaining <= 0)
                {
                    UpdateTactics(delta);
                    if (tactics.Retreating) BeginRetreat();
                    else BeginGuard();
                }
                return;
            }
            if (delta.sqrMagnitude > .001f && (state == State.Chase || state == State.Retreat)) facing = delta.normalized;
            if (delta.sqrMagnitude > .001f && state == State.Guard)
                facing = Vector3.RotateTowards(facing, delta.normalized, 4 * Time.deltaTime, 0);
            bool newAttack = TargetThreatening && !observedPlayerAttack;
            observedPlayerAttack = TargetThreatening;
            if (TargetUnblockable && delta.magnitude <= TargetReach + .5f && HasLineOfSight(delta)
                && TryDodgeLunge(delta)) return;
            if (!TargetUnblockable && state != State.Windup && TargetThreatening
                && delta.magnitude <= TargetReach + .3f && HasLineOfSight(delta))
                PrepareDefense(delta, newAttack);
            Guard.Tick(CanGuard && state != State.Windup
                && (state != State.Recovery || Time.time >= stateUntil), facing);
            UpdateTactics(delta);
            if (Guard.Disrupted)
            {
                state = State.Recovery;
                stateUntil = Time.time + .25f;
                comboRemaining = 0;
                return;
            }
            if (TryCornerRoll(delta)) return;
            if (TryApproachRoll(delta)) return;
            if (tactics.Retreating && state != State.Retreat && (state != State.Windup || tactics.UrgentRetreat)
                && (state != State.Recovery || Time.time >= stateUntil)) BeginRetreat();
            if (state == State.Retreat)
            {
                if (tactics.Retreating)
                {
                    if (tactics.CanCounterattack(Guard.Pressure, Guard.EndurancePoints, TargetGuard.Disrupted)
                        && delta.magnitude <= AttackDistance && !TargetThreatening
                        && Time.time >= nextAttackAt && HasLineOfSight(delta))
                    {
                        tactics.Reengage(Guard.Pressure, Time.time);
                        BeginWindup(true);
                    }
                    else Retreat(delta);
                    return;
                }
                state = State.Chase;
            }
            if (state == State.Chase)
            {
                if (delta.sqrMagnitude > .001f) facing = delta.normalized;
                if ((delta.magnitude <= AttackDistance || (TargetThreatening && delta.magnitude <= AttackReach + .3f)) && HasLineOfSight(delta))
                {
                    if (!TargetThreatening && (TargetGuard.Broken || tactics.Aggressive)) BeginWindup(true);
                    else BeginGuard();
                }
                else Chase(delta);
            }
            else if (state == State.Guard)
            {
                if (Time.time >= stateUntil)
                {
                    if (TargetThreatening) return;
                    if (delta.magnitude > AttackReach || !HasLineOfSight(delta)) state = State.Chase;
                    else BeginWindup(true);
                }
            }
            else if (state == State.Windup)
            {
                DrawWarning();
                if (Time.time >= stateUntil)
                {
                    // Direction stays locked from the start of the visible windup.
                    lastSwingAt = Time.time;
                    CombatAudio.Play(CombatSound.Swing, transform.position);
                    nextAttackAt = Time.time + attackInterval;
                    if (delta.magnitude <= AttackReach && (CircularAttacks || delta.sqrMagnitude < .001f || Vector3.Angle(facing, delta) <= halfAngle)
                        && HasLineOfSight(delta))
                        AttackTarget(damage, CircularAttacks && delta.sqrMagnitude > .001f ? delta.normalized : facing);
                    state = State.Recovery;
                    stateUntil = Time.time + (tactics.Aggressive ? recovery * .75f : recovery);
                }
            }
            else if (Time.time >= stateUntil)
            {
                if (TargetThreatening) BeginGuard();
                else if (tactics.Aggressive && delta.magnitude <= AttackReach && HasLineOfSight(delta))
                {
                    if (comboRemaining > 0) { comboRemaining--; BeginWindup(false); }
                    else BeginGuard();
                }
                else state = State.Chase;
            }
        }

        void UpdateTactics(Vector3 delta)
        {
            bool threatened = TargetThreatening && delta.magnitude <= TargetReach + .5f && HasLineOfSight(delta);
            bool opening = TargetGuard.Disrupted && delta.magnitude <= AttackReach && HasLineOfSight(delta);
            tactics.Update(Guard.Pressure, Time.time, Guard.State.Fatigue, Guard.EndurancePoints, threatened, opening);
        }

        void BeginWindup(bool newCombo)
        {
            if (Time.time < nextAttackAt)
            {
                BeginGuard();
                stateUntil = Mathf.Max(stateUntil, nextAttackAt);
                return;
            }
            state = State.Windup;
            stateUntil = Time.time + windup;
            Guard.State.RecordCombat(Time.time);
            Vector3 delta = Vector3.ProjectOnPlane(TargetTransform.position - transform.position, Vector3.up);
            if (delta.sqrMagnitude > .001f) facing = delta.normalized;
            if (newCombo) comboRemaining = tactics.Aggressive ? 1 : 0;
            Guard.State.SetBlocking(false, Time.time);
        }

        bool TryDodgeLunge(Vector3 delta)
        {
            if (!CanStartRoll) return false;
            Vector3 lateral = Vector3.Cross(Vector3.up, delta.normalized);
            float distance = rollSpeed * rollDuration;
            foreach (Vector3 direction in new[] { lateral, -lateral, -delta.normalized })
                if (health.CanMove(direction * distance) && BeginRoll(direction, distance, false)) return true;
            return false;
        }

        bool TryCornerRoll(Vector3 delta)
        {
            if (!CanStartRoll || delta.magnitude > TargetReach + 1) return false;
            Physics.SyncTransforms();
            Vector3 origin = transform.position + Vector3.up * .6f;
            Vector3 firstNormal = Vector3.zero;
            bool cornered = false;
            for (int index = 0; index < 8; index++)
            {
                Vector3 direction = Quaternion.AngleAxis(index * 45, Vector3.up) * Vector3.right;
                if (!Physics.SphereCast(origin, .36f, direction, out RaycastHit contact,
                    cornerProbeDistance, ~0, QueryTriggerInteraction.Ignore)
                    || contact.collider.GetComponentInParent<PlayerMotor>() != null
                    || Mathf.Abs(contact.normal.y) > .4f) continue;
                if (firstNormal.sqrMagnitude < .01f) firstNormal = contact.normal;
                else if (Mathf.Abs(Vector3.Dot(firstNormal, contact.normal)) < .6f) cornered = true;
            }
            if (!cornered) return false;
            float bestScore = float.NegativeInfinity;
            Vector3 escape = Vector3.zero;
            float distance = rollSpeed * rollDuration;
            for (int index = 0; index < 16; index++)
            {
                Vector3 direction = Quaternion.AngleAxis(index * 22.5f, Vector3.up) * Vector3.right;
                if (!health.CanMove(direction * distance)) continue;
                Vector3 destination = transform.position + direction * distance;
                float score = Vector3.Distance(destination, TargetTransform.position);
                for (int probe = 0; probe < 8; probe++)
                {
                    Vector3 outward = Quaternion.AngleAxis(probe * 45, Vector3.up) * Vector3.right;
                    if (Physics.SphereCast(destination + Vector3.up * .6f, .36f, outward,
                        out RaycastHit contact, 2, ~0, QueryTriggerInteraction.Ignore)
                        && contact.collider.GetComponentInParent<PlayerMotor>() == null)
                        score -= (2 - contact.distance) * .5f;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                escape = direction;
            }
            return escape.sqrMagnitude > .01f && BeginRoll(escape, distance, false);
        }

        bool TryApproachRoll(Vector3 delta)
        {
            float distance = delta.magnitude;
            float projectedPressure = Guard.Pressure + (rollPressure + 45) * 100f / Guard.EndurancePoints;
            if (!CanStartRoll || tactics.Retreating || !tactics.Aggressive || projectedPressure >= 90
                || distance < AttackReach + 1 || distance > approachRollRange) return false;
            float travel = Mathf.Min(rollSpeed * rollDuration, distance - 1.7f);
            float bestDistance = distance - 1;
            Vector3 approach = Vector3.zero;
            foreach (float angle in ApproachAngles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * delta.normalized;
                if (!health.CanMove(direction * travel)) continue;
                float remaining = (delta - direction * travel).magnitude;
                if (remaining < 1.2f || remaining >= bestDistance) continue;
                bestDistance = remaining;
                approach = direction;
            }
            if (approach.sqrMagnitude < .01f || !BeginRoll(approach, travel, true)) return false;
            tactics.Reengage(Guard.Pressure, Time.time);
            return true;
        }

        bool BeginRoll(Vector3 direction, float distance, bool closing)
        {
            if (!CanStartRoll) return false;
            if (!Guard.State.TrySpendForRoll(rollPressure, Time.time)) return false;
            state = State.Roll;
            rollDirection = direction;
            currentRollSpeed = distance / rollDuration;
            closingRoll = closing;
            rollRemaining = rollDuration;
            nextRollAt = Time.time + rollCooldown;
            nextAttackAt = Mathf.Max(nextAttackAt, Time.time + rollDuration + .3f);
            comboRemaining = 0;
            warning.enabled = false;
            lastSwingAt = float.NegativeInfinity;
            CombatAudio.Play(CombatSound.Roll, transform.position);
            return true;
        }

        void BeginGuard()
        {
            state = State.Guard;
            stateUntil = Time.time + (tactics.Aggressive ? .26f : .65f);
            Guard.State.SetBlocking(true, Time.time);
        }

        void PrepareDefense(Vector3 towardAttack, bool newAttack)
        {
            if (!CanGuard) return;
            bool emergency = state == State.Windup || (state == State.Recovery && Time.time < stateUntil)
                || (towardAttack.sqrMagnitude > .001f && Vector3.Angle(facing, towardAttack) > 70);
            if (newAttack) Guard.State.SetBlocking(false, Time.time);
            if (emergency)
            {
                Guard.State.TryEmergencyGuard(emergencyGuardPressure, Time.time);
                state = State.Guard;
                stateUntil = Time.time + (tactics.Aggressive ? .26f : .65f);
                comboRemaining = 0;
                lastSwingAt = float.NegativeInfinity;
                warning.enabled = false;
            }
            if (towardAttack.sqrMagnitude > .001f) facing = towardAttack.normalized;
            Guard.SetFacing(facing);
            Guard.State.SetBlocking(CanGuard, Time.time);
        }

        public void ReceiveAttack(int damage, Vector3 direction, CombatGuard attacker, bool unblockable = false)
        {
            if (!health.IsAlive || Rolling) return;
            bool newAttack = TargetThreatening && !observedPlayerAttack;
            if (TargetThreatening) observedPlayerAttack = true;
            if (!unblockable) PrepareDefense(-direction, newAttack);
            if (!unblockable && Guard.Defend(attacker, direction, Random.value < parryChance) != DefenseResult.Hit)
            {
                if (Guard.Disrupted) { state = State.Recovery; warning.enabled = false; }
                return;
            }
            bool concentrationBroken = Guard.Broken;
            int previousHealth = health.Health;
            health.TakeHit(damage, direction);
            if ((concentrationBroken || unblockable) && health.Health < previousHealth)
                blood.Burst(transform.position + Vector3.up * .8f, direction);
            Guard.OnHit();
            Guard.State.SetBlocking(false, Time.time);
            state = State.Recovery;
            stateUntil = Time.time + stunDuration;
            comboRemaining = 0;
            warning.enabled = false;
        }

        void LateUpdate()
        {
            float prepare = state == State.Windup ? 1 - Mathf.Clamp01((stateUntil - Time.time) / windup) : 0;
            float recoveryDuration = tactics.Aggressive ? recovery * .75f : recovery;
            float swing = state == State.Recovery && Time.time - lastSwingAt < recoveryDuration
                ? Mathf.Clamp01((Time.time - lastSwingAt) / .16f) : -1;
            float returnToReady = Mathf.Clamp01((Time.time - lastSwingAt - .16f) / Mathf.Max(.01f, recoveryDuration - .16f));
            weapon.Pose(facing, health.IsAlive && !Rolling, Guard.Blocking, Guard.Disrupted, swing, prepare,
                AttackReach, halfAngle, returnToReady, CircularAttacks);
            if (!health.IsAlive || visual == null) return;
            Camera camera = Camera.main;
            float side = Vector3.Dot(facing, camera.transform.right);
            float thrust = swing >= 0 ? Mathf.Sin(swing * Mathf.PI) : 0;
            visual.flipX = side < 0;
            float bob = (state == State.Chase || state == State.Retreat) && TargetAlive ? Mathf.Abs(Mathf.Sin(Time.time * 12)) * .07f : 0;
            visual.transform.localPosition = Vector3.up * (.85f + bob)
                + facing * (thrust * .1f - prepare * .07f);
            visual.transform.localScale = Rolling ? new Vector3(1.1f, .7f, 1)
                : state == State.Windup ? new Vector3(1.15f, .9f, 1) : Vector3.one;
            visual.transform.rotation = camera.transform.rotation * (Rolling
                ? Quaternion.Euler(0, 0, -360 * (1 - rollRemaining / rollDuration))
                : Quaternion.Euler(0, side * 18, side * (prepare * 8 - thrust * 10)));
        }

        bool HasLineOfSight(Vector3 delta)
        {
            return !Physics.Raycast(transform.position + Vector3.up * .65f, delta.normalized, out RaycastHit hit,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(TargetTransform);
        }

        void Chase(Vector3 delta)
        {
            if (delta.sqrMagnitude < .01f) return;
            float speed = moveSpeed * (tactics.Aggressive ? aggressiveSpeedMultiplier : 1);
            MoveWithSteering(delta.normalized, Mathf.Min(speed * Time.deltaTime, delta.magnitude), Vector3.zero);
        }

        void BeginRetreat()
        {
            state = State.Retreat;
            lastSwingAt = float.NegativeInfinity;
            warning.enabled = false;
            comboRemaining = 0;
            retreatSide = -retreatSide;
            nextSideChange = Time.time + .7f;
            Guard.State.SetBlocking(true, Time.time);
        }

        void Retreat(Vector3 delta)
        {
            float distance = delta.magnitude;
            float recoveryDistance = Mathf.Max(safeDistance, TargetReach + 2);
            if (distance > recoveryDistance + .5f) return; // Keep guard up and recover at a safe gap.
            Vector3 toward = distance > .01f ? delta / distance : facing;
            Vector3 away = -toward;
            Vector3 lateral = Vector3.Cross(Vector3.up, toward) * retreatSide;
            float step = retreatSpeed * Time.deltaTime;
            // Favor opening the gap: the old diagonal's radial speed was below the player's run speed.
            Vector3 direction = (away * (distance < recoveryDistance ? 1.25f : .2f) + lateral * .45f).normalized;
            if (MoveWithSteering(direction, step, away)) return;
            if (Time.time < nextSideChange) return;
            // At a wall or corner, change sides rather than bouncing every frame or running toward the player.
            retreatSide = -retreatSide;
            nextSideChange = Time.time + .7f;
            direction = (away * 1.25f + Vector3.Cross(Vector3.up, toward) * retreatSide * .45f).normalized;
            MoveWithSteering(direction, step, away);
        }

        bool MoveWithSteering(Vector3 direction, float step, Vector3 retreatAway)
        {
            foreach (float angle in SteeringAngles)
            {
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (retreatAway.sqrMagnitude > .01f && Vector3.Dot(candidate, retreatAway) < -.05f) continue;
                if (Physics.SphereCast(transform.position + Vector3.up * .6f, .36f, candidate,
                    out _, Mathf.Max(.5f, step + .05f), ~0, QueryTriggerInteraction.Ignore)) continue;
                if (health.Move(candidate * step)) return true;
            }
            return false;
        }

        void DrawWarning()
        {
            warning.enabled = ShowAttackRangeIndicators;
            if (!warning.enabled) return;
            warning.loop = CircularAttacks;
            warning.positionCount = CircularAttacks ? 48 : 23;
            Vector3 origin = transform.position + Vector3.up * .04f;
            float heading = Mathf.Atan2(facing.z, facing.x);
            if (!CircularAttacks) warning.SetPosition(0, origin);
            int segments = CircularAttacks ? 48 : 21;
            for (int i = 0; i < segments; i++)
            {
                float angle = heading + (CircularAttacks ? i * Mathf.PI * 2 / segments
                    : Mathf.Lerp(-halfAngle, halfAngle, i / 20f) * Mathf.Deg2Rad);
                warning.SetPosition(CircularAttacks ? i : i + 1, origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * AttackReach);
            }
            if (!CircularAttacks) warning.SetPosition(22, origin);
            float progress = 1 - Mathf.Clamp01((stateUntil - Time.time) / windup);
            warning.startWidth = warning.endWidth = .04f + progress * .1f;
            warning.startColor = warning.endColor = Color.Lerp(new Color(1, .4f, .15f), new Color(1, .12f, .1f), progress);
        }
    }
}
