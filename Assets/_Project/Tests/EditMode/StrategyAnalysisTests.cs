using System.Collections.Generic;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The persona matrix and the pairwise screening heuristic.
    ///
    /// Everything here is derived reporting over finished batch cells: no battle
    /// is run to produce a number that already exists, and nothing written here
    /// feeds back into a metric.
    /// </summary>
    public class StrategyAnalysisTests
    {
        /// <summary>
        /// A finished cell with only the fields the analysis reads.
        ///
        /// Built by hand rather than by running a batch, so each condition of the
        /// heuristic can be failed one at a time — a real batch cannot be asked to
        /// lose on exactly one metric.
        /// </summary>
        private static SimulationSummary Cell(string map, string strategy, int runs,
                                              int winPercent, int meanTurnsX100, int hpPerMille,
                                              int unresolved = 0,
                                              int strategySkills = 0, int noiseSkills = 0)
        {
            return new SimulationSummary
            {
                Map = map,
                Strategy = strategy,
                Runs = runs,
                Unresolved = unresolved,
                ResolvedRuns = runs - unresolved,
                WinRatePercent = winPercent,
                MeanTurnsX100 = meanTurnsX100,
                MeanRemainingHpPerMille = hpPerMille,
                StrategySkillActions = strategySkills,
                NoiseSkillActions = noiseSkills
            };
        }

        /// <summary>charge beats corridor-hold on all three: the heuristic should hold.</summary>
        private static List<SimulationSummary> DominatedCells()
        {
            return new List<SimulationSummary>
            {
                Cell("gym-d1-routes", "charge",        60, 72, 1820, 610),
                Cell("gym-d1-routes", "corridor-hold", 60, 61, 2570, 540),
                Cell("gym-d1-routes", "tpa-order",     60, 66, 2140, 580, 0, 12, 3)
            };
        }

        // ------------------------------------------------------- persona matrix

        [Test]
        public void Matrix_HoldsEveryStrategyWithItsOwnSampleCountAndMetrics()
        {
            PersonaMatrix matrix = PersonaMatrix.Build(DominatedCells());

            Assert.AreEqual(1, matrix.Encounters.Count);
            PersonaMatrix.EncounterGroup group = matrix.Group("gym-d1-routes");
            Assert.IsNotNull(group);
            Assert.AreEqual(3, group.Personas.Count);

            StrategyPersona charge = group.Find("charge");
            Assert.AreEqual(60, charge.SampleCount);
            Assert.AreEqual(72, charge.Metric(StrategyMetrics.WinRate).Value);
            Assert.AreEqual("72%", charge.Metric(StrategyMetrics.WinRate).Display);
            Assert.AreEqual(1820, charge.Metric(StrategyMetrics.AverageRounds).Value);
            Assert.AreEqual("18.20", charge.Metric(StrategyMetrics.AverageRounds).Display);
            Assert.AreEqual(610, charge.Metric(StrategyMetrics.RemainingHpRatio).Value);
            Assert.AreEqual("61.0%", charge.Metric(StrategyMetrics.RemainingHpRatio).Display);

            Assert.IsNotNull(group.Find("corridor-hold"));
            Assert.IsNotNull(group.Find("tpa-order"));
        }

        [Test]
        public void Matrix_CarriesBothSkillColumnsSeparately()
        {
            // The split matters: the sampling noise issues skills for strategies
            // that have no skill logic, so one combined column would be unreadable.
            StrategyPersona tpa = PersonaMatrix.Build(DominatedCells())
                .Group("gym-d1-routes").Find("tpa-order");

            Assert.AreEqual(12, tpa.StrategySkillActions);
            Assert.AreEqual(3, tpa.NoiseSkillActions);
        }

        [Test]
        public void Matrix_ReportsUnresolvedAsAPercentage()
        {
            List<SimulationSummary> cells = new List<SimulationSummary>
                { Cell("m", "charge", 60, 50, 1000, 500, unresolved: 10) };

            StrategyPersona persona = PersonaMatrix.Build(cells).Group("m").Find("charge");
            Assert.AreEqual(16, persona.UnresolvedPercent, "10 of 60.");
        }

        [Test]
        public void Matrix_KeepsTheOrderTheBatchProducedAndDoesNotDependOnHashing()
        {
            // Report text has to be diffable between runs, so the row order comes
            // from the batch's own deterministic list, not from a dictionary walk.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                PersonaMatrix matrix = PersonaMatrix.Build(DominatedCells());
                PersonaMatrix.EncounterGroup group = matrix.Encounters[0];

                Assert.AreEqual("charge", group.Personas[0].Strategy);
                Assert.AreEqual("corridor-hold", group.Personas[1].Strategy);
                Assert.AreEqual("tpa-order", group.Personas[2].Strategy);
            }
        }

        // -------------------------------------------------- pairwise comparison

        private static PairwiseComparison CompareOn(List<SimulationSummary> cells,
                                                    AnalysisOptions options = null)
        {
            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(cells, options);
            Assert.AreEqual(1, report.Comparisons.Count, "One configured pair, one encounter.");
            return report.Comparisons[0];
        }

        private static MetricComparison Of(PairwiseComparison c, string metric)
        {
            for (int i = 0; i < c.Metrics.Count; i++)
                if (c.Metrics[i].MetricName == metric) return c.Metrics[i];
            Assert.Fail("Metric missing from the comparison: " + metric);
            return null;
        }

        [Test]
        public void Heuristic_HoldsWhenAllThreeConditionsPass()
        {
            PairwiseComparison c = CompareOn(DominatedCells());

            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.WinRate).Verdict);
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.AverageRounds).Verdict);
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.RemainingHpRatio).Verdict);
            Assert.IsTrue(c.HeuristicHolds);
            Assert.AreEqual("charge", c.ChallengerName);
            Assert.AreEqual("corridor-hold", c.IncumbentName);
        }

        [Test]
        public void Heuristic_FailsOnWinRateAlone()
        {
            List<SimulationSummary> cells = DominatedCells();
            cells[0] = Cell("gym-d1-routes", "charge", 60, 60, 1820, 610);   // 60 < 61

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Fail, Of(c, StrategyMetrics.WinRate).Verdict);
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.AverageRounds).Verdict);
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.RemainingHpRatio).Verdict);
            Assert.IsFalse(c.HeuristicHolds);
        }

        [Test]
        public void Heuristic_FailsOnAverageRoundsAlone()
        {
            List<SimulationSummary> cells = DominatedCells();
            cells[0] = Cell("gym-d1-routes", "charge", 60, 72, 2570, 610);   // equal, not fewer

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Fail, Of(c, StrategyMetrics.AverageRounds).Verdict,
                "Rounds is the one condition a tie does not satisfy.");
            Assert.IsFalse(c.HeuristicHolds);
        }

        [Test]
        public void Heuristic_FailsOnRemainingHpAlone()
        {
            List<SimulationSummary> cells = DominatedCells();
            cells[0] = Cell("gym-d1-routes", "charge", 60, 72, 1820, 539);   // 53.9% < 54.0%

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Fail, Of(c, StrategyMetrics.RemainingHpRatio).Verdict);
            Assert.IsFalse(c.HeuristicHolds);
        }

        [Test]
        public void Heuristic_TreatsTiesAsPassingExceptForRounds()
        {
            // Win rate and HP say "at least as good"; rounds says "strictly fewer".
            List<SimulationSummary> cells = DominatedCells();
            cells[0] = Cell("gym-d1-routes", "charge", 60, 61, 2569, 540);

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.WinRate).Verdict, "61 >= 61.");
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.RemainingHpRatio).Verdict, "540 >= 540.");
            Assert.AreEqual(MetricVerdict.Pass, Of(c, StrategyMetrics.AverageRounds).Verdict, "25.69 < 25.70.");
            Assert.IsTrue(c.HeuristicHolds);
        }

        [Test]
        public void Comparer_WorksOnAnyPairNotJustTheAlertedOne()
        {
            // The detector must not be wired to two names. Same code, different pair.
            PersonaMatrix matrix = PersonaMatrix.Build(DominatedCells());
            PersonaMatrix.EncounterGroup group = matrix.Encounters[0];

            PairwiseComparison c = StrategyComparer.Compare(
                group.Find("tpa-order"), group.Find("corridor-hold"));

            Assert.AreEqual("tpa-order", c.ChallengerName);
            Assert.AreEqual("corridor-hold", c.IncumbentName);
            Assert.IsTrue(c.HeuristicHolds, "66 >= 61, 21.40 < 25.70, 58.0% >= 54.0%.");
        }

        [Test]
        public void Comparer_AcceptsADifferentRuleSetWithoutBeingRewritten()
        {
            AnalysisOptions options = AnalysisOptions.Default;
            options.Rules.Clear();
            options.Rules.Add(new MetricRule(StrategyMetrics.WinRate, true));

            PairwiseComparison c = CompareOn(DominatedCells(), options);

            Assert.AreEqual(1, c.Metrics.Count, "Only the configured rule is evaluated.");
            Assert.IsTrue(c.HeuristicHolds, "72 > 61.");
        }

        // --------------------------------------------------------------- guards

        [Test]
        public void MissingStrategy_ProducesNoAlertAndNoCrash()
        {
            List<SimulationSummary> cells = new List<SimulationSummary>
                { Cell("gym-d1-routes", "charge", 60, 72, 1820, 610) };

            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(cells);

            Assert.AreEqual(0, report.Comparisons.Count, "No pair, so nothing to compare.");
            Assert.AreEqual(0, report.Alerts.Count);
            Assert.AreEqual("", StrategyAnalysis.RenderAlerts(report));
            StringAssert.Contains("comparison unavailable", StrategyAnalysis.RenderMatrix(report));
        }

        [Test]
        public void SampleGuard_IsOffByDefaultAndInventsNoThreshold()
        {
            // "How many battles is enough" is experiment policy this project has
            // not decided, so the detector must not pick a number.
            Assert.IsFalse(AnalysisOptions.Default.MinimumSampleCount.HasValue);

            List<SimulationSummary> tiny = new List<SimulationSummary>
            {
                Cell("m", "charge",        2, 72, 1820, 610),
                Cell("m", "corridor-hold", 2, 61, 2570, 540)
            };

            PairwiseComparison c = CompareOn(tiny);
            Assert.IsTrue(c.HeuristicHolds, "With no guard configured, a small sample still compares.");
            Assert.IsFalse(c.BlockedBySampleGuard);
            Assert.AreEqual(2, c.ChallengerSamples, "N is always reported so a reader can judge it.");
        }

        [Test]
        public void SampleGuard_BlocksTheAlertWhenConfiguredAndSaysSo()
        {
            AnalysisOptions options = AnalysisOptions.Default;
            options.MinimumSampleCount = 30;

            List<SimulationSummary> tiny = new List<SimulationSummary>
            {
                Cell("m", "charge",        2, 72, 1820, 610),
                Cell("m", "corridor-hold", 2, 61, 2570, 540)
            };

            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(tiny, options);

            Assert.AreEqual(0, report.Alerts.Count, "A guarded comparison must not raise an alert.");
            Assert.IsTrue(report.Comparisons[0].BlockedBySampleGuard);
            StringAssert.Contains("sample-size guard", StrategyAnalysis.RenderMatrix(report));
        }

        [Test]
        public void UnavailableMetric_IsNeitherPassNorFailAndBlocksTheHeuristic()
        {
            // Mean rounds is 0 when nothing resolved. Treating that as a very fast
            // battle would hand the comparison to whichever side never finished.
            List<SimulationSummary> cells = new List<SimulationSummary>
            {
                Cell("m", "charge",        60, 72, 0,    610, unresolved: 60),
                Cell("m", "corridor-hold", 60, 61, 2570, 540)
            };

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Unavailable, Of(c, StrategyMetrics.AverageRounds).Verdict);
            StringAssert.Contains("n/a", Of(c, StrategyMetrics.AverageRounds).Detail);
            Assert.IsFalse(c.HeuristicHolds, "An unavailable metric cannot be silently passed.");
            Assert.AreEqual(0, StrategyAnalysis.Analyse(cells).Alerts.Count);
        }

        [Test]
        public void UnavailableHpRatio_IsReportedRatherThanReadAsAWipe()
        {
            List<SimulationSummary> cells = new List<SimulationSummary>
            {
                Cell("m", "charge",        60, 72, 1820, -1),
                Cell("m", "corridor-hold", 60, 61, 2570, 540)
            };

            PairwiseComparison c = CompareOn(cells);

            Assert.AreEqual(MetricVerdict.Unavailable, Of(c, StrategyMetrics.RemainingHpRatio).Verdict);
            Assert.IsFalse(c.HeuristicHolds);

            StrategyPersona persona = PersonaMatrix.Build(cells).Group("m").Find("charge");
            Assert.IsFalse(persona.Metric(StrategyMetrics.RemainingHpRatio).Available);
            StringAssert.Contains("n/a", StrategyAnalysis.RenderMatrix(StrategyAnalysis.Analyse(cells)));
        }

        // ------------------------------------------------------- map isolation

        private static List<SimulationSummary> TwoMaps()
        {
            return new List<SimulationSummary>
            {
                // routes: charge dominates
                Cell("gym-d1-routes", "charge",        60, 72, 1820, 610),
                Cell("gym-d1-routes", "corridor-hold", 60, 61, 2570, 540),
                // flat: corridor-hold is ahead, so nothing should fire here
                Cell("gym-d1-flat",   "charge",        60, 55, 2600, 500),
                Cell("gym-d1-flat",   "corridor-hold", 60, 70, 2100, 620)
            };
        }

        [Test]
        public void EachEncounterIsComparedOnItsOwn()
        {
            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(TwoMaps());

            Assert.AreEqual(2, report.Matrix.Encounters.Count, "Two encounters, never merged.");
            Assert.AreEqual(2, report.Comparisons.Count, "One comparison per encounter.");

            Assert.AreEqual(2, report.Matrix.Group("gym-d1-routes").Personas.Count);
            Assert.AreEqual(2, report.Matrix.Group("gym-d1-flat").Personas.Count);

            // The same strategy on two maps keeps two separate rows.
            Assert.AreEqual(72, report.Matrix.Group("gym-d1-routes").Find("charge")
                                     .Metric(StrategyMetrics.WinRate).Value);
            Assert.AreEqual(55, report.Matrix.Group("gym-d1-flat").Find("charge")
                                     .Metric(StrategyMetrics.WinRate).Value);
        }

        [Test]
        public void AnAlertOnOneEncounterDoesNotFireOnAnother()
        {
            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(TwoMaps());

            Assert.AreEqual(1, report.Alerts.Count, "Only routes satisfies the heuristic.");
            Assert.AreEqual("gym-d1-routes", report.Alerts[0].Encounter);

            for (int i = 0; i < report.Comparisons.Count; i++)
            {
                if (report.Comparisons[i].Encounter != "gym-d1-flat") continue;
                Assert.IsFalse(report.Comparisons[i].HeuristicHolds,
                    "flat must be judged on flat's own numbers.");
            }
        }

        // ------------------------------------------------------------ reporting

        [Test]
        public void Alert_NamesTheEncounterBothSamplesAndEveryMetric()
        {
            string text = StrategyAnalysis.RenderAlerts(StrategyAnalysis.Analyse(DominatedCells()));

            StringAssert.Contains("[ALERT] DOMINANT_STRATEGY_HEURISTIC:", text);
            StringAssert.Contains(
                "Charge currently dominates deliberate positioning under the configured heuristic.", text);
            StringAssert.Contains("gym-d1-routes", text);
            StringAssert.Contains("charge", text);
            StringAssert.Contains("corridor-hold", text);
            StringAssert.Contains("72%", text);
            StringAssert.Contains("61%", text);
            StringAssert.Contains("18.20", text);
            StringAssert.Contains("25.70", text);
            StringAssert.Contains("61.0%", text);
            StringAssert.Contains("54.0%", text);
        }

        [Test]
        public void Wording_NeverClaimsStrictDominance()
        {
            // Three batch averages are a screening heuristic. Calling it strict
            // dominance would claim something the comparison cannot support.
            StrategyAnalysis.Report report = StrategyAnalysis.Analyse(DominatedCells());
            string text = StrategyAnalysis.RenderAlerts(report) + StrategyAnalysis.RenderMatrix(report);

            StringAssert.DoesNotContain("strictly dominates", text);
            StringAssert.DoesNotContain("strict dominance", text);
            StringAssert.DoesNotContain("Strict dominance", text);
            StringAssert.Contains("heuristic", text);
        }

        [Test]
        public void Matrix_RendersARowPerStrategyWithNAndEveryColumn()
        {
            string text = StrategyAnalysis.RenderMatrix(StrategyAnalysis.Analyse(DominatedCells()));

            StringAssert.Contains("Strategy Persona Matrix", text);
            foreach (string column in new[] { "N", "Win%", "AvgRounds", "AvgHP%", "Timeout%", "SkillActs" })
                StringAssert.Contains(column, text);

            foreach (string strategy in new[] { "charge", "corridor-hold", "tpa-order" })
                StringAssert.Contains(strategy, text);

            StringAssert.Contains("18.20", text);
            StringAssert.Contains("12/3", text, "Strategy and noise skill counts, kept apart.");
        }

        [Test]
        public void Analysis_IsDeterministic()
        {
            StrategyAnalysis.Report first = StrategyAnalysis.Analyse(TwoMaps());
            StrategyAnalysis.Report second = StrategyAnalysis.Analyse(TwoMaps());

            Assert.AreEqual(StrategyAnalysis.RenderMatrix(first), StrategyAnalysis.RenderMatrix(second));
            Assert.AreEqual(StrategyAnalysis.RenderAlerts(first), StrategyAnalysis.RenderAlerts(second));
        }

        // ------------------------------------------------- batch integration

        private static BatchOutput RunBatch()
        {
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            List<MapEntry> maps = new List<MapEntry> { new MapEntry("fixture", TestWorld.Encounter) };
            List<IPlayerStrategy> strategies = new List<IPlayerStrategy>
            {
                StrategyCatalog.Create("charge"),
                StrategyCatalog.Create("corridor-hold"),
                StrategyCatalog.Create("tpa-order")
            };

            return MetricsBatch.Run(runner, maps, strategies, 6, 1, 15, 30);
        }

        [Test]
        public void Batch_BuildsTheMatrixFromItsOwnCells()
        {
            BatchOutput output = RunBatch();

            Assert.IsNotNull(output.Analysis);
            PersonaMatrix.EncounterGroup group = output.Analysis.Matrix.Group("fixture");
            Assert.IsNotNull(group);
            Assert.AreEqual(3, group.Personas.Count);

            for (int i = 0; i < output.Cells.Count; i++)
            {
                StrategyPersona persona = group.Find(output.Cells[i].Strategy);
                Assert.IsNotNull(persona, "Every cell must appear in the matrix.");
                Assert.AreEqual(output.Cells[i].Runs, persona.SampleCount);
                Assert.AreEqual(output.Cells[i].WinRatePercent,
                    persona.Metric(StrategyMetrics.WinRate).Value,
                    "The matrix must report the cell's own number, not a recomputed one.");
            }

            StringAssert.Contains("Strategy Persona Matrix", output.Summary);
        }

        [Test]
        public void Batch_PutsAnyAlertAtTheVeryTopOfTheReport()
        {
            BatchOutput output = RunBatch();

            if (output.Analysis.Alerts.Count == 0)
            {
                StringAssert.DoesNotContain("[ALERT] DOMINANT_STRATEGY_HEURISTIC", output.Summary);
                return;
            }

            int alert = output.Summary.IndexOf("[ALERT] DOMINANT_STRATEGY_HEURISTIC",
                                               System.StringComparison.Ordinal);
            int firstCell = output.Summary.IndexOf("encounter profile",
                                                   System.StringComparison.Ordinal);
            Assert.Greater(alert, -1);
            Assert.Less(alert, firstCell, "The alert belongs above the per-cell blocks.");
        }

        [Test]
        public void Batch_LeavesRawMetricsAndTheCsvUntouched()
        {
            BatchOutput output = RunBatch();
            string[] lines = output.RawCsv.Trim().Replace("\r\n", "\n").Split('\n');

            Assert.AreEqual(
                "map,strategy,seed,outcome,turns,hit_round_cap,player_turns,ap_granted,ap_unused," +
                "final_player_hp,enemies_remaining,mean_exposure_x100,first_crossing_x," +
                "unit_crossings,contact_round1,contact_turns,never_in_contact,state_hash",
                lines[0], "The CSV header changed.");
            Assert.AreEqual(output.AllResults.Count + 1, lines.Length);
            StringAssert.DoesNotContain("Persona", output.RawCsv);
            StringAssert.DoesNotContain("ALERT", output.RawCsv);

            // The analysis reads the cells; it must not have written to them.
            for (int i = 0; i < output.Cells.Count; i++)
                Assert.AreEqual(6, output.Cells[i].Runs);
        }

        [Test]
        public void Batch_AnalysisIsDeterministicAcrossIdenticalRuns()
        {
            BatchOutput first = RunBatch();
            BatchOutput second = RunBatch();

            Assert.AreEqual(first.Summary, second.Summary, "The whole report, alerts and matrix included.");
            Assert.AreEqual(StrategyAnalysis.RenderMatrix(first.Analysis),
                            StrategyAnalysis.RenderMatrix(second.Analysis));
            Assert.AreEqual(first.Analysis.Alerts.Count, second.Analysis.Alerts.Count);
        }

        [Test]
        public void Batch_DoesNotTouchTopRoutePercent()
        {
            // A known per-x reporting bug, held for a separate task. This pins the
            // current semantics so the analysis work cannot have shifted them.
            BatchOutput output = RunBatch();

            for (int i = 0; i < output.Cells.Count; i++)
            {
                Assert.AreEqual(-1, output.Cells[i].RouteProbeRow, "Fixture has no dividing wall.");
                Assert.AreEqual(0, output.Cells[i].TopRoutePercent);
                Assert.AreEqual(-1, output.Cells[i].TopRouteX);
            }
        }
    }
}
