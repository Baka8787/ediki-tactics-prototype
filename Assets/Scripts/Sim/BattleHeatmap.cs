using System.Globalization;
using System.Text;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// A counter per map cell.
    ///
    /// One flat array rather than int[,]: the grid is merged across hundreds of
    /// battles and indexed in tight loops, and a rectangular array costs a bounds
    /// check per dimension for no benefit here. Width and Height come from the
    /// map that was actually played — nothing in this file knows a map size.
    /// </summary>
    public sealed class SpatialGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly int[] _cells;

        public SpatialGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new int[width * height];
        }

        public int this[int x, int y] => InBounds(x, y) ? _cells[y * Width + x] : 0;

        public int this[Coord c] => this[c.X, c.Y];

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>
        /// Counts one sample. Returns false for a coordinate off the map WITHOUT
        /// clamping or wrapping it: a unit standing outside the grid is a fault in
        /// whatever produced it, and silently folding it onto an edge cell would
        /// hide that while making the heatmap subtly wrong.
        /// </summary>
        public bool Add(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            _cells[y * Width + x]++;
            return true;
        }

        public bool Add(Coord c) => Add(c.X, c.Y);

        public void Merge(SpatialGrid other)
        {
            if (other == null) return;
            if (other.Width != Width || other.Height != Height)
            {
                throw new System.InvalidOperationException(
                    "Cannot merge a " + other.Width + "x" + other.Height +
                    " grid into a " + Width + "x" + Height + " one — different maps.");
            }

            for (int i = 0; i < _cells.Length; i++) _cells[i] += other._cells[i];
        }

        public long Total
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < _cells.Length; i++) sum += _cells[i];
                return sum;
            }
        }

        public int Max
        {
            get
            {
                int max = 0;
                for (int i = 0; i < _cells.Length; i++) if (_cells[i] > max) max = _cells[i];
                return max;
            }
        }
    }

    /// <summary>
    /// Where the fighting happened, as two counters per cell.
    ///
    ///   Occupancy  where units STOOD, sampled once per unit per round
    ///   Clash      where damage LANDED, one per damaging effect
    ///
    /// The pair answers a question no scalar metric can: M4 says the party's mean
    /// exposure was 0.29, and this says whether that is one safe pocket everybody
    /// crowds into or a spread across the map. Purely derived from the effect
    /// stream and the states the runner already had — it re-computes nothing.
    /// </summary>
    public sealed class BattleHeatmap
    {
        public readonly int Width;
        public readonly int Height;

        public readonly SpatialGrid Occupancy;
        public readonly SpatialGrid Clash;

        /// <summary>
        /// The map these grids describe. Held for rendering blocked cells; it is
        /// immutable and shared across every clone of a state, so keeping it costs
        /// nothing and cannot go stale.
        /// </summary>
        public readonly BattleMap Map;

        /// <summary>Battles merged into this heatmap. 1 for a single run.</summary>
        public int BattlesObserved;

        /// <summary>
        /// Samples whose coordinate was off the map. Should always be zero; it is
        /// counted rather than ignored so a test can assert that, and so a real
        /// fault surfaces as a number instead of as a quietly wrong picture.
        /// </summary>
        public int OutOfBoundsSamples;

        public BattleHeatmap(BattleMap map)
        {
            Map = map;
            Width = map.Width;
            Height = map.Height;
            Occupancy = new SpatialGrid(Width, Height);
            Clash = new SpatialGrid(Width, Height);
        }

        public void Merge(BattleHeatmap other)
        {
            if (other == null) return;
            Occupancy.Merge(other.Occupancy);
            Clash.Merge(other.Clash);
            BattlesObserved += other.BattlesObserved;
            OutOfBoundsSamples += other.OutOfBoundsSamples;
        }

        // ------------------------------------------------------------ rendering

        public const char Empty = '.';
        public const char Overflow = '*';
        public const char Blocked = '#';

        /// <summary>
        /// Both grids, one after the other.
        ///
        /// Row 0 prints FIRST because y=0 is the top row in an encounter file, so
        /// the heatmap lines up with the map block a designer wrote. Flipping it
        /// to "maths orientation" would make the two impossible to compare.
        /// </summary>
        public string Render()
        {
            StringBuilder sb = new StringBuilder();
            RenderInto(sb, "Occupancy heatmap", "unit-rounds standing here", Occupancy);
            sb.AppendLine();
            RenderInto(sb, "Clash heatmap", "damaging effects landed here", Clash);
            return sb.ToString();
        }

        public string Render(SpatialGrid grid, string title, string what)
        {
            StringBuilder sb = new StringBuilder();
            RenderInto(sb, title, what, grid);
            return sb.ToString();
        }

        /// <summary>
        /// One row of a grid as its bare characters, no labels.
        ///
        /// Exposed so the orientation and the symbol table can be asserted
        /// against the API rather than by re-parsing the decorated output — a
        /// test that scrapes its own report ends up testing the decoration.
        /// </summary>
        public string RenderRow(SpatialGrid grid, int y)
        {
            StringBuilder sb = new StringBuilder(Width);
            for (int x = 0; x < Width; x++) sb.Append(CellChar(grid, x, y));
            return sb.ToString();
        }

        private void RenderInto(StringBuilder sb, string title, string what, SpatialGrid grid)
        {
            sb.Append("  ").Append(title).Append("  ").Append(N(Width)).Append('x').Append(N(Height))
              .Append("   ").Append(what)
              .Append("   total ").Append(grid.Total.ToString(CultureInfo.InvariantCulture))
              .Append("  peak ").Append(N(grid.Max))
              .Append("  over ").Append(N(BattlesObserved)).AppendLine(" battle(s)");
            sb.AppendLine("    . = 0   1-9 = that many   * = 10 or more   # = blocked"
                          + "   (row 0 is the TOP row, as in the encounter file)");

            // x ruler, so a column can be read off without counting.
            sb.Append("      ");
            for (int x = 0; x < Width; x++) sb.Append((char)('0' + x % 10));
            sb.AppendLine();

            for (int y = 0; y < Height; y++)
                sb.Append("   ").Append(N(y).PadLeft(2)).Append("  ").AppendLine(RenderRow(grid, y));

            if (OutOfBoundsSamples > 0)
            {
                sb.Append("    ** ").Append(N(OutOfBoundsSamples))
                  .AppendLine(" sample(s) fell OUTSIDE the map — that is a fault, not a rendering choice");
            }
        }

        /// <summary>
        /// Blocked terrain wins over the count.
        ///
        /// Nothing can stand on it and nothing can be damaged on it, so a non-zero
        /// count there means something upstream is wrong — which the '#' would
        /// hide. Tests assert blocked cells stay at zero for exactly that reason.
        /// </summary>
        private char CellChar(SpatialGrid grid, int x, int y)
        {
            if (!Map.IsPassable(new Coord(x, y))) return Blocked;

            int n = grid[x, y];
            if (n <= 0) return Empty;
            if (n >= 10) return Overflow;
            return (char)('0' + n);
        }

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Where each unit is standing, as an effect log is walked.
    ///
    /// Effects name a unit but not a place, and by the time a phase log is handed
    /// over its units have already moved — so any observer that needs "where was
    /// the victim when this landed" has to re-seed from a known state and follow
    /// the movement effects forward. Shared so the heatmap and the role metrics
    /// cannot answer that question differently.
    ///
    /// Arrays grown by doubling; nothing is allocated per effect.
    /// </summary>
    public sealed class UnitPositionTracker
    {
        private Coord[] _position = new Coord[16];
        private bool[] _known = new bool[16];

        public void Grow(BattleState state)
        {
            int highest = 0;
            for (int i = 0; i < state.Units.Count; i++)
                if (state.Units[i].Id > highest) highest = state.Units[i].Id;

            if (highest < _position.Length) return;

            int size = _position.Length;
            while (size <= highest) size *= 2;
            System.Array.Resize(ref _position, size);
            System.Array.Resize(ref _known, size);
        }

        /// <summary>Re-reads every unit's place from a state known to be current.</summary>
        public void Seed(BattleState state)
        {
            Grow(state);
            for (int i = 0; i < _known.Length; i++) _known[i] = false;

            for (int i = 0; i < state.Units.Count; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.Id < 0 || unit.Id >= _position.Length) continue;
                _position[unit.Id] = unit.Position;
                _known[unit.Id] = true;
            }
        }

        /// <summary>Applies an effect's movement, if it has any. Returns true when it did.</summary>
        public bool Follow(Effect e)
        {
            if (e is UnitMoved moved) { Remember(moved.UnitId, moved.To); return true; }
            if (e is UnitPushed pushed) { Remember(pushed.UnitId, pushed.To); return true; }
            if (e is UnitSpawned spawned) { Remember(spawned.UnitId, spawned.Position); return true; }
            return false;
        }

        public void Remember(int unitId, Coord where)
        {
            if (unitId < 0 || unitId >= _position.Length) return;
            _position[unitId] = where;
            _known[unitId] = true;
        }

        public bool TryPosition(int unitId, out Coord where)
        {
            if (unitId >= 0 && unitId < _position.Length && _known[unitId])
            {
                where = _position[unitId];
                return true;
            }

            where = new Coord(-1, -1);
            return false;
        }
    }

    /// <summary>
    /// Fills a BattleHeatmap by watching a battle through IBattleObserver.
    ///
    /// It reads and counts. It never touches a state it is handed, issues no
    /// command, and asks no question of the rule layer, so attaching it cannot
    /// move a unit or consume a random number — the property
    /// AttachingHeatmapObserverDoesNotChangeBattle exists to keep that true.
    ///
    /// One instance can watch a whole batch: RunBatch reuses the config, so the
    /// grids accumulate across every seed of a cell without allocating a grid per
    /// battle.
    /// </summary>
    public sealed class HeatmapObserver : IBattleObserver
    {
        private BattleHeatmap _heatmap;

        /// <summary>
        /// Where each unit is standing right now.
        ///
        /// Damage is reported by HpChanged, which names a unit but not a place,
        /// so a hit can only be credited to a cell by following the log forward
        /// from a known state.
        /// </summary>
        private readonly UnitPositionTracker _positions = new UnitPositionTracker();

        /// <summary>Null until the first battle is observed; sized from that map.</summary>
        public BattleHeatmap Heatmap => _heatmap;

        public void RoundStarted(int round, BattleState state)
        {
            Ensure(state);
        }

        /// <summary>
        /// Occupancy is sampled HERE, once per round, after both phases have run.
        ///
        /// Once per round per unit is the whole definition: sampling per command
        /// would weight a unit that acted three times three times as heavily, and
        /// turn the occupancy map into an action-count map.
        /// </summary>
        public void RoundEnded(int round, BattleState state)
        {
            Ensure(state);

            foreach (UnitState unit in state.Units)
            {
                if (!unit.IsAlive) continue;          // the dead stand nowhere
                if (!_heatmap.Occupancy.Add(unit.Position)) _heatmap.OutOfBoundsSamples++;
            }
        }

        public void PlayerCommand(int round, BattleState before, ICommand command, ExecuteResult result)
        {
            Ensure(before);

            // A rejected command produced no effects, so there is nothing to
            // count — which is exactly the required behaviour, arrived at by
            // reading the log rather than by a special case.
            CountDamage(before, result.Log);
        }

        public void PhaseResolved(int round, Faction phase, BattleState before, EffectLog log, BattleState after)
        {
            Ensure(before);
            CountDamage(before, log);
        }

        public void BattleFinished(BattleResult result, BattleState finalState)
        {
            Ensure(finalState);
            _heatmap.BattlesObserved++;
        }

        // ------------------------------------------------------------- internals

        /// <summary>
        /// Walks one log, crediting every DAMAGING effect to the cell its victim
        /// occupied at that moment.
        ///
        /// The trigger is HpChanged with a negative delta — the rule layer's own
        /// record that HP actually left a unit. Not the attack command, not the
        /// target count, not the attempt: a swing that was rejected never gets
        /// here, and one that landed for zero emits no HpChanged. An attack that
        /// draws a counter produces two of them, on two different units, and each
        /// is counted where its victim stood.
        /// </summary>
        private void CountDamage(BattleState reference, EffectLog log)
        {
            if (log == null || log.Count == 0) return;

            _positions.Seed(reference);

            for (int i = 0; i < log.Count; i++)
            {
                Effect e = log[i];

                // Movement first: a unit damaged after it moved must be credited
                // to where it ended up, not where the phase started.
                if (_positions.Follow(e)) continue;

                HpChanged hp = e as HpChanged;
                if (hp == null || hp.Delta >= 0) continue;   // healing is not a clash

                Coord at;
                if (!_positions.TryPosition(hp.UnitId, out at)) { _heatmap.OutOfBoundsSamples++; continue; }
                if (!_heatmap.Clash.Add(at)) _heatmap.OutOfBoundsSamples++;
            }
        }

        private void Ensure(BattleState state)
        {
            if (_heatmap == null) _heatmap = new BattleHeatmap(state.Map);
            _positions.Grow(state);
        }
    }
}
