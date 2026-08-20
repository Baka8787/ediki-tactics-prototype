using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// A tiny software-projected solid renderer that draws into an EditorWindow.
    ///
    /// WHY NOT PreviewRenderUtility (which would give a real Camera and real
    /// meshes): the whole point of this view is a camera the planner can pan,
    /// orbit and zoom on a grid of a few hundred boxes, and a preview camera
    /// brings a render pipeline with it — under URP it needs its own additional
    /// camera data, its own lighting setup, and it behaves differently again if
    /// the project ever switches pipeline (which OD-15 explicitly leaves open).
    /// Projecting a few thousand quads by hand is about two hundred lines, has
    /// no pipeline dependency at all, and cannot break when the renderer does.
    ///
    /// Painter's algorithm, flat shading, no textures, no depth buffer. That is
    /// enough for boxes on a grid and it is all the brief asks for: this is
    /// prototype geometry, not a scene view.
    /// </summary>
    public sealed class SolidRenderer
    {
        private struct Face
        {
            public Vector3 A, B, C, D;
            public Vector3 Normal;
            public Color Color;
            public bool IsTriangle;
            public bool DoubleSided;
        }

        private readonly List<Face> _faces = new List<Face>(4096);
        private static Material _material;

        /// <summary>Fixed key light, in world space. Nothing here is lit by the scene.</summary>
        private static readonly Vector3 LightDirection = new Vector3(-0.35f, 0.86f, 0.37f).normalized;

        private const float Ambient = 0.55f;

        public void Clear() => _faces.Clear();

        // ------------------------------------------------------------ primitives

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, bool doubleSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            _faces.Add(new Face { A = a, B = b, C = c, D = d, Normal = n, Color = color, DoubleSided = doubleSided });
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, bool doubleSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            _faces.Add(new Face
            {
                A = a, B = b, C = c, D = c, Normal = n, Color = color,
                IsTriangle = true, DoubleSided = doubleSided
            });
        }

        /// <summary>Axis-aligned box given by its footprint and its top / bottom heights.</summary>
        public void AddBox(float cx, float cz, float sizeX, float sizeZ, float bottom, float top,
                           Color topColor, Color sideColor)
        {
            float x0 = cx - sizeX * 0.5f, x1 = cx + sizeX * 0.5f;
            float z0 = cz - sizeZ * 0.5f, z1 = cz + sizeZ * 0.5f;

            AddQuad(new Vector3(x0, top, z0), new Vector3(x0, top, z1),
                    new Vector3(x1, top, z1), new Vector3(x1, top, z0), topColor);

            AddQuad(new Vector3(x0, bottom, z1), new Vector3(x0, top, z1),
                    new Vector3(x1, top, z1), new Vector3(x1, bottom, z1), sideColor);
            AddQuad(new Vector3(x1, bottom, z0), new Vector3(x1, top, z0),
                    new Vector3(x0, top, z0), new Vector3(x0, bottom, z0), sideColor);
            AddQuad(new Vector3(x0, bottom, z0), new Vector3(x0, top, z0),
                    new Vector3(x0, top, z1), new Vector3(x0, bottom, z1), sideColor);
            AddQuad(new Vector3(x1, bottom, z1), new Vector3(x1, top, z1),
                    new Vector3(x1, top, z0), new Vector3(x1, bottom, z0), sideColor);
        }

        /// <summary>Regular prism: 12 sides reads as a cylinder, 6 as a hex, 4 as a rotated box.</summary>
        public void AddPrism(float cx, float cz, float radius, float bottom, float top, int sides,
                             Color topColor, Color sideColor, float angleOffset = 0f)
        {
            Vector3[] ring = Ring(cx, cz, radius, sides, angleOffset);

            for (int i = 0; i < sides; i++)
            {
                Vector3 p = ring[i];
                Vector3 q = ring[(i + 1) % sides];
                AddQuad(new Vector3(p.x, bottom, p.z), new Vector3(p.x, top, p.z),
                        new Vector3(q.x, top, q.z), new Vector3(q.x, bottom, q.z), sideColor);
            }

            Vector3 centre = new Vector3(cx, top, cz);
            for (int i = 0; i < sides; i++)
            {
                Vector3 p = ring[i];
                Vector3 q = ring[(i + 1) % sides];
                AddTriangle(centre, new Vector3(p.x, top, p.z), new Vector3(q.x, top, q.z), topColor);
            }
        }

        /// <summary>Cone / pyramid. 4 sides is a pyramid, 12 reads as a cone.</summary>
        public void AddCone(float cx, float cz, float radius, float bottom, float top, int sides,
                            Color color, float angleOffset = 0f)
        {
            Vector3[] ring = Ring(cx, cz, radius, sides, angleOffset);
            Vector3 apex = new Vector3(cx, top, cz);

            for (int i = 0; i < sides; i++)
            {
                Vector3 p = ring[i];
                Vector3 q = ring[(i + 1) % sides];
                AddTriangle(new Vector3(q.x, bottom, q.z), new Vector3(p.x, bottom, p.z), apex, color);
            }
        }

        /// <summary>Flat annulus lying on the ground — the marker every state cue is built from.</summary>
        public void AddRing(float cx, float cz, float innerRadius, float outerRadius, float y,
                            Color color, int sides = 24, int dashOn = 0, int dashOff = 0)
        {
            Vector3[] inner = Ring(cx, cz, innerRadius, sides, 0f);
            Vector3[] outer = Ring(cx, cz, outerRadius, sides, 0f);

            for (int i = 0; i < sides; i++)
            {
                if (dashOn > 0 && (i % (dashOn + dashOff)) >= dashOn) continue;

                int j = (i + 1) % sides;
                AddQuad(new Vector3(inner[i].x, y, inner[i].z), new Vector3(outer[i].x, y, outer[i].z),
                        new Vector3(outer[j].x, y, outer[j].z), new Vector3(inner[j].x, y, inner[j].z),
                        color, true);
            }
        }

        /// <summary>Flat square patch on the ground. Used for hover, selection fill and grid lines.</summary>
        public void AddGroundQuad(float cx, float cz, float sizeX, float sizeZ, float y, Color color)
        {
            float x0 = cx - sizeX * 0.5f, x1 = cx + sizeX * 0.5f;
            float z0 = cz - sizeZ * 0.5f, z1 = cz + sizeZ * 0.5f;
            AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1),
                    new Vector3(x1, y, z1), new Vector3(x1, y, z0), color, true);
        }

        private static Vector3[] Ring(float cx, float cz, float radius, int sides, float angleOffset)
        {
            Vector3[] ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = angleOffset + Mathf.PI * 2f * i / sides;
                ring[i] = new Vector3(cx + Mathf.Cos(a) * radius, 0f, cz + Mathf.Sin(a) * radius);
            }
            return ring;
        }

        // -------------------------------------------------------------- drawing

        /// <summary>
        /// Projects, sorts and draws everything queued so far.
        ///
        /// Must run inside OnGUI on a Repaint event. <paramref name="windowSize"/>
        /// is the whole window, because GL.Viewport addresses the render target
        /// and the map view is only part of it.
        /// </summary>
        public void Draw(MapViewCamera camera, Rect viewRect, Vector2 windowSize)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_faces.Count == 0) return;

            Material mat = ColorMaterial();
            if (mat == null) return;

            int count = _faces.Count;
            EnsureBuffers(count);

            Vector3 forward = camera.Forward;
            bool perspective = camera.Perspective;
            float near = MapViewCamera.NearPlane;
            int visible = 0;

            for (int i = 0; i < count; i++)
            {
                Face f = _faces[i];

                // Backface cull. Doubled-sided faces are the flat ground markers
                // and the chasm walls, which have to be visible from both sides.
                if (!f.DoubleSided && Vector3.Dot(f.Normal, forward) > -0.0001f) continue;

                Vector3 pa = camera.WorldToView(f.A);
                Vector3 pb = camera.WorldToView(f.B);
                Vector3 pc = camera.WorldToView(f.C);
                Vector3 pd = f.IsTriangle ? pc : camera.WorldToView(f.D);

                // Near-plane reject. In perspective the projection divides by
                // depth, so a vertex behind the eye comes back mirrored through
                // the centre of the screen and would smear a face across the
                // whole view. Whole faces are dropped rather than clipped: the
                // near plane is a fraction of a cell, so this only bites when the
                // camera is literally inside a tile, and proper clipping is a lot
                // of code for a case you fly out of immediately.
                if (perspective && (pa.z < near || pb.z < near || pc.z < near || pd.z < near)) continue;

                int v = visible * 4;
                _projected[v] = pa; _projected[v + 1] = pb; _projected[v + 2] = pc; _projected[v + 3] = pd;

                // Negated so an ascending sort puts the FAR faces first.
                _sortKeys[visible] = -(f.IsTriangle
                    ? (pa.z + pb.z + pc.z) / 3f
                    : (pa.z + pb.z + pc.z + pd.z) * 0.25f);
                _faceIndex[visible] = i;
                _slotIndex[visible] = visible;
                visible++;
            }

            if (visible == 0) return;
            System.Array.Sort(_sortKeys, _slotIndex, 0, visible);

            float ppp = EditorGUIUtility.pixelsPerPoint;
            Rect viewport = new Rect(viewRect.x * ppp,
                                     (windowSize.y - viewRect.yMax) * ppp,
                                     viewRect.width * ppp,
                                     viewRect.height * ppp);

            mat.SetPass(0);
            GL.PushMatrix();
            GL.Viewport(viewport);
            GL.LoadPixelMatrix(0, viewRect.width, viewRect.height, 0);
            GL.Begin(GL.TRIANGLES);

            for (int k = 0; k < visible; k++)
            {
                int slot = _slotIndex[k];
                Face f = _faces[_faceIndex[slot]];
                int v = slot * 4;

                GL.Color(Shade(f.Color, f.Normal));

                Emit(_projected[v], _projected[v + 1], _projected[v + 2]);
                if (!f.IsTriangle) Emit(_projected[v], _projected[v + 2], _projected[v + 3]);
            }

            GL.End();
            GL.PopMatrix();
            GL.Viewport(new Rect(0f, 0f, windowSize.x * ppp, windowSize.y * ppp));
        }

        // Reused across repaints. A 64x64 map is a few thousand faces and OnGUI
        // runs several times a frame, so allocating these per draw would make the
        // window the noisiest thing in the editor's GC profile.
        private Vector3[] _projected = new Vector3[0];
        private float[] _sortKeys = new float[0];
        private int[] _faceIndex = new int[0];
        private int[] _slotIndex = new int[0];

        private void EnsureBuffers(int faceCount)
        {
            if (_sortKeys.Length >= faceCount) return;

            int size = Mathf.NextPowerOfTwo(Mathf.Max(1024, faceCount));
            _projected = new Vector3[size * 4];
            _sortKeys = new float[size];
            _faceIndex = new int[size];
            _slotIndex = new int[size];
        }

        private static void Emit(Vector3 a, Vector3 b, Vector3 c)
        {
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
        }

        /// <summary>
        /// Flat lambert against one fixed light, floored at Ambient so a face
        /// turned away is dim rather than black. Alpha passes through untouched —
        /// the ghost bodies for reinforcements rely on it.
        /// </summary>
        private static Color Shade(Color c, Vector3 normal)
        {
            float lambert = Mathf.Max(0f, Vector3.Dot(normal, LightDirection));
            float k = Ambient + (1f - Ambient) * lambert;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        private static Material ColorMaterial()
        {
            if (_material != null) return _material;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return _material;
        }
    }
}
