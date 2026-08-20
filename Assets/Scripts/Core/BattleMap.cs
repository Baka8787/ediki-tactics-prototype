using System;

namespace Ediki.Core
{
    /// <summary>
    /// Static battlefield geometry: size + terrain per cell.
    ///
    /// IMMUTABLE. Terrain never changes during a Stage 01 battle, so clones of
    /// BattleState share the same BattleMap reference instead of deep-copying it.
    /// If terrain ever becomes mutable (e.g. the pollution system), this class
    /// must gain a Clone() and BattleState.Clone() must call it.
    /// </summary>
    public sealed class BattleMap
    {
        public readonly int Width;
        public readonly int Height;
        public readonly TerrainCatalog Terrain;
        public readonly IGridTopology Topology;

        private readonly int[] _terrainIndex; // row-major

        public BattleMap(int width, int height, TerrainCatalog terrain, int[] terrainIndexRowMajor)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Map size must be positive.");
            if (terrainIndexRowMajor == null || terrainIndexRowMajor.Length != width * height)
                throw new ArgumentException("Terrain array length must equal width*height.");

            Width = width;
            Height = height;
            Terrain = terrain;
            _terrainIndex = terrainIndexRowMajor;
            Topology = new SquareGrid4(width, height);
        }

        public bool Contains(Coord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

        public TerrainDef TerrainAt(Coord c)
        {
            if (!Contains(c)) throw new ArgumentOutOfRangeException(nameof(c), "Coord " + c + " is outside the map.");
            return Terrain[_terrainIndex[c.Y * Width + c.X]];
        }

        /// <summary>Terrain-only passability. Does not consider unit occupancy.</summary>
        public bool IsPassable(Coord c) => Contains(c) && !TerrainAt(c).BlocksMovement;

        /// <summary>
        /// Whatever ends up standing here dies. Passable on purpose — see
        /// TerrainDef.IsLethal: a hazard you cannot enter is just a wall, and the
        /// point of this one is that units can be PUT there.
        /// </summary>
        public bool IsLethal(Coord c) => Contains(c) && TerrainAt(c).IsLethal;

        /// <summary>AP cost of entering this cell, rounded up. Undefined for blocking terrain.</summary>
        public int MovementCost(Coord c) => TerrainAt(c).MovementCost;

        /// <summary>
        /// Exact entry cost in hundredths of AP. Path costs accumulate in these
        /// and are rounded up ONCE at the end — rounding per cell would throw the
        /// fraction away and make 1.5 behave exactly like 2.
        /// </summary>
        public int MovementCostHundredths(Coord c) => TerrainAt(c).MovementCostHundredths;
    }
}
