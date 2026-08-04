using DestinyTogether.Core;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// A unica ponte entre o espaco da simulacao e o espaco do mundo.
    ///
    /// A simulacao nao sabe o que e um metro: ela trabalha em CELULAS no plano (X = leste,
    /// Y = norte). Aqui isso vira Vector3 com Y de altura. Se um dia a escala do mundo mudar
    /// (arte final com predios maiores), muda-se CellSize e nenhuma linha de logica e tocada.
    /// </summary>
    public static class GridToWorld
    {
        /// <summary>Tamanho de uma celula em unidades Unity. 1 celula = 1 unidade = "1 metro" do design.</summary>
        public const float CellSize = 1f;

        public static Vector3 ToWorld(Vec2 p, float height = 0f)
            => new Vector3(p.X * CellSize, height, p.Y * CellSize);

        public static Vector3 ToWorld(GridCoord c, float height = 0f)
            => ToWorld(c.Center, height);

        public static Vec2 ToSim(Vector3 world)
            => new Vec2(world.x / CellSize, world.z / CellSize);

        public static GridCoord ToCell(Vector3 world)
            => GridCoord.FromPosition(ToSim(world));

        public static Vector3 DirectionToWorld(Vec2 dir)
            => new Vector3(dir.X, 0f, dir.Y);
    }
}
