using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using UnityEngine;

namespace Ediki.Unity
{
    /// <summary>
    /// Loads the text data and puts a playable battle on screen.
    ///
    /// Auto-installs itself after scene load so pressing Play in any scene runs the
    /// prototype — there is no prefab to wire up and no scene asset to keep in sync.
    /// Delete the RuntimeInitializeOnLoadMethod below once a real menu/scene flow exists.
    /// </summary>
    public sealed class PrototypeBootstrap : MonoBehaviour
    {
        public const string DataPath = "Data/";

        /// <summary>
        /// Boots into the mechanics-validation map (accepted 2026-08-14).
        ///
        /// gym-big-split is the only layout measured so far where the base
        /// mechanics alone separate careful play from reckless play
        /// (hold 50% vs charge 2%). It is NOT a decision about what the narrative
        /// Stage 01 becomes — that is still open, see docs OD-23.
        /// </summary>
        public const string EncounterName = "gym-big-split.encounter";

        /// <summary>
        /// Maps the player can flip between during a session, in number-key order.
        /// Comparing them back to back is the whole point of a playtest.
        /// </summary>
        public static readonly string[] SelectableEncounters =
        {
            "gym-big-split.encounter",   // 1 - 18x12. Best M5 separation measured so far
            "gym-lanes.encounter",       // 2 - 24x16 successor: three routes that M6 says are real
            "gym-lanes-pair.encounter",  // 3 - gym-lanes + Zhengshou (two player units)
            "gym-lanes-bow.encounter",   // 4 - gym-lanes + two range-2 enemies
            "gym-big-north.encounter",   // 5 - 18x12, enemies all on one side
            "gym-arena-contact.encounter", // 6 - open arena, everything in reach at once
            "gym-arena-stagger.encounter", // 7 - open arena, enemies arrive one at a time
            "stage01.encounter",         // 8 - original 12x10 single chokepoint
            "stage01-open.encounter",    // 9 - open control, no chokepoint at all

            // 0 - the one cell where the kill objective changes the answer most:
            // corridor-hold wins 11% here and hitting the marked unit wins 31%,
            // at identical exposure. Everything else on this list is a rout, so
            // this is the only entry where "which enemies must die" is a question
            // a human can be asked. The other kill encounters (gym-big-split-kill,
            // the boss3/boss6 rungs, gym-lanes-kill) are batch-only.
            "gym-big-split-boss4-kill.encounter",

            // - and = : the candidate opening stage and its control. Side by side
            // on purpose — the only way to feel what the control kit is worth is
            // to play the same map with it and without it, and the scripted
            // strategies cannot answer that (control-hold measures WORSE than
            // corridor-hold, and the reason looks like a policy flaw rather than
            // a verdict on the skills).
            "gym-opening.encounter",
            "gym-opening-noskill.encounter",

            // [ and ] : two of the four crucible maps, both carrying the T13 squad
            // (Momotaro B / Genjin B / Kagemaru A / Masamori A — the one squad that
            // holds all four verbs at once, by construction rather than by choice:
            // see SquadMatrix, where T13 is 1100 in the A/B bit order).
            //
            // These two and not the other two, because push and the armour break
            // both resolve inside a single exchange and can therefore be FELT in
            // one sitting. Slow and taunt are answers to arrival order and target
            // choice; neither reads as a decision until several turns have gone by,
            // so gym-crucible-delay and gym-crucible-defend are batch-only.
            //
            // They displaced gym-squad-crucible and gym-duo-division-flat. Both
            // files are still there and still run in batch — the key list is 14
            // slots wide, not a statement about what matters.
            "gym-crucible-chasm.encounter",
            "gym-crucible-armor.encounter",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (FindAnyObjectByType<PrototypeBootstrap>() != null) return;
            GameObject go = new GameObject("[Ediki Prototype]");
            go.AddComponent<PrototypeBootstrap>();
        }

        private BattleRunner _runner;

        private void Awake()
        {
            // Resolved HERE, in Awake, and passed down. ResolveEncounterName can
            // read EditorPrefs, which Unity refuses to serve to a MonoBehaviour
            // field initializer — and AddComponent below runs one.
            string encounterName = ResolveEncounterName();

            BattleSetup setup;
            if (!TryLoadBattle(encounterName, out setup, out string error))
            {
                Debug.LogError("[Ediki] Failed to load Stage 01 data:\n" + error);
                enabled = false;
                return;
            }

            _runner = gameObject.AddComponent<BattleRunner>();
            _runner.Initialise(setup, encounterName);
        }

        /// <summary>
        /// EditorPrefs key the Prototype Editor sets before it enters play mode.
        ///
        /// This is the ONLY hook the editor has into the runtime, and it is a
        /// filename — no rules, no state, no data. The editor writes a validated
        /// encounter into Resources/Data and names it here; everything after that
        /// is the same load path any other map takes, so a battle started from the
        /// editor is not a special kind of battle.
        ///
        /// EditorPrefs rather than a static field because entering play mode
        /// reloads the domain and wipes statics, and rather than a file in
        /// Resources because a boot setting stored as data would change what a
        /// BUILT player does. Guarded by UNITY_EDITOR below, so it does not.
        /// </summary>
        public const string EditorEncounterKey = "Ediki.PrototypeEditor.PlayEncounter";

        /// <summary>
        /// True while this session was launched from the Prototype Editor's
        /// 試玩 button. Always false in a build.
        ///
        /// The battle screen uses it to stop offering the map-switch shortcuts:
        /// they load a DIFFERENT encounter, and the one under test only exists
        /// in the editor's document, so there would be no way back to it.
        /// </summary>
        public static bool IsEditorPlaytest
        {
            get
            {
#if UNITY_EDITOR
                return !string.IsNullOrEmpty(
                    UnityEditor.EditorPrefs.GetString(EditorEncounterKey, string.Empty));
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Which encounter this session boots. Always <see cref="EncounterName"/>
        /// in a build — the override branch does not compile into one.
        /// </summary>
        public static string ResolveEncounterName()
        {
#if UNITY_EDITOR
            string overridden = UnityEditor.EditorPrefs.GetString(EditorEncounterKey, string.Empty);
            if (!string.IsNullOrEmpty(overridden)) return overridden;
#endif
            return EncounterName;
        }

        public static bool TryLoadBattle(out BattleSetup setup, out string error)
        {
            return TryLoadBattle(ResolveEncounterName(), out setup, out error);
        }

        public static bool TryLoadBattle(string encounterName, out BattleSetup setup, out string error)
        {
            setup = null;
            error = null;

            try
            {
                TerrainCatalog terrain = TerrainLoader.Parse(ReadText("terrain"));
                UnitCatalog units = UnitLoader.Parse(ReadText("units"));
                AiProfileCatalog profiles = AiProfileLoader.Parse(ReadText("ai-profiles"));
                EncounterDef encounter = EncounterLoader.Parse(ReadText(encounterName), terrain);

                setup = EncounterLoader.CreateBattle(encounter, units, profiles);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ReadText(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>(DataPath + name);
            if (asset == null)
                throw new DataFormatException("Missing data file: Assets/_Project/Resources/" + DataPath + name + ".txt");
            return asset.text;
        }
    }
}
