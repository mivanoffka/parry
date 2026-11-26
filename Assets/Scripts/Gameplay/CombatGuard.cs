using UnityEngine;

namespace Parry
{
    public enum DefenseResult { Hit, Block, Parry }

    public sealed class CombatGuard : MonoBehaviour
    {
        public PostureState State { get; } = new PostureState();
        public WeaponKind Weapon => GetComponent<PlayerMotor>() is PlayerMotor player ? player.EquippedWeapon : WeaponKind.Sword;
        public float StrikeStrength => GetComponent<PlayerMotor>() is PlayerMotor player ? player.StrikeStrength
            : GetComponent<EnemyBrain>() is EnemyBrain enemy ? enemy.StrikeStrength : 1;
        bool CanDefendAgainst(CombatGuard attacker) => Weapon != WeaponKind.Axe && (Weapon != WeaponKind.Fists || attacker != null && attacker.Weapon == WeaponKind.Fists);
        public bool Broken => State.Broken(Time.time);
        public bool Disrupted => State.Disrupted(Time.time);
        public bool Blocking => State.Blocking;
        public float Pressure => State.Pressure;
        public int EndurancePoints => State.EndurancePoints;
        public float RemainingEndurance => State.RemainingEndurance;
        public string Feedback => Time.time < feedbackUntil ? feedback : Broken ? "GUARD BROKEN" : "";
        public Vector3 Facing { get; private set; } = Vector3.back;
        float feedbackUntil;
        string feedback;
        LineRenderer bar, background, flash, warmupBar, warmupBackground, chargeBar, chargeBackground;
        ParrySparks sparks;
        bool wasBroken;
        CombatGuard lastClashWith;
        int lastClashFrame = -1;
        AttackPhase ClashPhase
        {
            get
            {
                var player = GetComponent<PlayerMotor>();
                if (player != null) return player.ClashPhase;
                var enemy = GetComponent<EnemyBrain>();
                return enemy != null ? enemy.ClashPhase : AttackPhase.None;
            }
        }
        Vector3 AttackFacing
        {
            get
            {
                var player = GetComponent<PlayerMotor>();
                if (player != null) return player.AttackFacing;
                var enemy = GetComponent<EnemyBrain>();
                return enemy != null ? enemy.AttackFacing : Facing;
            }
        }
        bool Powerful => GetComponent<PlayerMotor>() is PlayerMotor player ? player.UnblockableThreat
            : GetComponent<EnemyBrain>() is EnemyBrain enemy && enemy.PowerfulThreat;
        readonly Collider[] nearbyDefenders = new Collider[64];
        public void SetFacing(Vector3 facing) => Facing = facing;
        public void Tick(bool wantsBlock, Vector3 facing, bool rolling = false)
        {
            Facing = facing;
            State.SetBlocking(wantsBlock && Weapon != WeaponKind.Axe, Time.time);
            if (!rolling) State.Tick(Time.deltaTime, Time.time);
        }
        public void AnnounceAttack(float windup, float reach)
        {
            Vector3 origin = transform.position + Vector3.up * .65f;
            int count = Physics.OverlapSphereNonAlloc(origin, reach + .5f, nearbyDefenders, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var defender = nearbyDefenders[i].GetComponentInParent<CombatGuard>();
                if (defender == null || defender == this) continue;
                Vector3 delta = defender.transform.position + Vector3.up * .65f - origin;
                if (Physics.Raycast(origin, delta.normalized, out RaycastHit obstacle, delta.magnitude,
                    ~0, QueryTriggerInteraction.Ignore) && obstacle.collider.GetComponentInParent<CombatGuard>() != defender) continue;
                defender.State.AnticipateAttack(Time.time, windup);
            }
        }
        public bool TryClash(CombatGuard attacker, Vector3 incomingDirection, bool powerful = false)
        {
            if (attacker == null || attacker == this || !CanDefendAgainst(attacker) || !attacker.CanDefendAgainst(this)) return false;
            // Resolving the pair interrupts both attacks immediately; a second callback cannot deal damage.
            if (lastClashFrame == Time.frameCount && lastClashWith == attacker) return true;
            if (Disrupted || attacker.Disrupted || Vector3.Angle(AttackFacing, -incomingDirection) > 70) return false;
            AttackClash clash = AttackClashRules.Resolve(ClashPhase, attacker.ClashPhase);
            if (clash == AttackClash.None) return false;
            lastClashWith = attacker; lastClashFrame = Time.frameCount;
            attacker.lastClashWith = this; attacker.lastClashFrame = Time.frameCount;
            bool parry = clash == AttackClash.Parry;
            bool ownPowerful = Powerful;
            if (parry) { State.RecordSuccessfulParry(); attacker.State.RecordSuccessfulParry(); }
            State.AddPressure((powerful ? State.PowerfulParryCost : parry ? 45 : 28) * attacker.StrikeStrength, Time.time);
            attacker.State.AddPressure((ownPowerful ? attacker.State.PowerfulParryCost : parry ? 45 : 28) * StrikeStrength, Time.time);
            State.Deflect(Time.time); attacker.State.Deflect(Time.time);
            State.Recoil(Time.time); attacker.State.Recoil(Time.time);
            float push = powerful || ownPowerful ? 12 : parry ? 7 : 5;
            Push(incomingDirection, push); attacker.Push(-incomingDirection, push);
            InterruptAttack(parry); attacker.InterruptAttack(parry);
            Signal(parry ? "PARRY CLASH!" : "BLOCK CLASH", parry);
            attacker.Signal(parry ? "PARRY CLASH!" : "BLOCK CLASH", parry);
            CombatAudio.Play(parry ? CombatSound.Parry : CombatSound.Block, transform.position);
            sparks.Burst((transform.position + attacker.transform.position) * .5f + Vector3.up * .8f,
                incomingDirection, parry);
            return true;
        }
        void InterruptAttack(bool parried)
        {
            var player = GetComponent<PlayerMotor>();
            if (player != null)
            {
                if (parried) player.OnAttackParried();
                else player.InterruptAttack();
            }
            var enemy = GetComponent<EnemyBrain>();
            if (enemy != null) enemy.InterruptAttack();
        }
        public DefenseResult Defend(CombatGuard attacker, Vector3 incomingDirection, bool allowParry = true, bool powerful = false)
        {
            if (!CanDefendAgainst(attacker) || !Blocking || Disrupted || Vector3.Angle(Facing, -incomingDirection) > 70) return DefenseResult.Hit;
            float strength = attacker != null ? attacker.StrikeStrength : 1;
            if (allowParry && State.CanParry(Time.time, attacker != null && attacker.Weapon == WeaponKind.Axe ? .5f : 1f))
            {
                State.Parry(Time.time, powerful, attacker != null && attacker.Weapon == WeaponKind.Axe ? 5 : 1, strength);
                if (powerful)
                {
                    PushApart(attacker, incomingDirection);
                }
                if (attacker != null)
                {
                    attacker.State.AddPressure(45, Time.time);
                    attacker.State.Recoil(Time.time);
                    var attackingPlayer = attacker.GetComponent<PlayerMotor>();
                    if (attackingPlayer != null) attackingPlayer.OnAttackParried();
                    attacker.Signal(attacker.Broken ? "GUARD BROKEN" : "DEFLECTED", false);
                }
                Signal("PARRY!", true);
                CombatAudio.Play(CombatSound.Parry, transform.position);
                sparks.Burst(transform.position + Facing * .65f + Vector3.up * .85f, incomingDirection);
                return DefenseResult.Parry;
            }
            State.Block(Time.time, powerful, strength);
            if (attacker != null && attacker.Weapon == WeaponKind.Axe) State.AddPressure(112 * strength, Time.time);
            if (powerful)
            {
                PushApart(attacker, incomingDirection);
                // Stop forward travel so it cannot swallow the attacker's backward impulse.
                if (attacker != null) attacker.State.Recoil(Time.time);
            }
            Signal(Broken ? "GUARD BROKEN" : "BLOCK", false);
            CombatAudio.Play(CombatSound.Block, transform.position);
            sparks.Burst(transform.position + Facing * .65f + Vector3.up * .85f, incomingDirection, false);
            return DefenseResult.Block;
        }
        void PushApart(CombatGuard attacker, Vector3 direction)
        {
            Push(direction, 12);
            if (attacker != null) attacker.Push(-direction, 12);
        }
        void Push(Vector3 direction, float speed)
        {
            var target = GetComponent<PracticeTarget>();
            if (target != null) target.Push(direction, speed);
            var player = GetComponent<PlayerMotor>();
            if (player != null) player.Push(direction, speed);
        }
        public void NotifyWarmupRequired()
        {
            feedback = "BUILD WARMUP: ATTACK / PARRY";
            feedbackUntil = Time.time + 1.2f;
        }
        public void OnHit(CombatGuard attacker = null) { State.CancelRelaxation(); State.AddPressure((attacker != null && attacker.Weapon == WeaponKind.Axe ? 110 : 22) * (attacker != null ? attacker.StrikeStrength : 1), Time.time); }
        public void ResetCombat()
        {
            State.Reset(); feedbackUntil = 0; wasBroken = false;
            lastClashWith = null; lastClashFrame = -1;
        }
        void Awake()
        {
            sparks = gameObject.AddComponent<ParrySparks>();
            background = DemoArt.Line("Concentration background", transform, new Color(.2f, .19f, .22f), .11f);
            bar = DemoArt.Line("Concentration pressure", transform, new Color(1, .65f, .15f), .07f);
            background.sortingOrder = 100;
            bar.sortingOrder = 101;
            background.positionCount = bar.positionCount = 2;
            warmupBackground = DemoArt.Line("Combat warmup background", transform, new Color(.12f, .2f, .23f), .085f);
            warmupBar = DemoArt.Line("Combat warmup", transform, new Color(.25f, .95f, .9f), .055f);
            warmupBackground.sortingOrder = 100; warmupBar.sortingOrder = 101;
            warmupBackground.positionCount = warmupBar.positionCount = 2;
            chargeBackground = DemoArt.Line("Strike charge background", transform, new Color(.08f, .1f, .12f), .12f);
            chargeBar = DemoArt.Line("Strike charge", transform, Color.green, .08f);
            chargeBackground.positionCount = chargeBar.positionCount = 2;
            chargeBackground.sortingOrder = 100; chargeBar.sortingOrder = 101;
            chargeBackground.enabled = chargeBar.enabled = false;
            flash = DemoArt.Line("Weapon clash", transform, ParrySparks.ImpactColor(true), .055f);
            flash.enabled = false;
        }
        void Signal(string text, bool parry)
        {
            feedback = text; feedbackUntil = Time.time + .65f;
            flash.enabled = true; flash.positionCount = 12;
            Vector3 center = transform.position + Facing * .6f + Vector3.up * .85f;
            Vector3 right = Camera.main.transform.right, up = Camera.main.transform.up;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                Vector3 ray = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                flash.SetPosition(i * 2, center + ray * .06f);
                flash.SetPosition(i * 2 + 1, center + ray * (parry ? .65f : .3f));
            }
            flash.startColor = flash.endColor = ParrySparks.ImpactColor(parry);
        }
        void LateUpdate()
        {
            if (Broken && !wasBroken) CombatAudio.Play(CombatSound.Break, transform.position);
            wasBroken = Broken;
            var player = GetComponent<PlayerMotor>();
            var target = GetComponent<PracticeTarget>();
            bool alive = player != null ? player.IsAlive : target != null && target.IsAlive;
            bool markersVisible = alive && SmokeCloud.Exposure(transform.position) <= .01f;
            var enemy = GetComponent<EnemyBrain>();
            if (player == null && SmokeCloud.PlayerObscured) markersVisible = false;
            AttackCharge charge = player != null ? player.Charge : enemy != null ? enemy.Charge : null;
            bool charging = player != null ? player.ChargeVisible : enemy != null && enemy.ChargeVisible;
            chargeBackground.enabled = markersVisible && charging;
            chargeBar.enabled = chargeBackground.enabled;
            if (chargeBar.enabled)
            {
                Vector3 right = Camera.main.transform.right * .55f;
                Vector3 center = transform.position + Vector3.up * 2.48f;
                chargeBackground.SetPosition(0, center - right);
                chargeBackground.SetPosition(1, center + right);
                Vector3 start = center - right - Camera.main.transform.forward * .02f;
                float fill = charge.Meter(Time.time);
                chargeBar.SetPosition(0, start);
                chargeBar.SetPosition(1, start + right * 2 * fill);
                chargeBar.startColor = chargeBar.endColor = Color.Lerp(Color.green, Color.red, fill);
            }
            background.enabled = markersVisible;
            warmupBackground.enabled = markersVisible;
            warmupBar.enabled = markersVisible && State.Warmup > 0;
            bar.enabled = markersVisible && Pressure > .001f;
            if (alive)
            {
                Camera view = Camera.main;
                Vector3 right = view.transform.right * .55f;
                Vector3 center = transform.position + Vector3.up * 2.05f;
                Vector3 warmupCenter = center + Vector3.up * .18f;
                warmupBackground.SetPosition(0, warmupCenter - right);
                warmupBackground.SetPosition(1, warmupCenter + right);
                Vector3 warmupStart = warmupCenter - right - view.transform.forward * .02f;
                warmupBar.SetPosition(0, warmupStart);
                warmupBar.SetPosition(1, warmupStart + right * 2 * State.Warmup / PostureState.MaximumWarmup);
                warmupBar.startColor = warmupBar.endColor = State.StrongAttackReady
                    ? new Color(.25f, .95f, .9f) : new Color(.3f, .5f, .6f);
                background.SetPosition(0, center - right); background.SetPosition(1, center + right);
                Vector3 fillStart = center - right - view.transform.forward * .02f;
                bar.SetPosition(0, fillStart);
                bar.SetPosition(1, fillStart + right * 2 * Mathf.Clamp01(Pressure / PostureState.Maximum));
                bar.startColor = bar.endColor = Broken ? Color.red : Blocking ? new Color(1, .85f, .3f) : new Color(1, .55f, .2f);
            }
            if (!alive || Time.time > feedbackUntil - .48f) flash.enabled = false;
        }
    }
}
