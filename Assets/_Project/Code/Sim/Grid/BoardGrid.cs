using System;
using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    public enum CellState : byte
    {
        Vazio = 0,
        Prefeitura = 1,
        Predio = 2,
        /// <summary>Predio destruido: tile bloqueado ate ser limpo. A penalidade e ESPACIAL, nao temporal.</summary>
        Escombro = 3,
        /// <summary>Tumulo de heroi: ocupa o tile permanentemente, no Quadrante do dono.</summary>
        Tumulo = 4
    }

    /// <summary>
    /// O tabuleiro construivel: GridSize x GridSize celulas com a Prefeitura fixa no centro.
    /// Sabe ocupacao, adjacencia, Quadrante e — o mais importante do jogo — o perimetro exposto
    /// e o tile de cidade mais proximo de um ponto, que e o que define o "tempo de aproximacao".
    /// </summary>
    public sealed class BoardGrid
    {
        public readonly int Size;
        public readonly int TownHallSize;
        public readonly GridCoord TownHallMin;
        public readonly GridCoord TownHallMax;
        public readonly Vec2 Center;

        private readonly CellState[] _cells;
        private readonly EntityId[] _occupants;

        public BoardGrid(int size, int townHallSize)
        {
            if (size < 3) throw new ArgumentException("Grid pequeno demais", nameof(size));
            if (townHallSize < 1 || townHallSize > size) throw new ArgumentException("Prefeitura nao cabe", nameof(townHallSize));

            Size = size;
            TownHallSize = townHallSize;
            _cells = new CellState[size * size];
            _occupants = new EntityId[size * size];

            int min = (size - townHallSize) / 2;
            TownHallMin = new GridCoord(min, min);
            TownHallMax = new GridCoord(min + townHallSize - 1, min + townHallSize - 1);
            Center = new Vec2(size * 0.5f, size * 0.5f);

            for (int y = TownHallMin.Y; y <= TownHallMax.Y; y++)
                for (int x = TownHallMin.X; x <= TownHallMax.X; x++)
                    _cells[Index(x, y)] = CellState.Prefeitura;
        }

        private int Index(int x, int y) => y * Size + x;

        public bool InBounds(GridCoord c) => c.X >= 0 && c.X < Size && c.Y >= 0 && c.Y < Size;

        public CellState Get(GridCoord c) => InBounds(c) ? _cells[Index(c.X, c.Y)] : CellState.Vazio;
        public EntityId OccupantOf(GridCoord c) => InBounds(c) ? _occupants[Index(c.X, c.Y)] : EntityId.None;

        public bool IsCityTile(GridCoord c)
        {
            var s = Get(c);
            return s == CellState.Prefeitura || s == CellState.Predio;
        }

        /// <summary>Celula livre para construir (nao vale sobre Escombro nem Tumulo).</summary>
        public bool IsFree(GridCoord c) => InBounds(c) && _cells[Index(c.X, c.Y)] == CellState.Vazio;

        public void Set(GridCoord c, CellState state, EntityId occupant)
        {
            if (!InBounds(c)) return;
            int i = Index(c.X, c.Y);
            _cells[i] = state;
            _occupants[i] = occupant;
        }

        public void Clear(GridCoord c)
        {
            if (!InBounds(c)) return;
            int i = Index(c.X, c.Y);
            _cells[i] = CellState.Vazio;
            _occupants[i] = EntityId.None;
        }

        /// <summary>
        /// Regra de colocacao: so ortogonalmente adjacente a um tile de cidade existente.
        /// E o que faz a cidade CRESCER a partir da Prefeitura em vez de virar ilhas soltas.
        /// </summary>
        public bool IsAdjacentToCity(GridCoord c)
        {
            for (int i = 0; i < GridCoord.Orthogonal.Length; i++)
                if (IsCityTile(c + GridCoord.Orthogonal[i]))
                    return true;
            return false;
        }

        public Quadrant QuadrantOf(GridCoord c)
        {
            float half = Size * 0.5f;
            bool east = c.X + 0.5f >= half;
            bool north = c.Y + 0.5f >= half;
            if (north) return east ? Quadrant.Nordeste : Quadrant.Noroeste;
            return east ? Quadrant.Sudeste : Quadrant.Sudoeste;
        }

        /// <summary>
        /// Tile de cidade mais proximo de um ponto no mundo. E o alvo de caminhada do monstro
        /// e a base do calculo de tempo de aproximacao.
        /// </summary>
        public bool TryGetNearestCityTile(Vec2 from, out GridCoord nearest, out float distance)
        {
            nearest = GridCoord.Invalid;
            distance = float.MaxValue;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var s = _cells[Index(x, y)];
                    if (s != CellState.Prefeitura && s != CellState.Predio) continue;
                    var c = new GridCoord(x, y);
                    float d = Vec2.Distance(from, c.Center);
                    if (d < distance)
                    {
                        distance = d;
                        nearest = c;
                    }
                }
            }
            return nearest.IsValid;
        }

        /// <summary>
        /// Numero de lados de tile de cidade que fazem fronteira com o vazio.
        /// E a metrica de "superficie de ataque": construir para fora sobe o DPS e sobe isto.
        /// </summary>
        public int ExposedPerimeter()
        {
            int count = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var c = new GridCoord(x, y);
                    if (!IsCityTile(c)) continue;
                    for (int i = 0; i < GridCoord.Orthogonal.Length; i++)
                    {
                        var n = c + GridCoord.Orthogonal[i];
                        if (!InBounds(n) || !IsCityTile(n)) count++;
                    }
                }
            }
            return count;
        }

        public IEnumerable<GridCoord> AllCells()
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    yield return new GridCoord(x, y);
        }

        public IEnumerable<GridCoord> CellsInQuadrant(Quadrant q)
        {
            foreach (var c in AllCells())
                if (QuadrantOf(c) == q)
                    yield return c;
        }

        /// <summary>Snapshot leve para a apresentacao desenhar o tabuleiro sem tocar no estado.</summary>
        public void CopyCellsTo(CellState[] destination)
        {
            if (destination == null || destination.Length < _cells.Length) return;
            Array.Copy(_cells, destination, _cells.Length);
        }
    }
}
