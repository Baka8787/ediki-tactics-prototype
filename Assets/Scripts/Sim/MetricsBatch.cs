using System.Collections.Generic;
using System.Text;

namespace Ediki.Sim
{
    public sealed class MapEntry
    {
        public readonly string Name;
        public readonly string EncounterText;

        /// <summary>
        /// M6: the row this map's dividing wall sits on. -1 = the map has no wall
        /// to cross, so it has no routes to count.
        /// </summary>
        public readonly int RouteProbeRow;

        public MapEntry(string name, string encounterText, int routeProbeRow = -1)
        {
            Name = name;
            EncounterText = encounterText;
            RouteProbeRow = routeProbeRow;
        }
    }

    public sealed class BatchOutput
    {
        public string Summary;
        public string RawCsv;
        public readonly List<SimulationSummary> Cells = new List<SimulationSummary>();
        public readonly List<BattleResult> AllResults = new List<BattleResult>();

        /// <summary>
        /// Battles worth a human's attention, already sorted. Diagnostic output:
        /// it is derived from AllResults and changes nothing about the batch.
        /// </summary>
        public readonly List<BattleAnomaly> Anomalies = new List<BattleAnomaly>();

        /// <summary>
        /// Strategy personas and the pairwise screening result. Derived from
        /// Cells after every battle has run; it reruns nothing.
        /// </summary>
        public StrategyAnalysis.Report Analysis;
    }

    /// <summary>
    /// Runs the whole matrix: every map x every strategy x N seeds, and writes
    /// both a human summary and a raw per-battle CSV.
    ///
    /// Changes no gameplay rules. If a run reveals a rule problem, record it as a
    /// prototype finding — do not patch the rules from here.
    /// </summary>
    public static class MetricsBatch
    {
        /// <param name="strategiesExpectedSkillFree">
        /// Strategy names the CALLER declares should never issue a skill. Null —
        /// the default — means nobody declared anything and UNEXPECTED_SKILL_USAGE
        /// is never raised. It is a parameter rather than something read off the
        /// strategy because IPlayerStrategy carries no such metadata, and deriving
        /// it from a name would be a guess dressed as a rule.
        /// </param>
        public static BatchOutput Run(SimulationRunner runner,
                                      IList<MapEntry> maps,
                                      IList<IPlayerStrategy> strategies,
                                      int runsPerCell,
                                      int baseSeed,
                                      int noisePercent,
                                      int maxRounds,
                                      IList<string> strategiesExpectedSkillFree = null,
                                      AnomalyThresholds anomalyThresholds = null,
                                      AnalysisOptions analysisOptions = null)
        {
            BatchOutput output = new BatchOutput();

            // The body is built separately from the head because the strategy
            // alerts belong at the very top and are not known until every cell
            // has run.
            StringBuilder head = new StringBuilder();
            StringBuilder summary = new StringBuilder();

            head.AppendLine("=== Ediki Stage 01 Prototype — Metrics ===");
            head.Append("runs per cell : ").AppendLine(runsPerCell.ToString());
            head.Append("base seed     : ").AppendLine(baseSeed.ToString());
            head.Append("noise         : ").Append(noisePercent).AppendLine("%  (sampling device, not a rule)");
            head.Append("round cap     : ").Append(maxRounds).AppendLine("  (safety net, not a rule — R-WIN-04)");
            head.AppendLine();

            for (int m = 0; m < maps.Count; m++)
            {
                // Derived metrics first. They are free, they do not depend on the
                // strategy, and they decide whether the outcome metrics below can
                // say anything at all.
                summary.Append("=== ").Append(maps[m].Name).AppendLine(" — encounter profile (derived) ===");
                try
                {
                    summary.Append(runner.DescribeProfile(maps[m].EncounterText));
                }
                catch (System.Exception ex)
                {
                    summary.Append("  (profile unavailable: ").Append(ex.Message).AppendLine(")");
                }

                for (int s = 0; s < strategies.Count; s++)
                {
                    SimulationConfig config = new SimulationConfig
                    {
                        MapName = maps[m].Name,
                        EncounterText = maps[m].EncounterText,
                        Strategy = strategies[s],
                        Runs = runsPerCell,
                        BaseSeed = baseSeed,
                        NoisePercent = noisePercent,
                        MaxRounds = maxRounds,
                        RouteProbeRow = maps[m].RouteProbeRow,
                        ExpectNoStrategySkillUse =
                            Declared(strategiesExpectedSkillFree, strategies[s].Name)
                    };

                    // One observer set for the whole cell, so the tallies
                    // accumulate over every seed instead of being allocated per
                    // battle. Both are read-only taps.
                    HeatmapObserver heatmap = new HeatmapObserver();
                    RoleMetricsObserver roles = new RoleMetricsObserver();
                    config.Observer = new CompositeObserver(heatmap, roles);

                    List<BattleResult> results = runner.RunBatch(config);
                    output.AllResults.AddRange(results);

                    SimulationSummary cell = SimulationRunner.Summarise(results);
                    cell.Heatmap = heatmap.Heatmap;
                    cell.Roles = roles.Metrics;
                    output.Cells.Add(cell);
                    summary.AppendLine(cell.Describe());
                }
            }

            AppendThresholdCheck(summary, output.Cells);

            output.Anomalies.AddRange(AnomalyDetector.DetectAll(output.AllResults, anomalyThresholds));
            AppendAnomalySummary(summary, output.Anomalies, output.AllResults.Count);

            // Derived reporting over the finished cells. It reruns nothing and
            // writes back into no metric.
            output.Analysis = StrategyAnalysis.Analyse(output.Cells, analysisOptions);
            summary.AppendLine();
            summary.Append(StrategyAnalysis.RenderMatrix(output.Analysis));

            output.Summary = head.ToString()
                             + StrategyAnalysis.RenderAlerts(output.Analysis)
                             + summary;
            output.RawCsv = SimulationRunner.ToCsv(output.AllResults);
            return output;
        }

        private static bool Declared(IList<string> names, string strategy)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], strategy, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// A count per reason, so the text report says whether anomalies.json is
        /// worth opening. The file itself carries the seeds.
        /// </summary>
        private static void AppendAnomalySummary(StringBuilder sb, List<BattleAnomaly> anomalies, int battles)
        {
            sb.AppendLine();
            sb.AppendLine("=== anomalies (see SimResults/anomalies.json for the seeds) ===");
            sb.Append("  ").Append(anomalies.Count).Append(" of ").Append(battles).AppendLine(" battles flagged");

            string[] reasons =
            {
                FailureReason.TimeoutUnresolved,
                FailureReason.AbortedByRejectedCommand,
                FailureReason.DefeatWithHighRemainingHp,
                FailureReason.UnexpectedSkillUsage
            };

            for (int r = 0; r < reasons.Length; r++)
            {
                int n = 0;
                for (int i = 0; i < anomalies.Count; i++)
                    if (anomalies[i].Has(reasons[r])) n++;
                sb.Append("  ").Append(reasons[r].PadRight(30)).Append(n).AppendLine();
            }
        }

        /// <summary>
        /// Compares the numbers against the thresholds in
        /// docs/06-validation/playtest-metrics.md. Reporting only — it never
        /// changes anything, and a FAIL is a finding for the designer, not a bug.
        /// </summary>
        private static void AppendThresholdCheck(StringBuilder sb, List<SimulationSummary> cells)
        {
            sb.AppendLine("=== threshold check (docs/06-validation/playtest-metrics.md) ===");

            for (int i = 0; i < cells.Count; i++)
            {
                SimulationSummary c = cells[i];
                sb.Append(c.Map).Append(" / ").Append(c.Strategy).AppendLine(":");
                sb.Append("  M1 < 70%   : ").AppendLine(Verdict(c.TopCompositionPercent < 70,
                    c.TopCompositionPercent + "%"));
                sb.Append("  M2 < 15%   : ").AppendLine(Verdict(c.ApWastePercent < 15,
                    c.ApWastePercent + "%"));
                sb.Append("  M3 6-10    : ").AppendLine(Verdict(
                    c.MeanTurnsX100 >= 600 && c.MeanTurnsX100 <= 1000,
                    (c.MeanTurnsX100 / 100) + "." + (c.MeanTurnsX100 % 100).ToString("00")));
                sb.Append("  M4 <= 2    : ").AppendLine(Verdict(c.MeanExposureX100 <= 200,
                    (c.MeanExposureX100 / 100) + "." + (c.MeanExposureX100 % 100).ToString("00")));

                // M6 only when the map declared a probe row. A map with no wall
                // has no routes, and "PASS" there would be a number about nothing.
                if (c.RouteProbeRow >= 0)
                {
                    sb.Append("  M6 < 70%   : ").AppendLine(Verdict(c.TopRoutePercent < 70,
                        "x=" + c.TopRouteX + " " + c.TopRoutePercent + "%"));
                    if (c.RunsWithoutCrossing * 100 >= c.Runs * 50)
                        sb.Append("  ** ").Append(c.RunsWithoutCrossing)
                          .AppendLine(" run(s) never crossed the wall — the route sample is thin");
                }

                if (c.Unresolved > 0)
                    sb.Append("  ** ").Append(c.Unresolved).AppendLine(" run(s) hit the round cap — possible stalemate");
            }

            sb.AppendLine();
            sb.AppendLine("M5 comparison (corridor-hold should beat charge, and the chokepoint map");
            sb.AppendLine("should beat the open control — otherwise the map taught nothing):");
            for (int i = 0; i < cells.Count; i++)
                sb.Append("  ").Append(cells[i].Map).Append(" / ").Append(cells[i].Strategy)
                  .Append(" : ").Append(cells[i].WinRatePercent).AppendLine("%");
        }

        private static string Verdict(bool pass, string value) => (pass ? "PASS" : "FAIL") + "  (" + value + ")";
    }
}
