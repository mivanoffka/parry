namespace Parry
{
    public enum LungeDodgeDefense { Hit, Block, Parry }
    public static class LungeDefenseRules
    {
        public static LungeDodgeDefense Caught(float sample, float parryChance, float blockChance)
        {
            float parry = System.Math.Clamp(parryChance, 0, 1);
            float block = System.Math.Clamp(blockChance, 0, 1 - parry);
            return sample < parry ? LungeDodgeDefense.Parry
                : sample < parry + block ? LungeDodgeDefense.Block : LungeDodgeDefense.Hit;
        }
    }

    public static class SwordStep
    {
        public static bool ProvokesRecoil(float distance, float alignment, float advance) =>
            advance > .001f && distance <= 1.35f + advance && alignment >= .35f;
        // Step into the cut during the last tenth of a second of its windup.
        public static float Distance(float elapsed, float deltaTime, float windup, float distance)
        {
            float duration = System.Math.Min(.1f, System.Math.Max(0, windup));
            if (duration <= 0 || deltaTime <= 0) return 0;
            float start = windup - duration;
            float overlap = System.Math.Max(0, System.Math.Min(elapsed + deltaTime, windup) - System.Math.Max(elapsed, start));
            return System.Math.Max(0, distance) * overlap / duration;
        }
    }

    public sealed class SlashSequence
    {
        public const float ContinuationWindow = .18f;
        public int Direction { get; private set; } = 1;
        float finishedAt = float.NegativeInfinity;
        bool slash;
        public void Begin(float now, bool isSlash)
        {
            Direction = isSlash && now >= finishedAt && now - finishedAt <= ContinuationWindow ? -Direction : 1;
            finishedAt = float.NegativeInfinity;
            slash = isSlash;
        }
        public void Finish(float now)
        {
            if (!slash) return;
            finishedAt = now;
            slash = false;
        }
        public void Reset() { slash = false; finishedAt = float.NegativeInfinity; Direction = 1; }
    }

    public enum AttackChoice { None, Slash, Lunge, Thrust, Sweep }
    public enum WeaponStyle { Normal, Thrusting, Sweeping }

    public static class PowerfulSweep
    {
        public const float HalfAngle = 120;
        public const float Windup = .9f;
        public const float ActiveTime = .24f;
        public const float Recovery = .6f;
        public const int DamageMultiplier = 2;
        public static bool IsPowerful(AttackChoice attack) => attack == AttackChoice.Lunge || attack == AttackChoice.Sweep;
    }

    // Each style explicitly declares its three optional actions.
    public readonly struct WeaponStyleOptions
    {
        public readonly AttackChoice NormalAttack, StrongAttack;
        public readonly bool CanGuard;
        public WeaponStyleOptions(AttackChoice normal, AttackChoice strong, bool guard)
        { NormalAttack = normal; StrongAttack = strong; CanGuard = guard; }
        public static WeaponStyleOptions Sword(WeaponStyle style) => style == WeaponStyle.Thrusting
            ? new WeaponStyleOptions(AttackChoice.None, AttackChoice.Lunge, true)
            : style == WeaponStyle.Sweeping ? new WeaponStyleOptions(AttackChoice.None, AttackChoice.Sweep, true)
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
