using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ediki.Sim
{
    /// <summary>Which way is better for a metric. Nothing here decides that per strategy.</summary>
    public enum MetricDirection { HigherIsBetter, LowerIsBetter }

    /// <summary>How one strategy's metric compared with another's.</summary>
    public enum MetricVerdict { Pass, Fail, Unavailable }

    /// <summary>
    /// One number about one strategy, carried with the two things a comparison
    /// needs and a raw int cannot express: which direction counts as better, and
    /// whether the number exists at all.
    ///
    /// Availability is explicit because several of this project's metrics use 0
    /// as a legitimate value AND as "nothing to report" — mean rounds is 0 when
    /// every battle was unresolved, which is not a fast battle. Treating that as
    /// zero would quietly hand a comparison to whichever side failed to finish.
    /// </summary>
    public sealed class StrategyMetric
    {
        public readonly string Name;
        public readonly bool Available;

        /// <summary>Integer so comparisons are exact; the scale is per metric.</summary>
        public readonly long Value;

        public readonly MetricDirection Better;

        /// <summary>Formatted for the report, invariant culture.</summary>
        public readonly string Display;

        public StrategyMetric(string name, bool available, long value,
                              MetricDirection better, string display)
        {
            Name = name;
            Available = available;
            Value = value;
            Better = better;
            Display = display;
        }

        public static StrategyMetric Missing(string name, MetricDirection better) =>
            new StrategyMetric(name, false, 0, better, "n/a");
    }

    /// <summary>Metric names, so a rule and a lookup cannot drift apart.</summary>
    public static class StrategyMetrics
    {
        public const string WinRate = "Win Rate";
        public const string AverageRounds = "Avg Rounds";
        public const string RemainingHpRatio = "Remaining HP";
    }

    /// <summary>
    /// One strategy's showing on ONE encounter.
    ///
    /// Encounter is part of the identity, never averaged away: gym-d1-routes and
    /// gym-d1-flat are different questions, and a strategy that wins one and
    /// loses the other has no meaningful combined number. The identity is the
    /// (Map, Strategy) pair SimulationSummary already carries — Map being the
    /// name the batch gave the encounter — and nothing here changes it.
    /// </summary>
    public sealed class StrategyPersona
    {
        public string Encounter;
        public string Strategy;

        /// <summary>Battles behind these numbers.</summary>
        public int SampleCount;

        public int UnresolvedRuns;
        public int StrategySkillActions;
        public int NoiseSkillActions;

        public readonly List<StrategyMetric> Metrics = new List<StrategyMetric>();

        public StrategyMetric Metric(string name)
        {
            for (int i = 0; i < Metrics.Count; i++)
                if (Metrics[i].Name == name) return Metrics[i];
            return null;
        }

        /// <summary>Unresolved battles as a percentage. 0 samples reports 0.</summary>
        public int UnresolvedPercent => SampleCount == 0 ? 0 : UnresolvedRuns * 100 / SampleCount;

        /// <summary>
        /// Reads a finished batch cell. Every number already exists on the
        /// summary; this only decides which of them are meaningful.
        /// </summary>
        public static StrategyPersona From(SimulationSummary cell)
        {
            StrategyPersona persona = new StrategyPersona
            {
                Encounter = cell.Map,
                Strategy = cell.Strategy,
                SampleCount = cell.Runs,
                UnresolvedRuns = cell.Unresolved,
                StrategySkillActions = cell.StrategySkillActions,
                NoiseSkillActions = cell.NoiseSkillActions
            };

            persona.Metrics.Add(cell.Runs > 0
                ? new StrategyMetric(StrategyMetrics.WinRate, true, cell.WinRatePercent,
                                     MetricDirection.HigherIsBetter, cell.WinRatePercent + "%")
                : StrategyMetric.Missing(StrategyMetrics.WinRate, MetricDirection.HigherIsBetter));

            // Mean turns is only defined over battles that finished. With none,
            // the field holds 0 and means nothing.
            persona.Metrics.Add(cell.ResolvedRuns > 0
                ? new StrategyMetric(StrategyMetrics.AverageRounds, true, cell.MeanTurnsX100,
                                     MetricDirection.LowerIsBetter, Hundredths(cell.MeanTurnsX100))
                : StrategyMetric.Missing(StrategyMetrics.AverageRounds, MetricDirection.LowerIsBetter));

            persona.Metrics.Add(cell.MeanRemainingHpPerMille >= 0
                ? new StrategyMetric(StrategyMetrics.RemainingHpRatio, true, cell.MeanRemainingHpPerMille,
                                     MetricDirection.HigherIsBetter,
                                     Percent1(cell.MeanRemainingHpPerMille))
                : StrategyMetric.Missing(StrategyMetrics.RemainingHpRatio, MetricDirection.HigherIsBetter));

            return persona;
        }

        internal static string Hundredths(int x100) =>
            (x100 / 100).ToString(CultureInfo.InvariantCulture) + "." +
            (x100 % 100).ToString("00", CultureInfo.InvariantCulture);

        /// <summary>Per-mille rendered as a percentage with one decimal.</summary>
        internal static string Percent1(int perMille) =>
            (perMille / 10).ToString(CultureInfo.InvariantCulture) + "." +
            (perMille % 10).ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// One condition of a heuristic: which metric, and whether a tie counts.
    ///
    /// Rules are data so the heuristic can be restated without touching the
    /// comparer, and so the comparer never needs to know a strategy's name.
    /// </summary>
    public sealed class MetricRule
    {
        public readonly string MetricName;

        /// <summary>True = the challenger must be strictly better; false = ties pass.</summary>
        public readonly bool RequireStrict;

        public MetricRule(string metricName, bool requireStrict)
        {
            MetricName = metricName;
            RequireStrict = requireStrict;
        }
    }

    public sealed class MetricComparison
    {
        public string MetricName;
        public MetricVerdict Verdict;
        public string Detail;
    }

    /// <summary>
    /// The result of holding one strategy up against another.
    ///
    /// Deliberately NOT called dominance in the game-theoretic sense: it compares
    /// three averages from one batch, which is a screening heuristic and not a
    /// proof that one line of play beats another everywhere.
    /// </summary>
    public sealed class PairwiseComparison
    {
        public string Encounter;
        public string ChallengerName;
        public string IncumbentName;

        public int ChallengerSamples;
        public int IncumbentSamples;

        public readonly List<MetricComparison> Metrics = new List<MetricComparison>();

        /// <summary>Every rule passed. False whenever any metric is unavailable.</summary>
        public bool HeuristicHolds;

        /// <summary>Set when a configured minimum sample count blocked the result.</summary>
        public bool BlockedBySampleGuard;

        /// <summary>Human-readable reasons, in rule order.</summary>
        public readonly List<string> Reasons = new List<string>();
    }

    /// <summary>Knobs that are experiment policy rather than detector behaviour.</summary>
    public sealed class AnalysisOptions
    {
        /// <summary>
        /// Smallest sample either side may have before a comparison is allowed to
        /// raise an alert.
        ///
        /// Null by default, meaning no guard. It is deliberately not given a
        /// number here: "how many battles is enough" is an experiment decision
        /// this project has never written down, and inventing one in a detector
        /// would make it look decided.
        /// </summary>
        public int? MinimumSampleCount;

        /// <summary>
        /// Which (challenger, incumbent) pairs get a report alert.
        ///
        /// The comparer works on any pair; this is only the list the report
        /// shouts about. Default is the one question this prototype keeps asking:
        /// does charging beat playing the position?
        /// </summary>
        public readonly List<KeyValuePair<string, string>> AlertPairs =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("charge", "corridor-hold")
            };

        /// <summary>
        /// The heuristic: at least as many wins, strictly fewer rounds, and at
        /// least as much HP left.
        /// </summary>
        public readonly List<MetricRule> Rules = new List<MetricRule>
        {
            new MetricRule(StrategyMetrics.WinRate, false),
            new MetricRule(StrategyMetrics.AverageRounds, true),
            new MetricRule(StrategyMetrics.RemainingHpRatio, false)
        };

        public static AnalysisOptions Default => new AnalysisOptions();
    }

    /// <summary>
    /// Compares two strategies metric by metric.
    ///
    /// It knows nothing about which strategies exist. Give it two personas and a
    /// list of rules and it applies them; the caller decides which pairs are
    /// interesting, so adding a strategy needs no change here.
    /// </summary>
    public static class StrategyComparer
    {
        public const string HeuristicName = "DOMINANT_STRATEGY_HEURISTIC";

        public static PairwiseComparison Compare(StrategyPersona challenger, StrategyPersona incumbent,
                                                 AnalysisOptions options = null)
        {
            AnalysisOptions settings = options ?? AnalysisOptions.Default;

            PairwiseComparison result = new PairwiseComparison
            {
                Encounter = challenger.Encounter,
                ChallengerName = challenger.Strategy,
                IncumbentName = incumbent.Strategy,
                ChallengerSamples = challenger.SampleCount,
                IncumbentSamples = incumbent.SampleCount,
                HeuristicHolds = true
            };

            for (int i = 0; i < settings.Rules.Count; i++)
            {
                MetricRule rule = settings.Rules[i];
                StrategyMetric a = challenger.Metric(rule.MetricName);
                StrategyMetric b = incumbent.Metric(rule.MetricName);

                MetricComparison comparison = new MetricComparison { MetricName = rule.MetricName };

                if (a == null || b == null || !a.Available || !b.Available)
                {
                    // Unavailable is neither pass nor fail. Scoring it as either
                    // would let a strategy that never finished a battle win on a
                    // metric it never produced.
                    comparison.Verdict = MetricVerdict.Unavailable;
                    comparison.Detail = Describe(a) + " vs " + Describe(b) + "  (metric unavailable)";
                    result.HeuristicHolds = false;
                }
                else
                {
                    bool passes = Passes(a, b, rule.RequireStrict);
                    comparison.Verdict = passes ? MetricVerdict.Pass : MetricVerdict.Fail;
                    comparison.Detail = a.Display + " " + Symbol(a.Better, rule.RequireStrict) + " " + b.Display;
                    if (!passes) result.HeuristicHolds = false;
                }

                result.Metrics.Add(comparison);
                result.Reasons.Add(rule.MetricName + ": " + comparison.Verdict + "  " + comparison.Detail);
            }

            // The guard is applied last so the metric verdicts still read true —
            // the comparison happened, it just does not get to raise an alert.
            if (settings.MinimumSampleCount.HasValue)
            {
                int floor = settings.MinimumSampleCount.Value;
                if (challenger.SampleCount < floor || incumbent.SampleCount < floor)
                {
                    result.BlockedBySampleGuard = true;
                    result.HeuristicHolds = false;
                    result.Reasons.Add("sample guard: needs " + floor + " per side, have "
                                       + challenger.SampleCount + " and " + incumbent.SampleCount);
                }
            }

            return result;
        }

        private static bool Passes(StrategyMetric a, StrategyMetric b, bool strict)
        {
            if (a.Better == MetricDirection.HigherIsBetter)
                return strict ? a.Value > b.Value : a.Value >= b.Value;
            return strict ? a.Value < b.Value : a.Value <= b.Value;
        }

        private static string Symbol(MetricDirection better, bool strict)
        {
            if (better == MetricDirection.HigherIsBetter) return strict ? ">" : ">=";
            return strict ? "<" : "<=";
        }

        private static string Describe(StrategyMetric m) => m == null ? "n/a" : m.Display;
    }

    /// <summary>
    /// Every strategy's showing, grouped by encounter.
    ///
    /// Lists rather than dictionaries throughout: the report has to come out the
    /// same way every run, and insertion order here is the batch's own order,
    /// which is already deterministic.
    /// </summary>
    public sealed class PersonaMatrix
    {
        public sealed class EncounterGroup
        {
            public string Encounter;
            public readonly List<StrategyPersona> Personas = new List<StrategyPersona>();

            public StrategyPersona Find(string strategy)
            {
                for (int i = 0; i < Personas.Count; i++)
                    if (Personas[i].Strategy == strategy) return Personas[i];
                return null;
            }
        }

        public readonly List<EncounterGroup> Encounters = new List<EncounterGroup>();

        public EncounterGroup Group(string encounter)
        {
            for (int i = 0; i < Encounters.Count; i++)
                if (Encounters[i].Encounter == encounter) return Encounters[i];
            return null;
        }

        public static PersonaMatrix Build(IList<SimulationSummary> cells)
        {
            PersonaMatrix matrix = new PersonaMatrix();
            if (cells == null) return matrix;

            for (int i = 0; i < cells.Count; i++)
            {
                StrategyPersona persona = StrategyPersona.From(cells[i]);

                EncounterGroup group = matrix.Group(persona.Encounter);
                if (group == null)
                {
                    group = new EncounterGroup { Encounter = persona.Encounter };
                    matrix.Encounters.Add(group);
                }

                group.Personas.Add(persona);
            }

            return matrix;
        }
    }

    /// <summary>
    /// Renders the persona matrix and the pairwise alerts.
    ///
    /// Derived reporting only: it reads finished summaries, runs no battle and
    /// changes no metric. Nothing it prints feeds back into the CSV.
    /// </summary>
    public static class StrategyAnalysis
    {
        public sealed class Report
        {
            public PersonaMatrix Matrix;
            public readonly List<PairwiseComparison> Comparisons = new List<PairwiseComparison>();

            /// <summary>Comparisons where the heuristic held. These become alerts.</summary>
            public readonly List<PairwiseComparison> Alerts = new List<PairwiseComparison>();
        }

        public static Report Analyse(IList<SimulationSummary> cells, AnalysisOptions options = null)
        {
            AnalysisOptions settings = options ?? AnalysisOptions.Default;

            Report report = new Report { Matrix = PersonaMatrix.Build(cells) };

            // Per encounter, never across them: a result on gym-d1-routes says
            // nothing about gym-d1-flat, and merging the two would answer a
            // question nobody asked.
            for (int e = 0; e < report.Matrix.Encounters.Count; e++)
            {
                PersonaMatrix.EncounterGroup group = report.Matrix.Encounters[e];

                for (int p = 0; p < settings.AlertPairs.Count; p++)
                {
                    StrategyPersona challenger = group.Find(settings.AlertPairs[p].Key);
                    StrategyPersona incumbent = group.Find(settings.AlertPairs[p].Value);

                    // A pair that is not present on this encounter is not a
                    // finding and not an error.
                    if (challenger == null || incumbent == null) continue;

                    PairwiseComparison comparison = StrategyComparer.Compare(challenger, incumbent, settings);
                    report.Comparisons.Add(comparison);
                    if (comparison.HeuristicHolds) report.Alerts.Add(comparison);
                }
            }

            return report;
        }

        /// <summary>The alert block, meant for the very top of a report. Empty when none held.</summary>
        public static string RenderAlerts(Report report)
        {
            if (report.Alerts.Count == 0) return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < report.Alerts.Count; i++)
            {
                PairwiseComparison c = report.Alerts[i];

                sb.Append("[ALERT] ").Append(StrategyComparer.HeuristicName).AppendLine(":");
                sb.AppendLine("Charge currently dominates deliberate positioning under the configured heuristic.");
                sb.Append("  Encounter / Map        : ").AppendLine(c.Encounter);
                sb.Append("  ").Append(Pad(c.ChallengerName)).Append(" N          : ")
                  .Append(N(c.ChallengerSamples)).AppendLine();
                sb.Append("  ").Append(Pad(c.IncumbentName)).Append(" N          : ")
                  .Append(N(c.IncumbentSamples)).AppendLine();

                for (int m = 0; m < c.Metrics.Count; m++)
                {
                    sb.Append("  ").Append(c.Metrics[m].MetricName.PadRight(22)).Append(" : ")
                      .Append(c.Metrics[m].Verdict.ToString().PadRight(12))
                      .AppendLine(c.Metrics[m].Detail);
                }

                sb.AppendLine("  (screening heuristic over batch averages, not a game-theoretic result)");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>The matrix plus every comparison that was evaluated.</summary>
        public static string RenderMatrix(Report report)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Strategy Persona Matrix ===");

            for (int e = 0; e < report.Matrix.Encounters.Count; e++)
            {
                PersonaMatrix.EncounterGroup group = report.Matrix.Encounters[e];

                sb.Append("  ").AppendLine(group.Encounter);
                sb.AppendLine("    Strategy            N    Win%   AvgRounds   AvgHP%   Timeout%   SkillActs(strategy/noise)");

                for (int p = 0; p < group.Personas.Count; p++)
                {
                    StrategyPersona persona = group.Personas[p];
                    sb.Append("    ").Append(persona.Strategy.PadRight(18))
                      .Append(N(persona.SampleCount).PadLeft(4))
                      .Append(Cell(persona, StrategyMetrics.WinRate).PadLeft(8))
                      .Append(Cell(persona, StrategyMetrics.AverageRounds).PadLeft(12))
                      .Append(Cell(persona, StrategyMetrics.RemainingHpRatio).PadLeft(9))
                      .Append((N(persona.UnresolvedPercent) + "%").PadLeft(11))
                      .Append((N(persona.StrategySkillActions) + "/" + N(persona.NoiseSkillActions)).PadLeft(15))
                      .AppendLine();
                }

                sb.AppendLine();
            }

            AppendComparisons(sb, report);
            return sb.ToString();
        }

        private static void AppendComparisons(StringBuilder sb, Report report)
        {
            sb.AppendLine("=== Pairwise comparison (screening heuristic) ===");

            if (report.Comparisons.Count == 0)
            {
                sb.AppendLine("  comparison unavailable — the configured pair is not present on any encounter");
                return;
            }

            for (int i = 0; i < report.Comparisons.Count; i++)
            {
                PairwiseComparison c = report.Comparisons[i];

                sb.Append("  ").Append(c.Encounter).Append("   ")
                  .Append(c.ChallengerName).Append(" (N=").Append(N(c.ChallengerSamples)).Append(") vs ")
                  .Append(c.IncumbentName).Append(" (N=").Append(N(c.IncumbentSamples)).AppendLine(")");

                for (int m = 0; m < c.Metrics.Count; m++)
                {
                    sb.Append("    ").Append(c.Metrics[m].MetricName.PadRight(16))
                      .Append(c.Metrics[m].Verdict.ToString().PadRight(13))
                      .AppendLine(c.Metrics[m].Detail);
                }

                if (c.BlockedBySampleGuard)
                    sb.AppendLine("    ** sample-size guard blocked this comparison");

                sb.Append("    ").Append(StrategyComparer.HeuristicName).Append(": ")
                  .AppendLine(c.HeuristicHolds ? "TRUE" : "FALSE");
                sb.AppendLine();
            }
        }

        private static string Cell(StrategyPersona persona, string metric)
        {
            StrategyMetric m = persona.Metric(metric);
            return m == null || !m.Available ? "n/a" : m.Display;
        }

        private static string Pad(string s) => s.PadRight(18);

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
