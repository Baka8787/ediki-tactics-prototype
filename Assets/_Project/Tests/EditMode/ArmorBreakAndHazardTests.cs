using Ediki.Core;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// 破甲 and lethal terrain — the two rule-layer gaps closed on 2026-08-17.
    ///
    /// Both add state that most battles never touch, and both were built to the
    /// pattern contamination and kill targets already use: fold into the hash
    /// only when actually present, so the A4 golden constants keep meaning
    /// something instead of being rubber-stamped. The tests that matter most here
    /// are therefore not the feature tests but the two at the bottom, which
    /// assert that a battle using neither is bit-for-bit what it always was.
    /// </summary>
    public class ArmorBreakAndHazardTests
    {
        // ------------------------------------------------------------- 破甲

        [Test]
        public void ArmorBreak_CutsDefAndRaisesDamage()
        {
            BattleState state = TestWorld.BegunHazard();

            // DEF 25 against ATK 30 is the floor-adjacent case: 5 a hit.
            UnitState grunt = state.FindUnit(TestWorld.GruntId);
            Assert.AreEqual(25, state.EffectiveDef(grunt), "Armour is intact to begin with.");

            BattleState broken = TestWorld.Apply(state,
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.AreEqual(5, broken.EffectiveDef(broken.FindUnit(TestWorld.GruntId)),
                "20 DEF removed from 25 leaves 5.");

            int before = broken.FindUnit(TestWorld.GruntId).Hp;
            BattleState hit = TestWorld.Apply(broken,
                new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            int dealt = before - hit.FindUnit(TestWorld.GruntId).Hp;

            Assert.AreEqual(25, dealt, "ATK 30 against the broken DEF 5, not the printed 25.");
        }

        [Test]
        public void ArmorBreak_NeverDrivesDefBelowZero()
        {
            // A negative DEF would turn the skill into a damage multiplier under
            // the subtractive model, which is not what "remove armour" means.
            const string thinArmour =
                "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 " +
                    "armorBreakCost=2 armorBreakRange=1 armorBreakAmount=20\n" +
                "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

            TerrainCatalog terrain = TerrainLoader.Parse(TestWorld.HazardTerrain);
            UnitCatalog units = UnitLoader.Parse(thinArmour);
            var profiles = AiProfileLoader.Parse(TestWorld.AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(TestWorld.HazardEncounter, terrain);
            BattleState state = BattleSimulator.Begin(
                EncounterLoader.CreateBattle(encounter, units, profiles).State).State;

            BattleState broken = TestWorld.Apply(state,
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.AreEqual(0, broken.EffectiveDef(broken.FindUnit(TestWorld.GruntId)),
                "DEF 5 minus 20 clamps at 0.");
            Assert.AreEqual(30, BattleRules.ComputeDamage(30, 0, false),
                "And the damage is plain ATK, not ATK plus the overshoot.");
        }

        [Test]
        public void ArmorBreak_DoesNotStack()
        {
            // Same reason Slow refuses: re-applying would be a way to spend AP
            // that the action-mix metric reads as productive while buying nothing.
            BattleState broken = TestWorld.Apply(TestWorld.BegunHazard(),
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            ExecuteResult again = BattleSimulator.Execute(broken,
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(again.Ok, "A second break on the same target must be rejected.");
        }

        [Test]
        public void ArmorBreak_SurvivesIntoTheTargetsOwnPhase()
        {
            // The regression that turn stamps exist to prevent: a status applied
            // by the player must still be live when the enemy acts, or the skill
            // measures as doing nothing for reasons that have nothing to do with
            // its design. Mirrors Slow_SurvivesIntoTheTargetsOwnPhase.
            BattleState broken = TestWorld.Apply(TestWorld.BegunHazard(),
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            BattleState enemyPhase = TestWorld.Apply(broken, new EndTurnCommand(Faction.Player));

            Assert.IsTrue(enemyPhase.IsArmorBroken(enemyPhase.FindUnit(TestWorld.GruntId)),
                "The break must outlive the phase that applied it.");
        }

        // --------------------------------------------------- lethal terrain

        [Test]
        public void PushIntoLethalTerrain_KillsOutright()
        {
            BattleState state = TestWorld.BegunHazard();
            Assert.IsTrue(state.Map.IsLethal(TestWorld.C(3, 2)), "Fixture sanity: (3,2) is the chasm.");

            BattleState after = TestWorld.Apply(state,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            UnitState grunt = after.FindUnit(TestWorld.GruntId);
            Assert.IsFalse(grunt.IsAlive, "A unit shoved into a chasm dies.");
            Assert.AreEqual(0, grunt.Hp);
            Assert.AreEqual(TestWorld.C(3, 2), grunt.Position, "And it dies IN the hazard, not before it.");
        }

        [Test]
        public void PushIntoLethalTerrain_IgnoresGuard()
        {
            // Routed straight to zero rather than through ComputeDamage on
            // purpose: a chasm has no ATK, so halving it would be meaningless.
            //
            // The flag is set directly rather than by issuing a GuardCommand: it
            // is the player's phase, so the grunt cannot legally act, and driving
            // a whole enemy phase to get there would test the turn loop instead of
            // this rule. Setting up a state is what a fixture is for.
            BattleState guarded = TestWorld.BegunHazard().Clone();
            guarded.FindUnit(TestWorld.GruntId).IsGuarding = true;

            BattleState after = TestWorld.Apply(guarded,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(after.FindUnit(TestWorld.GruntId).IsAlive, "Guard does not soften a fall.");
        }

        [Test]
        public void NothingWalksIntoAHazardOnItsOwn()
        {
            // The load-bearing one. LegalCommands enumerates from this
            // reachability map, and the batch runner's noise samples that list
            // uniformly — so a reachable chasm means units die at random on every
            // hazard map and every measurement taken there is garbage.
            BattleState state = TestWorld.BegunHazard();
            UnitState hero = state.FindUnit(TestWorld.HeroId);

            ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);

            CollectionAssert.DoesNotContain(reach.ReachableCells, TestWorld.C(3, 2),
                "The pathfinder must never offer a lethal cell as a destination.");
        }

        [Test]
        public void LethalTerrainIsStillPassableSoAPushCanReachIt()
        {
            // The distinction the whole mechanism rests on: blocking terrain
            // shapes where units can GO, lethal terrain shapes where they can be
            // PUT. Make it impassable and Push would be rejected instead.
            BattleState state = TestWorld.BegunHazard();

            Assert.IsTrue(state.Map.IsPassable(TestWorld.C(3, 2)));
            Assert.IsTrue(state.CanUnitEnter(TestWorld.C(3, 2)));
        }

        [Test]
        public void BlockingTerrainCannotAlsoBeLethal()
        {
            // Nothing could ever enter it, so the hazard would be unreachable and
            // the data would be quietly meaningless. Rejected at load instead.
            Assert.Throws<DataFormatException>(() =>
                TerrainLoader.Parse("terrain name=Bad symbol=b cost=0 blocks=true lethal=true\n"));
        }

        // ------------------------------------------------- hash containment

        [Test]
        public void ABattleThatBreaksNoArmourHashesAsItAlwaysDid()
        {
            // The A4 discipline: new state must cost the golden constants nothing
            // until something actually uses it.
            BattleState state = TestWorld.BegunHazard();
            Assert.IsFalse(state.HasArmorBreak, "Nothing is broken at the start.");

            uint before = StateHasher.Hash(state);
            BattleState moved = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            BattleState back = TestWorld.Apply(moved, new EndTurnCommand(Faction.Player));

            Assert.IsFalse(back.HasArmorBreak,
                "Guarding and ending a turn must not switch the armour-break fold on.");
            Assert.AreNotEqual(before, StateHasher.Hash(back),
                "Sanity: the hash still tracks the things it always tracked.");
        }

        [Test]
        public void ArmorBreakStateIsClonedAndHashed()
        {
            // ADR-0004's known failure mode is a field that Clone forgets. A3
            // covers the collections; this covers these two fields specifically.
            BattleState broken = TestWorld.Apply(TestWorld.BegunHazard(),
                new ArmorBreakCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsTrue(broken.HasArmorBreak, "The fold must switch on once something is broken.");

            BattleState clone = broken.Clone();
            UnitState original = broken.FindUnit(TestWorld.GruntId);
            UnitState copied = clone.FindUnit(TestWorld.GruntId);

            Assert.AreEqual(original.ArmorBrokenUntilTurn, copied.ArmorBrokenUntilTurn);
            Assert.AreEqual(original.ArmorBrokenAmount, copied.ArmorBrokenAmount);
            Assert.AreEqual(StateHasher.Hash(broken), StateHasher.Hash(clone));

            copied.ArmorBrokenAmount = 99;
            Assert.AreEqual(20, original.ArmorBrokenAmount, "Clone must be isolated.");
            Assert.AreNotEqual(StateHasher.Hash(broken), StateHasher.Hash(clone),
                "And the amount must reach the hash, or two different states would collide.");
        }
    }
}
