using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Ediki.Unity
{
    /// <summary>
    /// Every key the battle screen claims, in one place.
    ///
    /// It exists because of a bug that no per-unit check could have caught: the
    /// action bar handed 攻擊 the key `1`, which the battle screen had already
    /// claimed for "switch to map 1". The map switch is tested first and returns,
    /// so pressing attack silently threw away the encounter under test and loaded
    /// a different one. The action list checked its own shortcuts for duplicates
    /// and found none — the collision was with a list in another class.
    ///
    /// So the two lists live together now, and a test asserts they do not
    /// overlap. Adding a skill whose shortcut is already spoken for fails the
    /// suite instead of quietly rebinding something the player relies on.
    /// </summary>
    public static class BattleKeys
    {
        /// <summary>
        /// Map slots, in number-row order. Digit0 is the tenth slot, not the
        /// zeroth — it sits to the right of 9, so reading it as "10" is what the
        /// keyboard already says.
        /// </summary>
        public static readonly Key[] MapSelect =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
            Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals,
            // The number row runs out at 12. [ and ] carry on from there, and the
            // D1 pair has to be reachable together — playing one without the
            // other says nothing, which is the whole construction of a control.
            Key.LeftBracket, Key.RightBracket
        };

        /// <summary>Keys handled before any unit action sees them. Actions may not use these.</summary>
        public static IEnumerable<Key> Reserved
        {
            get
            {
                for (int i = 0; i < MapSelect.Length; i++) yield return MapSelect[i];

                yield return Key.R;          // restart this map
                yield return Key.Escape;     // cancel aim / help sheet
                yield return Key.F1;         // help sheet
                yield return Key.F2;         // reprint the state block
                yield return Key.F3;         // debug panel
                yield return Key.Tab;        // enemy overlay
                yield return Key.Z;          // own overlay
                yield return Key.Space;      // end turn
                yield return Key.Enter;      // end turn
            }
        }

        /// <summary>
        /// The single place a shortcut LABEL becomes a physical key.
        ///
        /// BattleActions deliberately does not know about input devices, so that
        /// what a unit can do stays testable outside a running editor. This is
        /// the seam. An unmapped label yields Key.None and the action is simply
        /// mouse-only, which is the right failure — a wrong key would be worse.
        /// </summary>
        public static Key KeyFor(string label)
        {
            switch (label)
            {
                case "A": return Key.A;
                case "G": return Key.G;
                case "H": return Key.H;
                case "V": return Key.V;
                case "F": return Key.F;
                case "B": return Key.B;
                case "T": return Key.T;
                case "C": return Key.C;
                case "X": return Key.X;
                default: return Key.None;
            }
        }
    }
}
