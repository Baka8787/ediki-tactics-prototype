using System;
using System.Collections.Generic;
using Ediki.Core.Ai;

namespace Ediki.Core.Data
{
    public sealed class SpawnDef
    {
        public readonly Faction Faction;
        public readonly string UnitId;
        public readonly Coord Position;
        public readonly string AiProfileId;
        public readonly string Group;

        /// <summary>Losing this unit loses the battle (Defend objectives).</summary>
        public readonly bool Protect;

        /// <summary>Killing this unit wins the battle (Kill objectives).</summary>
        public readonly bool IsObjectiveTarget;

        /// <summary>0 = present from the start; otherwise the round it walks in on.</summary>
        public readonly int ArrivesOnTurn;

        public SpawnDef(Faction faction, string unitId, Coord position, string aiProfileId, string group,
                        bool protect, int arrivesOnTurn, bool isObjectiveTarget = false)
        {
            Faction = faction;
            UnitId = unitId;
            Position = position;
            AiProfileId = aiProfileId;
            Group = group;
            Protect = protect;
            ArrivesOnTurn = arrivesOnTurn;
            IsObjectiveTarget = isObjectiveTarget;
        }

        public bool IsReinforcement => ArrivesOnTurn > 0;
    }

    /// <summary>
    /// A loaded encounter: map + spawns. Everything a designer configures for one
    /// battle lives in a single text file (OD-11).
    /// </summary>
    public sealed class EncounterDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly BattleMap Map;
        public readonly IReadOnlyList<SpawnDef> Spawns;
        public readonly ObjectiveDef Objective;

        /// <summary>Rule variants this encounter runs under. Never null.</summary>
        public readonly RuleSet Rules;

        public EncounterDef(string id, string displayName, BattleMap map, IList<SpawnDef> spawns,
                            ObjectiveDef objective, RuleSet rules = null)
        {
            Id = id;
            DisplayName = displayName;
            Map = map;
            Spawns = new List<SpawnDef>(spawns);
            Objective = objective ?? ObjectiveDef.Rout;
            Rules = rules ?? RuleSet.Default;
        }
    }

    /// <summary>Everything the runtime needs to start a battle, built from data only.</summary>
    public sealed class BattleSetup
    {
        public readonly BattleState State;
        public readonly EnemyAi Ai;
        public readonly EncounterDef Encounter;

        public BattleSetup(BattleState state, EnemyAi ai, EncounterDef encounter)
        {
            State = state;
            Ai = ai;
            Encounter = encounter;
        }
    }

    public static class EncounterLoader
    {
        public const string MapBlockStart = "map";
        public const string MapBlockEnd = "endmap";

        public static EncounterDef Parse(string text, TerrainCatalog terrain)
        {
            List<string> mapRows = new List<string>();
            List<DataLine> lines = DataLine.ParseAll(text, MapBlockStart, MapBlockEnd, mapRows);

            string id = "encounter";
            string displayName = null;
            List<SpawnDef> spawns = new List<SpawnDef>();
            ObjectiveDef objective = null;
            RuleSet rules = null;

            foreach (DataLine line in lines)
            {
                switch (line.Keyword.ToLowerInvariant())
                {
                    case "encounter":
                        id = line.GetString("id", id);
                        displayName = line.GetString("name", displayName);
                        break;

                    case "objective":
                        if (objective != null)
                            throw new DataFormatException("Line " + line.LineNumber + ": encounter already has an objective.");
                        objective = ParseObjective(line);
                        break;

                    case "rules":
                        if (rules != null)
                            throw new DataFormatException("Line " + line.LineNumber + ": encounter already has a rules line.");
                        rules = ParseRules(line);
                        break;

                    case "spawn":
                        spawns.Add(ParseSpawn(line));
                        break;

                    default:
                        throw new DataFormatException("Line " + line.LineNumber +
                            ": unexpected keyword '" + line.Keyword + "' in encounter data.");
                }
            }

            objective = objective ?? ObjectiveDef.Rout;

            BattleMap map = BuildMap(mapRows, terrain);
            ValidateSpawns(map, spawns);
            ValidateObjective(map, spawns, objective);

            return new EncounterDef(id, displayName ?? id, map, spawns, objective, rules);
        }

        public static BattleSetup CreateBattle(EncounterDef encounter, UnitCatalog units, AiProfileCatalog aiProfiles)
        {
            List<UnitState> unitStates = new List<UnitState>();
            List<PendingReinforcement> waves = new List<PendingReinforcement>();
            Dictionary<int, AiProfile> aiByUnitId = new Dictionary<int, AiProfile>();

            int nextId = 1;
            for (int i = 0; i < encounter.Spawns.Count; i++)
            {
                SpawnDef spawn = encounter.Spawns[i];
                UnitDef def = units.Get(spawn.UnitId);

                if (spawn.IsReinforcement)
                {
                    waves.Add(new PendingReinforcement(spawn.ArrivesOnTurn, spawn.Faction, def,
                                                      spawn.Position, spawn.AiProfileId));
                    continue;
                }

                int unitId = nextId++;   // stable: spawn order in the file
                unitStates.Add(new UnitState(unitId, def, spawn.Faction, spawn.Position, spawn.Protect,
                                             spawn.IsObjectiveTarget));

                if (spawn.Faction == Faction.Enemy)
                    aiByUnitId[unitId] = aiProfiles.GetOrDefault(spawn.AiProfileId);
            }

            BattleState state = new BattleState(encounter.Map, unitStates, encounter.Objective, waves,
                                                encounter.Rules);

            // Reinforcements get ids only when they arrive, so the AI resolves their
            // profile lazily from the wave list.
            EnemyAi ai = new EnemyAi(aiByUnitId, null, waves, aiProfiles);
            return new BattleSetup(state, ai, encounter);
        }

        // ------------------------------------------------------------ internals

        private static SpawnDef ParseSpawn(DataLine line)
        {
            string rawFaction = line.GetString("faction");
            Faction faction;
            switch (rawFaction.ToLowerInvariant())
            {
                case "player": faction = Faction.Player; break;
                case "enemy": faction = Faction.Enemy; break;
                default:
                    throw new DataFormatException("Line " + line.LineNumber +
                        ": unknown faction '" + rawFaction + "' (expected player / enemy).");
            }

            bool protect = line.GetBool("protect", false);
            if (protect && faction != Faction.Player)
                throw new DataFormatException("Line " + line.LineNumber + ": only player spawns can be protected.");

            bool target = line.GetBool("target", false);
            if (target && faction != Faction.Enemy)
                throw new DataFormatException("Line " + line.LineNumber + ": only enemy spawns can be kill targets.");

            int arrives = line.GetInt("turn", 0);
            if (arrives < 0)
                throw new DataFormatException("Line " + line.LineNumber + ": 'turn' must be 0 (start) or a later round.");
            if (arrives > 0 && protect)
                throw new DataFormatException("Line " + line.LineNumber + ": a protected unit cannot arrive as a reinforcement.");

            // A reinforcement gets its id when it lands, so the mark could not be
            // carried across. Rejecting it beats a target that silently is not one.
            if (arrives > 0 && target)
                throw new DataFormatException("Line " + line.LineNumber + ": a kill target cannot arrive as a reinforcement.");

            return new SpawnDef(
                faction,
                line.GetString("unit"),
                new Coord(line.GetInt("x"), line.GetInt("y")),
                line.GetString("ai", null),
                line.GetString("group", null),
                protect,
                arrives,
                target);
        }

        /// <summary>
        /// rules damage=subtractive | rules damage=percent
        ///
        /// Absent means the decided baseline (OD-05). An encounter that wants a
        /// different damage model says so in its own file, so the variant never
        /// leaks into any battle that did not ask for it.
        /// </summary>
        private static RuleSet ParseRules(DataLine line)
        {
            string raw = line.GetString("damage", "subtractive");
            switch (raw.ToLowerInvariant())
            {
                case "subtractive": return new RuleSet(DamageModel.Subtractive);
                case "percent":
                case "percentage": return new RuleSet(DamageModel.Percentage);
                default:
                    throw new DataFormatException("Line " + line.LineNumber +
                        ": unknown damage model '" + raw + "' (expected subtractive / percent).");
            }
        }

        /// <summary>
        /// objective type=rout
        /// objective type=reach x=5 y=1 turns=8
        /// objective type=survive turns=6
        /// objective type=defend turns=8
        /// objective type=kill                (the enemy spawn marked target=true)
        /// </summary>
        private static ObjectiveDef ParseObjective(DataLine line)
        {
            string raw = line.GetString("type");
            int turns = line.GetInt("turns", 0);
            if (turns < 0)
                throw new DataFormatException("Line " + line.LineNumber + ": 'turns' cannot be negative.");

            switch (raw.ToLowerInvariant())
            {
                case "rout":
                    return new ObjectiveDef(ObjectiveKind.Rout, default, turns);

                case "reach":
                    return new ObjectiveDef(ObjectiveKind.Reach,
                                            new Coord(line.GetInt("x"), line.GetInt("y")), turns);

                case "survive":
                    if (turns <= 0)
                        throw new DataFormatException("Line " + line.LineNumber + ": 'survive' needs a positive 'turns'.");
                    return new ObjectiveDef(ObjectiveKind.Survive, default, turns);

                case "defend":
                    if (turns <= 0)
                        throw new DataFormatException("Line " + line.LineNumber + ": 'defend' needs a positive 'turns'.");
                    return new ObjectiveDef(ObjectiveKind.Defend, default, turns);

                case "kill":
                    // The clock is optional, exactly as for Rout: the target is
                    // already a reason to move, so a limit would be a second
                    // variable rather than the thing being measured.
                    return new ObjectiveDef(ObjectiveKind.Kill, default, turns);

                default:
                    throw new DataFormatException("Line " + line.LineNumber +
                        ": unknown objective type '" + raw + "' (expected rout / reach / survive / defend / kill).");
            }
        }

        private static BattleMap BuildMap(List<string> rows, TerrainCatalog terrain)
        {
            // Trailing blank lines inside the block are harmless; drop them.
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);

            if (rows.Count == 0)
                throw new DataFormatException("Encounter has no map block (expected '" + MapBlockStart + "' ... '" + MapBlockEnd + "').");

            int height = rows.Count;
            int width = rows[0].Length;

            int[] cells = new int[width * height];

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                if (row.Length != width)
                    throw new DataFormatException("Map row " + y + " has " + row.Length +
                        " cells but row 0 has " + width + ". All rows must be the same width.");

                for (int x = 0; x < width; x++)
                {
                    TerrainDef def;
                    if (!terrain.TryGetBySymbol(row[x], out def))
                        throw new DataFormatException("Map row " + y + " column " + x +
                            ": symbol '" + row[x] + "' is not declared in terrain data.");
                    cells[y * width + x] = def.Index;
                }
            }

            return new BattleMap(width, height, terrain, cells);
        }

        private static void ValidateSpawns(BattleMap map, List<SpawnDef> spawns)
        {
            if (spawns.Count == 0) throw new DataFormatException("Encounter has no spawn lines.");

            HashSet<Coord> usedAtStart = new HashSet<Coord>();
            bool hasCombatant = false;
            bool hasEnemy = false;

            for (int i = 0; i < spawns.Count; i++)
            {
                SpawnDef s = spawns[i];

                if (!map.Contains(s.Position))
                    throw new DataFormatException("Spawn '" + s.UnitId + "' at " + s.Position + " is outside the map.");

                if (!map.IsPassable(s.Position))
                    throw new DataFormatException("Spawn '" + s.UnitId + "' at " + s.Position + " is on blocking terrain.");

                // Reinforcements may share a cell with someone who has moved on by then.
                if (!s.IsReinforcement && !usedAtStart.Add(s.Position))
                    throw new DataFormatException("Two units spawn on the same cell " + s.Position + ".");

                if (s.Faction == Faction.Player && !s.Protect) hasCombatant = true;
                if (s.Faction == Faction.Enemy) hasEnemy = true;
            }

            if (!hasCombatant)
                throw new DataFormatException("Encounter has no player unit that can fight " +
                                              "(a protected unit alone would lose on turn 1).");
            if (!hasEnemy) throw new DataFormatException("Encounter has no enemy spawn.");

            ValidateConnectivity(map, spawns);
        }

        private static void ValidateObjective(BattleMap map, List<SpawnDef> spawns, ObjectiveDef objective)
        {
            if (objective.Kind == ObjectiveKind.Reach)
            {
                if (!map.Contains(objective.Target))
                    throw new DataFormatException("Reach objective target " + objective.Target + " is outside the map.");
                if (!map.IsPassable(objective.Target))
                    throw new DataFormatException("Reach objective target " + objective.Target + " is blocking terrain.");
            }

            if (objective.Kind == ObjectiveKind.Defend)
            {
                bool hasProtected = false;
                for (int i = 0; i < spawns.Count && !hasProtected; i++)
                    hasProtected = spawns[i].Protect;

                if (!hasProtected)
                    throw new DataFormatException("A 'defend' objective needs at least one spawn with protect=true.");
            }

            bool marked = false;
            for (int i = 0; i < spawns.Count && !marked; i++)
                marked = spawns[i].IsObjectiveTarget;

            if (objective.Kind == ObjectiveKind.Kill && !marked)
                throw new DataFormatException("A 'kill' objective needs exactly one enemy spawn with target=true.");

            // A mark under any other objective would do nothing except perturb the
            // state hash, which is the kind of silent data bug the golden constants
            // exist to catch and should never have to.
            if (objective.Kind != ObjectiveKind.Kill && marked)
                throw new DataFormatException("A spawn is marked target=true but the objective is '" +
                                              objective.Kind + "', which never reads it.");

            // Survive and Defend are the two that cannot resolve at all without a
            // clock — they have no other terminating condition. Rout, Reach and
            // Kill each end on an event, so their limits are optional.
            if ((objective.Kind == ObjectiveKind.Survive || objective.Kind == ObjectiveKind.Defend)
                && !objective.HasTurnLimit)
            {
                throw new DataFormatException("Objective '" + objective.Kind + "' requires a turn limit.");
            }
        }

        /// <summary>
        /// R-MAP-02: every enemy must be reachable from the player, or the
        /// "defeat all enemies" win condition is unachievable. Terrain only —
        /// units move, walls do not.
        /// </summary>
        private static void ValidateConnectivity(BattleMap map, List<SpawnDef> spawns)
        {
            Coord start = default;
            bool found = false;
            for (int i = 0; i < spawns.Count && !found; i++)
            {
                if (spawns[i].Faction != Faction.Player) continue;
                start = spawns[i].Position;
                found = true;
            }
            if (!found) return;

            HashSet<Coord> seen = new HashSet<Coord> { start };
            Queue<Coord> queue = new Queue<Coord>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Coord cur = queue.Dequeue();
                foreach (Coord n in map.Topology.Neighbors(cur))
                {
                    if (!map.IsPassable(n)) continue;
                    if (!seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }

            for (int i = 0; i < spawns.Count; i++)
            {
                if (spawns[i].Faction != Faction.Enemy) continue;
                if (!seen.Contains(spawns[i].Position))
                    throw new DataFormatException("Enemy '" + spawns[i].UnitId + "' at " + spawns[i].Position +
                        " is unreachable from the player spawn — the victory condition could never be met.");
            }
        }
    }
}
