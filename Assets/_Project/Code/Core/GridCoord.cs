using System;

namespace DestinyTogether.Core
{
    /// <summary>
    /// Coordenada inteira de celula. O "espaco" da construcao e do tabuleiro.
    /// Origem (0,0) = canto sudoeste do terreno.
    /// </summary>
    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>
    {
        public int X;
        public int Y;

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly GridCoord Invalid = new GridCoord(int.MinValue, int.MinValue);
        public bool IsValid => X != int.MinValue;

        public static readonly GridCoord[] Orthogonal =
        {
            new GridCoord(1, 0), new GridCoord(-1, 0), new GridCoord(0, 1), new GridCoord(0, -1)
        };

        public static readonly GridCoord[] AllNeighbours =
        {
            new GridCoord(1, 0), new GridCoord(-1, 0), new GridCoord(0, 1), new GridCoord(0, -1),
            new GridCoord(1, 1), new GridCoord(1, -1), new GridCoord(-1, 1), new GridCoord(-1, -1)
        };

        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.X + b.X, a.Y + b.Y);
        public static GridCoord operator -(GridCoord a, GridCoord b) => new GridCoord(a.X - b.X, a.Y - b.Y);

        /// <summary>Distancia de Chebyshev (movimento em 8 direcoes custa 1).</summary>
        public static int ChebyshevDistance(GridCoord a, GridCoord b)
            => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        /// <summary>Distancia de Manhattan (movimento ortogonal).</summary>
        public static int ManhattanDistance(GridCoord a, GridCoord b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>Centro da celula em coordenadas continuas de simulacao (1 celula = 1 unidade).</summary>
        public Vec2 Center => new Vec2(X + 0.5f, Y + 0.5f);

        public static GridCoord FromPosition(Vec2 p) => new GridCoord((int)Math.Floor(p.X), (int)Math.Floor(p.Y));

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 73856093) ^ (Y * 19349663));
        public override string ToString() => $"[{X},{Y}]";

        public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
        public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
    }
}
