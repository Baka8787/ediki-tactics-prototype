using System.Collections.Generic;
using Ediki.Core;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// Covers the Exposure and threat-range rules — the measuring instrument for
    /// the whole prototype hypothesis (docs/02-design/exposure.md).
    /// </summary>
    public class ExposureTests
    {
        //   0123456
        // 0 #######
        // 1 #.....#
        // 2 #.ff..#
        // 3 #.....#
        // 4 #######

        [Test]
        public void StaticExposure_CountsPassableNeighbours()
        {
            BattleMap map = TestWorld.Create().State.Map;

            Assert.AreEqual(2, BattleQueries.StaticExposure(map, TestWorld.C(1, 1)), "Corner: walls north and west.");
            Assert.AreEqual(3, BattleQueries.StaticExposure(map, TestWorld.C(3, 1)), "Against the top wall.");
            Assert.AreEqual(4, BattleQueries.StaticExposure(map, TestWorld.C(2, 2)), "Open on all four sides.");
            Assert.AreEqual(0, BattleQueries.StaticExposure(map, TestWorld.C(0, 0)), "Walled-in cell.");
        }

        [Test]
        public void StaticExposure_IgnoresUnitPositions()
        {
            // It is a property of the map geometry, not of the current battle.
            BattleState before = TestWorld.Begun();
            int a = BattleQueries.StaticExposure(before.Map, TestWorld.C(4, 3));

            BattleState after = TestWorld.Apply(before,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(1, 3))));
            int b = BattleQueries.StaticExposure(after.Map, TestWorld.C(4, 3));

            Assert.AreEqual(a, b);
        }

        [Test]
        public void ChokepointHasLowerExposureThanOpenGround()
        {
            // The core design claim, stated as a test: corridors are safer than fields.
            const string corridorMap =
                "encounter id=corridor name=Corridor\n" +
                "map\n" +
                "#######\n" +
                "#.....#\n" +
                "###.###\n" +
                "#.....#\n" +
                "#######\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=3 y=2\n" +
                "spawn faction=enemy unit=grunt x=5 y=1 ai=rusher\n";

            BattleMap map = TestWorld.Create(corridorMap).State.Map;

            Assert.AreEqual(2, BattleQueries.StaticExposure(map, TestWorld.C(3, 2)), "Corridor cell.");
            Assert.AreEqual(3, BattleQueries.StaticExposure(map, TestWorld.C(3, 1)), "Open ground beside it.");
        }

        [Test]
        public void ThreatRange_CoversWhereTheUnitCanMoveThenStrike()
        {
            BattleState state = TestWorld.Begun();
            UnitState grunt = state.FindUnit(TestWorld.GruntId);

            HashSet<Coord> threat = BattleQueries.ThreatRange(state, grunt);

            Assert.IsTrue(threat.Contains(grunt.Position), "A unit threatens where it already stands.");
            Assert.IsTrue(threat.Contains(TestWorld.C(3, 1)), "4 AP of movement plus 1 range reaches (3,1).");
            Assert.IsFalse(threat.Contains(TestWorld.C(1, 1)), "The hero's spawn is out of reach on turn 1.");
        }

        [Test]
        public void CurrentThreatRange_UsesOnlyRemainingApAndMove()
        {
            BattleState state = TestWorld.Begun();
            UnitState grunt = state.FindUnit(TestWorld.GruntId);
            grunt.Ap = grunt.Def.AttackApCost;
            grunt.MoveUsedThisTurn = grunt.Def.Move;

            HashSet<Coord> current = BattleQueries.CurrentThreatRange(state, grunt);
            HashSet<Coord> strike = BattleQueries.StrikeRange(state, grunt);

            CollectionAssert.AreEquivalent(strike, current,
                "With no movement budget left, the displayed threat must be the immediate strike range only.");
            Assert.Less(current.Count, BattleQueries.ThreatRange(state, grunt).Count,
                "The current overlay must not keep showing fresh-turn reach after resources were spent.");
        }

        [Test]
        public void ThreatRange_ShrinksWhenTheUnitIsWalledIn()
        {
            const string field =
                "encounter id=field name=Field\n" +
                "map\n" +
                "#########\n" +
                "#.......#\n" +
                "#.......#\n" +
                "#.......#\n" +
                "#########\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=2 ai=rusher\n" +
                "spawn faction=enemy unit=grunt x=4 y=2 ai=rusher\n";

            const string corridor =
                "encounter id=corridor name=Corridor\n" +
                "map\n" +
                "#########\n" +
                "#########\n" +
                "#.......#\n" +
                "#########\n" +
                "#########\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=2 ai=rusher\n" +
                "spawn faction=enemy unit=grunt x=4 y=2 ai=rusher\n";

            int openSize = ThreatSize(field);
            int corridorSize = ThreatSize(corridor);

            Assert.Less(corridorSize, openSize, "Blocking terrain must reduce how far a unit can threaten.");
        }

        private static int ThreatSize(string encounter)
        {
            BattleState state = BattleSimulator.Begin(TestWorld.Create(encounter).State).State;
            return BattleQueries.ThreatRange(state, state.FindUnit(TestWorld.GruntId)).Count;
        }

        [Test]
        public void EffectiveExposure_CountsLivingThreateningEnemiesOnly()
        {
            BattleState state = TestWorld.BegunAdjacent();

            Assert.AreEqual(1, BattleQueries.EffectiveExposure(state, TestWorld.C(4, 3), Faction.Player),
                "One grunt threatens the hero's cell.");

            // Kill it and the cell becomes safe.
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            state = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.AreEqual(0, BattleQueries.EffectiveExposure(state, TestWorld.C(4, 3), Faction.Player),
                "Dead enemies must stop counting (R-THR-10 says 'living').");
        }

        [Test]
        public void DangerZone_IsTheUnionOfEveryLivingEnemyThreat()
        {
            BattleState state = TestWorld.Begun();
            HashSet<Coord> zone = BattleQueries.DangerZone(state, Faction.Player);
            HashSet<Coord> single = BattleQueries.ThreatRange(state, state.FindUnit(TestWorld.GruntId));

            CollectionAssert.AreEquivalent(single, zone, "One enemy: the zone is exactly its threat range.");
        }

        [Test]
        public void AttackableTargets_OnlyListsEnemiesInRange()
        {
            BattleState far = TestWorld.Begun();
            Assert.AreEqual(0, BattleQueries.AttackableTargets(far, far.FindUnit(TestWorld.HeroId)).Count);

            BattleState near = TestWorld.BegunAdjacent();
            List<UnitState> targets = BattleQueries.AttackableTargets(near, near.FindUnit(TestWorld.HeroId));
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(TestWorld.GruntId, targets[0].Id);
        }
    }
}
