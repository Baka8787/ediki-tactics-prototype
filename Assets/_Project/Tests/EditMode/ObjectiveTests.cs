using Ediki.Core;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// Non-rout objectives, reserve-AP counters and reinforcement waves (OD-19).
    /// These exist to give the player a reason to move; rout with no clock
    /// degrades into camping.
    /// </summary>
    public class ObjectiveTests
    {
        private const string Map =
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n";

        // ------------------------------------------------------------- Reach

        [Test]
        public void Reach_WinsTheInstantThePlayerStandsOnTheTarget()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=r name=R\n" +
                "objective type=reach x=3 y=1 turns=8\n" + Map +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=5 y=3 ai=rusher\n").State).State;

            Assert.AreEqual(BattleOutcome.InProgress, state.Outcome);

            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.IsTrue(r.Log.Contains<BattleEnded>(), "Standing on the target should end it immediately.");
            Assert.AreEqual(BattleOutcome.Victory, r.State.Outcome);
        }

        [Test]
        public void Reach_LosesWhenTheClockRunsOut()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=r name=R\n" +
                "objective type=reach x=5 y=1 turns=2\n" + Map +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=5 y=3 ai=rusher\n").State).State;

            // Sit still for two full rounds.
            for (int round = 0; round < 2 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleOutcome.Defeat, state.Outcome, "Camping must lose against a clock.");
        }

        [Test]
        public void Rout_HasNoClockByDefault()
        {
            // R-WIN-04 still holds: the turn limit belongs to the objective, not the rules.
            BattleState state = TestWorld.Begun();
            Assert.AreEqual(ObjectiveKind.Rout, state.Objective.Kind);
            Assert.IsFalse(state.Objective.HasTurnLimit);

            for (int round = 0; round < 12; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }
            Assert.AreEqual(BattleOutcome.InProgress, state.Outcome);
        }

        // ----------------------------------------------------------- Survive

        [Test]
        public void Survive_WinsOnceTheClockExpires()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=s name=S\n" +
                "objective type=survive turns=2\n" + Map +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=5 y=3 ai=rusher\n").State).State;

            for (int round = 0; round < 2 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleOutcome.Victory, state.Outcome);
        }

        // ------------------------------------------------------------ Defend

        private const string DefendEncounter =
            "encounter id=d name=D\n" +
            "objective type=defend turns=6\n" + Map +
            "spawn faction=player unit=shrine x=1 y=3 protect=true\n" +
            "spawn faction=player unit=hero   x=1 y=1\n" +
            "spawn faction=enemy  unit=grunt  x=5 y=3 ai=rusher\n";

        private const string ShrineUnits =
            TestWorld.Units +
            "unit id=shrine name=Shrine hp=10 atk=1 def=0 move=0 ap=0 range=1 attackCost=1 guardCost=1\n";

        private static BattleSetup DefendSetup()
        {
            TerrainCatalog terrain = TerrainLoader.Parse(TestWorld.Terrain);
            UnitCatalog units = UnitLoader.Parse(ShrineUnits);
            Ediki.Core.Ai.AiProfileCatalog profiles = AiProfileLoader.Parse(TestWorld.AiProfiles);
            return EncounterLoader.CreateBattle(EncounterLoader.Parse(DefendEncounter, terrain), units, profiles);
        }

        [Test]
        public void Defend_LosesTheMomentTheProtectedUnitDies()
        {
            BattleSetup setup = DefendSetup();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            UnitState shrine = null;
            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
                if (u.MustSurvive) shrine = u;
            Assert.IsNotNull(shrine, "The protected spawn should be flagged MustSurvive.");

            // Let the grunt walk in and break it. 10 HP against a 20 ATK grunt.
            for (int round = 0; round < 8 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                if (state.Outcome != BattleOutcome.InProgress) break;
                state = setup.Ai.RunFactionTurn(state, Faction.Enemy, new EffectLog()).State;
                if (state.Outcome != BattleOutcome.InProgress) break;
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleOutcome.Defeat, state.Outcome,
                "Losing the protected unit must lose the battle even though the hero is alive.");
        }

        [Test]
        public void ProtectedUnitDoesNotCountAsACombatant()
        {
            BattleState state = BattleSimulator.Begin(DefendSetup().State).State;
            Assert.AreEqual(2, state.CountLiving(Faction.Player));
            Assert.AreEqual(1, state.CountLivingCombatants(Faction.Player),
                "The shrine must not keep the battle alive once the hero is gone.");
        }

        [Test]
        public void DefendObjectiveWithoutAProtectedSpawn_IsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=d name=D\n" +
                "objective type=defend turns=6\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n"));
        }

        // -------------------------------------------------------------- Kill

        // Hero at (3,1) with a grunt on either side, both in reach on turn 1.
        // Ids follow spawn order: hero 1, west grunt 2, east grunt 3.
        // Hero deals 25 and can afford two attacks, so either grunt (40 HP) can
        // be finished inside the first turn — the tests never need the AI to run.
        private const string KillEncounter =
            "encounter id=k name=K\n" +
            "objective type=kill\n" + Map +
            "spawn faction=player unit=hero  x=3 y=1\n" +
            "spawn faction=enemy  unit=grunt x=2 y=1 ai=rusher\n" +
            "spawn faction=enemy  unit=grunt x=4 y=1 ai=rusher target=true\n";

        private const int WestGruntId = 2;
        private const int EastGruntId = 3;

        [Test]
        public void Kill_WinsWhenTheMarkedEnemyDies_WithOtherEnemiesStillAlive()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(KillEncounter).State).State;

            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, EastGruntId));
            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, EastGruntId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(BattleOutcome.Victory, r.State.Outcome);
            Assert.AreEqual(1, r.State.CountLiving(Faction.Enemy),
                "The unmarked enemy must still be standing — that is the whole point of a kill objective.");
        }

        [Test]
        public void Kill_KillingAnUnmarkedEnemyDoesNotWin()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(KillEncounter).State).State;

            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, WestGruntId));
            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, WestGruntId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.IsTrue(r.Log.Contains<UnitDied>(), "Fixture needs the west grunt to die here.");
            Assert.AreEqual(BattleOutcome.InProgress, r.State.Outcome,
                "Clearing a minion is not the objective and must not end the battle.");
        }

        [Test]
        public void Kill_MarksExactlyOneUnitAndFindsItAgain()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(KillEncounter).State).State;

            UnitState target = state.ObjectiveTarget();
            Assert.IsNotNull(target);
            Assert.AreEqual(EastGruntId, target.Id);
            Assert.IsFalse(state.FindUnit(WestGruntId).IsObjectiveTarget);
        }

        [Test]
        public void Kill_WithoutAMarkedSpawn_IsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=kill\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n"));
        }

        [Test]
        public void Kill_MarkOnAPlayerSpawn_IsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=kill\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1 target=true\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n"));
        }

        [Test]
        public void Kill_MarkOnAReinforcement_IsRejected()
        {
            // The mark travels on the spawn, but a wave gets its unit id only when
            // it lands. Silently dropping it would leave an unwinnable battle.
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=kill\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher turn=2 target=true\n"));
        }

        [Test]
        public void MarkWithoutAKillObjective_IsRejected()
        {
            // A mark no objective reads would change nothing except the state hash.
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=rout\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher target=true\n"));
        }

        [Test]
        public void KillTargetIsClonedAndHashed()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(KillEncounter).State).State;

            BattleState copy = state.Clone();
            Assert.AreEqual(StateHasher.Hash(state), StateHasher.Hash(copy));
            Assert.IsTrue(copy.FindUnit(EastGruntId).IsObjectiveTarget, "Clone dropped the mark (A3).");

            // Same map, same roster, same objective — only WHICH unit is marked
            // differs. If the hash cannot see that, two different battles would
            // share a golden constant.
            BattleState other = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=kill\n" + Map +
                "spawn faction=player unit=hero  x=3 y=1\n" +
                "spawn faction=enemy  unit=grunt x=2 y=1 ai=rusher target=true\n" +
                "spawn faction=enemy  unit=grunt x=4 y=1 ai=rusher\n").State).State;

            Assert.AreNotEqual(StateHasher.Hash(state), StateHasher.Hash(other),
                "Marking a different enemy must change the world hash.");
        }

        [Test]
        public void Kill_LosesWhenTheClockRunsOut()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=k name=K\n" +
                "objective type=kill turns=2\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher target=true\n").State).State;

            for (int round = 0; round < 2 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleOutcome.Defeat, state.Outcome);
        }

        [Test]
        public void Kill_HasNoClockByDefault()
        {
            // Same as Rout: the target is already a reason to move, so the limit
            // stays optional rather than being smuggled in with the objective.
            BattleState state = BattleSimulator.Begin(TestWorld.Create(KillEncounter).State).State;
            Assert.IsFalse(state.Objective.HasTurnLimit);
        }

        // ---------------------------------------------------- Counter attack

        // Grunt HP is raised so it survives two hero attacks — the reserve tests
        // need the fight to still be going after the hero has spent everything.
        private const string CounterUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 counterCost=3\n" +
            "unit id=grunt name=Grunt hp=200 atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        private static BattleState CounterFixture()
        {
            TerrainCatalog terrain = TerrainLoader.Parse(TestWorld.Terrain);
            UnitCatalog units = UnitLoader.Parse(CounterUnits);
            Ediki.Core.Ai.AiProfileCatalog profiles = AiProfileLoader.Parse(TestWorld.AiProfiles);
            EncounterDef enc = EncounterLoader.Parse(TestWorld.AdjacentEncounter, terrain);
            return BattleSimulator.Begin(EncounterLoader.CreateBattle(enc, units, profiles).State).State;
        }

        [Test]
        public void Counter_FiresWhenTheDefenderKeptEnoughApBack()
        {
            BattleState state = CounterFixture();
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));   // hero keeps all 8 AP

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.IsTrue(r.Log.Contains<CounterAttacked>(), "Log: " + r.Log);
            Assert.AreEqual(5, r.State.FindUnit(TestWorld.HeroId).Ap, "Countering consumes the reserve.");

            UnitState grunt = r.State.FindUnit(TestWorld.GruntId);
            Assert.Less(grunt.Hp, grunt.Def.MaxHp, "The counter should have hurt the attacker.");
        }

        [Test]
        public void Counter_DoesNotFireWithoutAReserve()
        {
            BattleState state = CounterFixture();
            // Spend down to 0: two attacks at 4 AP each.
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsTrue(state.FindUnit(TestWorld.GruntId).IsAlive, "Fixture needs the grunt to survive two hits.");
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.IsFalse(r.Log.Contains<CounterAttacked>(),
                "Spending everything on offence must forfeit the riposte — that is the whole trade.");
        }

        [Test]
        public void Counter_HappensAtMostOncePerRound()
        {
            BattleState state = CounterFixture();
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            state = TestWorld.Apply(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));
            ExecuteResult second = BattleSimulator.Execute(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));

            Assert.IsTrue(second.Ok, second.RejectReason);
            Assert.IsFalse(second.Log.Contains<CounterAttacked>(), "No free second riposte in the same round.");
        }

        [Test]
        public void Counter_IsOffByDefault()
        {
            BattleState state = TestWorld.BegunAdjacent();
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));
            Assert.IsFalse(r.Log.Contains<CounterAttacked>(), "counterCost defaults to 0 = no counter.");
        }

        // ---------------------------------------------------- Reinforcements

        [Test]
        public void Reinforcement_ArrivesOnItsDeclaredRound()
        {
            BattleSetup setup = TestWorld.Create(
                "encounter id=w name=W\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher turn=2\n");

            BattleState state = BattleSimulator.Begin(setup.State).State;
            Assert.AreEqual(1, state.CountLiving(Faction.Enemy), "The wave must not be on the field yet.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));   // round 1 enemy phase
            Assert.AreEqual(1, state.CountLiving(Faction.Enemy));

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));    // -> round 2
            ExecuteResult r = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));

            Assert.IsTrue(r.Log.Contains<UnitSpawned>(), "Round 2 should bring the wave in. Log: " + r.Log);
            Assert.AreEqual(2, r.State.CountLiving(Faction.Enemy));
        }

        [Test]
        public void PendingReinforcementsKeepTheBattleAlive()
        {
            // Killing everything on screen is not victory if another wave is due.
            BattleSetup setup = TestWorld.Create(
                "encounter id=w name=W\n" + Map +
                "spawn faction=player unit=hero  x=4 y=3\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher turn=3\n");

            BattleState state = BattleSimulator.Begin(setup.State).State;
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            ExecuteResult kill = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsTrue(kill.Log.Contains<UnitDied>());
            Assert.AreEqual(BattleOutcome.InProgress, kill.State.Outcome,
                "A pending wave means the field is not actually clear.");
        }

        [Test]
        public void ReinforcementStateIsClonedAndHashed()
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(
                "encounter id=w name=W\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher turn=2\n").State).State;

            BattleState copy = state.Clone();
            Assert.AreEqual(StateHasher.Hash(state), StateHasher.Hash(copy));

            copy.Reinforcements[0].Spawned = true;
            Assert.AreNotEqual(StateHasher.Hash(state), StateHasher.Hash(copy),
                "Wave state must be part of the world hash, or A4 would miss it.");
            Assert.IsFalse(state.Reinforcements[0].Spawned, "Clone must deep-copy the wave list (A3).");
        }
    }
}
