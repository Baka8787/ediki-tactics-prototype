using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;

namespace Ediki.Tests
{
    /// <summary>
    /// Inline fixtures for the rule tests.
    ///
    /// Tests deliberately do NOT read Assets/_Project/Resources/Data — designers
    /// are expected to tune those files, and a balance tweak must not break the
    /// rule suite. Only DataTests touches the shipped data.
    /// </summary>
    public static class TestWorld
    {
        public const string Terrain =
            "terrain name=Road symbol=. cost=1 blocks=false\n" +
            "terrain name=Forest symbol=f cost=2 blocks=false\n" +
            "terrain name=Blocking symbol=# cost=0 blocks=true\n";

        public const string Units =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        public const string AiProfiles =
            "aiprofile id=rusher target=nearest distance=1 aggression=90 retreatHp=0 guardHp=0\n";

        /// <summary>
        /// Hero flanked by a one-hit minion and a three-hit elite, all adjacent.
        ///
        /// Hero deals 25 a hit and can afford two attacks a turn, so the elite
        /// cannot die this turn and the second action is the "residue" the
        /// residue-aware strategy is built around. Ids: hero 1, minion 2, elite 3.
        ///
        /// The elite has to out-rank the minion on threat per action or there is
        /// no conflict to test: 70 damage a turn over 3 hits beats 20 over 1.
        /// </summary>
        public const string ResidueUnits =
            "unit id=hero   name=Hero   hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=minion name=Minion hp=20  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=elite  name=Elite  hp=60  atk=45 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        public const string ResidueEncounter =
            "encounter id=residue name=Residue\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero   x=3 y=1\n" +
            "spawn faction=enemy  unit=minion x=2 y=1 ai=rusher\n" +
            "spawn faction=enemy  unit=elite  x=4 y=1 ai=rusher\n";

        /// <summary>
        /// The residue fixture again, but the objective names the MINION.
        ///
        /// Deliberately the wrong unit by every other measure: threat per action
        /// ranks the elite first and residue-awareness only ever reaches for the
        /// minion with a spare action. Marking the minion is therefore the one
        /// setup where "hit the target" and "hit the best target" cannot agree,
        /// which is what makes the decapitate instrument falsifiable.
        /// </summary>
        public const string KillResidueEncounter =
            "encounter id=killresidue name=KillResidue\n" +
            "objective type=kill\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero   x=3 y=1\n" +
            "spawn faction=enemy  unit=minion x=2 y=1 ai=rusher target=true\n" +
            "spawn faction=enemy  unit=elite  x=4 y=1 ai=rusher\n";

        public static BattleState BegunResidue() => BegunWithResidueRoster(ResidueEncounter);

        public static BattleState BegunKillResidue() => BegunWithResidueRoster(KillResidueEncounter);

        private static BattleState BegunWithResidueRoster(string encounterText)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(Terrain);
            UnitCatalog units = UnitLoader.Parse(ResidueUnits);
            AiProfileCatalog profiles = AiProfileLoader.Parse(AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(encounterText, terrain);
            return BattleSimulator.Begin(EncounterLoader.CreateBattle(encounter, units, profiles).State).State;
        }

        /// <summary>
        ///   0123456
        /// 0 #######
        /// 1 #.....#   hero spawns at (1,1)
        /// 2 #.ff..#   forest costs 2
        /// 3 #.....#   grunt spawns at (5,3)
        /// 4 #######
        /// </summary>
        public const string Encounter =
            "encounter id=fixture name=Fixture\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero x=1 y=1\n" +
            "spawn faction=enemy unit=grunt x=5 y=3 ai=rusher\n";

        /// <summary>
        /// A wall with two gaps, for the M6 route probe.
        ///
        ///   0123456
        /// 0 #######
        /// 1 #.....#   grunt at (5,1)
        /// 2 ##.#.##   gaps at x=2 and x=4 — the wall row
        /// 3 #.....#   hero at (3,3)
        /// 4 #######
        ///
        /// From (3,3) the east gap is the shorter way to the grunt, and the hero
        /// clears the wall in ONE move that ends at (4,1). The crossing is only
        /// visible in the middle of that move's path, never in its destination.
        /// </summary>
        public const string CrossingEncounter =
            "encounter id=crossing name=Crossing\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "##.#.##\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero x=3 y=3\n" +
            "spawn faction=enemy unit=grunt x=5 y=1 ai=rusher\n";

        /// <summary>Same map, but the two units start side by side with full AP.</summary>
        public const string AdjacentEncounter =
            "encounter id=adjacent name=Adjacent\n" +
            "map\n" +
            "#######\n" +
            "#.....#\n" +
            "#.ff..#\n" +
            "#.....#\n" +
            "#######\n" +
            "endmap\n" +
            "spawn faction=player unit=hero x=4 y=3\n" +
            "spawn faction=enemy unit=grunt x=5 y=3 ai=rusher\n";

        /// <summary>
        /// Hero carrying the whole control kit, plus a wall that ignores pushes.
        /// One roster rather than three keeps the skill tests on a single fixture.
        /// </summary>
        public const string ControlUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 " +
                "tauntCost=2 tauntRadius=3 slowCost=2 slowRange=3 pushCost=2 pushRange=1\n" +
            "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n" +
            "unit id=rock  name=Rock  hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3 " +
                "immuneToPush=true\n";

        public static BattleSetup CreateControl(string encounterText)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(Terrain);
            UnitCatalog units = UnitLoader.Parse(ControlUnits);
            AiProfileCatalog profiles = AiProfileLoader.Parse(AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(encounterText, terrain);
            return EncounterLoader.CreateBattle(encounter, units, profiles);
        }

        /// <summary>
        /// A wider, emptier field than the standard fixture, because the control
        /// skills need room the 7x5 map does not have:
        ///
        ///   0123456789
        /// 0 ##########
        /// 1 #........#
        /// 2 #........#   hero (3,2), grunt (4,2) — two clear cells east of the
        /// 3 #........#   grunt, so a push has somewhere to land
        /// 4 ##########
        ///
        /// It is also long enough that AP, not the MOVE cap, decides how far the
        /// grunt gets — otherwise a slow cannot show up in reachability at all.
        /// </summary>
        public const string ControlEncounter =
            "encounter id=control name=Control\n" +
            "map\n" +
            "##########\n" +
            "#........#\n" +
            "#........#\n" +
            "#........#\n" +
            "##########\n" +
            "endmap\n" +
            "spawn faction=player unit=hero  x=3 y=2\n" +
            "spawn faction=enemy  unit=grunt x=4 y=2 ai=rusher\n";

        public static BattleState BegunControl() =>
            BattleSimulator.Begin(CreateControl(ControlEncounter).State).State;

        // ------------------------------------------- 破甲 ＋ lethal terrain

        /// <summary>
        /// The standard terrain plus a Chasm. A separate constant rather than an
        /// extra line on <see cref="Terrain"/>: every other fixture would then be
        /// parsing a hazard it never uses, and a shared fixture that grows for one
        /// test is how fixtures stop being readable.
        /// </summary>
        public const string HazardTerrain =
            "terrain name=Road symbol=. cost=1 blocks=false\n" +
            "terrain name=Forest symbol=f cost=2 blocks=false\n" +
            "terrain name=Blocking symbol=# cost=0 blocks=true\n" +
            "terrain name=Chasm symbol=x cost=1 blocks=false lethal=true\n";

        /// <summary>
        /// Hero carries 破甲 and 擊退; the grunt is armoured enough that the break
        /// is unmissable.
        ///
        /// DEF 25 against ATK 30 is 5 a hit — 8 hits through 40 HP. Break 20 off
        /// and it is 25 a hit, 2 hits. A test that cannot tell a working break
        /// from a no-op is worth nothing, so the gap is deliberately enormous.
        /// </summary>
        public const string BreakerUnits =
            "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=4 guardCost=3 " +
                "armorBreakCost=2 armorBreakRange=1 armorBreakAmount=20 pushCost=2 pushRange=1\n" +
            "unit id=grunt name=Grunt hp=40  atk=20 def=25 move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

        /// <summary>
        ///   0123456789
        /// 0 ##########
        /// 1 #........#
        /// 2 #..x.....#   chasm at (3,2)
        /// 3 #........#
        /// 4 ##########
        ///
        /// Hero (1,2), grunt (2,2): the push line runs straight into the chasm, so
        /// one command covers "shoved into a hazard" without any repositioning.
        /// </summary>
        public const string HazardEncounter =
            "encounter id=hazard name=Hazard\n" +
            "map\n" +
            "##########\n" +
            "#........#\n" +
            "#..x.....#\n" +
            "#........#\n" +
            "##########\n" +
            "endmap\n" +
            "spawn faction=player unit=hero  x=1 y=2\n" +
            "spawn faction=enemy  unit=grunt x=2 y=2 ai=rusher\n";

        public static BattleSetup CreateHazard(string encounterText)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(HazardTerrain);
            UnitCatalog units = UnitLoader.Parse(BreakerUnits);
            AiProfileCatalog profiles = AiProfileLoader.Parse(AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(encounterText, terrain);
            return EncounterLoader.CreateBattle(encounter, units, profiles);
        }

        public static BattleState BegunHazard() =>
            BattleSimulator.Begin(CreateHazard(HazardEncounter).State).State;

        public const int HeroId = 1;
        public const int GruntId = 2;

        public static BattleSetup Create() => Create(Encounter);

        public static BattleSetup Create(string encounterText)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(Terrain);
            UnitCatalog units = UnitLoader.Parse(Units);
            AiProfileCatalog profiles = AiProfileLoader.Parse(AiProfiles);
            EncounterDef encounter = EncounterLoader.Parse(encounterText, terrain);
            return EncounterLoader.CreateBattle(encounter, units, profiles);
        }

        /// <summary>Fixture with the first TurnStarted already emitted.</summary>
        public static BattleState Begun() => BattleSimulator.Begin(Create().State).State;

        /// <summary>Hero and grunt adjacent, both on a full AP bar.</summary>
        public static BattleState BegunAdjacent() =>
            BattleSimulator.Begin(Create(AdjacentEncounter).State).State;

        public static BattleState Apply(BattleState state, ICommand command)
        {
            ExecuteResult r = BattleSimulator.Execute(state, command);
            if (!r.Ok) throw new System.InvalidOperationException("Command rejected: " + r.RejectReason);
            return r.State;
        }

        public static Coord[] Path(params Coord[] steps) => steps;

        public static Coord C(int x, int y) => new Coord(x, y);
    }
}
