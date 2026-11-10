using UnityEngine;

namespace Parry
{
    public enum DefenseResult { Hit, Block, Parry }

    public sealed class CombatGuard : MonoBehaviour
    {
        public PostureState State { get; } = new PostureState();
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
        LineRenderer bar, background, flash;
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
        public void Tick(bool wantsBlock, Vector3 facing)
        {
            Facing = facing;
            State.SetBlocking(wantsBlock, Time.time);
            State.Tick(Time.deltaTime, Time.time);
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
            if (attacker == null || attacker == this) return false;
            // Resolving the pair interrupts both attacks immediately; a second callback cannot deal damage.
            if (lastClashFrame == Time.frameCount && lastClashWith == attacker) return true;
            if (Disrupted || attacker.Disrupted || Vector3.Angle(AttackFacing, -incomingDirection) > 70) return false;
            AttackClash clash = AttackClashRules.Resolve(ClashPhase, attacker.ClashPhase);
            if (clash == AttackClash.None) return false;
            lastClashWith = attacker; lastClashFrame = Time.frameCount;
            attacker.lastClashWith = this; attacker.lastClashFrame = Time.frameCount;
            bool parry = clash == AttackClash.Parry;
            bool ownPowerful = Powerful;
            State.AddPressure(powerful ? 80 : parry ? 45 : 28, Time.time);
            attacker.State.AddPressure(ownPowerful ? 80 : parry ? 45 : 28, Time.time);
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
            if (!Blocking || Disrupted || Vector3.Angle(Facing, -incomingDirection) > 70) return DefenseResult.Hit;
            if (allowParry && State.CanParry(Time.time))
            {
                State.Parry(Time.time, powerful);
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
            State.Block(Time.time, powerful);
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
        public void OnHit() { State.CancelRelaxation(); State.AddPressure(22, Time.time); }
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
            flash = DemoArt.Line("Weapon clash", transform, ParrySparks.ImpactColor(true), .055f);
            flash.enabled = false;
        }
        void Signal(string text, bool parry)
        {
            GetComponent<CharacterPresentation>()?.Impact(parry);
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
            background.enabled = markersVisible;
            bar.enabled = markersVisible && Pressure > .001f;
            if (alive)
            {
                Camera view = Camera.main;
                Vector3 right = view.transform.right * .55f;
                Vector3 center = transform.position + Vector3.up * 2.05f;
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
