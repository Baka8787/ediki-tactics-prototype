using Ediki.Core;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>Covers docs/03-spec/SPEC-battle-flow.md.</summary>
    public class TurnFlowTests
    {
        [Test]
        public void Begin_EmitsTurnStartedForThePlayer()
        {
            ExecuteResult r = BattleSimulator.Begin(TestWorld.Create().State);
            Assert.IsTrue(r.Ok);

            TurnStarted started = r.Log.First<TurnStarted>();
            Assert.IsNotNull(started);
            Assert.AreEqual(Faction.Player, started.Faction);
            Assert.AreEqual(1, started.TurnIndex);
        }

        [Test]
        public void FactionsAlternate_AndTurnIndexAdvancesOncePerRound()
        {
            BattleState state = TestWorld.Begun();
            Assert.AreEqual(Faction.Player, state.CurrentFaction);
            Assert.AreEqual(1, state.TurnIndex);

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            Assert.AreEqual(Faction.Enemy, state.CurrentFaction);
            Assert.AreEqual(1, state.TurnIndex, "The round counter advances only after both phases.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            Assert.AreEqual(Faction.Player, state.CurrentFaction);
            Assert.AreEqual(2, state.TurnIndex);
        }

        [Test]
        public void EndTurn_FromTheWrongFaction_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Enemy));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void ActingWithAUnitOutsideItsFactionsTurn_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new GuardCommand(TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("turn", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void ApResets_AtTheStartOfTheOwningFactionsPhase()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1))));
            Assert.AreEqual(7, state.FindUnit(TestWorld.HeroId).Ap);

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            Assert.AreEqual(7, state.FindUnit(TestWorld.HeroId).Ap, "Player AP must not refill during the enemy phase.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            Assert.AreEqual(8, state.FindUnit(TestWorld.HeroId).Ap);
        }

        [Test]
        public void UnusedAp_DoesNotCarryOver()
        {
            // OD-07 baseline: no carry-over.
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

            UnitState hero = state.FindUnit(TestWorld.HeroId);
            Assert.AreEqual(hero.Def.MaxAp, hero.Ap, "AP is reset to the cap, never stacked.");
        }

        [Test]
        public void Guard_ExpiresAtTheStartOfTheGuardingUnitsNextPhase()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            Assert.IsTrue(state.FindUnit(TestWorld.HeroId).IsGuarding);

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            Assert.IsTrue(state.FindUnit(TestWorld.HeroId).IsGuarding,
                "Guard must still be up while the enemy acts — that is the entire point (OD-06).");

            ExecuteResult back = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Enemy));
            Assert.IsFalse(back.State.FindUnit(TestWorld.HeroId).IsGuarding);
            Assert.IsTrue(back.Log.Contains<GuardExpired>());
        }

        [Test]
        public void Guard_CannotBeStackedInOneTurn()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));

            ExecuteResult r = BattleSimulator.Execute(state, new GuardCommand(TestWorld.HeroId));
            Assert.IsFalse(r.Ok, "Re-guarding would just burn AP for nothing; reject it explicitly.");
        }

        [Test]
        public void Wait_EndsTheUnitsActivationWithoutBurningAp()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new WaitCommand(TestWorld.HeroId));
            Assert.IsTrue(r.Ok);

            UnitWaited waited = r.Log.First<UnitWaited>();
            Assert.AreEqual(8, waited.UnusedAp, "Unused AP is the M2 metric — Wait must not zero it.");
            Assert.IsTrue(r.State.FindUnit(TestWorld.HeroId).HasEndedTurn);

            ExecuteResult after = BattleSimulator.Execute(r.State, new GuardCommand(TestWorld.HeroId));
            Assert.IsFalse(after.Ok, "A unit that has waited cannot act again this turn.");
        }

        [Test]
        public void HasEndedTurn_ClearsOnTheNextRound()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new WaitCommand(TestWorld.HeroId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

            Assert.IsFalse(state.FindUnit(TestWorld.HeroId).HasEndedTurn);
        }

        [Test]
        public void MoveAttackMove_IsLegalWhenApAllows()
        {
            // R-ACT-01: the player may interleave freely.
            BattleState state = TestWorld.BegunAdjacent();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 2))));
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 3))));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(3, 3))));

            Assert.AreEqual(1, state.FindUnit(TestWorld.HeroId).Ap, "1 + 1 + 4 + 1 = 7 of 8 AP.");
            Assert.AreEqual(TestWorld.C(3, 3), state.FindUnit(TestWorld.HeroId).Position);
        }
    }
}
