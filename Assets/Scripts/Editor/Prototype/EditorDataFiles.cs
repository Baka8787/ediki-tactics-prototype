using System;
using System.Collections.Generic;
using System.IO;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Reading and writing the shipped data files.
    ///
    /// Everything the editor loads comes from Assets/_Project/Resources/Data —
    /// the same folder PrototypeBootstrap, SimulationMenu and ReplayWindow read.
    /// There is no editor-only copy of anything: if the editor can see a unit,
    /// the game can spawn it, and if it cannot, neither can the game.
    /// </summary>
    public static class EditorDataFiles
    {
        public const string DataFolder = "Assets/_Project/Resources/Data";
        public const string EncounterSuffix = ".encounter.txt";

        public const string TerrainFile = "terrain.txt";
        public const string UnitsFile = "units.txt";
        public const string AiFile = "ai-profiles.txt";

        /// <summary>Editor-only metadata. Optional — the editor degrades to "offer everything".</summary>
        public const string RosterFile = "editor-roster.txt";

        public sealed class Catalogs
        {
            public TerrainCatalog Terrain;
            public UnitCatalog Units;
            public AiProfileCatalog AiProfiles;
            public EditorRoster Roster;

            /// <summary>Ids in declaration order — catalogs are lookups and do not enumerate.</summary>
            public List<string> UnitIds = new List<string>();
            public List<string> AiIds = new List<string>();

            public string Error;
            public bool Ok => Error == null;
        }

        public static Catalogs LoadCatalogs()
        {
            Catalogs c = new Catalogs();
            try
            {
                string terrainText = ReadRequired(TerrainFile);
                string unitsText = ReadRequired(UnitsFile);
                string aiText = ReadRequired(AiFile);

                c.Terrain = TerrainLoader.Parse(terrainText);
                c.Units = UnitLoader.Parse(unitsText);
                c.AiProfiles = AiProfileLoader.Parse(aiText);
                c.UnitIds = IdsOf(unitsText, "unit");
                c.AiIds = IdsOf(aiText, "aiprofile");

                // Optional: a project without one still opens, it just offers the
                // whole unit list instead of a curated roster.
                string rosterText = ReadOrNull(AssetPath(RosterFile));
                c.Roster = EditorRoster.Parse(rosterText, c.Units);
            }
            catch (Exception ex)
            {
                c.Error = ex.Message;
            }
            return c;
        }

        private static List<string> IdsOf(string text, string keyword)
        {
            List<string> ids = new List<string>();
            foreach (DataLine line in DataLine.ParseAll(text))
            {
                if (!string.Equals(line.Keyword, keyword, StringComparison.OrdinalIgnoreCase)) continue;
                if (line.Has("id")) ids.Add(line.GetString("id"));
            }
            return ids;
        }

        // ------------------------------------------------------------------ io

        public static string AssetPath(string fileName) => DataFolder + "/" + fileName;

        public static string AbsolutePath(string assetPath)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static string ReadRequired(string fileName)
        {
            string path = AbsolutePath(AssetPath(fileName));
            if (!File.Exists(path)) throw new DataFormatException("找不到資料檔：" + AssetPath(fileName));
            return File.ReadAllText(path);
        }

        public static string ReadOrNull(string assetPath)
        {
            string path = AbsolutePath(assetPath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        /// <summary>
        /// Writes and re-imports, so Resources.Load sees the new text without a
        /// manual refresh. Returns null on success or the reason it failed —
        /// callers show it rather than throwing into OnGUI.
        /// </summary>
        public static string Write(string assetPath, string text)
        {
            try
            {
                string absolute = AbsolutePath(assetPath);
                string folder = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // UTF-8 with no BOM: the shipped data files have none, and a BOM
                // would land in the first keyword and break the parser.
                File.WriteAllText(absolute, text, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Encounter asset paths under the data folder, sorted.</summary>
        public static List<string> ListEncounters()
        {
            List<string> found = new List<string>();
            string absolute = AbsolutePath(DataFolder);
            if (!Directory.Exists(absolute)) return found;

            string[] files = Directory.GetFiles(absolute, "*" + EncounterSuffix);
            for (int i = 0; i < files.Length; i++)
                found.Add(DataFolder + "/" + Path.GetFileName(files[i]));

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        /// <summary>"gym-lanes.encounter.txt" -> "gym-lanes.encounter", which is what Resources.Load wants.</summary>
        public static string ResourceNameOf(string assetPath)
        {
            string file = Path.GetFileName(assetPath);
            return file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - 4) : file;
        }
    }
}
