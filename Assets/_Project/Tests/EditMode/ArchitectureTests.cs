using System;
using System.Reflection;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// A1-A7 from docs/06-validation/test-strategy.md.
    /// These do not test gameplay — they test that the architecture has not been
    /// quietly broken.
    /// </summary>
    public class ArchitectureTests
    {
        // ------------------------------------------------------------------ A1

        [Test]
        public void A1_CoreAssembly_DoesNotReferenceUnity()
        {
            Assembly core = typeof(BattleState).Assembly;
            Assert.AreEqual("Ediki.Core", core.GetName().Name, "Core types moved out of Ediki.Core.");

            foreach (AssemblyName referenced in core.GetReferencedAssemblies())
            {
                string name = referenced.Name;
                Assert.IsFalse(name.StartsWith("UnityEngine", StringComparison.Ordinal),
                    "Ediki.Core references " + name + ". The asmdef must keep 'No Engine References' ticked (ADR-0001).");
                Assert.IsFalse(name.StartsWith("UnityEditor", StringComparison.Ordinal),
                    "Ediki.Core references " + name + ".");
            }
        }

        [Test]
        public void A1b_CoreAssembly_DoesNotReferencePresentationLayer()
        {
            Assembly core = typeof(BattleState).Assembly;
            foreach (AssemblyName referenced in core.GetReferencedAssemblies())
            {
                Assert.AreNotEqual("Ediki.Unity", referenced.Name,
                    "Dependency direction is Unity -> Core only. Core must never depend on the presentation layer.");
            }
        }

        // ------------------------------------------------------------------ A2

        [Test]
        public void A2_Execute_DoesNotMutateTheStateItIsGiven()
        {
            BattleState state = TestWorld.Begun();
            uint before = StateHasher.Hash(state);

            ExecuteResult result = BattleSimulator.Execute(
                state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));

            Assert.IsTrue(result.Ok, result.RejectReason);
            Assert.AreEqual(before, StateHasher.Hash(state), "Execute mutated the caller's state — it must be pure.");
            Assert.AreNotEqual(before, StateHasher.Hash(result.State), "Execute returned an unchanged state.");
        }

        [Test]
        public void A2b_RejectedCommand_LeavesStateUntouchedAndLogEmpty()
        {
            BattleState state = TestWorld.Begun();
            uint before = StateHasher.Hash(state);

            // Walking into blocking terrain.
            ExecuteResult result = BattleSimulator.Execute(
                state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 0))));

            Assert.IsFalse(result.Ok);
            Assert.IsNotNull(result.RejectReason);
            Assert.AreEqual(0, result.Log.Count, "A rejected command must not emit effects.");
            Assert.AreEqual(before, StateHasher.Hash(state));
            Assert.AreEqual(before, StateHasher.Hash(result.State), "Rejection must hand back the original state.");
        }

        // ------------------------------------------------------------------ A3

        [Test]
        public void A3_Clone_IsolatesEveryNestedUnit()
        {
            BattleState original = TestWorld.Begun();
            uint before = StateHasher.Hash(original);

            BattleState copy = original.Clone();
            Assert.AreEqual(before, StateHasher.Hash(copy), "Clone must produce an identical state.");

            // Mutate every field on the copy that the hash covers.
            for (int i = 0; i < copy.Units.Count; i++)
            {
                UnitState u = copy.Units[i];
                u.Hp -= 1;
                u.Ap -= 1;
                u.Position = new Coord(u.Position.X + 1, u.Position.Y);
                u.IsGuarding = !u.IsGuarding;
                u.HasEndedTurn = !u.HasEndedTurn;
                u.IsActivated = !u.IsActivated;
            }
            copy.TurnIndex += 5;
            copy.Outcome = BattleOutcome.Victory;

            Assert.AreEqual(before, StateHasher.Hash(original),
                "Mutating the clone changed the original — Clone() is shallow somewhere (ADR-0004).");
            Assert.AreNotSame(original.Units[0], copy.Units[0], "Unit instances are shared between states.");
        }

        // ------------------------------------------------------------------ A4

        // Golden constants produced by the same Core code compiled standalone
        // (dotnet, LangVersion 9). If one of these fails, ask FIRST whether the
        // change was supposed to alter simulation results — see workflows.md section 6.
        private const uint GoldenAfterBegin = 3080245196u;
        private const uint GoldenAfterPlayerTurn = 3711821134u;
        private const uint GoldenAfterEnemyTurn = 1561619701u;

        [Test]
        public void A4_GoldenHash_InitialState()
        {
            Assert.AreEqual(GoldenAfterBegin, StateHasher.Hash(TestWorld.Begun()));
        }

        [Test]
        public void A4b_GoldenHash_AfterFixedCommandSequence()
        {
            BattleState state = RunGoldenScript();
            Assert.AreEqual(GoldenAfterPlayerTurn, StateHasher.Hash(state));

            EffectLog log = new EffectLog();
            BattleState afterEnemy = TestWorld.Create().Ai.RunFactionTurn(state, Faction.Enemy, log).State;
            Assert.AreEqual(GoldenAfterEnemyTurn, StateHasher.Hash(afterEnemy));
        }

        [Test]
        public void A4c_SameScriptFromIndependentlyBuiltWorlds_ProducesSameHash()
        {
            Assert.AreEqual(StateHasher.Hash(RunGoldenScript()), StateHasher.Hash(RunGoldenScript()));
        }

        [Test]
        public void A4d_DifferentScript_ProducesDifferentHash()
        {
            BattleState a = RunGoldenScript();

            BattleState b = TestWorld.Begun();
            b = TestWorld.Apply(b, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 2))));
            b = TestWorld.Apply(b, new EndTurnCommand(Faction.Player));

            Assert.AreNotEqual(StateHasher.Hash(a), StateHasher.Hash(b),
                "Different play produced the same hash — the hash is not covering enough state.");
        }

        private static BattleState RunGoldenScript()
        {
            BattleState state = TestWorld.Begun();
            state = TestWorld.Apply(state, new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(2, 1), TestWorld.C(3, 1))));
            state = TestWorld.Apply(state, new GuardCommand(TestWorld.HeroId));
            state = TestWorld.Apply(state, new EndTurnCommand(Faction.Player));
            return state;
        }

        // ------------------------------------------------------------------ A5

        [Test]
        public void A5_MovementCostComesFromData_NotCode()
        {
            // Same map, same command — only the terrain data differs. If the cost
            // were hardcoded, both worlds would spend the same AP.
            const string cheapForest =
                "terrain name=Road symbol=. cost=1 blocks=false\n" +
                "terrain name=Forest symbol=f cost=1 blocks=false\n" +
                "terrain name=Blocking symbol=# cost=0 blocks=true\n";

            int expensive = ApSpentEnteringForest(TestWorld.Terrain);
            int cheap = ApSpentEnteringForest(cheapForest);

            Assert.AreEqual(2, expensive, "Forest should cost the 2 AP declared in the fixture data.");
            Assert.AreEqual(1, cheap, "Changing terrain data did not change movement cost — cost is hardcoded (A5).");
        }

        [Test]
        public void A5b_ActionApCostsComeFromData()
        {
            const string cheapAttack =
                "unit id=hero  name=Hero  hp=100 atk=30 def=10 move=4 ap=8 range=1 attackCost=1 guardCost=3\n" +
                "unit id=grunt name=Grunt hp=40  atk=20 def=5  move=3 ap=8 range=1 attackCost=4 guardCost=3\n";

            UnitCatalog normal = UnitLoader.Parse(TestWorld.Units);
            UnitCatalog modified = UnitLoader.Parse(cheapAttack);

            Assert.AreEqual(4, normal.Get("hero").AttackApCost);
            Assert.AreEqual(1, modified.Get("hero").AttackApCost,
                "Attack cost must be readable from data so OD-01 can be re-tuned without code changes.");
        }

        private static int ApSpentEnteringForest(string terrainData)
        {
            TerrainCatalog terrain = TerrainLoader.Parse(terrainData);
            UnitCatalog units = UnitLoader.Parse(TestWorld.Units);
            AiProfileCatalog profiles = AiProfileLoader.Parse(TestWorld.AiProfiles);
            EncounterDef enc = EncounterLoader.Parse(TestWorld.Encounter, terrain);
            BattleState state = BattleSimulator.Begin(EncounterLoader.CreateBattle(enc, units, profiles).State).State;

            // (1,1) -> (1,2) is road, (1,2) -> (2,2) is forest.
            ExecuteResult r = BattleSimulator.Execute(state,
                new MoveCommand(TestWorld.HeroId, TestWorld.Path(TestWorld.C(1, 2), TestWorld.C(2, 2))));
            Assert.IsTrue(r.Ok, r.RejectReason);

            ApSpent spent = r.Log.First<ApSpent>();
            return spent.Amount - 1;   // subtract the road step
        }

        // ------------------------------------------------------------------ A6

        [Test]
        public void A6_NeighborOrder_IsStableForTheSameInput()
        {
            IGridTopology topology = new SquareGrid4(7, 5);
            Coord c = TestWorld.C(3, 2);

            System.Collections.Generic.List<Coord> first = new System.Collections.Generic.List<Coord>(topology.Neighbors(c));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                System.Collections.Generic.List<Coord> again = new System.Collections.Generic.List<Coord>(topology.Neighbors(c));
                CollectionAssert.AreEqual(first, again, "Neighbour order changed between identical calls.");
            }

            Assert.AreEqual(4, first.Count);
        }

        [Test]
        public void A6b_ReachableCells_AreReturnedInDeterministicOrder()
        {
            BattleState state = TestWorld.Begun();
            UnitState hero = state.FindUnit(TestWorld.HeroId);

            var first = MovementCalculator.ComputeFor(state, hero).ReachableCells;
            var again = MovementCalculator.ComputeFor(state, hero).ReachableCells;

            CollectionAssert.AreEqual(first, again);
            for (int i = 1; i < first.Count; i++)
                Assert.Less(first[i - 1].CompareTo(first[i]), 0, "ReachableCells must be sorted, not hash-ordered.");
        }

        // ------------------------------------------------------------------ A7

        [Test]
        public void A7_QueriesAreReadOnly()
        {
            BattleState state = TestWorld.Begun();
            uint before = StateHasher.Hash(state);

            BattleQueries.StaticExposure(state.Map, TestWorld.C(3, 1));
            BattleQueries.EffectiveExposure(state, TestWorld.C(3, 1), Faction.Player);
            BattleQueries.DangerZone(state, Faction.Player);
            BattleQueries.ThreatRange(state, state.FindUnit(TestWorld.GruntId));
            BattleQueries.AttackableTargets(state, state.FindUnit(TestWorld.HeroId));

            Assert.AreEqual(before, StateHasher.Hash(state),
                "A read-only query mutated the battle state. Presentation calls these every frame.");
        }
    }
}
