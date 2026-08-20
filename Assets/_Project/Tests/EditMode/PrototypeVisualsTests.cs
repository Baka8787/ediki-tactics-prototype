using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Unity;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The visual grammar the editor's map view and the playtest board now share.
    ///
    /// The bug that made this worth testing: the game had its own terrain colour
    /// table listing five names, so Mire and Chasm fell through to a grey
    /// fallback and the one terrain that kills instantly was indistinguishable
    /// from open ground. A second table is a table that goes stale — these tests
    /// assert the single one covers everything terrain.txt actually contains.
    /// </summary>
    public class PrototypeVisualsTests
    {
        private static TerrainCatalog Terrain() => TerrainLoader.Parse(Read("terrain"));
        private static UnitCatalog Units() => UnitLoader.Parse(Read("units"));

        private static string Read(string name)
        {
            UnityEngine.TextAsset asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/" + name);
            Assert.IsNotNull(asset, "Missing Data/" + name + ".txt");
            return asset.text;
        }

        private static List<string> UnitIds()
        {
            List<string> ids = new List<string>();
            foreach (DataLine line in DataLine.ParseAll(Read("units")))
                if (line.Keyword == "unit") ids.Add(line.GetString("id"));
            return ids;
        }

        // ------------------------------------------------------------- terrain

        [Test]
        public void EveryShippedTerrainGetsItsOwnColour()
        {
            TerrainCatalog terrain = Terrain();
            Dictionary<string, string> byColour = new Dictionary<string, string>();

            for (int i = 0; i < terrain.Count; i++)
            {
                TerrainDef def = terrain[i];
                UnityEngine.Color c = PrototypeVisuals.TileColor(def);
                string key = c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000");

                string other;
                if (byColour.TryGetValue(key, out other))
                    Assert.Fail(def.Name + " and " + other + " are drawn in exactly the same colour.");

                byColour.Add(key, def.Name);
            }
        }

        /// <summary>
        /// Height is the channel that says what a tile DOES. A chasm has to sit
        /// below the ground you walk on or it is just a dark square, and an
        /// obstacle has to sit above it or it is just a differently coloured floor.
        /// </summary>
        [Test]
        public void TileHeightsOrderTerrainByWhatItDoes()
        {
            float chasm = PrototypeVisuals.TileTopHeight(TileStyle.Chasm);
            float swamp = PrototypeVisuals.TileTopHeight(TileStyle.Swamp);
            float normal = PrototypeVisuals.TileTopHeight(TileStyle.Normal);
            float rough = PrototypeVisuals.TileTopHeight(TileStyle.Rough);
            float obstacle = PrototypeVisuals.TileTopHeight(TileStyle.Obstacle);

            Assert.Less(chasm, 0f, "A chasm must be a hole, not a dark tile.");
            Assert.Less(chasm, swamp);
            Assert.Less(swamp, normal);
            Assert.Less(normal, rough);
            Assert.Less(rough, obstacle);
        }

        [Test]
        public void EveryShippedTerrainClassifiesByWhatItDoesNotByItsName()
        {
            TerrainCatalog terrain = Terrain();

            for (int i = 0; i < terrain.Count; i++)
            {
                TerrainDef def = terrain[i];
                TileStyle style = PrototypeVisuals.StyleOf(def);

                if (def.BlocksMovement)
                    Assert.AreEqual(TileStyle.Obstacle, style, def.Name + " blocks but is not drawn as one.");
                else if (def.IsLethal)
                    Assert.AreEqual(TileStyle.Chasm, style, def.Name + " kills but is not drawn as a pit.");
                else
                    Assert.AreNotEqual(TileStyle.Chasm, style,
                        def.Name + " is safe ground but is drawn as a lethal pit.");
            }
        }

        [Test]
        public void TheLethalTerrainIsTheOneDrawnAsAPit()
        {
            // The specific regression: Chasm used to reach the game's renderer as
            // an unrecognised name and come out grey.
            TerrainCatalog terrain = Terrain();

            TerrainDef chasm;
            Assert.IsTrue(terrain.TryGetByName("Chasm", out chasm));
            Assert.IsTrue(chasm.IsLethal, "terrain.txt no longer marks Chasm lethal.");

            Assert.AreEqual(TileStyle.Chasm, PrototypeVisuals.StyleOf(chasm));
            Assert.Less(PrototypeVisuals.TileTopHeight(PrototypeVisuals.StyleOf(chasm)),
                        PrototypeVisuals.TileTopHeight(TileStyle.Normal),
                        "The chasm does not sit below the ground around it.");
        }

        // --------------------------------------------------------------- units

        [Test]
        public void ColourCarriesTheSideAndNothingElse()
        {
            // The change this locks in: two DIFFERENT units on the same side are
            // drawn the same colour on purpose. Which one is which is the
            // nameplate's job — colour that means both side and identity stops
            // scaling the moment the roster grows.
            UnityEngine.Color a = PrototypeVisuals.BodyColor(Faction.Player, false);
            UnityEngine.Color b = PrototypeVisuals.BodyColor(Faction.Player, false);
            Assert.AreEqual(a.r, b.r);
            Assert.AreEqual(a.g, b.g);
            Assert.AreEqual(a.b, b.b);

            UnityEngine.Color enemy = PrototypeVisuals.BodyColor(Faction.Enemy, false);
            Assert.AreNotEqual(a.r, enemy.r, "The two sides are drawn the same colour.");

            UnityEngine.Color objective = PrototypeVisuals.BodyColor(Faction.Player, true);
            Assert.AreNotEqual(a.g, objective.g, "The objective is not distinguished from an ordinary unit.");
        }

        [Test]
        public void EveryShippedUnitGetsAShapeFromItsOwnNumbers()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
            {
                UnitDef def = units.Get(id);
                UnitArchetype archetype = PrototypeVisuals.ArchetypeOf(def);

                if (def.Move <= 0)
                    Assert.AreEqual(UnitArchetype.Prop, archetype, id + " cannot move but is not drawn as a prop.");
                if (archetype == UnitArchetype.Ranged)
                    Assert.GreaterOrEqual(def.AttackRange, 2, id + " is drawn as ranged with melee reach.");
                if (archetype == UnitArchetype.Mobile)
                    Assert.GreaterOrEqual(def.Move, 5, id + " is drawn as mobile without the move to match.");

                Assert.IsNotEmpty(PrototypeVisuals.PlannerNameOf(archetype));
            }
        }

        [Test]
        public void TheRosterUsesMoreThanOneShape()
        {
            // A shape channel that collapses to one silhouette is not a channel.
            UnitCatalog units = Units();
            HashSet<UnitArchetype> seen = new HashSet<UnitArchetype>();

            foreach (string id in UnitIds()) seen.Add(PrototypeVisuals.ArchetypeOf(units.Get(id)));

            Assert.GreaterOrEqual(seen.Count, 4,
                "The shipped roster only produces " + seen.Count + " silhouettes.");
        }
    }
}
