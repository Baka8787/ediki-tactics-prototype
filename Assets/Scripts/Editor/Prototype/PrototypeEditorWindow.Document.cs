using System.Collections.Generic;
using System.IO;
using Ediki.Core;
using Ediki.Core.Data;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    public sealed partial class PrototypeEditorWindow
    {
        // ------------------------------------------------------------------ new

        private void PromptNew()
        {
            if (_dirty && !EditorUtility.DisplayDialog("新建關卡",
                    "目前的關卡還沒儲存，要放棄嗎？", "放棄並新建", "取消"))
                return;

            NewDocument(10, 8);
            SetStatus("已建立 10 x 8 的新關卡。用左邊的工具開始畫。", MessageType.Info);
        }

        private void NewDocument(int width, int height)
        {
            int floor = EncounterDocumentIO.DefaultTerrainIndex(_data.Terrain);
            _doc = new EncounterDocument(width, height, floor)
            {
                Id = "new-encounter",
                DisplayName = "New-Encounter",
                Notes = "Prototype Editor"
            };

            AfterDocumentSwap();
            _dirty = false;
        }

        private void AfterDocumentSwap()
        {
            _history.Clear();
            _selectedSpawn = -1;
            _selectedCell = null;
            _hover = null;
            _sizeInitialised = false;
            _framed = false;
            _camera.TacticalView(_doc.Width, _doc.Height);
            Revalidate();
            Repaint();
        }

        // ----------------------------------------------------------------- open

        private void ShowOpenMenu()
        {
            GenericMenu menu = new GenericMenu();
            List<string> paths = EditorDataFiles.ListEncounters();

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string label = Path.GetFileName(path).Replace(EditorDataFiles.EncounterSuffix, "");
                menu.AddItem(new GUIContent(label), false, () => Open(path));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("從檔案開啟…"), false, () =>
            {
                string chosen = EditorUtility.OpenFilePanel("開啟關卡",
                    EditorDataFiles.AbsolutePath(EditorDataFiles.DataFolder), "txt");
                if (!string.IsNullOrEmpty(chosen)) OpenAbsolute(chosen);
            });

            menu.ShowAsContext();
        }

        private void Open(string assetPath)
        {
            if (_dirty && !EditorUtility.DisplayDialog("開啟關卡",
                    "目前的關卡還沒儲存，要放棄嗎？", "放棄並開啟", "取消"))
                return;

            string text = EditorDataFiles.ReadOrNull(assetPath);
            if (text == null)
            {
                SetStatus("讀不到檔案：" + assetPath, MessageType.Error);
                return;
            }

            LoadText(text, assetPath);
        }

        private void OpenAbsolute(string absolutePath)
        {
            if (_dirty && !EditorUtility.DisplayDialog("開啟關卡",
                    "目前的關卡還沒儲存，要放棄嗎？", "放棄並開啟", "取消"))
                return;

            try { LoadText(File.ReadAllText(absolutePath), ToAssetPath(absolutePath)); }
            catch (System.Exception ex) { SetStatus("讀取失敗：" + ex.Message, MessageType.Error); }
        }

        private void LoadText(string text, string assetPath)
        {
            List<string> warnings = new List<string>();
            _doc = EncounterDocumentIO.FromText(text, _data.Terrain, warnings);
            _doc.SourcePath = assetPath;

            AfterDocumentSwap();
            _dirty = false;

            if (warnings.Count == 0)
                SetStatus("已開啟 " + Path.GetFileName(assetPath ?? "檔案") + "。", MessageType.Info);
            else
                SetStatus("開啟時有問題，已盡量修復：\n" + string.Join("\n", warnings.ToArray()), MessageType.Warning);
        }

        private static string ToAssetPath(string absolutePath)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string full = Path.GetFullPath(absolutePath);
            if (!full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(root.Length + 1).Replace('\\', '/');
        }

        // ----------------------------------------------------------------- save

        private void Save(bool saveAs)
        {
            string path = _doc.SourcePath;

            if (saveAs || string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject(
                    "儲存關卡", _doc.Id + ".encounter", "txt",
                    "存到 " + EditorDataFiles.DataFolder + " 底下，試玩才找得到它。",
                    EditorDataFiles.DataFolder);
                if (string.IsNullOrEmpty(path)) return;
            }

            // Validate everything in memory first. A refused save must leave the
            // project exactly as it found it.
            PreparedEncounter prepared;
            if (!TryPrepare("還不能儲存", out prepared)) return;

            if (!Commit(prepared, path, "儲存")) return;

            _doc.SourcePath = path;
            _dirty = false;

            bool inResources = path.Replace('\\', '/').StartsWith(EditorDataFiles.DataFolder);
            SetStatus((inResources
                ? "已儲存：" + path
                : "已儲存：" + path + "\n注意：這個位置不在 " + EditorDataFiles.DataFolder
                  + " 底下，遊戲不會自動找到它。") + PlanNotes(prepared),
                inResources ? MessageType.Info : MessageType.Warning);
        }

        // ----------------------------------------------------------------- play

        private void Play()
        {
            // Nothing is written until this passes. A playtest that gets refused
            // used to have already appended rows to units.txt and rewritten the
            // ids in the open document — a refusal with permanent side effects.
            PreparedEncounter prepared;
            if (!TryPrepare("還不能試玩", out prepared)) return;

            string assetPath = EditorDataFiles.AssetPath(EncounterDocumentIO.PlaytestEncounterName + ".txt");
            if (!Commit(prepared, assetPath, "試玩")) return;

            PlaytestSession.Arm(EncounterDocumentIO.PlaytestEncounterName);

            // Hand focus to the Game view BEFORE entering play mode.
            //
            // The game reads the mouse and keyboard through the Input System,
            // which only delivers to the focused editor window. With this window
            // focused — which it is, you just clicked its button — the battle
            // gets no input at all and looks frozen. This is why "試玩中沒辦法像
            // Play mode 一樣操作": the game was fine, the keystrokes were going
            // to the editor.
            FocusGameView();

            SetStatus("進入試玩。按上面的「停止」或 Unity 的 Play 鍵回到編輯。", MessageType.Info);
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Brings the Game view up and focuses it.
        ///
        /// Uses the menu item rather than reflecting on the internal GameView
        /// type: the menu path is public API and stable, the type is neither.
        /// Failing to find it is not worth aborting a playtest over, so a miss is
        /// reported and play continues.
        /// </summary>
        private void FocusGameView()
        {
            if (EditorApplication.ExecuteMenuItem("Window/General/Game")) return;

            SetStatus("進入試玩。找不到 Game 視窗 —— 請手動點一下 Game 視窗，遊戲才收得到鍵盤與滑鼠。",
                      MessageType.Warning);
        }

        private void ReportGateFailure(EncounterValidation.GateResult gate, string what)
        {
            if (gate.LoaderError != null)
            {
                SetStatus(what + "：遊戲的關卡讀取器拒絕了這份資料。\n" + gate.LoaderError, MessageType.Error);
                return;
            }
            SetStatus(what + "，下面有 " + gate.ErrorCount + " 個錯誤要先修掉。", MessageType.Error);
        }

        private EncounterValidation.GateResult RunGate()
        {
            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(_doc, _data.Terrain, _data.Units, _data.AiProfiles, _data.Roster);
            ApplyIssues(gate.Issues);
            return gate;
        }

        // ----------------------------------------------------------- undo/redo

        private void Undo()
        {
            EncounterDocument previous = _history.Undo(_doc);
            if (previous == null) return;

            _doc = previous;
            ClampSelection();
            _sizeInitialised = false;
            MarkDirty();
        }

        private void Redo()
        {
            EncounterDocument next = _history.Redo(_doc);
            if (next == null) return;

            _doc = next;
            ClampSelection();
            _sizeInitialised = false;
            MarkDirty();
        }

        private void ClampSelection()
        {
            if (_selectedSpawn >= _doc.Spawns.Count) _selectedSpawn = -1;
        }

        // -------------------------------------------------------- housekeeping

        private void MarkDirty()
        {
            _dirty = true;
            _relayout = true;   // spawn count and selection can both have moved
            Revalidate();
            Repaint();
        }

        private void Revalidate()
        {
            if (_doc == null || _data == null || !_data.Ok) return;
            ApplyIssues(EncounterValidation.Check(_doc, _data.Terrain, _data.Units, _data.AiProfiles, _data.Roster));
        }

        private void ApplyIssues(List<EncounterIssue> issues)
        {
            _issues = issues ?? new List<EncounterIssue>();
            _problemCells.Clear();

            for (int i = 0; i < _issues.Count; i++)
            {
                EncounterIssue issue = _issues[i];
                if (issue.Level != IssueLevel.Error) continue;

                if (issue.Cell.HasValue) _problemCells.Add(issue.Cell.Value);
                else if (issue.SpawnIndex >= 0 && issue.SpawnIndex < _doc.Spawns.Count)
                    _problemCells.Add(_doc.Spawns[issue.SpawnIndex].Position);
            }
        }

        // -------------------------------------------------------------- naming

        private string DisplayNameOf(SpawnEntry spawn)
        {
            if (spawn.HasPendingStats && spawn.PendingStats != null) return spawn.PendingStats.DisplayName + " *";
            return DisplayNameOfId(spawn.UnitId);
        }

        private string DisplayNameOfId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return "（未選）";
            UnitDef def;
            if (_data != null && _data.Ok && _data.Units.TryGet(unitId, out def))
                return _plannerMode ? def.DisplayName : unitId;
            return unitId + " ?";
        }
    }
}
