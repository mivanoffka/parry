using System;

namespace Parry
{
    public static class EnemyFlankingRules
    {
        // Stable ranks keep allies from swapping sides on every movement update.
        public static float TurnToSlot(float currentHeading, float anchorHeading, int rank, int count)
        {
            if (count <= 1) return 0;
            float turn = (anchorHeading + 360f * rank / count - currentHeading) % 360f;
            if (turn > 180) turn -= 360;
            if (turn <= -180) turn += 360;
            return turn;
        }
    }

    // Periodic random rest decisions depend on current concentration; chosen rests have hysteresis.
    public sealed class EnemyTactics
    {
        public const float OpponentWeaknessThreshold = 70;
        public bool ExploitingWeakness { get; private set; }
        public bool FullOffense { get; private set; }
        public const float AdvantageEntry = 30;
        public const float AdvantageExit = 20;
        public const float ReengageThreshold = 35;
        public const float MinimumRetreatDuration = .8f;
        public const float AttackCommitmentDuration = .6f;
        public const float AggressiveThreshold = 55;
        public bool Retreating { get; private set; }
        public bool Aggressive { get; private set; } = true;
        public bool UrgentRetreat { get; private set; }
        public float AttackBlockedUntil { get; private set; }

        public void RecordBlock(float now, float duration) =>
            AttackBlockedUntil = Math.Max(AttackBlockedUntil, now + Math.Max(0, duration));

        public void RecordParry(float now, float duration) =>
            AttackBlockedUntil = now + Math.Max(0, duration);

        readonly Func<float> randomValue;
        float retreatUntil, reengageUntil, nextDecisionAt = float.NegativeInfinity;
        public EnemyTactics(Func<float> randomValue = null)
        {
            var random = new Random();
            this.randomValue = randomValue ?? (() => (float)random.NextDouble());
        }
        public static float RetreatChance(float pressure, float opponentPressure = 0)
        {
            float load = Math.Clamp(pressure / 100, 0, 1);
            // Smooth rise from 2% at full concentration to 98% at an empty reserve.
            float baseChance = .02f + .96f * load * load * (3 - 2 * load);
            float opponentLoad = Math.Clamp(opponentPressure / 100, 0, 1);
            float opportunity = opponentLoad * opponentLoad * (3 - 2 * opponentLoad);
            // A depleted opponent reduces the desire to rest, but never removes the choice entirely.
            return baseChance * (1 - .75f * opportunity);
        }
        public void Update(float pressure, float now, int endurance = 100,
            bool threatened = false, bool opening = false, float opponentPressure = 0, bool opponentDisrupted = false,
            float decisionInterval = 1)
        {
            float advantage = Math.Clamp(opponentPressure, 0, 100) - Math.Clamp(pressure, 0, 100);
            FullOffense = advantage >= (FullOffense ? AdvantageExit : AdvantageEntry);
            bool weakOpponent = opponentPressure >= OpponentWeaknessThreshold;
            ExploitingWeakness = opponentDisrupted || FullOffense;
            if (opponentDisrupted || FullOffense)
            {
                Retreating = UrgentRetreat = false;
                Aggressive = true;
                return;
            }
            if (Retreating)
            {
                if (pressure <= ReengageThreshold && now >= retreatUntil)
                {
                    Reengage(pressure, now);
                    nextDecisionAt = now + Math.Max(AttackCommitmentDuration, decisionInterval);
                }
            }
            else if (now >= nextDecisionAt && now >= reengageUntil)
            {
                // One sample per decision, never one per frame or per missed interval.
                nextDecisionAt = now + Math.Max(.1f, decisionInterval);
                if (randomValue() < RetreatChance(pressure, opponentPressure))
                {
                    Retreating = true;
                    retreatUntil = now + MinimumRetreatDuration;
                    float reserve = 100f / Math.Max(1, endurance);
                    UrgentRetreat = pressure >= 90 || (threatened && pressure + 28 * reserve >= 95);
                }
                else UrgentRetreat = false;
            }
            ExploitingWeakness = !Retreating && weakOpponent;
            Aggressive = !Retreating && (ExploitingWeakness || pressure <= AggressiveThreshold);
        }

        public void Reengage(float pressure, float now)
        {
            Retreating = UrgentRetreat = false;
            Aggressive = ExploitingWeakness || (!UrgentRetreat && pressure <= AggressiveThreshold);
            reengageUntil = now + AttackCommitmentDuration;
        }

        public bool CanCounterattack(float pressure, int endurance, bool opening) =>
            Retreating && opening && !UrgentRetreat && pressure <= AggressiveThreshold
            && pressure + 45 * (100f / Math.Max(1, endurance)) < 90;

        public void Reset()
        {
            AttackBlockedUntil = 0;
            Retreating = UrgentRetreat = ExploitingWeakness = FullOffense = false;
            Aggressive = true;
            retreatUntil = reengageUntil = 0;
            nextDecisionAt = float.NegativeInfinity;
        }
    }
}
