using System;
using System.Collections.Generic;
using Ediki.Core.Ai;

namespace Ediki.Core.Data
{
    public static class TerrainLoader
    {
        /// <summary>terrain name=Road symbol=. cost=1 blocks=false</summary>
        public static TerrainCatalog Parse(string text)
        {
            List<TerrainDef> defs = new List<TerrainDef>();
            foreach (DataLine line in DataLine.ParseAll(text))
            {
                if (!string.Equals(line.Keyword, "terrain", StringComparison.OrdinalIgnoreCase))
                    throw new DataFormatException("Line " + line.LineNumber + ": unexpected keyword '" + line.Keyword + "' in terrain data.");

                bool blocks = line.GetBool("blocks", false);
                int cost = ParseCostHundredths(line, blocks);
                if (!blocks && cost < 1)
                    throw new DataFormatException("Line " + line.LineNumber + ": passable terrain must cost more than 0 AP.");

                bool lethal = line.GetBool("lethal", false);
                if (lethal && blocks)
                    throw new DataFormatException("Line " + line.LineNumber +
                        ": terrain cannot be both blocking and lethal — nothing can ever enter it, so the hazard is unreachable.");

                defs.Add(new TerrainDef(defs.Count, line.GetString("name"), line.GetChar("symbol"), cost, blocks, lethal));
            }

            if (defs.Count == 0) throw new DataFormatException("Terrain data contains no 'terrain' lines.");
            return new TerrainCatalog(defs);
        }

        /// <summary>
        /// `cost=1` and `cost=1.5` both land as hundredths (100 and 150).
        ///
        /// Parsed by hand rather than through float: the rule layer is integer
        /// only (determinism rule 1), and a float that round-trips differently on
        /// two machines would desynchronise pathfinding without ever looking wrong.
        /// Two decimal places is the limit; more would be silently truncated, so
        /// it is rejected instead.
        /// </summary>
        private static int ParseCostHundredths(DataLine line, bool blocks)
        {
            string raw = line.GetString("cost", null);
            if (raw == null) return blocks ? 0 : 100;

            int dot = raw.IndexOf('.');
            if (dot < 0) return line.GetInt("cost", blocks ? 0 : 1) * 100;

            string whole = raw.Substring(0, dot);
            string fraction = raw.Substring(dot + 1);

            if (fraction.Length > 2)
                throw new DataFormatException("Line " + line.LineNumber +
                    ": terrain cost '" + raw + "' has more than two decimal places.");

            int units;
            if (whole.Length == 0) units = 0;
            else if (!int.TryParse(whole, out units))
                throw new DataFormatException("Line " + line.LineNumber + ": bad terrain cost '" + raw + "'.");

            int hundredths;
            string padded = fraction.PadRight(2, '0');
            if (!int.TryParse(padded, out hundredths))
                throw new DataFormatException("Line " + line.LineNumber + ": bad terrain cost '" + raw + "'.");

            return units * 100 + hundredths;
        }
    }

    public static class UnitLoader
    {
        /// <summary>unit id=momotaro name=Momotaro hp=300 atk=60 def=50 move=4 ap=8 range=1 attackCost=4 guardCost=3</summary>
        public static UnitCatalog Parse(string text)
        {
            List<UnitDef> defs = new List<UnitDef>();
            foreach (DataLine line in DataLine.ParseAll(text))
            {
                if (!string.Equals(line.Keyword, "unit", StringComparison.OrdinalIgnoreCase))
                    throw new DataFormatException("Line " + line.LineNumber + ": unexpected keyword '" + line.Keyword + "' in unit data.");

                string id = line.GetString("id");
                defs.Add(new UnitDef(
                    id,
                    line.GetString("name", id),
                    line.GetInt("hp"),
                    line.GetInt("atk"),
                    line.GetInt("def"),
                    line.GetInt("move"),
                    line.GetInt("ap"),
                    line.GetInt("apRegen", 0),          // 0 = refill to the cap every turn
                    line.GetInt("range", 1),
                    line.GetInt("attackCost"),
                    line.GetInt("guardCost"),
                    line.GetInt("counterCost", 0),
                    line.GetInt("restCost", 0),
                    line.GetInt("restHealPercent", 0),
                    line.GetInt("atkGrowth", 0),
                    line.GetInt("purifyCost", 0),
                    line.GetInt("purifyRadius", 2),
                    line.GetInt("contaminates", 0),
                    line.GetInt("contaminateRadius", 1),
                    line.GetInt("tauntCost", 0),
                    line.GetInt("tauntRadius", 2),
                    line.GetInt("slowCost", 0),
                    line.GetInt("slowRange", 1),
                    line.GetInt("pushCost", 0),
                    line.GetInt("pushRange", 1),
                    line.GetBool("immuneToPush", false),
                    line.GetInt("armorBreakCost", 0),
                    line.GetInt("armorBreakRange", 1),
                    line.GetInt("armorBreakAmount", 0),
                    line.GetInt("attacksPerRound", 0),      // 0 = uncapped
                    line.GetInt("skillUsesPerRound", 0)));  // 0 = uncapped
            }

            if (defs.Count == 0) throw new DataFormatException("Unit data contains no 'unit' lines.");
            return new UnitCatalog(defs);
        }
    }

    public static class AiProfileLoader
    {
        /// <summary>
        /// aiprofile id=aggressive target=nearest distance=1 aggression=80 retreatHp=0 guardHp=0
        /// </summary>
        public static AiProfileCatalog Parse(string text)
        {
            List<AiProfile> profiles = new List<AiProfile>();
            foreach (DataLine line in DataLine.ParseAll(text))
            {
                if (!string.Equals(line.Keyword, "aiprofile", StringComparison.OrdinalIgnoreCase))
                    throw new DataFormatException("Line " + line.LineNumber + ": unexpected keyword '" + line.Keyword + "' in ai profile data.");

                string rawTarget = line.GetString("target", "nearest");
                TargetPreference pref;
                switch (rawTarget.ToLowerInvariant())
                {
                    case "nearest": pref = TargetPreference.Nearest; break;
                    case "lowesthp": pref = TargetPreference.LowestHp; break;
                    case "lowestdef": pref = TargetPreference.LowestDefence; break;
                    default:
                        throw new DataFormatException("Line " + line.LineNumber +
                            ": unknown target preference '" + rawTarget + "' (expected nearest / lowestHp / lowestDef).");
                }

                profiles.Add(new AiProfile(
                    line.GetString("id"),
                    pref,
                    line.GetInt("distance", 1),
                    line.GetInt("aggression", 70),
                    line.GetInt("retreatHp", 0),
                    line.GetInt("guardHp", 0)));
            }

            if (profiles.Count == 0) throw new DataFormatException("AI profile data contains no 'aiprofile' lines.");
            return new AiProfileCatalog(profiles);
        }
    }
}
