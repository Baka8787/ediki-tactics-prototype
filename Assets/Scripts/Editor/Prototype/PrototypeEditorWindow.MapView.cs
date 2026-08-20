using System.Collections.Generic;
using Ediki.Core;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    public sealed partial class PrototypeEditorWindow
    {
        private static readonly Color MapBackground = new Color(0.13f, 0.14f, 0.17f);

        /// <summary>
        /// Taken when a stroke starts and pushed onto the history the first time
        /// the stroke actually changes something.
        ///
        /// Deferred on purpose: a drag across twenty cells is ONE thing the
        /// planner did, so it must be one undo step — and a click that lands on
        /// a cell already painted that colour must not produce an undo step that
        /// undoes nothing.
        /// </summary>
        private EncounterDocument _strokeSnapshot;
        private string _strokeLabel;
        private int _paintTerrain;

        private void DrawMapView(Rect rect)
        {
            EditorGUI.DrawRect(rect, MapBackground);
            if (Event.current.type != EventType.Repaint) return;

            MapViewRenderer.Options options = new MapViewRenderer.Options
            {
                SelectedSpawn = _selectedSpawn,
                Hovered = _hover,
                ShowStats = _showStats,
                ShowNames = _showNames,
                ProblemCells = _problemCells
            };

            _scene.Build(_solid, _doc, _data.Terrain, _data.Units, _camera, options);
            _solid.Draw(_camera, rect, new Vector2(position.width, position.height));

            DrawUnitLabels(rect);
            DrawViewOverlay(rect);
        }

        // ----------------------------------------------------------------- input

        /// <summary>
        /// Navigation follows the Scene view and Blender at once, because the two
        /// disagree only about the middle button and everything else is common:
        ///
        ///   middle drag              pan          (both)
        ///   shift + middle drag      pan          (Blender)
        ///   alt + left drag          orbit        (Scene view / Maya / Blender's
        ///                                         "emulate 3 button mouse")
        ///   right drag               orbit
        ///   alt + right drag         dolly        (Scene view)
        ///   wheel                    zoom
        ///   right held + WASD/QE     fly          (Scene view)
        ///
        /// Alt+left is checked BEFORE the paint tools, so holding alt turns the
        /// left button into a camera control instead of a brush — the same
        /// override every 3D application uses.
        /// </summary>
        private void HandleMapInput(Rect rect)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);

            TrackFlyKeys(e);

            switch (e.type)
            {
                case EventType.MouseMove:
                    {
                        Coord? next = inside ? _camera.WindowToCell(e.mousePosition, _doc.Width, _doc.Height) : null;
                        if (!Same(next, _hover)) { _hover = next; Repaint(); }
                        break;
                    }

                case EventType.MouseDown:
                    if (!inside) break;
                    GUI.FocusControl(null);
                    _lastMouse = e.mousePosition;

                    if (e.button == 2) { _panning = true; e.Use(); }
                    else if (e.button == 1) { _orbiting = true; _dollying = e.alt; e.Use(); }
                    else if (e.button == 0 && e.alt) { _orbiting = true; e.Use(); }
                    else if (e.button == 0)
                    {
                        _hover = _camera.WindowToCell(e.mousePosition, _doc.Width, _doc.Height);

                        // During a playtest the left button selects but never
                        // edits. Without this the window kept painting behind the
                        // running game, and the stray tiles and units only showed
                        // up after Stop — by which point they were indistinguishable
                        // from deliberate work.
                        if (EditingLocked) SelectOnly(_hover);
                        else BeginStroke(_hover);

                        e.Use();
                    }
                    Repaint();
                    break;

                case EventType.MouseDrag:
                    if (_panning)
                    {
                        _camera.Pan(_lastMouse, e.mousePosition);
                        _lastMouse = e.mousePosition;
                        e.Use(); Repaint();
                    }
                    else if (_dollying)
                    {
                        Vector2 d = e.mousePosition - _lastMouse;
                        _camera.Dolly(d.x + d.y);
                        _lastMouse = e.mousePosition;
                        e.Use(); Repaint();
                    }
                    else if (_orbiting)
                    {
                        _camera.Orbit(e.mousePosition - _lastMouse);
                        _lastMouse = e.mousePosition;
                        e.Use(); Repaint();
                    }
                    else if (_painting || _draggingSpawn >= 0)
                    {
                        _hover = _camera.WindowToCell(e.mousePosition, _doc.Width, _doc.Height);
                        ContinueStroke(_hover);
                        e.Use(); Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_panning || _orbiting || _painting || _draggingSpawn >= 0)
                    {
                        EndStroke();
                        e.Use(); Repaint();
                    }
                    break;

                case EventType.ScrollWheel:
                    if (!inside) break;
                    _camera.ZoomBy(e.delta.y, e.mousePosition);
                    e.Use(); Repaint();
                    break;
            }

            StepFlight();
        }

        // ------------------------------------------------------------- flight

        private readonly HashSet<KeyCode> _flyKeys = new HashSet<KeyCode>();
        private bool _dollying;
        private double _lastFlyTime;

        private static bool IsFlyKey(KeyCode k)
        {
            return k == KeyCode.W || k == KeyCode.A || k == KeyCode.S || k == KeyCode.D
                || k == KeyCode.Q || k == KeyCode.E;
        }

        /// <summary>
        /// WASD only steers while the orbit button is down, exactly as in the
        /// Scene view. Without that gate, W would fly the camera every time
        /// somebody typed a name into the inspector.
        /// </summary>
        private void TrackFlyKeys(Event e)
        {
            if (e.type == EventType.KeyDown && IsFlyKey(e.keyCode) && _orbiting)
            {
                _flyKeys.Add(e.keyCode);
                e.Use();
            }
            else if (e.type == EventType.KeyUp && IsFlyKey(e.keyCode))
            {
                _flyKeys.Remove(e.keyCode);
            }

            if (!_orbiting && _flyKeys.Count > 0) _flyKeys.Clear();
        }

        private void StepFlight()
        {
            if (_flyKeys.Count == 0 || !_orbiting)
            {
                _lastFlyTime = 0d;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float seconds = _lastFlyTime > 0d ? (float)(now - _lastFlyTime) : 1f / 60f;
            _lastFlyTime = now;
            if (seconds > 0.1f) seconds = 0.1f;   // a stall must not teleport the camera

            float right = (_flyKeys.Contains(KeyCode.D) ? 1f : 0f) - (_flyKeys.Contains(KeyCode.A) ? 1f : 0f);
            float forward = (_flyKeys.Contains(KeyCode.W) ? 1f : 0f) - (_flyKeys.Contains(KeyCode.S) ? 1f : 0f);
            float up = (_flyKeys.Contains(KeyCode.E) ? 1f : 0f) - (_flyKeys.Contains(KeyCode.Q) ? 1f : 0f);

            _camera.Fly(right, forward, up, seconds, Event.current.shift);
            Repaint();
        }

        private static bool Same(Coord? a, Coord? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (a.HasValue != b.HasValue) return false;
            return a.Value == b.Value;
        }

        /// <summary>Read-only click: moves the selection, changes no data.</summary>
        private void SelectOnly(Coord? cell)
        {
            _relayout = true;

            if (!cell.HasValue) { _selectedSpawn = -1; _selectedCell = null; return; }

            _selectedCell = cell.Value;
            int spawnIndex = _doc.IndexOfSpawnAt(cell.Value);
            if (spawnIndex >= 0) _selectedSpawn = spawnIndex;
        }

        private void BeginStroke(Coord? cell)
        {
            if (EditingLocked) { SelectOnly(cell); return; }

            // Any click can change the selection, and the inspector draws a
            // different set of controls for "nothing selected" than for a unit.
            // OnGUI abandons the pass when this is set, before any layout is emitted.
            _relayout = true;

            if (!cell.HasValue) { _selectedSpawn = -1; _selectedCell = null; return; }

            Coord c = cell.Value;
            _selectedCell = c;
            int spawnIndex = _doc.IndexOfSpawnAt(c);

            switch (_tool)
            {
                case Tool.Terrain:
                    if (spawnIndex >= 0) _selectedSpawn = spawnIndex;
                    ArmStroke("塗地形");
                    _paintTerrain = _brushTerrain;
                    _painting = true;
                    PaintCell(c);
                    break;

                case Tool.Unit:
                    if (spawnIndex >= 0)
                    {
                        _selectedSpawn = spawnIndex;
                        ArmStroke("移動單位");
                        _draggingSpawn = spawnIndex;
                    }
                    else
                    {
                        ArmStroke("放置單位");
                        PlaceUnit(c);
                    }
                    break;

                case Tool.Objective:
                    if (spawnIndex >= 0) _selectedSpawn = spawnIndex;
                    if (_doc.ObjectiveKind == ObjectiveKind.Reach)
                    {
                        ArmStroke("設定任務地點");
                        if (!(_doc.ObjectiveTarget == c))
                        {
                            CommitStroke();
                            _doc.ObjectiveTarget = c;
                            MarkDirty();
                        }
                    }
                    else
                    {
                        SetStatus("目前的任務類型不需要地點。改成「抵達指定地點」才會用到。", MessageType.Info);
                    }
                    break;

                // Two tools, not one that guesses.
                //
                // The single eraser deleted a unit when there was one and repainted
                // the ground when there was not, so what a click did depended on
                // something under the cursor rather than on what was chosen — and
                // a click aimed at the floor that landed on a unit deleted it.
                case Tool.EraseUnit:
                    if (spawnIndex < 0)
                    {
                        SetStatus("這一格沒有單位。要改地形請選「重置地形」。", MessageType.Info);
                        break;
                    }
                    ArmStroke("刪除單位");
                    CommitStroke();
                    _selectedSpawn = -1;
                    RemoveSpawn(spawnIndex);
                    break;

                case Tool.ResetTerrain:
                    if (spawnIndex >= 0) _selectedSpawn = spawnIndex;
                    ArmStroke("重置地形");
                    _paintTerrain = EncounterDocumentIO.DefaultTerrainIndex(_data.Terrain);
                    _painting = true;
                    PaintCell(c);
                    break;
            }
        }

        private void ContinueStroke(Coord? cell)
        {
            if (EditingLocked) return;
            if (!cell.HasValue) return;
            Coord c = cell.Value;

            if (_painting) { PaintCell(c); return; }

            if (_draggingSpawn >= 0 && _draggingSpawn < _doc.Spawns.Count)
            {
                SpawnEntry moving = _doc.Spawns[_draggingSpawn];
                if (moving.Position == c) return;

                int occupant = _doc.IndexOfSpawnAt(c);
                if (occupant >= 0 && occupant != _draggingSpawn) return;   // no stacking

                CommitStroke();
                moving.Position = c;
                _selectedCell = c;
                MarkDirty();
            }
        }

        private void EndStroke()
        {
            _panning = false;
            _orbiting = false;
            _dollying = false;
            _painting = false;
            _draggingSpawn = -1;
            _strokeSnapshot = null;
            _strokeLabel = null;
            _flyKeys.Clear();
        }

        private void ArmStroke(string label)
        {
            _blockedPaintCell = null;
            _warnedLethalCell = null;
            _strokeSnapshot = _doc.Clone();
            _strokeLabel = label;
        }

        /// <summary>Pushes the armed snapshot, once, at the moment the stroke first changes something.</summary>
        private void CommitStroke()
        {
            if (_strokeSnapshot == null) return;
            _history.Push(_strokeSnapshot, _strokeLabel);
            _strokeSnapshot = null;
        }

        /// <summary>
        /// Paints one cell, refusing to bury a unit under terrain it cannot occupy.
        ///
        /// A drag-paint crosses cells the eye is not on, so an obstacle stroke
        /// that happens to pass over a unit would leave it standing inside a wall
        /// — legal in the document, rejected by the loader, and only discovered
        /// at save time by which point the stroke is long finished. Refusing at
        /// the cell is the only point where the planner can still see what they
        /// meant to do.
        ///
        /// Only BLOCKING terrain is refused. Lethal terrain is passable by the
        /// rules — pushing an enemy into a chasm is the whole reason it exists —
        /// so painting it under a unit is allowed and merely warned about, and
        /// validation carries on flagging the unit as starting in it.
        /// </summary>
        private void PaintCell(Coord c)
        {
            if (_doc.TerrainAt(c) == _paintTerrain) return;

            int index = Mathf.Clamp(_paintTerrain, 0, _data.Terrain.Count - 1);
            TerrainDef target = _data.Terrain[index];
            SpawnEntry occupant = _doc.SpawnAt(c);

            if (occupant != null && target.BlocksMovement)
            {
                if (_blockedPaintCell != c)
                {
                    _blockedPaintCell = c;
                    SetStatus("(" + c.X + "," + c.Y + ") 上有 " + _doc.TagOf(occupant)
                              + "，不能改成「" + PlannerVocabulary.TerrainName(target)
                              + "」——沒有單位站得上去。\n請先把單位移開或刪掉。", MessageType.Warning);
                }
                return;
            }

            if (occupant != null && target.IsLethal && _warnedLethalCell != c)
            {
                _warnedLethalCell = c;
                SetStatus("(" + c.X + "," + c.Y + ") 上有 " + _doc.TagOf(occupant)
                          + "，改成「" + PlannerVocabulary.TerrainName(target)
                          + "」的話它一開場就會死。", MessageType.Warning);
            }

            CommitStroke();
            _doc.SetTerrain(c.X, c.Y, _paintTerrain);
            MarkDirty();
        }

        // Which cell the last refusal / lethal warning was about, so a drag over
        // the same occupied cell does not rewrite the status bar every frame.
        private Coord? _blockedPaintCell;
        private Coord? _warnedLethalCell;

        private void PlaceUnit(Coord c)
        {
            if (string.IsNullOrEmpty(_brushUnitId))
            {
                SetStatus("先在左邊選一個角色。", MessageType.Warning);
                return;
            }

            CommitStroke();

            _doc.Spawns.Add(NewSpawnAt(c));
            _selectedSpawn = _doc.Spawns.Count - 1;
            MarkDirty();
        }

        /// <summary>
        /// A spawn built from the current brush.
        ///
        /// An objective prop arrives with protect=true already set: that is the
        /// only way a shrine is ever placed, and making the planner remember a
        /// checkbox for it is how you end up with a defend map that cannot be won.
        /// </summary>
        private SpawnEntry NewSpawnAt(Coord c)
        {
            bool isProp = _data.Roster.SideOf(_brushUnitId) == RosterSide.Objective;

            return new SpawnEntry
            {
                Faction = isProp ? Faction.Player : _brushFaction,
                UnitId = _brushUnitId,
                Position = c,
                ArrivesOnTurn = isProp ? 0 : _brushArrivalTurn,
                Protect = isProp,
                AiProfileId = !isProp && _brushFaction == Faction.Enemy ? _brushAi : null
            };
        }

        private void RemoveSpawn(int index)
        {
            if (index < 0 || index >= _doc.Spawns.Count) return;
            _doc.Spawns.RemoveAt(index);
            if (_selectedSpawn == index) _selectedSpawn = -1;
            else if (_selectedSpawn > index) _selectedSpawn--;
            MarkDirty();
        }

        // ---------------------------------------------------------------- labels

        private void DrawUnitLabels(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;

            // Nearest first, so when two nameplates collide the one in front keeps
            // the spot it earned and the one behind moves or goes.
            _labelOrder.Clear();
            for (int i = 0; i < _scene.Labels.Count; i++) _labelOrder.Add(_scene.Labels[i]);
            _labelOrder.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            _placedLabels.Clear();

            GUI.BeginClip(rect);
            for (int i = 0; i < _labelOrder.Count; i++)
            {
                MapViewRenderer.UnitLabel label = _labelOrder[i];

                // Behind a wall. The selection is exempt — losing the label of the
                // thing you just clicked is worse than one plate drawn over a wall.
                if (label.Occluded && !label.Selected) continue;

                Vector2 p = label.Position - new Vector2(rect.x, rect.y);
                if (p.x < -80f || p.x > rect.width + 80f || p.y < -40f || p.y > rect.height + 40f) continue;

                int lines = 1 + (string.IsNullOrEmpty(label.Name) ? 0 : 1) + (string.IsNullOrEmpty(label.Stats) ? 0 : 1);
                float height = lines * 12f + 4f;

                Rect plate = new Rect(p.x - 56f, p.y - height, 112f, height);

                // Lift out of the way of an already-placed plate, up to three
                // times. Past that the cluster is too dense to label legibly and
                // dropping this one says so honestly — a pile of overlapping
                // nameplates reads as neither of the units it covers.
                if (!TryPlaceLabel(ref plate, label.Selected)) continue;
                _placedLabels.Add(plate);

                Color plateColor = new Color(0.06f, 0.06f, 0.08f, label.Dimmed ? 0.45f : 0.72f);
                EditorGUI.DrawRect(plate, plateColor);

                float y = plate.y + 1f;
                Color tint = label.Tint;
                if (label.Dimmed) tint.a = 0.65f;

                DrawCentred(new Rect(plate.x, y, plate.width, 12f), label.Tag, _labelTag, tint);
                y += 12f;

                if (!string.IsNullOrEmpty(label.Name))
                {
                    DrawCentred(new Rect(plate.x, y, plate.width, 12f), label.Name, _labelName,
                                new Color(0.92f, 0.92f, 0.94f, label.Dimmed ? 0.6f : 1f));
                    y += 12f;
                }

                if (!string.IsNullOrEmpty(label.Stats))
                    DrawCentred(new Rect(plate.x, y, plate.width, 12f), label.Stats, _labelName,
                                new Color(0.75f, 0.80f, 0.86f));
            }
            GUI.EndClip();
        }

        // Reused each repaint — this runs several times a frame.
        private readonly List<MapViewRenderer.UnitLabel> _labelOrder = new List<MapViewRenderer.UnitLabel>();
        private readonly List<Rect> _placedLabels = new List<Rect>();

        /// <summary>
        /// Nudges a nameplate up until it clears the ones already placed, or
        /// gives up. The selected unit never gives up — it is the one label the
        /// planner is actively looking for.
        /// </summary>
        private bool TryPlaceLabel(ref Rect plate, bool insist)
        {
            const int MaxLifts = 3;
            int lifts = insist ? MaxLifts + 6 : MaxLifts;

            for (int attempt = 0; attempt <= lifts; attempt++)
            {
                bool clear = true;
                for (int i = 0; i < _placedLabels.Count && clear; i++)
                    clear = !_placedLabels[i].Overlaps(plate);

                if (clear) return true;
                plate.y -= plate.height + 2f;
            }

            return insist;
        }

        private static void DrawCentred(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        private void DrawViewOverlay(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;

            string tool;
            switch (_tool)
            {
                case Tool.Terrain: tool = "地形筆刷 — " + PlannerVocabulary.TerrainName(
                    _data.Terrain[Mathf.Clamp(_brushTerrain, 0, _data.Terrain.Count - 1)]); break;
                case Tool.Unit: tool = "放置單位 — " + PlannerVocabulary.FactionName(_brushFaction)
                    + " " + DisplayNameOfId(_brushUnitId); break;
                case Tool.Objective: tool = "任務地點"; break;
                case Tool.EraseUnit: tool = "刪除單位"; break;
                default: tool = "重置地形 — " + PlannerVocabulary.TerrainName(
                    _data.Terrain[EncounterDocumentIO.DefaultTerrainIndex(_data.Terrain)]); break;
            }

            Rect box = new Rect(rect.x + 8f, rect.yMax - 40f, rect.width - 16f, 32f);
            GUI.BeginClip(box);
            Color previous = GUI.color;
            GUI.color = new Color(0.85f, 0.88f, 0.92f, 0.85f);
            GUI.Label(new Rect(0f, 0f, box.width, 14f), tool, EditorStyles.miniLabel);
            GUI.Label(new Rect(0f, 14f, box.width, 14f),
                      _doc.Width + " x " + _doc.Height + "    " + _camera.Describe(),
                      EditorStyles.miniLabel);
            GUI.color = previous;
            GUI.EndClip();
        }

        // ------------------------------------------------------------ shortcuts

        private void HandleShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            bool control = e.control || e.command;

            // The camera keys below stay live during a playtest; the ones that
            // change the encounter do not.
            if (control && e.keyCode == KeyCode.Z) { if (!EditingLocked) Undo(); e.Use(); return; }
            if (control && e.keyCode == KeyCode.Y) { if (!EditingLocked) Redo(); e.Use(); return; }
            if (control && e.keyCode == KeyCode.S) { if (!EditingLocked) Save(false); e.Use(); return; }

            switch (e.keyCode)
            {
                case KeyCode.Home:
                    _camera.FocusMap(_doc.Width, _doc.Height);
                    e.Use(); Repaint();
                    break;

                // Blender's numpad views, on the number row as well as the pad,
                // because plenty of keyboards no longer have a numpad.
                case KeyCode.Alpha7:
                case KeyCode.Keypad7:
                    _camera.TopView(_doc.Width, _doc.Height);
                    e.Use(); Repaint();
                    break;

                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    _camera.AxisView(_doc.Width, _doc.Height, false);
                    e.Use(); Repaint();
                    break;

                case KeyCode.Alpha3:
                case KeyCode.Keypad3:
                    _camera.AxisView(_doc.Width, _doc.Height, true);
                    e.Use(); Repaint();
                    break;

                case KeyCode.Alpha5:
                case KeyCode.Keypad5:
                    _camera.TogglePerspective();
                    e.Use(); Repaint();
                    break;

                case KeyCode.F:
                    if (_selectedSpawn >= 0 && _selectedSpawn < _doc.Spawns.Count)
                        _camera.FocusCell(_doc.Spawns[_selectedSpawn].Position);
                    else if (_selectedCell.HasValue)
                        _camera.FocusCell(_selectedCell.Value);
                    else
                        _camera.FocusMap(_doc.Width, _doc.Height);
                    e.Use(); Repaint();
                    break;

                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (EditingLocked) break;
                    if (_selectedSpawn >= 0 && _selectedSpawn < _doc.Spawns.Count)
                    {
                        _history.Push(_doc, "刪除單位");
                        RemoveSpawn(_selectedSpawn);
                        _relayout = true;
                        e.Use(); Repaint();
                    }
                    break;
            }
        }
    }
}
