using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Sim;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The 4x2 squad enumeration and the roster swap it runs on.
    ///
    /// The swap is the risky part: it rewrites encounter text, and a silent
    /// mistake there does not crash — it produces a matrix cell that ran the
    /// wrong squad and reads as a surprising result. Every failure mode below is
    /// therefore an exception rather than a best guess.
    /// </summary>
    public class SquadMatrixTests
    {
        [Test]
        public void SixteenSquads_CoverEveryCombinationExactlyOnce()
        {
            List<SquadMatrix.Squad> squads = SquadMatrix.All();
            Assert.AreEqual(16, squads.Count, "Four characters, two picks each.");

            HashSet<string> labels = new HashSet<string>();
            HashSet<string> ids = new HashSet<string>();
            foreach (SquadMatrix.Squad s in squads)
            {
                Assert.IsTrue(labels.Add(s.Label), "Duplicate combination: " + s.Label);
                Assert.IsTrue(ids.Add(s.Id), "Duplicate id: " + s.Id);
                Assert.AreEqual(4, s.UnitIds.Length);
            }

            Assert.AreEqual("T01", squads[0].Id);
            Assert.AreEqual("AAAA", squads[0].Label);
            Assert.AreEqual("T16", squads[15].Id);
            Assert.AreEqual("BBBB", squads[15].Label);
        }

        [Test]
        public void T13_IsTheAllVerbSquad()
        {
            // The bit order exists to make this true, and the Unity key bindings
            // and both playable crucibles depend on it. If somebody reorders the
            // bits for tidiness, this is what should stop them.
            SquadMatrix.Squad t13 = SquadMatrix.All()[12];

            Assert.AreEqual("T13", t13.Id);
            CollectionAssert.AreEqual(
                new[] { "Momotaro_B", "Genjin_B", "Kagemaru_A", "Masamori_A" }, t13.UnitIds);

            SquadMatrix.Squad derived = SquadMatrix.AllVerbs();
            Assert.AreEqual("T13", derived.Id, "AllVerbs() must agree with the index.");

            foreach (string verb in new[] { "push", "break", "slow", "taunt" })
                StringAssert.Contains(verb, t13.Verbs);
        }

        [Test]
        public void RosterSwap_ReplacesCombatSlotsAndLeavesEverythingElseAlone()
        {
            const string encounter =
                "encounter id=x name=X\n" +
                "objective type=defend turns=6\n" +
                "map\n#####\n#...#\n#####\nendmap\n" +
                "spawn faction=player unit=sc_shrine   x=1 y=1 protect=true\n" +
                "spawn faction=player unit=Momotaro_B x=2 y=1\n" +
                "spawn faction=player unit=Genjin_B   x=3 y=1\n" +
                "spawn faction=player unit=Kagemaru_A x=1 y=1\n" +
                "spawn faction=player unit=Masamori_A x=2 y=1\n" +
                "spawn faction=enemy  unit=sc_bruiser x=3 y=1 ai=rusher turn=2\n";

            SquadMatrix.Squad t01 = SquadMatrix.All()[0];    // AAAA
            string swapped = SquadMatrix.WithSquad(encounter, t01);

            StringAssert.Contains("unit=Momotaro_A", swapped);
            StringAssert.Contains("unit=Masamori_A", swapped);
            Assert.IsFalse(swapped.Contains("Momotaro_B"), "Slot 0 must have been rewritten.");
            Assert.IsFalse(swapped.Contains("Genjin_B"), "Slot 1 must have been rewritten.");

            StringAssert.Contains("unit=sc_shrine   x=1 y=1 protect=true", swapped,
                "A protected spawn is scenery the objective points at, not a squad slot.");
            StringAssert.Contains("unit=sc_bruiser", swapped, "Enemies are untouched.");
            StringAssert.Contains("objective type=defend turns=6", swapped);
            StringAssert.Contains("x=2 y=1", swapped, "Positions must survive the rewrite.");
        }

        [Test]
        public void RosterSwap_RefusesAnEncounterWithTheWrongNumberOfSlots()
        {
            // A cell that quietly ran three units would look like a bad squad
            // rather than a bad fixture, which is the expensive kind of wrong.
            const string threeSlots =
                "encounter id=x name=X\n" +
                "map\n#####\n#...#\n#####\nendmap\n" +
                "spawn faction=player unit=Momotaro_B x=1 y=1\n" +
                "spawn faction=player unit=Genjin_B   x=2 y=1\n" +
                "spawn faction=player unit=Kagemaru_A x=3 y=1\n";

            Assert.Throws<System.InvalidOperationException>(
                () => SquadMatrix.WithSquad(threeSlots, SquadMatrix.All()[0]));
        }

        [Test]
        public void EveryCrucibleMap_AcceptsEverySquadAndStillLoads()
        {
            // The real check: 64 cells, all of them parsed by the actual loader.
            // A swap that produced a valid-looking string the loader then rejected
            // would fail the whole batch several minutes in.
            string terrainText = Resources.LoadData("terrain");
            string unitsText = Resources.LoadData("units");
            TerrainCatalog terrain = TerrainLoader.Parse(terrainText);
            UnitCatalog units = UnitLoader.Parse(unitsText);

            string[] maps =
            {
                "gym-crucible-chasm.encounter", "gym-crucible-armor.encounter",
                "gym-crucible-delay.encounter", "gym-crucible-defend.encounter"
            };

            foreach (string map in maps)
            {
                string text = Resources.LoadData(map);
                Assert.IsNotNull(text, map + " is missing.");

                foreach (SquadMatrix.Squad squad in SquadMatrix.All())
                {
                    string swapped = SquadMatrix.WithSquad(text, squad);
                    EncounterDef def = EncounterLoader.Parse(swapped, terrain);

                    foreach (SpawnDef spawn in def.Spawns)
                        if (spawn.Faction == Faction.Player && !spawn.Protect)
                            Assert.IsTrue(System.Array.IndexOf(squad.UnitIds, spawn.UnitId) >= 0,
                                map + " / " + squad.Id + " kept a unit outside the squad: " + spawn.UnitId);

                    Assert.DoesNotThrow(
                        () => EncounterLoader.CreateBattle(def, units,
                                  Ediki.Core.Data.AiProfileLoader.Parse(Resources.LoadData("ai-profiles"))),
                        map + " / " + squad.Id + " failed to build.");
                }
            }
        }

        /// <summary>Reads the shipped data files the same way DataTests does.</summary>
        private static class Resources
        {
            public static string LoadData(string name)
            {
                UnityEngine.TextAsset asset =
                    UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/" + name);
                return asset == null ? null : asset.text;
            }
        }
    }
}
