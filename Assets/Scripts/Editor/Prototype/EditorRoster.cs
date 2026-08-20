using System;
using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;

namespace Ediki.Editor.Prototype
{
    /// <summary>Which side a roster entry belongs to.</summary>
    public enum RosterSide
    {
        Player = 0,
        Enemy = 1,

        /// <summary>
        /// A prop the objective points at. Spawned on the player side with
        /// protect=true, and excluded from the party limit — the same rule
        /// SquadMatrix applies when it rewrites a squad.
        /// </summary>
        Objective = 2
    }

    public sealed class RosterVariant
    {
        public string UnitId;
        public string Label;
        public string Note;
        public RosterCharacter Owner;
    }

    public sealed class RosterCharacter
    {
        public string Id;
        public string Name;
        public RosterSide Side;
        public readonly List<RosterVariant> Variants = new List<RosterVariant>();

        public override string ToString() => Name;
    }

    /// <summary>
    /// The editor's answer to "which units belong to which side, and which ids
    /// are the same character".
    ///
    /// The project has no other source for this and deliberately so: UnitDef
    /// carries no faction, and a battle decides sides per spawn line. That is
    /// correct for the rule layer — nothing in it needs to know — but it leaves
    /// a tool with nothing to filter a palette by, which is why this file
    /// exists (decided 2026-08-18).
    ///
    /// It is METADATA ONLY. It names ids that must already exist in units.txt
    /// and holds no stats, so it cannot drift from the numbers; the one failure
    /// mode is naming an id that has been deleted, and that is reported rather
    /// than swallowed. Units it does not mention still load, still play, and
    /// still display correctly — they are simply not offered in the palette.
    /// </summary>
    public sealed class EditorRoster
    {
        public readonly List<RosterCharacter> Characters = new List<RosterCharacter>();
        public readonly List<string> Warnings = new List<string>();

        private readonly Dictionary<string, RosterVariant> _byUnitId =
            new Dictionary<string, RosterVariant>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when there is no roster at all — the editor then offers everything.</summary>
        public bool IsEmpty => Characters.Count == 0;

        public RosterVariant VariantOf(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            RosterVariant v;
            return _byUnitId.TryGetValue(unitId, out v) ? v : null;
        }

        public RosterCharacter CharacterOf(string unitId)
        {
            RosterVariant v = VariantOf(unitId);
            return v == null ? null : v.Owner;
        }

        /// <summary>Side this unit is declared for, or null when it is not in the roster.</summary>
        public RosterSide? SideOf(string unitId)
        {
            RosterCharacter c = CharacterOf(unitId);
            return c == null ? (RosterSide?)null : c.Side;
        }

        public List<RosterCharacter> ForSide(RosterSide side)
        {
            List<RosterCharacter> result = new List<RosterCharacter>();
            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Side == side) result.Add(Characters[i]);
            return result;
        }

        /// <summary>
        /// What the planner should be offered when placing on a faction: its own
        /// characters, plus the objective props on the player side (that is how
        /// a defended shrine is placed).
        /// </summary>
        public List<RosterCharacter> Palette(Faction faction)
        {
            if (faction == Faction.Enemy) return ForSide(RosterSide.Enemy);

            List<RosterCharacter> result = ForSide(RosterSide.Player);
            result.AddRange(ForSide(RosterSide.Objective));
            return result;
        }

        // ---------------------------------------------------------------- parse

        public static EditorRoster Parse(string text, UnitCatalog units)
        {
            EditorRoster roster = new EditorRoster();
            if (string.IsNullOrEmpty(text)) return roster;

            Dictionary<string, RosterCharacter> byId =
                new Dictionary<string, RosterCharacter>(StringComparer.OrdinalIgnoreCase);

            List<DataLine> lines;
            try
            {
                lines = DataLine.ParseAll(text);
            }
            catch (DataFormatException ex)
            {
                roster.Warnings.Add("editor-roster.txt 讀取失敗：" + ex.Message);
                return roster;
            }

            foreach (DataLine line in lines)
            {
                try
                {
                    switch (line.Keyword.ToLowerInvariant())
                    {
                        case "character": ReadCharacter(line, roster, byId); break;
                        case "variant": ReadVariant(line, roster, byId, units); break;
                        default:
                            roster.Warnings.Add("第 " + line.LineNumber + " 行：看不懂的關鍵字 '"
                                                + line.Keyword + "'，已略過。");
                            break;
                    }
                }
                catch (DataFormatException ex)
                {
                    roster.Warnings.Add("第 " + line.LineNumber + " 行：" + ex.Message);
                }
            }

            for (int i = roster.Characters.Count - 1; i >= 0; i--)
            {
                if (roster.Characters[i].Variants.Count > 0) continue;
                roster.Warnings.Add("角色「" + roster.Characters[i].Name + "」沒有任何可用的單位，已略過。");
                roster.Characters.RemoveAt(i);
            }

            return roster;
        }

        private static void ReadCharacter(DataLine line, EditorRoster roster,
                                          Dictionary<string, RosterCharacter> byId)
        {
            string id = line.GetString("id");
            if (byId.ContainsKey(id))
                throw new DataFormatException("角色代號 '" + id + "' 重複了。");

            string rawSide = line.GetString("faction", "enemy").ToLowerInvariant();
            RosterSide side;
            switch (rawSide)
            {
                case "player": side = RosterSide.Player; break;
                case "enemy": side = RosterSide.Enemy; break;
                case "objective": side = RosterSide.Objective; break;
                default:
                    throw new DataFormatException("不認得的 faction '" + rawSide
                        + "'（只能是 player / enemy / objective）。");
            }

            RosterCharacter character = new RosterCharacter
            {
                Id = id,
                Name = line.GetString("name", id),
                Side = side
            };

            byId.Add(id, character);
            roster.Characters.Add(character);
        }

        private static void ReadVariant(DataLine line, EditorRoster roster,
                                        Dictionary<string, RosterCharacter> byId, UnitCatalog units)
        {
            string charId = line.GetString("char");
            RosterCharacter owner;
            if (!byId.TryGetValue(charId, out owner))
                throw new DataFormatException("variant 指到還沒宣告的角色 '" + charId + "'。");

            string unitId = line.GetString("unit");

            // The one way this file can be wrong: naming a unit that no longer
            // exists. Reported and dropped rather than left to fail later as a
            // spawn the game cannot build.
            UnitDef def;
            if (units != null && !units.TryGet(unitId, out def))
            {
                roster.Warnings.Add("角色「" + owner.Name + "」的單位「" + unitId
                                    + "」在 units.txt 裡找不到，已略過。");
                return;
            }

            if (roster._byUnitId.ContainsKey(unitId))
            {
                roster.Warnings.Add("單位「" + unitId + "」被列在兩個角色底下，只保留第一個。");
                return;
            }

            RosterVariant variant = new RosterVariant
            {
                UnitId = unitId,
                Label = line.GetString("label", unitId),
                Note = line.GetString("note", null),
                Owner = owner
            };

            owner.Variants.Add(variant);
            roster._byUnitId.Add(unitId, variant);
        }
    }
}
