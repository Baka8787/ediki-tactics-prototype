using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using Ediki.Unity;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Ediki.Tests
{
    /// <summary>
    /// Keyboard bindings across the whole battle screen.
    ///
    /// Split out from BattleActionTests because it is the one part that needs
    /// Unity.InputSystem — the action list itself is deliberately input-free so
    /// it can be checked without an editor. This file is the seam's own test.
    /// </summary>
    public class BattleKeyTests
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

        /// <summary>
        /// The bug this exists for: 攻擊 was given the key `1`, which the battle
        /// screen had already claimed for "switch to map 1". The map switch is
        /// tested first and returns, so pressing attack silently loaded a
        /// different encounter — and during an editor playtest that discarded the
        /// map under test, which is exactly how it was found.
        ///
        /// The per-unit uniqueness check in BattleActionTests could never have
        /// caught it: both lists were internally consistent, and the collision
        /// was between them.
        /// </summary>
        [Test]
        public void NoActionShortcutCollidesWithAKeyTheBattleScreenAlreadyClaims()
        {
            UnitCatalog units = Units();
            HashSet<Key> reserved = new HashSet<Key>(BattleKeys.Reserved);

            foreach (string id in UnitIds())
                foreach (UnitAction action in BattleActions.For(units.Get(id)))
                {
                    Key key = BattleKeys.KeyFor(action.ShortcutLabel);

                    Assert.AreNotEqual(Key.None, key,
                        id + " / " + action.Label + ": shortcut \"" + action.ShortcutLabel
                        + "\" maps to no key — BattleKeys.KeyFor needs a case for it.");

                    Assert.IsFalse(reserved.Contains(key),
                        id + " / " + action.Label + " is bound to " + key
                        + ", which the battle screen handles before actions ever see it.");
                }
        }

        [Test]
        public void ReservedKeysContainNoDuplicates()
        {
            HashSet<Key> seen = new HashSet<Key>();
            foreach (Key key in BattleKeys.Reserved)
                Assert.IsTrue(seen.Add(key), key + " is listed twice as a reserved key.");
        }
    }
}
