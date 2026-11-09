namespace Parry
{
    public enum AttackChoice { None, Slash, Lunge, Thrust }
    public enum WeaponStyle { Normal, Thrusting }

    // Each style explicitly declares its three optional actions.
    public readonly struct WeaponStyleOptions
    {
        public readonly AttackChoice NormalAttack, StrongAttack;
        public readonly bool CanGuard;
        public WeaponStyleOptions(AttackChoice normal, AttackChoice strong, bool guard)
        { NormalAttack = normal; StrongAttack = strong; CanGuard = guard; }
        public static WeaponStyleOptions Sword(WeaponStyle style) => style == WeaponStyle.Thrusting
            ? new WeaponStyleOptions(AttackChoice.None, AttackChoice.Lunge, true)
            : new WeaponStyleOptions(AttackChoice.Slash, AttackChoice.None, true);
    }

    // Tracks only the waiting phase; a frame crossing expiry is billed up to five seconds.
    public sealed class AttackPreparation
    {
        public const float Duration = 5;
        public bool Active { get; private set; }
        float startedAt, billedUntil;
        public void Begin(float now) { startedAt = billedUntil = now; Active = true; }
        public float WindupProgress(float now, float duration) => !Active ? 0
            : duration <= 0 ? 1 : System.Math.Clamp((now - startedAt) / duration, 0, 1);
        public float RemainingWindup(float now, float duration) => Active
            ? System.Math.Max(0, duration - (now - startedAt)) : 0;
        public bool Ready(float now, float duration) => Active && WindupProgress(now, duration) >= 1;
        public float Tick(float now)
        {
            if (!Active) return 0;
            float until = System.Math.Min(now, startedAt + Duration);
            float elapsed = System.Math.Max(0, until - billedUntil);
            billedUntil += elapsed;
            if (now >= startedAt + Duration) Active = false;
            return elapsed;
        }
        public void Cancel() { Active = false; }
    }

    public sealed class SpecialAttackSelector
    {
        public bool Holding { get; private set; }
        WeaponStyleOptions options;
        public AttackChoice Begin(WeaponStyleOptions style)
        {
            Cancel();
            options = style;
            // Prepared strong-only actions commit on press; normal slashes retain release input.
            if (style.NormalAttack == AttackChoice.None) return style.StrongAttack;
            Holding = true;
            return AttackChoice.None;
        }
        public AttackChoice Release()
        {
            if (!Holding) return AttackChoice.None;
            Holding = false;
            return options.NormalAttack;
        }
        public void Cancel() { Holding = false; }
    }
}
