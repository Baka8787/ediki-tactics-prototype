using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// Role metrics: AP residue and the counter-attack tallies.
    ///
    /// The counter numbers decide an experiment, so "opportunity" has to mean one
    /// exact thing: an attack that satisfied every condition the rule checks
    /// EXCEPT the AP. That isolates the economy, which is the whole question.
    /// </summary>
    public class RoleMetricsTests
    {
        /// <summary>
        /// Hero counters for 3 AP at range 1; grunt is melee, archer is range 2.
        /// Hero deals 30-5 = 25, grunt 20-10 = 10.
        /// </summary>
        private const string Units =
            "unit id=hero   name=Hero   hp=200 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 " +
                "counterCost=3 pushCost=2 pushRange=1\n" +
            "unit id=plain  name=Plain  hp=200 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=grunt  name=Grunt  hp=200 atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=archer name=Archer hp=200 atk=20 def=5  move=3 ap=8 range=2 attackCost=4 guardCost=3\n";

        /// <summary>
        ///   0123456
        /// 0 #######
        /// 1 #.....#
        /// 2 #.....#   player (2,2), enemy (3,2) adjacent
        /// 3 #.....#
        /// 4 #######
        /// </summary>
        private static string Encounter(string playerUnit, string enemyUnit) =>
            "encounter id=roles name=Roles\n" +
            "map\n#######\n#.....#\n#.....#\n#.....#\n#######\nendmap\n" +
            "spawn faction=player unit=" + playerUnit + " x=2 y=2\n" +
            "spawn faction=enemy  unit=" + enemyUnit + " x=3 y=2 ai=rusher\n";

        private const int PlayerId = 1;
        private const int EnemyId = 2;

        private static BattleSetup Setup(string playerUnit, string enemyUnit)
        {
            return EncounterLoader.CreateBattle(
                EncounterLoader.Parse(Encounter(playerUnit, enemyUnit), TerrainLoader.Parse(TestWorld.Terrain)),
                UnitLoader.Parse(Units),
                AiProfileLoader.Parse(TestWorld.AiProfiles));
        }

        private static BattleState Begun(string playerUnit, string enemyUnit) =>
            BattleSimulator.Begin(Setup(playerUnit, enemyUnit).State).State;

        /// <summary>Runs one enemy phase and shows it to the observer, as the runner does.</summary>
        private static BattleState EnemyPhase(RoleMetricsObserver observer, BattleSetup setup,
                                              BattleState state, int round)
        {
            EffectLog log = new EffectLog();
            BattleState before = state;
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;
            observer.PhaseResolved(round, Faction.Enemy, before, log, after);
            return after;
        }

        private static BattleState Feed(RoleMetricsObserver observer, int round,
                                        BattleState state, ICommand command)
        {
            ExecuteResult r = BattleSimulator.Execute(state, command);
            observer.PlayerCommand(round, state, command, r);
            return r.Ok ? r.State : state;
        }

        private static Coord C(int x, int y) => new Coord(x, y);

        // ----------------------------------------------------------- counter

        [Test]
        public void CounterOpportunity_IsAnAttackThatOnlyTheApCouldHaveStopped()
        {
            // Hero is adjacent, can counter, and has not countered yet — so every
            // enemy swing here is an opportunity whether or not it fires.
            BattleSetup setup = Setup("hero", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            Assert.Greater(observer.Metrics.EnemyAttacksOnPlayers, 0, "Fixture assumption: the grunt swings.");
            Assert.Greater(observer.Metrics.CounterOpportunities, 0);
            Assert.GreaterOrEqual(observer.Metrics.CounterOpportunities, observer.Metrics.CounterActivations,
                "An activation cannot exist without an opportunity.");
        }

        [Test]
        public void CounterOpportunity_IsNotCountedForARangedAttacker()
        {
            // A counter needs the attacker inside the DEFENDER's range. A range-2
            // enemy that strikes from two cells away can never be answered, so
            // counting it would dilute the rate with attacks the AP never had a
            // say in.
            BattleSetup setup = Setup("hero", "archer");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            // Step away so the archer stays at range 2 rather than closing to 1.
            state = Feed(observer, 1, state, new MoveCommand(PlayerId, new[] { C(1, 2) }));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            if (observer.Metrics.EnemyAttacksOnPlayers == 0) Assert.Ignore("The archer did not attack this turn.");

            Assert.AreEqual(0, observer.Metrics.CounterActivations,
                "Fixture assumption: a range-2 attacker cannot be countered at range 1.");
            Assert.AreEqual(0, observer.Metrics.CounterOpportunities,
                "Out-of-reach attacks are not opportunities the AP could have taken.");
        }

        [Test]
        public void CounterOpportunity_IsNotCountedForADefenderThatCannotCounter()
        {
            BattleSetup setup = Setup("plain", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            Assert.Greater(observer.Metrics.EnemyAttacksOnPlayers, 0);
            Assert.AreEqual(0, observer.Metrics.CounterOpportunities,
                "A unit with no counterCost was never in the running.");
        }

        [Test]
        public void CounterActivation_AndItsDamageAreAttributedToTheCounter()
        {
            BattleSetup setup = Setup("hero", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            // Player does nothing, so all 8 AP survive into the enemy phase.
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            Assert.AreEqual(1, observer.Metrics.CounterActivations,
                "One counter per round: HasCounteredThisRound resets on the unit's own phase.");
            Assert.AreEqual(25, observer.Metrics.CounterDamage, "Hero deals 30-5 = 25 on the riposte.");
            Assert.AreEqual(3, observer.Metrics.Ap.OnCounter, "counterCost is 3 and it is spent, not free.");
        }

        [Test]
        public void CounterDamage_IsNotConfusedWithOrdinaryAttackDamage()
        {
            BattleSetup setup = Setup("hero", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            // A normal attack first: 25 damage that is NOT counter damage.
            state = Feed(observer, 1, state, new AttackCommand(PlayerId, EnemyId));

            Assert.AreEqual(25, observer.Metrics.DamageDealt);
            Assert.AreEqual(0, observer.Metrics.CounterDamage,
                "The grunt cannot counter, so none of this is riposte damage.");
            Assert.AreEqual(1, observer.Metrics.PlayerAttacks);
        }

        [Test]
        public void CounterIsCappedAtOncePerRoundSoLaterAttacksAreNotOpportunities()
        {
            BattleSetup setup = Setup("hero", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            Assert.AreEqual(1, observer.Metrics.CounterActivations);
            Assert.LessOrEqual(observer.Metrics.CounterOpportunities,
                observer.Metrics.EnemyAttacksOnPlayers,
                "Attacks after the round's one counter are not opportunities.");
        }

        // -------------------------------------------------------- AP residue

        [Test]
        public void ApResidue_FilesEverySpendUnderTheActionThatMadeIt()
        {
            BattleState state = Begun("hero", "grunt");
            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            state = Feed(observer, 1, state, new AttackCommand(PlayerId, EnemyId));   // 4
            state = Feed(observer, 1, state, new GuardCommand(PlayerId));             // 3
            state = Feed(observer, 1, state, new MoveCommand(PlayerId, new[] { C(2, 1) })); // 1

            ApResidue ap = observer.Metrics.Ap;
            Assert.AreEqual(4, ap.OnAttack);
            Assert.AreEqual(3, ap.OnGuard);
            Assert.AreEqual(1, ap.OnMove);
            Assert.AreEqual(0, ap.OnSkill);
            Assert.AreEqual(8, ap.TotalSpent, "Everything the turn spent is accounted for exactly once.");
        }

        [Test]
        public void ApResidue_CountsASkillSeparatelyFromAnAttack()
        {
            BattleState state = Begun("hero", "grunt");
            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            state = Feed(observer, 1, state, new PushCommand(PlayerId, EnemyId));     // pushCost 2

            Assert.AreEqual(2, observer.Metrics.Ap.OnSkill);
            Assert.AreEqual(0, observer.Metrics.Ap.OnAttack);
            Assert.AreEqual(1, observer.Metrics.Pushes);
        }

        [Test]
        public void ApReserved_IsWhatSurvivesIntoTheEnemyPhase()
        {
            // AP resets only for the INCOMING faction, so the player's leftover is
            // genuinely still there when it is attacked — that is what a counter
            // spends, and what this number has to capture.
            BattleState state = Begun("hero", "grunt");
            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            state = Feed(observer, 1, state, new AttackCommand(PlayerId, EnemyId));   // 8 -> 4

            ExecuteResult end = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));
            observer.PhaseResolved(1, Faction.Player, state, end.Log, end.State);

            Assert.AreEqual(4, observer.Metrics.Ap.ReservedIntoEnemyPhase,
                "Measured before the transition, which is the last moment it is the player's.");
        }

        [Test]
        public void RejectedCommands_AreCountedAndSpendNothing()
        {
            BattleState state = Begun("hero", "grunt");
            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);

            state = Feed(observer, 1, state, new MoveCommand(PlayerId, new[] { C(2, 0) }));  // into a wall

            Assert.AreEqual(1, observer.Metrics.RejectedCommands);
            Assert.AreEqual(0, observer.Metrics.Ap.TotalSpent, "A refused command costs nothing.");
            Assert.AreEqual(0, observer.Metrics.PlayerAttacks);
        }

        [Test]
        public void DamageReceivedAndDealtAreKeptApart()
        {
            BattleSetup setup = Setup("hero", "grunt");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            RoleMetricsObserver observer = new RoleMetricsObserver();
            observer.RoundStarted(1, state);
            state = Feed(observer, 1, state, new AttackCommand(PlayerId, EnemyId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            EnemyPhase(observer, setup, state, 1);

            Assert.Greater(observer.Metrics.DamageDealt, 0);
            Assert.Greater(observer.Metrics.DamageReceived, 0);
            Assert.AreEqual(10 * observer.Metrics.EnemyAttacksOnPlayers, observer.Metrics.DamageReceived,
                "The grunt deals 20-10 = 10 a swing, and nothing else damaged the hero.");
        }

        // ------------------------------------------------------ determinism

        private static SimulationConfig Config(IBattleObserver observer) =>
            new SimulationConfig
            {
                MapName = "roles",
                EncounterText = Encounter("hero", "grunt"),
                Strategy = new CorridorHoldStrategy(),
                Runs = 6,
                BaseSeed = 1,
                NoisePercent = 15,
                MaxRounds = 30,
                RouteProbeRow = -1,
                Observer = observer
            };

        private static SimulationRunner Runner() =>
            new SimulationRunner(TestWorld.Terrain, Units, TestWorld.AiProfiles);

        [Test]
        public void AttachingRoleMetricsObserverDoesNotChangeBattle()
        {
            SimulationRunner runner = Runner();
            SimulationConfig plain = Config(null);
            plain.Observer = null;

            for (int seed = 1; seed <= 8; seed++)
            {
                BattleResult without = runner.RunOne(plain, seed);
                BattleResult with = runner.RunOne(Config(new RoleMetricsObserver()), seed);

                Assert.AreEqual(without.FinalStateHash, with.FinalStateHash,
                    "The role observer changed the battle at seed " + seed + ".");
                Assert.AreEqual(without.Outcome, with.Outcome);
                Assert.AreEqual(without.FinalPlayerHp, with.FinalPlayerHp);
            }
        }

        [Test]
        public void SameSeedProducesTheSameRoleMetrics()
        {
            SimulationRunner runner = Runner();

            RoleMetricsObserver first = new RoleMetricsObserver();
            runner.RunOne(Config(first), 4321);

            RoleMetricsObserver second = new RoleMetricsObserver();
            runner.RunOne(Config(second), 4321);

            Assert.AreEqual(first.Metrics.Describe(), second.Metrics.Describe());
        }

        [Test]
        public void CompositeObserver_FeedsEveryObserverAndStillDoesNotChangeTheBattle()
        {
            // The batch now attaches two taps at once; neither may perturb the run.
            SimulationRunner runner = Runner();

            HeatmapObserver heatmap = new HeatmapObserver();
            RoleMetricsObserver roles = new RoleMetricsObserver();

            BattleResult composed = runner.RunOne(Config(new CompositeObserver(heatmap, roles)), 11);

            SimulationConfig plain = Config(null);
            plain.Observer = null;
            BattleResult bare = runner.RunOne(plain, 11);

            Assert.AreEqual(bare.FinalStateHash, composed.FinalStateHash);
            Assert.AreEqual(1, roles.Metrics.Battles, "The role observer saw the battle.");
            Assert.AreEqual(1, heatmap.Heatmap.BattlesObserved, "So did the heatmap.");
            Assert.Greater(heatmap.Heatmap.Occupancy.Total, 0);
        }

        // ------------------------------------------- counter-reserve instrument

        [Test]
        public void CounterReserve_AttacksOnlyWhileTheRiposteStaysAffordable()
        {
            // Hero: 8 AP, attack 4, counterCost 3.
            //   8 -> attack -> 4, and 4 >= 3, so the first swing is allowed
            //   4 -> attack -> 0, and 0 <  3, so the second is refused
            BattleState state = Begun("hero", "grunt");
            CounterReserveStrategy strategy = new CounterReserveStrategy();

            ICommand first = strategy.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));
            Assert.IsInstanceOf<AttackCommand>(first, "The first attack still leaves 4 AP, above the reserve.");

            state = TestWorld.Apply(state, first);
            Assert.AreEqual(4, state.FindUnit(PlayerId).Ap);

            ICommand second = strategy.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));
            Assert.IsInstanceOf<WaitCommand>(second,
                "A second attack would leave 0 AP, so the reserve forbids it.");
        }

        [Test]
        public void CounterReserve_NeverLetsAttacksDropApBelowCounterCost()
        {
            // The invariant, driven to exhaustion rather than reasoned about.
            BattleState state = Begun("hero", "grunt");
            CounterReserveStrategy strategy = new CounterReserveStrategy();
            UnitState unit = state.FindUnit(PlayerId);
            int counterCost = unit.Def.CounterApCost;

            for (int step = 0; step < 12; step++)
            {
                unit = state.FindUnit(PlayerId);
                ICommand command = strategy.DecideNext(state, unit, new DeterministicRandom(1));

                Assert.IsFalse(command is GuardCommand, "The instrument must never spend the reserve on Guard.");

                if (command is WaitCommand) break;
                state = TestWorld.Apply(state, command);

                Assert.GreaterOrEqual(state.FindUnit(PlayerId).Ap, counterCost,
                    "An attack dropped AP below counterCost at step " + step + ".");
            }
        }

        [Test]
        public void CounterReserve_EndsTheActivationRatherThanGuardingOrResting()
        {
            BattleState state = Begun("hero", "grunt");
            CounterReserveStrategy strategy = new CounterReserveStrategy();

            state = TestWorld.Apply(state, new AttackCommand(PlayerId, EnemyId));   // 8 -> 4
            ICommand next = strategy.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));

            Assert.IsInstanceOf<WaitCommand>(next);
            Assert.IsFalse(next is GuardCommand);
            Assert.IsFalse(next is RestCommand);
            Assert.IsFalse(next is MoveCommand, "It holds position by design; see the instrument's limits.");
        }

        [Test]
        public void CounterReserve_DegradesToPlainAttackingForAUnitThatCannotCounter()
        {
            // counterCost 0 means there is no reserve to protect, so the same test
            // becomes "attack while affordable" without a special case.
            BattleState state = Begun("plain", "grunt");
            CounterReserveStrategy strategy = new CounterReserveStrategy();

            Assert.AreEqual(0, state.FindUnit(PlayerId).Def.CounterApCost);

            ICommand first = strategy.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));
            state = TestWorld.Apply(state, first);
            ICommand second = strategy.DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));

            Assert.IsInstanceOf<AttackCommand>(first);
            Assert.IsInstanceOf<AttackCommand>(second, "8 AP buys two 4 AP attacks when nothing is reserved.");
        }

        [Test]
        public void CounterReserve_WaitsWhenNothingIsInReach()
        {
            BattleState state = Begun("hero", "grunt");
            state = TestWorld.Apply(state, new MoveCommand(PlayerId, new[] { C(1, 2) }));  // out of reach

            ICommand command = new CounterReserveStrategy()
                .DecideNext(state, state.FindUnit(PlayerId), new DeterministicRandom(1));

            Assert.IsInstanceOf<WaitCommand>(command, "No target, so it keeps its AP and stands down.");
        }

        [Test]
        public void CounterReserve_IsDeterministicAndReproducible()
        {
            SimulationRunner runner = Runner();

            SimulationConfig config = Config(null);
            config.Observer = null;
            config.Strategy = new CounterReserveStrategy();

            Assert.AreEqual(runner.RunOne(config, 77).FinalStateHash,
                            runner.RunOne(config, 77).FinalStateHash);

            RoleMetricsObserver a = new RoleMetricsObserver();
            SimulationConfig watched = Config(a);
            watched.Strategy = new CounterReserveStrategy();
            runner.RunOne(watched, 77);

            RoleMetricsObserver b = new RoleMetricsObserver();
            SimulationConfig watched2 = Config(b);
            watched2.Strategy = new CounterReserveStrategy();
            runner.RunOne(watched2, 77);

            Assert.AreEqual(a.Metrics.Describe(), b.Metrics.Describe());
        }

        [Test]
        public void CounterReserve_ActuallyRaisesTheReserveAgainstTheBaselineStrategy()
        {
            // The instrument has to do the one thing it exists for, or the whole
            // 2x2 measures nothing.
            SimulationRunner runner = Runner();

            RoleMetricsObserver normal = new RoleMetricsObserver();
            SimulationConfig a = Config(normal);
            a.Strategy = new CorridorHoldStrategy();
            for (int seed = 1; seed <= 8; seed++) runner.RunOne(a, seed);

            RoleMetricsObserver reserving = new RoleMetricsObserver();
            SimulationConfig b = Config(reserving);
            b.Strategy = new CounterReserveStrategy();
            for (int seed = 1; seed <= 8; seed++) runner.RunOne(b, seed);

            Assert.AreEqual(0, reserving.Metrics.Ap.OnGuard, "The instrument must never guard.");
            Assert.Greater(reserving.Metrics.Ap.ReservedIntoEnemyPhase / (double)reserving.Metrics.PlayerRounds,
                           normal.Metrics.Ap.ReservedIntoEnemyPhase / (double)normal.Metrics.PlayerRounds,
                           "counter-reserve must hold back more AP per round than the baseline.");
        }

        [Test]
        public void CounterReserve_IsResolvableByNameForReplay()
        {
            IPlayerStrategy strategy = StrategyCatalog.Create("counter-reserve");

            Assert.IsNotNull(strategy);
            Assert.AreEqual("counter-reserve", strategy.Name,
                "The catalog key and the strategy's own name must agree or replays mislabel rows.");
        }

        [Test]
        public void Batch_AttachesRoleMetricsPerCellWithoutTouchingTheCsv()
        {
            SimulationRunner runner = Runner();
            List<MapEntry> maps = new List<MapEntry>
                { new MapEntry("roles", Encounter("hero", "grunt")) };
            List<IPlayerStrategy> strategies = new List<IPlayerStrategy> { new CorridorHoldStrategy() };

            BatchOutput output = MetricsBatch.Run(runner, maps, strategies, 4, 1, 15, 30);

            Assert.IsNotNull(output.Cells[0].Roles);
            Assert.AreEqual(4, output.Cells[0].Roles.Battles);
            StringAssert.Contains("role metrics", output.Summary);

            string[] lines = output.RawCsv.Trim().Replace("\r\n", "\n").Split('\n');
            Assert.AreEqual(
                "map,strategy,seed,outcome,turns,hit_round_cap,player_turns,ap_granted,ap_unused," +
                "final_player_hp,enemies_remaining,mean_exposure_x100,first_crossing_x," +
                "unit_crossings,contact_round1,contact_turns,never_in_contact,state_hash",
                lines[0], "The CSV header changed.");
            StringAssert.DoesNotContain("counter", output.RawCsv);
        }
    }
}
