using System.Collections.Generic;
using System.IO;
using Ediki.Sim;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor
{
    /// <summary>
    /// Replays one battle from anomalies.json and prints it round by round.
    ///
    /// This project has no CLI and no Main — the batch runs from the Ediki menu —
    /// so this window IS the entry point, and it takes the same
    /// "--replay &lt;encounter&gt; &lt;seed&gt; &lt;strategy&gt;" line that the
    /// anomaly file's fields spell out. Parsing and running both live in
    /// Ediki.Sim; nothing here knows anything about battles.
    ///
    /// The replay goes through SimulationRunner.RunOne with the batch's own
    /// config, so it reproduces the flagged battle rather than resembling it.
    /// </summary>
    public sealed class ReplayWindow : EditorWindow
    {
        private const string OutputFolder = "SimResults";

        // Batch defaults. A replay run with different sampling noise or a
        // different cap is a different battle, so these are shown rather than
        // hidden — changing one and getting a different result is not a bug.
        private const int BatchNoisePercent = 15;
        private const int BatchRoundCap = 60;

        private string _line = ReplayRequest.Flag + " gym-opening.encounter 1 corridor-hold";
        private Vector2 _scroll;
        private string _output = "";
        private string _error = "";

        [MenuItem("Ediki/Replay Battle…")]
        public static void Open()
        {
            ReplayWindow window = GetWindow<ReplayWindow>(true, "Ediki — Replay Battle");
            window.minSize = new Vector2(720, 420);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Usage: " + ReplayRequest.Usage, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Copy encounter / seed / strategy straight out of SimResults/anomalies.json.");
            EditorGUILayout.Space();

            _line = EditorGUILayout.TextField("Replay", _line);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run replay", GUILayout.Height(26))) RunReplay();

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            if (!string.IsNullOrEmpty(_output))
            {
                EditorGUILayout.Space();
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_output, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void RunReplay()
        {
            _output = "";
            _error = "";

            string terrain = Read("terrain");
            string units = Read("units");
            string aiProfiles = Read("ai-profiles");
            if (terrain == null || units == null || aiProfiles == null)
            {
                _error = "Missing data files under Assets/_Project/Resources/Data (terrain / units / ai-profiles).";
                return;
            }

            List<string> encounters = AvailableEncounters();

            ReplayRequest request;
            string parseError;
            if (!ReplayRequest.TryParseLine(_line, encounters, out request, out parseError))
            {
                _error = parseError;
                return;
            }

            string encounterText = Read(request.Encounter);
            if (encounterText == null)
            {
                _error = "Encounter \"" + request.Encounter + "\" is listed but could not be read.";
                return;
            }

            // Everything below can throw on malformed data. A stack trace in the
            // console is not an error message, so each stage says what it was
            // doing when it failed.
            SimulationRunner runner;
            try
            {
                runner = new SimulationRunner(terrain, units, aiProfiles);
            }
            catch (System.Exception ex)
            {
                _error = "Data failed to load: " + ex.Message;
                return;
            }

            ReplayRunner.Result replay;
            try
            {
                SimulationConfig config = new SimulationConfig
                {
                    MapName = request.Encounter,
                    EncounterText = encounterText,
                    Strategy = StrategyCatalog.Create(request.Strategy),
                    Runs = 1,
                    BaseSeed = request.Seed,
                    NoisePercent = BatchNoisePercent,
                    MaxRounds = BatchRoundCap
                };

                replay = ReplayRunner.Run(runner, config, request.Seed);
            }
            catch (System.Exception ex)
            {
                _error = "Replay failed while simulating " + request.Encounter + ": " + ex.Message;
                return;
            }

            _output = replay.Transcript;

            try
            {
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "replay-" + Safe(request.Encounter)
                                                   + "-" + request.Strategy + "-" + request.Seed + ".txt");
                File.WriteAllText(path, replay.Transcript);
                Debug.Log("[Ediki] Replay written: " + path + "\n\n" + replay.Transcript);
            }
            catch (System.Exception ex)
            {
                // The transcript is already on screen; failing to file it is not
                // a reason to lose it.
                _error = "Replay ran, but the transcript could not be saved: " + ex.Message;
            }
        }

        /// <summary>Encounter resources, so a typo gets a list instead of a null.</summary>
        private static List<string> AvailableEncounters()
        {
            List<string> names = new List<string>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>("Data");
            for (int i = 0; i < assets.Length; i++)
                if (assets[i].name.EndsWith(".encounter")) names.Add(assets[i].name);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        private static string Safe(string name) => name.Replace('/', '_').Replace('\\', '_');

        private static string Read(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>("Data/" + name);
            return asset == null ? null : asset.text;
        }
    }
}
