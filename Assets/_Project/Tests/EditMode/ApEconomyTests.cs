using Ediki.Core;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// AP carry-over and Rest (OD-21).
    ///
    /// Regen below the cap is the whole point: banking AP this turn has to buy
    /// something next turn, or "leftover AP" stays meaningless.
    /// </summary>
    public class ApEconomyTests
    {
        // cap 10, regen 8 — the decided economy.
        private const string Units =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=10 apRegen=8 range=1 " +
                "attackCost=4 guardCost=3 restCost=2 restHealPercent=10\n" +
            "unit id=grunt name=Grunt hp=200 atk=20 def=5  move=3 ap=10 apRegen=8 range=1 " +
                "attackCost=4 guardCost=3\n";

        private const string Encounter =
            "encounter id=ap name=Ap\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero  x=1 y=1\n" +
            "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher\n";

        private static BattleState Begun()
        {
            TerrainCatalog terrain = TerrainLoader.Parse(TestWorld.Terrain);
            UnitCatalog units = UnitLoader.Parse(Units);
            Ediki.Core.Ai.AiProfileCatalog ai = AiProfileLoader.Parse(TestWorld.AiProfiles);
            EncounterDef enc = EncounterLoader.Parse(Encounter, terrain);
            return BattleSimulator.Begin(EncounterLoader.CreateBattle(enc, units, ai).State).State;
        }

        private static BattleState NextRound(BattleState state)
        {
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            return TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
        }

        [Test]
        public void UnitsStartAtTheCap()
        {
            Assert.AreEqual(10, Begun().FindUnit(TestWorld.HeroId).Ap);
        }

        [Test]
        public void SpendingEverythingStillOnlyRegensToTheRegenValue()
        {
            // The difference that makes banking worth anything: burn the bar down
            // and next turn you have 8, not the full 10.
            BattleState state = Begun();
            state.FindUnit(TestWorld.HeroId).Ap = 0;

            state = NextRound(state);
            Assert.AreEqual(8, state.FindUnit(TestWorld.HeroId).Ap,
                "0 + 8 regen = 8. Spending everything costs you the 2 you could have banked.");
        }

        [Test]
        public void BankingTwoApBuysTheCapNextTurn()
        {
            BattleState state = Begun();
            state.FindUnit(TestWorld.HeroId).Ap = 2;

            state = NextRound(state);
            Assert.AreEqual(10, state.FindUnit(TestWorld.HeroId).Ap, "2 + 8 = 10, exactly the cap.");
        }

        [Test]
        public void CarryOverIsCappedAtMaxAp()
        {
            BattleState state = Begun();          // 10 AP, spend nothing
            state = NextRound(state);
            Assert.AreEqual(10, state.FindUnit(TestWorld.HeroId).Ap, "10 + 8 must clamp to the cap of 10.");
        }

        [Test]
        public void SpendingBelowTheCapLetsRegenActuallyLand()
        {
            BattleState state = Begun();
            // Three road cells (3 AP) plus a guard (3 AP) = 6 spent, 4 left.
            // MOVE 4 caps the walking, so the rest of the spend has to come from
            // somewhere else — which is the point of having several actions.
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId,
                TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1), TestWorld.C(4, 1))));
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            Assert.AreEqual(4, state.FindUnit(TestWorld.HeroId).Ap);

            state = NextRound(state);
            Assert.AreEqual(10, state.FindUnit(TestWorld.HeroId).Ap, "4 + 8 = 12, clamped to 10.");
        }

        [Test]
        public void ApResetReportsWhatWasGainedAndWhatSpilled()
        {
            BattleState state = Begun();   // full at 10
            ExecuteResult endPlayer = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));
            ExecuteResult back = BattleSimulator.Execute(endPlayer.State, new EndTurnCommand(Faction.Enemy));

            ApReset reset = null;
            for (int i = 0; i < back.Log.Count; i++)
                if (back.Log[i] is ApReset r && r.UnitId == TestWorld.HeroId) reset = r;

            Assert.IsNotNull(reset);
            Assert.AreEqual(10, reset.NewAp);
            Assert.AreEqual(0, reset.Gained, "Already at the cap, so nothing landed.");
            Assert.AreEqual(8, reset.Wasted, "The whole regen spilled — that is the only real AP waste (M2).");
        }

        // ------------------------------------------------------------------ Rest

        [Test]
        public void Rest_HealsAPercentageOfMaxHpAndEndsTheTurn()
        {
            BattleState state = Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            hero.Hp = 50;                                  // fixture setup, not a rule path

            ExecuteResult r = BattleSimulator.Execute(state, new RestCommand(TestWorld.HeroId));
            Assert.IsTrue(r.Ok, r.RejectReason);

            UnitState after = r.State.FindUnit(TestWorld.HeroId);
            Assert.AreEqual(60, after.Hp, "10% of 100 max HP.");
            Assert.AreEqual(8, after.Ap, "Rest costs 2 AP.");
            Assert.IsTrue(after.HasEndedTurn, "Resting stands the unit down — that is the price.");
            Assert.AreEqual(10, r.Log.First<UnitRested>().HealAmount);
        }

        [Test]
        public void Rest_CannotExceedMaxHp()
        {
            BattleState state = Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            hero.Hp = 95;

            ExecuteResult r = BattleSimulator.Execute(state, new RestCommand(TestWorld.HeroId));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(100, r.State.FindUnit(TestWorld.HeroId).Hp);
            Assert.AreEqual(5, r.Log.First<UnitRested>().HealAmount);
        }

        [Test]
        public void Rest_AtFullHpStillBanksTheTurn()
        {
            // Legal on purpose: with carry-over, standing down early is a way to
            // bank AP, and the heal simply does nothing.
            BattleState state = Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new RestCommand(TestWorld.HeroId));

            Assert.IsTrue(r.Ok);
            Assert.AreEqual(0, r.Log.First<UnitRested>().HealAmount);
            Assert.IsTrue(r.State.FindUnit(TestWorld.HeroId).HasEndedTurn);
        }

        [Test]
        public void Rest_CannotActAgainAfterResting()
        {
            BattleState state = Begun();
            state = TestWorld.Apply(state, new RestCommand(TestWorld.HeroId));

            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1))));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void Rest_IsRejectedWithoutEnoughAp()
        {
            BattleState state = Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            hero.Ap = 1;

            ExecuteResult r = BattleSimulator.Execute(state, new RestCommand(TestWorld.HeroId));
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("ap", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Rest_IsUnavailableWhenTheUnitHasNoRestCost()
        {
            // grunt has no restCost in this fixture.
            BattleState state = Begun();
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            ExecuteResult r = BattleSimulator.Execute(state, new RestCommand(TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("cannot rest", r.RejectReason.ToLowerInvariant());
        }
    }
}
