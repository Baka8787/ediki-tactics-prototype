using System.Collections.Generic;
using Ediki.Core;
using UnityEngine;

namespace Ediki.Unity
{
    /// <summary>
    /// Gray-box rendering (ADR-0005). Deliberately throwaway: cubes, flat colours,
    /// no prefabs, no art. When the dimension decision lands (OD-15) this whole
    /// class is expected to be deleted, and Ediki.Core will not care.
    ///
    /// Reads values from the rule layer through BattleQueries; never holds a
    /// mutable BattleState of its own (A7).
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        /// <summary>Where a tile slab starts. Matches the editor's map view.</summary>
        private const float TileBottom = -0.10f;

        private static readonly Color ColorFallback = new Color(0.4f, 0.4f, 0.4f);

        // Overlay tints. Five sets, because "where can I go", "where can I hit",
        // "where can they go" and "where can they hit" are four different
        // questions — and the fifth, the OVERLAP of my movement with their
        // threat, is the one the whole game is about.
        //
        // The previous version painted danger first and movement second, so
        // movement won every overlap and the single most decision-relevant cell
        // type on the board was invisible.
        private static readonly Color TintMyMove = new Color(0.25f, 0.65f, 1f);   // I can stand here
        private static readonly Color TintMyReach = new Color(0.30f, 0.95f, 0.85f); // I can hit from here after moving
        private static readonly Color TintEnemyMove = new Color(0.85f, 0.55f, 0.15f); // they can walk here
        private static readonly Color TintDanger = new Color(1f, 0.25f, 0.2f);    // they can hit here
        private static readonly Color TintContested = new Color(0.85f, 0.25f, 0.85f); // BOTH — the real decision
        private static readonly Color TintHover = new Color(1f, 1f, 0.4f);

        /// <summary>A cell the aimed action can legally be fired at. Gold, like the bar's armed button.</summary>
        private static readonly Color TintActionTarget = new Color(1f, 0.82f, 0.35f);

        private static readonly Color GuardColor = new Color(0.95f, 0.85f, 0.3f);
        private static readonly Color TauntColor = new Color(1f, 0.75f, 0.1f);
        private static readonly Color SlowColor = new Color(0.45f, 0.75f, 0.95f);

        private BattleMap _map;
        private Renderer[] _cellRenderers;
        private MaterialPropertyBlock _block;
        private int _colorProperty;

        // Health bars. Two flat cubes per unit — a dark trough and a coloured
        // fill — parented to the unit and lying in the XZ plane.
        //
        // No billboarding and no Canvas: the camera looks straight down, so a bar
        // lying flat above the unit is already face-on, and a world-space quad
        // stays in the same throwaway grayblock idiom as everything else here
        // (ADR-0005). A screen-space HP bar would need OnGUI per unit per frame
        // and would not survive the camera being moved later.
        private const float BarWidth = 0.86f;
        private const float BarHeight = 0.14f;

        /// <summary>Where a unit's body is centred. Shared by the cube and its bar.</summary>
        private const float UnitCentreY = 0.75f;

        private static readonly Color BarTrough = new Color(0.10f, 0.10f, 0.12f);
        private static readonly Color BarHigh = new Color(0.35f, 0.85f, 0.35f);
        private static readonly Color BarMid = new Color(0.95f, 0.80f, 0.25f);
        private static readonly Color BarLow = new Color(0.95f, 0.25f, 0.20f);

        private sealed class HealthBar
        {
            public Transform Trough;
            public Transform Fill;
            public Renderer FillRenderer;
        }

        private readonly Dictionary<int, HealthBar> _healthBars = new Dictionary<int, HealthBar>();

        private readonly Dictionary<int, Transform> _unitViews = new Dictionary<int, Transform>();

        /// <summary>Renderers making up each body. A cross and a prop are two pieces, not one.</summary>
        private readonly Dictionary<int, Renderer[]> _unitParts = new Dictionary<int, Renderer[]>();

        /// <summary>Unit id -> its palette slot within its own faction. Built once.</summary>
        private readonly Dictionary<int, Color> _unitColors = new Dictionary<int, Color>();

        private Transform _unitRoot;

        /// <summary>
        /// Builds the board. Safe to call again for a different map — it clears
        /// whatever it built last time first, so the component is reused rather
        /// than destroyed and re-added (which would leave two views alive for a frame).
        /// </summary>
        public void Build(BattleState state)
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _unitViews.Clear();
            _unitColors.Clear();
            _healthBars.Clear();
            _unitParts.Clear();

            _map = state.Map;
            _block = new MaterialPropertyBlock();
            _colorProperty = Shader.PropertyToID("_BaseColor");

            Transform cellRoot = new GameObject("Cells").transform;
            cellRoot.SetParent(transform, false);

            _cellRenderers = new Renderer[_map.Width * _map.Height];

            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    Coord c = new Coord(x, y);
                    TerrainDef terrain = _map.TerrainAt(c);

                    // Same heights the editor's map view uses (PrototypeVisuals),
                    // so a map reads identically while you build it and while you
                    // play it. This is also the only reason a chasm is visible at
                    // all here: it used to fall through to a grey fallback tile,
                    // which made the one terrain that kills instantly look exactly
                    // like ordinary ground.
                    TileStyle style = PrototypeVisuals.StyleOf(terrain);
                    float top = PrototypeVisuals.TileTopHeight(style);

                    GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cell.name = "Cell " + c + " " + terrain.Name;
                    cell.transform.SetParent(cellRoot, false);

                    // A pit is drawn as a slab whose TOP is below the walkable
                    // plane, deep enough that the surrounding tiles wall it in.
                    float bottom = style == TileStyle.Chasm ? top - 0.5f : TileBottom;
                    float height = Mathf.Max(0.05f, top - bottom);

                    cell.transform.localPosition = new Vector3(x, bottom + height * 0.5f, -y);
                    cell.transform.localScale = new Vector3(0.96f, height, 0.96f);

                    Object.Destroy(cell.GetComponent<Collider>());
                    _cellRenderers[y * _map.Width + x] = cell.GetComponent<Renderer>();
                }
            }

            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(transform, false);

            for (int i = 0; i < state.Units.Count; i++)
                CreateUnitView(state.Units[i]);

            SetUpCamera();
        }

        /// <summary>The five cell sets the board paints. Any may be null = not shown.</summary>
        public sealed class Overlays
        {
            /// <summary>Cells the selected unit can stand on this turn.</summary>
            public HashSet<Coord> MyMove;

            /// <summary>Cells it could attack this turn, INCLUDING after moving.</summary>
            public HashSet<Coord> MyReach;

            /// <summary>Cells the enemy side can walk to on a full bar.</summary>
            public HashSet<Coord> EnemyMove;

            /// <summary>Cells the enemy side can attack — move plus reach.</summary>
            public HashSet<Coord> EnemyThreat;

            /// <summary>
            /// Legal targets for the action currently being aimed, or null.
            ///
            /// Painted over everything else on purpose: while an action is armed
            /// the board is answering one question, and the usual four overlays
            /// are background to it.
            /// </summary>
            public HashSet<Coord> ActionTargets;

            public Coord? Hovered;
        }

        public void Refresh(BattleState state, Overlays overlays)
        {
            Overlays o = overlays ?? new Overlays();

            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    Coord c = new Coord(x, y);
                    Color color = TerrainColor(_map.TerrainAt(c));

                    bool myMove = o.MyMove != null && o.MyMove.Contains(c);
                    bool myReach = o.MyReach != null && o.MyReach.Contains(c);
                    bool theirMove = o.EnemyMove != null && o.EnemyMove.Contains(c);
                    bool theirThreat = o.EnemyThreat != null && o.EnemyThreat.Contains(c);

                    // Weakest claim first, strongest last. The order encodes what
                    // matters: "they could walk here" is background information,
                    // "I can go here and be shot for it" is the decision.
                    if (theirMove && !theirThreat) color = Color.Lerp(color, TintEnemyMove, 0.30f);
                    if (theirThreat) color = Color.Lerp(color, TintDanger, 0.45f);
                    if (myReach && !myMove) color = Color.Lerp(color, TintMyReach, 0.45f);
                    if (myMove) color = Color.Lerp(color, TintMyMove, 0.50f);

                    // Contested is set outright rather than blended. Blending blue
                    // over red just makes a muddy purple that also happens to be
                    // what a lightly-tinted cell looks like, and this cell type is
                    // far too important to be told apart by shade.
                    if (myMove && theirThreat) color = TintContested;

                    // Set outright, above every other overlay, for the same reason
                    // Contested is: a cell you are about to aim at is a decision,
                    // not a shade of one.
                    if (o.ActionTargets != null && o.ActionTargets.Contains(c)) color = TintActionTarget;

                    if (o.Hovered.HasValue && o.Hovered.Value == c) color = Color.Lerp(color, TintHover, 0.55f);

                    Renderer r = _cellRenderers[y * _map.Width + x];
                    r.GetPropertyBlock(_block);
                    _block.SetColor(_colorProperty, color);
                    r.SetPropertyBlock(_block);
                }
            }

            for (int i = 0; i < state.Units.Count; i++)
            {
                UnitState u = state.Units[i];
                Transform view;

                // Built on demand, not only at Build().
                //
                // Reinforcements do not exist as UnitStates until the round they
                // walk in on, so a view was never made for them and this loop
                // silently skipped them forever. They were fully alive in the
                // rules and completely invisible on screen — you watched your HP
                // fall to something that was not there.
                if (!_unitViews.TryGetValue(u.Id, out view))
                {
                    CreateUnitView(u);
                    if (!_unitViews.TryGetValue(u.Id, out view)) continue;
                }

                UpdateHealthBar(u);

                if (!u.IsAlive)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                view.localPosition = new Vector3(u.Position.X, UnitCentreY, -u.Position.Y);

                Color color = ColorOf(u.Id);

                // A sleeping enemy is dimmed rather than recoloured, so its
                // identity colour survives — you can still tell WHICH enemy it is.
                if (u.Faction == Faction.Enemy && !u.IsActivated) color *= 0.55f;

                // Statuses override identity, because a status is the thing you
                // need to notice this turn.
                if (u.IsGuarding) color = Color.Lerp(color, GuardColor, 0.6f);
                if (state.IsSlowed(u)) color = Color.Lerp(color, SlowColor, 0.55f);
                if (state.IsTaunting(u)) color = Color.Lerp(color, TauntColor, 0.7f);

                Renderer[] parts;
                if (!_unitParts.TryGetValue(u.Id, out parts)) continue;
                for (int p = 0; p < parts.Length; p++) Paint(parts[p], color);
            }
        }

        /// <summary>Screen point to grid cell, via the y=0 plane. Null when off-map.</summary>
        public Coord? ScreenToCell(Camera camera, Vector3 screenPosition)
        {
            if (camera == null || _map == null) return null;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (Mathf.Approximately(ray.direction.y, 0f)) return null;

            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return null;

            Vector3 hit = ray.origin + ray.direction * t;
            int x = Mathf.RoundToInt(hit.x);
            int y = Mathf.RoundToInt(-hit.z);

            Coord c = new Coord(x, y);
            return _map.Contains(c) ? c : (Coord?)null;
        }

        // ------------------------------------------------------ unit appearance
        //
        // The SAME grammar the editor draws with (PrototypeVisuals), so a squad
        // looks the way it looked while it was being placed.
        //
        // Three independent channels, and the change that matters is the third:
        //
        //   SHAPE   what it does      six archetypes, from UnitDef alone
        //   SIZE    how hard to kill  footprint grows with max HP
        //   COLOUR  WHICH SIDE        blue / red, plus gold for the objective
        //
        // Colour used to carry side AND identity at once, by spacing hues across
        // a faction band. That is one channel doing two jobs, and it is why four
        // identical minions arrived in four unrelated colours. Identity is TEXT
        // now — the nameplate over each unit — which is what the editor already
        // did and the only channel that scales past a handful of unit types.

        /// <summary>Shape channel: what this unit DOES. Never who owns it.</summary>
        public static UnitArchetype ShapeFor(UnitDef def) => PrototypeVisuals.ArchetypeOf(def);

        /// <summary>Size channel: how much killing it takes. Footprint, not height.</summary>
        public static float FootprintFor(UnitDef def)
        {
            if (def.MaxHp >= 300) return 0.82f;
            if (def.MaxHp >= 150) return 0.68f;
            return 0.54f;
        }

        /// <summary>
        /// Hands every unit a palette slot, spaced across its faction's band.
        /// Units of the SAME type share a slot, so four identical minions read as
        /// one kind of thing rather than four unrelated colours.
        /// </summary>
        /// <summary>
        /// Faction, plus gold for whatever the objective is about.
        ///
        /// No per-type hue any more. Which unit this is now comes from the
        /// nameplate above it, which is the channel that still works when the
        /// roster has twenty entries instead of four.
        /// </summary>
        public static Color BodyColorOf(UnitState unit)
        {
            bool objective = unit.IsObjectiveTarget || unit.MustSurvive;
            return PrototypeVisuals.BodyColor(unit.Faction, objective);
        }

        /// <summary>The colour this unit is drawn in. Used by the console legend.</summary>
        public Color ColorOf(int unitId)
        {
            Color c;
            return _unitColors.TryGetValue(unitId, out c) ? c : ColorFallback;
        }

        /// <summary>One line of legend for the console, matching what is drawn.</summary>
        public static string DescribeVisual(UnitDef def, Faction faction)
        {
            string shape = PrototypeVisuals.PlannerNameOf(PrototypeVisuals.ArchetypeOf(def));
            float f = FootprintFor(def);
            string size = f >= 0.8f ? "large " : f >= 0.65f ? "medium" : "small ";
            return (faction == Faction.Player ? "blue " : "red  ") + shape.PadRight(4) + " " + size;
        }

        /// <summary>
        /// One body, in the archetype silhouette the editor drew it with.
        ///
        /// A cross and a two-tier prop are two objects rather than one, so the
        /// root is an empty transform and the shape hangs under it. That keeps
        /// positioning and the health bar identical for every archetype.
        /// </summary>
        private void CreateUnitView(UnitState unit)
        {
            GameObject go = new GameObject("Unit " + unit.Id + " " + unit.Def.DisplayName);
            go.transform.SetParent(_unitRoot, false);

            float footprint = FootprintFor(unit.Def);
            float height = BodyHeight(unit);
            UnitArchetype archetype = PrototypeVisuals.ArchetypeOf(unit.Def);

            List<Renderer> parts = new List<Renderer>();

            switch (archetype)
            {
                case UnitArchetype.Prop:
                    parts.Add(Part(go.transform, PrimitiveType.Cube,
                        new Vector3(0f, -height * 0.28f, 0f),
                        new Vector3(footprint * 1.15f, height * 0.42f, footprint * 1.15f)));
                    parts.Add(Part(go.transform, PrimitiveType.Cube,
                        new Vector3(0f, height * 0.10f, 0f),
                        new Vector3(footprint * 0.58f, height * 0.34f, footprint * 0.58f)));
                    break;

                case UnitArchetype.Support:
                    parts.Add(Part(go.transform, PrimitiveType.Cube, Vector3.zero,
                        new Vector3(footprint * 1.2f, height * 0.7f, footprint * 0.34f)));
                    parts.Add(Part(go.transform, PrimitiveType.Cube, Vector3.zero,
                        new Vector3(footprint * 0.34f, height * 0.7f, footprint * 1.2f)));
                    break;

                case UnitArchetype.Ranged:
                    parts.Add(Mesh(go.transform, PrototypeMeshes.HexPrism, Vector3.zero,
                        new Vector3(footprint * 1.05f, height, footprint * 1.05f)));
                    break;

                case UnitArchetype.Mobile:
                    parts.Add(Mesh(go.transform, PrototypeMeshes.Pyramid, Vector3.zero,
                        new Vector3(footprint * 1.15f, height * 1.15f, footprint * 1.15f)));
                    break;

                case UnitArchetype.Heavy:
                    parts.Add(Part(go.transform, PrimitiveType.Cube, Vector3.zero,
                        new Vector3(footprint * 1.1f, height * 0.86f, footprint * 1.1f)));
                    break;

                default:
                    // Unity cylinders are two units tall, so the y scale halves.
                    parts.Add(Part(go.transform, PrimitiveType.Cylinder, Vector3.zero,
                        new Vector3(footprint, height * 0.5f, footprint)));
                    break;
            }

            _unitViews[unit.Id] = go.transform;
            _unitParts[unit.Id] = parts.ToArray();
            _unitColors[unit.Id] = BodyColorOf(unit);

            CreateHealthBar(unit, go.transform);
        }

        private static Renderer Part(Transform parent, PrimitiveType type, Vector3 offset, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = scale;
            Object.Destroy(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        private static Renderer Mesh(Transform parent, UnityEngine.Mesh mesh, Vector3 offset, Vector3 scale)
        {
            GameObject go = new GameObject("Body");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        /// <summary>
        /// Where this unit's nameplate hangs, in world space.
        ///
        /// Derived from the body it belongs to rather than fixed, for the same
        /// reason the health bar is: a kill target is taller, and a constant
        /// height would bury its label inside its own head.
        /// </summary>
        public static Vector3 NameplateAnchor(UnitState unit)
        {
            return new Vector3(unit.Position.X,
                               UnitCentreY + BodyHeight(unit) * 0.5f + 0.45f,
                               -unit.Position.Y);
        }

        /// <summary>
        /// Kill targets stand a head taller. It is the only thing the OBJECTIVE
        /// cares about, so it gets the one channel not already spoken for.
        /// </summary>
        private static float BodyHeight(UnitState unit) => unit.IsObjectiveTarget ? 1.5f : 0.9f;

        /// <summary>
        /// Where this unit's bar floats. Derived from its body height, not fixed:
        /// the camera looks straight down, so anything at or below the top of the
        /// cube is simply hidden behind it — and a tall objective target tops out
        /// above where a constant bar height would have put the bar.
        /// </summary>
        private static float BarYFor(UnitState unit) => UnitCentreY + BodyHeight(unit) * 0.5f + 0.15f;

        private void CreateHealthBar(UnitState unit, Transform owner)
        {
            GameObject trough = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trough.name = "HP trough";
            trough.transform.SetParent(_unitRoot, false);
            trough.transform.localScale = new Vector3(BarWidth, 0.04f, BarHeight);
            Object.Destroy(trough.GetComponent<Collider>());
            Paint(trough.GetComponent<Renderer>(), BarTrough);

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "HP fill";
            fill.transform.SetParent(_unitRoot, false);
            fill.transform.localScale = new Vector3(BarWidth, 0.06f, BarHeight * 0.7f);
            Object.Destroy(fill.GetComponent<Collider>());

            _healthBars[unit.Id] = new HealthBar
            {
                Trough = trough.transform,
                Fill = fill.transform,
                FillRenderer = fill.GetComponent<Renderer>()
            };
        }

        private void UpdateHealthBar(UnitState u)
        {
            HealthBar bar;
            if (!_healthBars.TryGetValue(u.Id, out bar)) return;

            if (!u.IsAlive)
            {
                bar.Trough.gameObject.SetActive(false);
                bar.Fill.gameObject.SetActive(false);
                return;
            }

            bar.Trough.gameObject.SetActive(true);
            bar.Fill.gameObject.SetActive(true);

            float x = u.Position.X;
            float z = -u.Position.Y;
            float barY = BarYFor(u);
            bar.Trough.localPosition = new Vector3(x, barY, z);

            float fraction = u.Def.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)u.Hp / u.Def.MaxHp);
            float width = BarWidth * fraction;

            // Grown from the left edge rather than the centre, so a bar at 20%
            // sits where the eye expects it instead of floating in the middle.
            bar.Fill.localScale = new Vector3(width, 0.06f, BarHeight * 0.7f);
            bar.Fill.localPosition = new Vector3(x - (BarWidth - width) * 0.5f, barY + 0.02f, z);

            // Green -> amber -> red. The universal convention, and the only thing
            // on the board that is about a NUMBER rather than an identity, so it
            // deliberately does not use either faction's palette.
            Color c = fraction > 0.5f
                ? Color.Lerp(BarMid, BarHigh, (fraction - 0.5f) * 2f)
                : Color.Lerp(BarLow, BarMid, fraction * 2f);
            Paint(bar.FillRenderer, c);
        }

        private void Paint(Renderer r, Color c)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_colorProperty == 0) _colorProperty = Shader.PropertyToID("_BaseColor");
            r.GetPropertyBlock(_block);
            _block.SetColor(_colorProperty, c);
            r.SetPropertyBlock(_block);
        }

        /// <summary>The editor draws the same tile in the same colour. One list, not two.</summary>
        private static Color TerrainColor(TerrainDef terrain) => PrototypeVisuals.TileColor(terrain);

        private void SetUpCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }

            cam.orthographic = true;

            // Fit both axes with a margin, so a wider map does not get cropped and a
            // small one is not left tiny in the middle of the screen.
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.6f;
            float halfHeight = _map.Height * 0.5f;
            float halfWidthAsHeight = _map.Width * 0.5f / Mathf.Max(0.1f, aspect);
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidthAsHeight) + 1.5f;
            cam.transform.position = new Vector3((_map.Width - 1) * 0.5f, 20f, -(_map.Height - 1) * 0.5f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            }
        }
    }
}
