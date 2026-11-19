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
    public enum WeaponKind { Fists, Sword, Axe }

    public static class CombatTiming
    {
        public const float StrikeDurationMultiplier = 2;
    }

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
        public static WeaponStyle AllowedStyle(WeaponKind weapon, WeaponStyle style) =>
            weapon == WeaponKind.Fists || weapon == WeaponKind.Axe && style == WeaponStyle.Thrusting
                ? WeaponStyle.Normal : style;
        public static WeaponStyleOptions ForWeapon(WeaponKind weapon, WeaponStyle style)
        {
            var options = Sword(AllowedStyle(weapon, style));
            return new WeaponStyleOptions(options.NormalAttack, options.StrongAttack, weapon != WeaponKind.Axe);
        }
        public static bool AllowsAttack(WeaponKind weapon, AttackChoice attack) => weapon == WeaponKind.Sword
            || weapon == WeaponKind.Axe && (attack == AttackChoice.Slash || attack == AttackChoice.Sweep)
            || weapon == WeaponKind.Fists && attack == AttackChoice.Slash;
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
        public float ReadyHeldDuration(float now, float duration) => Active
            ? System.Math.Max(0, now - startedAt - duration) : 0;
        public bool Ready(float now, float duration) => Active && WindupProgress(now, duration) >= 1;
        public float Tick(float now, bool expire = true)
        {
            if (!Active) return 0;
            float until = expire ? System.Math.Min(now, startedAt + Duration) : now;
            float elapsed = System.Math.Max(0, until - billedUntil);
            billedUntil += elapsed;
            if (expire && now >= startedAt + Duration) Active = false;
            return elapsed;
        }
        public void Cancel() { Active = false; }
    }

    // Charge is sampled once on release, independently of the minimum animation windup.
    public sealed class AttackCharge
    {
        public const float DefaultDuration = .6f;
        public const float DefaultPerfectWindow = .1f;
        public bool Active { get; private set; }
        public bool Released { get; private set; }
        float startedAt, duration, perfectWindow, cap, peak, releasedStrength;
        bool releasedPerfect;
        public void Begin(float now, float chargeDuration, float window, float sustainedStrength, float perfectStrength)
        {
            startedAt = now;
            duration = System.Math.Max(.01f, chargeDuration);
            perfectWindow = System.Math.Max(0, window);
            cap = System.Math.Max(1, sustainedStrength);
            peak = System.Math.Max(cap, perfectStrength);
            Active = true; Released = false; releasedPerfect = false;
            releasedStrength = 1;
        }
        public bool Perfect(float now) => Active && (Released ? releasedPerfect
            : perfectWindow > 0 && now - startedAt >= duration + perfectWindow * .3f
                && now - startedAt <= duration + perfectWindow * .7f);
        public float Strength(float now)
        {
            if (!Active) return 1;
            if (Released) return releasedStrength;
            float extraTime = now - startedAt - duration;
            if (perfectWindow > 0 && extraTime >= 0 && extraTime < perfectWindow)
            {
                float t = extraTime / perfectWindow;
                float envelope = t < .3f ? t / .3f : t <= .7f ? 1 : (1 - t) / .3f;
                envelope = envelope * envelope * (3 - 2 * envelope);
                return cap + (peak - cap) * envelope;
            }
            float progress = System.Math.Clamp((now - startedAt) / duration, 0, 1);
            return 1 + (cap - 1) * progress;
        }
        public float Meter(float now)
        {
            if (!Active) return 0;
            float strength = Strength(now);
            return System.Math.Clamp(strength / peak, 0, 1);
        }
        public void Release(float now)
        {
            if (!Active || Released) return;
            releasedStrength = Strength(now);
            releasedPerfect = Perfect(now);
            Released = true;
        }
        public void Cancel() { Active = Released = releasedPerfect = false; releasedStrength = 1; }
    }

    public sealed class AttackReleaseGate
    {
        public bool Waiting { get; private set; }
        public void Begin(bool waitForRelease) => Waiting = waitForRelease;
        public void Release() => Waiting = false;
        public void Cancel() => Waiting = false;
        public float Advance(float elapsed, float delta, float windup) => Waiting
            ? System.Math.Min(System.Math.Max(0, delta), System.Math.Max(0, windup - elapsed))
            : System.Math.Max(0, delta);
    }

    public sealed class SpecialAttackSelector
    {
        public bool Holding { get; private set; }
        WeaponStyleOptions options;
        bool releaseMode;
        public AttackChoice Begin(WeaponStyleOptions style, bool holdToWindup = false)
        {
            Cancel();
            options = style;
            releaseMode = holdToWindup;
            if (holdToWindup)
            {
                Holding = true;
                return style.NormalAttack;
            }
            // Prepared strong-only actions commit on press; normal slashes retain release input.
            if (style.NormalAttack == AttackChoice.None) return style.StrongAttack;
            Holding = true;
            return AttackChoice.None;
        }
        public AttackChoice Release()
        {
            if (!Holding) return AttackChoice.None;
            Holding = false;
            return releaseMode ? options.NormalAttack == AttackChoice.None ? options.StrongAttack : AttackChoice.None : options.NormalAttack;
        }
        public void Cancel() { Holding = false; }
    }
}
