using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Converte entre as 8 Faixas e a geometria do mundo. A Faixa e a unidade de comunicacao
    /// do time — precisa ser a mesma coisa no aviso sonoro, no HUD e no spawn.
    /// </summary>
    public static class LaneGeometry
    {
        public const int LaneCount = 8;

        /// <summary>Norte = 0 graus, crescendo no sentido horario (Nordeste = 45).</summary>
        public static float DegreesOf(Lane lane) => (int)lane * 45f;

        public static Vec2 DirectionOf(Lane lane) => Vec2.FromCompassDegrees(DegreesOf(lane));

        /// <summary>Ponto de spawn no anel dos Arredores, com dispersao lateral dentro da Faixa.</summary>
        public static Vec2 SpawnPoint(Vec2 center, float radius, Lane lane, Rng rng)
        {
            float spread = rng.Range(-20f, 20f);
            return center + Vec2.FromCompassDegrees(DegreesOf(lane) + spread) * radius;
        }

        public static Lane LaneOf(Vec2 center, Vec2 point)
        {
            float deg = (point - center).CompassDegrees;
            int idx = (int)Math.Round(deg / 45f) % LaneCount;
            return (Lane)idx;
        }

        public static string ShortName(Lane lane)
        {
            switch (lane)
            {
                case Lane.Norte: return "N";
                case Lane.Nordeste: return "NE";
                case Lane.Leste: return "L";
                case Lane.Sudeste: return "SE";
                case Lane.Sul: return "S";
                case Lane.Sudoeste: return "SO";
                case Lane.Oeste: return "O";
                case Lane.Noroeste: return "NO";
                default: return "?";
            }
        }
    }
}
