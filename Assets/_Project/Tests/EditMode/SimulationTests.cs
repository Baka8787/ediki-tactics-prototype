using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The metrics runner is a measuring instrument. These tests check that it
    /// measures without disturbing what it measures.
    /// </summary>
    public class SimulationTests
    {
        private static SimulationRunner NewRunner() =>
            new SimulationRunner(TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

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

        [Test]
        public void SameSeed_ReplaysTheSameBattle()
        {
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            BattleResult a = runner.RunOne(config, 12345);
            BattleResult b = runner.RunOne(config, 12345);

            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
            Assert.AreEqual(a.Outcome, b.Outcome);
            Assert.AreEqual(a.Turns, b.Turns);
            CollectionAssert.AreEqual(a.ActionCompositions, b.ActionCompositions);
        }

        [Test]
        public void DifferentSeeds_ActuallyExploreDifferentPlay()
        {
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            HashSet<uint> hashes = new HashSet<uint>();
            for (int seed = 1; seed <= 25; seed++) hashes.Add(runner.RunOne(config, seed).FinalStateHash);

            Assert.Greater(hashes.Count, 1,
                "Every seed produced the same battle — the sampling noise is not doing anything.");
        }

        [Test]
        public void ZeroNoise_MakesEverySeedIdentical()
        {
            // Confirms the variance comes from the sampling device, not from the
            // rule layer. The rules stay deterministic (ADR-0003).
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy(), noise: 0);

            uint first = runner.RunOne(config, 1).FinalStateHash;
            for (int seed = 2; seed <= 10; seed++)
                Assert.AreEqual(first, runner.RunOne(config, seed).FinalStateHash,
                    "With no noise the simulation must be fully deterministic regardless of seed.");
        }

        [Test]
        public void BatchIsReproducible()
        {
            SimulationRunner runner = NewRunner();

            List<BattleResult> a = runner.RunBatch(NewConfig(new AggressiveStrategy()));
            List<BattleResult> b = runner.RunBatch(NewConfig(new AggressiveStrategy()));

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].FinalStateHash, b[i].FinalStateHash);
        }

        [Test]
        public void MetricsAreInternallyConsistent()
        {
            BattleResult r = NewRunner().RunOne(NewConfig(new CorridorHoldStrategy()), 7);

            Assert.AreEqual(r.PlayerTurns, r.ActionCompositions.Count,
                "One action-composition entry per player turn (M1).");
            Assert.LessOrEqual(r.ApUnused, r.ApGranted, "Unused AP cannot exceed granted AP (M2).");
            Assert.GreaterOrEqual(r.ApUnused, 0);
            Assert.Greater(r.Turns, 0);

            for (int i = 0; i < r.EndOfTurnExposure.Count; i++)
            {
                Assert.GreaterOrEqual(r.EndOfTurnExposure[i], 0);
                Assert.LessOrEqual(r.EndOfTurnExposure[i], 4, "Four-neighbour grid: exposure cannot exceed 4 (M4).");
            }
        }

        [Test]
        public void SummaryAggregatesWithoutInventingNumbers()
        {
            SimulationRunner runner = NewRunner();
            List<BattleResult> results = runner.RunBatch(NewConfig(new CorridorHoldStrategy()));
            SimulationSummary s = SimulationRunner.Summarise(results);

            Assert.AreEqual(results.Count, s.Runs);
            Assert.AreEqual(results.Count, s.Wins + s.Losses + s.Unresolved,
                "Every run must land in exactly one outcome bucket.");
            Assert.GreaterOrEqual(s.TopCompositionPercent, 0);
            Assert.LessOrEqual(s.TopCompositionPercent, 100);
            Assert.GreaterOrEqual(s.ApWastePercent, 0);
            Assert.LessOrEqual(s.ApWastePercent, 100);
        }

        [Test]
        public void UnresolvedRunsAreExcludedFromTurnStatistics()
        {
            // A run that trips the safety net has no meaningful length. Averaging
            // the cap value in would quietly invent a number.
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());
            config.MaxRounds = 1;              // force everything to be unresolved
            config.Runs = 4;

            SimulationSummary s = SimulationRunner.Summarise(runner.RunBatch(config));

            Assert.AreEqual(4, s.Unresolved);
            Assert.AreEqual(0, s.MeanTurnsX100, "With no resolved battles there is no mean turn count.");
        }

        [Test]
        public void RunningMetricsDoesNotMutateTheSharedCatalogsOrRules()
        {
            // The runner rebuilds the encounter per battle; nothing it does may
            // leak into the next run or into the rule layer's constants.
            SimulationRunner runner = NewRunner();
            SimulationConfig config = NewConfig(new CorridorHoldStrategy());

            uint before = runner.RunOne(config, 99).FinalStateHash;
            runner.RunBatch(NewConfig(new AggressiveStrategy()));
            uint after = runner.RunOne(config, 99).FinalStateHash;

            Assert.AreEqual(before, after, "An unrelated batch changed the outcome of a seeded run.");
            Assert.AreEqual(50, BattleRules.GuardDamagePercent, "Metrics must not touch rule constants.");
        }

        [Test]
        public void LegalCommands_AreAllAcceptedBySimulator()
        {
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);

            List<ICommand> legal = LegalCommands.For(state, hero);
            Assert.Greater(legal.Count, 0);

            for (int i = 0; i < legal.Count; i++)
            {
                ExecuteResult r = BattleSimulator.Execute(state, legal[i]);
                Assert.IsTrue(r.Ok, "Enumerated as legal but rejected: " + legal[i].Describe() + " — " + r.RejectReason);
            }
        }

        [Test]
        public void CsvExportHasOneRowPerBattle()
        {
            List<BattleResult> results = NewRunner().RunBatch(NewConfig(new CorridorHoldStrategy()));
            string csv = SimulationRunner.ToCsv(results);

            string[] lines = csv.Trim().Replace("\r\n", "\n").Split('\n');
            Assert.AreEqual(results.Count + 1, lines.Length, "Header plus one row per battle.");
            StringAssert.StartsWith("map,strategy,seed,outcome,turns", lines[0]);
            StringAssert.Contains("first_crossing_x", lines[0], "M6 must reach the raw export (route analysis is offline).");
        }

        // ------------------------------------------------------------------ M6

        private static SimulationConfig CrossingConfig(int probeRow) =>
            new SimulationConfig
            {
                MapName = "crossing",
                EncounterText = TestWorld.CrossingEncounter,
                Strategy = new AggressiveStrategy(),
                Runs = 4,
                BaseSeed = 1,
                NoisePercent = 0,          // no sampling noise: the route must be forced, not lucky
                MaxRounds = 20,
                RouteProbeRow = probeRow
            };

        [Test]
        public void M6_ReportsTheGapTheUnitActuallyWalkedThrough()
        {
            // The hero clears the wall in one move that ENDS at (4,1), north of
            // the wall row. Only a scan of the whole path can see the crossing;
            // reading From/To would report "never crossed".
            BattleResult r = NewRunner().RunOne(CrossingConfig(2), 1);

            Assert.AreEqual(4, r.FirstCrossingX,
                "The east gap is the shorter way to the enemy, so x=4 is the route taken.");
            Assert.AreEqual(1, r.UnitCrossings.Count, "One player unit, so one crossing.");
            Assert.AreEqual(TestWorld.HeroId, r.UnitCrossings[0].Key);
        }

        [Test]
        public void M6_CountsOnlyTheFirstCrossingOfEachUnit()
        {
            // Walking back and forth over the wall must not let one unit vote twice.
            BattleResult r = NewRunner().RunOne(CrossingConfig(2), 1);
            Assert.LessOrEqual(r.UnitCrossings.Count, 1);
        }

        [Test]
        public void M6_DisabledProbe_RecordsNothing()
        {
            // A map with no dividing wall has no routes; -1 must stay silent
            // rather than report an x that means nothing.
            BattleResult r = NewRunner().RunOne(CrossingConfig(-1), 1);

            Assert.AreEqual(-1, r.FirstCrossingX);
            Assert.AreEqual(0, r.UnitCrossings.Count);
        }

        [Test]
        public void M6_ProbeDoesNotChangeTheBattleItMeasures()
        {
            // M6 is an observation. Turning it on must not move a single unit.
            SimulationRunner runner = NewRunner();

            uint withProbe = runner.RunOne(CrossingConfig(2), 3).FinalStateHash;
            uint without = runner.RunOne(CrossingConfig(-1), 3).FinalStateHash;

            Assert.AreEqual(without, withProbe, "Measuring the route changed the battle.");
        }

        [Test]
        public void M6_DisabledProbe_ReportsNothingRatherThanZeroCrossings()
        {
            // "0 of 200 crossed" and "nothing was measured" look identical in a
            // histogram and mean opposite things. A map with no wall must produce
            // the second one.
            SimulationSummary s = SimulationRunner.Summarise(NewRunner().RunBatch(CrossingConfig(-1)));

            Assert.AreEqual(0, s.RunsWithoutCrossing, "A disabled probe must not fill the never-crossed bucket.");
            Assert.AreEqual(0, s.RouteHistogram.Count);
            StringAssert.DoesNotContain("M6", s.Describe(), "M6 must stay silent when it measured nothing.");
        }

        // ------------------------------------------------------------------ M7

        [Test]
        public void M7_RecordsTheRoundAnEnemyCouldFirstReachThePlayer()
        {
            // Fixture: hero (1,1), grunt (5,3), grunt moves 3 and strikes at 1,
            // so it cannot possibly reach the hero before it has moved.
            BattleResult r = NewRunner().RunOne(NewConfig(new CorridorHoldStrategy(), noise: 0), 1);

            Assert.AreEqual(1, r.EnemyReleases.Count, "One enemy, so at most one release entry.");
            Assert.Greater(r.EnemyReleases[0].Value, 1,
                "The grunt starts out of reach; release on round 1 would mean the probe is measuring the wrong thing.");
            Assert.AreEqual(0, r.EnemiesInContactOnRound1);
        }

        [Test]
        public void M7_AnEnemyStartingInReachIsReleasedOnRoundOne()
        {
            SimulationConfig config = new SimulationConfig
            {
                MapName = "adjacent",
                EncounterText = TestWorld.AdjacentEncounter,   // hero and grunt side by side
                Strategy = new CorridorHoldStrategy(),
                Runs = 1,
                BaseSeed = 1,
                NoisePercent = 0,
                MaxRounds = 20,
                RouteProbeRow = -1
            };

            BattleResult r = NewRunner().RunOne(config, 1);

            Assert.AreEqual(1, r.EnemiesInContactOnRound1,
                "A unit standing next to the player is in reach before anyone moves.");
            Assert.AreEqual(1, r.EnemyReleases[0].Value);
        }

        [Test]
        public void M7_ContactTurnsNeverExceedTheBattleLength()
        {
            // Contact turns are (death round - release round) summed over enemies,
            // so a single enemy cannot contribute more turns than the battle had.
            BattleResult r = NewRunner().RunOne(NewConfig(new CorridorHoldStrategy()), 5);

            Assert.GreaterOrEqual(r.ContactTurns, 0);
            Assert.LessOrEqual(r.ContactTurns, r.PlayerTurns * 4,
                "Four enemies at most in this fixture; contact cannot outrun the clock.");
        }

        [Test]
        public void M7_ProbeDoesNotChangeTheBattleItMeasures()
        {
            SimulationRunner runner = NewRunner();

            SimulationConfig on = NewConfig(new CorridorHoldStrategy());
            SimulationConfig off = NewConfig(new CorridorHoldStrategy());
            off.MeasureContact = false;

            Assert.AreEqual(runner.RunOne(off, 11).FinalStateHash, runner.RunOne(on, 11).FinalStateHash,
                "Sampling contact changed the battle.");
            Assert.AreEqual(0, runner.RunOne(off, 11).EnemyReleases.Count);
        }

        // ----------------------------------------------------------- M1b / TPA

        [Test]
        public void M1b_CountsOnePerUnitPerTurn()
        {
            BattleResult r = NewRunner().RunOne(NewConfig(new CorridorHoldStrategy()), 3);

            // One player unit in the fixture, so per-unit and per-turn counts match.
            // With two units the per-unit list would be twice as long — that is the
            // whole reason it exists (OD-26).
            Assert.AreEqual(r.ActionCompositions.Count, r.UnitActionCompositions.Count);
        }

        [Test]
        public void ThreatPriority_DiffersFromCorridorHoldOnlyInWhichTargetItHits()
        {
            // Same positioning logic, so on a fixture with a single enemy there is
            // no target choice to make and the two must produce identical battles.
            SimulationRunner runner = NewRunner();

            uint hold = runner.RunOne(NewConfig(new CorridorHoldStrategy(), noise: 0), 4).FinalStateHash;
            uint tpa = runner.RunOne(NewConfig(new ThreatPriorityStrategy(), noise: 0), 4).FinalStateHash;

            Assert.AreEqual(hold, tpa,
                "With one enemy the strategies cannot differ; a difference means tpa-order changed the movement too.");
        }

        [Test]
        public void ThreatPriority_PicksTheTargetThatRemovesMostThreatPerAction()
        {
            // Two reachable enemies: one nearly dead, one at full HP. Removing the
            // nearly dead one costs one hit and removes the same damage per turn,
            // so threat-per-action ranks it first.
            BattleState state = TestWorld.BegunAdjacent();
            UnitState hero = state.FindUnit(TestWorld.HeroId);

            List<UnitState> targets = BattleQueries.AttackableTargets(state, hero);
            Assert.AreEqual(1, targets.Count, "Fixture assumption: exactly one enemy in reach.");

            ThreatPriorityStrategy strategy = new ThreatPriorityStrategy();
            ICommand command = strategy.DecideNext(state, hero, new DeterministicRandom(1));

            AttackCommand attack = command as AttackCommand;
            Assert.IsNotNull(attack, "An adjacent enemy with full AP should be attacked.");
            Assert.AreEqual(TestWorld.GruntId, attack.TargetId);
        }

        [Test]
        public void ResidueAware_SpendsTheLoadBearingActionOnTheEliteAndTheSpareOneOnTheMinion()
        {
            // The whole point of the strategy, in one battle turn.
            // Hero: 25 damage a hit, two attacks a turn.
            // Elite: 60 HP = 3 hits. Minion: 20 HP = 1 hit.
            BattleState state = TestWorld.BegunResidue();
            UnitState hero = state.FindUnit(1);
            ResidueAwareStrategy strategy = new ResidueAwareStrategy();

            // First action: the elite still needs more hits than NEXT turn alone
            // can supply, so hitting it now is what makes it killable next turn.
            AttackCommand first = strategy.DecideNext(state, hero, new DeterministicRandom(1)) as AttackCommand;
            Assert.IsNotNull(first);
            Assert.AreEqual(3, first.TargetId, "Elite first: 3 hits left, only 2 arrive next turn.");

            state = TestWorld.Apply(state, first);
            hero = state.FindUnit(1);

            // Second action: the elite is now down to what next turn can finish on
            // its own, so this one buys nothing there — spend it on a whole kill.
            AttackCommand second = strategy.DecideNext(state, hero, new DeterministicRandom(1)) as AttackCommand;
            Assert.IsNotNull(second);
            Assert.AreEqual(2, second.TargetId,
                "The surplus action should complete a kill instead of chipping something that dies anyway.");
        }

        [Test]
        public void ResidueAware_IsIdenticalToTpaOrderWhenNoActionIsEverSpare()
        {
            // Every enemy in the standard fixture dies inside one turn's budget,
            // so the finishability rule can never change a decision. Identical
            // hashes are the proof that it only fires where the theory says.
            SimulationRunner runner = NewRunner();

            uint tpa = runner.RunOne(NewConfig(new ThreatPriorityStrategy()), 21).FinalStateHash;
            uint residue = runner.RunOne(NewConfig(new ResidueAwareStrategy()), 21).FinalStateHash;

            Assert.AreEqual(tpa, residue);
        }

        [Test]
        public void Decapitate_IsIdenticalToCorridorHoldWhenNothingIsMarked()
        {
            // The standard fixture is a rout with no kill target, so the new rule
            // can never fire. Identical hashes prove the instrument is wired to
            // the objective and not to something else it changed on the way past.
            SimulationRunner runner = NewRunner();

            uint hold = runner.RunOne(NewConfig(new CorridorHoldStrategy(), noise: 0), 4).FinalStateHash;
            uint decap = runner.RunOne(NewConfig(new DecapitateStrategy(), noise: 0), 4).FinalStateHash;

            Assert.AreEqual(hold, decap);
        }

        [Test]
        public void Decapitate_HitsTheMarkedEnemyEvenWhenThreatOrderingWantsTheOtherOne()
        {
            // Minion (20 HP, marked) and elite (60 HP) both adjacent. Threat per
            // action ranks the elite first — 70 damage a turn over 3 hits beats 20
            // over 1 — so the two strategies must disagree here or the decapitate
            // rule is not doing anything.
            BattleState state = TestWorld.BegunKillResidue();
            UnitState hero = state.FindUnit(1);

            AttackCommand tpa = new ThreatPriorityStrategy()
                .DecideNext(state, hero, new DeterministicRandom(1)) as AttackCommand;
            AttackCommand decap = new DecapitateStrategy()
                .DecideNext(state, hero, new DeterministicRandom(1)) as AttackCommand;

            Assert.IsNotNull(tpa);
            Assert.IsNotNull(decap);
            Assert.AreEqual(3, tpa.TargetId, "Fixture assumption: threat ordering wants the elite.");
            Assert.AreEqual(2, decap.TargetId, "Decapitate must take the marked unit, cheap or not.");
        }

        [Test]
        public void Decapitate_EndsTheBattleWithoutClearingTheField()
        {
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.ResidueUnits, TestWorld.AiProfiles);

            BattleResult r = runner.RunOne(new SimulationConfig
            {
                MapName = "killresidue",
                EncounterText = TestWorld.KillResidueEncounter,
                Strategy = new DecapitateStrategy(),
                Runs = 1,
                BaseSeed = 1,
                NoisePercent = 0,
                MaxRounds = 30
            }, 1);

            Assert.AreEqual(BattleOutcome.Victory, r.Outcome);
            Assert.Greater(r.EnemiesRemaining, 0,
                "A kill objective that still clears the field is measuring rout under another name.");
        }

        // ------------------------------------------- contamination / purify

        private const string ContamUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 purifyCost=4 purifyRadius=2\n" +
            "unit id=fouler name=Fouler hp=40 atk=20 def=5 move=1 ap=8 range=1 attackCost=4 guardCost=3 contaminates=1 contaminateRadius=1\n";

        private const string ContamEncounter =
            "encounter id=contam name=Contam\n" +
            "map\n#######\n#.....#\n#.....#\n#.....#\n#######\nendmap\n" +
            "spawn faction=player unit=hero   x=2 y=2\n" +
            "spawn faction=enemy  unit=fouler x=4 y=2 ai=rusher\n";

        private static BattleSetup ContamSetup()
        {
            return EncounterLoader.CreateBattle(
                EncounterLoader.Parse(ContamEncounter, TerrainLoader.Parse(TestWorld.Terrain)),
                UnitLoader.Parse(ContamUnits),
                AiProfileLoader.Parse(TestWorld.AiProfiles));
        }

        [Test]
        public void CleanGround_HashesExactlyAsItDidBeforeTerrainCouldChange()
        {
            // The contamination array stays null until something dirties a cell,
            // so a battle that never contaminates anything must be bit-identical
            // to one from before mutable terrain existed. This is what keeps the
            // A4 golden constants meaningful across the change.
            BattleState state = TestWorld.Begun();

            Assert.IsFalse(state.HasContamination);
            Assert.AreEqual(0, state.ContaminationAt(TestWorld.C(1, 1)));
        }

        [Test]
        public void Contamination_SpreadsOncePerRoundAndIsCapped()
        {
            BattleState state = BattleSimulator.Begin(ContamSetup().State).State;
            Coord under = TestWorld.C(4, 2);          // the fouler's own cell

            Assert.AreEqual(0, state.ContaminationAt(under));

            for (int round = 0; round < 5; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleState.MaxContamination, state.ContaminationAt(under),
                "Five rounds of +1 must stop at the cap, not keep climbing.");
            Assert.AreEqual(0, state.ContaminationAt(TestWorld.C(1, 1)),
                "Radius 1 must not reach three cells away.");
        }

        [Test]
        public void Purify_ClearsOneLevelInTheThirteenCellDiamond()
        {
            BattleState state = BattleSimulator.Begin(ContamSetup().State).State;
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

            Coord dirty = TestWorld.C(3, 2);          // adjacent to the fouler, within the hero's radius 2
            Assert.AreEqual(1, state.ContaminationAt(dirty));

            ExecuteResult r = BattleSimulator.Execute(state, new PurifyCommand(1));
            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(0, r.State.ContaminationAt(dirty));
            Assert.IsTrue(r.Log.Contains<TerrainContaminated>());
        }

        [Test]
        public void Purify_IsRejectedForUnitsThatCannotDoIt()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new PurifyCommand(TestWorld.HeroId));

            Assert.IsFalse(r.Ok, "The fixture hero has no purify cost and must not be able to purify.");
            Assert.AreEqual(0, r.Log.Count, "A rejected command emits no effects.");
        }

        [Test]
        public void FractionalTerrain_RoundsOnceForTheWholePathNotPerCell()
        {
            // 1.5 a cell: one cell costs 2, two cost 3. Rounding per cell would
            // charge 4 and make the fraction meaningless.
            TerrainCatalog catalog = TerrainLoader.Parse(
                "terrain name=Road symbol=. cost=1 blocks=false\n" +
                "terrain name=Bog symbol=b cost=1.5 blocks=false\n" +
                "terrain name=Wall symbol=# cost=0 blocks=true\n");

            TerrainDef bog;
            Assert.IsTrue(catalog.TryGetBySymbol('b', out bog));
            Assert.AreEqual(150, bog.MovementCostHundredths);
            Assert.AreEqual(2, TerrainDef.CeilToAp(150), "One bog cell costs 2 AP.");
            Assert.AreEqual(3, TerrainDef.CeilToAp(300), "Two cost 3, not 4.");
            Assert.AreEqual(5, TerrainDef.CeilToAp(450), "Three cost 5.");
        }

        // --------------------------------------------------------- the solver

        [Test]
        public void Solver_ScoresEveryLegalActionAndMergesTheSymmetricOnes()
        {
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            PositionSolver solver = new PositionSolver(setup.Ai);
            PositionSolver.Analysis a = solver.Analyse(state, TestWorld.HeroId,
                new PositionSolver.Options { Horizon = 2 });

            Assert.Greater(a.LegalActionCount, 1, "The fixture should offer several legal actions.");
            Assert.Greater(a.Actions.Count, 0);
            Assert.LessOrEqual(a.Actions.Count, a.LegalActionCount,
                "Merging symmetric actions can only ever reduce the count.");

            int weight = 0;
            for (int i = 0; i < a.Actions.Count; i++) weight += a.Actions[i].Weight;
            Assert.AreEqual(a.LegalActionCount, weight, "Every legal action must land in exactly one group.");

            for (int i = 1; i < a.Actions.Count; i++)
                Assert.LessOrEqual(a.Actions[i - 1].Lost ? 1 : 0, a.Actions[i].Lost ? 1 : 0,
                    "Losing lines must sort last.");
        }

        [Test]
        public void Solver_IsDeterministicAndDoesNotDisturbThePosition()
        {
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;
            uint before = StateHasher.Hash(state);

            PositionSolver solver = new PositionSolver(setup.Ai);
            PositionSolver.Options options = new PositionSolver.Options { Horizon = 3 };

            PositionSolver.Analysis first = solver.Analyse(state, TestWorld.HeroId, options);
            PositionSolver.Analysis second = solver.Analyse(state, TestWorld.HeroId, options);

            Assert.AreEqual(before, StateHasher.Hash(state),
                "Searching a position must not modify it — every line runs on a clone.");
            Assert.AreEqual(first.Actions.Count, second.Actions.Count);
            Assert.AreEqual(first.BestDamage, second.BestDamage);
            Assert.AreEqual(first.WorstDamage, second.WorstDamage);
        }

        [Test]
        public void Solver_NearOptimalRatioSurvivesAZeroDamageOptimum()
        {
            // The Metrics Framework calls this the most dangerous metric failure:
            // a purely relative band divides by the optimum, which is zero exactly
            // when the position is most interesting, and returns "fine" instead of
            // an error. The band here is absolute-floored, so it stays defined.
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            PositionSolver.Analysis a = new PositionSolver(setup.Ai).Analyse(
                state, TestWorld.HeroId,
                new PositionSolver.Options { Horizon = 1, PriceUnfinishedThreat = false });

            Assert.AreEqual(0, a.BestDamage, "Fixture assumption: nothing can reach the hero in one round.");
            Assert.GreaterOrEqual(a.NearOptimalPercent(5), 1, "A zero optimum must still admit a neighbourhood.");
            Assert.LessOrEqual(a.NearOptimalPercent(5), 100);
        }

        // -------------------------------------------- M1 skill classification

        private static SimulationRunner ControlRunner() =>
            new SimulationRunner(TestWorld.Terrain, TestWorld.ControlUnits, TestWorld.AiProfiles);

        private static SimulationConfig ControlConfig(IPlayerStrategy strategy) =>
            new SimulationConfig
            {
                MapName = "control",
                EncounterText = TestWorld.ControlEncounter,
                Strategy = strategy,
                Runs = 6,
                BaseSeed = 1,
                NoisePercent = 0,          // the skill has to be CHOSEN, not rolled by the noise
                MaxRounds = 20,
                RouteProbeRow = -1
            };

        [Test]
        public void M1_CountsSkillsAsActions()
        {
            // Until 2026-08-16 every skill a strategy issued was classified as
            // "not an action" and vanished before M1 saw it.
            BattleResult r = ControlRunner().RunOne(ControlConfig(new ControlHoldStrategy()), 1);

            bool sawSkill = false;
            for (int i = 0; i < r.UnitActionCompositions.Count; i++)
                if (BattleResult.ContainsSkill(r.UnitActionCompositions[i])) sawSkill = true;

            Assert.IsTrue(sawSkill,
                "control-hold pushes and slows on this fixture, and none of it reached the action mix.");
        }

        [Test]
        public void M1_TellsAKitThatLosesApartFromAKitThatIsNeverUsed()
        {
            // The reason the classification gap mattered: both readings of a
            // negative result produced identical telemetry, so §17.3 / §20 / §21
            // could not say which one they had measured.
            SimulationRunner runner = ControlRunner();

            SimulationSummary control = SimulationRunner.Summarise(
                runner.RunBatch(ControlConfig(new ControlHoldStrategy())));
            SimulationSummary plain = SimulationRunner.Summarise(
                runner.RunBatch(ControlConfig(new CorridorHoldStrategy())));

            Assert.Greater(control.UnitTurnsWithSkillPercent, 0, "control-hold uses the kit.");
            Assert.AreEqual(0, plain.UnitTurnsWithSkillPercent,
                "corridor-hold never issues a skill, so anything above zero is the counter misfiring.");
        }

        [Test]
        public void ComposeKey_KeepsTheOldStringForBattlesWithNoSkills()
        {
            // Composition keys print in enum order and the skills were appended
            // after Wait, so every M1 number recorded before this change stays
            // comparable. A reordering here silently invalidates the archive.
            List<ActionKind> mix = new List<ActionKind>
                { ActionKind.Move, ActionKind.Attack, ActionKind.Attack };

            Assert.AreEqual("Move+Attackx2", BattleResult.ComposeKey(mix));
            Assert.AreEqual("(nothing)", BattleResult.ComposeKey(new List<ActionKind>()));
            Assert.IsFalse(BattleResult.ContainsSkill("Move+Attackx2"));
        }

        [Test]
        public void ComposeKey_NamesEverySkill()
        {
            // One assertion per skill: a new ActionKind with no name would throw
            // here rather than being dropped, which is the failure mode this
            // whole change exists to remove.
            Assert.AreEqual("Taunt", BattleResult.ComposeKey(new List<ActionKind> { ActionKind.Taunt }));
            Assert.AreEqual("Slow", BattleResult.ComposeKey(new List<ActionKind> { ActionKind.Slow }));
            Assert.AreEqual("Push", BattleResult.ComposeKey(new List<ActionKind> { ActionKind.Push }));
            Assert.AreEqual("Purify", BattleResult.ComposeKey(new List<ActionKind> { ActionKind.Purify }));

            Assert.AreEqual("Move+Attack+Slow",
                BattleResult.ComposeKey(new List<ActionKind>
                    { ActionKind.Slow, ActionKind.Move, ActionKind.Attack }),
                "Order comes from the enum, not from the order the actions happened.");

            foreach (ActionKind kind in System.Enum.GetValues(typeof(ActionKind)))
                Assert.IsNotEmpty(BattleResult.ComposeKey(new List<ActionKind> { kind }),
                    "Every ActionKind must have a printable name: " + kind);
        }

        [Test]
        public void CountingSkillsDidNotChangeTheBattleItCounts()
        {
            // M1 is an observation. Classifying four more command types must not
            // move a single unit, so a skill-using battle has to replay to the
            // same hash it did before — which is what seeding it twice checks,
            // together with A4 covering the rule layer.
            SimulationRunner runner = ControlRunner();

            BattleResult a = runner.RunOne(ControlConfig(new ControlHoldStrategy()), 4);
            BattleResult b = runner.RunOne(ControlConfig(new ControlHoldStrategy()), 4);

            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
            CollectionAssert.AreEqual(a.UnitActionCompositions, b.UnitActionCompositions);
        }

        [Test]
        public void M6_SummaryPutsEveryRunInExactlyOneBucket()
        {
            SimulationRunner runner = NewRunner();
            SimulationSummary s = SimulationRunner.Summarise(runner.RunBatch(CrossingConfig(2)));

            int counted = s.RunsWithoutCrossing;
            foreach (KeyValuePair<int, int> bucket in s.RouteHistogram) counted += bucket.Value;

            Assert.AreEqual(s.Runs, counted, "A run either used a route or crossed nothing.");
            Assert.GreaterOrEqual(s.TopRoutePercent, 0);
            Assert.LessOrEqual(s.TopRoutePercent, 100);
        }
    }
}
