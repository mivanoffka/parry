using System;

namespace Parry
{
    public static class CameraZoom
    {
        public static float Pitch(float size, float normalSize, float maximumSize,
            float normalPitch = 39, float closePitch = 28, float distantPitch = 58)
        {
            bool close = size <= normalSize;
            float low = close ? 3 : normalSize;
            float high = close ? normalSize : maximumSize;
            float t = Math.Clamp((size - low) / Math.Max(.001f, high - low), 0, 1);
            t = t * t * (3 - 2 * t);
            float from = close ? closePitch : normalPitch;
            float to = close ? normalPitch : distantPitch;
            return from + (to - from) * t;
        }
    }
}
