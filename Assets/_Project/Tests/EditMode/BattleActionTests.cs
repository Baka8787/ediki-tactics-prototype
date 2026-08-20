using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Unity;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The playtest action bar's contents.
    ///
    /// The reason this is tested at all: the bar exists because the skill list
    /// is going to grow, and the whole claim behind it is that a unit gains a
    /// button by having a cost in units.txt — no code change, no list to keep in
    /// sync. That claim is only true if nothing here is hardcoded per unit, so
    /// every assertion below reads the expected number back off the UnitDef
    /// rather than writing it out.
    ///
    /// This covers WHICH actions are offered and what they cost. Whether the
    /// resulting command is legal is BattleSimulator's business and is tested
    /// where the rules are.
    /// </summary>
    public class BattleActionTests
    {
        private static UnitCatalog Units()
        {
            UnityEngine.TextAsset asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/units");
            Assert.IsNotNull(asset, "Missing Data/units.txt");
            return UnitLoader.Parse(asset.text);
        }

        private static List<string> UnitIds()
        {
            UnityEngine.TextAsset asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/units");
            List<string> ids = new List<string>();
            foreach (DataLine line in DataLine.ParseAll(asset.text))
                if (line.Keyword == "unit") ids.Add(line.GetString("id"));
            return ids;
        }

        private static UnitAction Find(List<UnitAction> actions, string label)
        {
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Label == label) return actions[i];
            return null;
        }

        // ------------------------------------------------------------ the basics

        [Test]
        public void EveryUnitCanAlwaysAttackGuardAndWait()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
            {
                List<UnitAction> actions = BattleActions.For(units.Get(id));

                Assert.IsNotNull(Find(actions, "攻擊"), id + " has no attack button.");
                Assert.IsNotNull(Find(actions, "格擋"), id + " has no guard button.");
                Assert.IsNotNull(Find(actions, "待機"), id + " has no wait button.");
            }
        }

        [Test]
        public void CostsAndRangesComeFromTheUnitDefinition()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
            {
                UnitDef def = units.Get(id);
                List<UnitAction> actions = BattleActions.For(def);

                UnitAction attack = Find(actions, "攻擊");
                Assert.AreEqual(def.AttackApCost, attack.ApCost, id + ": attack cost was not read from data.");
                Assert.AreEqual(def.AttackRange, attack.Range, id + ": attack range was not read from data.");
                Assert.AreEqual(ActionTarget.Enemy, attack.Target);

                Assert.AreEqual(def.GuardApCost, Find(actions, "格擋").ApCost,
                    id + ": guard cost was not read from data.");
            }
        }

        [Test]
        public void AttackAndPurifyUseConfirmableTargetingModes()
        {
            UnitCatalog units = Units();
            Assert.AreEqual(ActionTarget.Enemy,
                Find(BattleActions.For(units.Get("momotaro")), "攻擊").Target);
            Assert.AreEqual(ActionTarget.SelfConfirm,
                Find(BattleActions.For(units.Get("momotaro")), "淨化").Target);
        }

        // --------------------------------------------------- skills appear by data

        [Test]
        public void ASkillAppearsExactlyWhenTheUnitHasACostForIt()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
            {
                UnitDef def = units.Get(id);
                List<UnitAction> actions = BattleActions.For(def);

                AssertPresence(id, actions, "擊退", def.CanPush, def.PushApCost, def.PushRange);
                AssertPresence(id, actions, "減速", def.CanSlow, def.SlowApCost, def.SlowRange);
                AssertPresence(id, actions, "破甲", def.CanArmorBreak, def.ArmorBreakApCost, def.ArmorBreakRange);
                AssertPresence(id, actions, "引誘", def.CanTaunt, def.TauntApCost, -1);
                AssertPresence(id, actions, "淨化", def.CanPurify, def.PurifyApCost, -1);
                AssertPresence(id, actions, "休息", def.CanRest, def.RestApCost, -1);
            }
        }

        private static void AssertPresence(string id, List<UnitAction> actions, string label,
                                           bool expected, int cost, int range)
        {
            UnitAction action = Find(actions, label);

            if (!expected)
            {
                Assert.IsNull(action, id + " offers " + label + " but has no cost for it.");
                return;
            }

            Assert.IsNotNull(action, id + " has a cost for " + label + " but no button.");
            Assert.AreEqual(cost, action.ApCost, id + " / " + label + ": cost was not read from data.");
            if (range >= 0) Assert.AreEqual(range, action.Range, id + " / " + label + ": range was not read from data.");
        }

        /// <summary>
        /// The concrete claim, on the units the crucible maps actually carry.
        /// If someone re-prices these in units.txt, the bar follows without
        /// anybody touching this code — and this test follows with it.
        /// </summary>
        [Test]
        public void TheCrucibleSquadGetsTheKitItsDataSaysItHas()
        {
            UnitCatalog units = Units();

            Assert.IsNotNull(Find(BattleActions.For(units.Get("Momotaro_B")), "擊退"), "Momotaro_B lost push.");
            Assert.IsNotNull(Find(BattleActions.For(units.Get("Genjin_B")), "破甲"), "Genjin_B lost armour break.");
            Assert.IsNotNull(Find(BattleActions.For(units.Get("Kagemaru_A")), "減速"), "Kagemaru_A lost slow.");
            Assert.IsNotNull(Find(BattleActions.For(units.Get("Masamori_A")), "引誘"), "Masamori_A lost taunt.");

            // And the plain builds must NOT sprout one.
            Assert.IsNull(Find(BattleActions.For(units.Get("Momotaro_A")), "擊退"), "Momotaro_A gained push.");
            Assert.IsNull(Find(BattleActions.For(units.Get("kagemaru_plain")), "減速"), "kagemaru_plain gained slow.");
        }

        // ------------------------------------------------------------- targeting

        [Test]
        public void OnlyRangedActionsAskForATarget()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
                foreach (UnitAction action in BattleActions.For(units.Get(id)))
                {
                    if (action.Target == ActionTarget.Enemy)
                        Assert.Greater(action.Range, 0,
                            id + " / " + action.Label + " wants a target but has no range to reach one.");
                    else
                        Assert.AreEqual(0, action.Range,
                            id + " / " + action.Label + " is self-targeted but carries a range.");
                }
        }

        [Test]
        public void EveryActionBuildsTheCommandItsLabelPromises()
        {
            UnitCatalog units = Units();
            const int Actor = 7;
            const int Target = 9;

            List<UnitAction> actions = BattleActions.For(units.Get("Momotaro_B"));
            Assert.IsInstanceOf<AttackCommand>(Find(actions, "攻擊").Build(Actor, Target));
            Assert.IsInstanceOf<GuardCommand>(Find(actions, "格擋").Build(Actor, Target));
            Assert.IsInstanceOf<RestCommand>(Find(actions, "休息").Build(Actor, Target));
            Assert.IsInstanceOf<PushCommand>(Find(actions, "擊退").Build(Actor, Target));
            Assert.IsInstanceOf<WaitCommand>(Find(actions, "待機").Build(Actor, Target));

            Assert.IsInstanceOf<SlowCommand>(
                Find(BattleActions.For(units.Get("Kagemaru_A")), "減速").Build(Actor, Target));
            Assert.IsInstanceOf<ArmorBreakCommand>(
                Find(BattleActions.For(units.Get("Genjin_B")), "破甲").Build(Actor, Target));
            Assert.IsInstanceOf<TauntCommand>(
                Find(BattleActions.For(units.Get("Masamori_A")), "引誘").Build(Actor, Target));
            Assert.IsInstanceOf<PurifyCommand>(
                Find(BattleActions.For(units.Get("momotaro_pure")), "淨化").Build(Actor, Target));

            // The command must carry the ids it was handed, or the bar would fire
            // skills at the wrong unit.
            PushCommand push = (PushCommand)Find(actions, "擊退").Build(Actor, Target);
            Assert.AreEqual(Actor, push.ActorId);
            Assert.AreEqual(Target, push.TargetId);
        }

        // ------------------------------------------------------------- shortcuts

        /// <summary>
        /// Two actions on one key would make one of them unreachable from the
        /// keyboard, and the bar would look fine while doing it.
        /// </summary>
        [Test]
        public void NoUnitHasTwoActionsOnTheSameKey()
        {
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
            {
                HashSet<string> used = new HashSet<string>();
                foreach (UnitAction action in BattleActions.For(units.Get(id)))
                {
                    Assert.IsFalse(string.IsNullOrEmpty(action.ShortcutLabel),
                        id + " / " + action.Label + " has no shortcut.");
                    Assert.IsTrue(used.Add(action.ShortcutLabel),
                        id + ": two actions share the key " + action.ShortcutLabel + ".");
                }
            }
        }

        [Test]
        public void EveryActionIsLabelledAndExplained()
        {
            // The bar is the planner-facing surface, so an action with no hint is
            // a skill nobody can find out the meaning of without reading code.
            UnitCatalog units = Units();

            foreach (string id in UnitIds())
                foreach (UnitAction action in BattleActions.For(units.Get(id)))
                {
                    Assert.IsFalse(string.IsNullOrEmpty(action.Label), id + " has an unlabelled action.");
                    Assert.IsFalse(string.IsNullOrEmpty(action.Hint),
                        id + " / " + action.Label + " has no explanation.");
                    Assert.IsNotNull(action.Build, id + " / " + action.Label + " builds no command.");
                }
        }

        [Test]
        public void ANullDefinitionOffersNothingRatherThanThrowing()
        {
            Assert.AreEqual(0, BattleActions.For(null).Count);
        }
    }
}
