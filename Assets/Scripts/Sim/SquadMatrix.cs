using System.Collections.Generic;
using System.Text;

namespace Ediki.Sim
{
    /// <summary>
    /// The 4x2 roster as 16 squads, and the machinery to run every one of them
    /// across every crucible map.
    ///
    /// Each character contributes one bit — A or B — so the 16 squads are the
    /// complete power set and nothing is cherry-picked. That matters: a matrix
    /// that only ran the combinations somebody expected to be good would measure
    /// the expectation, not the roster.
    ///
    /// The roster is swapped by rewriting the encounter TEXT rather than by
    /// building 64 encounter files. Sixteen near-identical files per map would
    /// drift the moment anybody edited a map, and the difference between two
    /// squads would then be "roster, plus whatever else got out of sync".
    /// </summary>
    public static class SquadMatrix
    {
        /// <summary>Slot order. Encounter files MUST list their four player
        /// combat spawns in this order — see the comment in each crucible file.</summary>
        public static readonly string[] Characters = { "Momotaro", "Genjin", "Kagemaru", "Masamori" };

        public sealed class Squad
        {
            public readonly string Id;          // T01..T16
            public readonly string[] UnitIds;   // one per slot, in Characters order
            public readonly string Label;       // e.g. "BBAA"

            public Squad(string id, string[] unitIds, string label)
            {
                Id = id;
                UnitIds = unitIds;
                Label = label;
            }

            /// <summary>Which verbs this squad can actually issue. Purely derived
            /// from the picks, so it cannot disagree with the roster.</summary>
            public string Verbs
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    if (UnitIds[0].EndsWith("_B")) sb.Append("push ");
                    if (UnitIds[1].EndsWith("_B")) sb.Append("break ");
                    if (UnitIds[2].EndsWith("_A")) sb.Append("slow ");
                    if (UnitIds[3].EndsWith("_A")) sb.Append("taunt ");
                    return sb.Length == 0 ? "(none)" : sb.ToString().TrimEnd();
                }
            }
        }

        /// <summary>
        /// All sixteen, T01..T16.
        ///
        /// Bit order is Momotaro (MSB) -> Masamori (LSB), 0 = A, 1 = B, and the
        /// index is the value plus one. That convention is chosen so that T13 is
        /// 1100 = Momotaro B, Genjin B, Kagemaru A, Masamori A — the one squad
        /// that carries all four verbs at once, and therefore the one worth having
        /// a memorable number.
        /// </summary>
        public static List<Squad> All()
        {
            List<Squad> squads = new List<Squad>(16);
            for (int value = 0; value < 16; value++)
            {
                string[] ids = new string[4];
                StringBuilder label = new StringBuilder(4);
                for (int slot = 0; slot < 4; slot++)
                {
                    // Slot 0 reads the most significant bit.
                    int bit = (value >> (3 - slot)) & 1;
                    char pick = bit == 0 ? 'A' : 'B';
                    ids[slot] = Characters[slot] + "_" + pick;
                    label.Append(pick);
                }
                squads.Add(new Squad("T" + (value + 1).ToString("00"), ids, label.ToString()));
            }
            return squads;
        }

        /// <summary>The all-verbs squad, by construction rather than by memory.</summary>
        public static Squad AllVerbs()
        {
            foreach (Squad s in All())
                if (s.Verbs.Contains("push") && s.Verbs.Contains("break")
                    && s.Verbs.Contains("slow") && s.Verbs.Contains("taunt")) return s;
            return null;
        }

        /// <summary>
        /// Rewrites the four player combat spawns to this squad's units.
        ///
        /// Protected spawns are skipped: a shrine is scenery the objective points
        /// at, not a squad slot, and swapping it would change what the map is.
        /// Throws rather than guesses when the file does not have exactly four
        /// combat slots — a matrix cell that silently ran three units would look
        /// like a bad squad instead of a bad fixture.
        /// </summary>
        public static string WithSquad(string encounterText, Squad squad)
        {
            string[] lines = encounterText.Replace("\r\n", "\n").Split('\n');
            int slot = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("spawn ")) continue;
                if (trimmed.IndexOf("faction=player", System.StringComparison.Ordinal) < 0) continue;
                if (trimmed.IndexOf("protect=true", System.StringComparison.Ordinal) >= 0) continue;

                if (slot >= squad.UnitIds.Length)
                    throw new System.InvalidOperationException(
                        "Encounter has more than " + squad.UnitIds.Length + " player combat spawns.");

                lines[i] = ReplaceUnitId(line, squad.UnitIds[slot]);
                slot++;
            }

            if (slot != squad.UnitIds.Length)
                throw new System.InvalidOperationException(
                    "Encounter has " + slot + " player combat spawns, expected " + squad.UnitIds.Length + ".");

            return string.Join("\n", lines);
        }

        private static string ReplaceUnitId(string line, string unitId)
        {
            int at = line.IndexOf("unit=", System.StringComparison.Ordinal);
            int start = at + "unit=".Length;
            int end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
            return line.Substring(0, start) + unitId + line.Substring(end);
        }

        /// <summary>
        /// One MapEntry per (map, squad). Named "map/Tnn" so every existing
        /// report, CSV column and anomaly record identifies the cell without any
        /// of them needing to know squads exist.
        /// </summary>
        public static List<MapEntry> BuildCells(IList<MapEntry> maps, IList<Squad> squads)
        {
            List<MapEntry> cells = new List<MapEntry>(maps.Count * squads.Count);
            for (int m = 0; m < maps.Count; m++)
                for (int s = 0; s < squads.Count; s++)
                    cells.Add(new MapEntry(maps[m].Name + "/" + squads[s].Id,
                                           WithSquad(maps[m].EncounterText, squads[s]),
                                           maps[m].RouteProbeRow));
            return cells;
        }

        // ------------------------------------------------------------ reporting

        /// <summary>
        /// The matrix as four tables — one metric each — squads down, maps across.
        ///
        /// Four separate grids rather than one grid of tuples because the whole
        /// point is to read a COLUMN and a ROW: a squad that wins everywhere is a
        /// different finding from a map that everything wins, and a tuple grid
        /// hides both.
        /// </summary>
        public static string Format(IList<MapEntry> maps, IList<Squad> squads,
                                    IList<IPlayerStrategy> strategies, BatchOutput output)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("=== SQUAD MATRIX — 16 squads x " + maps.Count + " maps ===");
            sb.AppendLine();
            sb.AppendLine("Squad key (slot order: Momotaro / Genjin / Kagemaru / Masamori)");
            foreach (Squad s in squads)
                sb.Append("  ").Append(s.Id).Append("  ").Append(s.Label)
                  .Append("   ").AppendLine(s.Verbs);
            sb.AppendLine();

            for (int st = 0; st < strategies.Count; st++)
            {
                string strategy = strategies[st].Name;
                sb.Append("### strategy: ").AppendLine(strategy);
                sb.AppendLine();
                Grid(sb, "WIN RATE %", maps, squads, output, strategy, c => c.WinRatePercent);
                Grid(sb, "M1 top action-composition % (>=70 = collapsed)", maps, squads, output, strategy,
                     c => c.TopCompositionPercent);
                Grid(sb, "M2 AP waste % (<15 target)", maps, squads, output, strategy, c => c.ApWastePercent);
                Grid(sb, "M4 mean exposure x100", maps, squads, output, strategy, c => c.MeanExposureX100);
                Grid(sb, "M1b unit-turns using a skill %", maps, squads, output, strategy,
                     c => c.UnitTurnsWithSkillPercent);
                Grid(sb, "unresolved (hit the round cap)", maps, squads, output, strategy, c => c.Unresolved);
            }

            return sb.ToString();
        }

        private delegate int Metric(SimulationSummary cell);

        private static void Grid(StringBuilder sb, string title, IList<MapEntry> maps,
                                 IList<Squad> squads, BatchOutput output, string strategy, Metric metric)
        {
            sb.Append("-- ").AppendLine(title);
            sb.Append("        ");
            for (int m = 0; m < maps.Count; m++) sb.Append(Pad(ShortName(maps[m].Name), 10));
            sb.AppendLine("   verbs");

            for (int s = 0; s < squads.Count; s++)
            {
                sb.Append(Pad(squads[s].Id + " " + squads[s].Label, 8));
                for (int m = 0; m < maps.Count; m++)
                {
                    SimulationSummary cell = Find(output, maps[m].Name + "/" + squads[s].Id, strategy);
                    sb.Append(Pad(cell == null ? "-" : metric(cell).ToString(), 10));
                }
                sb.Append("   ").AppendLine(squads[s].Verbs);
            }
            sb.AppendLine();
        }

        private static SimulationSummary Find(BatchOutput output, string map, string strategy)
        {
            for (int i = 0; i < output.Cells.Count; i++)
            {
                SimulationSummary c = output.Cells[i];
                if (c.Map == map && c.Strategy == strategy) return c;
            }
            return null;
        }

        /// <summary>"gym-crucible-chasm" -> "chasm". Column headers have to fit.</summary>
        private static string ShortName(string mapName)
        {
            int at = mapName.LastIndexOf('-');
            return at >= 0 && at + 1 < mapName.Length ? mapName.Substring(at + 1) : mapName;
        }

        private static string Pad(string s, int width)
        {
            if (s.Length >= width) return s.Substring(0, width - 1) + " ";
            return s + new string(' ', width - s.Length);
        }
    }
}
