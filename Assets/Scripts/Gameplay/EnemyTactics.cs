namespace Parry
{
    // Separate enter/exit thresholds prevent the enemy from oscillating between chase and retreat.
    public sealed class EnemyTactics
    {
        public const float RetreatThreshold = 75;
        public const float ReengageThreshold = 45;
        public const float MinimumRetreatDuration = .8f;
        public const float MaximumRetreatDuration = 2.5f;
        public const float AttackCommitmentDuration = 3;
        public const float AggressiveThreshold = 65;
        public bool Retreating { get; private set; }
        public bool Aggressive { get; private set; } = true;

        float retreatUntil, retreatDeadline, reengageUntil;
        public void Update(float pressure, float now)
        {
            if (Retreating)
            {
                if ((pressure <= ReengageThreshold && now >= retreatUntil) || now >= retreatDeadline)
                    Reengage(pressure, now);
            }
            else if (pressure >= RetreatThreshold && now >= reengageUntil)
            {
                Retreating = true;
                retreatUntil = now + MinimumRetreatDuration;
                retreatDeadline = now + MaximumRetreatDuration;
            }
            Aggressive = !Retreating && pressure <= AggressiveThreshold;
        }
        public void Reengage(float pressure, float now)
        {
            Retreating = false;
            Aggressive = pressure <= AggressiveThreshold;
            reengageUntil = now + AttackCommitmentDuration;
        }
        public void Reset() { Retreating = false; Aggressive = true; retreatUntil = retreatDeadline = reengageUntil = 0; }
    }
}
