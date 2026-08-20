using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>Covers docs/03-spec/SPEC-ai-behaviour.md (OD-10 baseline).</summary>
    public class AiTests
    {
        [Test]
        public void UnengagedUnit_ClosesInInsteadOfIdlingForever()
        {
            // OD-16 baseline. Before this decision an unactivated enemy returned
            // Wait, which let the player camp on an exposure-1 cell forever.
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            UnitState grunt = state.FindUnit(TestWorld.GruntId);
            Assert.IsFalse(grunt.IsActivated, "The hero spawns outside the grunt's threat range.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            ICommand decision = setup.Ai.DecideNext(state, state.FindUnit(TestWorld.GruntId));

            Assert.IsInstanceOf<MoveCommand>(decision,
                "A unit with no attackable target must advance on the player, not idle (OD-16).");
        }

        [Test]
        public void ActivationLatch_IsObservabilityOnly_AndNoLongerGatesBehaviour()
        {
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            Assert.IsFalse(state.FindUnit(TestWorld.GruntId).IsActivated);

            UnitState hero = state.FindUnit(TestWorld.HeroId);
            int before = Coord.ManhattanDistance(state.FindUnit(TestWorld.GruntId).Position, hero.Position);

            EffectLog log = new EffectLog();
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;

            Assert.Less(Coord.ManhattanDistance(after.FindUnit(TestWorld.GruntId).Position, hero.Position), before,
                "An unactivated unit still closes the gap. Log: " + log);
        }

        [Test]
        public void NoEnemyRemainsIdleAcrossAWholeBattle()
        {
            // The regression guard for the stalemate that OD-16 was raised for:
            // a passive player must still eventually be reached.
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            for (int round = 0; round < 30 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));   // player does nothing
                if (state.Outcome != BattleOutcome.InProgress) break;
                state = setup.Ai.RunFactionTurn(state, Faction.Enemy, new EffectLog()).State;
                if (state.Outcome != BattleOutcome.InProgress) break;
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreEqual(BattleOutcome.Defeat, state.Outcome,
                "A player who never acts must eventually lose — otherwise the stalemate is back.");
        }

        [Test]
        public void Perception_LatchesOnceThePlayerStepsIntoRange()
        {
            BattleState state = TestWorld.Begun();
            Assert.IsFalse(state.FindUnit(TestWorld.GruntId).IsActivated);

            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.IsTrue(r.State.FindUnit(TestWorld.GruntId).IsActivated);
            Assert.IsTrue(r.Log.Contains<UnitActivated>(), "Activation must be visible in the effect log.");

            UnitActivated activated = r.Log.First<UnitActivated>();
            Assert.AreEqual(TestWorld.GruntId, activated.UnitId);
            Assert.AreEqual(TestWorld.HeroId, activated.TriggeredByUnitId);
        }

        [Test]
        public void Perception_StaysLatchedAfterThePlayerBacksOff()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));
            Assert.IsTrue(state.FindUnit(TestWorld.GruntId).IsActivated);

            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(1, 1))));

            Assert.IsTrue(state.FindUnit(TestWorld.GruntId).IsActivated,
                "Activation is a latch — enemies do not go back to sleep (R-THR-08).");
        }

        [Test]
        public void ActivatedEnemy_ClosesInAndAttacks()
        {
            BattleSetup setup = TestWorld.Create(TestWorld.AdjacentEncounter);
            BattleState state = BattleSimulator.Begin(setup.State).State;
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            EffectLog log = new EffectLog();
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;

            Assert.IsTrue(log.Contains<AttackResolved>(), "An adjacent, activated enemy should attack. Log: " + log);
            Assert.Less(after.FindUnit(TestWorld.HeroId).Hp, 100);
        }

        [Test]
        public void ActivatedEnemy_MovesTowardTheTargetWhenOutOfRange()
        {
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            Coord before = state.FindUnit(TestWorld.GruntId).Position;
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            int distanceBefore = Coord.ManhattanDistance(before, hero.Position);

            EffectLog log = new EffectLog();
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;

            Coord now = after.FindUnit(TestWorld.GruntId).Position;
            Assert.Less(Coord.ManhattanDistance(now, hero.Position), distanceBefore,
                "The grunt should have closed the gap. Log: " + log);
        }

        [Test]
        public void AiNeverBypassesTheSimulator_AndNeverProducesIllegalMoves()
        {
            BattleSetup setup = TestWorld.Create();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            for (int round = 0; round < 12 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                if (state.Outcome != BattleOutcome.InProgress) break;

                EffectLog log = new EffectLog();
                state = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;
                if (state.Outcome != BattleOutcome.InProgress) break;

                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

                // Invariants that must hold after every enemy phase.
                for (int i = 0; i < state.Units.Count; i++)
                {
                    UnitState u = state.Units[i];
                    if (!u.IsAlive) continue;
                    Assert.IsTrue(state.Map.IsPassable(u.Position), "Unit " + u.Id + " ended on blocking terrain.");
                    Assert.GreaterOrEqual(u.Ap, 0, "Unit " + u.Id + " overspent AP.");
                    Assert.AreSame(u, state.UnitAt(u.Position), "Two units share cell " + u.Position + ".");
                }
            }
        }

        [Test]
        public void AiProfile_IsDataDriven_NotHardcodedPerUnitType()
        {
            AiProfileCatalog catalog = AiProfileLoader.Parse(
                "aiprofile id=rusher target=nearest distance=1 aggression=90 retreatHp=0 guardHp=0\n" +
                "aiprofile id=turtle target=lowestHp distance=3 aggression=10 retreatHp=50 guardHp=80\n");

            AiProfile rusher = catalog.GetOrDefault("rusher");
            AiProfile turtle = catalog.GetOrDefault("turtle");

            Assert.AreEqual(TargetPreference.Nearest, rusher.TargetPreference);
            Assert.AreEqual(90, rusher.Aggression);

            Assert.AreEqual(TargetPreference.LowestHp, turtle.TargetPreference);
            Assert.AreEqual(3, turtle.PreferredDistance);
            Assert.AreEqual(50, turtle.RetreatHpPercent);
            Assert.AreEqual(80, turtle.GuardHpPercent);
        }

        [Test]
        public void UnknownAiProfile_FallsBackInsteadOfThrowing()
        {
            AiProfileCatalog catalog = AiProfileLoader.Parse(TestWorld.AiProfiles);
            Assert.AreEqual(AiProfile.Default, catalog.GetOrDefault("does-not-exist"));
        }

        [Test]
        public void EnemyPhase_IsDeterministic()
        {
            uint RunOnce()
            {
                BattleSetup setup = TestWorld.Create();
                BattleState state = BattleSimulator.Begin(setup.State).State;
                state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                    TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                return StateHasher.Hash(setup.Ai.RunFactionTurn(state, Faction.Enemy, new EffectLog()).State);
            }

            Assert.AreEqual(RunOnce(), RunOnce());
        }
    }
}
