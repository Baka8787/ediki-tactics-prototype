using System;
using System.Collections.Generic;
using Ediki.Core;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// The thing the Prototype Editor edits.
    ///
    /// This is an AUTHORING BUFFER, not a second data model. Every field maps
    /// one-to-one onto something the shipped encounter format already carries
    /// (see EncounterDocumentIO), and the only artefact it can produce is a
    /// canonical .encounter.txt that EncounterLoader.Parse accepts. Nothing here
    /// knows a single rule — no damage, no movement, no victory logic.
    ///
    /// It exists because the runtime types are deliberately immutable
    /// (BattleMap, UnitDef, SpawnDef and ObjectiveDef all have readonly fields,
    /// and BattleMap does not even expose its terrain array), so there is nothing
    /// in Core that a painting tool could mutate cell by cell. Rather than loosen
    /// that, the editor keeps its own mutable copy and re-emits text.
    /// </summary>
    public sealed class EncounterDocument
    {
        public const int MinSize = 3;
        public const int MaxSize = 64;

        public string Id = "new-encounter";
        public string DisplayName = "New-Encounter";

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Terrain catalog index per cell, row-major. Same layout as BattleMap.</summary>
        private int[] _cells;

        public ObjectiveKind ObjectiveKind = ObjectiveKind.Rout;
        public Coord ObjectiveTarget = new Coord(0, 0);
        public int TurnLimit;

        public DamageModel Damage = DamageModel.Subtractive;

        public readonly List<SpawnEntry> Spawns = new List<SpawnEntry>();

        /// <summary>Absolute path this document was last read from / written to. Null = never saved.</summary>
        public string SourcePath;

        /// <summary>
        /// The comment block at the top of the file, verbatim and without the
        /// leading '#'.
        ///
        /// Preserved rather than regenerated because the shipped encounters carry
        /// their design rationale here — why a chasm is where it is, which spawn
        /// order SquadMatrix rewrites in place — and a tool that silently deleted
        /// that on save would destroy the only record of it.
        /// </summary>
        public string Notes = "";

        /// <summary>Comments that trailed the last data line. Written back at the end.</summary>
        public string Footer = "";

        private EncounterDocument() { }

        public EncounterDocument(int width, int height, int fillTerrainIndex)
        {
            Width = Clamp(width);
            Height = Clamp(height);
            _cells = new int[Width * Height];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = fillTerrainIndex;
        }

        private static int Clamp(int v) => v < MinSize ? MinSize : (v > MaxSize ? MaxSize : v);

        public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public bool Contains(Coord c) => Contains(c.X, c.Y);

        public int TerrainAt(int x, int y) => _cells[y * Width + x];
        public int TerrainAt(Coord c) => _cells[c.Y * Width + c.X];

        /// <summary>True when the cell actually changed, so callers can skip empty undo steps.</summary>
        public bool SetTerrain(int x, int y, int terrainIndex)
        {
            if (!Contains(x, y)) return false;
            int i = y * Width + x;
            if (_cells[i] == terrainIndex) return false;
            _cells[i] = terrainIndex;
            return true;
        }

        /// <summary>
        /// Grows or crops the grid, keeping the overlap. New cells take
        /// <paramref name="fillTerrainIndex"/>. Spawns and the objective target
        /// that fall outside are NOT silently moved — validation reports them, so
        /// a mistyped size is visible instead of quietly rearranging the map.
        /// </summary>
        public void Resize(int width, int height, int fillTerrainIndex)
        {
            int w = Clamp(width);
            int h = Clamp(height);
            if (w == Width && h == Height) return;

            int[] next = new int[w * h];
            for (int i = 0; i < next.Length; i++) next[i] = fillTerrainIndex;

            int copyW = Math.Min(w, Width);
            int copyH = Math.Min(h, Height);
            for (int y = 0; y < copyH; y++)
                for (int x = 0; x < copyW; x++)
                    next[y * w + x] = _cells[y * Width + x];

            _cells = next;
            Width = w;
            Height = h;
        }

        public SpawnEntry SpawnAt(Coord c)
        {
            for (int i = 0; i < Spawns.Count; i++)
                if (Spawns[i].Position == c) return Spawns[i];
            return null;
        }

        public int IndexOfSpawnAt(Coord c)
        {
            for (int i = 0; i < Spawns.Count; i++)
                if (Spawns[i].Position == c) return i;
            return -1;
        }

        /// <summary>
        /// P1 / E3 — the label the planner sees. Numbered per faction in spawn
        /// order, which is the order CreateBattle assigns runtime ids in, so the
        /// tag on screen matches what the battle log will call it.
        /// </summary>
        public string TagOf(SpawnEntry spawn)
        {
            int n = 0;
            for (int i = 0; i < Spawns.Count; i++)
            {
                if (Spawns[i].Faction != spawn.Faction) continue;
                n++;
                if (ReferenceEquals(Spawns[i], spawn))
                    return (spawn.Faction == Faction.Player ? "P" : "E") + n;
            }
            return spawn.Faction == Faction.Player ? "P?" : "E?";
        }

        public EncounterDocument Clone()
        {
            EncounterDocument copy = new EncounterDocument
            {
                Id = Id,
                DisplayName = DisplayName,
                Width = Width,
                Height = Height,
                _cells = (int[])_cells.Clone(),
                ObjectiveKind = ObjectiveKind,
                ObjectiveTarget = ObjectiveTarget,
                TurnLimit = TurnLimit,
                Damage = Damage,
                SourcePath = SourcePath,
                Notes = Notes,
                Footer = Footer
            };
            for (int i = 0; i < Spawns.Count; i++) copy.Spawns.Add(Spawns[i].Clone());
            return copy;
        }
    }

    /// <summary>
    /// One `spawn` line, plus the editor-only stat edits that have not been
    /// materialised into units.txt yet.
    ///
    /// The pending edits NEVER reach the saved encounter file: the format has no
    /// per-spawn stat fields and is not being given any (decided 2026-08-18).
    /// They become a real unit id through UnitVariantWriter before Save or Play,
    /// so what lands on disk is always a plain `unit=` reference that Ediki.Sim
    /// and the Unity playtest already understand.
    /// </summary>
    public sealed class SpawnEntry
    {
        public Faction Faction = Faction.Enemy;

        /// <summary>The id as it will be written. Rewritten when pending edits materialise.</summary>
        public string UnitId = "";

        public Coord Position;
        public string AiProfileId;
        public string Group;
        public bool Protect;
        public bool IsObjectiveTarget;

        /// <summary>0 = on the board at the start; N = walks in on round N.</summary>
        public int ArrivesOnTurn;

        /// <summary>Comment lines that immediately preceded this spawn in the file.</summary>
        public string Comment;

        /// <summary>Uncommitted stat edits. Null = this spawn uses the catalog unit as-is.</summary>
        public UnitStatBlock PendingStats;

        public bool HasPendingStats => PendingStats != null;
        public bool IsReinforcement => ArrivesOnTurn > 0;

        public SpawnEntry Clone()
        {
            return new SpawnEntry
            {
                Faction = Faction,
                UnitId = UnitId,
                Position = Position,
                AiProfileId = AiProfileId,
                Group = Group,
                Protect = Protect,
                IsObjectiveTarget = IsObjectiveTarget,
                ArrivesOnTurn = ArrivesOnTurn,
                Comment = Comment,
                PendingStats = PendingStats == null ? null : PendingStats.Clone()
            };
        }
    }
}
