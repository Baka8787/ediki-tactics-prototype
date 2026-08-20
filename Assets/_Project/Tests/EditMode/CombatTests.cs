using Ediki.Core;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>Covers docs/03-spec/SPEC-combat.md (OD-05 / OD-06 baseline).</summary>
    public class CombatTests
    {
        // ------------------------------------------- rule variants (gym only)

        [Test]
        public void PercentageModel_TurnsDefIntoAShareOfDamagePrevented()
        {
            // 60 ATK against 20 DEF: subtraction leaves 40, a fifth off leaves 48.
            Assert.AreEqual(40, BattleRules.ComputeDamage(60, 20, false, DamageModel.Subtractive));
            Assert.AreEqual(48, BattleRules.ComputeDamage(60, 20, false, DamageModel.Percentage));
        }

        [Test]
        public void PercentageModel_RemovesTheOneDamageFloorForWeakAttackers()
        {
            // THE difference between the two models. Zhengshou's ATK 33 against a
            // DEF 45 wall is below the subtraction threshold, so he deals the
            // minimum 1 and needs 120 hits to kill 120 HP — he simply cannot do
            // the job. Under percentage he deals 18 and needs 7.
            Assert.AreEqual(1, BattleRules.ComputeDamage(33, 45, false, DamageModel.Subtractive));
            Assert.AreEqual(18, BattleRules.ComputeDamage(33, 45, false, DamageModel.Percentage));
        }

        [Test]
        public void PercentageModel_NeverMakesAUnitImmune()
        {
            Assert.AreEqual(10, BattleRules.ComputeDamage(100, 90, false, DamageModel.Percentage));
            Assert.AreEqual(10, BattleRules.ComputeDamage(100, 200, false, DamageModel.Percentage),
                "DEF above the cap must not go past it.");
            Assert.GreaterOrEqual(BattleRules.ComputeDamage(1, 99, false, DamageModel.Percentage), 1);
        }

        [Test]
        public void DefaultRuleSet_IsTheDecidedBaseline()
        {
            // Anything that does not ask for a variant must get OD-05 behaviour.
            Assert.AreEqual(DamageModel.Subtractive, RuleSet.Default.Damage);
            Assert.AreEqual(BattleRules.ComputeDamage(60, 20, false),
                            BattleRules.ComputeDamage(60, 20, false, RuleSet.Default.Damage));
            Assert.AreEqual(DamageModel.Subtractive, TestWorld.Begun().Rules.Damage,
                "An encounter with no rules line must run the baseline.");
        }

        [Test]
        public void AtkGrowth_IsAPureFunctionOfTheRoundAndZeroByDefault()
        {
            UnitDef flat = UnitLoader.Parse(TestWorld.Units).Get("hero");
            Assert.AreEqual(0, flat.AtkGrowth);
            Assert.AreEqual(flat.Atk, flat.AtkOnRound(1));
            Assert.AreEqual(flat.Atk, flat.AtkOnRound(99), "A unit with no growth must never change.");

            UnitDef growing = UnitLoader.Parse(
                "unit id=g name=G hp=10 atk=20 def=0 move=1 ap=8 range=1 attackCost=4 guardCost=3 atkGrowth=20\n")
                .Get("g");

            Assert.AreEqual(20, growing.AtkOnRound(1), "Round 1 is always the base value.");
            Assert.AreEqual(40, growing.AtkOnRound(2));
            Assert.AreEqual(100, growing.AtkOnRound(5));
        }

        [Test]
        public void Damage_IsAtkMinusDef()
        {
            Assert.AreEqual(40, BattleRules.ComputeDamage(60, 20, false));
            Assert.AreEqual(50, BattleRules.ComputeDamage(100, 50, false));
            Assert.AreEqual(25, BattleRules.ComputeDamage(30, 5, false));
        }

        [Test]
        public void Damage_NeverDropsBelowOne()
        {
            Assert.AreEqual(1, BattleRules.ComputeDamage(10, 10, false));
            Assert.AreEqual(1, BattleRules.ComputeDamage(5, 999, false));
            Assert.AreEqual(1, BattleRules.ComputeDamage(11, 10, true), "Guard must not floor damage to zero.");
        }

        [Test]
        public void Guard_HalvesIncomingDamage()
        {
            Assert.AreEqual(40, BattleRules.ComputeDamage(50, 10, false));
            Assert.AreEqual(20, BattleRules.ComputeDamage(50, 10, true));
        }

        [Test]
        public void Attack_SpendsApAndDealsDamage()
        {
            BattleState state = Adjacent();
            UnitState grunt = state.FindUnit(TestWorld.GruntId);
            int hpBefore = grunt.Hp;

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsTrue(r.Ok, r.RejectReason);

            Assert.AreEqual(4, r.Log.First<ApSpent>().Amount, "Attack costs the 4 AP declared in unit data (OD-01).");
            Assert.IsTrue(r.Log.First<AttackResolved>().Hit, "Attacks always hit under the deterministic baseline (OD-05).");
            Assert.AreEqual(-25, r.Log.First<HpChanged>().Delta, "hero 30 atk - grunt 5 def = 25.");
            Assert.AreEqual(hpBefore - 25, r.State.FindUnit(TestWorld.GruntId).Hp);
        }

        [Test]
        public void Attack_OutOfRange_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(r.Ok);
            StringAssert.Contains("range", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void Attack_OwnFaction_IsRejected()
        {
            BattleState state = TestWorld.Begun();
            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.HeroId));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void Attack_WithoutEnoughAp_IsRejected()
        {
            BattleState state = Adjacent();
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));   // 8 - 3 = 5
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));  // 5 - 4 = 1
            Assert.AreEqual(1, state.FindUnit(TestWorld.HeroId).Ap);

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("ap", r.RejectReason.ToLowerInvariant());
        }

        [Test]
        public void TwoAttacksPerTurn_ArePossibleAtFourApEach()
        {
            // Direct consequence of OD-01 (attack = 4 AP with an 8 AP bar).
            BattleState state = Adjacent();
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.AreEqual(0, state.FindUnit(TestWorld.HeroId).Ap);
            Assert.IsFalse(state.FindUnit(TestWorld.GruntId).IsAlive, "2 x 25 damage kills a 40 HP grunt.");
        }

        [Test]
        public void KillingTheLastEnemy_EndsTheBattleInTheSameCommand()
        {
            BattleState state = Adjacent();
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsTrue(r.Ok);

            Assert.IsTrue(r.Log.Contains<UnitDied>());
            Assert.IsTrue(r.Log.Contains<BattleEnded>(), "Victory must be decided inside the killing command (R-WIN-03).");
            Assert.AreEqual(BattleOutcome.Victory, r.Log.First<BattleEnded>().Outcome);
            Assert.AreEqual(BattleOutcome.Victory, r.State.Outcome);
        }

        [Test]
        public void CommandsAfterTheBattleEnds_AreRejected()
        {
            BattleState state = Adjacent();
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.AreEqual(BattleOutcome.Victory, state.Outcome);

            ExecuteResult r = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));
            Assert.IsFalse(r.Ok);
        }

        [Test]
        public void DeadUnit_StopsOccupyingItsCell()
        {
            BattleState state = Adjacent();
            Coord gruntCell = state.FindUnit(TestWorld.GruntId).Position;
            Assert.IsTrue(state.IsOccupied(gruntCell));

            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(state.IsOccupied(gruntCell), "Dead units must not keep blocking movement.");
        }

        [Test]
        public void GuardAppliedToTheTarget_ReducesDamageTaken()
        {
            BattleState state = Adjacent();
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            Assert.IsTrue(state.FindUnit(TestWorld.HeroId).IsGuarding, "Guard must survive into the enemy phase.");

            ExecuteResult r = BattleSimulator.Execute(state, new AttackCommand(TestWorld.GruntId, TestWorld.HeroId));
            Assert.IsTrue(r.Ok, r.RejectReason);
            Assert.AreEqual(-5, r.Log.First<HpChanged>().Delta, "(20 atk - 10 def) halved by Guard = 5.");
        }

        /// <summary>Hero at (4,3) beside the grunt at (5,3), both on a full AP bar.</summary>
        private static BattleState Adjacent() => TestWorld.BegunAdjacent();
    }
}
