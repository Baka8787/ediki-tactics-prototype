using System;
using System.Collections.Generic;
using System.IO;
using Ediki.Core;
using Ediki.Unity;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// 《穢土紀》Prototype Editor — the encounter authoring tool.
    ///
    /// Scope, deliberately: map, terrain, units, arrivals, objective, stats,
    /// save/load, play. It is not a level editor, not a scene tool, and not an
    /// inspector replacement. Everything it produces is one .encounter.txt in
    /// the shipped format plus, when stats were edited, extra rows appended to
    /// units.txt — both of which the Unity playtest and Ediki.Sim already read.
    ///
    /// It never touches BattleState, never issues a Command, and knows no rules.
    /// The furthest it reaches into the runtime is a filename in EditorPrefs.
    /// </summary>
    public sealed partial class PrototypeEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 24f;
        private const float StatusHeight = 118f;

        private const float MinPanelWidth = 150f;
        private const float MaxPanelWidth = 640f;
        private const float MinMapWidth = 220f;
        private const float SplitterWidth = 5f;

        private const string LeftWidthKey = "Ediki.PrototypeEditor.LeftWidth";
        private const string RightWidthKey = "Ediki.PrototypeEditor.RightWidth";

        // Draggable, and remembered between sessions. The inspector holds a lot
        // of numeric fields and Unity gives the LABEL a fixed share of the row,
        // so a narrow panel does not shrink the labels — it squeezes the input
        // boxes to nothing. Being able to widen it is what makes them typable.
        private float _leftWidth = 190f;
        private float _rightWidth = 360f;
        private int _draggingSplitter;   // 0 none, 1 left, 2 right

        private enum Tool { Terrain, Unit, Objective, EraseUnit, ResetTerrain }

        // ------------------------------------------------------------- document

        private EditorDataFiles.Catalogs _data;
        private EncounterDocument _doc;
        private readonly DocumentHistory _history = new DocumentHistory();
        private bool _dirty;

        // ----------------------------------------------------------------- view

        private readonly MapViewCamera _camera = new MapViewCamera();
        private readonly SolidRenderer _solid = new SolidRenderer();
        private readonly MapViewRenderer _scene = new MapViewRenderer();
        private Rect _mapRect;

        // ---------------------------------------------------------- interaction

        private Tool _tool = Tool.Terrain;
        private int _brushTerrain;
        private Faction _brushFaction = Faction.Player;
        private string _brushUnitId;
        private string _brushAi;
        private int _brushArrivalTurn;

        private Coord? _hover;
        private Coord? _selectedCell;
        private int _selectedSpawn = -1;

        private bool _painting;
        private int _draggingSpawn = -1;
        private bool _panning;
        private bool _orbiting;
        private Vector2 _lastMouse;

        // ------------------------------------------------------------------- ui

        private bool _plannerMode = true;
        private bool _showStats;
        private bool _showNames = true;
        private Vector2 _inspectorScroll;
        private Vector2 _issueScroll;
        private List<EncounterIssue> _issues = new List<EncounterIssue>();
        private readonly HashSet<Coord> _problemCells = new HashSet<Coord>();
        private string _status = "";
        private MessageType _statusType = MessageType.Info;

        private GUIStyle _labelTag;
        private GUIStyle _labelName;

        [MenuItem("Ediki/Prototype Editor  (企劃用關卡編輯器)")]
        public static void Open()
        {
            PrototypeEditorWindow window = GetWindow<PrototypeEditorWindow>("Ediki 關卡編輯器");
            window.minSize = new Vector2(1080f, 640f);
            window.Show();
        }

        // ------------------------------------------------- surviving a reload
        //
        // Entering play mode reloads the domain, and so does every script
        // recompile. Unity rebuilds the window and keeps only [SerializeField]
        // members — an EncounterDocument is a plain C# object, so it was being
        // dropped and OnEnable was building a fresh empty 10x8 in its place.
        //
        // That is what "press 試玩 and everything comes back blank with no units"
        // was: not the game failing to load the map, but the EDITOR forgetting
        // the map the moment play mode started, and silently discarding unsaved
        // work with it.
        //
        // The document is stored as its own canonical text, which is the one
        // representation guaranteed to round-trip (there is a test over every
        // shipped encounter that says so). Pending stat edits ride along as JSON
        // because they are the part that has not been written to units.txt yet
        // and would otherwise be the only thing a recompile could still destroy.

        [SerializeField] private string _restoreText;
        [SerializeField] private string _restorePath;
        [SerializeField] private bool _restoreDirty;
        [SerializeField] private string _restorePending;
        [SerializeField] private int _restoreSelected = -1;
        [SerializeField] private CameraState _restoreCamera;
        [SerializeField] private bool _hasRestore;

        [Serializable]
        private struct CameraState
        {
            public Vector3 Pivot;
            public float Yaw, Pitch, Distance, OrthoZoom;
            public bool Perspective;
        }

        private void OnEnable()
        {
            _leftWidth = EditorPrefs.GetFloat(LeftWidthKey, 190f);
            _rightWidth = EditorPrefs.GetFloat(RightWidthKey, 360f);

            ReloadCatalogs();
            wantsMouseMove = true;

            if (_data == null || !_data.Ok) return;

            if (_hasRestore && !string.IsNullOrEmpty(_restoreText)) RestoreDocument();
            else if (_doc == null) NewDocument(10, 8);
        }

        private void OnDisable()
        {
            if (_doc == null || _data == null || !_data.Ok) { _hasRestore = false; return; }

            try
            {
                _restoreText = EncounterDocumentIO.ToText(_doc, _data.Terrain);
            }
            catch (Exception)
            {
                // A half-built document that cannot be written is not worth
                // losing the session over, but it is also not worth restoring
                // something wrong — start clean next time and say nothing false.
                _hasRestore = false;
                return;
            }

            _restorePath = _doc.SourcePath;
            _restoreDirty = _dirty;
            _restoreSelected = _selectedSpawn;
            _restorePending = PendingEditsStore.Save(_doc);
            _restoreCamera = new CameraState
            {
                Pivot = _camera.Pivot,
                Yaw = _camera.Yaw,
                Pitch = _camera.Pitch,
                Distance = _camera.Distance,
                OrthoZoom = _camera.OrthoZoom,
                Perspective = _camera.Perspective
            };
            _hasRestore = true;
        }

        private void RestoreDocument()
        {
            List<string> warnings = new List<string>();
            _doc = EncounterDocumentIO.FromText(_restoreText, _data.Terrain, warnings);
            _doc.SourcePath = _restorePath;
            PendingEditsStore.Load(_doc, _restorePending);

            _history.Clear();
            _selectedSpawn = _restoreSelected < _doc.Spawns.Count ? _restoreSelected : -1;
            _selectedCell = null;
            _hover = null;
            _sizeInitialised = false;

            _camera.Pivot = _restoreCamera.Pivot;
            _camera.Yaw = _restoreCamera.Yaw;
            _camera.Pitch = _restoreCamera.Pitch;
            _camera.Distance = _restoreCamera.Distance;
            _camera.OrthoZoom = _restoreCamera.OrthoZoom;
            _camera.Perspective = _restoreCamera.Perspective;

            // The camera was restored, so do not re-frame on the first paint.
            _framed = true;

            _dirty = _restoreDirty;
            Revalidate();
            Repaint();
        }

        private void ReloadCatalogs()
        {
            _data = EditorDataFiles.LoadCatalogs();
            if (!_data.Ok)
            {
                SetStatus("資料檔讀取失敗：" + _data.Error, MessageType.Error);
                return;
            }

            _brushTerrain = Mathf.Clamp(_brushTerrain, 0, _data.Terrain.Count - 1);
            if (string.IsNullOrEmpty(_brushUnitId) && _data.UnitIds.Count > 0) _brushUnitId = _data.UnitIds[0];
            if (string.IsNullOrEmpty(_brushAi) && _data.AiIds.Count > 0) _brushAi = _data.AiIds[0];
        }

        // --------------------------------------------------------------- OnGUI

        private void OnGUI()
        {
            if (_data == null || !_data.Ok)
            {
                EditorGUILayout.HelpBox(
                    "無法讀取 " + EditorDataFiles.DataFolder + " 底下的資料檔。\n" +
                    (_data != null ? _data.Error : ""), MessageType.Error);
                if (GUILayout.Button("重新載入")) ReloadCatalogs();
                return;
            }

            EnsureStyles();
            if (_doc == null) NewDocument(10, 8);

            float bodyTop = ToolbarHeight;
            float bodyHeight = position.height - ToolbarHeight - StatusHeight;
            if (bodyHeight < 80f) bodyHeight = 80f;

            Rect leftRect, rightRect;
            HandleSplitters(bodyTop, bodyHeight, out leftRect, out rightRect);
            Rect statusRect = new Rect(0f, bodyTop + bodyHeight, position.width, StatusHeight);

            // Input BEFORE any layout is emitted, and it emits none of its own.
            //
            // IMGUI caches the control list from the Layout event and indexes into
            // it on every later event. Selecting a unit changes how many controls
            // the inspector draws, so doing it in the middle of a pass makes this
            // pass disagree with the layout it is reading — the classic
            // "Getting control N's position in a group with only M controls".
            // The sanctioned fix is to abandon the pass and let a fresh Layout run.
            _camera.SetViewRect(_mapRect);

            // The first document is built in OnEnable, before the window has a
            // size, so the framing it computed then was against a zero rect.
            if (!_framed && _mapRect.width > 1f)
            {
                _framed = true;
                _camera.TacticalView(_doc.Width, _doc.Height);
            }

            HandleMapInput(_mapRect);
            HandleShortcuts();

            if (_relayout && Event.current.type != EventType.Layout)
            {
                _relayout = false;
                Repaint();
                GUIUtility.ExitGUI();
            }
            _relayout = false;

            DrawToolbar();
            DrawToolPanel(leftRect);
            DrawMapView(_mapRect);
            DrawInspector(rightRect);
            DrawStatusBar(statusRect);
            DrawSplitterHandles(bodyTop, bodyHeight);
        }

        /// <summary>
        /// Lays out the three columns and lets the two seams be dragged.
        ///
        /// Runs before anything is drawn and changes only RECT SIZES, never the
        /// number of controls, so it needs none of the layout-restart machinery
        /// the tool and selection changes do.
        /// </summary>
        private void HandleSplitters(float top, float height, out Rect leftRect, out Rect rightRect)
        {
            Event e = Event.current;

            float available = position.width - MinMapWidth - SplitterWidth * 2f;
            float cap = Mathf.Max(MinPanelWidth, Mathf.Min(MaxPanelWidth, available - MinPanelWidth));

            _leftWidth = Mathf.Clamp(_leftWidth, MinPanelWidth, cap);
            _rightWidth = Mathf.Clamp(_rightWidth, MinPanelWidth, cap);

            if (e.type == EventType.MouseDrag && _draggingSplitter != 0)
            {
                if (_draggingSplitter == 1) _leftWidth = e.mousePosition.x - SplitterWidth * 0.5f;
                else _rightWidth = position.width - e.mousePosition.x - SplitterWidth * 0.5f;

                _leftWidth = Mathf.Clamp(_leftWidth, MinPanelWidth, cap);
                _rightWidth = Mathf.Clamp(_rightWidth, MinPanelWidth, cap);

                EditorPrefs.SetFloat(LeftWidthKey, _leftWidth);
                EditorPrefs.SetFloat(RightWidthKey, _rightWidth);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && _draggingSplitter != 0)
            {
                _draggingSplitter = 0;
                e.Use();
            }

            leftRect = new Rect(0f, top, _leftWidth, height);
            float mapX = _leftWidth + SplitterWidth;
            float mapWidth = Mathf.Max(MinMapWidth, position.width - _leftWidth - _rightWidth - SplitterWidth * 2f);
            _mapRect = new Rect(mapX, top, mapWidth, height);
            rightRect = new Rect(_mapRect.xMax + SplitterWidth, top, _rightWidth, height);

            if (e.type == EventType.MouseDown && e.button == 0 && _draggingSplitter == 0)
            {
                if (SplitterRect(leftRect.xMax, top, height).Contains(e.mousePosition))
                {
                    _draggingSplitter = 1;
                    e.Use();
                }
                else if (SplitterRect(_mapRect.xMax, top, height).Contains(e.mousePosition))
                {
                    _draggingSplitter = 2;
                    e.Use();
                }
            }
        }

        private static Rect SplitterRect(float x, float top, float height)
        {
            return new Rect(x, top, SplitterWidth, height);
        }

        private void DrawSplitterHandles(float top, float height)
        {
            Rect left = SplitterRect(_leftWidth, top, height);
            Rect right = SplitterRect(_mapRect.xMax, top, height);

            EditorGUIUtility.AddCursorRect(left, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(right, MouseCursor.ResizeHorizontal);

            if (Event.current.type != EventType.Repaint) return;

            Color grip = new Color(0.35f, 0.36f, 0.40f);
            EditorGUI.DrawRect(new Rect(left.center.x - 1f, top + height * 0.45f, 2f, height * 0.1f), grip);
            EditorGUI.DrawRect(new Rect(right.center.x - 1f, top + height * 0.45f, 2f, height * 0.1f), grip);
        }

        /// <summary>Set by anything that changes how many controls the panels will draw.</summary>
        private bool _relayout;

        /// <summary>
        /// Abandons this GUI pass so a fresh Layout runs before anything is drawn.
        ///
        /// Called immediately after a change that alters how many controls the
        /// rest of the pass would emit — switching tool, selecting a unit,
        /// flipping planner mode. Unity catches the ExitGUIException and unwinds
        /// the layout groups, which is why this is safe inside a BeginArea.
        /// </summary>
        private static void RestartLayout()
        {
            if (Event.current.type == EventType.Layout) return;
            GUIUtility.ExitGUI();
        }

        private void EnsureStyles()
        {
            if (_labelTag != null) return;

            _labelTag = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            _labelName = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
        }

        // ------------------------------------------------------------- toolbar

        /// <summary>
        /// PLAY mode makes the editor read-only.
        ///
        /// The window keeps drawing and keeps receiving input while the game
        /// runs, so every click on the map view was still painting terrain and
        /// dropping units behind the playtest — and the damage only became
        /// visible after Stop, by which point nobody could tell which edits were
        /// deliberate. EDIT changes the encounter; PLAY only looks at it.
        ///
        /// The camera is deliberately NOT locked: orbiting the board while the
        /// battle runs is the whole reason to keep this window open during a
        /// playtest, and moving the camera cannot change what gets saved.
        /// </summary>
        private static bool EditingLocked => EditorApplication.isPlaying;

        private void DrawToolbar()
        {
            Rect rect = new Rect(0f, 0f, position.width, ToolbarHeight);
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            EditorGUILayout.BeginHorizontal();

            bool playing = EditingLocked;

            using (new EditorGUI.DisabledScope(playing))
            {
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(46f))) PromptNew();
                if (GUILayout.Button("開啟 ▾", EditorStyles.toolbarButton, GUILayout.Width(56f))) ShowOpenMenu();
                if (GUILayout.Button("儲存", EditorStyles.toolbarButton, GUILayout.Width(46f))) Save(false);
                if (GUILayout.Button("另存", EditorStyles.toolbarButton, GUILayout.Width(46f))) Save(true);
            }

            GUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(playing))
                if (GUILayout.Button("▶ 試玩", EditorStyles.toolbarButton, GUILayout.Width(58f))) Play();
            using (new EditorGUI.DisabledScope(!playing))
                if (GUILayout.Button("■ 停止", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                    EditorApplication.isPlaying = false;

            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            if (playing)
            {
                GUI.backgroundColor = new Color(1f, 0.62f, 0.2f);
                GUI.contentColor = Color.white;
            }
            // Constant width in both states: the mode can flip between the Layout
            // event and the Repaint that follows it, and a control that changes
            // size across that boundary is exactly what desynchronises IMGUI.
            GUILayout.Label(playing ? "PLAY — 編輯已鎖定" : "EDIT",
                            EditorStyles.toolbarButton, GUILayout.Width(128f));
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;

            GUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(playing || !_history.CanUndo))
                if (GUILayout.Button("↶ 復原", EditorStyles.toolbarButton, GUILayout.Width(54f))) Undo();
            using (new EditorGUI.DisabledScope(playing || !_history.CanRedo))
                if (GUILayout.Button("↷ 重做", EditorStyles.toolbarButton, GUILayout.Width(54f))) Redo();

            GUILayout.Space(10f);

            if (GUILayout.Button("俯視", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.TopView(_doc.Width, _doc.Height);
            if (GUILayout.Button("戰術", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.TacticalView(_doc.Width, _doc.Height);
            if (GUILayout.Button("正面", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.AxisView(_doc.Width, _doc.Height, false);
            if (GUILayout.Button("側面", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.AxisView(_doc.Width, _doc.Height, true);
            if (GUILayout.Button("置中", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.FocusMap(_doc.Width, _doc.Height);

            if (GUILayout.Button(_camera.Perspective ? "透視" : "正交",
                                 EditorStyles.toolbarButton, GUILayout.Width(42f)))
                _camera.TogglePerspective();

            GUILayout.FlexibleSpace();

            _showNames = GUILayout.Toggle(_showNames, "名稱", EditorStyles.toolbarButton, GUILayout.Width(42f));
            _showStats = GUILayout.Toggle(_showStats, "數值", EditorStyles.toolbarButton, GUILayout.Width(42f));

            bool planner = GUILayout.Toggle(_plannerMode, _plannerMode ? "企劃模式" : "工程模式",
                                            EditorStyles.toolbarButton, GUILayout.Width(70f));
            bool modeChanged = planner != _plannerMode;
            _plannerMode = planner;

            bool reloaded = GUILayout.Button("重載資料", EditorStyles.toolbarButton, GUILayout.Width(64f));

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (reloaded)
            {
                ReloadCatalogs();
                Revalidate();
                SetStatus("已重新讀取 terrain / units / ai-profiles。", MessageType.Info);
            }

            // Both change which fields the panels below emit, so the pass has to
            // start over rather than draw against a stale layout.
            if (modeChanged || reloaded) { Repaint(); RestartLayout(); }

            // Only when it flips: OnGUI runs several times a frame and a new
            // GUIContent each pass is pure garbage.
            if (_dirty != _titleShowsDirty)
            {
                _titleShowsDirty = _dirty;
                titleContent = new GUIContent(_dirty ? "Ediki 關卡編輯器 *" : "Ediki 關卡編輯器");
            }
        }

        private bool _titleShowsDirty;
        private bool _framed;

        // ----------------------------------------------------------- tool panel

        private void DrawToolPanel(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);

            using (new EditorGUI.DisabledScope(EditingLocked))
                DrawTools();

            GUILayout.FlexibleSpace();
            DrawNavigationHelp();

            GUILayout.EndArea();
        }

        private void DrawTools()
        {
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);

            ToolButton(Tool.Terrain, "地形筆刷", "拖曳滑鼠可以連續塗");
            ToolButton(Tool.Unit, "放置單位", "點空格放置，拖曳單位可以移動");
            ToolButton(Tool.Objective, "任務地點", "點一格設成「抵達」的目標");
            ToolButton(Tool.EraseUnit, "刪除單位", "只刪單位，絕不動地形");
            ToolButton(Tool.ResetTerrain, "重置地形", "只還原地面，絕不刪單位");

            EditorGUILayout.Space(6f);

            switch (_tool)
            {
                case Tool.Terrain: DrawTerrainPalette(); break;
                case Tool.Unit: DrawUnitPalette(); break;
                case Tool.Objective: DrawObjectiveHint(); break;
                case Tool.EraseUnit: DrawEraseUnitHint(); break;
                case Tool.ResetTerrain: DrawResetTerrainHint(); break;
            }

        }

        /// <summary>
        /// Outside the disabled scope on purpose: the camera keeps working
        /// during a playtest, so its help must not read as greyed out.
        /// </summary>
        private void DrawNavigationHelp()
        {
            // Always emitted, only the text changes: isPlaying can already read
            // true while the old window is still being drawn during a mode
            // transition, and a box that appears and disappears across that
            // boundary would desynchronise the cached layout.
            EditorGUILayout.HelpBox(
                EditingLocked
                    ? "試玩中。\n編輯已鎖定，視角操作照常。\n按上面的「停止」回到編輯。"
                    : "編輯中。\n按上面的「▶ 試玩」會先檢查再進遊戲。",
                EditingLocked ? MessageType.Warning : MessageType.None);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("視角操作", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("中鍵拖曳　平移", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("右鍵拖曳　旋轉", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Alt+左鍵　旋轉", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Alt+右鍵　推近拉遠", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("滾輪　　　縮放", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("右鍵+WASD 飛行", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("　　　+QE　升降", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("　　　+Shift 加速", EditorStyles.miniLabel);
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Home　　　看整張圖", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("F　　　　　看選取的東西", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("1 / 3 / 7　正面 / 側面 / 俯視", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("5　　　　　透視 / 正交", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Delete　　刪除選取單位", EditorStyles.miniLabel);
        }

        private void ToolButton(Tool tool, string label, string hint)
        {
            bool on = _tool == tool;
            bool next = GUILayout.Toggle(on, label, EditorStyles.miniButton, GUILayout.Height(22f));

            // Exactly one tool is active, so the hint line keeps the count of
            // controls in this loop constant. The PALETTE below does not, which
            // is why switching restarts the pass.
            if (_tool == tool) EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);

            if (next != on)
            {
                _tool = tool;
                GUI.FocusControl(null);
                Repaint();
                RestartLayout();
            }
        }

        private void DrawTerrainPalette()
        {
            EditorGUILayout.LabelField("地形", EditorStyles.boldLabel);

            for (int i = 0; i < _data.Terrain.Count; i++)
            {
                TerrainDef def = _data.Terrain[i];
                string label = _plannerMode
                    ? PlannerVocabulary.TerrainName(def)
                    : def.Name + "  '" + def.Symbol + "'";

                bool on = _brushTerrain == i;
                if (GUILayout.Toggle(on, label, EditorStyles.miniButton, GUILayout.Height(20f)) != on)
                    _brushTerrain = i;
            }

            EditorGUILayout.Space(4f);
            TerrainDef current = _data.Terrain[Mathf.Clamp(_brushTerrain, 0, _data.Terrain.Count - 1)];
            EditorGUILayout.LabelField(PlannerVocabulary.TerrainEffect(current), EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawUnitPalette()
        {
            EditorGUILayout.LabelField("陣營", EditorStyles.boldLabel);
            int faction = GUILayout.Toolbar(_brushFaction == Faction.Player ? 0 : 1, new[] { "我方", "敵方" });
            _brushFaction = faction == 0 ? Faction.Player : Faction.Enemy;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("角色", EditorStyles.boldLabel);

            _brushUnitId = CharacterAndVariant(_brushUnitId, _brushFaction, true);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("行為", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_brushFaction != Faction.Enemy))
            {
                int ai = Mathf.Max(0, _data.AiIds.IndexOf(_brushAi));
                int pickedAi = EditorGUILayout.Popup(ai, AiMenuLabels());
                if (_brushFaction == Faction.Enemy && pickedAi >= 0 && pickedAi < _data.AiIds.Count)
                    _brushAi = _data.AiIds[pickedAi];
            }

            EditorGUILayout.Space(4f);
            _brushArrivalTurn = Mathf.Max(0, EditorGUILayout.IntField("登場回合", _brushArrivalTurn));
            EditorGUILayout.LabelField(_brushArrivalTurn == 0 ? "0 = 一開始就在場上" : "第 " + _brushArrivalTurn + " 回合登場",
                                       EditorStyles.miniLabel);
        }

        private void DrawObjectiveHint()
        {
            EditorGUILayout.HelpBox(
                _doc.ObjectiveKind == ObjectiveKind.Reach
                    ? "點一格設成要抵達的地點。"
                    : "目前的任務類型不需要地點。\n要用地點的話，把右側的任務類型改成「抵達指定地點」。",
                MessageType.Info);
        }

        private void DrawEraseUnitHint()
        {
            EditorGUILayout.HelpBox("點單位就刪掉它。\n點到空格什麼都不會發生 —— 地形不會被動到。",
                                    MessageType.Info);
        }

        private void DrawResetTerrainHint()
        {
            EditorGUILayout.HelpBox("把格子還原成「"
                + PlannerVocabulary.TerrainName(
                    _data.Terrain[EncounterDocumentIO.DefaultTerrainIndex(_data.Terrain)])
                + "」。可以拖曳。\n上面站著的單位不會被刪掉。", MessageType.Info);
        }

        /// <summary>
        /// Character picker plus an A/B variant switch, filtered to one side.
        ///
        /// This is the shape the roster gives us and the shape the planner asked
        /// for: pick WHO, then pick which build of them. The unit id is derived
        /// from the pair rather than typed, so the enemy list can never contain a
        /// party member and switching 桃太郎 from A to B is one click instead of
        /// hunting through 47 rows.
        ///
        /// Emits a constant number of controls in every branch — a popup, a
        /// toolbar and a note — so switching faction or character never desyncs
        /// the cached IMGUI layout.
        /// </summary>
        private string CharacterAndVariant(string unitId, Faction faction, bool autoCorrectSide)
        {
            EditorRoster roster = _data.Roster;
            List<RosterCharacter> palette = roster.Palette(faction);

            if (palette.Count == 0)
            {
                // No roster (or none for this side): fall back to the flat list,
                // which is what the editor did before the roster existed.
                int flat = Mathf.Max(0, _data.UnitIds.IndexOf(unitId));
                int pickedFlat = EditorGUILayout.Popup("單位", flat, UnitMenuLabels());
                GUILayout.Toolbar(0, new[] { "—" });
                EditorGUILayout.LabelField(" ", "沒有角色名單，顯示全部單位。", EditorStyles.miniLabel);
                return pickedFlat >= 0 && pickedFlat < _data.UnitIds.Count ? _data.UnitIds[pickedFlat] : unitId;
            }

            RosterCharacter current = roster.CharacterOf(unitId);
            bool offSide = current == null || palette.IndexOf(current) < 0;

            if (offSide && autoCorrectSide && palette[0].Variants.Count > 0)
            {
                current = palette[0];
                unitId = current.Variants[0].UnitId;
                offSide = false;
            }

            string[] names = new string[palette.Count + (offSide ? 1 : 0)];
            for (int i = 0; i < palette.Count; i++) names[i] = palette[i].Name;
            if (offSide) names[palette.Count] = "（不在名單內）" + unitId;

            int index = offSide ? palette.Count : palette.IndexOf(current);
            int picked = EditorGUILayout.Popup("角色", index, names);

            if (picked != index && picked >= 0 && picked < palette.Count)
            {
                current = palette[picked];
                unitId = current.Variants[0].UnitId;
                offSide = false;
            }

            if (offSide || current == null)
            {
                GUILayout.Toolbar(0, new[] { "—" });
                EditorGUILayout.LabelField(" ", "這個單位不在角色名單裡，仍然可以使用。",
                                           EditorStyles.wordWrappedMiniLabel);
                return unitId;
            }

            string[] labels = new string[current.Variants.Count];
            int variantIndex = 0;
            for (int i = 0; i < current.Variants.Count; i++)
            {
                labels[i] = current.Variants[i].Label;
                if (string.Equals(current.Variants[i].UnitId, unitId, StringComparison.OrdinalIgnoreCase))
                    variantIndex = i;
            }

            int pickedVariant = GUILayout.Toolbar(variantIndex, labels);
            if (pickedVariant >= 0 && pickedVariant < current.Variants.Count)
                unitId = current.Variants[pickedVariant].UnitId;

            RosterVariant chosen = roster.VariantOf(unitId);
            EditorGUILayout.LabelField(" ", chosen != null && !string.IsNullOrEmpty(chosen.Note)
                ? chosen.Note : " ", EditorStyles.wordWrappedMiniLabel);

            return unitId;
        }

        private string[] UnitMenuLabels()
        {
            string[] labels = new string[_data.UnitIds.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                string id = _data.UnitIds[i];
                UnitDef def;
                if (!_data.Units.TryGet(id, out def)) { labels[i] = id; continue; }

                string archetype = PrototypeVisuals.PlannerNameOf(PrototypeVisuals.ArchetypeOf(def));
                labels[i] = _plannerMode
                    ? def.DisplayName + "  (" + archetype + ")"
                    : id + "  —  " + def.DisplayName + " (" + archetype + ")";
            }
            return labels;
        }

        private string[] AiMenuLabels()
        {
            string[] labels = new string[_data.AiIds.Count];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = _plannerMode ? PlannerVocabulary.AiName(_data.AiIds[i]) : _data.AiIds[i];
            return labels;
        }

        // -------------------------------------------------------------- status

        private void DrawStatusBar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DescribeSelection(), EditorStyles.miniLabel, GUILayout.Width(rect.width * 0.45f));

            int errors = 0, warnings = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Level == IssueLevel.Error) errors++; else warnings++;
            }

            string summary = errors == 0 && warnings == 0
                ? "檢查通過，可以試玩。"
                : (errors > 0 ? errors + " 個錯誤" : "") + (errors > 0 && warnings > 0 ? "、" : "")
                  + (warnings > 0 ? warnings + " 個提醒" : "");

            GUIStyle style = errors > 0 ? EditorStyles.boldLabel : EditorStyles.miniLabel;
            Color previous = GUI.color;
            if (errors > 0) GUI.color = new Color(1f, 0.55f, 0.5f);
            else if (warnings > 0) GUI.color = new Color(1f, 0.85f, 0.4f);
            EditorGUILayout.LabelField(summary, style);
            GUI.color = previous;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll);
            for (int i = 0; i < _issues.Count; i++)
            {
                EncounterIssue issue = _issues[i];
                Color c = GUI.color;
                GUI.color = issue.Level == IssueLevel.Error
                    ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.87f, 0.45f);

                string prefix = issue.Level == IssueLevel.Error ? "錯誤" : "提醒";
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(prefix, EditorStyles.miniButton, GUILayout.Width(40f))) JumpTo(issue);
                GUI.color = c;
                EditorGUILayout.LabelField(issue.Message, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private string DescribeSelection()
        {
            if (_selectedSpawn >= 0 && _selectedSpawn < _doc.Spawns.Count)
            {
                SpawnEntry s = _doc.Spawns[_selectedSpawn];
                return "已選取  " + _doc.TagOf(s) + "  " + DisplayNameOf(s)
                     + "   位置 (" + s.Position.X + "," + s.Position.Y + ")";
            }

            if (_hover.HasValue)
            {
                int idx = _doc.TerrainAt(_hover.Value);
                string name = idx >= 0 && idx < _data.Terrain.Count
                    ? (_plannerMode ? PlannerVocabulary.TerrainName(_data.Terrain[idx]) : _data.Terrain[idx].Name)
                    : "?";
                return "游標  (" + _hover.Value.X + "," + _hover.Value.Y + ")   " + name;
            }

            return _doc.Width + " x " + _doc.Height + "   單位 " + _doc.Spawns.Count + " 個";
        }

        private void JumpTo(EncounterIssue issue)
        {
            if (issue.SpawnIndex >= 0 && issue.SpawnIndex < _doc.Spawns.Count)
            {
                _selectedSpawn = issue.SpawnIndex;
                _selectedCell = _doc.Spawns[issue.SpawnIndex].Position;
                _camera.FocusCell(_selectedCell.Value);
            }
            else if (issue.Cell.HasValue)
            {
                _selectedCell = issue.Cell;
                _camera.FocusCell(issue.Cell.Value);
            }
            Repaint();
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }
    }
}
