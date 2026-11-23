using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Tripo-generated fish models (Resources/UDF/Models/Fish/{species id}) re-oriented and
    /// scaled to the convention the game uses: head toward +Z, back toward +Y, origin at the
    /// body centre, length along Z = species length.
    ///
    /// Orientation: the longer horizontal axis becomes Z; the head is the thicker end (tails
    /// are thin). <see cref="Fix"/> holds per-species corrections where that guess is wrong.
    /// </summary>
    public static class FishModels
    {
        public class Entry { public Mesh mesh; public Material material; }

        /// <summary>Extra rotation applied after the automatic alignment (checked visually).</summary>
        public static readonly Dictionary<string, Quaternion> Fix = new Dictionary<string, Quaternion>
        {
            // generated standing on its edge: head +Y, wings along Z -> head +Z, wings along X
            { "starlight_ray", Quaternion.LookRotation(Vector3.right, Vector3.forward) },
            // generated in a slight dive (tail up): level it
            { "ribbon_eel", Quaternion.Euler(-20f, 0, 0) },
            // its fan tail is wider than the head, so the thick-end guess picks the tail
            { "mochi_fish", Quaternion.Euler(0, 180f, 0) },
        };

        static readonly Dictionary<string, Entry> s_Cache = new Dictionary<string, Entry>();

        public static Entry Get(FishSpecies sp)
        {
            if (s_Cache.TryGetValue(sp.id, out var e)) return e;
            s_Cache[sp.id] = e = Build(sp);
            return e;
        }

        static Entry Build(FishSpecies sp)
        {
            var prefab = Resources.Load<GameObject>("UDF/Models/Fish/" + sp.id);
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
            var src = mf.sharedMesh;

            // the FBX root itself carries the Z-up -> Y-up rotation (often on the same object as
            // the MeshFilter), so use the full asset-space matrix; position is re-centred below
            Matrix4x4 toRoot = mf.transform.localToWorldMatrix;
            var v = src.vertices; var n = src.normals; var tg = src.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = toRoot.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = toRoot.MultiplyVector(n[i]).normalized;
            for (int i = 0; i < tg.Length; i++) { var t3 = toRoot.MultiplyVector(tg[i]).normalized; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }

            // 1) long horizontal axis -> Z (rays are wider than long: keep their span on X)
            var b = Bounds(v);
            Quaternion q = Quaternion.identity;
            bool wide = sp.shape == BodyShape.Ray;
            if (!wide && b.size.x > b.size.z * 1.1f) q = Quaternion.Euler(0, 90, 0);
            Rotate(v, n, tg, q);
            b = Bounds(v);

            // 2) head = thicker (in X) end along Z
            if (!wide)
            {
                float front = 0, back = 0;
                foreach (var p in v)
                {
                    float t = (p.z - b.min.z) / Mathf.Max(b.size.z, 1e-5f);
                    float w = Mathf.Abs(p.x - b.center.x);
                    if (t > 0.7f) front = Mathf.Max(front, w);
                    if (t < 0.3f) back = Mathf.Max(back, w);
                }
                if (back > front) Rotate(v, n, tg, Quaternion.Euler(0, 180, 0));
            }

            // 3) manual corrections
            if (Fix.TryGetValue(sp.id, out var fix)) Rotate(v, n, tg, fix);

            // 4) centre + scale to species length
            b = Bounds(v);
            float len = wide ? Mathf.Max(b.size.x, b.size.z) : b.size.z;
            float s = sp.length / Mathf.Max(len, 1e-5f);
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) * s;

            var m = new Mesh { name = "Fish_" + sp.id };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v;
            if (n.Length == v.Length) m.normals = n;
            if (tg.Length == v.Length) m.tangents = tg;
            m.uv = src.uv;
            m.subMeshCount = 1;
            var tris = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++) tris.AddRange(src.GetTriangles(sm));
            m.SetTriangles(tris, 0);
            if (n.Length != v.Length) m.RecalculateNormals();
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return new Entry { mesh = m, material = mr.sharedMaterial };
        }

        static Bounds Bounds(Vector3[] v)
        {
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            return b;
        }

        static void Rotate(Vector3[] v, Vector3[] n, Vector4[] tg, Quaternion q)
        {
            if (q == Quaternion.identity) return;
            for (int i = 0; i < v.Length; i++) v[i] = q * v[i];
            for (int i = 0; i < n.Length; i++) n[i] = q * n[i];
            for (int i = 0; i < tg.Length; i++) { var t3 = q * (Vector3)tg[i]; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }
        }
    }
}
