using System;

namespace Parry
{
    // One cycle contains two steps. Visual cadence is bounded independently of world speed.
    public sealed class LocomotionCycle
    {
        public const float MaximumCyclesPerSecond = 1.5f;
        public float Phase { get; private set; }
        public static float Rate(float speed, bool guarding) => speed <= .05f ? 0
            : Math.Min(MaximumCyclesPerSecond, Math.Max(.65f, speed / 5)) * (guarding ? .85f : 1);
        public void Advance(float speed, float elapsed, bool guarding)
        {
            Phase = (Phase + Rate(speed, guarding) * Math.Max(0, elapsed)) % 1;
        }
    }
}
