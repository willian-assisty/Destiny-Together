using System;

namespace DestinyTogether.Core
{
    /// <summary>
    /// Vetor 2D no plano do tabuleiro (X = leste, Y = norte).
    /// Existe para que DT.Sim nao precise referenciar UnityEngine: a simulacao roda
    /// em testes EditMode e em servidor headless sem engine.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-5f ? new Vec2(X / m, Y / m) : Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrMagnitude;
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * MathUtil.Clamp01(t);

        /// <summary>Move <paramref name="from"/> na direcao de <paramref name="to"/> no maximo <paramref name="maxDelta"/>.</summary>
        public static Vec2 MoveTowards(Vec2 from, Vec2 to, float maxDelta)
        {
            Vec2 d = to - from;
            float m = d.Magnitude;
            if (m <= maxDelta || m < 1e-5f) return to;
            return from + d / m * maxDelta;
        }

        /// <summary>Angulo em graus no sentido horario a partir do norte (+Y). Usado para nomear Faixas.</summary>
        public float CompassDegrees
        {
            get
            {
                float deg = (float)(Math.Atan2(X, Y) * 180.0 / Math.PI);
                return deg < 0f ? deg + 360f : deg;
            }
        }

        public static Vec2 FromCompassDegrees(float degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new Vec2((float)Math.Sin(rad), (float)Math.Cos(rad));
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }
}
