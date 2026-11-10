using UnityEngine;
using System.Collections.Generic;

namespace Parry
{
    [RequireComponent(typeof(PracticeTarget), typeof(CombatGuard))]
    public sealed class EnemyBrain : MonoBehaviour
    {
        [Header("Durability")]
        [SerializeField] int maxHealth = 2;
        [SerializeField] bool autoRespawn;
        public bool AutoRespawn => autoRespawn;
        [SerializeField] float hitKnockback = 3;
        [Header("Endurance (baseline 100)")]
        [SerializeField, Min(1)] int endurance = 100;
        [Header("Chase")]
        [SerializeField] float moveSpeed = 4.2f;
        [SerializeField] float attackDistance = 2.1f;
        [SerializeField] float stunDuration = .23f;
        [Header("Attack")]
        [SerializeField] float windup = .24f;
        [SerializeField] float recovery = .5f;
        [SerializeField] float attackInterval = .65f;
        [SerializeField] float reach = 2.5f;
        [SerializeField, Range(1, 180)] float halfAngle = 35;
        [SerializeField] int damage = 1;
        [SerializeField, Min(0)] float attackPressure = 15f;
        [Header("Emergency defense")]
        [SerializeField] float emergencyGuardPressure = 45;
        [SerializeField, Range(0, 1)] float parryChance = .5f;
        [SerializeField, Min(0)] float counterattackDelayAfterParry = .7f;
        [SerializeField, Min(0)] float counterattackDelayAfterBlock = 1.2f;

        [Header("Concentration tactics")]
        [SerializeField, Min(.1f)] float retreatDecisionInterval = 1f;
        [SerializeField] float retreatSpeed = 9f;
        [SerializeField] float safeDistance = 5.2f;
        [SerializeField] float aggressiveSpeedMultiplier = 1.25f;
        [Header("Powerful lunge")]
        [SerializeField, Min(.1f)] float lungeWindup = .9f;
        [SerializeField, Min(.05f)] float lungeActiveTime = .12f;
        [SerializeField, Min(.1f)] float lungeRecovery = .65f;
        [SerializeField, Min(0)] float lungeDistance = 1.4f;
        [SerializeField, Min(0)] float lungePressure = 195f;
        [SerializeField, Min(1)] float lungeCooldown = 5f;
        [SerializeField, Range(0, 1)] float lungeChance = .35f;
        bool performingLunge, lungeHit;
        float nextLungeAt, lungeElapsed;
        [Header("Tactical rolls")]
        [SerializeField] float rollSpeed = 14f;
        [SerializeField] float rollDuration = .28f;
        [SerializeField] float rollCooldown = 3f;
        [SerializeField] float rollPressure = 20f;
        [SerializeField] float cornerProbeDistance = .9f;
        [SerializeField] float approachRollRange = 7f;

        [Header("Group tactics")]
        [SerializeField, Min(1)] float coordinationRadius = 8f;
        static readonly List<EnemyBrain> activeEnemies = new List<EnemyBrain>();
        void OnEnable() { activeEnemies.Add(this); }
        void OnDisable() { activeEnemies.Remove(this); }

        enum State { Chase, Guard, Windup, Lunge, Recovery, Retreat, Roll }
        State state;
        [SerializeField] PlayerMotor player;
        [SerializeField] EnemyBrain opponent;
        Transform TargetTransform => opponent != null ? opponent.transform : player != null ? player.transform : null;
        bool TargetAlive => opponent != null ? opponent.Health.IsAlive : player != null && player.IsAlive;
        bool TargetThreatening => opponent != null ? opponent.AttackThreatening : player != null && player.AttackThreatening;
        float TargetReach => opponent != null ? opponent.ThreatReach : player != null ? player.ThreatReach : 0;
        bool TargetUnblockable => opponent != null ? opponent.UnblockableAttack : player != null && player.UnblockableAttack && player.AttackThreatening;
        CombatGuard TargetGuard => opponent != null ? opponent.Guard : player.Guard;
        public AttackPhase ClashPhase => !health.IsAlive ? AttackPhase.None
            : state == State.Lunge ? AttackPhase.Active
            : state == State.Windup ? (Time.time < stateUntil ? AttackPhase.Windup : AttackPhase.Active)
            : state == State.Recovery && Time.time >= lastSwingAt && Time.time - lastSwingAt < CurrentActive
                ? AttackPhase.Active : AttackPhase.None;
        public Vector3 AttackFacing => facing;
        public void InterruptAttack()
        {
            Guard.State.CompleteStrongAttack(Time.time);
            performingLunge = false;
            state = State.Recovery;
            stateUntil = Time.time + .35f;
            lastSwingAt = float.NegativeInfinity;
            comboRemaining = 0;
            warning.enabled = false;
            Guard.State.SetBlocking(false, Time.time);
        }
        public bool UnblockableAttack => performingLunge && state == State.Lunge;
        public bool PowerfulThreat => performingLunge && (state == State.Windup || state == State.Lunge);
        public bool AttackThreatening => health.IsAlive && !Guard.Disrupted
            && (state == State.Lunge || state == State.Windup && !performingLunge);
        public void Initialize(EnemyBrain target) { opponent = target; player = null; }
        bool AttackTarget(int amount, Vector3 direction)
        {
            CombatHitbox hitbox = opponent != null ? opponent.Health.Hitbox : player != null ? player.Hitbox : null;
            if (hitbox == null || !hitbox.IntersectsAttack(transform.position + Vector3.up * .8f,
                facing, AttackReach, performingLunge ? 20 : halfAngle, CircularAttacks && !performingLunge)) return false;
            if (opponent != null) opponent.ReceiveAttack(amount, direction, Guard, performingLunge);
            else if (player != null) player.ReceiveAttack(amount, direction, Guard, performingLunge);
            return true;
        }
        PracticeTarget health;
        SpriteRenderer visual;
        Transform shadow;
        LineRenderer warning;
        Vector3 facing = Vector3.back;
        float stateUntil, observedHitTime = float.NegativeInfinity;
        bool wasAlive = true;
        bool observedPlayerAttack;
        readonly EnemyTactics tactics = new EnemyTactics(() => Random.value);
        int comboRemaining;
        float retreatSide = 1, nextSideChange;
        float lastSwingAt = float.NegativeInfinity;
        float nextAttackAt;
        float nextRollAt, rollRemaining;
        float currentRollSpeed;
        bool closingRoll;
        Vector3 rollDirection;
        public bool Rolling => state == State.Roll;
        public float RollProgress => 1 - Mathf.Clamp01(rollRemaining / Mathf.Max(.001f, rollDuration));
        public string TacticalStatus => !health.IsAlive ? "DEFEATED" : Guard.Disrupted ? "STAGGERED"
            : Rolling ? (closingRoll ? "CLOSING" : "ESCAPING")
            : Guard.State.Relaxing ? "RELAXING" : tactics.Retreating ? "RECOVERING" : tactics.FullOffense ? "FULL OFFENSE" : tactics.Aggressive ? "PRESSING" : "CAUTIOUS";
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
        public float ThreatReach => AttackReach + (UnblockableAttack ? lungeDistance : 0);
        float attackWindupMultiplier = 1;
        float CurrentWindup => (performingLunge ? lungeWindup : windup) * attackWindupMultiplier;
        float CurrentActive => performingLunge ? lungeActiveTime : .1f;
        float AttackRecovery => performingLunge ? lungeRecovery : recovery * (tactics.FullOffense ? .55f : tactics.Aggressive ? .75f : 1);
        float GuardWait => tactics.FullOffense ? .1f : tactics.Aggressive ? .26f : .65f;
        float AttackDistance => attackDistance * AttackReach / Mathf.Max(.1f, reach);
        static readonly float[] SteeringAngles = { 0, 45, -45, 90, -90, 135, -135 };
        static readonly float[] ApproachAngles = { 0, 22.5f, -22.5f, 45, -45 };
        bool CanGuard => !Guard.Disrupted && !Rolling && state != State.Lunge;
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
            CharacterPresentation.Install(gameObject, visual, true);
            health.SetBodyColor(Color.white);
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
                Guard.State.CompleteStrongAttack(Time.time);
                performingLunge = false;
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
                attackWindupMultiplier = 1;
                tactics.Reset();
                comboRemaining = 0;
                state = State.Recovery;
                stateUntil = Time.time + .8f;
            }
            if (!TargetAlive)
            {
                Guard.State.CompleteStrongAttack(Time.time);
                performingLunge = false;
                state = State.Chase;
                comboRemaining = 0;
                observedPlayerAttack = false;
                tactics.Reset();
                Guard.Tick(false, facing);
                return;
            }
            if (health.LastHitTime > observedHitTime)
            {
                Guard.State.CompleteStrongAttack(Time.time);
                performingLunge = false;
                observedHitTime = health.LastHitTime;
                Guard.State.CancelRelaxation();
                state = State.Recovery;
                stateUntil = Time.time + stunDuration;
            }
            Vector3 delta = Vector3.ProjectOnPlane(TargetTransform.position - transform.position, Vector3.up);
            if (state == State.Lunge)
            {
                Guard.Tick(false, facing);
                if (Guard.Disrupted) InterruptAttack();
                else AdvanceLunge(Time.deltaTime);
                return;
            }
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
            UpdateTactics(delta);
            bool newAttack = TargetThreatening && !observedPlayerAttack;
            observedPlayerAttack = TargetThreatening;
            if (TargetUnblockable && delta.magnitude <= TargetReach + .5f && HasLineOfSight(delta)
                && TryDodgeLunge(delta)) return;
            if (!tactics.ExploitingWeakness && !TargetUnblockable && state != State.Windup && TargetThreatening
                && delta.magnitude <= TargetReach + .3f && HasLineOfSight(delta))
                PrepareDefense(delta, newAttack);
            Guard.Tick(CanGuard && !(state == State.Retreat && Guard.State.Relaxing) && state != State.Windup
                && (state != State.Recovery || Time.time >= stateUntil), facing);
            if (Guard.Disrupted)
            {
                Guard.State.CompleteStrongAttack(Time.time);
                performingLunge = false;
                state = State.Recovery;
                stateUntil = Time.time + .25f;
                comboRemaining = 0;
                return;
            }
            if (TryCornerRoll(delta)) return;
            if (TryFlankPlayer(delta)) return;
            if (TryApproachRoll(delta)) return;
            if (tactics.Retreating && state != State.Retreat && (state != State.Windup || tactics.UrgentRetreat && !performingLunge)
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
                Guard.State.LeaveRelaxation();
                state = State.Chase;
            }
            if (state == State.Chase)
            {
                if (delta.sqrMagnitude > .001f) facing = delta.normalized;
                if ((delta.magnitude <= AttackDistance || CanConsiderLunge(delta) || (TargetThreatening && delta.magnitude <= AttackReach + .3f)) && HasLineOfSight(delta))
                {
                    if ((!TargetThreatening || tactics.ExploitingWeakness) && (TargetGuard.Broken || tactics.Aggressive)) BeginWindup(true);
                    else BeginGuard();
                }
                else Chase(delta);
            }
            else if (state == State.Guard)
            {
                if (Time.time >= stateUntil || (tactics.ExploitingWeakness && Time.time >= nextAttackAt))
                {
                    if (TargetThreatening && !tactics.ExploitingWeakness) return;
                    if ((delta.magnitude > AttackReach && !CanConsiderLunge(delta)) || !HasLineOfSight(delta)) state = State.Chase;
                    else BeginWindup(true);
                }
            }
            else if (state == State.Windup)
            {
                DrawWarning();
                if (Time.time >= stateUntil)
                {
                    if (performingLunge)
                    {
                        if (!Guard.State.TrySpendForStrongAttack(lungePressure, Time.time)) { InterruptAttack(); return; }
                        state = State.Lunge;
                        lungeElapsed = 0; lungeHit = false;
                        lastSwingAt = Time.time;
                        nextAttackAt = Time.time + lungeActiveTime + lungeRecovery;
                        Guard.AnnounceAttack(0, AttackReach + lungeDistance);
                        CombatAudio.Play(CombatSound.Swing, transform.position);
                        AdvanceLunge(Mathf.Min(Time.deltaTime, lungeActiveTime));
                        return;
                    }
                    // Direction stays locked from the start of the visible windup.
                    lastSwingAt = Time.time;
                    CombatAudio.Play(CombatSound.Swing, transform.position);
                    nextAttackAt = Time.time + attackInterval * (tactics.FullOffense ? .7f : 1);
                    if (HasLineOfSight(delta))
                        AttackTarget(damage, CircularAttacks && delta.sqrMagnitude > .001f ? delta.normalized : facing);
                    state = State.Recovery;
                    stateUntil = Time.time + AttackRecovery;
                }
            }
            else if (Time.time >= stateUntil)
            {
                performingLunge = false;
                if (TargetThreatening && !tactics.FullOffense) BeginGuard();
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
            bool seesTarget = HasLineOfSight(delta);
            bool threatened = TargetThreatening && delta.magnitude <= TargetReach + .5f && seesTarget;
            bool opening = TargetGuard.Disrupted && delta.magnitude <= AttackReach && seesTarget;
            tactics.Update(Guard.Pressure, Time.time, Guard.EndurancePoints, threatened, opening,
                seesTarget ? TargetGuard.Pressure : 0, seesTarget && TargetGuard.Disrupted, retreatDecisionInterval);
        }

        void BeginWindup(bool newCombo)
        {
            float attackReadyAt = Mathf.Max(nextAttackAt, tactics.AttackBlockedUntil);
            if (Time.time < attackReadyAt)
            {
                BeginGuard();
                stateUntil = Mathf.Max(stateUntil, attackReadyAt);
                return;
            }
            Vector3 targetDelta = Vector3.ProjectOnPlane(TargetTransform.position - transform.position, Vector3.up);
            performingLunge = newCombo && CanConsiderLunge(targetDelta) && Random.value < lungeChance;
            if (!performingLunge && targetDelta.magnitude > AttackReach) { state = State.Chase; return; }
            attackWindupMultiplier = Guard.State.ExitRelaxationForAttack();
            if (!performingLunge && !Guard.State.TrySpendForAttack(attackPressure, Time.time))
            {
                BeginGuard();
                return;
            }
            state = State.Windup;
            stateUntil = Time.time + CurrentWindup;
            if (!performingLunge) Guard.AnnounceAttack(CurrentWindup, AttackReach);
            else { nextLungeAt = Time.time + lungeCooldown; comboRemaining = 0; }
            Guard.State.RecordCombat(Time.time);
            Vector3 delta = Vector3.ProjectOnPlane(TargetTransform.position - transform.position, Vector3.up);
            if (delta.sqrMagnitude > .001f) facing = delta.normalized;
            if (newCombo && !performingLunge) comboRemaining = tactics.FullOffense ? 2 : tactics.Aggressive ? 1 : 0;
            Guard.State.SetBlocking(false, Time.time);
        }

        bool CanConsiderLunge(Vector3 delta) => Time.time >= nextLungeAt && !TargetThreatening
            && delta.magnitude >= 1.2f && delta.magnitude <= AttackReach + lungeDistance * .8f
            && (Guard.RemainingEndurance > lungePressure || tactics.FullOffense)
            && !tactics.Retreating && HasLineOfSight(delta);

        void AdvanceLunge(float dt)
        {
            float elapsed = Mathf.Min(dt, lungeActiveTime - lungeElapsed);
            // Small movement steps also test the blade along its path, avoiding skipped hits.
            int steps = Mathf.Max(1, Mathf.CeilToInt(lungeDistance * elapsed / lungeActiveTime / .15f));
            for (int i = 0; i < steps && state == State.Lunge && !Guard.Disrupted; i++)
            {
                health.Move(facing * (lungeDistance * elapsed / lungeActiveTime / steps));
                if (!lungeHit && HasLineOfSight(TargetTransform.position - transform.position))
                    lungeHit = AttackTarget(damage, facing);
            }
            lungeElapsed += elapsed;
            if (state != State.Lunge) return;
            if (Guard.Disrupted || lungeElapsed >= lungeActiveTime)
            {
                Guard.State.CompleteStrongAttack(Time.time);
                state = State.Recovery;
                stateUntil = Time.time + lungeRecovery;
            }
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
            if (!CanStartRoll || tactics.ExploitingWeakness || delta.magnitude > TargetReach + 1) return false;
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
            Guard.State.CompleteStrongAttack(Time.time);
            performingLunge = false;
            state = State.Guard;
            stateUntil = Time.time + GuardWait;
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
                stateUntil = Time.time + GuardWait;
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
            if (!health.IsAlive) return;
            if (Guard.TryClash(attacker, direction, unblockable)) return;
            bool newAttack = TargetThreatening && !observedPlayerAttack;
            if (TargetThreatening) observedPlayerAttack = true;
            PrepareDefense(-direction, newAttack);
            DefenseResult defense = Guard.Defend(attacker, direction, Random.value < parryChance, unblockable);
            if (defense != DefenseResult.Hit)
            {
                comboRemaining = 0;
                if (defense == DefenseResult.Parry)
                {
                    tactics.RecordParry(Time.time, counterattackDelayAfterParry);
                    state = State.Guard;
                    stateUntil = tactics.AttackBlockedUntil;
                }
                else
                {
                    tactics.RecordBlock(Time.time, counterattackDelayAfterBlock);
                    BeginGuard();
                    stateUntil = Mathf.Max(stateUntil, tactics.AttackBlockedUntil);
                }
                if (Guard.Disrupted) { state = State.Recovery; warning.enabled = false; }
                return;
            }
            Guard.State.CompleteStrongAttack(Time.time);
            performingLunge = false;
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
            float prepare = state == State.Windup ? 1 - Mathf.Clamp01((stateUntil - Time.time) / Mathf.Max(.001f, CurrentWindup)) : 0;
            float recoveryDuration = AttackRecovery;
            float swing = state == State.Lunge ? Mathf.Clamp01(lungeElapsed / lungeActiveTime)
                : state == State.Recovery && Time.time - lastSwingAt < recoveryDuration
                ? Mathf.Clamp01((Time.time - lastSwingAt) / CurrentActive) : -1;
            float returnToReady = Mathf.Clamp01((Time.time - lastSwingAt - CurrentActive) / Mathf.Max(.01f, recoveryDuration - CurrentActive));
            weapon.Pose(facing, health.IsAlive && !Rolling, Guard.Blocking, Guard.Disrupted, swing, prepare,
                AttackReach, performingLunge ? 20 : halfAngle, returnToReady, CircularAttacks && !performingLunge,
                lunge: performingLunge, powerful: performingLunge, thrustStance: performingLunge, relaxing: Guard.State.Relaxing);
            weapon.ChargeGlint(prepare, performingLunge && state == State.Windup);
            if (!health.IsAlive || visual == null) return;
            Camera camera = Camera.main;
            float side = Vector3.Dot(facing, camera.transform.right);
            float thrust = swing >= 0 ? Mathf.Sin(Mathf.Pow(swing, .55f) * Mathf.PI) : 0;
            float tension = Mathf.SmoothStep(0, 1, Mathf.Clamp01(prepare / .78f));
            visual.flipX = side < 0;
            float bob = (state == State.Chase || state == State.Retreat) && TargetAlive ? Mathf.Abs(Mathf.Sin(Time.time * 12)) * .07f : 0;
            visual.transform.localPosition = Vector3.up * (.85f + bob)
                + facing * (thrust * .16f - tension * .13f);
            visual.transform.localScale = Rolling ? new Vector3(1.1f, .7f, 1)
                : state == State.Windup ? new Vector3(1.15f, .9f, 1) : Vector3.one;
            visual.transform.rotation = camera.transform.rotation * (Rolling
                ? Quaternion.Euler(0, 0, -360 * (1 - rollRemaining / rollDuration))
                : Quaternion.Euler(0, side * 18, side * (tension * 13 - thrust * 15)));
        }

        bool HasLineOfSight(Vector3 delta)
        {
            if (SmokeCloud.Obscures(transform.position, transform.position + delta)) return false;
            return !Physics.Raycast(transform.position + Vector3.up * .65f, delta.normalized, out RaycastHit hit,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(TargetTransform);
        }

        bool TryFlankPlayer(Vector3 delta)
        {
            if (opponent != null || player == null || tactics.Retreating
                || (state != State.Chase && state != State.Guard)
                || delta.sqrMagnitude < .01f || delta.sqrMagnitude > coordinationRadius * coordinationRadius)
                return false;

            int count = 0, rank = 0;
            EnemyBrain anchor = null;
            foreach (var ally in activeEnemies)
            {
                if (ally == null || ally.health == null || !ally.health.IsAlive
                    || ally.player != player || ally.opponent != null || ally.arena != arena
                    || Vector3.ProjectOnPlane(ally.transform.position - player.transform.position, Vector3.up).sqrMagnitude
                        > coordinationRadius * coordinationRadius) continue;
                if (anchor == null) anchor = ally;
                if (ally == this) rank = count;
                count++;
            }
            if (count < 2) return false;
            Vector3 outward = -delta.normalized;
            Vector3 anchorOutward = Vector3.ProjectOnPlane(anchor.transform.position - player.transform.position, Vector3.up);
            if (anchorOutward.sqrMagnitude < .01f) anchorOutward = Vector3.forward;
            float heading = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
            float anchorHeading = Mathf.Atan2(anchorOutward.x, anchorOutward.z) * Mathf.Rad2Deg;
            float turn = EnemyFlankingRules.TurnToSlot(heading, anchorHeading, rank, count);
            if (Mathf.Abs(turn) <= Mathf.Min(35, 108f / count)) return false;

            // Follow the ring instead of taking a shortcut through the player's position.
            Vector3 tangent = Vector3.Cross(Vector3.up, outward) * Mathf.Sign(turn);
            float radius = Mathf.Max(1.2f, AttackDistance * .95f);
            float radial = Mathf.Clamp((radius - delta.magnitude) / radius, -1, 1);
            Vector3 direction = (tangent + outward * radial).normalized;
            float step = moveSpeed * Time.deltaTime;
            // A blocked route must still allow normal combat rather than an endless flank attempt.
            return MoveWithSteering(direction, step, Vector3.zero);
        }

        void Chase(Vector3 delta)
        {
            if (delta.sqrMagnitude < .01f) return;
            float speed = moveSpeed * (tactics.FullOffense ? aggressiveSpeedMultiplier * 1.2f
                : tactics.Aggressive ? aggressiveSpeedMultiplier : 1);
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
            Guard.State.BeginRelaxation(Time.time);

        }

        void Retreat(Vector3 delta)
        {
            float distance = delta.magnitude;
            float recoveryDistance = Mathf.Max(safeDistance, TargetReach + 2);
            if (distance > recoveryDistance + .5f) return; // Keep guard up and recover at a safe gap.
            Vector3 toward = distance > .01f ? delta / distance : facing;
            Vector3 away = -toward;
            Vector3 lateral = Vector3.Cross(Vector3.up, toward) * retreatSide;
            float step = retreatSpeed * (Guard.State.Relaxing || Guard.State.GuardTransition ? .3f : 1) * Time.deltaTime;
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
            float progress = 1 - Mathf.Clamp01((stateUntil - Time.time) / Mathf.Max(.001f, CurrentWindup));
            warning.startWidth = warning.endWidth = .04f + progress * .1f;
            warning.startColor = warning.endColor = Color.Lerp(new Color(1, .4f, .15f), new Color(1, .12f, .1f), progress);
        }
    }
}
