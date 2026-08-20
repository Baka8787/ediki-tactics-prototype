using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ediki.Core;
using Ediki.Core.Data;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Document to text and back, in the shipped .encounter.txt format.
    ///
    /// WRITING is the load-bearing direction: whatever comes out here is what
    /// the Unity playtest and Ediki.Sim both read, and DataTests already parses
    /// every .encounter under Resources/Data, so a bad emitter fails the suite.
    /// The editor never writes a file without running EncounterLoader.Parse over
    /// the result first (see EncounterValidation.Gate).
    ///
    /// READING is deliberately lenient — a file with an unknown map symbol or a
    /// ragged row should open so it can be repaired, not refuse to open. The
    /// strict reading is still done, by the canonical loader, at save time.
    /// Nothing here re-implements the FORMAT: line splitting and key=value
    /// parsing come from DataLine, and the map block markers from
    /// EncounterLoader's own constants.
    /// </summary>
    public static class EncounterDocumentIO
    {
        public const string PlaytestEncounterName = "editor-playtest.encounter";

        // ------------------------------------------------------------- writing

        public static string ToText(EncounterDocument doc, TerrainCatalog terrain)
        {
            StringBuilder sb = new StringBuilder();

            AppendComment(sb, doc.Notes);
            if (!string.IsNullOrEmpty(doc.Notes)) sb.Append('\n');

            sb.Append("encounter id=").Append(Token(doc.Id, "encounter"))
              .Append(" name=").Append(Token(doc.DisplayName, doc.Id)).Append('\n');

            sb.Append(ObjectiveLine(doc)).Append('\n');

            // Omitted when Subtractive, exactly as the hand-written files do:
            // absent means the decided baseline (OD-05).
            if (doc.Damage == DamageModel.Percentage) sb.Append("rules damage=percent\n");

            sb.Append('\n').Append(EncounterLoader.MapBlockStart).Append('\n');
            for (int y = 0; y < doc.Height; y++)
            {
                for (int x = 0; x < doc.Width; x++)
                {
                    int idx = doc.TerrainAt(x, y);
                    char symbol = idx >= 0 && idx < terrain.Count ? terrain[idx].Symbol : '?';
                    sb.Append(symbol);
                }
                sb.Append('\n');
            }
            sb.Append(EncounterLoader.MapBlockEnd).Append('\n').Append('\n');

            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                AppendComment(sb, doc.Spawns[i].Comment);
                sb.Append(SpawnLine(doc.Spawns[i])).Append('\n');
            }

            if (!string.IsNullOrEmpty(doc.Footer))
            {
                sb.Append('\n');
                AppendComment(sb, doc.Footer);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes a stored comment block back as '#' lines.
        ///
        /// Blank lines inside a block are kept as bare '#' so the shape of a
        /// hand-formatted header survives a round trip, and a line that already
        /// begins with '#' is not given a second one.
        /// </summary>
        private static void AppendComment(StringBuilder sb, string block)
        {
            if (string.IsNullOrEmpty(block)) return;

            string[] lines = block.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) sb.Append("#\n");
                else if (line[0] == '#') sb.Append(line).Append('\n');
                else sb.Append("# ").Append(line).Append('\n');
            }
        }

        private static string ObjectiveLine(EncounterDocument doc)
        {
            StringBuilder sb = new StringBuilder("objective type=");
            switch (doc.ObjectiveKind)
            {
                case ObjectiveKind.Reach:
                    sb.Append("reach x=").Append(doc.ObjectiveTarget.X)
                      .Append(" y=").Append(doc.ObjectiveTarget.Y);
                    break;
                case ObjectiveKind.Survive: sb.Append("survive"); break;
                case ObjectiveKind.Defend: sb.Append("defend"); break;
                case ObjectiveKind.Kill: sb.Append("kill"); break;
                default: sb.Append("rout"); break;
            }
            if (doc.TurnLimit > 0) sb.Append(" turns=").Append(doc.TurnLimit);
            return sb.ToString();
        }

        private static string SpawnLine(SpawnEntry s)
        {
            StringBuilder sb = new StringBuilder("spawn faction=");
            sb.Append(s.Faction == Faction.Player ? "player" : "enemy");
            sb.Append(" unit=").Append(Token(s.UnitId, "unknown"));
            sb.Append(" x=").Append(s.Position.X).Append(" y=").Append(s.Position.Y);
            if (!string.IsNullOrEmpty(s.AiProfileId)) sb.Append(" ai=").Append(Token(s.AiProfileId, ""));
            if (!string.IsNullOrEmpty(s.Group)) sb.Append(" group=").Append(Token(s.Group, ""));
            if (s.Protect) sb.Append(" protect=true");
            if (s.IsObjectiveTarget) sb.Append(" target=true");
            if (s.ArrivesOnTurn > 0) sb.Append(" turn=").Append(s.ArrivesOnTurn);
            return sb.ToString();
        }

        /// <summary>
        /// The format splits on whitespace, so a value containing a space would
        /// silently become two malformed pairs. Squeezed to underscores rather
        /// than rejected, because this runs on every keystroke of a text field.
        /// </summary>
        public static string Token(string raw, string fallback)
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c) || c == '=' || c == '#') sb.Append('_');
                else sb.Append(c);
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }

        // ------------------------------------------------------------- reading

        public static EncounterDocument FromText(string text, TerrainCatalog terrain, List<string> warnings)
        {
            List<string> rows = new List<string>();
            List<DataLine> lines;
            try
            {
                lines = DataLine.ParseAll(text, EncounterLoader.MapBlockStart, EncounterLoader.MapBlockEnd, rows);
            }
            catch (DataFormatException ex)
            {
                warnings.Add("檔案格式有問題，只能部分讀取：" + ex.Message);
                lines = new List<DataLine>();
            }

            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);

            int fallbackTerrain = DefaultTerrainIndex(terrain);
            EncounterDocument doc = BuildMap(rows, terrain, fallbackTerrain, warnings);

            bool sawObjective = false;

            for (int i = 0; i < lines.Count; i++)
            {
                DataLine line = lines[i];
                try
                {
                    switch (line.Keyword.ToLowerInvariant())
                    {
                        case "encounter":
                            doc.Id = line.GetString("id", doc.Id);
                            doc.DisplayName = line.GetString("name", doc.Id);
                            break;

                        case "objective":
                            ReadObjective(line, doc);
                            sawObjective = true;
                            break;

                        case "rules":
                            string dmg = line.GetString("damage", "subtractive").ToLowerInvariant();
                            doc.Damage = dmg == "percent" || dmg == "percentage"
                                ? DamageModel.Percentage : DamageModel.Subtractive;
                            break;

                        case "spawn":
                            doc.Spawns.Add(ReadSpawn(line));
                            break;

                        default:
                            warnings.Add("第 " + line.LineNumber + " 行：看不懂的關鍵字 '" + line.Keyword + "'，已略過。");
                            break;
                    }
                }
                catch (DataFormatException ex)
                {
                    warnings.Add("第 " + line.LineNumber + " 行讀取失敗，已略過：" + ex.Message);
                }
            }

            if (!sawObjective) doc.ObjectiveKind = ObjectiveKind.Rout;

            AttachComments(text, doc);
            return doc;
        }

        /// <summary>
        /// Re-reads the raw text for comment lines and anchors each block to the
        /// data line that follows it.
        ///
        /// DataLine drops comments — correctly, since the rule layer has no use
        /// for them — so this is a second, much simpler pass rather than a change
        /// to the shared parser. A block in front of a spawn belongs to that
        /// spawn (which is exactly how the shipped gym maps group their squads);
        /// a block in front of anything else belongs to the header; whatever is
        /// left at the end is the footer.
        ///
        /// Without this, opening a shipped encounter and pressing save would
        /// delete every line of design rationale in it.
        /// </summary>
        private static void AttachComments(string text, EncounterDocument doc)
        {
            if (text == null) return;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            List<string> pending = new List<string>();
            List<string> header = new List<string>();
            bool inMap = false;
            bool headerClosed = false;
            int spawnIndex = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();

                if (inMap)
                {
                    if (trimmed == EncounterLoader.MapBlockEnd) inMap = false;
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    // A blank line inside a block is part of its shape; a blank
                    // line with nothing pending is just spacing.
                    if (pending.Count > 0) pending.Add("");
                    continue;
                }

                if (trimmed[0] == '#')
                {
                    pending.Add(trimmed.Length > 1 && trimmed[1] == ' '
                        ? trimmed.Substring(2) : trimmed.Substring(1));
                    continue;
                }

                if (trimmed == EncounterLoader.MapBlockStart) inMap = true;

                bool isSpawn = trimmed.StartsWith("spawn", StringComparison.OrdinalIgnoreCase);

                if (pending.Count > 0)
                {
                    Trim(pending);
                    if (isSpawn && spawnIndex < doc.Spawns.Count)
                        doc.Spawns[spawnIndex].Comment = string.Join("\n", pending.ToArray());
                    else
                        header.AddRange(pending);
                    pending.Clear();
                }

                if (isSpawn) spawnIndex++;
                headerClosed = true;
            }

            Trim(pending);
            if (pending.Count > 0)
            {
                if (headerClosed) doc.Footer = string.Join("\n", pending.ToArray());
                else header.AddRange(pending);
            }

            Trim(header);
            doc.Notes = string.Join("\n", header.ToArray());
        }

        private static void Trim(List<string> block)
        {
            while (block.Count > 0 && block[block.Count - 1].Length == 0) block.RemoveAt(block.Count - 1);
            while (block.Count > 0 && block[0].Length == 0) block.RemoveAt(0);
        }

        private static void ReadObjective(DataLine line, EncounterDocument doc)
        {
            string raw = line.GetString("type", "rout").ToLowerInvariant();
            switch (raw)
            {
                case "reach":
                    doc.ObjectiveKind = ObjectiveKind.Reach;
                    doc.ObjectiveTarget = new Coord(line.GetInt("x", 0), line.GetInt("y", 0));
                    break;
                case "survive": doc.ObjectiveKind = ObjectiveKind.Survive; break;
                case "defend": doc.ObjectiveKind = ObjectiveKind.Defend; break;
                case "kill": doc.ObjectiveKind = ObjectiveKind.Kill; break;
                default: doc.ObjectiveKind = ObjectiveKind.Rout; break;
            }
            doc.TurnLimit = line.GetInt("turns", 0);
        }

        private static SpawnEntry ReadSpawn(DataLine line)
        {
            string rawFaction = line.GetString("faction", "enemy").ToLowerInvariant();
            return new SpawnEntry
            {
                Faction = rawFaction == "player" ? Faction.Player : Faction.Enemy,
                UnitId = line.GetString("unit", ""),
                Position = new Coord(line.GetInt("x", 0), line.GetInt("y", 0)),
                AiProfileId = line.GetString("ai", null),
                Group = line.GetString("group", null),
                Protect = line.GetBool("protect", false),
                IsObjectiveTarget = line.GetBool("target", false),
                ArrivesOnTurn = line.GetInt("turn", 0)
            };
        }

        private static EncounterDocument BuildMap(List<string> rows, TerrainCatalog terrain,
                                                  int fallback, List<string> warnings)
        {
            if (rows.Count == 0)
            {
                warnings.Add("檔案裡沒有地圖區塊，已建立 10x8 的空白地圖。");
                return new EncounterDocument(10, 8, fallback);
            }

            int width = 0;
            for (int y = 0; y < rows.Count; y++) if (rows[y].Length > width) width = rows[y].Length;
            if (width == 0)
            {
                warnings.Add("地圖區塊是空的，已建立 10x8 的空白地圖。");
                return new EncounterDocument(10, 8, fallback);
            }

            EncounterDocument doc = new EncounterDocument(width, rows.Count, fallback);
            bool ragged = false;
            bool unknown = false;

            for (int y = 0; y < doc.Height; y++)
            {
                string row = rows[y];
                if (row.Length != width) ragged = true;

                for (int x = 0; x < doc.Width; x++)
                {
                    if (x >= row.Length) continue;   // stays fallback
                    TerrainDef def;
                    if (terrain.TryGetBySymbol(row[x], out def)) doc.SetTerrain(x, y, def.Index);
                    else unknown = true;
                }
            }

            if (ragged) warnings.Add("地圖每列長度不一致，短的列已用預設地形補滿。");
            if (unknown) warnings.Add("地圖裡有 terrain.txt 沒有定義的符號，那些格子已改成預設地形。");
            return doc;
        }

        /// <summary>
        /// The terrain a blank map and every repaired cell is made of: the first
        /// passable, non-lethal, cheapest entry in the catalog. Derived rather
        /// than hardcoded to "Open", so the editor follows terrain.txt (A5)
        /// instead of assuming a name that data is free to change.
        /// </summary>
        public static int DefaultTerrainIndex(TerrainCatalog terrain)
        {
            int best = 0;
            int bestCost = int.MaxValue;
            for (int i = 0; i < terrain.Count; i++)
            {
                TerrainDef d = terrain[i];
                if (d.BlocksMovement || d.IsLethal) continue;
                if (d.MovementCostHundredths >= bestCost) continue;
                bestCost = d.MovementCostHundredths;
                best = i;
            }
            return best;
        }

        /// <summary>The first blocking terrain, used as the wall the New-map border is drawn with.</summary>
        public static int WallTerrainIndex(TerrainCatalog terrain)
        {
            for (int i = 0; i < terrain.Count; i++)
                if (terrain[i].BlocksMovement) return i;
            return DefaultTerrainIndex(terrain);
        }

    }
}
