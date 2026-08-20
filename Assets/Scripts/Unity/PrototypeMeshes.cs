using System.Collections.Generic;
using UnityEngine;

namespace Ediki.Unity
{
    /// <summary>
    /// The two shapes Unity has no primitive for.
    ///
    /// The editor's map view draws six silhouettes; Unity's built-in primitives
    /// cover four of them (cube, wide cube, two crossed cubes, cylinder). A hex
    /// prism and a pyramid have to be built, and building them is what stops the
    /// game from collapsing 遠程 and 近戰 onto the same cylinder — which would
    /// throw away exactly the distinction the shape channel exists to carry.
    ///
    /// Built once and shared. Both are unit-sized (radius 0.5, height 1) so the
    /// caller scales them the same way it scales a primitive.
    /// </summary>
    public static class PrototypeMeshes
    {
        private static Mesh _hexPrism;
        private static Mesh _pyramid;

        /// <summary>Six-sided prism — the editor's 遠程 silhouette.</summary>
        public static Mesh HexPrism => _hexPrism != null ? _hexPrism : (_hexPrism = BuildPrism(6));

        /// <summary>Square pyramid — the editor's 機動 silhouette.</summary>
        public static Mesh Pyramid => _pyramid != null ? _pyramid : (_pyramid = BuildPyramid(4));

        private static Mesh BuildPrism(int sides)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Sides. Each face gets its own four vertices so the normals stay
            // flat — a shared ring would smooth the prism into a cylinder.
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = RingPoint(i, sides, 0.5f, -0.5f);
                Vector3 b = RingPoint(i + 1, sides, 0.5f, -0.5f);
                Vector3 c = RingPoint(i + 1, sides, 0.5f, 0.5f);
                Vector3 d = RingPoint(i, sides, 0.5f, 0.5f);

                int v = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
                triangles.Add(v); triangles.Add(v + 3); triangles.Add(v + 2);
            }

            AddCap(vertices, triangles, sides, 0.5f, true);
            AddCap(vertices, triangles, sides, -0.5f, false);

            return Finish(vertices, triangles, "Ediki Hex Prism");
        }

        private static Mesh BuildPyramid(int sides)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Rotated an eighth of a turn so a four-sided pyramid presents a
            // corner to the camera rather than a flat face — that is what makes
            // it read as a spike instead of a small box.
            const float Offset = Mathf.PI / 4f;
            Vector3 apex = new Vector3(0f, 0.5f, 0f);

            for (int i = 0; i < sides; i++)
            {
                Vector3 a = RingPoint(i, sides, 0.5f, -0.5f, Offset);
                Vector3 b = RingPoint(i + 1, sides, 0.5f, -0.5f, Offset);

                int v = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(apex);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
            }

            AddCap(vertices, triangles, sides, -0.5f, false, Offset);

            return Finish(vertices, triangles, "Ediki Pyramid");
        }

        private static void AddCap(List<Vector3> vertices, List<int> triangles,
                                   int sides, float y, bool facingUp, float offset = 0f)
        {
            int centre = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));

            int first = vertices.Count;
            for (int i = 0; i < sides; i++) vertices.Add(RingPoint(i, sides, 0.5f, y, offset));

            for (int i = 0; i < sides; i++)
            {
                int a = first + i;
                int b = first + (i + 1) % sides;

                if (facingUp) { triangles.Add(centre); triangles.Add(a); triangles.Add(b); }
                else { triangles.Add(centre); triangles.Add(b); triangles.Add(a); }
            }
        }

        private static Vector3 RingPoint(int i, int sides, float radius, float y, float offset = 0f)
        {
            float angle = offset + Mathf.PI * 2f * i / sides;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        private static Mesh Finish(List<Vector3> vertices, List<int> triangles, string name)
        {
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
