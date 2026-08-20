using Ediki.Core;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// 嘲諷 / 遲滯 / 擊退 — the three skills that change WHEN an enemy can reach
    /// you rather than how hard it hits.
    ///
    /// All three are turn-stamped rather than counted down, so most of what is
    /// worth testing here is timing: a slow that lapses before the slowed unit
    /// moves, or a taunt that lapses before the enemy picks a target, would both
    /// look like working code and measure as "the skill does nothing".
    /// </summary>
    public class ControlSkillTests
    {
        private const string Map =
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n";

        // ------------------------------------------------------------- Slow

        [Test]
        public void Slow_MakesEveryCellCostOneMoreAp()
        {
            BattleState state = TestWorld.BegunControl();
            UnitState grunt = state.FindUnit(TestWorld.GruntId);       // (4,2), road all round

            // Three road cells east: 3 AP normally, 6 AP slowed. Cost is the thing
            // being asserted, not the reachable count — with MOVE 3 the cap binds
            // before AP does on a small map, and the count would not move at all.
            Coord threeAway = TestWorld.C(7, 2);
            Assert.AreEqual(3, MovementCalculator.ComputeFor(state, grunt).CostTo(threeAway));

            ExecuteResult r = BattleSimulator.Execute(state,
                new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsTrue(r.Ok, r.RejectReason);

            UnitState slowed = r.State.FindUnit(TestWorld.GruntId);
            Assert.IsTrue(r.State.IsSlowed(slowed));

            Assert.AreEqual(6, MovementCalculator.ComputeFor(r.State, slowed).CostTo(threeAway),
                "遲滯 is +1 AP per CELL, so three cells cost three more.");
        }

        [Test]
        public void Slow_SurvivesIntoTheTargetsOwnPhase()
        {
            // The bug this exists to catch: a countdown ticked at the start of a
            // phase expires the slow before the slowed unit has moved once, so
            // the skill measures as worthless while looking correct.
            BattleState state = TestWorld.BegunControl();

            state = TestWorld.Apply(state, new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            Assert.AreEqual(Faction.Enemy, state.CurrentFaction);
            Assert.IsTrue(state.IsSlowed(state.FindUnit(TestWorld.GruntId)),
                "The slow must still be on when the target actually gets to move.");
        }

        [Test]
        public void Slow_LapsesAfterThatOnePhase()
        {
            BattleState state = TestWorld.BegunControl();
            state = TestWorld.Apply(state, new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));

            Assert.IsFalse(state.IsSlowed(state.FindUnit(TestWorld.GruntId)),
                "One round means one round — a slow that never lapses is a different skill.");
        }

        [Test]
        public void Slow_CannotBeStacked()
        {
            BattleState state = TestWorld.BegunControl();
            state = TestWorld.Apply(state, new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));

            ExecuteResult again = BattleSimulator.Execute(state,
                new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(again.Ok, "Re-slowing would be a way to burn AP that looks productive.");
        }

        [Test]
        public void Slow_IsRejectedBeyondRange()
        {
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=s name=S\n" + Map +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            // Manhattan (1,1)->(5,3) is 6; slow range is 3.
            ExecuteResult r = BattleSimulator.Execute(state,
                new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
        }

        // ------------------------------------------------------------ Taunt

        [Test]
        public void Taunt_OverridesTheAiTargetPreference()
        {
            // Two player units: the taunter is at FULL health and far, the other
            // is nearly dead and adjacent. A "lowestHp" hunter wants the wounded
            // one, so anything but the taunter proves the override never fired.
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=t name=T\n" + Map +
                "spawn faction=player unit=hero  x=3 y=1\n" +
                "spawn faction=player unit=grunt x=5 y=2\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher\n");

            BattleState state = BattleSimulator.Begin(setup.State).State;
            state.FindUnit(2).Hp = 1;                       // the bait

            state = TestWorld.Apply(state, new TauntCommand(TestWorld.HeroId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            EffectLog log = new EffectLog();
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, log).State;

            Assert.AreEqual(1, after.FindUnit(2).Hp, "The bait was hit — the taunt did not redirect.");
            Assert.Less(after.FindUnit(TestWorld.HeroId).Hp, 100, "The taunter should have been hit instead.");
        }

        [Test]
        public void Taunt_DoesNotReachEnemiesOutsideItsRadius()
        {
            // Same shape as the test above, but the taunter is eight cells from
            // the enemy and its radius is three. The wounded decoy should be hit,
            // because a taunt with unlimited reach would make "get close enough to
            // be worth hitting" free — and that cost is the whole trade.
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=t name=T\n" +
                "map\n" +
                "##########\n" +
                "#........#\n" +
                "#........#\n" +
                "#........#\n" +
                "##########\n" +
                "endmap\n" +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=player unit=grunt x=8 y=3\n" +
                "spawn faction=enemy  unit=grunt x=8 y=2 ai=rusher\n");

            BattleState state = BattleSimulator.Begin(setup.State).State;
            state.FindUnit(2).Hp = 30;

            state = TestWorld.Apply(state, new TauntCommand(TestWorld.HeroId));
            Assert.AreEqual(8, state.Map.Topology.Distance(TestWorld.C(1, 1), TestWorld.C(8, 2)),
                "Fixture assumption: the enemy is well outside the taunt radius.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            BattleState after = setup.Ai.RunFactionTurn(state, Faction.Enemy, new EffectLog()).State;

            Assert.Less(after.FindUnit(2).Hp, 30, "The nearby decoy should still have been the target.");
            Assert.AreEqual(100, after.FindUnit(TestWorld.HeroId).Hp,
                "A taunt from eight cells away must not pull anything.");
        }

        [Test]
        public void Taunt_CannotBeReapplied()
        {
            BattleState state = TestWorld.BegunControl();
            state = TestWorld.Apply(state, new TauntCommand(TestWorld.HeroId));

            ExecuteResult again = BattleSimulator.Execute(state, new TauntCommand(TestWorld.HeroId));
            Assert.IsFalse(again.Ok);
        }

        // ------------------------------------------------------------- Push

        [Test]
        public void Push_MovesTheTargetOneCellDirectlyAway()
        {
            // Hero (3,2), grunt (4,2) -> grunt should end up at (5,2).
            BattleState state = TestWorld.BegunControl();

            ExecuteResult r = BattleSimulator.Execute(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(TestWorld.C(5, 2), r.State.FindUnit(TestWorld.GruntId).Position);
            Assert.IsTrue(r.Log.Contains<UnitPushed>());
            Assert.AreEqual(TestWorld.C(3, 2), r.State.FindUnit(TestWorld.HeroId).Position,
                "Pushing must not move the pusher.");
        }

        [Test]
        public void Push_DealsNoDamage()
        {
            BattleState state = TestWorld.BegunControl();
            int before = state.FindUnit(TestWorld.GruntId).Hp;

            ExecuteResult r = BattleSimulator.Execute(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(before, r.State.FindUnit(TestWorld.GruntId).Hp,
                "Push is a positioning verb. Adding damage would make it a strictly better attack.");
        }

        [Test]
        public void Push_IsRejectedWhenTheDestinationIsBlocked()
        {
            // Grunt backed against the east wall at (5,1); (6,1) is blocking.
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=p name=P\n" + Map +
                "spawn faction=player unit=hero  x=4 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=1 ai=rusher\n");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            ExecuteResult r = BattleSimulator.Execute(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(r.Ok, "A push that cannot land must fail loudly, not spend AP for nothing.");
            Assert.AreEqual(0, r.Log.Count);
        }

        [Test]
        public void Push_IsRejectedAgainstAnImmuneUnit()
        {
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=p name=P\n" + Map +
                "spawn faction=player unit=hero x=4 y=3\n" +
                "spawn faction=enemy  unit=rock x=5 y=3 ai=rusher\n");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            ExecuteResult r = BattleSimulator.Execute(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void Push_IsRejectedOffAxis()
        {
            // Diagonal neighbours share no row or column, so "away" is undefined
            // on a four-neighbour grid.
            BattleSetup setup = TestWorld.CreateControl(
                "encounter id=p name=P\n" + Map +
                "spawn faction=player unit=hero  x=4 y=3\n" +
                "spawn faction=enemy  unit=grunt x=5 y=2 ai=rusher\n");
            BattleState state = BattleSimulator.Begin(setup.State).State;

            ExecuteResult r = BattleSimulator.Execute(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
        }

        // ------------------------------------------------- determinism / A3

        [Test]
        public void ControlStatusesAreClonedAndHashed()
        {
            BattleState state = TestWorld.BegunControl();
            uint clean = StateHasher.Hash(state);

            BattleState slowed = TestWorld.Apply(state, new SlowCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.AreNotEqual(clean, StateHasher.Hash(slowed), "Status must be part of the world hash.");

            BattleState copy = slowed.Clone();
            Assert.AreEqual(StateHasher.Hash(slowed), StateHasher.Hash(copy));

            copy.FindUnit(TestWorld.GruntId).SlowedUntilTurn = 99;
            Assert.AreNotEqual(StateHasher.Hash(slowed), StateHasher.Hash(copy));
            Assert.AreEqual(0, slowed.FindUnit(TestWorld.GruntId).SlowedUntilTurn == 99 ? 1 : 0,
                "Clone must deep-copy the status (A3).");
        }

        [Test]
        public void ABattleThatNeverUsesTheKitHashesAsItAlwaysDid()
        {
            // The reason A4's golden constants survived this change: statuses are
            // folded into the hash only when something actually carries one.
            BattleState plain = TestWorld.Begun();
            Assert.IsFalse(plain.HasControlStatus);
            Assert.AreEqual(StateHasher.Hash(TestWorld.Begun()), StateHasher.Hash(plain));
        }

        [Test]
        public void UnitsWithoutTheSkillCannotUseIt()
        {
            BattleState state = TestWorld.Begun();   // plain roster, no kit

            Assert.IsFalse(BattleSimulator.Execute(state, new TauntCommand(TestWorld.HeroId)).Ok);
            Assert.IsFalse(BattleSimulator.Execute(state, new SlowCommand(TestWorld.HeroId, TestWorld.GruntId)).Ok);
            Assert.IsFalse(BattleSimulator.Execute(state, new PushCommand(TestWorld.HeroId, TestWorld.GruntId)).Ok);
        }
    }
}
