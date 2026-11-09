using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Parry
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(CharacterController), typeof(CombatGuard))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movement on XZ")]
        [SerializeField] float speed = 8f;
        [SerializeField] float acceleration = 48f;
        [SerializeField] float rollSpeed = 15f;
        [SerializeField] float rollDuration = .23f;
        [SerializeField] float rollCooldown = .6f;
        [SerializeField] float rollPressure = 50f;
        [SerializeField, Min(0)] float rollChainPressureIncrement = 25f;
        [SerializeField, Min(0)] float rollChainResetDelay = 2f;
        [Header("Melee")]
        [SerializeField] float windup = .21f;
        [SerializeField] float activeTime = .09f;
        [SerializeField] float recovery = .12f;
        [SerializeField] float reach = 2.5f;
        [SerializeField, Range(1, 180)] float halfAngle = 35f;
        [SerializeField] int damage = 1;
        [Header("Special attack: lunge")]
        [SerializeField, Min(0)] float preparationPressurePerSecond = 5f;
        [SerializeField, Min(0)] float strongAttackPressure = 195f;
        [SerializeField] float lungeWindup = .9f;
        [SerializeField, Min(1)] float preparedRunSpeedMultiplier = 1.5f;
        [SerializeField] float lungeActiveTime = .12f;
        [SerializeField] float lungeRecovery = .32f;
        [SerializeField] float lungeDistance = 1.4f;
        [SerializeField] float lungeWidth = .3f;
        [Header("Survival")]
        [SerializeField] int maxHealth = 2;
        [SerializeField] float hurtInvulnerability = .8f;
        [Header("Endurance (baseline 100)")]
        [SerializeField, Min(1)] int endurance = 100;
        public int Health { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsAlive => Health > 0;
        public CombatGuard Guard { get; private set; }
        public CombatHitbox Hitbox { get; private set; }
        public Vector3 Facing { get; private set; } = Vector3.back;
        public bool Rolling => rollRemaining > 0;
        public bool Attacking => attackRemaining > 0;
        float attackStartedAt;
        public AttackPhase ClashPhase => PreparingPowerfulAttack ? AttackPhase.Windup : !Attacking ? AttackPhase.None
            : AttackClashRules.Phase(Time.time - attackStartedAt, CurrentWindup, CurrentActive, CurrentRecovery);
        public Vector3 AttackFacing => Attacking ? attackDirection : Facing;
        public bool PreparingPowerfulAttack => !Attacking && Style == WeaponStyle.Thrusting && preparation.Active;
        public bool UnblockableThreat => UnblockableAttack || PreparingPowerfulAttack;
        public bool AttackThreatening => Attacking && attackRemaining > CurrentRecovery;
        public WeaponStyle Style { get; private set; } = WeaponStyle.Normal;
        bool ThrustAttack => activeAttack == AttackChoice.Thrust || activeAttack == AttackChoice.Lunge;
        public bool UnblockableAttack => Attacking && activeAttack == AttackChoice.Lunge;
        public float AttackReach => arena != null ? arena.BaseAttackRadius : reach;
        public float ThreatReach => CurrentReach + (UnblockableAttack ? lungeDistance : 0);
        float CurrentWindup => activeAttack == AttackChoice.Lunge ? 0 : windup;
        float CurrentActive => activeAttack == AttackChoice.Lunge ? lungeActiveTime : activeTime;
        float CurrentRecovery => activeAttack == AttackChoice.Lunge ? lungeRecovery : recovery;
        float CurrentReach => AttackReach * (UnblockableAttack ? .85f : 1);
        bool CircularStrike => CircularAttacks && !ThrustAttack;
        public float RollReady => Mathf.Clamp01(1 - (nextRoll - Time.time) / rollCooldown);
        CharacterController controller;
        Camera view;
        SpriteRenderer visual;
        LineRenderer slash;
        ArenaDemo arena;
        bool CircularAttacks => arena != null && arena.CircularAttacks;
        Vector3 velocity, rollDirection, attackDirection;
        float rollRemaining, nextRoll, attackRemaining, bufferedUntil;
        bool buffered;
        bool swingSoundPlayed;
        AttackChoice activeAttack, bufferedAttack;
        Vector3 selectedDirection, bufferedDirection;
        readonly RollSequence rollSequence = new RollSequence();
        readonly AttackPreparation preparation = new AttackPreparation();
        readonly SpecialAttackSelector attackSelector = new SpecialAttackSelector();

        float invulnerableUntil;
        Vector3 knockback;
        WeaponVisual weapon;
        BloodSplatter blood;
        readonly HashSet<PracticeTarget> hit = new HashSet<PracticeTarget>();
        readonly Collider[] overlaps = new Collider[64];

        public void Push(Vector3 direction, float speed)
        {
            knockback = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * speed;
        }

        void Awake()
        {
            arena = GetComponentInParent<ArenaDemo>();
            controller = GetComponent<CharacterController>();
            Guard = GetComponent<CombatGuard>();
            Guard.State.ConfigureEndurance(endurance);
            Health = maxHealth;
            Hitbox = gameObject.AddComponent<CombatHitbox>();
            view = Camera.main;
            visual = GetComponentInChildren<SpriteRenderer>();
            slash = DemoArt.Line("Sword arc", transform, new Color(1, .85f, .5f), .1f);
            DrawSlash(0, Facing);
            slash.startColor = slash.endColor = new Color(1, .87f, .6f, .35f);
            weapon = WeaponVisual.Create(transform);
            blood = BloodSplatter.Create(transform.parent);
        }

        void Update()
        {
            if (!IsAlive) { Guard.Tick(false, Facing); return; }
            if (!Attacking) activeAttack = AttackChoice.None;
            float dt = Time.deltaTime;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if (keyboard != null && !Attacking && !Rolling && !Guard.Disrupted)
            {
                WeaponStyle nextStyle = keyboard.digit1Key.wasPressedThisFrame ? WeaponStyle.Normal
                    : keyboard.digit2Key.wasPressedThisFrame ? WeaponStyle.Thrusting : Style;
                if (nextStyle != Style)
                {
                    Style = nextStyle;
                    if (Style == WeaponStyle.Thrusting)
                    {
                        preparation.Begin(Time.time);
                    }
                    else preparation.Cancel();
                    attackSelector.Cancel();
                    buffered = false;
                }
            }
            if (Style == WeaponStyle.Thrusting && !Attacking)
            {
                float waiting = preparation.Tick(Time.time);
                bool paid = Guard.State.TrySpendForPreparation(preparationPressurePerSecond * waiting, Time.time);
                if (!paid || !preparation.Active) ResetPreparation();
            }
            Vector2 input = Vector2.zero;
            if (keyboard != null)
                input = new Vector2((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > .04f) input = pad.leftStick.ReadValue();
            input = Vector2.ClampMagnitude(input, 1);
            Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            Vector3 move = right * input.x + forward * input.y;
            if (!Attacking && !Rolling && !attackSelector.Holding
                && !(buffered && bufferedAttack == AttackChoice.Lunge))
            {
                if (move.sqrMagnitude > .01f) Facing = move.normalized;
                if (Mouse.current != null)
                {
                    Ray ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (new Plane(Vector3.up, transform.position).Raycast(ray, out float distance))
                    {
                        Vector3 aim = ray.GetPoint(distance) - transform.position;
                        if (aim.sqrMagnitude > .1f) Facing = aim.normalized;
                    }
                }
                if (pad != null && pad.rightStick.ReadValue().sqrMagnitude > .15f)
                {
                    Vector2 aim = pad.rightStick.ReadValue();
                    Facing = (right * aim.x + forward * aim.y).normalized;
                }
            }
            bool wantsBlock = (Mouse.current != null && Mouse.current.rightButton.isPressed)
                || (keyboard != null && keyboard.kKey.isPressed) || (pad != null && pad.leftShoulder.isPressed);
            if (wantsBlock && (Style == WeaponStyle.Thrusting || UnblockableAttack)) CancelAttack();
            Guard.Tick(wantsBlock && WeaponStyleOptions.Sword(Style).CanGuard && !Attacking && !Rolling, Facing);
            if (Guard.Disrupted)
            {
                CancelAttack();
                rollRemaining = 0;
                move = Vector3.zero;
                velocity = Vector3.zero;
            }
            bool attackPressed = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                || (keyboard != null && keyboard.jKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame);
            bool attackHeld = (Mouse.current != null && Mouse.current.leftButton.isPressed)
                || (keyboard != null && keyboard.jKey.isPressed) || (pad != null && pad.buttonWest.isPressed);
            if (attackPressed && !Guard.Blocking && !Guard.Disrupted && !Rolling && !UnblockableAttack)
            {
                buffered = false;
                AttackChoice pressedChoice = attackSelector.Begin(WeaponStyleOptions.Sword(Style));
                selectedDirection = Facing;
                if (pressedChoice != AttackChoice.None)
                {
                    buffered = true;
                    bufferedUntil = Time.time + preparation.RemainingWindup(Time.time, lungeWindup) + .18f;
                    bufferedAttack = pressedChoice; bufferedDirection = selectedDirection;
                }
            }
            if (Guard.Blocking || Guard.Disrupted || Rolling) attackSelector.Cancel();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ResetPreparation();
            if (attackSelector.Holding && !attackHeld)
            {
                AttackChoice choice = attackSelector.Release();
                if (choice != AttackChoice.None)
                {
                    buffered = true; bufferedUntil = Time.time + .18f;
                    bufferedAttack = choice; bufferedDirection = selectedDirection;
                }
            }
            bool rollPressed = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (rollPressed) TryRoll(move);
            if (buffered && Time.time > bufferedUntil) buffered = false;
            if (Guard.Blocking || Guard.Disrupted) buffered = false;
            if (buffered && !Attacking && !Rolling
                && (bufferedAttack != AttackChoice.Lunge || preparation.Ready(Time.time, lungeWindup)))
            {
                buffered = false;
                // Pay once when releasing the prepared strike, including misses or interrupted strikes.
                if (bufferedAttack != AttackChoice.Lunge
                    || Guard.State.TrySpendForStrongAttack(strongAttackPressure, Time.time))
                {
                    activeAttack = bufferedAttack;
                    attackStartedAt = Time.time;
                    preparation.Cancel();
                    attackRemaining = CurrentWindup + CurrentActive + CurrentRecovery;
                    attackDirection = bufferedDirection;
                    Facing = attackDirection;
                    Guard.AnnounceAttack(CurrentWindup, ThreatReach);
                    Guard.State.RecordCombat(Time.time);
                    hit.Clear();
                    swingSoundPlayed = false;
                }
                else ResetPreparation();
            }
            if (Rolling)
            {
                controller.Move((rollDirection * rollSpeed + Vector3.down * 2) * dt);
                rollRemaining = Mathf.Max(0, rollRemaining - dt);
                velocity = Vector3.zero;
            }
            else
            {
                bool readyToLunge = PreparingPowerfulAttack && preparation.Ready(Time.time, lungeWindup);
                float movementMultiplier = UnblockableAttack ? 0 : Attacking ? .25f : Guard.Blocking ? .45f
                    : attackSelector.Holding ? .4f : readyToLunge ? preparedRunSpeedMultiplier : 1;
                velocity = Vector3.MoveTowards(velocity, move * speed * movementMultiplier, acceleration * dt);
                controller.Move((velocity + Vector3.down * 2) * dt);
            }
            controller.Move(knockback * dt);
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 25 * dt);
            if (Attacking)
            {
                float elapsed = CurrentWindup + CurrentActive + CurrentRecovery - attackRemaining;
                // Sweep throughout the active window, including frames that cross it.
                if (elapsed < CurrentWindup + CurrentActive && elapsed + dt >= CurrentWindup)
                {
                    if (UnblockableAttack)
                    {
                        float activeDelta = Mathf.Max(0, Mathf.Min(elapsed + dt, CurrentWindup + CurrentActive) - Mathf.Max(elapsed, CurrentWindup));
                        float travel = lungeDistance * activeDelta / Mathf.Max(.01f, CurrentActive);
                        int steps = Mathf.Max(1, Mathf.CeilToInt(travel / .15f));
                        for (int step = 0; step < steps && Attacking; step++)
                        {
                            controller.Move(attackDirection * (travel / steps));
                            Strike();
                        }
                    }
                    else Strike();
                }
                bool finishedPowerfulStrike = UnblockableAttack && elapsed + dt >= CurrentWindup + CurrentActive;
                attackRemaining = Mathf.Max(0, attackRemaining - dt);
                if (finishedPowerfulStrike)
                {
                    Guard.State.CompleteStrongAttack(Time.time);
                    if (Guard.Disrupted) CancelAttack();
                }
                if (!Attacking && activeAttack == AttackChoice.Lunge) ResetPreparation();
            }
            if (visual != null)
            {
                visual.color = Time.time < invulnerableUntil && Mathf.Sin(Time.time * 45) > 0
                    ? new Color(1, .35f, .35f, .5f) : Color.white;
                visual.flipX = Vector3.Dot(Facing, right) < 0;
                float bob = input.sqrMagnitude > .01f && !Rolling ? Mathf.Abs(Mathf.Sin(Time.time * 15)) * .08f : 0;
                visual.transform.localPosition = Vector3.up * (.85f + bob);
                visual.transform.localRotation = view.transform.rotation * Quaternion.Euler(0, 0, Rolling ? -360 * (1 - rollRemaining / rollDuration) : -input.x * 5);
                visual.transform.localScale = Rolling ? new Vector3(1.1f, .7f, 1) : Vector3.one;
            }
        }

        void LateUpdate()
        {
            float elapsed = CurrentWindup + CurrentActive + CurrentRecovery - attackRemaining;
            float swing = Attacking && elapsed >= CurrentWindup ? Mathf.Clamp01((elapsed - CurrentWindup) / CurrentActive) : -1;
            bool preparingLunge = PreparingPowerfulAttack;
            float prepare = preparingLunge ? preparation.WindupProgress(Time.time, lungeWindup)
                : Attacking && elapsed < CurrentWindup ? elapsed / CurrentWindup : 0;
            float returnToReady = Attacking ? Mathf.Clamp01((elapsed - CurrentWindup - CurrentActive) / CurrentRecovery) : 0;
            Vector3 poseDirection = Attacking ? attackDirection : Facing;
            weapon.Pose(poseDirection, IsAlive && !Rolling, Guard.Blocking, Guard.Disrupted, swing, prepare,
                preparingLunge ? AttackReach * .85f : CurrentReach, halfAngle, returnToReady, CircularStrike && !preparingLunge, ThrustAttack || preparingLunge, UnblockableAttack || preparingLunge,
                Attacking ? ThrustAttack : Style == WeaponStyle.Thrusting);
            weapon.ChargeGlint(prepare,
                preparingLunge && IsAlive && !Rolling && !Guard.Disrupted && !Guard.Blocking);
            if (visual != null && IsAlive && !Rolling)
            {
                float side = Vector3.Dot(poseDirection, view.transform.right);
                float thrust = swing >= 0 ? Mathf.Sin(Mathf.Pow(swing, .55f) * Mathf.PI) : 0;
                float tension = Mathf.SmoothStep(0, 1, Mathf.Clamp01(prepare / .78f));
                visual.flipX = side < 0;
                visual.transform.rotation = view.transform.rotation * Quaternion.Euler(0, side * 18,
                    side * (tension * 13 - thrust * 15));
                visual.transform.localPosition += poseDirection * (thrust * .16f - tension * .13f);
            }
            slash.enabled = IsAlive && (arena == null || arena.ShowAttackRangeIndicators);
            if (slash.enabled)
            {
                DrawSlash(0, Attacking ? attackDirection : Facing);
                slash.startColor = slash.endColor = new Color(1, .87f, .6f, .35f);
                if (swing >= 0 && elapsed < CurrentWindup + CurrentActive) DrawSlash(swing, attackDirection);
            }
        }

        public bool TryRoll(Vector3 direction)
        {
            if (!IsAlive || Rolling || Attacking || Time.time < nextRoll
                || !rollSequence.TrySpend(Guard.State, Time.time, rollDuration, rollPressure,
                    rollChainPressureIncrement, rollChainResetDelay)) return false;
            rollRemaining = rollDuration;
            nextRoll = Time.time + rollCooldown;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            rollDirection = direction.sqrMagnitude > .01f ? direction.normalized : Facing;
            ResetPreparation();
            CombatAudio.Play(CombatSound.Roll, transform.position);
            return true;
        }

        void ResetPreparation()
        {
            Style = WeaponStyle.Normal;
            preparation.Cancel();
            buffered = false;
            attackSelector.Cancel();
        }

        public void InterruptAttack() => CancelAttack();

        void CancelAttack()
        {
            Guard.State.CompleteStrongAttack(Time.time);
            ResetPreparation();
            activeAttack = AttackChoice.None;
            attackRemaining = 0;
            slash.enabled = false;
        }

        public void ReceiveAttack(int amount, Vector3 direction, CombatGuard attacker)
        {
            if (!IsAlive) return;
            if (Guard.TryClash(attacker, direction)) return;
            if (Time.time < invulnerableUntil) return;
            if (Guard.Defend(attacker, direction) != DefenseResult.Hit)
            {
                if (Guard.Disrupted) CancelAttack();
                return;
            }
            if (TakeHit(amount, direction)) Guard.OnHit();
        }

        public bool TakeHit(int amount, Vector3 direction)
        {
            if (!IsAlive || Time.time < invulnerableUntil || amount <= 0) return false;
            Health = Mathf.Max(0, Health - amount);
            CombatAudio.Play(IsAlive ? CombatSound.Hit : CombatSound.Death, transform.position);
            blood.Burst(transform.position + Vector3.up * .8f, direction);
            invulnerableUntil = Time.time + hurtInvulnerability;
            knockback = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * 6;
            rollRemaining = 0;
            CancelAttack();
            if (!IsAlive)
            {
                velocity = knockback = Vector3.zero;
                visual.color = new Color(.35f, .35f, .35f);
                visual.transform.localScale = new Vector3(1, .4f, 1);
            }
            return true;
        }

        public void Respawn()
        {
            controller.enabled = false;
            transform.position = Vector3.zero;
            controller.enabled = true;
            Health = maxHealth;
            ResetPreparation();
            Guard.ResetCombat();
            velocity = knockback = Vector3.zero;
            attackRemaining = rollRemaining = 0;
            activeAttack = AttackChoice.None;
            attackSelector.Cancel();
            nextRoll = 0;
            rollSequence.Reset();
            buffered = false;
            hit.Clear();
            invulnerableUntil = Time.time + 1;
            slash.enabled = false;
            visual.color = Color.white;
            visual.transform.localScale = Vector3.one;
            CombatAudio.Play(CombatSound.Revive, transform.position);
        }

        void Strike()
        {
            if (!swingSoundPlayed)
            {
                CombatAudio.Play(CombatSound.Swing, transform.position);
                swingSoundPlayed = true;
            }
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * .6f, CurrentReach, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlaps[i].GetComponentInParent<PracticeTarget>();
                if (target == null || !target.IsAlive || hit.Contains(target)) continue;
                var enemy = target.GetComponent<EnemyBrain>();
                Vector3 delta = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (!target.Hitbox.IntersectsAttack(transform.position + Vector3.up * .8f,
                    attackDirection, CurrentReach, halfAngle, CircularStrike, ThrustAttack,
                    ThrustAttack ? lungeWidth : .12f)) continue;
                // Walls block attacks. Target colliders are triggers and ignored by this ray.
                if (Physics.Raycast(transform.position + Vector3.up * .65f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)) continue;
                hit.Add(target);
                Vector3 hitDirection = CircularStrike && delta.sqrMagnitude > .001f ? delta.normalized : attackDirection;
                if (enemy != null) enemy.ReceiveAttack(damage, hitDirection, Guard, UnblockableAttack);
                else target.TakeHit(damage, hitDirection);
                if (Guard.Disrupted) { CancelAttack(); break; }
            }
        }

        void DrawSlash(float progress, Vector3 direction)
        {
            slash.enabled = IsAlive && (arena == null || arena.ShowAttackRangeIndicators);
            if (!slash.enabled) return;
            slash.loop = CircularStrike;
            int segments = CircularStrike ? 48 : 18;
            slash.positionCount = segments;
            float heading = Mathf.Atan2(direction.z, direction.x);
            for (int i = 0; i < segments; i++)
            {
                float angle = heading + (CircularStrike ? i * Mathf.PI * 2 / segments
                    : Mathf.Lerp(ThrustAttack ? -8 : -halfAngle, ThrustAttack ? 8 : halfAngle, i / (float)(segments - 1)) * Mathf.Deg2Rad);
                slash.SetPosition(i, transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (CurrentReach * (.8f + progress * .2f)) + Vector3.up * .4f);
            }
            slash.startColor = slash.endColor = new Color(1, .87f, .6f, 1 - progress * .7f);
        }

        void OnGUI()
        {
            if (!IsAlive) return;
            GUI.Label(new Rect(22, 94, 430, 24), Style == WeaponStyle.Normal
                ? "[1] NORMAL: slash / guard   (no strong attack)"
                : $"[2] PREPARED: click attack ({strongAttackPressure:0} STA), waiting costs {preparationPressurePerSecond:0} STA/s");
        }

        void OnDisable()
        {
            if (Guard != null) Guard.State.CompleteStrongAttack(Time.time);
            ResetPreparation();
            if (weapon != null) weapon.ChargeGlint(0, false);
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                buffered = false; velocity = Vector3.zero;
                if (Guard != null) Guard.State.CompleteStrongAttack(Time.time);
                ResetPreparation();
                if (Guard != null) Guard.State.SetBlocking(false, Time.time);
            }
        }
    }
}
