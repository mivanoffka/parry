using System;

namespace Parry
{
    public enum AttackChoice { None, Slash, Lunge }

    // Pointer coordinates use screen space: positive Y selects the upper menu sector.
    public sealed class SpecialAttackSelector
    {
        public const float HoldDuration = .28f;
        public const float SelectionDistance = 36;
        public bool Holding { get; private set; }
        public bool MenuOpen { get; private set; }
        public AttackChoice Selected { get; private set; }
        public float OriginX { get; private set; }
        public float OriginY { get; private set; }
        float pressedAt;

        public void Begin(float now, float x, float y)
        {
            Cancel();
            Holding = true;
            pressedAt = now;
            OriginX = x;
            OriginY = y;
        }

        public void Update(float now, float x, float y)
        {
            if (!Holding) return;
            MenuOpen = now - pressedAt >= HoldDuration;
            float dx = x - OriginX, dy = y - OriginY;
            Selected = MenuOpen && dy >= SelectionDistance && Math.Abs(dx) <= dy
                ? AttackChoice.Lunge : AttackChoice.None;
        }

        public AttackChoice Release(float now, float x, float y)
        {
            if (!Holding) return AttackChoice.None;
            Update(now, x, y);
            AttackChoice result = MenuOpen ? Selected : AttackChoice.Slash;
            Cancel();
            return result;
        }

        public void Cancel() { Holding = MenuOpen = false; Selected = AttackChoice.None; }
    }
}
