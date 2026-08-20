using System;
using System.Collections.Generic;

namespace Ediki.Core
{
    /// <summary>
    /// One terrain type. Comes from data (terrain.txt) — never hardcoded (A5).
    /// Deliberately tiny: MovementCost + BlocksMovement is all Stage 01 needs.
    /// Do not grow this into a terrain framework without a decided requirement.
    /// </summary>
    public sealed class TerrainDef
    {
        public readonly int Index;
        public readonly string Name;
        public readonly char Symbol;
        public readonly bool BlocksMovement;

        /// <summary>
        /// Anything that ends up standing here dies immediately. Pits, chasms,
        /// deep water — the "instant death terrain" the design notes list as one
        /// of the missing affordance multipliers.
        ///
        /// A lethal cell is PASSABLE, not blocking, and that distinction is the
        /// whole mechanism: blocking terrain shapes where units can go, lethal
        /// terrain shapes where they can be PUT. Displacement is what makes it
        /// matter, which is why Push is the verb that reaches it.
        ///
        /// Units never path into one voluntarily — MovementCalculator refuses it
        /// as a destination (see the note there). Without that, LegalCommands
        /// would list suicide moves and the batch runner's uniform noise sampler
        /// would take them, killing units at random on any map with a hazard and
        /// quietly corrupting every measurement taken on it.
        /// </summary>
        public readonly bool IsLethal;

        /// <summary>
        /// AP to enter one cell, in hundredths. `cost=1.5` in data is 150 here.
        ///
        /// Stored scaled instead of as a float because the rule layer is integer
        /// only (determinism rule 1) — a float would make the same path cost
        /// different amounts on different machines.
        ///
        /// Costs ACCUMULATE exactly and are rounded up once, at the end of a path,
        /// not per cell. One forest at 1.5 costs 2 AP; two cost 3, not 4. Per-cell
        /// rounding would make the fraction meaningless — every cell would price
        /// the same as the next integer up.
        /// </summary>
        public readonly int MovementCostHundredths;

        /// <summary>AP for a single step, rounded up. For display and one-cell checks.</summary>
        public int MovementCost => CeilToAp(MovementCostHundredths);

        public static int CeilToAp(int hundredths) => (hundredths + 99) / 100;

        public TerrainDef(int index, string name, char symbol, int movementCostHundredths, bool blocksMovement,
                          bool isLethal = false)
        {
            Index = index;
            Name = name;
            Symbol = symbol;
            MovementCostHundredths = movementCostHundredths;
            BlocksMovement = blocksMovement;
            IsLethal = isLethal;
        }

        public override string ToString() => Name;
    }

    /// <summary>Immutable lookup of all terrain types, ordered by declaration.</summary>
    public sealed class TerrainCatalog
    {
        private readonly TerrainDef[] _byIndex;
        private readonly Dictionary<char, TerrainDef> _bySymbol;
        private readonly Dictionary<string, TerrainDef> _byName;

        public TerrainCatalog(IList<TerrainDef> defs)
        {
            if (defs == null || defs.Count == 0)
                throw new ArgumentException("TerrainCatalog requires at least one terrain definition.");

            _byIndex = new TerrainDef[defs.Count];
            _bySymbol = new Dictionary<char, TerrainDef>();
            _byName = new Dictionary<string, TerrainDef>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < defs.Count; i++)
            {
                TerrainDef d = defs[i];
                _byIndex[i] = d;
                if (_bySymbol.ContainsKey(d.Symbol))
                    throw new ArgumentException("Duplicate terrain symbol '" + d.Symbol + "'.");
                _bySymbol.Add(d.Symbol, d);
                if (_byName.ContainsKey(d.Name))
                    throw new ArgumentException("Duplicate terrain name '" + d.Name + "'.");
                _byName.Add(d.Name, d);
            }
        }

        public int Count => _byIndex.Length;

        public TerrainDef this[int index] => _byIndex[index];

        public bool TryGetBySymbol(char symbol, out TerrainDef def) => _bySymbol.TryGetValue(symbol, out def);

        public bool TryGetByName(string name, out TerrainDef def) => _byName.TryGetValue(name, out def);
    }
}
