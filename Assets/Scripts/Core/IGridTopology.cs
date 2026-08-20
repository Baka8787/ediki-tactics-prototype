using System.Collections.Generic;

namespace Ediki.Core
{
    /// <summary>
    /// Grid adjacency. Stage 01 implements SquareGrid4 ONLY.
    ///
    /// R-GRID-01: do NOT implement a hex topology. The interface exists so a
    /// future swap does not touch the rule layer — not so we can support two
    /// grids now.
    /// </summary>
    public interface IGridTopology
    {
        /// <summary>Neighbour order MUST be stable for the same input (R-GRID-02 / A6).</summary>
        IEnumerable<Coord> Neighbors(Coord c);

        int Distance(Coord a, Coord b);

        bool Contains(Coord c);

        IEnumerable<Coord> AllCoords();
    }

    /// <summary>4-directional square grid (OD-04 baseline).</summary>
    public sealed class SquareGrid4 : IGridTopology
    {
        public readonly int Width;
        public readonly int Height;

        // Fixed order: N, E, S, W. Stability is a hard requirement (A6).
        private static readonly int[] OffsetX = { 0, 1, 0, -1 };
        private static readonly int[] OffsetY = { -1, 0, 1, 0 };

        public SquareGrid4(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public IEnumerable<Coord> Neighbors(Coord c)
        {
            for (int i = 0; i < 4; i++)
            {
                Coord n = new Coord(c.X + OffsetX[i], c.Y + OffsetY[i]);
                if (Contains(n)) yield return n;
            }
        }

        public int Distance(Coord a, Coord b) => Coord.ManhattanDistance(a, b);

        public bool Contains(Coord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

        public IEnumerable<Coord> AllCoords()
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    yield return new Coord(x, y);
        }
    }
}
