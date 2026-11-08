using System;

namespace Parry
{
    // Engine-independent combat rules, shared by both fighters.
    public sealed class PostureState
    {
        public const float Maximum = 100;
        public const float FatigueThreshold = 60;
        public const float MaximumFatigue = 8;
        public float Pressure { get; private set; }
        public int EndurancePoints { get; private set; } = 100;
        public float RemainingEndurance => (Maximum - Pressure) * EndurancePoints / Maximum;
        public void ConfigureEndurance(int points)
        {
            EndurancePoints = Math.Max(1, points);
            Reset();
        }
        float NormalizeCost(float points) => points * Maximum / EndurancePoints;
        public float Fatigue { get; private set; }
        public float RecoveryMultiplier => 1 / (1 + Fatigue * .5f);
        public bool Blocking { get; private set; }
        public bool Broken(float now) => now < brokenUntil;
        public bool Disrupted(float now) => Broken(now) || now < recoilUntil;
        public bool CanParry(float now) => Blocking && now < parryUntil && !Disrupted(now);
        float brokenUntil, recoilUntil, parryUntil, nextParry, recoverAfter;
        public void RecordCombat(float now) => recoverAfter = Math.Max(recoverAfter, now + 2);

        public void SetBlocking(bool blocking, float now)
        {
            blocking &= !Disrupted(now);
            if (blocking && !Blocking)
            {
                parryUntil = now >= nextParry ? now + .28f : now;
                nextParry = now + .3f;
            }
            Blocking = blocking;
        }
        public void AddPressure(float amount, float now)
        {
            if (amount <= 0) return;
            RecordCombat(now);
            if (Broken(now)) return;
            if (Pressure >= Maximum) Pressure = 35;
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
        }
        public void Recoil(float now) { RecordCombat(now); recoilUntil = Math.Max(recoilUntil, now + .35f); Blocking = false; }
        public bool TryEmergencyGuard(float pressureCost, float now)
        {
            if (Disrupted(now)) return false;
            AddPressure(pressureCost, now);
            SetBlocking(true, now);
            return Blocking;
        }
        public bool TrySpendForRoll(float pressureCost, float now)
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
            bool resting = now > recoverAfter && !Disrupted(now);
            if (Pressure > FatigueThreshold) Fatigue = Math.Min(MaximumFatigue, Fatigue + elapsed);
            else if (resting) Fatigue = Math.Max(0, Fatigue - elapsed * .5f);
            if (Broken(now)) return;
            if (Pressure >= Maximum) { Pressure = 35; recoverAfter = Math.Max(recoverAfter, now + .5f); resting = false; }
            if (resting)
                Pressure = Math.Max(0, Pressure - NormalizeCost((Blocking ? 28 : 18) * elapsed * RecoveryMultiplier));
        }
        public void Reset()
        {
            Pressure = Fatigue = brokenUntil = recoilUntil = parryUntil = nextParry = recoverAfter = 0;
            Blocking = false;
        }
    }
}
