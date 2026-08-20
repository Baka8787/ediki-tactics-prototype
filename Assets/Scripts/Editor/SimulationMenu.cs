using System.Collections.Generic;
using System.IO;
using Ediki.Sim;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor
{
    /// <summary>
    /// Runs the metrics batch from the Unity menu and drops the results next to
    /// the project. The runner itself is engine-free (Ediki.Sim), so the same
    /// batch can also be driven headlessly outside Unity.
    /// </summary>
    public static class SimulationMenu
    {
        private const string OutputFolder = "SimResults";

        [MenuItem("Ediki/Run Metrics (quick — 30 runs)")]
        public static void RunQuick() => Run(30, 1, 15, 60);

        [MenuItem("Ediki/Run Metrics (full — 300 runs)")]
        public static void RunFull() => Run(300, 1, 15, 60);

        [MenuItem("Ediki/Run Metrics (deterministic — no noise, 1 run)")]
        public static void RunDeterministic() => Run(1, 1, 0, 60);

        [MenuItem("Ediki/Run Squad Matrix (16 squads x 4 crucibles — 50 runs)")]
        public static void RunSquadMatrixQuick() => RunSquadMatrix(50, 1, 15, 60);

        [MenuItem("Ediki/Run Squad Matrix (16 squads x 4 crucibles — 100 runs)")]
        public static void RunSquadMatrixFull() => RunSquadMatrix(100, 1, 15, 60);

        /// <summary>
        /// Every 4x2 squad against every crucible map.
        ///
        /// TWO strategies, not one, and that is the whole design: control-hold is
        /// the only scripted player that issues skills at all, and corridor-hold
        /// is the same positional logic with the kit switched off. A squad's
        /// column is therefore readable as "what this roster is worth" and the gap
        /// between the two rows as "what its verbs were worth to a player who
        /// cannot judge timing".
        ///
        /// ⚠️ That second number has a hard ceiling and it is not the kit's fault:
        /// scripted strategies cannot judge WHEN to use a skill, which this
        /// project has measured six times. The matrix bounds the mechanical value
        /// of a roster. It cannot bound the played value of one.
        /// </summary>
        private static void RunSquadMatrix(int runsPerCell, int baseSeed, int noisePercent, int maxRounds)
        {
            string terrain = Read("terrain");
            string units = Read("units");
            string aiProfiles = Read("ai-profiles");
            if (terrain == null || units == null || aiProfiles == null) return;

            List<MapEntry> maps = new List<MapEntry>();
            AddMap(maps, "gym-crucible-chasm", "gym-crucible-chasm.encounter");
            AddMap(maps, "gym-crucible-armor", "gym-crucible-armor.encounter");
            AddMap(maps, "gym-crucible-delay", "gym-crucible-delay.encounter");
            AddMap(maps, "gym-crucible-defend", "gym-crucible-defend.encounter");
            if (maps.Count != 4) return;

            List<SquadMatrix.Squad> squads = SquadMatrix.All();

            List<MapEntry> cells;
            try
            {
                cells = SquadMatrix.BuildCells(maps, squads);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Ediki] Squad matrix aborted — roster swap failed: " + ex.Message);
                return;
            }

            // one-ply is added as a THIRD row on the same cells, same seed, same
            // enemies. It is an instrument for "can a 1-ply evaluator use the
            // signals the State already carries" — not a better player — so it is
            // read against the other two rather than instead of them.
            List<IPlayerStrategy> strategies = Strategies("one-ply", "control-hold", "corridor-hold");
            if (strategies == null) return;

            SimulationRunner runner;
            try
            {
                runner = new SimulationRunner(terrain, units, aiProfiles);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Ediki] Squad matrix aborted — data failed to load: " + ex.Message);
                return;
            }

            BatchOutput output;
            try
            {
                output = MetricsBatch.Run(runner, cells, strategies, runsPerCell, baseSeed,
                                          noisePercent, maxRounds);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Ediki] Squad matrix run failed: " + ex);
                return;
            }

            string matrix = SquadMatrix.Format(maps, squads, strategies, output);

            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);
            Directory.CreateDirectory(folder);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");

            string matrixPath = Path.Combine(folder, "squad-matrix-" + stamp + ".txt");
            string csvPath = Path.Combine(folder, "squad-matrix-" + stamp + ".csv");

            // The full per-cell report goes out too. The matrix is a reading aid;
            // the EP profiles and instrument limits underneath it are the evidence.
            File.WriteAllText(matrixPath, matrix + "\n\n" + output.Summary);
            File.WriteAllText(csvPath, output.RawCsv);

            Debug.Log("[Ediki] Squad matrix written:\n" + matrixPath + "\n" + csvPath
                      + "\n\n" + matrix);
        }

        private static void Run(int runsPerCell, int baseSeed, int noisePercent, int maxRounds)
        {
            string terrain = Read("terrain");
            string units = Read("units");
            string aiProfiles = Read("ai-profiles");
            if (terrain == null || units == null || aiProfiles == null) return;

            // The third number is the M6 probe row — the row this map's dividing
            // wall sits on. -1 means the map has no wall, so it has no routes.
            List<MapEntry> maps = new List<MapEntry>();
            AddMap(maps, "gym-big-split", "gym-big-split.encounter", 5);
            AddMap(maps, "gym-lanes", "gym-lanes.encounter", 8);
            AddMap(maps, "gym-lanes-pair", "gym-lanes-pair.encounter", 8);
            AddMap(maps, "gym-lanes-bow", "gym-lanes-bow.encounter", 8);
            AddMap(maps, "stage01", "stage01.encounter", 4);
            AddMap(maps, "stage01-open", "stage01-open.encounter");

            // Kill objectives. Each -kill map is paired with the rout map of the
            // SAME roster, because a kill map differs from the plain baseline in
            // two ways at once and the pair is the only way to price the victory
            // condition on its own. The rungs are required-hits on the marked
            // unit: 2 (plain kohaku), 3, 4, 6. Probe rows are inherited from the
            // base map so the M6 histograms stay comparable.
            AddMap(maps, "gym-big-split-kill", "gym-big-split-kill.encounter", 5);
            AddMap(maps, "gym-big-split-boss3", "gym-big-split-boss3.encounter", 5);
            AddMap(maps, "gym-big-split-boss3-kill", "gym-big-split-boss3-kill.encounter", 5);
            AddMap(maps, "gym-big-split-boss4", "gym-big-split-boss4.encounter", 5);
            AddMap(maps, "gym-big-split-boss4-kill", "gym-big-split-boss4-kill.encounter", 5);
            AddMap(maps, "gym-big-split-boss6", "gym-big-split-boss6.encounter", 5);
            AddMap(maps, "gym-big-split-boss6-kill", "gym-big-split-boss6-kill.encounter", 5);
            AddMap(maps, "gym-lanes-kill", "gym-lanes-kill.encounter", 8);
            AddMap(maps, "gym-lanes-boss6", "gym-lanes-boss6.encounter", 8);
            AddMap(maps, "gym-lanes-boss6-kill", "gym-lanes-boss6-kill.encounter", 8);

            // Candidate opening stage, with its two controls. Probe row 4 is its
            // dividing wall. Read the three together or not at all:
            //   -noskill  same party, control kit off -> what the kit is worth
            //   -solo     Momotaro alone              -> what the party is worth
            AddMap(maps, "gym-opening", "gym-opening.encounter", 4);
            AddMap(maps, "gym-opening-noskill", "gym-opening-noskill.encounter", 4);
            AddMap(maps, "gym-opening-solo", "gym-opening-solo.encounter", 4);

            // D1: is "which route" a real decision? Three maps, two variables.
            // The pair the plan asks for is routes/flat; noforest sits between
            // them with ONLY the two forest cells changed, so (routes - noforest)
            // prices the terrain and (noforest - flat) prices the width. Probe
            // row 5 is the southern face of the wall band on all three, so the
            // histograms are directly comparable — see playtest-metrics §22.
            AddMap(maps, "gym-d1-routes", "gym-d1-routes.encounter", 5);
            AddMap(maps, "gym-d1-noforest", "gym-d1-noforest.encounter", 5);
            AddMap(maps, "gym-d1-flat", "gym-d1-flat.encounter", 5);

            // Open arenas with no geometry at all. They exist to check the paper
            // models in docs/NOTE-2026-08-14-*: with every enemy in reach on
            // round 1 the battle should collapse into a pure ordering problem.
            AddMap(maps, "arena-contact", "gym-arena-contact.encounter");
            AddMap(maps, "arena-stagger", "gym-arena-stagger.encounter");
            AddMap(maps, "arena-residue", "gym-arena-residue.encounter");
            AddMap(maps, "arena-backline", "gym-arena-backline.encounter");
            AddMap(maps, "arena-conflict", "gym-arena-conflict.encounter");
            if (maps.Count == 0) return;

            // tpa-order, residue-aware and decapitate all differ from corridor-hold
            // ONLY in which target they hit, so each pair measures one target rule
            // on its own. decapitate is the one that reads the objective, so on a
            // rout map it is corridor-hold exactly and the two rows must match.
            //
            // Built through StrategyCatalog so this list and the replay window
            // cannot disagree about what a name means. The SET is unchanged —
            // these six, in this order — because adding the other three would
            // make every published cell a different batch.
            List<IPlayerStrategy> strategies = Strategies(
                "corridor-hold", "tpa-order", "residue-aware",
                "decapitate", "control-hold", "charge");
            if (strategies == null) return;

            SimulationRunner runner;
            try
            {
                runner = new SimulationRunner(terrain, units, aiProfiles);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Ediki] Metrics aborted — data failed to load: " + ex.Message);
                return;
            }

            BatchOutput output;
            try
            {
                output = MetricsBatch.Run(runner, maps, strategies, runsPerCell, baseSeed, noisePercent, maxRounds);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Ediki] Metrics run failed: " + ex);
                return;
            }

            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);
            Directory.CreateDirectory(folder);

            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string summaryPath = Path.Combine(folder, "metrics-" + stamp + ".txt");
            string csvPath = Path.Combine(folder, "metrics-" + stamp + ".csv");

            // Fixed name, not stamped: this one is meant to be re-read and diffed
            // after a change, and a new filename every run makes that manual.
            string anomalyPath = Path.Combine(folder, "anomalies.json");

            File.WriteAllText(summaryPath, output.Summary);
            File.WriteAllText(csvPath, output.RawCsv);
            File.WriteAllText(anomalyPath, AnomalyReport.ToJson(output.Anomalies));

            Debug.Log("[Ediki] Metrics written:\n" + summaryPath + "\n" + csvPath
                      + "\n" + anomalyPath + "  (" + output.Anomalies.Count + " flagged)"
                      + "\n\n" + output.Summary);
        }

        /// <summary>
        /// Resolves names through StrategyCatalog, refusing the whole batch if one
        /// is unknown. A silently dropped strategy would produce a report with a
        /// missing row that looks exactly like a strategy that measured nothing.
        /// </summary>
        private static List<IPlayerStrategy> Strategies(params string[] names)
        {
            List<IPlayerStrategy> resolved = new List<IPlayerStrategy>();
            for (int i = 0; i < names.Length; i++)
            {
                IPlayerStrategy strategy = StrategyCatalog.Create(names[i]);
                if (strategy == null)
                {
                    Debug.LogError("[Ediki] Unknown strategy \"" + names[i] + "\". Known: "
                                   + StrategyCatalog.NameList());
                    return null;
                }
                resolved.Add(strategy);
            }
            return resolved;
        }

        private static void AddMap(List<MapEntry> into, string name, string resource, int routeProbeRow = -1)
        {
            string text = Read(resource);
            if (text != null) into.Add(new MapEntry(name, text, routeProbeRow));
        }

        private static string Read(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>("Data/" + name);
            if (asset == null)
            {
                Debug.LogError("[Ediki] Missing Assets/_Project/Resources/Data/" + name + ".txt");
                return null;
            }
            return asset.text;
        }
    }
}
