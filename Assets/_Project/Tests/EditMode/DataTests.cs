using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Ediki.Tests
{
    /// <summary>
    /// Parser behaviour, plus a guard rail over the data designers actually edit.
    /// A typo in stage01.encounter.txt should fail here, not at runtime.
    /// </summary>
    public class DataTests
    {
        // ------------------------------------------------------- parser basics

        [Test]
        public void Terrain_ParsesCostAndBlocking()
        {
            TerrainCatalog catalog = TerrainLoader.Parse(TestWorld.Terrain);

            TerrainDef road, forest, blocking;
            Assert.IsTrue(catalog.TryGetBySymbol('.', out road));
            Assert.IsTrue(catalog.TryGetBySymbol('f', out forest));
            Assert.IsTrue(catalog.TryGetBySymbol('#', out blocking));

            Assert.AreEqual(1, road.MovementCost);
            Assert.AreEqual(2, forest.MovementCost);
            Assert.IsTrue(blocking.BlocksMovement);
        }

        [Test]
        public void Terrain_PassableTerrainMustCostAtLeastOne()
        {
            Assert.Throws<DataFormatException>(() =>
                TerrainLoader.Parse("terrain name=Free symbol=. cost=0 blocks=false\n"));
        }

        [Test]
        public void Terrain_DuplicateSymbolIsRejected()
        {
            Assert.Throws<System.ArgumentException>(() => TerrainLoader.Parse(
                "terrain name=A symbol=. cost=1 blocks=false\n" +
                "terrain name=B symbol=. cost=1 blocks=false\n"));
        }

        [Test]
        public void MissingRequiredKey_ReportsTheLineNumberAndKey()
        {
            DataFormatException ex = Assert.Throws<DataFormatException>(() =>
                UnitLoader.Parse("unit id=x name=X hp=10 atk=1 def=1 move=1 ap=8 attackCost=4\n"));

            StringAssert.Contains("guardCost", ex.Message);
            StringAssert.Contains("Line 1", ex.Message);
        }

        [Test]
        public void CommentsAndBlankLinesAreIgnored()
        {
            UnitCatalog catalog = UnitLoader.Parse(
                "# a comment\n" +
                "\n" +
                "unit id=x name=X hp=10 atk=1 def=1 move=1 ap=8 range=1 attackCost=4 guardCost=3\n" +
                "   # indented comment\n");

            Assert.IsNotNull(catalog.Get("x"));
        }

        [Test]
        public void MalformedTokenIsRejected()
        {
            Assert.Throws<DataFormatException>(() => UnitLoader.Parse("unit id=x nonsense\n"));
        }

        // ------------------------------------------------- encounter validation

        [Test]
        public void Encounter_RaggedMapIsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=bad name=Bad\n" +
                "map\n" +
                "#####\n" +
                "#...#\n" +
                "####\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=3 y=1 ai=rusher\n"));
        }

        [Test]
        public void Encounter_UndeclaredSymbolIsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=bad name=Bad\n" +
                "map\n" +
                "#####\n" +
                "#.Z.#\n" +
                "#####\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=3 y=1 ai=rusher\n"));
        }

        [Test]
        public void Encounter_SpawnOnBlockingTerrainIsRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=bad name=Bad\n" +
                "map\n" +
                "#####\n" +
                "#...#\n" +
                "#####\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=0 y=0\n" +
                "spawn faction=enemy unit=grunt x=3 y=1 ai=rusher\n"));
        }

        [Test]
        public void Encounter_UnreachableEnemyIsRejected()
        {
            // R-MAP-02: victory must be achievable.
            DataFormatException ex = Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=bad name=Bad\n" +
                "map\n" +
                "#######\n" +
                "#..#..#\n" +
                "#..#..#\n" +
                "#######\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=5 y=1 ai=rusher\n"));

            StringAssert.Contains("unreachable", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Encounter_OverlappingSpawnsAreRejected()
        {
            Assert.Throws<DataFormatException>(() => TestWorld.Create(
                "encounter id=bad name=Bad\n" +
                "map\n" +
                "#####\n" +
                "#...#\n" +
                "#####\n" +
                "endmap\n" +
                "spawn faction=player unit=hero x=1 y=1\n" +
                "spawn faction=enemy unit=grunt x=1 y=1 ai=rusher\n"));
        }

        // ------------------------------------------------ the data we actually ship

        [Test]
        public void ShippedStage01Data_LoadsAndValidates()
        {
            BattleSetup setup = LoadShippedStage01();

            Assert.AreEqual(12, setup.State.Map.Width);
            Assert.AreEqual(10, setup.State.Map.Height);
            Assert.AreEqual(1, setup.State.CountLiving(Faction.Player));
            Assert.AreEqual(4, setup.State.CountLiving(Faction.Enemy), "GDD Stage 01: four kohaku.");
        }

        [Test]
        public void ShippedStage01Map_HasAChokepointAndABait()
        {
            // The map only earns its keep if it offers different exposures.
            BattleMap map = LoadShippedStage01().State.Map;

            int golden = BattleQueries.StaticExposure(map, TestWorld.C(5, 5));
            int bait = BattleQueries.StaticExposure(map, TestWorld.C(5, 7));

            Assert.AreEqual(2, golden, "(5,5) should be walled east and west.");
            Assert.AreEqual(4, bait, "(5,7) highland should be open on all sides.");
            Assert.Less(golden, bait, "Without an exposure gradient the map teaches nothing (M5).");
        }

        [Test]
        public void ShippedStage01_NoEnemyThreatensThePlayerSpawn()
        {
            // GDD Stage 01: "the player has to move to make contact".
            BattleSetup setup = LoadShippedStage01();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            foreach (UnitState player in state.LivingUnitsOf(Faction.Player))
                Assert.AreEqual(0, BattleQueries.EffectiveExposure(state, player.Position, Faction.Player),
                    "Nothing should be able to reach the player on turn 1.");

            foreach (UnitState enemy in state.LivingUnitsOf(Faction.Enemy))
                Assert.IsFalse(enemy.IsActivated, "No enemy should start awake.");
        }

        [Test]
        public void EveryShippedEncounter_LoadsAndValidates()
        {
            // Encounters are added constantly and are pure data, so nothing else
            // would catch a bad map until someone opened it. Connectivity and
            // spawn legality are checked by the loader itself.
            TerrainCatalog terrain = TerrainLoader.Parse(Read("terrain"));
            UnitCatalog units = UnitLoader.Parse(Read("units"));
            AiProfileCatalog profiles = AiProfileLoader.Parse(Read("ai-profiles"));

            TextAsset[] assets = Resources.LoadAll<TextAsset>("Data");
            int encounters = 0;

            foreach (TextAsset asset in assets)
            {
                if (!asset.name.EndsWith(".encounter")) continue;
                encounters++;

                try
                {
                    EncounterDef def = EncounterLoader.Parse(asset.text, terrain);
                    EncounterLoader.CreateBattle(def, units, profiles);
                }
                catch (System.Exception ex)
                {
                    Assert.Fail(asset.name + " failed to load: " + ex.Message);
                }
            }

            Assert.Greater(encounters, 0, "No .encounter assets found under Resources/Data.");
        }

        [Test]
        public void ShippedGymLanes_KeepsTheThreeRoutesM6Counts()
        {
            // M6 reads the x of the first player step onto the wall row, so the
            // wall row IS the metric's definition. If someone re-cuts this map,
            // the probe row in the metrics run has to move with it.
            BattleMap map = LoadShipped("gym-lanes.encounter").State.Map;

            Assert.AreEqual(24, map.Width);
            Assert.AreEqual(16, map.Height);

            System.Collections.Generic.List<int> gaps = new System.Collections.Generic.List<int>();
            for (int x = 0; x < map.Width; x++)
                if (map.IsPassable(TestWorld.C(x, 8))) gaps.Add(x);

            CollectionAssert.AreEqual(new[] { 3, 11, 12, 20 }, gaps,
                "Wall row y=8 carries three routes: west x=3, mid x=11-12 (two cells wide), east x=20.");
        }

        [Test]
        public void ShippedD1Trio_KeepsTheGeometryItsConclusionsRestOn()
        {
            // The D1 result (playtest-metrics §22) is a comparison between three
            // maps that must differ in EXACTLY the intended cells. Re-cutting any
            // of them silently turns a controlled pair into two unrelated maps,
            // and the histograms would still look perfectly reasonable.
            BattleMap routes = LoadShipped("gym-d1-routes.encounter").State.Map;
            BattleMap noforest = LoadShipped("gym-d1-noforest.encounter").State.Map;
            BattleMap flat = LoadShipped("gym-d1-flat.encounter").State.Map;

            CollectionAssert.AreEqual(new[] { 2, 5, 6, 7, 10 }, GapsOn(routes, 5),
                "Probe row y=5: west x=2, mid x=5-7 (three wide), east x=10.");
            CollectionAssert.AreEqual(new[] { 2, 5, 6, 7, 10 }, GapsOn(noforest, 5),
                "The middle rung differs from gym-d1-routes in TERRAIN only, so its gaps must match.");
            CollectionAssert.AreEqual(new[] { 2, 6, 10 }, GapsOn(flat, 5),
                "The control narrows the mid lane to one cell; west and east stay where they are.");

            // Terrain is the other half of the variable, and it is the half that
            // moved M3 by 3.7 rounds when it changed (§22.6).
            Assert.AreEqual(2, routes.MovementCost(TestWorld.C(10, 5)), "East gate is forest on the experiment map.");
            Assert.AreEqual(1, noforest.MovementCost(TestWorld.C(10, 5)), "Removing the forest is the middle rung's whole content.");
            Assert.AreEqual(1, flat.MovementCost(TestWorld.C(10, 5)), "The control has no forest either.");

            // Both players spawn on the symmetry axis on purpose: side-by-side
            // spawns would split the party by which lane it started nearer, and
            // the per-unit reading (F2) would confirm itself.
            foreach (BattleMap map in new[] { routes, noforest, flat })
            {
                Assert.AreEqual(13, map.Width);
                Assert.AreEqual(12, map.Height);
                for (int y = 2; y <= 5; y++)
                    Assert.AreEqual(map.IsPassable(TestWorld.C(2, y)), map.IsPassable(TestWorld.C(10, y)),
                        "West and east lanes must stay mirror images at y=" + y + ", or the two lanes differ in more than terrain.");
            }
        }

        private static System.Collections.Generic.List<int> GapsOn(BattleMap map, int row)
        {
            System.Collections.Generic.List<int> gaps = new System.Collections.Generic.List<int>();
            for (int x = 0; x < map.Width; x++)
                if (map.IsPassable(TestWorld.C(x, row))) gaps.Add(x);
            return gaps;
        }

        [Test]
        public void ShippedGymLanes_RoutesDifferInExposure()
        {
            // Three routes that are equally safe are one route drawn three times.
            BattleMap map = LoadShipped("gym-lanes.encounter").State.Map;

            int west = BattleQueries.StaticExposure(map, TestWorld.C(3, 5));
            int mid = BattleQueries.StaticExposure(map, TestWorld.C(11, 5));
            int east = BattleQueries.StaticExposure(map, TestWorld.C(20, 5));

            Assert.AreEqual(2, west, "West is a 1-wide corridor.");
            Assert.AreEqual(4, mid, "Mid is open field.");
            Assert.AreEqual(3, east, "East is a 2-wide corridor.");
            Assert.Less(west, mid, "Without an exposure gradient the routes are interchangeable.");
        }

        [Test]
        public void ShippedZhengshou_MatchesTheGddStatBlock()
        {
            // GDD character report, "基礎數值 (Ver. RuleLock)". HIT 0.68 / EVA 0.10
            // are in the GDD too and are deliberately unused under OD-05.
            UnitDef z = UnitLoader.Parse(Read("units")).Get("zhengshou");

            Assert.AreEqual(435, z.MaxHp);
            Assert.AreEqual(33, z.Atk);
            Assert.AreEqual(70, z.Def);
            Assert.AreEqual(3, z.Move);

            UnitDef kohaku = UnitLoader.Parse(Read("units")).Get("kohaku");
            Assert.AreEqual(30, BattleRules.ComputeDamage(kohaku.Atk, z.Def, false),
                "Zhengshou takes 30 a hit where Momotaro takes 50 — that is the point of him.");
        }

        [Test]
        public void ShippedRoleVariants_AreCompleteAtFourByTwo()
        {
            // The 4x2 research target: four characters, two recognisable roles
            // each. EXPERIMENTAL, not a GDD commitment (OD-33) — this test guards
            // that the set stays loadable and internally distinct, not that the
            // design is final.
            UnitCatalog roster = UnitLoader.Parse(Read("units"));

            string[] ids =
            {
                "Momotaro_A", "Momotaro_B",
                "Genjin_A",   "Genjin_B",
                "Kagemaru_A", "Kagemaru_B",
                "Masamori_A", "Masamori_B"
            };

            foreach (string id in ids)
            {
                UnitDef def;
                Assert.IsTrue(roster.TryGet(id, out def), id + " is missing from the shipped roster.");
                Assert.Greater(def.MaxHp, 0, id + " has no HP.");
                Assert.Greater(def.AttackApCost, 0, id + " has no attack cost.");
            }

            // Each pair must differ in something a player could name, or the "two
            // roles" claim is decoration. Asserted per pair rather than in bulk so
            // a failure says which character stopped being two characters.
            UnitDef mA = roster.Get("Momotaro_A"), mB = roster.Get("Momotaro_B");
            Assert.AreNotEqual(mA.AttackApCost, mB.AttackApCost, "Momotaro A/B: throughput vs displacement.");
            Assert.IsFalse(mA.CanPush);
            Assert.IsTrue(mB.CanPush);

            UnitDef gA = roster.Get("Genjin_A"), gB = roster.Get("Genjin_B");
            Assert.AreNotEqual(gA.AttackApCost, gB.AttackApCost, "Genjin A/B: one big hit vs setting one up.");
            Assert.IsFalse(gA.CanArmorBreak);
            Assert.IsTrue(gB.CanArmorBreak);

            UnitDef kA = roster.Get("Kagemaru_A"), kB = roster.Get("Kagemaru_B");
            Assert.AreNotEqual(kA.AttackRange, kB.AttackRange, "Kagemaru A/B: reach vs mobility.");
            Assert.Less(kA.Move, kB.Move, "The short-ranged build is the faster one or it is just worse.");
            Assert.IsTrue(kA.CanSlow);
            Assert.IsFalse(kB.CanSlow);

            UnitDef zA = roster.Get("Masamori_A"), zB = roster.Get("Masamori_B");
            Assert.Greater(zA.Def, zB.Def, "Masamori A/B: raw wall vs reactive defence.");
            Assert.IsTrue(zA.CanTaunt);
            Assert.IsTrue(zB.CanCounter);
        }

        [Test]
        public void ShippedGenjinB_BreaksArmourAcrossAHitCountStep()
        {
            // The break is sized against the hit-count ladder, not for feel: this
            // project measured that ladder as a cliff (2 hits 68%, 3 hits 1%), so
            // a break that crosses no step is worth nothing at all. If someone
            // retunes it, this is the number that should stop them.
            UnitCatalog roster = UnitLoader.Parse(Read("units"));
            UnitDef genjinB = roster.Get("Genjin_B");
            UnitDef momotaro = roster.Get("momotaro");
            UnitDef elite = roster.Get("kohaku_3hit");

            int intact = BattleRules.ComputeDamage(momotaro.Atk, elite.Def, false);
            int broken = BattleRules.ComputeDamage(momotaro.Atk, elite.Def - genjinB.ArmorBreakAmount, false);

            Assert.AreEqual(3, Hits(elite.MaxHp, intact), "120 HP at 40 a hit is three.");
            Assert.AreEqual(2, Hits(elite.MaxHp, broken), "Broken, it must become two — that is the whole point.");
        }

        private static int Hits(int hp, int perHit) => (hp + perHit - 1) / perHit;

        [Test]
        public void ShippedRoster_IsStillPerfectlyDivisible()
        {
            // The research notes' central claim about this project's numbers:
            // Momotaro deals 40 a hit, kohaku has 80 HP, and he can afford two
            // attacks a turn — so a kill costs exactly one turn and no spare
            // action ever exists. Everything those notes say about focus fire
            // being the only play follows from these four numbers, so they are
            // worth a test that fails loudly if anyone edits them.
            EncounterDef encounter = EncounterLoader.Parse(Read("gym-arena-contact.encounter"),
                                                           TerrainLoader.Parse(Read("terrain")));
            System.Collections.Generic.List<EncounterProfile.Profile> profiles =
                EncounterProfile.Build(encounter, UnitLoader.Parse(Read("units")));

            Assert.AreEqual(1, profiles.Count);
            EncounterProfile.Profile momotaro = profiles[0];

            Assert.AreEqual(2, momotaro.AttacksPerTurn, "8 AP regen / 4 AP an attack.");
            Assert.AreEqual(3, momotaro.LethalExposure, "Three kohaku attacking at once is 300 damage.");

            Assert.AreEqual(1, momotaro.Enemies.Count);
            Assert.AreEqual(2, momotaro.Enemies[0].RequiredHits);
            Assert.AreEqual(0, momotaro.Enemies[0].Residue, "No remainder means no spare action to spend.");
        }

        [Test]
        public void ResidueArena_ActuallyLeavesASpareAction()
        {
            // gym-arena-residue only earns its keep if the elite really does fail
            // to divide the action budget. If someone retunes kohaku_3hit's HP,
            // the arena silently stops measuring what it was built to measure.
            EncounterDef encounter = EncounterLoader.Parse(Read("gym-arena-residue.encounter"),
                                                           TerrainLoader.Parse(Read("terrain")));
            EncounterProfile.Profile p =
                EncounterProfile.Build(encounter, UnitLoader.Parse(Read("units")))[0];

            EncounterProfile.EnemyRow elite = null, minion = null;
            for (int i = 0; i < p.Enemies.Count; i++)
            {
                if (p.Enemies[i].EnemyId == "kohaku_3hit") elite = p.Enemies[i];
                if (p.Enemies[i].EnemyId == "kohaku_1hit") minion = p.Enemies[i];
            }

            Assert.IsNotNull(elite);
            Assert.IsNotNull(minion);
            Assert.AreEqual(3, elite.RequiredHits);
            Assert.AreEqual(1, elite.Residue, "Three hits against two attacks a turn leaves one action over.");
            Assert.AreEqual(1, minion.RequiredHits, "The spare action has to be able to buy a whole kill.");
            Assert.Greater(elite.ThreatPerActionX100, minion.ThreatPerActionX100,
                "The formula must prefer the elite, or the arena is not testing a formula failure.");
        }

        [Test]
        public void ShippedStage01_RunsToCompletionWithoutIllegalState()
        {
            BattleSetup setup = LoadShippedStage01();
            BattleState state = BattleSimulator.Begin(setup.State).State;

            // Blunt strategy: walk at the nearest enemy and swing. It loses — that is
            // fine. What matters is that a full game runs without breaking any invariant.
            for (int round = 0; round < 40 && state.Outcome == BattleOutcome.InProgress; round++)
            {
                state = PlayOneCrudePlayerTurn(state);
                if (state.Outcome != BattleOutcome.InProgress) break;

                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
                if (state.Outcome != BattleOutcome.InProgress) break;

                state = setup.Ai.RunFactionTurn(state, Faction.Enemy, new EffectLog()).State;
                if (state.Outcome != BattleOutcome.InProgress) break;

                state = TestWorld.Apply(state, new EndTurnCommand(Faction.Enemy));
            }

            Assert.AreNotEqual(BattleOutcome.InProgress, state.Outcome,
                "A full Stage 01 game did not resolve in 40 rounds. See OD-16 (stalemate).");
        }

        private static BattleState PlayOneCrudePlayerTurn(BattleState state)
        {
            UnitState hero = null;
            foreach (UnitState u in state.LivingUnitsOf(Faction.Player)) { hero = u; break; }
            if (hero == null) return state;

            for (int step = 0; step < 8; step++)
            {
                hero = state.FindUnit(hero.Id);
                if (hero == null || !hero.IsAlive || state.Outcome != BattleOutcome.InProgress) break;

                var targets = BattleQueries.AttackableTargets(state, hero);
                if (targets.Count > 0 && hero.Ap >= hero.Def.AttackApCost)
                {
                    ExecuteResult attack = BattleSimulator.Execute(state, new AttackCommand(hero.Id, targets[0].Id));
                    if (!attack.Ok) break;
                    state = attack.State;
                    continue;
                }

                UnitState nearest = null;
                int bestDistance = int.MaxValue;
                foreach (UnitState e in state.LivingUnitsOf(Faction.Enemy))
                {
                    int d = Coord.ManhattanDistance(hero.Position, e.Position);
                    if (d < bestDistance) { bestDistance = d; nearest = e; }
                }
                if (nearest == null) break;

                ReachabilityMap reach = MovementCalculator.ComputeFor(state, hero);
                Coord best = hero.Position;
                int bestScore = int.MaxValue;
                foreach (Coord c in reach.ReachableCells)
                {
                    int d = Coord.ManhattanDistance(c, nearest.Position);
                    if (d < bestScore) { bestScore = d; best = c; }
                }
                if (best == hero.Position) break;

                Coord[] path = reach.PathTo(best);
                if (path == null || path.Length == 0) break;

                ExecuteResult move = BattleSimulator.Execute(state, new MoveCommand(hero.Id, path));
                if (!move.Ok) break;
                state = move.State;
            }

            return state;
        }

        private static BattleSetup LoadShippedStage01() => LoadShipped("stage01.encounter");

        private static BattleSetup LoadShipped(string encounterResource)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(Read("terrain"));
            UnitCatalog units = UnitLoader.Parse(Read("units"));
            AiProfileCatalog profiles = AiProfileLoader.Parse(Read("ai-profiles"));
            EncounterDef encounter = EncounterLoader.Parse(Read(encounterResource), terrain);
            return EncounterLoader.CreateBattle(encounter, units, profiles);
        }

        private static string Read(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>("Data/" + name);
            Assert.IsNotNull(asset, "Missing Assets/_Project/Resources/Data/" + name + ".txt");
            return asset.text;
        }
    }
}
