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

        /// <summary>
        /// O relevo do chao. Configurado UMA vez pelo Bootstrap; <see cref="GroundShape.Flat"/> ate la.
        ///
        /// Estado global num utilitario estatico e feio, e aqui vale o preco: esta e a unica ponte
        /// entre o espaco da simulacao e o espaco do mundo, entao por sob ela o relevo alcanca TUDO
        /// que pisa no chao — heroi, monstro, arvore, Esconderijo — de uma vez. A alternativa era
        /// somar a altura em quinze pontos de chamada e descobrir o decimo sexto quando algo
        /// aparecesse flutuando.
        ///
        /// O caminho de volta (<see cref="ToSim"/>) ignora Y e continua exato: a simulacao e plana.
        /// </summary>
        public static GroundShape Ground = GroundShape.Flat;

        public static Vector3 ToWorld(Vec2 p, float height = 0f)
            => new Vector3(p.X * CellSize, height + Ground.HeightAt(p), p.Y * CellSize);

        /// <summary>Ponto no mundo IGNORANDO o relevo. Para o que precisa de plano: malha do chao, HUD.</summary>
        public static Vector3 ToFlatWorld(Vec2 p, float height = 0f)
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
