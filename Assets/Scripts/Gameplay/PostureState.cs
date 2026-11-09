using System;

namespace Parry
{
    public enum AttackPhase { None, Windup, Active, Recovery }
    public enum AttackClash { None, Block, Parry }
    public static class AttackClashRules
    {
        public static AttackPhase Phase(float elapsed, float windup, float active, float recovery)
        {
            if (elapsed < 0) return AttackPhase.None;
            if (elapsed < windup) return AttackPhase.Windup;
            if (elapsed < windup + active) return AttackPhase.Active;
            return elapsed < windup + active + recovery ? AttackPhase.Recovery : AttackPhase.None;
        }
        public static AttackClash Resolve(AttackPhase first, AttackPhase second)
        {
            if (first == AttackPhase.Active && second == AttackPhase.Active) return AttackClash.Parry;
            return (first == AttackPhase.Active && second == AttackPhase.Windup)
                || (second == AttackPhase.Active && first == AttackPhase.Windup) ? AttackClash.Block : AttackClash.None;
        }
    }

    // Only accepted rolls advance the chain; other combat actions do not erase its cost.
    public sealed class RollSequence
    {
        int length;
        float lastRollEnd = float.NegativeInfinity;
        bool Continuing(float now, float resetDelay) => length > 0 && now < lastRollEnd + Math.Max(0, resetDelay);
        public float Cost(float now, float baseCost, float increment, float resetDelay) =>
            Math.Max(0, baseCost) + (Continuing(now, resetDelay) ? length * Math.Max(0, increment) : 0);
        public bool TrySpend(PostureState state, float now, float duration, float baseCost, float increment, float resetDelay)
        {
            float cost = Cost(now, baseCost, increment, resetDelay);
            if (!state.TrySpendForRoll(cost, now)) return false;
            length = Continuing(now, resetDelay) ? length + 1 : 1;
            lastRollEnd = now + Math.Max(0, duration);
            return true;
        }
        public void Reset() { length = 0; lastRollEnd = float.NegativeInfinity; }
    }

    // Engine-independent combat rules, shared by both fighters.
    public sealed class PostureState
    {
        public const float Maximum = 100;
        public float Pressure { get; private set; }
        public int EndurancePoints { get; private set; } = 100;
        public float RemainingEndurance => (Maximum - Pressure) * EndurancePoints / Maximum;
        public void ConfigureEndurance(int points)
        {
            EndurancePoints = Math.Max(1, points);
            Reset();
        }
        float NormalizeCost(float points) => points * Maximum / EndurancePoints;
        public float RecoveryMultiplier => Pressure > 67 ? 4 : Pressure > 33 ? 2 : 1;
        public bool Blocking { get; private set; }
        public bool Broken(float now) => now < brokenUntil;
        public bool Disrupted(float now) => Broken(now) || now < recoilUntil;
        public bool CanParry(float now) => Blocking && now < parryUntil && !Disrupted(now);
        public const float EarlyGuardGrace = .2f;
        bool parryArmed, anticipatedAttack;
        float guardRaisedAt;
        bool strongAttackBreakPending;
        float brokenUntil, recoilUntil, parryUntil, nextParry, recoverAfter;
        public void RecordCombat(float now) => recoverAfter = Math.Max(recoverAfter, now + 2);

        public void SetBlocking(bool blocking, float now)
        {
            blocking &= !Disrupted(now);
            if (blocking && !Blocking)
            {
                guardRaisedAt = now;
                anticipatedAttack = false;
                parryArmed = now >= nextParry;
                parryUntil = parryArmed ? now + .28f : now;
                nextParry = now + .3f;
            }
            Blocking = blocking;
        }
        public void AnticipateAttack(float now, float windup)
        {
            if (!Blocking || Disrupted(now) || !parryArmed || anticipatedAttack
                || now < guardRaisedAt || now - guardRaisedAt > EarlyGuardGrace) return;
            anticipatedAttack = true;
            parryUntil = Math.Max(parryUntil, now + Math.Max(0, windup) + .08f);
        }
        public void AddPressure(float amount, float now)
        {
            if (amount <= 0) return;
            RecordCombat(now);
            if (Broken(now)) return;
            if (Pressure >= Maximum && !strongAttackBreakPending) Pressure = 35;
            Pressure = Math.Min(Maximum, Pressure + NormalizeCost(amount));
            if (Pressure >= Maximum)
            {
                brokenUntil = now + 1.25f;
                Blocking = false;
            }
        }
        public void Deflect(float now)
        {
            RecordCombat(now);
            // A fresh press can deflect one incoming strike, never the entire window.
            parryUntil = now;
            parryArmed = false;
        }
        public void Recoil(float now) { RecordCombat(now); recoilUntil = Math.Max(recoilUntil, now + .35f); Blocking = false; }
        public bool TryEmergencyGuard(float pressureCost, float now)
        {
            if (Disrupted(now)) return false;
            AddPressure(pressureCost, now);
            SetBlocking(true, now);
            return Blocking;
        }
        public bool TrySpendForRoll(float pressureCost, float now) => TrySpendConcentration(pressureCost, now);
        public bool TrySpendForPreparation(float pressureCost, float now) => TrySpendConcentration(pressureCost, now);
        public bool TrySpendForStrongAttack(float pressureCost, float now)
        {
            if (pressureCost < 0 || Disrupted(now) || strongAttackBreakPending) return false;
            RecordCombat(now);
            Blocking = false;
            strongAttackBreakPending = pressureCost >= RemainingEndurance;
            Pressure = Math.Min(Maximum, Pressure + NormalizeCost(pressureCost));
            return true;
        }
        // Exhaustion must not cancel the strike before its active window can deal damage.
        public void CompleteStrongAttack(float now)
        {
            if (!strongAttackBreakPending) return;
            strongAttackBreakPending = false;
            RecordCombat(now);
            Pressure = Maximum;
            Blocking = false;
            brokenUntil = Math.Max(brokenUntil, now + 1.25f);
        }
        bool TrySpendConcentration(float pressureCost, float now)
        {
            if (!CanSpendForRoll(pressureCost, now)) return false;
            AddPressure(pressureCost, now);
            RecordCombat(now);
            SetBlocking(false, now);
            return true;
        }
        public bool CanSpendForRoll(float pressureCost, float now) => pressureCost >= 0 && !Disrupted(now)
            && Pressure + NormalizeCost(pressureCost) < Maximum;
        public void Tick(float deltaTime, float now)
        {
            float elapsed = Math.Max(0, deltaTime);
            bool resting = now > recoverAfter && !Disrupted(now) && !strongAttackBreakPending;
            if (Broken(now)) return;
            if (Pressure >= Maximum && !strongAttackBreakPending) { Pressure = 35; recoverAfter = Math.Max(recoverAfter, now + .5f); resting = false; }
            if (resting)
                Recover(NormalizeCost((Blocking ? 28 : 18) * elapsed));
        }
        void Recover(float budget)
        {
            // Split at zone boundaries so long frames cannot recover a lower zone at the higher rate.
            while (budget > 0 && Pressure > 0)
            {
                float boundary = Pressure > 67 ? 67 : Pressure > 33 ? 33 : 0;
                float multiplier = RecoveryMultiplier;
                float required = (Pressure - boundary) / multiplier;
                if (budget < required) { Pressure -= budget * multiplier; return; }
                Pressure = boundary;
                budget -= required;
            }
        }
        public void Reset()
        {
            Pressure = brokenUntil = recoilUntil = parryUntil = nextParry = recoverAfter = 0;
            Blocking = false;
            strongAttackBreakPending = parryArmed = anticipatedAttack = false;
            guardRaisedAt = 0;
        }
    }
}
