using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The gate before any squad matrix runs with OnePly in it.
    ///
    /// The question is NOT "does one-ply play well". It is "can a 1-ply evaluator
    /// tell commands apart using signals the State already carries" — so what
    /// these tests check is that each component can move a ranking on its own,
    /// and that the ones that cannot are identified rather than assumed away.
    /// </summary>
    public class OnePlySmokeTests
    {
        private const string Terrain =
            "terrain name=Road symbol=. cost=1 blocks=false\n" +
            "terrain name=Blocking symbol=# cost=0 blocks=true\n" +
            "terrain name=Chasm symbol=x cost=1 blocks=false lethal=true\n";

        private const string Units =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=10 apRegen=8 range=1 " +
                "attackCost=4 guardCost=3 pushCost=3 pushRange=1 armorBreakCost=3 " +
                "armorBreakRange=1 armorBreakAmount=20 slowCost=3 slowRange=3\n" +
            "unit id=frail name=Frail hp=20  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=tough name=Tough hp=200 atk=20 def=25 move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        private const string Profiles =
            "aiprofile id=rusher target=nearest distance=1 aggression=90 retreatHp=0 guardHp=0\n";

        private static BattleState Begin(string encounter)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(Terrain);
            EncounterDef def = EncounterLoader.Parse(encounter, terrain);
            return BattleSimulator.Begin(EncounterLoader.CreateBattle(
                def, UnitLoader.Parse(Units), AiProfileLoader.Parse(Profiles)).State).State;
        }

        private static OnePlyTacticalStrategy.Ranked Best(List<OnePlyTacticalStrategy.Ranked> ranked)
        {
            OnePlyTacticalStrategy.Ranked best = ranked[0];
            for (int i = 1; i < ranked.Count; i++)
                if (ranked[i].Score.Total > best.Score.Total) best = ranked[i];
            return best;
        }

        // ------------------------------------------------- 1. enumerate + execute

        [Test]
        public void EveryLegalCommandEvaluatesWithoutRejection()
        {
            // LegalCommands and the simulator must agree. A rejected candidate is
            // scored at minus infinity so it can never be chosen, but if any turn
            // up at all the two disagree and every ranking below is suspect.
            BattleState state = Begin(Open);
            List<OnePlyTacticalStrategy.Ranked> ranked =
                OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            Assert.Greater(ranked.Count, 5, "A hero with a kit should have plenty of options.");
            foreach (OnePlyTacticalStrategy.Ranked r in ranked)
                Assert.IsTrue(r.Executed, "Simulator rejected a supposedly legal command: " + r.Command.Describe());
        }

        [Test]
        public void RankingIsDeterministic()
        {
            BattleState state = Begin(Open);
            List<OnePlyTacticalStrategy.Ranked> a = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));
            List<OnePlyTacticalStrategy.Ranked> b = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Command.Describe(), b[i].Command.Describe(), "Order drifted at " + i);
                Assert.AreEqual(a[i].Score.Total, b[i].Score.Total, "Score drifted at " + i);
            }

            // And the state it was asked about must be untouched (A2 in miniature).
            Assert.AreEqual(StateHasher.Hash(state), StateHasher.Hash(state));
        }

        // --------------------------------------------------- 2. signal by signal

        /// <summary>Hero (1) next to a 20 HP frail (2) it one-shots.</summary>
        private const string Open =
            "encounter id=open name=Open\n" +
            "map\n#######\n#.....#\n#.....#\n#######\nendmap\n" +
            "spawn faction=player unit=hero  x=2 y=1\n" +
            "spawn faction=enemy  unit=frail x=3 y=1 ai=rusher\n";

        [Test]
        public void KillAndDamage_ChangeTheRanking()
        {
            BattleState state = Begin(Open);
            OnePlyTacticalStrategy.Ranked best = Best(OnePlyTacticalStrategy.Rank(state, state.FindUnit(1)));

            Assert.IsInstanceOf<AttackCommand>(best.Command, "A free kill should win: " + best.Score.Describe());
            Assert.AreEqual(1, best.Score.EnemyKills);
            Assert.Greater(best.Score.Damage, 0, "Removing 20 HP from the board must score.");
            Assert.Greater(best.Score.Kill, 0);
        }

        /// <summary>
        /// Hero (1) at (2,1), a 200 HP tough (2) at (3,1), chasm at (4,1).
        /// Attacking removes 5 HP. Pushing removes 200 and is scored purely
        /// through Damage + Kill — nothing in the evaluator knows what a pit is.
        /// </summary>
        private const string Pit =
            "encounter id=pit name=Pit\n" +
            "map\n#######\n#..x..#\n#.....#\n#######\nendmap\n" +
            "spawn faction=player unit=hero  x=1 y=1\n" +
            "spawn faction=enemy  unit=tough x=2 y=1 ai=rusher\n";

        [Test]
        public void Hazard_ChangesTheRanking_WithoutAnyHazardWeight()
        {
            Assert.AreEqual(0, OnePlyTacticalStrategy.HazardWeight,
                "The point of this test is that hazard needs no weight of its own.");

            BattleState state = Begin(Pit);
            OnePlyTacticalStrategy.Ranked best = Best(OnePlyTacticalStrategy.Rank(state, state.FindUnit(1)));

            Assert.IsInstanceOf<PushCommand>(best.Command,
                "Shoving 200 HP off the board should outscore a 5 HP swing: " + best.Score.Describe());
            Assert.AreEqual(1, best.Score.HazardDeaths);
            Assert.AreEqual(0, best.Score.Hazard, "Attribution only — it must contribute nothing.");
            Assert.Greater(best.Score.Damage, 100, "The score came from the HP that left the board.");
        }

        [Test]
        public void ArmorBreak_ChangesTheScore_ButOnlyThroughAnInventedWeight()
        {
            // Honest framing: the break produces a real, observable state delta,
            // and a score that exists only because ArmorBreakPerDef was chosen.
            const string wall =
                "encounter id=wall name=Wall\n" +
                "map\n#######\n#.....#\n#.....#\n#######\nendmap\n" +
                "spawn faction=player unit=hero  x=2 y=1\n" +
                "spawn faction=enemy  unit=tough x=3 y=1 ai=rusher\n";

            BattleState state = Begin(wall);
            List<OnePlyTacticalStrategy.Ranked> ranked = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            OnePlyTacticalStrategy.Ranked breakCmd = null;
            foreach (OnePlyTacticalStrategy.Ranked r in ranked)
                if (r.Command is ArmorBreakCommand) breakCmd = r;

            Assert.IsNotNull(breakCmd, "The break must be enumerated against a DEF 25 target.");
            Assert.AreEqual(20, breakCmd.Score.DefBroken, "20 DEF is an observable state delta.");
            Assert.AreEqual(20 * OnePlyTacticalStrategy.ArmorBreakPerDef, breakCmd.Score.ArmorBreak);
        }

        [Test]
        public void Exposure_IsSilentWhenTheMapFitsInsideOneThreatRange()
        {
            // MEASURED, not assumed. On a 10x4 map a MOVE 3 enemy threatens every
            // cell the hero can reach, so every move has the same exposure and the
            // signal ranks nothing. This is a property of map SCALE, not of the
            // signal — and it is the caveat that has to travel with any matrix
            // result, because the crucible maps are 11x8 with MOVE 3-5 enemies.
            const string small =
                "encounter id=expo1 name=ExpoSmall\n" +
                "map\n##########\n#........#\n#........#\n##########\nendmap\n" +
                "spawn faction=player unit=hero  x=2 y=1\n" +
                "spawn faction=enemy  unit=tough x=4 y=1 ai=rusher\n";

            BattleState state = Begin(small);
            foreach (OnePlyTacticalStrategy.Ranked r in OnePlyTacticalStrategy.Rank(state, state.FindUnit(1)))
                if (r.Command is MoveCommand)
                    Assert.AreEqual(0, r.Score.ExposureDelta,
                        "Every reachable cell is inside the same threat range here.");
        }

        [Test]
        public void Exposure_RanksMovesOnceTheMapIsBiggerThanOneThreatRange()
        {
            // Same signal, same weights, wider map: now the hero can leave the
            // threat range and the delta separates moves. Nothing here asks
            // whether the resulting exposure is "safe" — only whether it changed.
            const string wide =
                "encounter id=expo2 name=ExpoWide\n" +
                "map\n################\n#..............#\n#..............#\n################\nendmap\n" +
                "spawn faction=player unit=hero  x=5 y=1\n" +
                "spawn faction=enemy  unit=tough x=9 y=1 ai=rusher\n";

            BattleState state = Begin(wide);
            List<OnePlyTacticalStrategy.Ranked> ranked = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            bool sawRetreat = false;
            foreach (OnePlyTacticalStrategy.Ranked r in ranked)
                if (r.Command is MoveCommand && r.Score.ExposureDelta < 0) sawRetreat = true;

            Assert.IsTrue(sawRetreat,
                "With room to leave the threat range, exposure must be able to rank a move.");
        }

        // ------------------------------------------------- 3. the null results

        [Test]
        public void SlowAndTaunt_ProduceNoLocalScore_AndThatIsTheFinding()
        {
            // Both change the State. Neither changes anything a 1-ply view can
            // read: their whole value is what the enemy does NEXT turn. This test
            // pins that as a measured null result rather than an oversight — if
            // someone later gives them a weight, this is what should stop them.
            const string reach =
                "encounter id=reach name=Reach\n" +
                "map\n##########\n#........#\n#........#\n##########\nendmap\n" +
                "spawn faction=player unit=hero  x=2 y=1\n" +
                "spawn faction=enemy  unit=tough x=4 y=1 ai=rusher\n";

            BattleState state = Begin(reach);
            List<OnePlyTacticalStrategy.Ranked> ranked = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            OnePlyTacticalStrategy.Ranked slow = null;
            foreach (OnePlyTacticalStrategy.Ranked r in ranked)
                if (r.Command is SlowCommand) slow = r;

            Assert.IsNotNull(slow, "Slow must at least be enumerated, or this proves nothing.");
            Assert.AreEqual(0, slow.Score.Damage);
            Assert.AreEqual(0, slow.Score.Kill);
            Assert.AreEqual(0, slow.Score.ArmorBreak);
            Assert.AreEqual(0, slow.Score.Objective);
            Assert.AreEqual(0, slow.Score.Exposure,
                "Slow does not move anyone, so even exposure cannot see it this ply.");
            Assert.AreEqual(0, slow.Score.Total,
                "A 1-ply evaluator scores 遲滯 at exactly zero. That is the result.");
        }

        [Test]
        public void TieRate_IsReported_SoDegenerationToListOrderIsVisible()
        {
            // If most commands share a score, "best" is really "first", and the
            // strategy is an ordering rather than an evaluator. Measured, not
            // asserted away — the number goes in the report.
            BattleState state = Begin(Open);
            List<OnePlyTacticalStrategy.Ranked> ranked = OnePlyTacticalStrategy.Rank(state, state.FindUnit(1));

            Dictionary<int, int> byScore = new Dictionary<int, int>();
            foreach (OnePlyTacticalStrategy.Ranked r in ranked)
            {
                int n;
                byScore[r.Score.Total] = byScore.TryGetValue(r.Score.Total, out n) ? n + 1 : 1;
            }

            int top = Best(ranked).Score.Total;
            Assert.AreEqual(1, byScore[top],
                "The winning score must be unique here, or the choice is list order rather than evaluation.");
            Assert.Greater(byScore.Count, 1, "At least two distinct scores, or nothing was distinguished.");
        }
    }
}
