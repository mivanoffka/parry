using System;

namespace Parry
{
    // Recovery has hysteresis, but immediate danger can override an attack commitment.
    public sealed class EnemyTactics
    {
        public const float OpponentWeaknessThreshold = 70;
        public bool ExploitingWeakness { get; private set; }
        public const float RetreatThreshold = 70;
        public const float ReengageThreshold = 35;
        public const float MinimumRetreatDuration = .8f;
        public const float AttackCommitmentDuration = .6f;
        public const float AggressiveThreshold = 55;
        public bool Retreating { get; private set; }
        public bool Aggressive { get; private set; } = true;
        public bool UrgentRetreat { get; private set; }

        float retreatUntil, reengageUntil, recentGain, lastPressure, lastUpdate;
        bool sampled;

        public void Update(float pressure, float now, float fatigue = 0, int endurance = 100,
            bool threatened = false, bool opening = false, float opponentPressure = 0, bool opponentDisrupted = false)
        {
            float elapsed = sampled ? Math.Max(0, now - lastUpdate) : 0;
            recentGain = Math.Max(0, recentGain - elapsed * 25);
            if (sampled) recentGain = Math.Min(30, recentGain + Math.Max(0, pressure - lastPressure));
            lastPressure = pressure;
            lastUpdate = now;
            sampled = true;
            // A vulnerable opponent takes priority over conserving our own reserve.
            ExploitingWeakness = opponentDisrupted || opponentPressure >= OpponentWeaknessThreshold;
            if (ExploitingWeakness)
            {
                Retreating = UrgentRetreat = false;
                Aggressive = true;
                return;
            }
            float fatigueRisk = Math.Min(12, Math.Max(0, fatigue) * 2);
            float reserve = 100f / Math.Max(1, endurance);
            float entry = RetreatThreshold - fatigueRisk - Math.Min(10, recentGain * .4f);
            float exit = ReengageThreshold - fatigueRisk * .5f;
            // Keep enough concentration for an ordinary block, or the penalty for a parried attack.
            UrgentRetreat = pressure >= 90 || (threatened && pressure + 28 * reserve >= 95);
            bool vulnerable = pressure >= entry || (pressure >= 60 && pressure + 45 * reserve >= 100);
            bool canExploitOpening = opening && !threatened && pressure < 75
                && pressure + 45 * reserve < 90 && fatigue < 4;

            if (Retreating)
            {
                if (!UrgentRetreat && pressure <= exit && now >= retreatUntil)
                    Reengage(pressure, now);
            }
            else if (UrgentRetreat || (vulnerable && !canExploitOpening && now >= reengageUntil))
            {
                Retreating = true;
                retreatUntil = now + MinimumRetreatDuration;
            }
            Aggressive = !Retreating && !UrgentRetreat && pressure <= AggressiveThreshold - fatigueRisk;
        }

        public void Reengage(float pressure, float now)
        {
            Retreating = false;
            Aggressive = ExploitingWeakness || (!UrgentRetreat && pressure <= AggressiveThreshold);
            reengageUntil = now + AttackCommitmentDuration;
        }

        public bool CanCounterattack(float pressure, int endurance, bool opening) =>
            Retreating && opening && !UrgentRetreat && pressure <= AggressiveThreshold
            && pressure + 45 * (100f / Math.Max(1, endurance)) < 90;

        public void Reset()
        {
            Retreating = UrgentRetreat = sampled = ExploitingWeakness = false;
            Aggressive = true;
            retreatUntil = reengageUntil = recentGain = lastPressure = lastUpdate = 0;
        }
    }
}
