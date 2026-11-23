using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    public static class Geometry
    {
        public static bool PointInPolygon(Vector2 p, IList<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }

        public static float DistanceToPolygonEdge(Vector2 p, IList<Vector2> poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                best = Mathf.Min(best, DistanceToSegment(p, poly[j], poly[i]));
            return best;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        public static float SignedArea(IList<Vector2> poly)
        {
            float a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].x * poly[i].y - poly[i].x * poly[j].y;
            return a * 0.5f;
        }

        /// <summary>Ensures counter-clockwise winding in the XY (=world XZ) plane.</summary>
        public static void MakeCCW(List<Vector2> poly)
        {
            if (SignedArea(poly) < 0) poly.Reverse();
        }

        /// <summary>Ear-clipping triangulation of a simple CCW polygon. Returns index triples.</summary>
        public static List<int> Triangulate(IList<Vector2> poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    if (Cross(poly[b] - poly[a], poly[c] - poly[b]) <= 1e-7f) continue; // reflex
                    bool contains = false;
                    foreach (int k in idx)
                    {
                        if (k == a || k == b || k == c) continue;
                        if (InTriangle(poly[k], poly[a], poly[b], poly[c])) { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(a); result.Add(b); result.Add(c);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate polygon, fall back to fan below
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            else if (idx.Count > 3)
                for (int i = 1; i < idx.Count - 1; i++) { result.Add(idx[0]); result.Add(idx[i]); result.Add(idx[i + 1]); }
            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            return d1 >= 0 && d2 >= 0 && d3 >= 0;
        }

        /// <summary>Inward offset of a CCW polygon (miter, clamped). Good enough for foam rims.</summary>
        public static List<Vector2> Inset(IList<Vector2> poly, float d)
        {
            var res = new List<Vector2>(poly.Count);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = poly[(i + n - 1) % n], cur = poly[i], next = poly[(i + 1) % n];
                Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
                Vector2 n0 = new Vector2(-e0.y, e0.x), n1 = new Vector2(-e1.y, e1.x); // left normals = inward for CCW
                Vector2 m = (n0 + n1).normalized;
                float denom = Mathf.Max(Vector2.Dot(m, n1), 0.35f);
                res.Add(cur + m * (d / denom));
            }
            return res;
        }
    }
}
