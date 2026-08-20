using System;
using Ediki.Core;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// The map view's camera: an orbit rig with a perspective/orthographic
    /// switch, driven the way the Scene view and Blender's viewport are.
    ///
    /// It is a PIVOT rig, not a free-flying eye. Yaw, pitch and distance orbit a
    /// point on the board, and the navigation verbs move that point. That is the
    /// model both references actually use for object placement — Blender orbits
    /// the 3D cursor, the Scene view orbits the framed selection — and it is the
    /// one that keeps "the thing I am editing" on screen while you look at it
    /// from somewhere else.
    ///
    /// World layout matches BattleView exactly: cell (x, y) sits at world
    /// (x, height, -y), so y = 0 is the far edge and the two views cannot
    /// disagree about which way the map faces.
    ///
    /// The rotation is built from sin/cos by hand rather than from
    /// Quaternion.Euler, for two reasons. It is called once per vertex — several
    /// thousand times per repaint — and building a quaternion each time to
    /// immediately invert it is pure waste; and Quaternion.Euler is a native
    /// call, which makes anything that uses it impossible to test outside a
    /// running editor. This way the projection and its inverse are ordinary
    /// arithmetic that a plain console harness can check cell by cell.
    ///
    /// The matrix is Unity's Euler(pitch, yaw, 0), i.e. Ry(yaw) * Rx(pitch):
    ///
    ///     [  ca    sa*sp    sa*cp ]
    ///     [   0       cp      -sp ]
    ///     [ -sa    ca*sp    ca*cp ]
    ///
    /// with ca/sa from yaw and cp/sp from pitch. Column 2 is the view direction.
    /// </summary>
    public sealed class MapViewCamera
    {
        /// <summary>
        /// Below the horizon is allowed — you can duck under the board, exactly
        /// as in Blender — but not flat, because a view direction parallel to the
        /// ground has no ground intersection and picking would have nothing to
        /// return.
        /// </summary>
        public const float MinPitch = -85f;
        public const float MaxPitch = 89.9f;

        public const float MinOrthoZoom = 6f;
        public const float MaxOrthoZoom = 220f;

        public const float MinDistance = 1.5f;
        public const float MaxDistance = 400f;

        /// <summary>Nothing closer than this is drawn. Perspective only.</summary>
        public const float NearPlane = 0.08f;

        /// <summary>World point the rig orbits, and the point every gesture moves.</summary>
        public Vector3 Pivot = Vector3.zero;

        public float Yaw = 32f;
        public float Pitch = 50f;

        /// <summary>
        /// Perspective by default: this is a tool for placing things in a 3D
        /// scene, and depth cues are most of what tells a raised tile from a
        /// recessed one at a glance. Orthographic stays one keystroke away
        /// because it is the mode that measures — under it, ten cells look like
        /// ten cells wherever they are on screen.
        /// </summary>
        public bool Perspective = true;

        /// <summary>Vertical field of view in degrees. Perspective only.</summary>
        public float FieldOfView = 55f;

        /// <summary>Eye-to-pivot distance. Perspective only.</summary>
        public float Distance = 16f;

        /// <summary>Screen points per world unit. Orthographic only.</summary>
        public float OrthoZoom = 42f;

        private Rect _rect;

        // Trig cache. Rebuilt only when the angles actually move, so a repaint
        // that projects 20 000 vertices computes four sines in total.
        private float _cachedYaw = float.NaN;
        private float _cachedPitch = float.NaN;
        private float _ca, _sa, _cp, _sp;

        public void SetViewRect(Rect rect) => _rect = rect;

        private void Refresh()
        {
            if (_cachedYaw == Yaw && _cachedPitch == Pitch) return;

            double yaw = Yaw * Math.PI / 180.0;
            double pitch = Pitch * Math.PI / 180.0;

            _ca = (float)Math.Cos(yaw);
            _sa = (float)Math.Sin(yaw);
            _cp = (float)Math.Cos(pitch);
            _sp = (float)Math.Sin(pitch);

            _cachedYaw = Yaw;
            _cachedPitch = Pitch;
        }

        /// <summary>Direction the camera looks, in world space. Column 2 of the matrix.</summary>
        public Vector3 Forward
        {
            get { Refresh(); return new Vector3(_sa * _cp, -_sp, _ca * _cp); }
        }

        /// <summary>Camera right, in world space. Column 0.</summary>
        public Vector3 Right
        {
            get { Refresh(); return new Vector3(_ca, 0f, -_sa); }
        }

        /// <summary>Where the eye actually is. Only meaningful in perspective.</summary>
        public Vector3 Position => Pivot - Forward * Distance;

        /// <summary>World cell centre for a grid coordinate.</summary>
        public static Vector3 CellToWorld(int x, int y, float height = 0f) => new Vector3(x, height, -y);

        /// <summary>Screen points per world unit at the pivot. Drives label sizing and framing.</summary>
        private float ProjectionScale
        {
            get
            {
                if (!Perspective) return OrthoZoom;
                float halfFov = FieldOfView * 0.5f * Mathf.Deg2Rad;
                return _rect.height * 0.5f / Mathf.Max(0.01f, Mathf.Tan(halfFov));
            }
        }

        // ----------------------------------------------------------- projection

        /// <summary>
        /// World to view-rect-local screen point. Z carries camera-space depth
        /// (larger = farther) so the renderer can painter-sort without a second
        /// transform, and so it can reject what is behind the near plane.
        ///
        /// When z is under NearPlane in perspective the x/y are meaningless — the
        /// divide has blown up or flipped sign — so callers MUST check z first.
        /// SolidRenderer does.
        /// </summary>
        public Vector3 WorldToView(Vector3 world)
        {
            Refresh();

            Vector3 origin = Perspective ? Position : Pivot;

            float dx = world.x - origin.x;
            float dy = world.y - origin.y;
            float dz = world.z - origin.z;

            // Transpose of the matrix above: world -> camera.
            float lx = _ca * dx - _sa * dz;
            float ly = _sa * _sp * dx + _cp * dy + _ca * _sp * dz;
            float lz = _sa * _cp * dx - _sp * dy + _ca * _cp * dz;

            float scale;
            if (Perspective)
            {
                if (lz < NearPlane) return new Vector3(0f, 0f, lz);
                scale = ProjectionScale / lz;
            }
            else scale = OrthoZoom;

            return new Vector3(_rect.width * 0.5f + lx * scale,
                               _rect.height * 0.5f - ly * scale,
                               lz);
        }

        /// <summary>Same, but in window coordinates — for placing GUI labels.</summary>
        public Vector2 WorldToWindow(Vector3 world)
        {
            Vector3 v = WorldToView(world);
            return new Vector2(_rect.x + v.x, _rect.y + v.y);
        }

        /// <summary>
        /// Window point back onto the ground plane (world y = 0), as a ray cast.
        ///
        /// One code path for both projections, because they differ only in where
        /// the ray starts and which way it points: a perspective ray fans out
        /// from the eye, an orthographic one leaves the image plane along the
        /// view direction. Everything after that is the same plane intersection.
        ///
        /// Returns false when the ground is not in front of the camera at all —
        /// looking at the horizon, or up from underneath. Callers must treat that
        /// as "no cell", never as a clamp to the edge of the board.
        /// </summary>
        public bool TryWindowToGround(Vector2 windowPoint, out Vector3 ground)
        {
            Refresh();
            ground = Pivot;

            float scale = ProjectionScale;
            if (scale < 1e-4f) return false;

            float lx = (windowPoint.x - _rect.x - _rect.width * 0.5f) / scale;
            float ly = (_rect.y + _rect.height * 0.5f - windowPoint.y) / scale;

            Vector3 origin;
            Vector3 direction;

            if (Perspective)
            {
                origin = Position;
                direction = LocalToWorldVector(lx, ly, 1f);
            }
            else
            {
                origin = Pivot + LocalToWorldVector(lx, ly, 0f);
                direction = Forward;
            }

            if (Mathf.Abs(direction.y) < 1e-5f) return false;

            float t = -origin.y / direction.y;

            // t < 0 means "behind the ray origin", which is only a miss in
            // PERSPECTIVE — there the origin is the eye, and ground behind you is
            // genuinely not on screen.
            //
            // In orthographic there is no eye: the origin is a point on an image
            // plane through the pivot, chosen for convenience, and every cell
            // below that plane sits at a negative t. Rejecting those blanked the
            // bottom half of every tilted orthographic view.
            if (Perspective && t <= 0f) return false;
            if (t > 100000f || t < -100000f) return false;

            ground = new Vector3(origin.x + direction.x * t, 0f, origin.z + direction.z * t);
            return true;
        }

        /// <summary>Convenience for gestures: falls back to the pivot when the ray misses.</summary>
        public Vector3 WindowToGround(Vector2 windowPoint)
        {
            Vector3 ground;
            return TryWindowToGround(windowPoint, out ground) ? ground : Pivot;
        }

        /// <summary>Window point to grid cell, or null when it lands off the map.</summary>
        public Coord? WindowToCell(Vector2 windowPoint, int width, int height)
        {
            Vector3 g;
            if (!TryWindowToGround(windowPoint, out g)) return null;

            int x = Mathf.RoundToInt(g.x);
            int y = Mathf.RoundToInt(-g.z);
            if (x < 0 || x >= width || y < 0 || y >= height) return null;
            return new Coord(x, y);
        }

        private Vector3 LocalToWorldVector(float lx, float ly, float lz)
        {
            Refresh();
            return new Vector3(
                _ca * lx + _sa * _sp * ly + _sa * _cp * lz,
                _cp * ly - _sp * lz,
                -_sa * lx + _ca * _sp * ly + _ca * _cp * lz);
        }

        // ------------------------------------------------------------- gestures

        /// <summary>
        /// Drag-pan that keeps the world point under the cursor under the cursor.
        /// Computed from the ground plane rather than from a fixed pixels-per-unit
        /// factor, so panning feels identical at any zoom, tilt and projection.
        /// </summary>
        public void Pan(Vector2 fromWindow, Vector2 toWindow)
        {
            Vector3 a, b;
            if (!TryWindowToGround(fromWindow, out a)) return;
            if (!TryWindowToGround(toWindow, out b)) return;
            Pivot += a - b;
        }

        /// <summary>
        /// Wheel zoom. In perspective this dollies the eye along the view
        /// direction; in orthographic it changes the scale. Either way the world
        /// point under the cursor is put back where it was, which is what makes
        /// "point at a corner and roll the wheel" land on that corner.
        /// </summary>
        public void ZoomBy(float steps, Vector2 aroundWindowPoint)
        {
            Vector3 before;
            bool had = TryWindowToGround(aroundWindowPoint, out before);

            float factor = (float)Math.Exp(steps * 0.12f);
            if (Perspective) Distance = Mathf.Clamp(Distance * factor, MinDistance, MaxDistance);
            else OrthoZoom = Mathf.Clamp(OrthoZoom / factor, MinOrthoZoom, MaxOrthoZoom);

            if (!had) return;

            Vector3 after;
            if (TryWindowToGround(aroundWindowPoint, out after)) Pivot += before - after;
        }

        public void Orbit(Vector2 delta)
        {
            Yaw += delta.x * 0.4f;
            if (Yaw > 360f) Yaw -= 360f;
            if (Yaw < -360f) Yaw += 360f;
            Pitch = Mathf.Clamp(Pitch - delta.y * 0.3f, MinPitch, MaxPitch);
        }

        /// <summary>Drag-zoom, the Scene view's alt+right-drag. Positive pulls back.</summary>
        public void Dolly(float pixels)
        {
            ZoomBy(pixels * 0.03f, new Vector2(_rect.center.x, _rect.center.y));
        }

        /// <summary>
        /// WASD/QE flight while the orbit button is held, as in the Scene view.
        ///
        /// It moves the PIVOT, so what changes is where the rig is looking rather
        /// than only where the eye is — fly across the board and the next orbit
        /// turns around where you arrived, not around where you started.
        ///
        /// Speed scales with how far out you are, so one second of W crosses about
        /// the same fraction of the screen whether you are inspecting one cell or
        /// looking at the whole map.
        /// </summary>
        public void Fly(float right, float forward, float up, float seconds, bool fast)
        {
            if (right == 0f && forward == 0f && up == 0f) return;

            float reach = Perspective ? Distance : _rect.height / Mathf.Max(1f, OrthoZoom);
            float speed = Mathf.Clamp(reach, 4f, 60f) * (fast ? 2.6f : 0.9f) * seconds;

            Vector3 f = Forward;
            Vector3 r = Right;

            Pivot += (r * right + f * forward) * speed;
            Pivot += new Vector3(0f, up * speed, 0f);
        }

        // -------------------------------------------------------------- framing

        public void FocusMap(int width, int height)
        {
            Pivot = new Vector3((width - 1) * 0.5f, 0f, -(height - 1) * 0.5f);
            Frame(0.5f * Mathf.Sqrt(width * width + height * height) + 1f);
        }

        public void FocusCell(Coord c)
        {
            Pivot = CellToWorld(c.X, c.Y);
            Frame(3.2f);
        }

        /// <summary>
        /// Pulls back far enough for a sphere of <paramref name="radius"/> around
        /// the pivot to fit, in whichever projection is active.
        ///
        /// The orthographic case measures the ACTUAL projected extent of the
        /// bounding box rather than guessing from width and height: yaw mixes the
        /// two axes and pitch squashes one of them, so the on-screen box of a 24x8
        /// map at 32 degrees is not something a formula over 24 and 8 can predict.
        /// </summary>
        private void Frame(float radius)
        {
            if (_rect.width < 1f || _rect.height < 1f) return;

            if (Perspective)
            {
                float aspect = _rect.width / _rect.height;
                float halfV = FieldOfView * 0.5f * Mathf.Deg2Rad;
                float halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
                float half = Mathf.Max(0.05f, Mathf.Min(halfV, halfH));

                Distance = Mathf.Clamp(radius / Mathf.Sin(half) * 1.05f, MinDistance, MaxDistance);
                return;
            }

            float saved = OrthoZoom;
            OrthoZoom = 1f;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = Pivot + new Vector3(
                    (i & 1) == 0 ? -radius : radius,
                    (i & 2) == 0 ? 0f : 1f,
                    (i & 4) == 0 ? -radius : radius);

                Vector3 v = WorldToView(corner);
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            OrthoZoom = saved;

            float spanX = Mathf.Max(0.001f, maxX - minX) + 1.5f;
            float spanY = Mathf.Max(0.001f, maxY - minY) + 2f;

            OrthoZoom = Mathf.Clamp(Mathf.Min(_rect.width / spanX, _rect.height / spanY),
                                    MinOrthoZoom, MaxOrthoZoom);
        }

        // --------------------------------------------------------------- presets

        /// <summary>Straight down, orthographic. The LAYOUT view — distances read true in cells.</summary>
        public void TopView(int width, int height)
        {
            Yaw = 0f;
            Pitch = MaxPitch;
            Perspective = false;
            FocusMap(width, height);
        }

        /// <summary>Tilted three-quarter, perspective. The READING view — height becomes visible.</summary>
        public void TacticalView(int width, int height)
        {
            Yaw = 32f;
            Pitch = 50f;
            Perspective = true;
            FocusMap(width, height);
        }

        /// <summary>Blender's numpad 1 / 3: look along an axis, near eye level.</summary>
        public void AxisView(int width, int height, bool fromSide)
        {
            Yaw = fromSide ? 90f : 0f;
            Pitch = 14f;
            FocusMap(width, height);
        }

        /// <summary>
        /// Flips projection without moving the picture: the ortho scale and the
        /// perspective distance are matched so the pivot stays the same size on
        /// screen. Toggling then reads as a change of PROJECTION rather than as
        /// the camera jumping somewhere else.
        /// </summary>
        public void TogglePerspective()
        {
            if (_rect.height < 1f) { Perspective = !Perspective; return; }

            float halfFov = FieldOfView * 0.5f * Mathf.Deg2Rad;
            float perspectiveScale = _rect.height * 0.5f / Mathf.Max(0.01f, Mathf.Tan(halfFov));

            if (Perspective)
            {
                OrthoZoom = Mathf.Clamp(perspectiveScale / Mathf.Max(0.01f, Distance),
                                        MinOrthoZoom, MaxOrthoZoom);
                Perspective = false;
            }
            else
            {
                Distance = Mathf.Clamp(perspectiveScale / Mathf.Max(0.01f, OrthoZoom),
                                       MinDistance, MaxDistance);
                Perspective = true;
            }
        }

        public string Describe()
        {
            return (Perspective ? "透視  距離 " + Mathf.RoundToInt(Distance)
                                : "正交  縮放 " + Mathf.RoundToInt(OrthoZoom))
                 + "    角度 " + Mathf.RoundToInt(Pitch) + "° / " + Mathf.RoundToInt(Yaw) + "°";
        }
    }
}
