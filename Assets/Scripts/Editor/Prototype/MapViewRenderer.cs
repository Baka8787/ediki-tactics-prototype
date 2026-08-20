using System.Collections.Generic;
using Ediki.Core;
using Ediki.Unity;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Turns a document into prototype geometry.
    ///
    /// The visual grammar it implements (and the reason each channel exists) is
    /// documented on PrototypeVisuals. In short: COLOUR says which side, SHAPE
    /// says what it does, MARKERS say what state it is in, and TEXT says which
    /// one it is. No channel carries two meanings, which is what makes a board
    /// with no art still readable.
    /// </summary>
    public sealed class MapViewRenderer
    {
        public sealed class Options
        {
            public int SelectedSpawn = -1;
            public Coord? Hovered;
            public bool ShowStats;
            public bool ShowNames = true;
            public HashSet<Coord> ProblemCells;
        }

        public sealed class UnitLabel
        {
            public Vector2 Position;
            public string Tag;
            public string Name;
            public string Stats;
            public Color Tint;
            public bool Dimmed;

            /// <summary>Camera-space depth. Near labels win the fight for screen space.</summary>
            public float Depth;

            /// <summary>Terrain stands between this unit and the eye.</summary>
            public bool Occluded;

            /// <summary>Always drawn, occluded or not — you must be able to see what you picked.</summary>
            public bool Selected;
        }

        public readonly List<UnitLabel> Labels = new List<UnitLabel>();

        private const float TileSize = 0.94f;
        private const float TileBottom = -0.10f;

        // ---------------------------------------------------------- the palette

        private static readonly Color GroundPlane = new Color(0.10f, 0.11f, 0.13f);

        private static readonly Color PlayerBody = new Color(0.30f, 0.55f, 0.88f);
        private static readonly Color EnemyBody = new Color(0.84f, 0.29f, 0.26f);
        private static readonly Color ObjectiveGold = new Color(0.92f, 0.74f, 0.24f);
        private static readonly Color UnknownUnit = new Color(0.45f, 0.45f, 0.48f);

        private static readonly Color SelectRing = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color HoverTint = new Color(1f, 0.95f, 0.55f, 0.35f);
        private static readonly Color DangerRing = new Color(0.95f, 0.30f, 0.22f, 0.85f);
        private static readonly Color ProblemRing = new Color(1f, 0.25f, 0.25f, 0.95f);
        private static readonly Color ReachMarker = new Color(0.35f, 0.90f, 0.60f, 0.90f);

        public void Build(SolidRenderer gl, EncounterDocument doc, TerrainCatalog terrain,
                          UnitCatalog units, MapViewCamera camera, Options options)
        {
            gl.Clear();
            Labels.Clear();
            Options o = options ?? new Options();

            // The dark backing that turns the gaps between tiles into grid lines,
            // emitted PER CELL rather than as one slab across the whole map.
            //
            // This is the occlusion bug at oblique angles. The renderer sorts by
            // a face's AVERAGE depth, which is a good key only while every face
            // is about the same size. A single map-wide quad averages to the
            // centre of the board, so every tile beyond the centre sorted as
            // farther than it and got painted over — the far half of a tilted map
            // disappeared under its own backing. Cell-sized pieces make the key
            // honest again, and cost one quad per cell.
            for (int y = 0; y < doc.Height; y++)
                for (int x = 0; x < doc.Width; x++)
                    gl.AddGroundQuad(x, -y, 1.04f, 1.04f, TileBottom - 0.04f, GroundPlane);

            for (int y = 0; y < doc.Height; y++)
                for (int x = 0; x < doc.Width; x++)
                    BuildTile(gl, doc, terrain, x, y, o);

            BuildObjectiveMarker(gl, doc);

            for (int i = 0; i < doc.Spawns.Count; i++)
                BuildUnit(gl, doc, terrain, units, camera, i, o);
        }

        // ----------------------------------------------------------------- tiles

        private static void BuildTile(SolidRenderer gl, EncounterDocument doc, TerrainCatalog terrain,
                                      int x, int y, Options o)
        {
            int idx = doc.TerrainAt(x, y);
            TerrainDef def = idx >= 0 && idx < terrain.Count ? terrain[idx] : null;
            TileStyle style = PrototypeVisuals.StyleOf(def);

            float cx = x;
            float cz = -y;
            float top = PrototypeVisuals.TileTopHeight(style);

            Color face = TileColor(def, style);
            Color side = face * 0.66f;
            side.a = face.a;

            if (style == TileStyle.Chasm)
            {
                // A pit has to be a hole you can see INTO, or it reads as a dark
                // tile and the one terrain that kills things looks like decoration.
                gl.AddGroundQuad(cx, cz, TileSize, TileSize, top, new Color(0.06f, 0.06f, 0.09f));

                float rim = PrototypeVisuals.TileTopHeight(TileStyle.Normal);
                Color wall = new Color(0.16f, 0.16f, 0.21f);
                float h = TileSize * 0.5f;

                gl.AddQuad(new Vector3(cx - h, rim, cz - h), new Vector3(cx - h, top, cz - h),
                           new Vector3(cx + h, top, cz - h), new Vector3(cx + h, rim, cz - h), wall, true);
                gl.AddQuad(new Vector3(cx - h, rim, cz + h), new Vector3(cx - h, top, cz + h),
                           new Vector3(cx + h, top, cz + h), new Vector3(cx + h, rim, cz + h), wall, true);
                gl.AddQuad(new Vector3(cx - h, rim, cz - h), new Vector3(cx - h, top, cz - h),
                           new Vector3(cx - h, top, cz + h), new Vector3(cx - h, rim, cz + h), wall, true);
                gl.AddQuad(new Vector3(cx + h, rim, cz - h), new Vector3(cx + h, top, cz - h),
                           new Vector3(cx + h, top, cz + h), new Vector3(cx + h, rim, cz + h), wall, true);

                gl.AddRing(cx, cz, 0.40f, 0.47f, rim + 0.01f, DangerRing, 20, 2, 2);
            }
            else
            {
                gl.AddBox(cx, cz, TileSize, TileSize, TileBottom, top, face, side);

                if (style == TileStyle.Swamp)
                {
                    // Three ripples. A swamp is the one terrain whose whole point
                    // is a NUMBER (it costs 3), so it gets a texture cue rather
                    // than only a shade — shade alone is what forest already uses.
                    Color ripple = face * 1.45f;
                    for (int i = 0; i < 3; i++)
                        gl.AddGroundQuad(cx, cz - 0.26f + i * 0.26f, TileSize * 0.72f, 0.06f,
                                         top + 0.012f, ripple);
                }
                else if (style == TileStyle.Rough)
                {
                    Color cap = face * 1.30f;
                    gl.AddGroundQuad(cx, cz, TileSize * 0.44f, TileSize * 0.44f, top + 0.012f, cap);
                }
            }

            if (o.Hovered.HasValue && o.Hovered.Value.X == x && o.Hovered.Value.Y == y)
                gl.AddGroundQuad(cx, cz, TileSize, TileSize, top + 0.02f, HoverTint);

            if (o.ProblemCells != null && o.ProblemCells.Contains(new Coord(x, y)))
                gl.AddRing(cx, cz, 0.42f, 0.50f, top + 0.03f, ProblemRing);
        }

        private static Color TileColor(TerrainDef def, TileStyle style)
        {
            if (def != null)
            {
                // Named terrain gets a hand-picked hue so the shipped maps look
                // the way the team already reads them. Anything terrain.txt adds
                // later still gets a sensible colour from its style.
                switch (def.Name)
                {
                    case "Open": return new Color(0.44f, 0.52f, 0.38f);
                    case "Road": return new Color(0.60f, 0.56f, 0.46f);
                    case "Forest": return new Color(0.23f, 0.42f, 0.26f);
                    case "Highland": return new Color(0.56f, 0.47f, 0.33f);
                    case "Mire": return new Color(0.32f, 0.35f, 0.26f);
                    case "Blocking": return new Color(0.20f, 0.21f, 0.25f);
                }
            }

            switch (style)
            {
                case TileStyle.Obstacle: return new Color(0.20f, 0.21f, 0.25f);
                case TileStyle.Swamp: return new Color(0.32f, 0.35f, 0.26f);
                case TileStyle.Rough: return new Color(0.34f, 0.44f, 0.30f);
                case TileStyle.Chasm: return new Color(0.08f, 0.08f, 0.11f);
                default: return new Color(0.46f, 0.50f, 0.44f);
            }
        }

        // ------------------------------------------------------------- objective

        private static void BuildObjectiveMarker(SolidRenderer gl, EncounterDocument doc)
        {
            if (doc.ObjectiveKind != ObjectiveKind.Reach) return;
            if (!doc.Contains(doc.ObjectiveTarget)) return;

            float cx = doc.ObjectiveTarget.X;
            float cz = -doc.ObjectiveTarget.Y;
            gl.AddRing(cx, cz, 0.26f, 0.40f, 0.30f, ReachMarker);
            gl.AddRing(cx, cz, 0.06f, 0.15f, 0.30f, ReachMarker);
        }

        // ----------------------------------------------------------------- units

        private void BuildUnit(SolidRenderer gl, EncounterDocument doc, TerrainCatalog terrain,
                               UnitCatalog units, MapViewCamera camera, int index, Options o)
        {
            SpawnEntry spawn = doc.Spawns[index];
            if (!doc.Contains(spawn.Position)) return;

            int terrainIndex = doc.TerrainAt(spawn.Position);
            TerrainDef tile = terrainIndex >= 0 && terrainIndex < terrain.Count ? terrain[terrainIndex] : null;
            float ground = PrototypeVisuals.TileTopHeight(PrototypeVisuals.StyleOf(tile));

            float cx = spawn.Position.X;
            float cz = -spawn.Position.Y;

            UnitDef def;
            bool known = units.TryGet(spawn.UnitId, out def);

            UnitArchetype archetype = spawn.HasPendingStats
                ? PrototypeVisuals.ArchetypeOf(spawn.PendingStats.ToDef("preview"))
                : PrototypeVisuals.ArchetypeOf(known ? def : null);

            Color body = BodyColor(spawn, known);

            // A reinforcement is not on the board yet, so it is drawn as a ghost:
            // same shape and colour (you must still be able to tell WHAT arrives),
            // half transparent, on a dashed ring rather than a solid footprint.
            bool ghost = spawn.IsReinforcement;
            if (ghost) body.a = 0.45f;

            bool selected = index == o.SelectedSpawn;
            float height = spawn.IsObjectiveTarget ? 0.86f : 0.58f;

            BuildBody(gl, archetype, cx, cz, ground, height, body);

            if (spawn.Protect || spawn.IsObjectiveTarget)
                gl.AddRing(cx, cz, 0.40f, 0.48f, ground + 0.015f, ObjectiveGold);

            if (ghost)
                gl.AddRing(cx, cz, 0.36f, 0.44f, ground + 0.02f, new Color(body.r, body.g, body.b, 0.9f), 24, 2, 2);

            if (selected)
            {
                gl.AddRing(cx, cz, 0.46f, 0.54f, ground + 0.03f, SelectRing);
                gl.AddRing(cx, cz, 0.56f, 0.60f, ground + 0.03f, new Color(1f, 1f, 1f, 0.35f));
            }

            if (!known && !spawn.HasPendingStats)
                gl.AddRing(cx, cz, 0.30f, 0.50f, ground + 0.04f, ProblemRing, 24, 1, 1);

            // Labels are GUI, not geometry, so they are not near-plane culled with
            // the rest of the scene. A unit behind the eye would otherwise get a
            // nameplate mirrored to the far side of the screen.
            Vector3 head = camera.WorldToView(new Vector3(cx, ground + height + 0.30f, cz));
            if (camera.Perspective && head.z < MapViewCamera.NearPlane) return;

            Vector2 window = camera.WorldToWindow(new Vector3(cx, ground + height + 0.30f, cz));

            string name = spawn.HasPendingStats && spawn.PendingStats != null
                ? spawn.PendingStats.DisplayName
                : (known ? def.DisplayName : spawn.UnitId + " ?");

            string stats = null;
            if (o.ShowStats)
            {
                UnitStatBlock s = spawn.HasPendingStats ? spawn.PendingStats
                                : (known ? UnitStatBlock.From(def) : null);
                if (s != null) stats = "HP " + s.MaxHp + "   AP " + s.MaxAp;
            }

            Labels.Add(new UnitLabel
            {
                Position = window,
                Tag = doc.TagOf(spawn) + (ghost ? "  T" + spawn.ArrivesOnTurn : ""),
                Name = o.ShowNames ? name : null,
                Stats = stats,
                Tint = spawn.Faction == Faction.Player ? PlayerBody : EnemyBody,
                Dimmed = ghost,
                Depth = head.z,
                Selected = selected,
                Occluded = IsHiddenByTerrain(doc, terrain, camera,
                                             new Vector3(cx, ground + height * 0.6f, cz), spawn.Position)
            });
        }

        /// <summary>
        /// Is there terrain between this unit's body and the eye?
        ///
        /// Nameplates are GUI, drawn after the board and with no depth buffer to
        /// test against, so a unit standing behind a wall kept its label floating
        /// on top of that wall. Marching the line back to the camera and asking
        /// each cell how tall it is answers the same question the depth buffer
        /// would, at a cost of a few dozen array reads per unit.
        ///
        /// Deliberately samples from the unit's MIDDLE rather than the top of its
        /// nameplate: the label belongs to the body, and a body more than half
        /// hidden is one whose label is in the way rather than informative.
        /// </summary>
        private static bool IsHiddenByTerrain(EncounterDocument doc, TerrainCatalog terrain,
                                              MapViewCamera camera, Vector3 from, Coord ownCell)
        {
            Vector3 direction = camera.Perspective
                ? (camera.Position - from)
                : -camera.Forward;

            float length = direction.magnitude;
            if (length < 1e-4f) return false;
            direction /= length;

            // A quarter of a cell: fine enough that a one-cell wall cannot be
            // stepped over, coarse enough to stay cheap.
            const float Step = 0.25f;
            const float MaxDistance = 60f;

            float travelled = Step;
            while (travelled < MaxDistance)
            {
                Vector3 p = from + direction * travelled;
                travelled += Step;

                int x = Mathf.RoundToInt(p.x);
                int y = Mathf.RoundToInt(-p.z);

                // Left the board: nothing further can be in the way.
                if (!doc.Contains(x, y))
                {
                    if (p.y > 2f) return false;
                    continue;
                }

                if (x == ownCell.X && y == ownCell.Y) continue;   // its own tile is not a wall

                int idx = doc.TerrainAt(x, y);
                if (idx < 0 || idx >= terrain.Count) continue;

                float top = PrototypeVisuals.TileTopHeight(PrototypeVisuals.StyleOf(terrain[idx]));
                if (p.y < top) return true;

                // Above everything the map can contain, and still climbing.
                if (p.y > 1.5f) return false;
            }

            return false;
        }

        private static Color BodyColor(SpawnEntry spawn, bool known)
        {
            if (!known && !spawn.HasPendingStats) return UnknownUnit;

            // The thing being defended is neither side's soldier — it is the
            // objective, and that is the only case where colour is allowed to
            // mean something other than faction.
            if (spawn.Protect) return ObjectiveGold;
            return spawn.Faction == Faction.Player ? PlayerBody : EnemyBody;
        }

        private static void BuildBody(SolidRenderer gl, UnitArchetype archetype,
                                      float cx, float cz, float ground, float height, Color body)
        {
            Color top = body * 1.25f;
            top.a = body.a;

            float y0 = ground + 0.02f;
            float y1 = ground + height;

            switch (archetype)
            {
                case UnitArchetype.Melee:
                    gl.AddPrism(cx, cz, 0.28f, y0, y1, 14, top, body);
                    break;

                case UnitArchetype.Ranged:
                    gl.AddPrism(cx, cz, 0.30f, y0, y1, 6, top, body, Mathf.PI / 6f);
                    break;

                case UnitArchetype.Heavy:
                    // Squat and wide: the widest footprint on the board, and the
                    // only one that is shorter than it is broad.
                    gl.AddBox(cx, cz, 0.62f, 0.62f, y0, y0 + (y1 - y0) * 0.88f, top, body);
                    break;

                case UnitArchetype.Mobile:
                    gl.AddCone(cx, cz, 0.32f, y0, y1 + 0.06f, 4, body, Mathf.PI / 4f);
                    break;

                case UnitArchetype.Support:
                    // A cross reads as "it helps" in every strategy game ever made,
                    // and it is the one silhouette nothing else here can be mistaken for.
                    gl.AddBox(cx, cz, 0.58f, 0.20f, y0, y0 + height * 0.62f, top, body);
                    gl.AddBox(cx, cz, 0.20f, 0.58f, y0, y0 + height * 0.62f, top, body);
                    break;

                default: // Prop
                    gl.AddBox(cx, cz, 0.66f, 0.66f, y0, y0 + 0.22f, top, body);
                    gl.AddBox(cx, cz, 0.34f, 0.34f, y0 + 0.22f, y0 + 0.40f, top, body);
                    break;
            }
        }
    }
}
