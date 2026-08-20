using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The three defects the 2026-08-17 research notes found by reading the code,
    /// plus the per-round caps that close the carry-over hole.
    ///
    /// All four were found on paper before any of them was ever observed in a
    /// batch. That is worth saying out loud: "no run has hit it" was never
    /// evidence of safety, only of strategy coverage.
    /// </summary>
    public class HardeningTests
    {
        // ------------------------------- 1. reinforcement deadlock (T3 §6)

        private const string BlockedWaveTerrain =
            "terrain name=Road symbol=. cost=1 blocks=false\n" +
            "terrain name=Blocking symbol=# cost=0 blocks=true\n";

        private const string WaveUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        /// <summary>Hero starts ON the cell the wave is declared at.</summary>
        private const string BlockedWaveEncounter =
            "encounter id=blockedwave name=BlockedWave\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.....#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero  x=3 y=2\n" +
            "spawn faction=enemy  unit=grunt x=3 y=2 ai=rusher turn=2\n";

        private static BattleState BegunWave()
        {
            TerrainCatalog terrain = TerrainLoader.Parse(BlockedWaveTerrain);
            UnitCatalog units = UnitLoader.Parse(WaveUnits);
            AiProfileCatalog profiles = AiProfileLoader.Parse(TestWorld.AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(BlockedWaveEncounter, terrain);
            return BattleSimulator.Begin(
                EncounterLoader.CreateBattle(encounter, units, profiles).State).State;
        }

        [Test]
        public void BlockedReinforcement_StillArrives_SoTheBattleCanEnd()
        {
            // The defect: a wave whose cell was occupied on its declared turn was
            // skipped, never retried (the guard compared Turn != TurnIndex), and
            // left Spawned false forever. HasPendingReinforcements only reads
            // Spawned, and victory is gated on it — so the player could clear the
            // field and the battle would never end. R-WIN-04: no round cap, no
            // draw, no way out.
            BattleState state = BegunWave();
            Assert.IsTrue(state.HasPendingReinforcements(Faction.Enemy));

            // Hero never moves, so the declared cell stays occupied throughout.
            for (int i = 0; i < 4 && state.HasPendingReinforcements(Faction.Enemy); i++)
            {
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.IsFalse(state.HasPendingReinforcements(Faction.Enemy),
                "A permanently blocked wave must still resolve, or victory is unreachable.");
            Assert.AreEqual(1, state.CountLiving(Faction.Enemy), "It arrives, relocated.");

            UnitState arrived = null;
            foreach (UnitState u in state.LivingUnitsOf(Faction.Enemy)) arrived = u;
            Assert.AreNotEqual(TestWorld.C(3, 2), arrived.Position, "Displaced off the occupied cell.");
            Assert.LessOrEqual(Coord.ManhattanDistance(arrived.Position, TestWorld.C(3, 2)), 2,
                "But displaced NEAR it — the author put the wave there for a reason.");
        }

        [Test]
        public void UnblockedReinforcement_StillArrivesExactlyWhereAndWhenDeclared()
        {
            // The fix must not move waves that were never blocked.
            const string clear =
                "encounter id=clearwave name=ClearWave\n" +
                "map\n" +
                "#######\n" +
                "#.....#\n" +
                "#.....#\n" +
                "#.....#\n" +
                "#######\n" +
                "endmap\n" +
                "spawn faction=player unit=hero  x=1 y=1\n" +
                "spawn faction=enemy  unit=grunt x=5 y=3 ai=rusher turn=2\n";

            TerrainCatalog terrain = TerrainLoader.Parse(BlockedWaveTerrain);
            EncounterDef encounter = EncounterLoader.Parse(clear, terrain);
            BattleState state = BattleSimulator.Begin(EncounterLoader.CreateBattle(
                encounter, UnitLoader.Parse(WaveUnits),
                AiProfileLoader.Parse(TestWorld.AiProfiles)).State).State;

            Assert.AreEqual(0, state.CountLiving(Faction.Enemy), "Turn 1: not due yet.");

            // A wave lands at the start of ITS OWN faction's phase, so an enemy
            // wave declared for turn 2 arrives on the enemy phase of turn 2 —
            // three phase changes away, not two.
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));   // -> enemy, T1
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));    // -> player, T2
            Assert.AreEqual(0, state.CountLiving(Faction.Enemy), "Still the player's phase.");

            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));   // -> enemy, T2

            Assert.AreEqual(1, state.CountLiving(Faction.Enemy));
            foreach (UnitState u in state.LivingUnitsOf(Faction.Enemy))
                Assert.AreEqual(TestWorld.C(5, 3), u.Position, "Exactly where declared.");
        }

        // ------------------------------- 2. target distance sees walls (T2 §6)

        /// <summary>
        ///   01234567
        /// 0 ########
        /// 1 #.#....#   sniper (1,1)   decoy (3,1)
        /// 2 #.#....#
        /// 3 #......#
        /// 4 #.#....#   bait (1,4)
        /// 5 ########
        ///
        /// decoy is Manhattan 2 away but SIX cells by path; bait is Manhattan 3
        /// and three by path. Attack range ignores walls (a known rule property),
        /// so both are shootable — which is exactly what made the old Manhattan
        /// target choice visible as a wrong answer rather than a harmless one.
        /// </summary>
        private const string WallTerrain =
            "terrain name=Road symbol=. cost=1 blocks=false\n" +
            "terrain name=Blocking symbol=# cost=0 blocks=true\n";

        private const string WallUnits =
            "unit id=sniper name=Sniper hp=60 atk=30 def=10 move=3 ap=8 range=3 attackCost=4 guardCost=3\n" +
            "unit id=decoy  name=Decoy  hp=90 atk=10 def=10 move=3 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=bait   name=Bait   hp=90 atk=10 def=10 move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        private const string WallEncounter =
            "encounter id=walltarget name=WallTarget\n" +
            "map\n" +
            "########\n" +
            "#.#....#\n" +
            "#.#....#\n" +
            "#......#\n" +
            "#.#....#\n" +
            "########\n" +
            "endmap\n" +
            "spawn faction=enemy  unit=sniper x=1 y=1 ai=rusher\n" +
            "spawn faction=player unit=decoy  x=3 y=1\n" +
            "spawn faction=player unit=bait   x=1 y=4\n";

        [Test]
        public void NearestTarget_MeasuresPathDistance_NotStraightLine()
        {
            // OD-18 fixed this for SelectPosition in 2026-08-14 and the same fix
            // was never applied to SelectTarget, so a unit would pick a target that
            // was near through a wall and then walk the long way round to it.
            TerrainCatalog terrain = TerrainLoader.Parse(WallTerrain);
            EncounterDef encounter = EncounterLoader.Parse(WallEncounter, terrain);
            BattleSetup setup = EncounterLoader.CreateBattle(
                encounter, UnitLoader.Parse(WallUnits), AiProfileLoader.Parse(TestWorld.AiProfiles));

            BattleState state = BattleSimulator.Begin(setup.State).State;
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));

            UnitState sniper = null;
            foreach (UnitState u in state.LivingUnitsOf(Faction.Enemy)) sniper = u;
            ICommand decision = setup.Ai.DecideNext(state, sniper);

            AttackCommand attack = decision as AttackCommand;
            Assert.IsNotNull(attack, "Both targets are inside range 3, so it should shoot: " + decision.Describe());

            UnitState chosen = state.FindUnit(attack.TargetId);
            Assert.AreEqual("bait", chosen.Def.Id,
                "Manhattan says the decoy (2 vs 3); the path says the bait (3 vs 6). The path wins.");
        }

        // ------------------------------------------ 3. per-round caps

        private const string CappedUnits =
            // 10 AP and a 5 AP attack: without the cap this unit swings twice.
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=10 apRegen=8 range=1 " +
                "attackCost=5 guardCost=3 pushCost=1 pushRange=1 attacksPerRound=1 skillUsesPerRound=1\n" +
            "unit id=grunt name=Grunt hp=400 atk=20 def=5 move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        private static BattleState BegunCapped()
        {
            TerrainCatalog terrain = TerrainLoader.Parse(TestWorld.Terrain);
            EncounterDef encounter = EncounterLoader.Parse(TestWorld.ControlEncounter, terrain);
            return BattleSimulator.Begin(EncounterLoader.CreateBattle(
                encounter, UnitLoader.Parse(CappedUnits),
                AiProfileLoader.Parse(TestWorld.AiProfiles)).State).State;
        }

        [Test]
        public void AttackCap_ClosesTheCarryOverHole()
        {
            // Ap = min(kept + 8, 10), so banking 2 AP hands a 5 AP attacker two
            // swings out of a cost priced to allow one. AP alone cannot express
            // that limit, which is the whole reason the cap is a separate field.
            BattleState state = BegunCapped();
            UnitState hero = state.FindUnit(TestWorld.HeroId);
            hero.Ap = 10;   // the state a banked round arrives in

            BattleState once = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.AreEqual(5, once.FindUnit(TestWorld.HeroId).Ap, "Enough AP left for a second swing...");

            ExecuteResult twice = BattleSimulator.Execute(once,
                new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.IsFalse(twice.Ok, "...but the per-round cap refuses it.");
        }

        [Test]
        public void SkillCap_CountsTheWholeKitTogether()
        {
            BattleState state = BegunCapped();

            BattleState once = TestWorld.Apply(state, new PushCommand(TestWorld.HeroId, TestWorld.GruntId));
            ExecuteResult twice = BattleSimulator.Execute(once,
                new PushCommand(TestWorld.HeroId, TestWorld.GruntId));

            Assert.IsFalse(twice.Ok, "1 AP skills otherwise permit eight activations in a round.");
        }

        [Test]
        public void CappedCommands_AreNotEvenListedAsLegal()
        {
            // Load-bearing. LegalCommands is what the batch runner's 15% uniform
            // noise samples from; a command left in the list that the simulator
            // then rejects does not just fail, it burns the unit's activation and
            // is recorded as a decision the strategy made.
            BattleState state = BegunCapped();
            BattleState after = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));

            List<ICommand> legal = LegalCommands.For(after, after.FindUnit(TestWorld.HeroId));

            foreach (ICommand c in legal)
                Assert.IsNotInstanceOf<AttackCommand>(c, "A spent attack must not be offered again.");
        }

        [Test]
        public void ARosterWithNoCapsHashesExactlyAsItAlwaysDid()
        {
            // Same containment discipline as contamination, kill targets and 破甲:
            // new state costs the A4 golden constants nothing until it is used.
            BattleState uncapped = TestWorld.BegunControl();
            Assert.IsFalse(uncapped.HasPerRoundCaps);

            BattleState capped = BegunCapped();
            Assert.IsTrue(capped.HasPerRoundCaps, "The gate must open for a capped roster.");
        }

        [Test]
        public void CapCountersResetEachRound()
        {
            BattleState state = BegunCapped();
            BattleState spent = TestWorld.Apply(state, new AttackCommand(TestWorld.HeroId, TestWorld.GruntId));
            Assert.AreEqual(1, spent.FindUnit(TestWorld.HeroId).AttacksThisRound);

            BattleState nextRound = TestWorld.Apply(
                TestWorld.Apply(spent, new EndTurnCommand(Faction.Player)),
                new EndTurnCommand(Faction.Enemy));

            Assert.AreEqual(0, nextRound.FindUnit(TestWorld.HeroId).AttacksThisRound,
                "A cap that never resets is a one-shot, not a per-round limit.");
        }
    }
}
