using System.Collections.Generic;
using Ediki.Core;
using Ediki.Unity;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using Ediki.Editor.Prototype;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The Prototype Editor's data layer.
    ///
    /// The load-bearing test here is the round trip over EVERY shipped
    /// encounter: the editor is only safe to hand a planner if opening a file
    /// and saving it back cannot change what the battle is. Everything else in
    /// this class is a specific way that could go wrong.
    ///
    /// Nothing here touches the editor UI — the window, the renderer and the
    /// camera are UnityEditor types and are verified by compiling, not by NUnit.
    /// </summary>
    public class PrototypeEditorTests
    {
        private static TerrainCatalog Terrain() => TerrainLoader.Parse(Read("terrain"));
        private static UnitCatalog Units() => UnitLoader.Parse(Read("units"));
        private static AiProfileCatalog Profiles() => AiProfileLoader.Parse(Read("ai-profiles"));

        private static string Read(string name)
        {
            UnityEngine.TextAsset asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/" + name);
            Assert.IsNotNull(asset, "Missing Assets/_Project/Resources/Data/" + name + ".txt");
            return asset.text;
        }

        // ------------------------------------------------------------ round trip

        [Test]
        public void EveryShippedEncounter_SurvivesOpenAndSaveUnchanged()
        {
            TerrainCatalog terrain = Terrain();
            UnityEngine.TextAsset[] assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>("Data");

            int checkedCount = 0;
            foreach (UnityEngine.TextAsset asset in assets)
            {
                if (!asset.name.EndsWith(".encounter")) continue;
                checkedCount++;

                EncounterDef before = EncounterLoader.Parse(asset.text, terrain);

                List<string> warnings = new List<string>();
                EncounterDocument doc = EncounterDocumentIO.FromText(asset.text, terrain, warnings);
                Assert.AreEqual(0, warnings.Count,
                    asset.name + " produced warnings when opened: " + string.Join(" / ", warnings.ToArray()));

                string rewritten = EncounterDocumentIO.ToText(doc, terrain);
                EncounterDef after = EncounterLoader.Parse(rewritten, terrain);

                AssertSameEncounter(asset.name, before, after);
            }

            Assert.Greater(checkedCount, 0, "No .encounter assets found — the round trip proved nothing.");
        }

        private static void AssertSameEncounter(string name, EncounterDef a, EncounterDef b)
        {
            Assert.AreEqual(a.Id, b.Id, name + ": id changed.");
            Assert.AreEqual(a.DisplayName, b.DisplayName, name + ": display name changed.");
            Assert.AreEqual(a.Map.Width, b.Map.Width, name + ": map width changed.");
            Assert.AreEqual(a.Map.Height, b.Map.Height, name + ": map height changed.");

            for (int y = 0; y < a.Map.Height; y++)
                for (int x = 0; x < a.Map.Width; x++)
                {
                    Coord c = new Coord(x, y);
                    Assert.AreEqual(a.Map.TerrainAt(c).Index, b.Map.TerrainAt(c).Index,
                        name + ": terrain at " + c + " changed.");
                }

            Assert.AreEqual(a.Objective.Kind, b.Objective.Kind, name + ": objective kind changed.");
            Assert.AreEqual(a.Objective.Target, b.Objective.Target, name + ": objective target changed.");
            Assert.AreEqual(a.Objective.TurnLimit, b.Objective.TurnLimit, name + ": turn limit changed.");
            Assert.AreEqual(a.Rules.Damage, b.Rules.Damage, name + ": damage model changed.");

            Assert.AreEqual(a.Spawns.Count, b.Spawns.Count, name + ": spawn count changed.");
            for (int i = 0; i < a.Spawns.Count; i++)
            {
                SpawnDef x = a.Spawns[i];
                SpawnDef y = b.Spawns[i];
                string where = name + " spawn " + i + " (" + x.UnitId + "): ";

                // Spawn ORDER is load-bearing — CreateBattle assigns runtime unit
                // ids by it, and the state hash covers those ids.
                Assert.AreEqual(x.UnitId, y.UnitId, where + "unit changed.");
                Assert.AreEqual(x.Faction, y.Faction, where + "faction changed.");
                Assert.AreEqual(x.Position, y.Position, where + "position changed.");
                Assert.AreEqual(x.AiProfileId, y.AiProfileId, where + "ai profile changed.");
                Assert.AreEqual(x.Group, y.Group, where + "group changed.");
                Assert.AreEqual(x.Protect, y.Protect, where + "protect changed.");
                Assert.AreEqual(x.IsObjectiveTarget, y.IsObjectiveTarget, where + "kill target changed.");
                Assert.AreEqual(x.ArrivesOnTurn, y.ArrivesOnTurn, where + "arrival turn changed.");
            }
        }

        /// <summary>
        /// The gym maps document why they are shaped the way they are, and some of
        /// that documentation is load-bearing — gym-crucible-chasm's spawn block
        /// says outright that SquadMatrix rewrites those four lines in place. An
        /// editor that dropped comments on save would delete that with no warning.
        /// </summary>
        [Test]
        public void EveryShippedEncounter_KeepsItsCommentsThroughOpenAndSave()
        {
            TerrainCatalog terrain = Terrain();
            UnityEngine.TextAsset[] assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>("Data");

            foreach (UnityEngine.TextAsset asset in assets)
            {
                if (!asset.name.EndsWith(".encounter")) continue;

                List<string> warnings = new List<string>();
                EncounterDocument doc = EncounterDocumentIO.FromText(asset.text, terrain, warnings);
                string rewritten = EncounterDocumentIO.ToText(doc, terrain);

                List<string> before = CommentBodies(asset.text);
                List<string> after = CommentBodies(rewritten);

                CollectionAssert.AreEqual(before, after,
                    asset.name + ": comment lines were lost or reordered by a save.");
            }
        }

        /// <summary>Comment text in file order, stripped of the marker and any padding.</summary>
        private static List<string> CommentBodies(string text)
        {
            List<string> bodies = new List<string>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed[0] != '#') continue;

                string body = trimmed.Substring(1).Trim();
                if (body.Length == 0) continue;   // spacing, not content
                bodies.Add(body);
            }
            return bodies;
        }

        [Test]
        public void EveryShippedEncounter_StillPassesTheGateAfterOpening()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            AiProfileCatalog profiles = Profiles();

            UnityEngine.TextAsset[] assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>("Data");
            foreach (UnityEngine.TextAsset asset in assets)
            {
                if (!asset.name.EndsWith(".encounter")) continue;

                List<string> warnings = new List<string>();
                EncounterDocument doc = EncounterDocumentIO.FromText(asset.text, terrain, warnings);

                EncounterValidation.GateResult gate =
                    EncounterValidation.Gate(doc, terrain, units, profiles);

                Assert.IsTrue(gate.Ok, asset.name + " failed the editor gate: "
                    + (gate.LoaderError ?? Join(gate.Issues)));
            }
        }

        private static string Join(List<EncounterIssue> issues)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < issues.Count; i++) parts.Add(issues[i].ToString());
            return string.Join(" / ", parts.ToArray());
        }

        // -------------------------------------------------------- unit stat block

        [Test]
        public void EveryShippedUnit_SurvivesTheStatBlockRoundTrip()
        {
            UnitCatalog units = Units();
            List<string> ids = UnitIds();

            foreach (string id in ids)
            {
                UnitDef original = units.Get(id);
                UnitStatBlock block = UnitStatBlock.From(original);

                Assert.IsTrue(block.MatchesStatsOf(original), id + ": MatchesStatsOf rejected its own source.");

                UnitCatalog reparsed = UnitLoader.Parse(block.ToDataLine(id));
                UnitDef after = reparsed.Get(id);

                Assert.IsTrue(UnitStatBlock.From(after).MatchesStatsOf(original),
                    id + ": a stat block written to units.txt format did not read back the same.\n"
                    + block.ToDataLine(id));
            }
        }

        [Test]
        public void StatBlock_OmitsApRegenWhenItEqualsTheCap()
        {
            // UnitDef resolves a missing apRegen to MaxAp, so writing it back out
            // explicitly would be noise that reads like a deliberate difference.
            UnitStatBlock b = UnitStatBlock.From(Units().Get("shrine"));
            Assert.IsFalse(b.ToDataLine("x").Contains("apRegen="),
                "apRegen was written even though it equals the cap.");
        }

        private static List<string> UnitIds()
        {
            List<string> ids = new List<string>();
            foreach (DataLine line in DataLine.ParseAll(Read("units")))
                if (line.Keyword == "unit") ids.Add(line.GetString("id"));
            return ids;
        }

        // ------------------------------------------------------------- document

        [Test]
        public void Resize_KeepsTheOverlapAndFillsTheRest()
        {
            TerrainCatalog terrain = Terrain();
            int floor = EncounterDocumentIO.DefaultTerrainIndex(terrain);
            int wall = EncounterDocumentIO.WallTerrainIndex(terrain);

            EncounterDocument doc = new EncounterDocument(4, 4, floor);
            doc.SetTerrain(1, 1, wall);
            doc.SetTerrain(3, 3, wall);

            doc.Resize(6, 6, floor);

            Assert.AreEqual(6, doc.Width);
            Assert.AreEqual(6, doc.Height);
            Assert.AreEqual(wall, doc.TerrainAt(1, 1), "The overlap was not preserved.");
            Assert.AreEqual(wall, doc.TerrainAt(3, 3), "The overlap was not preserved.");
            Assert.AreEqual(floor, doc.TerrainAt(5, 5), "New cells were not filled.");

            doc.Resize(2, 2, floor);
            Assert.AreEqual(wall, doc.TerrainAt(1, 1), "Cropping lost a cell that was still inside.");
        }

        [Test]
        public void TagOf_NumbersEachFactionInSpawnOrder()
        {
            EncounterDocument doc = SmallDoc(Terrain());
            Assert.AreEqual("P1", doc.TagOf(doc.Spawns[0]));
            Assert.AreEqual("E1", doc.TagOf(doc.Spawns[1]));
            Assert.AreEqual("E2", doc.TagOf(doc.Spawns[2]));
        }

        // ------------------------------------------------------------ validation

        [Test]
        public void Validation_ReportsASpawnOutsideTheMap()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns[1].Position = new Coord(99, 99);

            AssertHasError(doc, terrain, "地圖外面");
        }

        [Test]
        public void Validation_ReportsTwoUnitsOnTheSameCell()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns[2].Position = doc.Spawns[1].Position;

            AssertHasError(doc, terrain, "重疊");
        }

        [Test]
        public void Validation_ReportsAUnitThatIsNotInTheRoster()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns[1].UnitId = "definitely_not_a_unit";

            AssertHasError(doc, terrain, "找不到");
        }

        [Test]
        public void Validation_ReportsASpawnOnBlockingTerrain()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.SetTerrain(doc.Spawns[1].Position.X, doc.Spawns[1].Position.Y,
                           EncounterDocumentIO.WallTerrainIndex(terrain));

            AssertHasError(doc, terrain, "障礙物");
        }

        [Test]
        public void Validation_ReportsAnEnemyWalledOffFromThePlayer()
        {
            TerrainCatalog terrain = Terrain();
            int wall = EncounterDocumentIO.WallTerrainIndex(terrain);
            EncounterDocument doc = SmallDoc(terrain);

            // Box E1 in completely.
            Coord p = doc.Spawns[1].Position;
            doc.SetTerrain(p.X + 1, p.Y, wall);
            doc.SetTerrain(p.X - 1, p.Y, wall);
            doc.SetTerrain(p.X, p.Y + 1, wall);
            doc.SetTerrain(p.X, p.Y - 1, wall);

            AssertHasError(doc, terrain, "走不到");
        }

        [Test]
        public void Validation_ReportsMoreThanFourPlayerUnits()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);

            string[] extras = { "zhengshou", "genjin", "kagemaru", "kohaku_tank" };
            for (int i = 0; i < extras.Length; i++)
                doc.Spawns.Add(new SpawnEntry
                {
                    Faction = Faction.Player,
                    UnitId = extras[i],
                    Position = new Coord(1 + i, 3)
                });

            AssertHasError(doc, terrain, "我方最多 4 個出戰單位");
        }

        [Test]
        public void Validation_ReportsTheSameCharacterBroughtTwice()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Player,
                UnitId = doc.Spawns[0].UnitId,
                Position = new Coord(3, 4)
            });

            AssertHasError(doc, terrain, "不能重複帶同一人");
        }

        [Test]
        public void Validation_ProtectedPropsDoNotCountAgainstThePartyLimit()
        {
            // SquadMatrix skips protected spawns when it swaps a roster, for the
            // same reason: a shrine is scenery, not a squad slot.
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);

            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Player, UnitId = "sc_shrine",
                Position = new Coord(4, 4), Protect = true
            });
            string[] combat = { "zhengshou", "genjin", "kagemaru" };
            for (int i = 0; i < combat.Length; i++)
                doc.Spawns.Add(new SpawnEntry
                {
                    Faction = Faction.Player, UnitId = combat[i], Position = new Coord(1 + i, 3)
                });

            doc.ObjectiveKind = ObjectiveKind.Defend;
            doc.TurnLimit = 6;

            List<EncounterIssue> issues = EncounterValidation.Check(doc, terrain, Units(), Profiles());
            for (int i = 0; i < issues.Count; i++)
                Assert.IsFalse(issues[i].Message.Contains("我方最多"),
                    "Four combatants plus a shrine was reported as five: " + issues[i].Message);
        }

        [Test]
        public void Validation_ReportsDefendWithNothingToDefend()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.ObjectiveKind = ObjectiveKind.Defend;
            doc.TurnLimit = 6;

            AssertHasError(doc, terrain, "沒有任何單位被設成要保護");
        }

        [Test]
        public void Validation_ReportsSurviveWithNoClock()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.ObjectiveKind = ObjectiveKind.Survive;
            doc.TurnLimit = 0;

            AssertHasError(doc, terrain, "回合上限");
        }

        [Test]
        public void Validation_PassesACleanDocument()
        {
            TerrainCatalog terrain = Terrain();
            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(SmallDoc(terrain), terrain, Units(), Profiles());

            Assert.IsTrue(gate.Ok, "A clean document was rejected: "
                + (gate.LoaderError ?? Join(gate.Issues)));
            Assert.IsNotNull(gate.Text);
        }

        /// <summary>
        /// The gate's whole purpose: whatever it approves must be something the
        /// shipped loader and the shipped battle builder both accept.
        /// </summary>
        [Test]
        public void Gate_OutputIsAlwaysLoadableByTheRealRuntime()
        {
            TerrainCatalog terrain = Terrain();
            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(SmallDoc(terrain), terrain, Units(), Profiles());

            Assert.IsTrue(gate.Ok);
            EncounterDef def = EncounterLoader.Parse(gate.Text, terrain);
            BattleSetup setup = EncounterLoader.CreateBattle(def, Units(), Profiles());
            Assert.IsNotNull(setup.State);
            Assert.AreEqual(3, setup.State.Units.Count);
        }

        private static void AssertHasError(EncounterDocument doc, TerrainCatalog terrain, string fragment)
        {
            List<EncounterIssue> issues = EncounterValidation.Check(doc, terrain, Units(), Profiles());

            bool found = false;
            for (int i = 0; i < issues.Count && !found; i++)
                found = issues[i].Level == IssueLevel.Error && issues[i].Message.Contains(fragment);

            Assert.IsTrue(found, "Expected an error containing \"" + fragment + "\". Got: " + Join(issues));
        }

        // --------------------------------------------------------------- history

        [Test]
        public void History_UndoAndRedoRestoreTerrainAndSpawns()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            DocumentHistory history = new DocumentHistory();

            int before = doc.TerrainAt(2, 2);
            int wall = EncounterDocumentIO.WallTerrainIndex(terrain);

            history.Push(doc, "塗地形");
            doc.SetTerrain(2, 2, wall);
            doc.Spawns.RemoveAt(2);

            Assert.IsTrue(history.CanUndo);
            EncounterDocument undone = history.Undo(doc);
            Assert.AreEqual(before, undone.TerrainAt(2, 2), "Undo did not restore the tile.");
            Assert.AreEqual(3, undone.Spawns.Count, "Undo did not restore the deleted unit.");

            Assert.IsTrue(history.CanRedo);
            EncounterDocument redone = history.Redo(undone);
            Assert.AreEqual(wall, redone.TerrainAt(2, 2), "Redo did not re-apply the tile.");
            Assert.AreEqual(2, redone.Spawns.Count, "Redo did not re-apply the deletion.");
        }

        [Test]
        public void History_NewEditAfterUndoDropsTheRedoBranch()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            DocumentHistory history = new DocumentHistory();

            history.Push(doc, "a");
            doc.SetTerrain(2, 2, EncounterDocumentIO.WallTerrainIndex(terrain));
            doc = history.Undo(doc);

            Assert.IsTrue(history.CanRedo);
            history.Push(doc, "b");
            Assert.IsFalse(history.CanRedo, "A new edit must fork history, not keep the abandoned branch.");
        }

        [Test]
        public void History_StopsGrowingAtCapacity()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            DocumentHistory history = new DocumentHistory();

            for (int i = 0; i < DocumentHistory.Capacity + 20; i++) history.Push(doc, "step");

            int steps = 0;
            while (history.CanUndo) { doc = history.Undo(doc); steps++; }
            Assert.AreEqual(DocumentHistory.Capacity, steps);
        }

        // -------------------------------------------------------------- variants

        [Test]
        public void Variant_UnchangedStatsCreateNothing()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            EncounterDocument doc = SmallDoc(terrain);

            doc.Spawns[0].PendingStats = UnitStatBlock.From(units.Get(doc.Spawns[0].UnitId));

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());
            Assert.IsFalse(plan.ChangesAnything, "Retyping the same numbers created a variant.");
        }

        [Test]
        public void Variant_ChangedStatsAllocateANewIdAndLeaveTheOriginalAlone()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            EncounterDocument doc = SmallDoc(terrain);

            string baseId = doc.Spawns[0].UnitId;
            UnitStatBlock stats = UnitStatBlock.From(units.Get(baseId));
            stats.MaxHp += 55;
            doc.Spawns[0].PendingStats = stats;

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());

            Assert.AreEqual(1, plan.NewUnitLines.Count);
            Assert.AreEqual(baseId + "_v1", plan.SpawnIdRewrites[0]);
            Assert.IsTrue(plan.NewUnitLines[0].Contains("id=" + baseId + "_v1"));

            // The new row must parse, and the original must be untouched.
            UnitCatalog reparsed = UnitLoader.Parse(plan.NewUnitLines[0]);
            Assert.AreEqual(units.Get(baseId).MaxHp + 55, reparsed.Get(baseId + "_v1").MaxHp);
            Assert.AreEqual(units.Get(baseId).MaxHp, Units().Get(baseId).MaxHp,
                "The base unit's stats moved — variants must be additive.");
        }

        [Test]
        public void Variant_TwoSpawnsEditedIdenticallyShareOneId()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            EncounterDocument doc = SmallDoc(terrain);

            for (int i = 1; i <= 2; i++)
            {
                UnitStatBlock s = UnitStatBlock.From(units.Get(doc.Spawns[i].UnitId));
                s.Atk += 10;
                doc.Spawns[i].PendingStats = s;
            }

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());

            Assert.AreEqual(1, plan.NewUnitLines.Count, "Identical edits produced two rows.");
            Assert.AreEqual(plan.SpawnIdRewrites[1], plan.SpawnIdRewrites[2]);
        }

        [Test]
        public void Variant_ReusesAnExistingUnitWhoseNumbersAlreadyMatch()
        {
            // Momotaro_A and momotaro differ only by id and display name, so
            // editing one INTO the other must not mint a third row.
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            EncounterDocument doc = SmallDoc(terrain);

            UnitStatBlock target = UnitStatBlock.From(units.Get("Momotaro_A"));
            doc.Spawns[0].PendingStats = target;

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());

            Assert.AreEqual(0, plan.NewUnitLines.Count, "An identical existing unit was not reused.");
            Assert.AreEqual("Momotaro_A", plan.SpawnIdRewrites[0]);
        }

        [Test]
        public void Variant_EditingAVariantAgainDoesNotNestTheSuffix()
        {
            Assert.AreEqual("momotaro", UnitVariantWriter.RootIdOf("momotaro_v1"));
            Assert.AreEqual("momotaro", UnitVariantWriter.RootIdOf("momotaro_v27"));

            // Hand-authored rungs are NOT generated suffixes and must survive.
            Assert.AreEqual("Momotaro_A", UnitVariantWriter.RootIdOf("Momotaro_A"));
            Assert.AreEqual("kagemaru_c1", UnitVariantWriter.RootIdOf("kagemaru_c1"));
            Assert.AreEqual("momotaro_push_g5", UnitVariantWriter.RootIdOf("momotaro_push_g5"));
        }

        [Test]
        public void Variant_AppendBlockKeepsTheExistingFileIntact()
        {
            string existing = Read("units");
            List<string> lines = new List<string> { "unit id=zzz_test name=ZZZ hp=1 atk=1 def=1 move=1 ap=1 range=1 attackCost=1 guardCost=1" };

            string appended = UnitVariantWriter.AppendBlock(existing, lines);

            Assert.IsTrue(appended.StartsWith(existing.TrimEnd('\n')),
                "Appending rewrote something above the new block.");
            UnitCatalog reparsed = UnitLoader.Parse(appended);
            Assert.AreEqual(1, reparsed.Get("zzz_test").MaxHp);
            Assert.AreEqual(300, reparsed.Get("momotaro").MaxHp, "An existing unit changed.");
        }

        // ---------------------------------------------------------------- shapes

        [Test]
        public void Archetype_ReadsTheUnitDefRatherThanItsName()
        {
            UnitCatalog units = Units();
            Assert.AreEqual(UnitArchetype.Prop, PrototypeVisuals.ArchetypeOf(units.Get("shrine")));
            Assert.AreEqual(UnitArchetype.Support, PrototypeVisuals.ArchetypeOf(units.Get("momotaro_pure")));
            Assert.AreEqual(UnitArchetype.Ranged, PrototypeVisuals.ArchetypeOf(units.Get("Kagemaru_A")));
            Assert.AreEqual(UnitArchetype.Mobile, PrototypeVisuals.ArchetypeOf(units.Get("Kagemaru_B")));
            Assert.AreEqual(UnitArchetype.Support, PrototypeVisuals.ArchetypeOf(units.Get("momotaro")),
                "Momotaro's innate purification now intentionally takes visual priority over his HP-based heavy shape.");
            Assert.AreEqual(UnitArchetype.Melee, PrototypeVisuals.ArchetypeOf(units.Get("genjin")));
        }

        /// <summary>
        /// Edited stats must classify the same way the catalog unit would.
        ///
        /// There used to be TWO classifiers — one over UnitDef, one over the
        /// editable block — and this test existed to check they agreed. The
        /// second one is gone: the block converts to a UnitDef and the single
        /// classifier answers. What is left to prove is that the conversion does
        /// not change the answer.
        /// </summary>
        [Test]
        public void Archetype_SurvivesTheRoundTripThroughTheEditableBlock()
        {
            UnitCatalog units = Units();
            foreach (string id in UnitIds())
            {
                UnitDef def = units.Get(id);
                Assert.AreEqual(PrototypeVisuals.ArchetypeOf(def),
                                PrototypeVisuals.ArchetypeOf(UnitStatBlock.From(def).ToDef(id)),
                    id + ": the inspector would draw a different shape from the map view.");
            }
        }

        [Test]
        public void TileStyle_CoversEveryShippedTerrain()
        {
            TerrainCatalog terrain = Terrain();

            TerrainDef def;
            Assert.IsTrue(terrain.TryGetByName("Open", out def));
            Assert.AreEqual(TileStyle.Normal, PrototypeVisuals.StyleOf(def));

            Assert.IsTrue(terrain.TryGetByName("Forest", out def));
            Assert.AreEqual(TileStyle.Rough, PrototypeVisuals.StyleOf(def));

            Assert.IsTrue(terrain.TryGetByName("Mire", out def));
            Assert.AreEqual(TileStyle.Swamp, PrototypeVisuals.StyleOf(def));

            Assert.IsTrue(terrain.TryGetByName("Chasm", out def));
            Assert.AreEqual(TileStyle.Chasm, PrototypeVisuals.StyleOf(def));

            Assert.IsTrue(terrain.TryGetByName("Blocking", out def));
            Assert.AreEqual(TileStyle.Obstacle, PrototypeVisuals.StyleOf(def));
        }

        [Test]
        public void DefaultTerrain_IsPassableAndCheap()
        {
            TerrainCatalog terrain = Terrain();
            TerrainDef def = terrain[EncounterDocumentIO.DefaultTerrainIndex(terrain)];

            Assert.IsFalse(def.BlocksMovement, "A new map would be made of walls.");
            Assert.IsFalse(def.IsLethal, "A new map would kill everything standing on it.");
        }

        // ---------------------------------------------------------------- roster

        private static EditorRoster Roster() => EditorRoster.Parse(Read("editor-roster"), Units());

        [Test]
        public void Roster_LoadsCleanlyAndNamesOnlyUnitsThatExist()
        {
            EditorRoster roster = Roster();

            Assert.IsFalse(roster.IsEmpty, "editor-roster.txt produced no characters.");
            Assert.AreEqual(0, roster.Warnings.Count,
                "The roster names something that is not in units.txt: "
                + string.Join(" / ", roster.Warnings.ToArray()));
        }

        [Test]
        public void Roster_HasExactlyTheFourPartyCharacters()
        {
            // Four is the 討鬼團. The GDD character report names 桃太郎, 正守,
            // 玄真 and 影丸 and no fifth, and SquadMatrix.Characters is a
            // four-element array. If somebody adds a fifth to the roster without
            // that changing first, this is where it shows up.
            List<RosterCharacter> party = Roster().ForSide(RosterSide.Player);

            Assert.AreEqual(4, party.Count,
                "The party is not four characters: " + string.Join(" / ", Names(party)));

            foreach (RosterCharacter c in party)
                Assert.AreEqual(2, c.Variants.Count,
                    c.Name + " should have exactly the A/B pair, has " + c.Variants.Count + ".");
        }

        [Test]
        public void Roster_PartyMatchesTheCharactersSquadMatrixSwaps()
        {
            // SquadMatrix builds ids as Character + "_A" / "_B" and rewrites the
            // four player slots with them. If the roster offers a character the
            // matrix cannot build, the two disagree about who the party is.
            EditorRoster roster = Roster();
            UnitCatalog units = Units();

            foreach (string character in Ediki.Sim.SquadMatrix.Characters)
                foreach (string suffix in new[] { "_A", "_B" })
                {
                    string id = character + suffix;
                    UnitDef def;
                    Assert.IsTrue(units.TryGet(id, out def), id + " is missing from units.txt.");

                    RosterSide? side = roster.SideOf(id);
                    Assert.IsTrue(side.HasValue, id + " is not in the editor roster.");
                    Assert.AreEqual(RosterSide.Player, side.Value, id + " is not on the player side.");
                }
        }

        [Test]
        public void Roster_NeverPutsAUnitOnTwoSides()
        {
            EditorRoster roster = Roster();
            Dictionary<string, RosterSide> seen = new Dictionary<string, RosterSide>();

            foreach (RosterCharacter c in roster.Characters)
                foreach (RosterVariant v in c.Variants)
                {
                    Assert.IsFalse(seen.ContainsKey(v.UnitId), v.UnitId + " is listed twice.");
                    seen.Add(v.UnitId, c.Side);
                }
        }

        /// <summary>
        /// The roster adds validation rules, and a rule that condemns the shipped
        /// data is a wrong rule. gym-e4-protection spawns e4_backline on the
        /// PLAYER side — units.txt calls it "always the lowest-HP party member" —
        /// so the roster must either agree or stay silent about it.
        /// </summary>
        [Test]
        public void Roster_DoesNotInvalidateAnyShippedEncounter()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            AiProfileCatalog profiles = Profiles();
            EditorRoster roster = Roster();

            UnityEngine.TextAsset[] assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>("Data");
            foreach (UnityEngine.TextAsset asset in assets)
            {
                if (!asset.name.EndsWith(".encounter")) continue;

                List<string> warnings = new List<string>();
                EncounterDocument doc = EncounterDocumentIO.FromText(asset.text, terrain, warnings);

                EncounterValidation.GateResult gate =
                    EncounterValidation.Gate(doc, terrain, units, profiles, roster);

                Assert.IsTrue(gate.Ok, asset.name + " was rejected once the roster rules applied: "
                    + (gate.LoaderError ?? Join(gate.Issues)));
            }
        }

        [Test]
        public void Roster_RejectsAPartyMemberPlacedOnTheEnemySide()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns[1].UnitId = "Momotaro_A";   // spawn 1 is an enemy

            List<EncounterIssue> issues =
                EncounterValidation.Check(doc, terrain, Units(), Profiles(), Roster());

            bool found = false;
            for (int i = 0; i < issues.Count && !found; i++)
                found = issues[i].Level == IssueLevel.Error && issues[i].Message.Contains("不能放在敵方");

            Assert.IsTrue(found, "A party member on the enemy side was allowed: " + Join(issues));
        }

        [Test]
        public void Roster_RejectsTwoBuildsOfTheSameCharacter()
        {
            // Momotaro_A and Momotaro_B are two tactical roles of 桃太郎, which
            // is why this has to be caught by CHARACTER and not by unit id.
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);

            doc.Spawns[0].UnitId = "Momotaro_A";
            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Player, UnitId = "Momotaro_B", Position = new Coord(3, 4)
            });

            List<EncounterIssue> issues =
                EncounterValidation.Check(doc, terrain, Units(), Profiles(), Roster());

            bool found = false;
            for (int i = 0; i < issues.Count && !found; i++)
                found = issues[i].Level == IssueLevel.Error && issues[i].Message.Contains("同一個角色只能帶一個");

            Assert.IsTrue(found, "Two builds of 桃太郎 were allowed: " + Join(issues));
        }

        [Test]
        public void Roster_StaysSilentAboutUnitsItDoesNotList()
        {
            // Unlisted units are measurement fixtures. The editor must not have an
            // opinion about which side they belong on.
            EditorRoster roster = Roster();
            Assert.IsFalse(roster.SideOf("e4_backline").HasValue,
                "e4_backline is listed, but gym-e4-protection spawns it as a player unit.");
            Assert.IsFalse(roster.SideOf("zhengshou").HasValue,
                "zhengshou should be absent from the palette (專案負責人 2026-08-18) but still usable.");
        }

        [Test]
        public void Roster_MissingFileDegradesToOfferingEverything()
        {
            EditorRoster empty = EditorRoster.Parse(null, Units());
            Assert.IsTrue(empty.IsEmpty);
            Assert.AreEqual(0, empty.Palette(Faction.Player).Count);

            // And validation must then add no rules of its own.
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);
            doc.Spawns[1].UnitId = "Momotaro_A";

            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(doc, terrain, Units(), Profiles(), empty);
            Assert.IsTrue(gate.Ok, "Without a roster the editor invented a rule: "
                + (gate.LoaderError ?? Join(gate.Issues)));
        }

        [Test]
        public void Roster_ReportsAnIdThatNoLongerExists()
        {
            EditorRoster roster = EditorRoster.Parse(
                "character id=ghost name=Ghost faction=player\n" +
                "variant char=ghost unit=definitely_not_a_unit label=A\n", Units());

            Assert.IsTrue(roster.IsEmpty, "A character whose only unit is gone should be dropped.");

            // Two reports, and both earn their place: which id vanished, and that
            // the character was dropped as a result. Only the first names the id
            // somebody has to go and fix.
            string joined = string.Join(" / ", roster.Warnings.ToArray());
            StringAssert.Contains("definitely_not_a_unit", joined);
            StringAssert.Contains("找不到", joined);
            StringAssert.Contains("沒有任何可用的單位", joined);
        }

        [Test]
        public void Roster_ObjectivePropsAreOfferedOnThePlayerSide()
        {
            // A defended shrine is placed as a player spawn with protect=true,
            // so it has to appear in the player palette without being a party member.
            EditorRoster roster = Roster();

            List<RosterCharacter> playerPalette = roster.Palette(Faction.Player);
            bool hasProp = false;
            for (int i = 0; i < playerPalette.Count && !hasProp; i++)
                hasProp = playerPalette[i].Side == RosterSide.Objective;

            Assert.IsTrue(hasProp, "No objective prop is offered when placing player units.");
            Assert.AreEqual(RosterSide.Objective, roster.SideOf("sc_shrine"));

            List<RosterCharacter> enemyPalette = roster.Palette(Faction.Enemy);
            for (int i = 0; i < enemyPalette.Count; i++)
                Assert.AreEqual(RosterSide.Enemy, enemyPalette[i].Side,
                    "The enemy palette offers " + enemyPalette[i].Name + ", which is not an enemy.");
        }

        private static List<string> Names(List<RosterCharacter> characters)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < characters.Count; i++) names.Add(characters[i].Name);
            return names;
        }

        // ------------------------------------------- validate before writing

        /// <summary>
        /// The ordering the editor's Save and Play both depend on: a stat edit
        /// can be resolved to a variant id and validated ENTIRELY in memory.
        ///
        /// It used to append the row to units.txt first and validate afterwards,
        /// so a refused playtest still left a permanent row behind on the file
        /// every encounter in the project reads. The test that catches a
        /// regression is this one: the plan and the provisional catalog must be
        /// enough to reach a verdict, with no file ever opened for writing.
        /// </summary>
        [Test]
        public void AVariantCanBeValidatedWithoutWritingUnitsFile()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            string unitsBefore = Read("units");

            EncounterDocument doc = SmallDoc(terrain);
            UnitStatBlock tuned = UnitStatBlock.From(units.Get(doc.Spawns[0].UnitId));
            tuned.MaxHp += 40;
            doc.Spawns[0].PendingStats = tuned;

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());
            Assert.AreEqual(1, plan.NewUnitLines.Count);

            // The provisional catalog: units.txt as it WOULD be, parsed in memory.
            UnitCatalog provisional = UnitLoader.Parse(
                UnitVariantWriter.AppendBlock(unitsBefore, plan.NewUnitLines));

            EncounterDocument candidate = doc.Clone();
            UnitVariantWriter.ApplyToDocument(candidate, plan);

            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(candidate, terrain, provisional, Profiles(), Roster());

            Assert.IsTrue(gate.Ok, "A valid encounter could not be checked in memory: "
                + (gate.LoaderError ?? Join(gate.Issues)));

            // The real catalog must still know nothing about the variant, and the
            // real document must still hold its pending edit.
            UnitDef leaked;
            Assert.IsFalse(Units().TryGet(plan.SpawnIdRewrites[0], out leaked),
                "The variant reached units.txt during validation.");
            Assert.AreEqual(unitsBefore, Read("units"), "units.txt changed during validation.");
            Assert.IsTrue(doc.Spawns[0].HasPendingStats,
                "Validating consumed the pending edit — a refusal would lose the planner's typing.");
        }

        /// <summary>
        /// The failing half of the same contract: an encounter that will be
        /// rejected must be rejected BEFORE anything is written.
        /// </summary>
        [Test]
        public void AnInvalidEncounterIsRejectedWithTheVariantStillOnlyInMemory()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            string unitsBefore = Read("units");

            EncounterDocument doc = SmallDoc(terrain);

            UnitStatBlock tuned = UnitStatBlock.From(units.Get(doc.Spawns[0].UnitId));
            tuned.Atk += 15;
            doc.Spawns[0].PendingStats = tuned;

            // Break the encounter in a way only the gate can catch.
            doc.Spawns[1].Position = new Coord(99, 99);

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());
            UnitCatalog provisional = UnitLoader.Parse(
                UnitVariantWriter.AppendBlock(unitsBefore, plan.NewUnitLines));

            EncounterDocument candidate = doc.Clone();
            UnitVariantWriter.ApplyToDocument(candidate, plan);

            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(candidate, terrain, provisional, Profiles(), Roster());

            Assert.IsFalse(gate.Ok, "A spawn outside the map was accepted.");
            Assert.AreEqual(unitsBefore, Read("units"),
                "A REFUSED encounter still modified units.txt.");
            Assert.IsTrue(doc.Spawns[0].HasPendingStats,
                "A refused encounter consumed the pending stat edit.");
            Assert.AreEqual("momotaro", doc.Spawns[0].UnitId,
                "A refused encounter still rewrote the unit id in the live document.");
        }

        // ------------------------------------------------ surviving a reload

        /// <summary>
        /// Entering play mode reloads the domain and keeps only serialized
        /// fields, so the window has to be able to rebuild its document from a
        /// string. If this breaks, pressing 試玩 silently throws the planner's
        /// unsaved work away and hands back a blank map.
        /// </summary>
        [Test]
        public void ReloadRestore_RebuildsTheDocumentFromItsText()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument before = SmallDoc(terrain);
            before.Spawns[2].ArrivesOnTurn = 3;
            before.ObjectiveKind = ObjectiveKind.Reach;
            before.ObjectiveTarget = new Coord(4, 1);
            before.TurnLimit = 9;

            string stored = EncounterDocumentIO.ToText(before, terrain);

            List<string> warnings = new List<string>();
            EncounterDocument after = EncounterDocumentIO.FromText(stored, terrain, warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(before.Width, after.Width);
            Assert.AreEqual(before.Height, after.Height);
            Assert.AreEqual(before.Spawns.Count, after.Spawns.Count, "The units did not come back.");
            Assert.AreEqual(3, after.Spawns[2].ArrivesOnTurn);
            Assert.AreEqual(ObjectiveKind.Reach, after.ObjectiveKind);
            Assert.AreEqual(new Coord(4, 1), after.ObjectiveTarget);
            Assert.AreEqual(9, after.TurnLimit);
        }

        [Test]
        public void ReloadRestore_KeepsStatEditsThatWereNotSavedYet()
        {
            // These live only in memory — the encounter format has no per-spawn
            // stat fields — so they are the one part a reload could still lose.
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);

            UnitStatBlock edited = UnitStatBlock.From(Units().Get(doc.Spawns[0].UnitId));
            edited.DisplayName = "Momotaro-WIP";
            edited.MaxHp = 271;
            edited.AttackApCost = 6;
            edited.AttackRange = 3;
            edited.PushApCost = 2;
            doc.Spawns[0].PendingStats = edited;

            string stored = PendingEditsStore.Save(doc);

            List<string> warnings = new List<string>();
            EncounterDocument restored = EncounterDocumentIO.FromText(
                EncounterDocumentIO.ToText(doc, terrain), terrain, warnings);
            PendingEditsStore.Load(restored, stored);

            Assert.IsTrue(restored.Spawns[0].HasPendingStats, "The unsaved stat edit was lost.");
            UnitStatBlock back = restored.Spawns[0].PendingStats;
            Assert.AreEqual("Momotaro-WIP", back.DisplayName);
            Assert.AreEqual(271, back.MaxHp);
            Assert.AreEqual(6, back.AttackApCost);
            Assert.AreEqual(3, back.AttackRange);
            Assert.AreEqual(2, back.PushApCost);

            Assert.IsFalse(restored.Spawns[1].HasPendingStats, "An untouched unit gained an edit.");
        }

        [Test]
        public void ReloadRestore_SurvivesAnEmptyOrDamagedStore()
        {
            TerrainCatalog terrain = Terrain();
            EncounterDocument doc = SmallDoc(terrain);

            Assert.AreEqual(string.Empty, PendingEditsStore.Save(doc), "Nothing was edited.");

            PendingEditsStore.Load(doc, null);
            PendingEditsStore.Load(doc, "");
            PendingEditsStore.Load(doc, "not a record");
            PendingEditsStore.Load(doc, "99 unit id=pending name=X hp=1 atk=1 def=1 move=1 ap=1 range=1 attackCost=1 guardCost=1");
            PendingEditsStore.Load(doc, "0 unit id=pending garbage");

            for (int i = 0; i < doc.Spawns.Count; i++)
                Assert.IsFalse(doc.Spawns[i].HasPendingStats,
                    "A damaged record was accepted as a stat edit.");
        }

        // --------------------------------------------------------- end to end

        /// <summary>
        /// The whole authoring path, in the order a planner walks it: build a
        /// 10x8 map, paint each terrain kind, place a squad, retune a unit, set
        /// an arrival wave and an objective, then hand the result to the real
        /// runtime and take a turn in it.
        ///
        /// This is the acceptance criterion as a test. Every step below is a
        /// button in the window; if this passes, the data path behind those
        /// buttons produces a battle that actually starts.
        /// </summary>
        [Test]
        public void APlannerCanBuildAnEncounterAndTheRuntimeCanPlayIt()
        {
            TerrainCatalog terrain = Terrain();
            UnitCatalog units = Units();
            AiProfileCatalog profiles = Profiles();

            int floor = EncounterDocumentIO.DefaultTerrainIndex(terrain);
            int wall = EncounterDocumentIO.WallTerrainIndex(terrain);

            // 1. a 10x8 map
            EncounterDocument doc = new EncounterDocument(10, 8, floor)
            {
                Id = "planner-walkthrough",
                DisplayName = "Planner-Walkthrough"
            };
            Assert.AreEqual(10, doc.Width);
            Assert.AreEqual(8, doc.Height);

            // 2. paint every terrain kind the brief names
            TerrainDef swamp, chasm, forest;
            Assert.IsTrue(terrain.TryGetByName("Mire", out swamp));
            Assert.IsTrue(terrain.TryGetByName("Chasm", out chasm));
            Assert.IsTrue(terrain.TryGetByName("Forest", out forest));

            doc.SetTerrain(4, 3, swamp.Index);
            doc.SetTerrain(5, 3, chasm.Index);
            doc.SetTerrain(6, 3, forest.Index);
            doc.SetTerrain(2, 2, wall);
            Assert.AreEqual(TileStyle.Swamp, PrototypeVisuals.StyleOf(terrain[doc.TerrainAt(4, 3)]));
            Assert.AreEqual(TileStyle.Chasm, PrototypeVisuals.StyleOf(terrain[doc.TerrainAt(5, 3)]));
            Assert.AreEqual(TileStyle.Obstacle, PrototypeVisuals.StyleOf(terrain[doc.TerrainAt(2, 2)]));

            // 3-4. place the player and two enemies
            doc.Spawns.Add(new SpawnEntry { Faction = Faction.Player, UnitId = "momotaro", Position = new Coord(1, 6) });
            doc.Spawns.Add(new SpawnEntry { Faction = Faction.Enemy, UnitId = "kohaku", Position = new Coord(8, 1), AiProfileId = "rusher" });

            // 5. an enemy that walks in on turn 3
            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Enemy, UnitId = "kohaku", Position = new Coord(1, 1),
                AiProfileId = "cautious", ArrivesOnTurn = 3
            });

            // 6. retune the player's stats — HP / ATK / DEF / MOVE / range / skill AP
            UnitStatBlock tuned = UnitStatBlock.From(units.Get("momotaro"));
            tuned.DisplayName = "Momotaro-Walkthrough";
            tuned.MaxHp = 260;
            tuned.Atk = 72;
            tuned.Def = 44;
            tuned.Move = 5;
            tuned.AttackRange = 2;
            tuned.PushApCost = 3;
            tuned.PushRange = 1;
            doc.Spawns[0].PendingStats = tuned;

            // 7. an objective with a clock
            doc.ObjectiveKind = ObjectiveKind.Rout;
            doc.TurnLimit = 12;

            // 8. materialise the stat edit into a real unit id, as Save/Play do
            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(doc, units, UnitIds());
            Assert.AreEqual(1, plan.NewUnitLines.Count, "The retuned unit did not become a row.");

            UnitCatalog withVariant = UnitLoader.Parse(
                UnitVariantWriter.AppendBlock(Read("units"), plan.NewUnitLines));
            UnitVariantWriter.ApplyToDocument(doc, plan);

            Assert.AreEqual("momotaro_v1", doc.Spawns[0].UnitId);
            UnitDef variant = withVariant.Get("momotaro_v1");
            Assert.AreEqual(260, variant.MaxHp);
            Assert.AreEqual(5, variant.Move);
            Assert.AreEqual(2, variant.AttackRange);
            Assert.AreEqual(3, variant.PushApCost);
            Assert.AreEqual(300, withVariant.Get("momotaro").MaxHp, "The original unit was modified.");

            // 9. the gate, which is what Save and Play both go through
            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(doc, terrain, withVariant, profiles);
            Assert.IsTrue(gate.Ok, "The finished encounter failed the gate: "
                + (gate.LoaderError ?? Join(gate.Issues)));

            // 10. the runtime opens it and a battle starts
            EncounterDef parsed = EncounterLoader.Parse(gate.Text, terrain);
            Assert.AreEqual("planner-walkthrough", parsed.Id);
            Assert.AreEqual(12, parsed.Objective.TurnLimit);
            Assert.AreEqual(10, parsed.Map.Width);
            Assert.AreEqual(8, parsed.Map.Height);

            BattleSetup setup = EncounterLoader.CreateBattle(parsed, withVariant, profiles);
            Assert.AreEqual(2, setup.State.Units.Count, "The turn-3 arrival should not be on the board yet.");
            Assert.AreEqual(1, setup.State.Reinforcements.Count);
            Assert.AreEqual(3, setup.State.Reinforcements[0].Turn);

            // 11. and it is actually playable — one real command through the funnel
            BattleState state = BattleSimulator.Begin(setup.State).State;
            Assert.AreEqual(BattleOutcome.InProgress, state.Outcome);

            UnitState hero = state.Units[0];
            Assert.AreEqual(260, hero.Def.MaxHp, "The battle did not use the retuned stats.");

            ExecuteResult moved = BattleSimulator.Execute(state,
                new MoveCommand(hero.Id, new[] { new Coord(1, 5) }));
            Assert.IsTrue(moved.Ok, "The first move was rejected: " + moved.RejectReason);
            Assert.AreEqual(new Coord(1, 5), moved.State.FindUnit(hero.Id).Position);
        }

        // ----------------------------------------------------------------- setup

        /// <summary>
        /// A minimal legal encounter: walled 6x6, one player, two enemies.
        /// Built through the same document API the window uses.
        /// </summary>
        private static EncounterDocument SmallDoc(TerrainCatalog terrain)
        {
            int floor = EncounterDocumentIO.DefaultTerrainIndex(terrain);
            int wall = EncounterDocumentIO.WallTerrainIndex(terrain);

            EncounterDocument doc = new EncounterDocument(6, 6, floor);
            for (int x = 0; x < 6; x++) { doc.SetTerrain(x, 0, wall); doc.SetTerrain(x, 5, wall); }
            for (int y = 0; y < 6; y++) { doc.SetTerrain(0, y, wall); doc.SetTerrain(5, y, wall); }

            doc.Id = "editor-test";
            doc.DisplayName = "Editor-Test";

            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Player,
                UnitId = "momotaro",
                Position = new Coord(2, 4)
            });
            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Enemy,
                UnitId = "kohaku",
                Position = new Coord(2, 1),
                AiProfileId = "rusher"
            });
            doc.Spawns.Add(new SpawnEntry
            {
                Faction = Faction.Enemy,
                UnitId = "kohaku",
                Position = new Coord(3, 1),
                AiProfileId = "cautious"
            });
            return doc;
        }
    }
}
