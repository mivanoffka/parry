using System;

namespace Parry
{
    public static class FighterSpacingRules
    {
        public const float MinimumDistance = 1.15f;
        public const float RestDistance = 1.4f;

        // First contact of a swept point with another fighter's exclusion circle.
        public static float ContactFraction(float x, float z, float dx, float dz)
        {
            float approach = x * dx + z * dz;
            float lengthSquared = dx * dx + dz * dz;
            if (approach >= 0 || lengthSquared < .0000001f) return 1;
            float c = x * x + z * z - MinimumDistance * MinimumDistance;
            if (c <= 0) return 0;
            float discriminant = approach * approach - lengthSquared * c;
            if (discriminant <= 0) return 1;
            float fraction = (-approach - (float)Math.Sqrt(discriminant)) / lengthSquared;
            return fraction >= 0 && fraction < 1 ? fraction : 1;
        }
    }
}
