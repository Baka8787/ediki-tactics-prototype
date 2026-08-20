using System.Collections.Generic;
using Ediki.Core;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The triage layer: which battles get flagged, what the JSON looks like, and
    /// whether a replay reproduces the battle it claims to.
    ///
    /// Everything here observes. If one of these ever needs a rule changed to
    /// pass, the layer has stopped being diagnostic.
    /// </summary>
    public class AnomalyAndReplayTests
    {
        private static SimulationRunner NewRunner() =>
            new SimulationRunner(TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

        private static SimulationRunner ControlRunner() =>
            new SimulationRunner(TestWorld.Terrain, TestWorld.ControlUnits, TestWorld.AiProfiles);

        private static SimulationConfig NewConfig(IPlayerStrategy strategy, int noise = 15) =>
            new SimulationConfig
            {
                MapName = "fixture",
                EncounterText = TestWorld.Encounter,
                Strategy = strategy,
                Runs = 8,
                BaseSeed = 1,
                NoisePercent = noise,
                MaxRounds = 30
            };

        private static SimulationConfig ControlConfig(IPlayerStrategy strategy, int noise = 0) =>
            new SimulationConfig
            {
                MapName = "control",
                EncounterText = TestWorld.ControlEncounter,
                Strategy = strategy,
                Runs = 8,
                BaseSeed = 1,
                NoisePercent = noise,
                MaxRounds = 20,
                RouteProbeRow = -1
            };

        /// <summary>A finished battle with the fields the detector reads, and nothing else.</summary>
        private static BattleResult Finished(BattleOutcome outcome, int hp, int maxHp)
        {
            return new BattleResult
            {
                Map = "map",
                Strategy = "corridor-hold",
                Seed = 1,
                Outcome = outcome,
                FinalPlayerHp = hp,
                PlayerMaxHp = maxHp,
                PlayerTurns = 5,
                RoundCap = 60
            };
        }

        // ------------------------------------------------------- A. unresolved

        [Test]
        public void Timeout_IsFlaggedWhenTheBattleNeverReachedAnOutcome()
        {
            // One round of a battle that needs many: the harness stops with the
            // outcome still InProgress, which is what "unresolved" means here.
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());
            config.MaxRounds = 1;

            BattleResult result = NewRunner().RunOne(config, 1);
            Assert.IsTrue(result.HitRoundCap, "Fixture assumption: one round cannot finish this battle.");

            BattleAnomaly anomaly = AnomalyDetector.Detect(result);

            Assert.IsNotNull(anomaly);
            Assert.IsTrue(anomaly.Has(FailureReason.TimeoutUnresolved));
            Assert.AreEqual(1, anomaly.RoundCap, "The cap belongs in the record — 1 of 1 reads differently from 1 of 60.");
            Assert.AreEqual("InProgress", anomaly.Result);
        }

        [Test]
        public void ARunThatEndsNormally_IsNotFlagged()
        {
            // The detector has to stay quiet on ordinary battles or the file is noise.
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Victory, 100, 100)));
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 10, 100)),
                "A defeat with 10% HP left was a fair fight, not an anomaly.");
        }

        [Test]
        public void LosingToTheEncounterClock_IsADefeatNotATimeout()
        {
            // The two clocks are different things. `objective turns=N` is a RULE:
            // it produces a real Defeat, and the run is resolved. Only the
            // harness's own cap leaves a battle unresolved.
            BattleResult result = Finished(BattleOutcome.Defeat, 10, 100);
            Assert.IsFalse(result.HitRoundCap);

            BattleAnomaly anomaly = AnomalyDetector.Detect(result);
            Assert.IsNull(anomaly, "A rule-decided defeat must not be reported as an unresolved run.");
        }

        // ------------------------------------------------- B. high-HP defeat

        [Test]
        public void HighHpDefeat_IsFlaggedStrictlyAboveTheThreshold()
        {
            // 40% exactly is not "above 40%". The boundary is stated because the
            // signature this looks for — losing on time with the party intact —
            // sits near it: playtest-metrics measured 550 of 1150, i.e. 47.8%.
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 40, 100)),
                "Exactly 40% must not trip a > 40% rule.");

            BattleAnomaly flagged = AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 41, 100));
            Assert.IsNotNull(flagged);
            Assert.IsTrue(flagged.Has(FailureReason.DefeatWithHighRemainingHp));
            Assert.AreEqual(410, flagged.RemainingHpPerMille);
            Assert.AreEqual("0.410", flagged.RemainingHpRatioText);

            BattleAnomaly measured = AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 550, 1150));
            Assert.IsNotNull(measured);
            Assert.AreEqual("0.478", measured.RemainingHpRatioText);
        }

        [Test]
        public void HighHpVictory_IsNotADefeat()
        {
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Victory, 100, 100)));
        }

        [Test]
        public void ZeroMaxHp_DoesNotDivideByZero()
        {
            // An encounter of nothing but objective props has no fighting HP.
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 0, 0)));
        }

        [Test]
        public void TheThresholdIsConfigurable()
        {
            AnomalyThresholds strict = new AnomalyThresholds { HighHpDefeatPercent = 10 };
            Assert.IsNotNull(AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 11, 100), strict));
            Assert.IsNull(AnomalyDetector.Detect(Finished(BattleOutcome.Defeat, 11, 100)));
        }

        [Test]
        public void PlayerMaxHp_CountsTheFallenSoAWipeDoesNotLookHealthy()
        {
            // Measured against survivors only, a party reduced to one healthy unit
            // would report full strength. The denominator is fixed at setup.
            BattleResult result = NewRunner().RunOne(NewConfig(new CorridorHoldStrategy()), 3);

            Assert.AreEqual(100, result.PlayerMaxHp, "One hero of 100 max HP in this fixture.");
            Assert.LessOrEqual(result.FinalPlayerHp, result.PlayerMaxHp);
        }

        // ------------------------------------------ C. unexpected skill usage

        [Test]
        public void UnexpectedSkillUsage_IsOnlyRaisedWhenTheCallerAsksForIt()
        {
            // The check is opt-in because IPlayerStrategy carries no skill policy;
            // inferring one from the strategy's NAME would be a guess.
            SimulationRunner runner = ControlRunner();

            SimulationConfig silent = ControlConfig(new ControlHoldStrategy());
            BattleResult unasked = runner.RunOne(silent, 1);
            Assert.Greater(unasked.StrategySkillActions, 0, "Fixture assumption: control-hold uses the kit here.");
            Assert.IsFalse(unasked.ExpectedNoStrategySkillUse);

            // This run IS flagged — control-hold spends its actions on control and
            // never finishes the grunt, so it trips the round cap. That is the
            // separate finding recorded in Strategies.cs, and the assertion has to
            // be about the ONE reason this test is for, not about the run being
            // otherwise clean.
            BattleAnomaly notAsked = AnomalyDetector.Detect(unasked);
            Assert.IsFalse(notAsked != null && notAsked.Has(FailureReason.UnexpectedSkillUsage),
                "Nobody declared this strategy skill-free, so skills must not be a finding.");

            SimulationConfig asked = ControlConfig(new ControlHoldStrategy());
            asked.ExpectNoStrategySkillUse = true;
            BattleAnomaly anomaly = AnomalyDetector.Detect(runner.RunOne(asked, 1));

            Assert.IsNotNull(anomaly);
            Assert.IsTrue(anomaly.Has(FailureReason.UnexpectedSkillUsage),
                "The same battle, with the declaration made, must raise it.");
        }

        [Test]
        public void AStrategyWithNoSkillLogic_NeverAttributesSkillsToItself()
        {
            // The reason strategy and noise are counted apart: LegalCommands lists
            // every affordable skill and the sampling noise draws from that list,
            // so corridor-hold emits skills it never chose. Counting those as the
            // strategy's would flag every batch run on a skill-carrying party.
            SimulationRunner runner = ControlRunner();
            int noiseIssued = 0;

            for (int seed = 1; seed <= 40; seed++)
            {
                BattleResult r = runner.RunOne(ControlConfig(new CorridorHoldStrategy(), noise: 15), seed);
                Assert.AreEqual(0, r.StrategySkillActions,
                    "corridor-hold has no line of code that issues a skill (seed " + seed + ").");
                noiseIssued += r.NoiseSkillActions;
            }

            Assert.Greater(noiseIssued, 0,
                "The noise never issued a skill, so this test is not proving the attribution works.");
        }

        [Test]
        public void UnexpectedSkillUsage_ReadsOnlyTheStrategyColumn()
        {
            // The rule as a truth table, stated once and without a fixture in the
            // way. The row that matters is the first one: skills the NOISE issued
            // must never raise this, or every batch run on a skill-carrying party
            // reports a strategy fault that did not happen.
            BattleResult noiseOnly = Finished(BattleOutcome.Victory, 100, 100);
            noiseOnly.NoiseSkillActions = 5;
            noiseOnly.StrategySkillActions = 0;
            noiseOnly.ExpectedNoStrategySkillUse = true;
            Assert.IsNull(AnomalyDetector.Detect(noiseOnly),
                "Noise issued the skills, not the strategy — this is not a finding.");

            BattleResult noiseOnlyNotAsked = Finished(BattleOutcome.Victory, 100, 100);
            noiseOnlyNotAsked.NoiseSkillActions = 5;
            noiseOnlyNotAsked.ExpectedNoStrategySkillUse = false;
            Assert.IsNull(AnomalyDetector.Detect(noiseOnlyNotAsked));

            BattleResult strategyButNotAsked = Finished(BattleOutcome.Victory, 100, 100);
            strategyButNotAsked.StrategySkillActions = 3;
            strategyButNotAsked.ExpectedNoStrategySkillUse = false;
            Assert.IsNull(AnomalyDetector.Detect(strategyButNotAsked),
                "Undeclared means the question was never asked, whatever the strategy did.");

            BattleResult strategyAndAsked = Finished(BattleOutcome.Victory, 100, 100);
            strategyAndAsked.StrategySkillActions = 1;
            strategyAndAsked.ExpectedNoStrategySkillUse = true;
            BattleAnomaly flagged = AnomalyDetector.Detect(strategyAndAsked);
            Assert.IsNotNull(flagged);
            Assert.IsTrue(flagged.Has(FailureReason.UnexpectedSkillUsage));
            Assert.AreEqual(1, flagged.Reasons.Count, "Only the skill rule should have fired here.");
        }

        [Test]
        public void NoiseIssuedSkills_DoNotFlagASkillFreeStrategy()
        {
            // The same rule, but with the skills actually issued by the sampling
            // device inside a real battle rather than assigned to a field. This is
            // the path that produced 1-2% skill use for corridor-hold on
            // gym-opening, which is what would have been mistaken for a fault.
            SimulationRunner runner = ControlRunner();

            BattleResult withNoiseSkills = null;
            for (int seed = 1; seed <= 40 && withNoiseSkills == null; seed++)
            {
                SimulationConfig config = ControlConfig(new CorridorHoldStrategy(), noise: 15);
                config.ExpectNoStrategySkillUse = true;

                BattleResult candidate = runner.RunOne(config, seed);
                if (candidate.NoiseSkillActions > 0 && candidate.StrategySkillActions == 0)
                    withNoiseSkills = candidate;
            }

            Assert.IsNotNull(withNoiseSkills,
                "No seed produced a noise-issued skill, so this test proves nothing. " +
                "Check that the fixture units still carry skills and that noise is on.");

            Assert.IsTrue(withNoiseSkills.ExpectedNoStrategySkillUse, "The declaration is what arms the rule.");
            BattleAnomaly anomaly = AnomalyDetector.Detect(withNoiseSkills);

            Assert.IsFalse(anomaly != null && anomaly.Has(FailureReason.UnexpectedSkillUsage),
                "A skill the noise chose was attributed to the strategy.");
        }

        [Test]
        public void SkillFreeStrategyWithNoNoise_IsNotFlaggedEvenWhenAsked()
        {
            SimulationConfig config = ControlConfig(new CorridorHoldStrategy(), noise: 0);
            config.ExpectNoStrategySkillUse = true;

            BattleResult result = ControlRunner().RunOne(config, 1);

            Assert.AreEqual(0, result.StrategySkillActions);
            BattleAnomaly anomaly = AnomalyDetector.Detect(result);
            Assert.IsFalse(anomaly != null && anomaly.Has(FailureReason.UnexpectedSkillUsage));
        }

        // ------------------------------------------------------ multiple reasons

        [Test]
        public void OneBattleCanCarrySeveralReasonsAndKeepsThemAll()
        {
            BattleResult result = Finished(BattleOutcome.InProgress, 90, 100);
            result.HitRoundCap = true;
            result.EndedByRejectedCommand = true;
            result.ExpectedNoStrategySkillUse = true;
            result.StrategySkillActions = 2;

            BattleAnomaly anomaly = AnomalyDetector.Detect(result);

            Assert.IsNotNull(anomaly);
            Assert.IsTrue(anomaly.Has(FailureReason.TimeoutUnresolved));
            Assert.IsTrue(anomaly.Has(FailureReason.AbortedByRejectedCommand));
            Assert.IsTrue(anomaly.Has(FailureReason.UnexpectedSkillUsage));
            Assert.AreEqual(3, anomaly.Reasons.Count, "Reasons must accumulate, never overwrite.");

            // High-HP defeat is NOT among them: the run never reached Defeat.
            Assert.IsFalse(anomaly.Has(FailureReason.DefeatWithHighRemainingHp));
        }

        [Test]
        public void TimeoutAndAbortAreDistinguishable()
        {
            // Both leave the battle InProgress and both set HitRoundCap, which is
            // exactly why the second reason exists.
            BattleResult capped = Finished(BattleOutcome.InProgress, 10, 100);
            capped.HitRoundCap = true;

            BattleAnomaly timeout = AnomalyDetector.Detect(capped);
            Assert.IsTrue(timeout.Has(FailureReason.TimeoutUnresolved));
            Assert.IsFalse(timeout.Has(FailureReason.AbortedByRejectedCommand),
                "A clean cap must not be reported as an engine fault.");
        }

        // ----------------------------------------------------------- JSON

        private static List<BattleAnomaly> TwoAnomalies()
        {
            BattleResult a = Finished(BattleOutcome.Defeat, 90, 100);
            a.Map = "gym-b";
            a.Strategy = "charge";
            a.Seed = 7;

            BattleResult b = Finished(BattleOutcome.Defeat, 60, 100);
            b.Map = "gym-a";
            b.Strategy = "corridor-hold";
            b.Seed = 3;

            return AnomalyDetector.DetectAll(new List<BattleResult> { a, b });
        }

        [Test]
        public void Json_IsSortedByEncounterThenStrategyThenSeed()
        {
            List<BattleAnomaly> anomalies = TwoAnomalies();

            Assert.AreEqual(2, anomalies.Count);
            Assert.AreEqual("gym-a", anomalies[0].Encounter, "Sorted by encounter, not by arrival order.");
            Assert.AreEqual("gym-b", anomalies[1].Encounter);
        }

        [Test]
        public void Json_IsByteIdenticalForTheSameInput()
        {
            // The file is meant to be diffed between runs, so anything that
            // depends on dictionary walk order or a culture would destroy it.
            string first = AnomalyReport.ToJson(TwoAnomalies());
            string second = AnomalyReport.ToJson(TwoAnomalies());

            Assert.AreEqual(first, second);
        }

        [Test]
        public void Json_HasStableFieldNamesAndBalancedStructure()
        {
            string json = AnomalyReport.ToJson(TwoAnomalies());

            foreach (string field in new[]
            {
                "\"format_version\"", "\"high_hp_defeat_percent\"", "\"count\"", "\"anomalies\"",
                "\"encounter\"", "\"strategy\"", "\"seed\"", "\"failure_reasons\"",
                "\"result\"", "\"rounds\"", "\"round_cap\"",
                "\"remaining_hp\"", "\"max_hp\"", "\"remaining_hp_ratio\""
            })
            {
                StringAssert.Contains(field, json, "anomalies.json lost a documented field.");
            }

            Assert.AreEqual(Count(json, '{'), Count(json, '}'), "Unbalanced braces.");
            Assert.AreEqual(Count(json, '['), Count(json, ']'), "Unbalanced brackets.");
            StringAssert.Contains("\"count\": 2", json);
            StringAssert.DoesNotContain(",\n  ]", json, "Trailing comma before the closing bracket.");
        }

        [Test]
        public void Json_EmptyReportIsStillValidAndSaysSo()
        {
            string json = AnomalyReport.ToJson(new List<BattleAnomaly>());

            StringAssert.Contains("\"count\": 0", json);
            StringAssert.Contains("\"anomalies\": []", json);
            Assert.AreEqual(Count(json, '{'), Count(json, '}'));
        }

        [Test]
        public void Json_EscapesStringsRatherThanBreakingTheFile()
        {
            BattleResult odd = Finished(BattleOutcome.Defeat, 90, 100);
            odd.Map = "gym\"quote\\slash";

            string json = AnomalyReport.ToJson(AnomalyDetector.DetectAll(new List<BattleResult> { odd }));

            StringAssert.Contains("gym\\\"quote\\\\slash", json);
            Assert.AreEqual(Count(json, '{'), Count(json, '}'));
        }

        private static int Count(string s, char c)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (s[i] == c) n++;
            return n;
        }

        // --------------------------------------------------------- replay

        [Test]
        public void Replay_ReproducesTheBattleTheBatchRan()
        {
            // The point of the whole feature. Same entry point, same seed, same
            // config — so the transcript describes the flagged battle rather than
            // a battle that resembles it.
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            List<BattleResult> batch = runner.RunBatch(config);

            for (int i = 0; i < batch.Count; i++)
            {
                int seed = config.BaseSeed + i;
                ReplayRunner.Result replay = ReplayRunner.Run(runner, config, seed);

                Assert.AreEqual(batch[i].Seed, seed, "Batch seeds are BaseSeed + index.");
                Assert.AreEqual(batch[i].FinalStateHash, replay.Battle.FinalStateHash,
                    "Replay diverged from the batch at seed " + seed + ".");
                Assert.AreEqual(batch[i].Outcome, replay.Battle.Outcome);
                Assert.AreEqual(batch[i].Turns, replay.Battle.Turns);
                Assert.AreEqual(batch[i].PlayerTurns, replay.Battle.PlayerTurns);
                Assert.AreEqual(batch[i].FinalPlayerHp, replay.Battle.FinalPlayerHp);
            }
        }

        [Test]
        public void Replay_IsIdenticalWhenRunTwice()
        {
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            string first = ReplayRunner.Run(runner, config, 4).Transcript;
            string second = ReplayRunner.Run(runner, config, 4).Transcript;

            Assert.AreEqual(first, second);
        }

        [Test]
        public void AttachingAnObserverDoesNotChangeTheBattle()
        {
            // An observer that could alter a decision would make every replay a
            // description of a different run. Nothing it is handed may be touched.
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            BattleResult without = runner.RunOne(config, 9);
            ReplayRunner.Result with = ReplayRunner.Run(runner, config, 9);
            BattleResult after = runner.RunOne(config, 9);

            Assert.AreEqual(without.FinalStateHash, with.Battle.FinalStateHash);
            Assert.AreEqual(without.FinalStateHash, after.FinalStateHash,
                "The observer left something behind — the next run changed.");
            Assert.IsNull(config.Observer, "ReplayRunner must hand the config back as it found it.");
        }

        [Test]
        public void Transcript_ReportsRoundsUnitsPositionsAndOutcome()
        {
            ReplayRunner.Result replay = ReplayRunner.Run(NewRunner(), NewConfig(new CorridorHoldStrategy()), 4);
            string text = replay.Transcript;

            StringAssert.Contains("=== Replay: fixture / corridor-hold / seed 4 ===", text);
            StringAssert.Contains("Round 1", text);
            StringAssert.Contains("[Player:Hero #1]", text, "Team, name and id belong on every actor line.");
            StringAssert.Contains("Pos=(", text);
            StringAssert.Contains("AP=", text);
            StringAssert.Contains("HP=", text);
            StringAssert.Contains("Action: ", text);
            StringAssert.Contains("state hash", text, "The hash is what ties a transcript to a batch row.");
            Assert.Greater(replay.Transcript.Length, 200);
        }

        [Test]
        public void Transcript_ShowsDamageAndDeaths()
        {
            // A fixture where the two sides start adjacent, so blows land early.
            SimulationConfig config = new SimulationConfig
            {
                MapName = "adjacent",
                EncounterText = TestWorld.AdjacentEncounter,
                Strategy = new CorridorHoldStrategy(),
                Runs = 1,
                BaseSeed = 1,
                NoisePercent = 0,
                MaxRounds = 20,
                RouteProbeRow = -1
            };

            string text = ReplayRunner.Run(NewRunner(), config, 1).Transcript;

            StringAssert.Contains("Attack -> ", text);
            StringAssert.Contains("Damage ", text);
            StringAssert.Contains("DIES", text);
        }

        [Test]
        public void Transcript_SaysSoWhenARunIsUnresolved()
        {
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());
            config.MaxRounds = 1;

            string text = ReplayRunner.Run(NewRunner(), config, 1).Transcript;
            StringAssert.Contains("unresolved", text);
        }

        // -------------------------------------------------- request parsing

        private static readonly List<string> KnownEncounters =
            new List<string> { "gym-opening.encounter", "gym-lanes.encounter" };

        private static string ErrorFor(string line)
        {
            ReplayRequest request;
            string error;
            bool ok = ReplayRequest.TryParseLine(line, KnownEncounters, out request, out error);

            Assert.IsFalse(ok, "Expected \"" + line + "\" to be rejected.");
            Assert.IsNull(request);
            Assert.IsNotNull(error);
            Assert.IsNotEmpty(error);
            return error;
        }

        [Test]
        public void Replay_ParsesAWellFormedRequest()
        {
            ReplayRequest request;
            string error;
            bool ok = ReplayRequest.TryParseLine("--replay gym-opening.encounter 42 corridor-hold",
                                                 KnownEncounters, out request, out error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("gym-opening.encounter", request.Encounter);
            Assert.AreEqual(42, request.Seed);
            Assert.AreEqual("corridor-hold", request.Strategy);
            Assert.AreEqual("--replay gym-opening.encounter 42 corridor-hold", request.ToString());
        }

        [Test]
        public void Replay_AcceptsTheThreeValuesWithoutTheFlag()
        {
            ReplayRequest request;
            string error;
            Assert.IsTrue(ReplayRequest.TryParseLine("gym-lanes.encounter -5 charge",
                                                     KnownEncounters, out request, out error), error);
            Assert.AreEqual(-5, request.Seed, "Negative seeds are legal ints and must survive parsing.");
        }

        [Test]
        public void Replay_ExplainsEachWayARequestCanBeWrong()
        {
            // Every one of these used to be a NullReferenceException waiting to
            // happen. The assertions are on CONTENT, not just on failing.
            StringAssert.Contains("nope.encounter", ErrorFor("--replay nope.encounter 1 corridor-hold"));
            StringAssert.Contains("Available", ErrorFor("--replay nope.encounter 1 corridor-hold"));

            StringAssert.Contains("no-such-strategy",
                ErrorFor("--replay gym-opening.encounter 1 no-such-strategy"));
            StringAssert.Contains("corridor-hold",
                ErrorFor("--replay gym-opening.encounter 1 no-such-strategy"));

            StringAssert.Contains("whole number", ErrorFor("--replay gym-opening.encounter abc corridor-hold"));

            StringAssert.Contains("exactly 3", ErrorFor("--replay gym-opening.encounter 1"));
            StringAssert.Contains(ReplayRequest.Usage, ErrorFor("--replay"));
            StringAssert.Contains(ReplayRequest.Usage, ErrorFor(""));
        }

        [Test]
        public void Replay_SuggestsTheEncounterYouProbablyMeant()
        {
            // The usual mistake is dropping the ".encounter" suffix.
            StringAssert.Contains("gym-opening.encounter", ErrorFor("--replay gym-opening 1 corridor-hold"));
        }

        [Test]
        public void Replay_SkipsTheEncounterCheckWhenNoListIsAvailable()
        {
            ReplayRequest request;
            string error;
            Assert.IsTrue(ReplayRequest.TryParseLine("--replay anything.encounter 1 charge",
                                                     null, out request, out error), error);
            Assert.AreEqual("anything.encounter", request.Encounter);
        }

        // ------------------------------------------------------- the catalog

        [Test]
        public void StrategyCatalog_CreatesEveryNameItAdvertisesAndNothingElse()
        {
            // One map from name to strategy, shared by the batch and by replay. A
            // name that advertises but does not resolve would make a replay silently
            // run a different strategy from the batch row it came from.
            for (int i = 0; i < StrategyCatalog.Names.Length; i++)
            {
                string name = StrategyCatalog.Names[i];
                IPlayerStrategy strategy = StrategyCatalog.Create(name);

                Assert.IsNotNull(strategy, "Advertised but not constructible: " + name);
                Assert.AreEqual(name, strategy.Name,
                    "The catalog key and the strategy's own Name must agree, or reports mislabel rows.");
            }

            Assert.IsNull(StrategyCatalog.Create("not-a-strategy"));
            Assert.IsNull(StrategyCatalog.Create(null));
            Assert.IsFalse(StrategyCatalog.IsKnown(""));
        }

        [Test]
        public void StrategyCatalog_HandsOutIndependentInstances()
        {
            Assert.AreNotSame(StrategyCatalog.Create("corridor-hold"), StrategyCatalog.Create("corridor-hold"));
        }
    }
}
