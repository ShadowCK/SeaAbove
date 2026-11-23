using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Tripo-generated bobber / lure (Resources/UDF/Models/Tackle/{name}) centred on their
    /// bounds and scaled so the largest extent equals the requested size. Y stays up.
    /// </summary>
    public static class TackleModels
    {
        public class Entry { public Mesh mesh; public Material material; }
        static readonly Dictionary<string, Entry> s_Cache = new Dictionary<string, Entry>();

        public static Entry Get(string name, float size)
        {
            string key = name + "@" + size;
            if (s_Cache.TryGetValue(key, out var e)) return e;
            s_Cache[key] = e = Build(name, size);
            return e;
        }

        public class Fitted { public Mesh mesh; public Material material; public Vector3 size; }

        /// <summary>
        /// Loads a readable Tripo model from Resources, bakes the FBX root rotation, optionally turns
        /// it 90° so its long horizontal side runs along X, and scales it uniformly to fit inside
        /// <paramref name="fit"/>. The origin is put at the centre of the bottom face.
        /// <paramref name="stretchXZ"/>: X and Z are scaled independently to fill the footprint exactly;
        /// Y then gets the mean of the two (capped by fit.y), or exactly fit.y with <paramref name="exactY"/>.
        /// <paramref name="readable"/> keeps the CPU copy of the mesh (for cutting / measuring it).
        /// </summary>
        public static Fitted LoadFitted(string resourcePath, Vector3 fit, bool longSideOnX = true,
                                        bool stretchXZ = false, bool exactY = false, bool readable = false)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
            var src = mf.sharedMesh;
            Matrix4x4 m4 = mf.transform.localToWorldMatrix;
            var v = src.vertices; var n = src.normals; var tg = src.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = m4.MultiplyPoint3x4(v[i]);
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            Quaternion turn = longSideOnX && b.size.z > b.size.x ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
            Matrix4x4 full = Matrix4x4.Rotate(turn) * m4;
            v = src.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = full.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = full.MultiplyVector(n[i]).normalized;
            for (int i = 0; i < tg.Length; i++) { var t3 = full.MultiplyVector(tg[i]).normalized; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }
            b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            float fx = fit.x / Mathf.Max(b.size.x, 1e-5f), fy = fit.y / Mathf.Max(b.size.y, 1e-5f), fz = fit.z / Mathf.Max(b.size.z, 1e-5f);
            Vector3 s = Vector3.one * Mathf.Min(fx, fy, fz);
            if (stretchXZ) s = new Vector3(fx, exactY ? fy : Mathf.Min(fy, (fx + fz) * 0.5f), fz);
            Vector3 origin = new Vector3(b.center.x, b.min.y, b.center.z);
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i] - origin, s);
            // non-uniform scale: normals take the inverse scale, tangents the scale
            Vector3 inv = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.Scale(n[i], inv).normalized;
            for (int i = 0; i < tg.Length; i++) { var t3 = Vector3.Scale(tg[i], s).normalized; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }

            var mesh = new Mesh { name = "Fitted_" + prefab.name };
            if (v.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            if (n.Length == v.Length) mesh.normals = n;
            if (tg.Length == v.Length) mesh.tangents = tg;
            mesh.uv = src.uv;
            var tris = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++) tris.AddRange(src.GetTriangles(sm));
            mesh.SetTriangles(tris, 0);
            if (n.Length != v.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(!readable);
            return new Fitted { mesh = mesh, material = mr.sharedMaterial, size = Vector3.Scale(b.size, s) };
        }

        static Entry Build(string name, float size)
        {
            var prefab = Resources.Load<GameObject>("UDF/Models/Tackle/" + name);
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
            var src = mf.sharedMesh;
            Matrix4x4 m4 = mf.transform.localToWorldMatrix;   // includes the FBX Z-up -> Y-up root rotation
            var v = src.vertices; var n = src.normals; var tg = src.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = m4.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = m4.MultiplyVector(n[i]).normalized;
            for (int i = 0; i < tg.Length; i++) { var t3 = m4.MultiplyVector(tg[i]).normalized; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            float s = size / Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-5f);
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) * s;

            var mesh = new Mesh { name = "Tackle_" + name };
            if (v.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            if (n.Length == v.Length) mesh.normals = n;
            if (tg.Length == v.Length) mesh.tangents = tg;
            mesh.uv = src.uv;
            var tris = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++) tris.AddRange(src.GetTriangles(sm));
            mesh.SetTriangles(tris, 0);
            if (n.Length != v.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return new Entry { mesh = mesh, material = mr.sharedMaterial };
        }
    }
}
