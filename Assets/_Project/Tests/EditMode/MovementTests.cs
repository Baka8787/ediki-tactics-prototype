using Ediki.Core;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>Covers docs/03-spec/SPEC-movement.md.</summary>
    public class MovementTests
    {
        [Test]
        public void Move_OverRoad_CostsOneApPerCell()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(2, r.Log.First<ApSpent>().Amount);
            Assert.AreEqual(6, r.State.FindUnit(TestWorld.HeroId).Ap);
            Assert.AreEqual(TestWorld.C(3, 1), r.State.FindUnit(TestWorld.HeroId).Position);
        }

        [Test]
        public void Move_IntoForest_CostsTheTerrainValue()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(2, 2))));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(3, r.Log.First<ApSpent>().Amount, "Road (1) + Forest (2) should cost 3 AP.");
        }

        [Test]
        public void Move_IntoBlockingTerrain_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 0))));

            Assert.IsFalse(r.Ok);
            StringAssert.Contains("blocking", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Move_OntoAnotherUnit_IsRejected()
        {
            // Hero starts at (4,3), directly beside the grunt at (5,3).
            BattleState state = TestWorld.BegunAdjacent();

            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(5, 3))));

            Assert.IsFalse(r.Ok, "Units occupy their cell (OD-03).");
            StringAssert.Contains("occupied", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Move_WithNonAdjacentStep_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 1))));

            Assert.IsFalse(r.Ok, "Execute must not trust the caller's path (R-MOVE-06).");
            StringAssert.Contains("adjacent", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Move_ExceedingRemainingAp_IsRejected()
        {
            // Guard first so AP (5) binds before MOVE (4 cells).
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            Assert.AreEqual(5, state.FindUnit(TestWorld.HeroId).Ap);

            // road 1 + forest 2 + forest 2 = 5 AP over 3 cells: exactly affordable.
            Coord[] exactly5 = TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(2, 2), TestWorld.C(3, 2));
            Assert.IsTrue(BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId, exactly5)).Ok,
                "A path costing exactly the unit's AP must be legal.");

            // Same route plus one road step = 6 AP over 4 cells: within MOVE, over AP.
            Coord[] costs6 = TestWorld.Path(
                TestWorld.C(1, 2), TestWorld.C(2, 2), TestWorld.C(3, 2), TestWorld.C(4, 2));

            ExecuteResult r = BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId, costs6));
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("ap", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Move_EmptyPath_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId, new Coord[0]));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void ApAndMoveAreBothCumulativeAcrossCommandsInOneTurn()
        {
            // R-MOVE-05: interleaving move/attack/move must not reset either budget.
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));

            UnitState hero = state.FindUnit(TestWorld.HeroId);
            Assert.AreEqual(6, hero.Ap, "8 AP fixture, two road cells spent.");
            Assert.AreEqual(2, hero.MoveUsedThisTurn);

            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 1))));
            Assert.AreEqual(3, state.FindUnit(TestWorld.HeroId).MoveUsedThisTurn);

            ExecuteResult tooFar = BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(5, 1), TestWorld.C(5, 2))));
            Assert.IsFalse(tooFar.Ok, "3 used + 2 more = 5 cells, over MOVE 4.");
        }

        // ------------------------------------------------------------ MOVE cap

        [Test]
        public void Move_LongerThanMoveStat_IsRejectedEvenWithApToSpare()
        {
            // hero MOVE = 4, AP = 8. Five road cells cost only 5 AP but exceed MOVE.
            BattleState state = TestWorld.Begun();
            Coord[] fiveCells = TestWorld.Path(
                TestWorld.C(1, 2), TestWorld.C(1, 3), TestWorld.C(2, 3),
                TestWorld.C(3, 3), TestWorld.C(4, 3));

            ExecuteResult r = BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId, fiveCells));

            Assert.IsFalse(r.Ok);
            StringAssert.Contains("move", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Move_ExactlyMoveStat_IsLegal()
        {
            BattleState state = TestWorld.Begun();
            Coord[] fourCells = TestWorld.Path(
                TestWorld.C(1, 2), TestWorld.C(1, 3), TestWorld.C(2, 3), TestWorld.C(3, 3));

            Assert.IsTrue(BattleSimulator.Execute(state, new MoveCommand(TestWorld.HeroId, fourCells)).Ok);
        }

        [Test]
        public void MoveCapCannotBeBypassedByChainingActions()
        {
            // OD-17 resolution: MOVE is a per-TURN budget. Splitting the journey
            // into several move actions must not buy extra distance, or the cap is
            // decorative — which is exactly what the metrics caught.
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(1, 3), TestWorld.C(2, 3), TestWorld.C(3, 3))));

            Assert.AreEqual(4, state.FindUnit(TestWorld.HeroId).MoveUsedThisTurn);

            ExecuteResult fifth = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 3))));

            Assert.IsFalse(fifth.Ok, "MOVE 4 is spent; a fifth cell must be refused even with AP to spare.");
            StringAssert.Contains("move", fifth.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void MoveBudgetRefreshesOnTheNextTurn()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(1, 3), TestWorld.C(2, 3), TestWorld.C(3, 3))));

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

            Assert.AreEqual(0, state.FindUnit(TestWorld.HeroId).MoveUsedThisTurn);
            Assert.IsTrue(BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(4, 3)))).Ok);
        }

        [Test]
        public void Reachability_ShrinksAsTheMoveBudgetIsSpent()
        {
            BattleState state = TestWorld.Begun();
            int before = MovementCalculator.ComputeFor(state, state.FindUnit(TestWorld.HeroId)).ReachableCells.Count;

            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(1, 3))));
            int after = MovementCalculator.ComputeFor(state, state.FindUnit(TestWorld.HeroId)).ReachableCells.Count;

            Assert.Less(after, before, "Two cells of MOVE are gone; the range must reflect that.");
        }

        [Test]
        public void Reachability_RespectsTheMoveCap()
        {
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);   // MOVE 4, AP 8
            ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);

            foreach (Coord cell in reach.ReachableCells)
            {
                Coord[] path = reach.PathTo(cell);
                Assert.LessOrEqual(path.Length, hero.Def.Move,
                    "Reachability offered " + cell + " via " + path.Length + " cells, over MOVE " + hero.Def.Move + ".");
            }

            // (5,1) is 4 road cells away and affordable — MOVE 4 exactly allows it.
            Assert.IsTrue(reach.CanReach(TestWorld.C(5, 1)));
            // (5,2) needs 5 cells; out of range for a single action.
            Assert.IsFalse(reach.CanReach(TestWorld.C(5, 2)));
        }

        [Test]
        public void Reachability_PrefersACheapRouteButStaysWithinTheStepCap()
        {
            // Going around the forest is cheaper in AP; going through it uses fewer
            // cells. With a step cap the two can disagree, so reachability has to
            // search (cell, steps), not cell alone — otherwise it would report a
            // cell unreachable that ValidatePath happily accepts.
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);

            foreach (Coord cell in reach.ReachableCells)
            {
                if (cell == hero.Position) continue;
                Coord[] path = reach.PathTo(cell);
                string reason;
                int cost = MovementCalculator.ValidatePath(state, hero, path, out reason);
                Assert.AreEqual(reach.CostTo(cell), cost,
                    "Reachability and ValidatePath disagree about " + cell + ": " + reason);
            }
        }

        [Test]
        public void Reachability_ExcludesBlockedAndOccupiedCells()
        {
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);

            Assert.IsFalse(reach.CanReach(TestWorld.C(0, 0)), "Blocking terrain must never be reachable.");
            Assert.IsFalse(reach.CanReach(TestWorld.C(5, 3)), "The grunt's cell is occupied.");
            Assert.IsTrue(reach.CanReach(TestWorld.C(3, 1)));
            Assert.AreEqual(2, reach.CostTo(TestWorld.C(3, 1)));
            Assert.AreEqual(0, reach.CostTo(hero.Position));
        }

        [Test]
        public void Reachability_PathsAreWalkableByTheSimulator()
        {
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);

            foreach (Coord cell in reach.ReachableCells)
            {
                if (cell == hero.Position) continue;
                Coord[] path = reach.PathTo(cell);
                Assert.IsNotNull(path, "No path to a cell reported as reachable: " + cell);

                ExecuteResult r = BattleSimulator.Execute(state, new MoveCommand(hero.Id, path));
                Assert.IsTrue(r.Ok, "Flood fill offered " + cell + " but the simulator rejected it: " + r.RejectReason);
                Assert.AreEqual(reach.CostTo(cell), r.Log.First<ApSpent>().Amount,
                    "Reported cost and charged cost disagree for " + cell + ".");
            }
        }
    }
}
