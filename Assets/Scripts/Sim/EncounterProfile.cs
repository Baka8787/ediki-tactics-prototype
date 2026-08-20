using System.Collections.Generic;
using System.Text;
using Ediki.Core;
using Ediki.Core.Data;

namespace Ediki.Sim
{
    /// <summary>
    /// DERIVED metrics: everything about an encounter that can be computed from
    /// the data alone, before a single battle runs.
    ///
    /// M1-M7 are OUTCOME metrics — they depend on the strategy, so they cost a
    /// batch to obtain and they cannot be set, only measured. The numbers here
    /// are algebra over units.txt and the map, and they are the ones that decide
    /// whether the outcome metrics will have anything to say:
    ///
    ///   Lethal exposure  how many attackers at once is death — if that is 2,
    ///                    every other question is downstream of one mistake
    ///   Release          who can reach the player at all on round 1 — if that
    ///                    is everyone, the encounter has no spatial layer and
    ///                    degenerates into an ordering problem
    ///   Divisibility     EAtK mod APT — with no remainder, focus-firing one
    ///                    target at a time is the only sensible play
    ///   TPA              threat removed per action; the ordering the player
    ///                    would derive if the encounter really were just sorting
    ///
    /// Reading order matters: a later line cannot rescue an earlier one.
    ///
    /// Sources: docs/NOTE-2026-08-14-戰棋數值設計-Metrics-Framework-研究報告.md §14.1
    /// and docs/NOTE-2026-08-14-敵人刀數與目標優先級數值模型.md §1.
    /// Both are research notes, not specifications — this class computes what
    /// they define and decides nothing.
    /// </summary>
    public static class EncounterProfile
    {
        /// <summary>One enemy type's derived numbers, against one player unit.</summary>
        public sealed class EnemyRow
        {
            public string EnemyId;
            public int Count;

            /// <summary>Damage the player deals it per hit.</summary>
            public int PlayerDamage;

            /// <summary>Hits needed to kill it: ceil(HP / damage).</summary>
            public int RequiredHits;

            /// <summary>
            /// AP of path between the player spawn and firing range of the nearest
            /// one.
            ///
            /// Deliberately NOT folded into the kill cost. Approach is either a
            /// cost the player pays (it belongs in the denominator) or a delay
            /// before the enemy arrives (it is a release time) — never both. Under
            /// OD-16 every enemy closes in unasked, so on this project it is a
            /// release time, and M7 measures it properly during the battle. This
            /// column is here as the static half of that picture.
            /// </summary>
            public int ApproachAp;

            /// <summary>Damage it deals the player in one of its turns.</summary>
            public int DamagePerTurn;

            /// <summary>DamagePerTurn / RequiredHits, x100. Approach excluded, see ApproachAp.</summary>
            public int ThreatPerActionX100;

            /// <summary>RequiredHits mod attacks-per-turn. Zero means no leftover action ever exists.</summary>
            public int Residue;

            /// <summary>
            /// ATK gained per round. Non-zero means every other number in this
            /// row describes only round 1 — see the warning Describe prints.
            /// </summary>
            public int AtkGrowth;
        }

        public sealed class Profile
        {
            public string PlayerId;
            public int PlayerHp;

            /// <summary>Attacks the player can afford per turn: regen / attack cost.</summary>
            public int AttacksPerTurn;

            /// <summary>How many of the deadliest enemy, attacking at once, kill in one round.</summary>
            public int LethalExposure;

            public readonly List<EnemyRow> Enemies = new List<EnemyRow>();
        }

        /// <summary>
        /// Builds one profile per player combatant. Objective props are skipped —
        /// they never act, so none of these numbers mean anything for them.
        /// </summary>
        public static List<Profile> Build(EncounterDef encounter, UnitCatalog units)
        {
            List<Profile> profiles = new List<Profile>();

            // Group the enemy spawns by unit id; identical enemies share every
            // derived number except approach, which uses the nearest one.
            List<string> enemyIds = new List<string>();
            Dictionary<string, int> enemyCounts = new Dictionary<string, int>();
            Dictionary<string, List<Coord>> enemyPositions = new Dictionary<string, List<Coord>>();

            for (int i = 0; i < encounter.Spawns.Count; i++)
            {
                SpawnDef spawn = encounter.Spawns[i];
                if (spawn.Faction != Faction.Enemy) continue;

                int n;
                if (!enemyCounts.TryGetValue(spawn.UnitId, out n))
                {
                    enemyIds.Add(spawn.UnitId);
                    enemyPositions[spawn.UnitId] = new List<Coord>();
                    n = 0;
                }
                enemyCounts[spawn.UnitId] = n + 1;
                enemyPositions[spawn.UnitId].Add(spawn.Position);
            }

            for (int i = 0; i < encounter.Spawns.Count; i++)
            {
                SpawnDef spawn = encounter.Spawns[i];
                if (spawn.Faction != Faction.Player || spawn.Protect) continue;

                UnitDef player = units.Get(spawn.UnitId);
                Profile profile = new Profile
                {
                    PlayerId = player.Id,
                    PlayerHp = player.MaxHp,
                    AttacksPerTurn = player.AttackApCost <= 0
                        ? 0
                        : player.ApRegen / player.AttackApCost
                };

                Dictionary<Coord, int> field =
                    MovementCalculator.TerrainDistanceField(encounter.Map, spawn.Position);

                int worstDamagePerTurn = 0;

                for (int e = 0; e < enemyIds.Count; e++)
                {
                    UnitDef enemy = units.Get(enemyIds[e]);
                    EnemyRow row = new EnemyRow
                    {
                        EnemyId = enemy.Id,
                        Count = enemyCounts[enemy.Id],

                        // The encounter's own damage model, not the default: an
                        // eight-hit wall under subtraction can be a four-hit wall
                        // under percentage, and a profile that ignored that would
                        // be describing a different battle from the one that runs.
                        PlayerDamage = BattleRules.ComputeDamage(player.Atk, enemy.Def, false,
                                                                 encounter.Rules.Damage)
                    };

                    row.RequiredHits = CeilDiv(enemy.MaxHp, row.PlayerDamage);
                    row.ApproachAp = ApproachCost(encounter.Map, field, enemyPositions[enemy.Id],
                                                  player.AttackRange);

                    int perHit = BattleRules.ComputeDamage(enemy.Atk, player.Def, false,
                                                           encounter.Rules.Damage);
                    int enemyAttacksPerTurn = enemy.AttackApCost <= 0
                        ? 0 : enemy.ApRegen / enemy.AttackApCost;
                    row.DamagePerTurn = perHit * enemyAttacksPerTurn;

                    row.AtkGrowth = enemy.AtkGrowth;
                    row.ThreatPerActionX100 = row.RequiredHits == 0
                        ? 0 : row.DamagePerTurn * 100 / row.RequiredHits;
                    row.Residue = profile.AttacksPerTurn == 0
                        ? 0 : row.RequiredHits % profile.AttacksPerTurn;

                    if (row.DamagePerTurn > worstDamagePerTurn) worstDamagePerTurn = row.DamagePerTurn;

                    profile.Enemies.Add(row);
                }

                profile.LethalExposure = worstDamagePerTurn == 0
                    ? 0 : CeilDiv(player.MaxHp, worstDamagePerTurn);

                profiles.Add(profile);
            }

            return profiles;
        }

        public static string Describe(EncounterDef encounter, UnitCatalog units)
        {
            List<Profile> profiles = Build(encounter, units);
            StringBuilder sb = new StringBuilder();

            for (int p = 0; p < profiles.Count; p++)
            {
                Profile profile = profiles[p];
                sb.Append("  profile: ").Append(profile.PlayerId)
                  .Append("   HP ").Append(profile.PlayerHp)
                  .Append("   attacks/turn ").Append(profile.AttacksPerTurn)
                  .Append("   lethal exposure ").Append(profile.LethalExposure)
                  .AppendLine(profile.LethalExposure <= 2
                      ? "  <- two attackers is death; one mistake ends the battle"
                      : "");

                sb.AppendLine("    enemy             n   hits  dmg/turn    TPA  residue   approach AP");
                for (int e = 0; e < profile.Enemies.Count; e++)
                {
                    EnemyRow row = profile.Enemies[e];
                    sb.Append("    ").Append(row.EnemyId.PadRight(16))
                      .Append(row.Count.ToString().PadLeft(3))
                      .Append(row.RequiredHits.ToString().PadLeft(6))
                      .Append(row.DamagePerTurn.ToString().PadLeft(10))
                      .Append((row.ThreatPerActionX100 / 100).ToString().PadLeft(7))
                      .Append(row.Residue.ToString().PadLeft(9))
                      .Append(row.ApproachAp.ToString().PadLeft(14))
                      .AppendLine(row.AtkGrowth != 0 ? "   +" + row.AtkGrowth + " ATK/round" : "");
                }

                // A growth type breaks every number above, because they are all
                // round-1 snapshots and it is the only unit whose threat is a
                // function of time. It reads as the SAFEST target here and is
                // usually the one that decides the battle.
                if (HasGrowth(profile))
                    sb.AppendLine("    ** a growth unit is present: the columns above are round 1 only, " +
                                  "and TPA cannot see time");

                sb.AppendLine(AllDivisible(profile)
                    ? "    no target leaves a spare action — focus fire is the only play"
                    : "    at least one target leaves a spare action — what to spend it on is a choice");
                sb.AppendLine("    TPA excludes approach on purpose: under OD-16 the enemy closes in,");
                sb.AppendLine("    so approach is a release time, not a cost. M7 measures it for real.");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static bool HasGrowth(Profile profile)
        {
            for (int i = 0; i < profile.Enemies.Count; i++)
                if (profile.Enemies[i].AtkGrowth != 0) return true;
            return false;
        }

        private static bool AllDivisible(Profile profile)
        {
            for (int i = 0; i < profile.Enemies.Count; i++)
                if (profile.Enemies[i].Residue != 0) return false;
            return true;
        }

        /// <summary>
        /// AP the player must spend to stand somewhere it can hit the nearest one
        /// of this enemy type. Terrain cost, not straight-line distance — walls
        /// are exactly what this number is supposed to notice.
        /// </summary>
        private static int ApproachCost(BattleMap map, Dictionary<Coord, int> field,
                                        List<Coord> targets, int attackRange)
        {
            int best = int.MaxValue;

            foreach (KeyValuePair<Coord, int> cell in field)
            {
                for (int t = 0; t < targets.Count; t++)
                {
                    if (map.Topology.Distance(cell.Key, targets[t]) > attackRange) continue;
                    if (cell.Value < best) best = cell.Value;
                    break;
                }
            }

            return best == int.MaxValue ? 0 : best;
        }

        private static int CeilDiv(int a, int b) => b <= 0 ? 0 : (a + b - 1) / b;
    }
}
