using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// Occupancy and clash heatmaps.
    ///
    /// The rule tests below drive BattleSimulator directly and hand the resulting
    /// logs to the observer, so every expected cell count is arithmetic a reader
    /// can check by hand rather than the output of a battle nobody watched.
    /// </summary>
    public class SpatialHeatmapTests
    {
        /// <summary>
        /// Both sides carry a counter, so ONE attack damages TWO units in two
        /// different cells. That is this project's only multi-victim action —
        /// no skill damages several targets yet — and it is the shape the clash
        /// rule has to get right.
        ///
        /// Hero deals 30-5 = 25, Grunt counters for 20-10 = 10. Grunt has 40 HP,
        /// so two hits kill it and "the dead occupy nothing" is testable.
        /// </summary>
        private const string CounterUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 " +
                "counterCost=3 pushCost=2 pushRange=1\n" +
            "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3 " +
                "counterCost=3\n";

        /// <summary>
        ///   0123456
        /// 0 #######
        /// 1 #.....#
        /// 2 #.....#   hero (2,2), grunt (3,2) — adjacent, all road so every
        /// 3 #.....#   movement cost is 1 and the arithmetic stays in the test
        /// 4 #######
        /// </summary>
        private const string CounterEncounter =
            "encounter id=heat name=Heat\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.....#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero  x=2 y=2\n" +
            "spawn faction=enemy  unit=grunt x=3 y=2 ai=rusher\n";

        private const int HeroId = 1;
        private const int GruntId = 2;

        private static BattleState BegunCounter()
        {
            BattleSetup setup = EncounterLoader.CreateBattle(
                EncounterLoader.Parse(CounterEncounter, TerrainLoader.Parse(TestWorld.Terrain)),
                UnitLoader.Parse(CounterUnits),
                AiProfileLoader.Parse(TestWorld.AiProfiles));
            return BattleSimulator.Begin(setup.State).State;
        }

        /// <summary>Executes a command and shows the observer exactly what the runner would.</summary>
        private static BattleState Feed(HeatmapObserver observer, int round, BattleState state, ICommand command)
        {
            ExecuteResult result = BattleSimulator.Execute(state, command);
            observer.PlayerCommand(round, state, command, result);
            return result.Ok ? result.State : state;
        }

        private static Coord C(int x, int y) => new Coord(x, y);

        // ------------------------------------------------------------ occupancy

        [Test]
        public void Occupancy_CountsEveryLivingUnitOncePerRound()
        {
            // Two rounds, hand-computed:
            //   round 1  hero (2,2), grunt (3,2)
            //   round 2  hero moved to (1,2), grunt still (3,2)
            // so (2,2)=1, (1,2)=1, (3,2)=2 — and nothing else.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            observer.RoundStarted(1, state);
            observer.RoundEnded(1, state);

            state = Feed(observer, 2, state, new MoveCommand(HeroId, new[] { C(1, 2) }));
            observer.RoundEnded(2, state);

            BattleHeatmap heat = observer.Heatmap;

            Assert.AreEqual(1, heat.Occupancy[2, 2], "Hero stood here for round 1 only.");
            Assert.AreEqual(1, heat.Occupancy[1, 2], "Hero stood here for round 2 only.");
            Assert.AreEqual(2, heat.Occupancy[3, 2], "The grunt never moved, so it counts in both rounds.");
            Assert.AreEqual(4, heat.Occupancy.Total, "Two units x two rounds and not one sample more.");
            Assert.AreEqual(0, heat.OutOfBoundsSamples);
        }

        [Test]
        public void Occupancy_CountsBothFactions()
        {
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();
            observer.RoundEnded(1, state);

            Assert.AreEqual(1, observer.Heatmap.Occupancy[2, 2], "Player unit.");
            Assert.AreEqual(1, observer.Heatmap.Occupancy[3, 2], "Enemy unit.");
        }

        [Test]
        public void Occupancy_CountsAUnitOncePerRoundHoweverManyActionsItTook()
        {
            // Four commands in one round still add one sample. Counting per action
            // would turn this into a map of who was busy, not of where people were.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            state = Feed(observer, 1, state, new MoveCommand(HeroId, new[] { C(2, 1) }));
            state = Feed(observer, 1, state, new MoveCommand(HeroId, new[] { C(1, 1) }));
            state = Feed(observer, 1, state, new MoveCommand(HeroId, new[] { C(1, 2) }));
            state = Feed(observer, 1, state, new GuardCommand(HeroId));
            observer.RoundEnded(1, state);

            Assert.AreEqual(1, observer.Heatmap.Occupancy[1, 2], "One sample, at the cell it ended the round on.");
            Assert.AreEqual(0, observer.Heatmap.Occupancy[2, 1], "Cells merely passed through are not occupancy.");
            Assert.AreEqual(2, observer.Heatmap.Occupancy.Total, "Hero once, grunt once.");
        }

        [Test]
        public void Occupancy_IgnoresTheDead()
        {
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            // 25 a hit against 40 HP: two hits, one turn's worth of AP.
            state = Feed(observer, 1, state, new AttackCommand(HeroId, GruntId));
            state = Feed(observer, 1, state, new AttackCommand(HeroId, GruntId));
            Assert.IsFalse(state.FindUnit(GruntId).IsAlive, "Fixture assumption: two hits kill the grunt.");

            observer.RoundEnded(1, state);

            Assert.AreEqual(1, observer.Heatmap.Occupancy[2, 2], "The living hero still counts.");
            Assert.AreEqual(0, observer.Heatmap.Occupancy[3, 2], "A corpse occupies nothing.");
        }

        // ---------------------------------------------------------------- clash

        [Test]
        public void Clash_CountsOneDamagingEffectPerVictimWhereTheVictimStood()
        {
            // One AttackCommand, two victims: the grunt takes 25 at (3,2) and the
            // counter puts 10 back on the hero at (2,2). Each is credited to its
            // own cell — the attacker's cell is not where "an attack happened".
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            ExecuteResult result = BattleSimulator.Execute(state, new AttackCommand(HeroId, GruntId));
            Assert.IsTrue(result.Log.Contains<CounterAttacked>(),
                "Fixture assumption: the grunt counters, so this action has two victims.");

            observer.PlayerCommand(1, state, new AttackCommand(HeroId, GruntId), result);

            Assert.AreEqual(1, observer.Heatmap.Clash[3, 2], "Where the grunt was hit.");
            Assert.AreEqual(1, observer.Heatmap.Clash[2, 2], "Where the counter hit the hero.");
            Assert.AreEqual(2, observer.Heatmap.Clash.Total);
        }

        [Test]
        public void Clash_IgnoresRejectedActions()
        {
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            // Into the wall: rejected, and a rejected command emits no effects.
            ExecuteResult rejected = BattleSimulator.Execute(
                state, new MoveCommand(HeroId, new[] { C(2, 0) }));
            Assert.IsFalse(rejected.Ok, "Fixture assumption: (2,0) is blocking terrain.");

            observer.PlayerCommand(1, state, new MoveCommand(HeroId, new[] { C(2, 0) }), rejected);

            Assert.AreEqual(0, observer.Heatmap.Clash.Total, "A refused action is not a clash.");
            Assert.AreEqual(0, observer.Heatmap.Occupancy.Total, "...and it is not occupancy either.");
        }

        [Test]
        public void Clash_IgnoresActionsThatDealNoDamage()
        {
            // Guard, Move and Push all succeed and none of them emits an HpChanged.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            state = Feed(observer, 1, state, new GuardCommand(HeroId));
            state = Feed(observer, 1, state, new PushCommand(HeroId, GruntId));
            state = Feed(observer, 1, state, new MoveCommand(HeroId, new[] { C(1, 2) }));

            Assert.AreEqual(C(4, 2), state.FindUnit(GruntId).Position,
                "Fixture assumption: the push landed, so a successful skill really did run.");
            Assert.AreEqual(0, observer.Heatmap.Clash.Total,
                "Actions that moved and shoved but dealt no damage must not appear.");
        }

        [Test]
        public void Clash_IgnoresHealing()
        {
            // Rest emits HpChanged with a POSITIVE delta. The trigger is damage,
            // not "HP changed".
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            state = Feed(observer, 1, state, new AttackCommand(HeroId, GruntId));
            long afterDamage = observer.Heatmap.Clash.Total;

            ExecuteResult heal = BattleSimulator.Execute(state, new RestCommand(HeroId));
            if (heal.Ok && heal.Log.Contains<HpChanged>())
            {
                observer.PlayerCommand(1, state, new RestCommand(HeroId), heal);
                Assert.AreEqual(afterDamage, observer.Heatmap.Clash.Total, "Healing is not a clash.");
            }
        }

        [Test]
        public void Clash_CreditsTheCellTheVictimMovedTo_NotTheOneThePhaseStartedIn()
        {
            // The reason positions are tracked through a log instead of read off
            // one state: inside the enemy phase a unit moves and is then hit, and
            // the hit belongs where it ended up.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            // Push the grunt from (3,2) to (4,2), then hit it there.
            state = Feed(observer, 1, state, new PushCommand(HeroId, GruntId));
            Assert.AreEqual(C(4, 2), state.FindUnit(GruntId).Position);

            state = Feed(observer, 1, state, new MoveCommand(HeroId, new[] { C(3, 2) }));
            state = Feed(observer, 1, state, new AttackCommand(HeroId, GruntId));

            // BOTH units moved before damage landed, and both are credited where
            // they ended up: the grunt was pushed to (4,2) and hit there, and the
            // counter caught the hero at (3,2), the cell it had just walked into.
            Assert.AreEqual(1, observer.Heatmap.Clash[4, 2], "The grunt was hit at its NEW cell.");
            Assert.AreEqual(1, observer.Heatmap.Clash[3, 2], "The counter caught the hero at ITS new cell.");
            Assert.AreEqual(0, observer.Heatmap.Clash[2, 2],
                "Neither was hit at the cell it started the round on — that is the bug this guards.");
        }

        [Test]
        public void Clash_CountsDamageFromTheEnemyPhaseToo()
        {
            // Damage arriving through PhaseResolved has to be counted the same
            // way: the enemy phase is where most of it happens.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            EffectLog log = new EffectLog();
            BattleSetup setup = EncounterLoader.CreateBattle(
                EncounterLoader.Parse(CounterEncounter, TerrainLoader.Parse(TestWorld.Terrain)),
                UnitLoader.Parse(CounterUnits),
                AiProfileLoader.Parse(TestWorld.AiProfiles));

            BattleState before = state;
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;
            observer.PhaseResolved(1, Faction.Enemy, before, log, after);

            // Expectations are read out of the log rather than predicted from AP
            // arithmetic: the grunt affords two attacks and the hero counters once
            // per round, and the point being tested is the mapping from victim to
            // cell, not how many swings the economy pays for.
            int hitsOnHero = 0, hitsOnGrunt = 0;
            for (int i = 0; i < log.Count; i++)
            {
                HpChanged hp = log[i] as HpChanged;
                if (hp == null || hp.Delta >= 0) continue;
                if (hp.UnitId == HeroId) hitsOnHero++;
                else if (hp.UnitId == GruntId) hitsOnGrunt++;
            }

            Assert.Greater(hitsOnHero, 0, "Fixture assumption: the grunt closes and swings.");
            Assert.AreEqual(hitsOnHero, observer.Heatmap.Clash[2, 2],
                "Every hit on the hero belongs to the hero's cell.");
            Assert.AreEqual(hitsOnGrunt, observer.Heatmap.Clash[3, 2],
                "Every counter on the grunt belongs to the grunt's cell.");
        }

        // ------------------------------------------------------------------ map

        [Test]
        public void Grid_TakesItsSizeFromTheMapThatWasPlayed()
        {
            // Two fixtures of different sizes, no constant anywhere in between.
            HeatmapObserver small = new HeatmapObserver();
            small.RoundEnded(1, BegunCounter());

            HeatmapObserver wide = new HeatmapObserver();
            wide.RoundEnded(1, TestWorld.BegunControl());

            Assert.AreEqual(7, small.Heatmap.Width);
            Assert.AreEqual(5, small.Heatmap.Height);
            Assert.AreEqual(10, wide.Heatmap.Width);
            Assert.AreEqual(5, wide.Heatmap.Height);

            BattleState state = BegunCounter();
            Assert.AreEqual(state.Map.Width, small.Heatmap.Width, "Width must come from the map, not a constant.");
            Assert.AreEqual(state.Map.Height, small.Heatmap.Height);
        }

        [Test]
        public void Grid_NeitherClampsNorWrapsACoordinateOffTheMap()
        {
            // Folding a stray coordinate onto an edge would hide the fault and
            // quietly corrupt the picture, so Add refuses and says so.
            SpatialGrid grid = new SpatialGrid(7, 5);

            Assert.IsFalse(grid.Add(-1, 2));
            Assert.IsFalse(grid.Add(7, 2));
            Assert.IsFalse(grid.Add(2, -1));
            Assert.IsFalse(grid.Add(2, 5));

            Assert.AreEqual(0, grid.Total, "Nothing may be counted anywhere.");
            Assert.AreEqual(0, grid[0, 2], "Not clamped onto the near edge.");
            Assert.AreEqual(0, grid[6, 2], "Not wrapped onto the far edge.");
            Assert.AreEqual(0, grid[-1, 2], "Reading out of bounds returns zero rather than throwing.");
        }

        [Test]
        public void ARealBattleNeverProducesAnOutOfBoundsSample()
        {
            // If this ever fails, something is placing units off the map and the
            // counter is how it becomes visible.
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            HeatmapObserver observer = new HeatmapObserver();
            SimulationConfig config = HeatmapConfig(observer);
            for (int seed = 1; seed <= 8; seed++) runner.RunOne(config, seed);

            Assert.AreEqual(0, observer.Heatmap.OutOfBoundsSamples);
            Assert.Greater(observer.Heatmap.Occupancy.Total, 0, "The observer saw nothing at all.");
        }

        [Test]
        public void BlockedCellsNeverAccumulate()
        {
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            HeatmapObserver observer = new HeatmapObserver();
            SimulationConfig config = HeatmapConfig(observer);
            for (int seed = 1; seed <= 8; seed++) runner.RunOne(config, seed);

            BattleHeatmap heat = observer.Heatmap;
            for (int y = 0; y < heat.Height; y++)
            {
                for (int x = 0; x < heat.Width; x++)
                {
                    if (heat.Map.IsPassable(C(x, y))) continue;
                    Assert.AreEqual(0, heat.Occupancy[x, y], "Something stood on blocking terrain at " + C(x, y));
                    Assert.AreEqual(0, heat.Clash[x, y], "Something was damaged on blocking terrain at " + C(x, y));
                }
            }
        }

        // ------------------------------------------------------------ rendering

        [Test]
        public void Render_UsesDotForZeroDigitsForOneToNineAndStarForTenPlus()
        {
            BattleHeatmap heat = new BattleHeatmap(BegunCounter().Map);

            heat.Occupancy.Add(1, 1);                                   // 1
            for (int i = 0; i < 9; i++) heat.Occupancy.Add(2, 1);       // 9
            for (int i = 0; i < 10; i++) heat.Occupancy.Add(3, 1);      // 10 -> *
            for (int i = 0; i < 250; i++) heat.Occupancy.Add(4, 1);     // still *

            Assert.AreEqual("#19**.#", heat.RenderRow(heat.Occupancy, 1),
                "1 prints as '1', 9 as '9', 10 and 250 both as '*', and an untouched cell as '.'.");
            Assert.AreEqual("#.....#", heat.RenderRow(heat.Occupancy, 2),
                "An untouched row is all dots between the walls.");

            StringAssert.Contains("#19**.#", heat.Render(),
                "The decorated report must carry the same row the API produces.");
        }

        [Test]
        public void Render_ShowsBlockingTerrainAsHash()
        {
            BattleHeatmap heat = new BattleHeatmap(BegunCounter().Map);

            Assert.AreEqual("#######", heat.RenderRow(heat.Occupancy, 0), "The whole top edge is blocking.");
            Assert.AreEqual("#######", heat.RenderRow(heat.Occupancy, 4), "...and so is the bottom.");
            Assert.AreEqual("#.....#", heat.RenderRow(heat.Occupancy, 1));
        }

        [Test]
        public void Render_KeepsTheMapsOrientationRowZeroFirst()
        {
            // Row 0 is the TOP row in an encounter file, so the heatmap has to
            // print it first or it cannot be laid beside the map it describes.
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();

            observer.RoundEnded(1, state);                               // hero (2,2), grunt (3,2)
            state = Feed(observer, 2, state, new MoveCommand(HeroId, new[] { C(1, 2) }));
            observer.RoundEnded(2, state);                               // hero (1,2), grunt (3,2)

            BattleHeatmap heat = observer.Heatmap;

            Assert.AreEqual("#######", heat.RenderRow(heat.Occupancy, 0));
            Assert.AreEqual("#.....#", heat.RenderRow(heat.Occupancy, 1), "Nothing ever stood on row 1.");
            Assert.AreEqual("#112..#", heat.RenderRow(heat.Occupancy, 2),
                "x is the column and y is the row: (1,2)=1, (2,2)=1, (3,2)=2.");
            Assert.AreEqual("#.....#", heat.RenderRow(heat.Occupancy, 3));

            // The row order in the report must match the row order in the API.
            string text = heat.Render();
            Assert.Less(text.IndexOf("#112..#", System.StringComparison.Ordinal),
                        text.IndexOf("Clash heatmap", System.StringComparison.Ordinal),
                        "Occupancy prints before Clash.");
        }

        [Test]
        public void Render_PrintsBothGridsAndDoesNotFlattenThem()
        {
            HeatmapObserver observer = new HeatmapObserver();
            BattleState state = BegunCounter();
            observer.RoundEnded(1, state);

            BattleHeatmap heat = observer.Heatmap;
            string text = heat.Render();

            StringAssert.Contains("Occupancy heatmap", text);
            StringAssert.Contains("Clash heatmap", text);

            // Every row is exactly Width characters and there are Height of them,
            // twice — a flattened grid could not satisfy that.
            for (int y = 0; y < heat.Height; y++)
            {
                Assert.AreEqual(heat.Width, heat.RenderRow(heat.Occupancy, y).Length);
                Assert.AreEqual(heat.Width, heat.RenderRow(heat.Clash, y).Length);
                StringAssert.Contains(heat.RenderRow(heat.Occupancy, y), text);
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            Assert.GreaterOrEqual(lines.Length, heat.Height * 2,
                "Two grids of " + heat.Height + " rows cannot fit in fewer lines.");
        }

        // ------------------------------------------------------------ merging

        [Test]
        public void Merge_AddsCellsAndRefusesADifferentMap()
        {
            BattleHeatmap a = new BattleHeatmap(BegunCounter().Map);
            BattleHeatmap b = new BattleHeatmap(BegunCounter().Map);

            a.Occupancy.Add(1, 1);
            b.Occupancy.Add(1, 1);
            b.Occupancy.Add(2, 1);
            b.BattlesObserved = 3;
            a.BattlesObserved = 1;

            a.Merge(b);

            Assert.AreEqual(2, a.Occupancy[1, 1]);
            Assert.AreEqual(1, a.Occupancy[2, 1]);
            Assert.AreEqual(4, a.BattlesObserved);

            BattleHeatmap other = new BattleHeatmap(TestWorld.BegunControl().Map);
            Assert.Throws<System.InvalidOperationException>(() => a.Merge(other),
                "Merging two different maps would silently produce a picture of neither.");
        }

        // -------------------------------------------------------- determinism

        private static SimulationConfig HeatmapConfig(HeatmapObserver observer) =>
            new SimulationConfig
            {
                MapName = "fixture",
                EncounterText = TestWorld.Encounter,
                Strategy = new CorridorHoldStrategy(),
                Runs = 6,
                BaseSeed = 1,
                NoisePercent = 15,
                MaxRounds = 30,
                Observer = observer
            };

        [Test]
        public void SameSeedProducesTheSameHeatmap()
        {
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            HeatmapObserver first = new HeatmapObserver();
            runner.RunOne(HeatmapConfig(first), 12345);

            HeatmapObserver second = new HeatmapObserver();
            runner.RunOne(HeatmapConfig(second), 12345);

            Assert.AreEqual(first.Heatmap.Render(), second.Heatmap.Render());
            Assert.AreEqual(first.Heatmap.Occupancy.Total, second.Heatmap.Occupancy.Total);
            Assert.AreEqual(first.Heatmap.Clash.Total, second.Heatmap.Clash.Total);
        }

        [Test]
        public void AttachingHeatmapObserverDoesNotChangeBattle()
        {
            // The same property AttachingAnObserverDoesNotChangeTheBattle asserts
            // for the transcript. Counting cells reads states and consumes no
            // random numbers, so the hash must be identical with and without.
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            SimulationConfig plain = HeatmapConfig(null);
            plain.Observer = null;

            for (int seed = 1; seed <= 10; seed++)
            {
                BattleResult without = runner.RunOne(plain, seed);
                BattleResult with = runner.RunOne(HeatmapConfig(new HeatmapObserver()), seed);

                Assert.AreEqual(without.FinalStateHash, with.FinalStateHash,
                    "The heatmap observer changed the battle at seed " + seed + ".");
                Assert.AreEqual(without.Outcome, with.Outcome);
                Assert.AreEqual(without.Turns, with.Turns);
                Assert.AreEqual(without.FinalPlayerHp, with.FinalPlayerHp);
            }
        }

        [Test]
        public void OneObserverAccumulatesAcrossAWholeBatch()
        {
            // How a batch cell is measured: one observer for every seed, so the
            // grids add up without allocating a grid per battle.
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            HeatmapObserver observer = new HeatmapObserver();
            SimulationConfig config = HeatmapConfig(observer);
            List<BattleResult> results = runner.RunBatch(config);

            Assert.AreEqual(results.Count, observer.Heatmap.BattlesObserved);

            long expectedRounds = 0;
            for (int i = 0; i < results.Count; i++) expectedRounds += results[i].PlayerTurns;

            Assert.Greater(observer.Heatmap.Occupancy.Total, 0);
            Assert.LessOrEqual(observer.Heatmap.Occupancy.Total, expectedRounds * 5,
                "At most one sample per living unit per round.");
        }

        [Test]
        public void RoundEndIsObservedOncePerRoundIncludingTheOneTheBattleEndsIn()
        {
            // The final round exits the loop through a break. If it were skipped,
            // the round where everything was decided would be missing from the map.
            CountingObserver counter = new CountingObserver();
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            SimulationConfig config = HeatmapConfig(null);
            config.Observer = counter;
            BattleResult result = runner.RunOne(config, 4);

            Assert.AreEqual(result.PlayerTurns, counter.RoundEndings,
                "One round-end per round played, no more and no fewer.");
            CollectionAssert.AllItemsAreUnique(counter.RoundsSeen, "A round was reported twice.");
        }

        // ------------------------------------------------- batch integration

        private static BatchOutput RunSmallBatch()
        {
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            List<MapEntry> maps = new List<MapEntry> { new MapEntry("fixture", TestWorld.Encounter) };
            List<IPlayerStrategy> strategies = new List<IPlayerStrategy>
                { new CorridorHoldStrategy(), new AggressiveStrategy() };

            return MetricsBatch.Run(runner, maps, strategies, 4, 1, 15, 30);
        }

        [Test]
        public void Batch_AttachesAHeatmapPerCellAndRendersItInTheTextReport()
        {
            BatchOutput output = RunSmallBatch();

            Assert.AreEqual(2, output.Cells.Count);
            for (int i = 0; i < output.Cells.Count; i++)
            {
                Assert.IsNotNull(output.Cells[i].Heatmap, "Every cell should carry its own heatmap.");
                Assert.AreEqual(4, output.Cells[i].Heatmap.BattlesObserved,
                    "The cell's heatmap covers every seed in the cell.");
            }

            StringAssert.Contains("Occupancy heatmap", output.Summary);
            StringAssert.Contains("Clash heatmap", output.Summary);
        }

        [Test]
        public void Batch_LeavesTheCsvAlone()
        {
            // The heatmap is diagnostic output. Existing columns and their meaning
            // are what every recorded metric was read from, so the CSV must not
            // notice this feature at all.
            BatchOutput output = RunSmallBatch();
            string[] lines = output.RawCsv.Trim().Replace("\r\n", "\n").Split('\n');

            Assert.AreEqual(
                "map,strategy,seed,outcome,turns,hit_round_cap,player_turns,ap_granted,ap_unused," +
                "final_player_hp,enemies_remaining,mean_exposure_x100,first_crossing_x," +
                "unit_crossings,contact_round1,contact_turns,never_in_contact,state_hash",
                lines[0], "The CSV header changed.");

            Assert.AreEqual(output.AllResults.Count + 1, lines.Length, "Header plus one row per battle.");
            StringAssert.DoesNotContain("heatmap", output.RawCsv, "The grids must not leak into the CSV.");
            StringAssert.DoesNotContain("Occupancy", output.RawCsv);
        }

        [Test]
        public void Batch_IsDeterministicIncludingItsHeatmaps()
        {
            BatchOutput first = RunSmallBatch();
            BatchOutput second = RunSmallBatch();

            Assert.AreEqual(first.Summary, second.Summary, "The whole text report, heatmaps included.");
            Assert.AreEqual(first.RawCsv, second.RawCsv);

            for (int i = 0; i < first.Cells.Count; i++)
            {
                Assert.AreEqual(first.Cells[i].Heatmap.Render(), second.Cells[i].Heatmap.Render());
                Assert.AreEqual(first.Cells[i].Heatmap.Occupancy.Total, second.Cells[i].Heatmap.Occupancy.Total);
                Assert.AreEqual(first.Cells[i].Heatmap.Clash.Total, second.Cells[i].Heatmap.Clash.Total);
            }
        }

        [Test]
        public void Batch_DoesNotChangeTheBattlesItNowWatches()
        {
            // The heatmap observer is attached to every cell of every batch, so
            // the whole existing metric archive rests on this staying true.
            SimulationRunner runner = new SimulationRunner(
                TestWorld.Terrain, TestWorld.Units, TestWorld.AiProfiles);

            SimulationConfig unwatched = new SimulationConfig
            {
                MapName = "fixture",
                EncounterText = TestWorld.Encounter,
                Strategy = new CorridorHoldStrategy(),
                Runs = 4,
                BaseSeed = 1,
                NoisePercent = 15,
                MaxRounds = 30
            };

            List<BattleResult> plain = runner.RunBatch(unwatched);
            BatchOutput watched = RunSmallBatch();

            for (int i = 0; i < plain.Count; i++)
            {
                Assert.AreEqual(plain[i].FinalStateHash, watched.AllResults[i].FinalStateHash,
                    "Watching a batch changed it at index " + i + ".");
            }
        }

        [Test]
        public void Batch_DoesNotTouchTheM6RouteReporting()
        {
            // TopRoutePercent is a known per-x reporting bug held for a separate
            // task. This asserts the heatmap work left its semantics exactly where
            // it found them, on a map whose probe is off.
            BatchOutput output = RunSmallBatch();

            for (int i = 0; i < output.Cells.Count; i++)
            {
                Assert.AreEqual(-1, output.Cells[i].RouteProbeRow, "Fixture has no dividing wall.");
                Assert.AreEqual(0, output.Cells[i].TopRoutePercent);
                Assert.AreEqual(-1, output.Cells[i].TopRouteX);
                Assert.AreEqual(0, output.Cells[i].RunsWithoutCrossing);
            }
        }

        private sealed class CountingObserver : IBattleObserver
        {
            public int RoundEndings;
            public readonly List<int> RoundsSeen = new List<int>();

            public void RoundStarted(int round, BattleState state) { }
            public void PlayerCommand(int round, BattleState before, ICommand command, ExecuteResult result) { }
            public void PhaseResolved(int round, Faction phase, BattleState before, EffectLog log, BattleState after) { }
            public void BattleFinished(BattleResult result, BattleState finalState) { }

            public void RoundEnded(int round, BattleState state)
            {
                RoundEndings++;
                RoundsSeen.Add(round);
            }
        }
    }
}
