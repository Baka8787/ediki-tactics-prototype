using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The measurement instruments and the four test-paper encounters.
    ///
    /// An instrument's whole job is to spend AP on ONE action so the encounter
    /// can be asked what that action is worth. If an instrument drifts into
    /// doing something else — guarding, retreating, picking clever targets —
    /// the number it produces stops being about the action.
    /// </summary>
    public class InstrumentTests
    {
        private const string Units =
            "unit id=kit   name=Kit   hp=200 atk=30 def=10 move=4 ap=10 apRegen=8 range=2 attackCost=4 guardCost=3 " +
                "pushCost=3 pushRange=1 slowCost=3 slowRange=3 tauntCost=3 tauntRadius=3\n" +
            "unit id=bare  name=Bare  hp=200 atk=30 def=10 move=4 ap=10 apRegen=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=rock  name=Rock  hp=200 atk=20 def=5  move=3 ap=10 apRegen=8 range=1 attackCost=4 guardCost=3 " +
                "immuneToPush=true\n" +
            "unit id=grunt name=Grunt hp=200 atk=20 def=5  move=3 ap=10 apRegen=8 range=1 attackCost=4 guardCost=3\n";

        private static string Encounter(string player, string enemy, int ex, int ey) =>
            "encounter id=inst name=Inst\n" +
            "map\n#########\n#.......#\n#.......#\n#.......#\n#########\nendmap\n" +
            "spawn faction=player unit=" + player + " x=2 y=2\n" +
            "spawn faction=enemy  unit=" + enemy + " x=" + ex + " y=" + ey + " ai=rusher\n";

        private const int PlayerId = 1;
        private const int EnemyId = 2;

        private static BattleState Begun(string player, string enemy, int ex = 3, int ey = 2)
        {
            BattleSetup setup = EncounterLoader.CreateBattle(
                EncounterLoader.Parse(Encounter(player, enemy, ex, ey), TerrainLoader.Parse(TestWorld.Terrain)),
                UnitLoader.Parse(Units), AiProfileLoader.Parse(TestWorld.AiProfiles));
            return BattleSimulator.Begin(setup.State).State;
        }

        private static ICommand Decide(IPlayerStrategy s, BattleState state) =>
            s.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));

        // ---------------------------------------------------- each instrument

        [Test]
        public void AttackOnly_SwingsWhenItCanAndClosesWhenItCannot()
        {
            AttackOnlyStrategy s = new AttackOnlyStrategy();

            Assert.IsInstanceOf<AttackCommand>(Decide(s, Begun("bare", "grunt")),
                "Adjacent enemy, full AP: it attacks.");
            Assert.IsInstanceOf<MoveCommand>(Decide(s, Begun("bare", "grunt", 7, 2)),
                "Out of reach: it closes rather than standing still.");
        }

        [Test]
        public void AttackOnly_NeverGuardsRestsOrUsesASkill()
        {
            // The damage floor has to stay a damage floor, or every instrument
            // read against it is measuring something else.
            BattleState state = Begun("kit", "grunt");
            AttackOnlyStrategy s = new AttackOnlyStrategy();

            for (int step = 0; step < 10; step++)
            {
                ICommand c = Decide(s, state);
                Assert.IsFalse(c is GuardCommand || c is RestCommand
                               || c is PushCommand || c is SlowCommand || c is TauntCommand,
                               "attack-only produced " + c.GetType().Name);
                if (c is WaitCommand) break;
                state = TestWorld.Apply(state, c);
            }
        }

        [Test]
        public void PushInstrument_PushesFirstThenFallsBackToAttacking()
        {
            Assert.IsInstanceOf<PushCommand>(Decide(new PushInstrumentStrategy(), Begun("kit", "grunt")),
                "An adjacent, pushable enemy is shoved before it is hit.");
        }

        [Test]
        public void PushInstrument_SkipsAnAnchoredEnemyRatherThanWastingTheTurn()
        {
            // immuneToPush is what stops Push being universally applicable, and
            // the instrument must fall through instead of emitting a command the
            // simulator would refuse.
            Assert.IsInstanceOf<AttackCommand>(Decide(new PushInstrumentStrategy(), Begun("kit", "rock")));
        }

        [Test]
        public void SlowInstrument_SlowsAnUnslowedTargetAndOnlyOnce()
        {
            BattleState state = Begun("kit", "grunt");
            SlowInstrumentStrategy s = new SlowInstrumentStrategy();

            ICommand first = Decide(s, state);
            Assert.IsInstanceOf<SlowCommand>(first);

            state = TestWorld.Apply(state, first);
            Assert.IsTrue(state.IsSlowed(state.FindUnit(EnemyId)));
            Assert.IsInstanceOf<AttackCommand>(Decide(s, state),
                "A second stack would be refused, so it must not be requested.");
        }

        [Test]
        public void TauntInstrument_TauntsOnceThenAttacks()
        {
            BattleState state = Begun("kit", "grunt");
            TauntInstrumentStrategy s = new TauntInstrumentStrategy();

            ICommand first = Decide(s, state);
            Assert.IsInstanceOf<TauntCommand>(first);

            state = TestWorld.Apply(state, first);
            Assert.IsTrue(state.IsTaunting(state.FindUnit(PlayerId)));
            Assert.IsInstanceOf<AttackCommand>(Decide(s, state));
        }

        [Test]
        public void EveryInstrumentDegradesToAttackOnlyForAUnitWithoutTheSkill()
        {
            // This is what makes an instrument row comparable to its control:
            // on a unit that cannot use the action, the two must be identical.
            BattleState state = Begun("bare", "grunt");

            foreach (IPlayerStrategy s in new IPlayerStrategy[]
                     { new PushInstrumentStrategy(), new SlowInstrumentStrategy(), new TauntInstrumentStrategy() })
            {
                Assert.IsInstanceOf<AttackCommand>(Decide(s, state), s.Name + " did not degrade cleanly.");
            }
        }

        [Test]
        public void EveryInstrumentIsResolvableByNameAndAgreesWithItsOwnName()
        {
            foreach (string name in new[]
                     { "attack-only", "push-instrument", "slow-instrument", "taunt-instrument", "counter-reserve" })
            {
                IPlayerStrategy s = StrategyCatalog.Create(name);
                Assert.IsNotNull(s, "Not resolvable: " + name);
                Assert.AreEqual(name, s.Name);
            }
        }

        // ------------------------------------------------------- determinism

        private static SimulationRunner ShippedRunner()
        {
            return new SimulationRunner(
                Read("terrain"), Read("units"), Read("ai-profiles"));
        }

        private static string Read(string name)
        {
            UnityEngine.TextAsset asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/" + name);
            Assert.IsNotNull(asset, "Missing Resources/Data/" + name + ".txt");
            return asset.text;
        }

        private static SimulationConfig Cell(string encounter, string strategy) =>
            new SimulationConfig
            {
                MapName = encounter,
                EncounterText = Read(encounter),
                Strategy = StrategyCatalog.Create(strategy),
                Runs = 4,
                BaseSeed = 1,
                NoisePercent = 15,
                MaxRounds = 30,
                RouteProbeRow = -1
            };

        private static readonly string[] TestPapers =
        {
            "gym-e1-damagerace.encounter",
            "gym-e2-pushdelay.encounter",
            "gym-e3-slowcontrol.encounter",
            "gym-e4-protection.encounter"
        };

        [Test]
        public void EveryTestPaperLoadsAndReplaysDeterministically()
        {
            SimulationRunner runner = ShippedRunner();

            for (int i = 0; i < TestPapers.Length; i++)
            {
                SimulationConfig config = Cell(TestPapers[i], "attack-only");

                BattleResult first = runner.RunOne(config, 7);
                BattleResult second = runner.RunOne(config, 7);

                Assert.AreEqual(first.FinalStateHash, second.FinalStateHash, TestPapers[i] + " is not reproducible.");
                Assert.AreEqual(first.Outcome, second.Outcome);
                Assert.Greater(first.PlayerTurns, 0, TestPapers[i] + " ended before anyone acted.");
            }
        }

        [Test]
        public void TestPapersDoNotContaminateEachOther()
        {
            // The runner rebuilds the encounter per battle; running one map must
            // not shift another, or a matrix row would depend on batch order.
            SimulationRunner runner = ShippedRunner();

            uint before = runner.RunOne(Cell(TestPapers[0], "attack-only"), 3).FinalStateHash;
            for (int i = 1; i < TestPapers.Length; i++) runner.RunOne(Cell(TestPapers[i], "attack-only"), 3);
            uint after = runner.RunOne(Cell(TestPapers[0], "attack-only"), 3).FinalStateHash;

            Assert.AreEqual(before, after, "Running other encounters changed this one.");
        }

        [Test]
        public void AnInstrumentDoesNotDisturbTheRngOrTheObservers()
        {
            // Instruments consume no random numbers of their own, and attaching an
            // observer must not move the battle either.
            SimulationRunner runner = ShippedRunner();

            foreach (string strategy in new[] { "attack-only", "push-instrument", "slow-instrument" })
            {
                SimulationConfig bare = Cell("gym-e2-pushdelay.encounter", strategy);
                BattleResult without = runner.RunOne(bare, 5);

                SimulationConfig watched = Cell("gym-e2-pushdelay.encounter", strategy);
                watched.Observer = new CompositeObserver(new HeatmapObserver(), new RoleMetricsObserver());
                BattleResult with = runner.RunOne(watched, 5);

                Assert.AreEqual(without.FinalStateHash, with.FinalStateHash, strategy + " was disturbed.");
                Assert.AreEqual(without.PlayerTurns, with.PlayerTurns);
            }
        }

        [Test]
        public void RoleVariantsDifferOnlyInTheFieldsTheHypothesisNames()
        {
            // Momo A/B is the crossover pair, so an accidental extra difference
            // would make every E1/E2 comparison unattributable.
            UnitCatalog units = UnitLoader.Parse(Read("units"));
            UnitDef a = units.Get("Momotaro_A");
            UnitDef b = units.Get("Momotaro_B");

            Assert.AreEqual(a.MaxHp, b.MaxHp);
            Assert.AreEqual(a.Atk, b.Atk);
            Assert.AreEqual(a.Def, b.Def);
            Assert.AreEqual(a.Move, b.Move);
            Assert.AreEqual(a.AttackRange, b.AttackRange);
            Assert.AreEqual(a.MaxAp, b.MaxAp);
            Assert.AreEqual(a.ApRegen, b.ApRegen);
            Assert.AreEqual(a.GuardApCost, b.GuardApCost);

            Assert.AreEqual(4, a.AttackApCost, "A is the 4 AP variant.");
            Assert.AreEqual(5, b.AttackApCost, "B is the 5 AP variant.");
            Assert.IsFalse(a.CanPush, "Only B carries the push kit.");
            Assert.IsTrue(b.CanPush);
        }

        [Test]
        public void TheAnchoredEnemyIsIdenticalToThePushableOneExceptForImmunity()
        {
            // Without this, E2's "push is not universal" control would be
            // confounded by a stat difference.
            UnitCatalog units = UnitLoader.Parse(Read("units"));
            UnitDef pushable = units.Get("e2_pushable");
            UnitDef anchored = units.Get("e2_anchor");

            Assert.AreEqual(pushable.MaxHp, anchored.MaxHp);
            Assert.AreEqual(pushable.Atk, anchored.Atk);
            Assert.AreEqual(pushable.Def, anchored.Def);
            Assert.AreEqual(pushable.Move, anchored.Move);
            Assert.AreEqual(pushable.AttackApCost, anchored.AttackApCost);

            Assert.IsFalse(pushable.ImmuneToPush);
            Assert.IsTrue(anchored.ImmuneToPush, "The anchor is what stops Push applying to everything.");
        }
    }
}
