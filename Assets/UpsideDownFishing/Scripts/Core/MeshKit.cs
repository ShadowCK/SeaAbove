using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>Small procedural mesh helpers (vertex coloured, flat-ish cartoon look).</summary>
    public class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetColors(c);
            if (uv.Count == v.Count) m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            if (n.Count == v.Count) m.SetNormals(n); else m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public int Add(Vector3 p, Color col, Vector3? normal = null, Vector2? tex = null)
        {
            v.Add(p); c.Add(col);
            if (normal.HasValue) n.Add(normal.Value);
            if (tex.HasValue) uv.Add(tex.Value);
            return v.Count - 1;
        }

        public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }

        /// <summary>Double-sided flat triangle (for fins).</summary>
        public void Fin(Vector3 a, Vector3 b, Vector3 d, Color col)
        {
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            int i0 = Add(a, col, nn), i1 = Add(b, col, nn), i2 = Add(d, col, nn);
            Tri(i0, i1, i2);
            int j0 = Add(a, col, -nn), j1 = Add(b, col, -nn), j2 = Add(d, col, -nn);
            Tri(j0, j2, j1);
        }

        /// <summary>UV sphere / ellipsoid with a vertical colour gradient.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Color top, Color bottom, int rings = 8, int segs = 12)
        {
            int start = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= segs; s++)
                {
                    float th = 2 * Mathf.PI * s / segs;
                    var unit = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    var p = center + Vector3.Scale(unit, radii);
                    var nn = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized;
                    Add(p, Color.Lerp(bottom, top, unit.y * 0.5f + 0.5f), nn);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = start + r * (segs + 1) + s, b = a + segs + 1;
                    Tri(a, a + 1, b); Tri(a + 1, b + 1, b);
                }
        }

        /// <summary>Tube along a list of points with per-point radius.</summary>
        public void Tube(IList<Vector3> path, IList<float> radius, Color col, int sides = 8)
        {
            int start = v.Count;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 fwd = (i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                Vector3 side = Vector3.Cross(fwd, Mathf.Abs(fwd.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, fwd);
                for (int s = 0; s <= sides; s++)
                {
                    float a = 2 * Mathf.PI * s / sides;
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    Add(path[i] + dir * radius[i], col, dir);
                }
            }
            for (int i = 0; i < path.Count - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + i * (sides + 1) + s, b = a + sides + 1;
                    Tri(a, b, a + 1); Tri(a + 1, b, b + 1);
                }
        }

        public void Box(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                Vector3 fc = center + Vector3.Scale(d, h);
                Vector3 hu = Vector3.Scale(u, h), hw = Vector3.Scale(w, h);
                int i0 = Add(fc - hu - hw, col, d), i1 = Add(fc + hu - hw, col, d), i2 = Add(fc + hu + hw, col, d), i3 = Add(fc - hu + hw, col, d);
                Tri(i0, i1, i2); Tri(i0, i2, i3);
            }
        }

        public static GameObject Spawn(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
