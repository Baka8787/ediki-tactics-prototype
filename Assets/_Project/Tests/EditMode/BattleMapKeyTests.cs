using Ediki.Unity;
using NUnit.Framework;

namespace Ediki.Tests
{
    /// <summary>
    /// The one key check that needs the runtime bootstrap, kept apart from
    /// BattleKeyTests so the rest can run outside a Unity editor.
    /// </summary>
    public class BattleMapKeyTests
    {
        [Test]
        public void EverySelectableEncounterHasAKeyToReachIt()
        {
            // The loop that reads these stops at the shorter of the two lists, so
            // an encounter added past the end of the key row becomes unreachable
            // without anything reporting it.
            Assert.GreaterOrEqual(BattleKeys.MapSelect.Length,
                PrototypeBootstrap.SelectableEncounters.Length,
                "There are more selectable encounters than keys to reach them with.");
        }
    }
}
