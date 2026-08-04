using System;

namespace DestinyTogether.Core
{
    /// <summary>Substituto de UnityEngine.Mathf para as camadas puras.</summary>
    public static class MathUtil
    {
        public const float Epsilon = 1e-5f;

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v)
            => Math.Abs(b - a) < Epsilon ? 0f : Clamp01((v - a) / (b - a));
        public static float MoveTowards(float from, float to, float maxDelta)
        {
            float d = to - from;
            if (Math.Abs(d) <= maxDelta) return to;
            return from + Math.Sign(d) * maxDelta;
        }
    }
}
