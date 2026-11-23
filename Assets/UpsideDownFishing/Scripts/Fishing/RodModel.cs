using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Loads the scanned/generated fishing-rod model (Resources/UDF/Models/FishingRod, generated
    /// with Tripo) and turns it into a rigid "butt section": rear grip + reel seat + spinning reel.
    ///
    /// AI-generated models are normalised into a unit cube, so the long thin blank comes out
    /// squashed. We keep only the detailed part (from the butt up to just past the reel) and the
    /// rod blank is generated procedurally from <see cref="cutPoint"/> to the tip (so it can bend).
    ///
    /// Orientation is detected from the geometry, so the model can be swapped without code changes:
    ///   * long axis   = largest bounds extent
    ///   * rod centre  = centroid of the thin end slices
    ///   * reel        = vertices far from the axis; its side defines "down"
    ///   * butt end    = the end nearer to the reel
    /// Output frame: +Z toward the tip, -Y toward the reel, origin on the axis at the reel.
    /// </summary>
    public class RodModel
    {
        public Mesh mesh;
        public Material[] materials;
        /// <summary>Where the procedural blank starts (on the axis, in model space).</summary>
        public Vector3 cutPoint;
        /// <summary>Radius of the model's rod at the cut, so the blank joins seamlessly.</summary>
        public float cutRadius;
        /// <summary>Side plate of the reel where the crank shaft comes out (model space).</summary>
        public Vector3 crankPivot;
        /// <summary>-1 = crank on the left (+X is right), +1 = right.</summary>
        public float crankSide = -1f;
        /// <summary>How far the reel hangs below the rod axis (m).</summary>
        public float reelDepth;

        const string ResourcePath = "UDF/Models/FishingRod";
        static RodModel s_Cached;
        static bool s_Tried;

        /// <param name="handleLength">Real-world distance from the butt cap to the reel (m).</param>
        public static RodModel Load(float handleLength = 0.2f)
        {
            if (s_Tried) return s_Cached;
            s_Tried = true;

            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null) return null;
            var src = mf.sharedMesh;
            if (!src.isReadable)
            {
                Debug.LogWarning("[UDF] FishingRod model mesh is not readable; enable Read/Write in its import settings.");
                return null;
            }

            s_Cached = Process(src, handleLength);
            if (s_Cached != null) s_Cached.materials = mr.sharedMaterials;
            return s_Cached;
        }

        static RodModel Process(Mesh src, float handleLength)
        {
            var v = src.vertices;
            var nrm = src.normals;
            var tan = src.tangents;
            var uv = src.uv;
            if (v.Length == 0) return null;

            // ---- long axis
            var b = src.bounds;
            int ax = b.extents.x >= b.extents.y && b.extents.x >= b.extents.z ? 0 : (b.extents.y >= b.extents.z ? 1 : 2);
            Vector3 U = Vector3.zero; U[ax] = 1;
            Vector3 P1 = Vector3.zero; P1[(ax + 1) % 3] = 1;
            Vector3 P2 = Vector3.zero; P2[(ax + 2) % 3] = 1;
            float min = b.min[ax], len = b.size[ax];

            // ---- slice statistics
            const int N = 40;
            var cnt = new int[N];
            var cen = new Vector2[N];
            for (int i = 0; i < v.Length; i++)
            {
                int s = Mathf.Clamp((int)((v[i][ax] - min) / len * N), 0, N - 1);
                cnt[s]++;
                cen[s] += new Vector2(Vector3.Dot(v[i], P1), Vector3.Dot(v[i], P2));
            }
            int first = System.Array.FindIndex(cnt, c => c > 0), last = System.Array.FindLastIndex(cnt, c => c > 0);
            Vector2 axisC = (cen[first] / cnt[first] + cen[last] / cnt[last]) * 0.5f;

            var r = new float[v.Length];
            var maxR = new float[N];
            for (int i = 0; i < v.Length; i++)
            {
                r[i] = (new Vector2(Vector3.Dot(v[i], P1), Vector3.Dot(v[i], P2)) - axisC).magnitude;
                int s = Mathf.Clamp((int)((v[i][ax] - min) / len * N), 0, N - 1);
                maxR[s] = Mathf.Max(maxR[s], r[i]);
            }
            var nonEmpty = new List<float>();
            for (int s = 0; s < N; s++) if (cnt[s] > 0) nonEmpty.Add(maxR[s]);
            nonEmpty.Sort();
            float rodR = nonEmpty[Mathf.Min(nonEmpty.Count - 1, nonEmpty.Count / 4)];   // a thin-ish slice
            float reelThreshold = rodR * 2.2f;

            // ---- reel = far-from-axis vertices
            double reelAlong = 0; Vector2 reelOff = Vector2.zero; int reelN = 0;
            for (int i = 0; i < v.Length; i++)
                if (r[i] > reelThreshold)
                {
                    reelAlong += v[i][ax];
                    reelOff += new Vector2(Vector3.Dot(v[i], P1), Vector3.Dot(v[i], P2)) - axisC;
                    reelN++;
                }
            if (reelN < 20)
            {
                Debug.LogWarning("[UDF] FishingRod model: could not find a reel; using the procedural rod.");
                return null;
            }
            float reelPos = (float)(reelAlong / reelN);
            bool buttAtMax = (b.max[ax] - reelPos) < (reelPos - min);
            float tipSign = buttAtMax ? -1f : 1f;
            float buttPos = buttAtMax ? b.max[ax] : min;

            float reelFront = float.NegativeInfinity;     // furthest reel vertex toward the tip (signed)
            for (int i = 0; i < v.Length; i++)
                if (r[i] > reelThreshold) reelFront = Mathf.Max(reelFront, v[i][ax] * tipSign);
            float cut = reelFront + 0.05f * len;          // signed (tipSign) along-axis cut position

            // rod radius just behind the cut (ignoring reel parts)
            float cutR = 0;
            for (int i = 0; i < v.Length; i++)
            {
                float a = v[i][ax] * tipSign;
                if (a <= cut && a > cut - 0.08f * len && r[i] < reelThreshold) cutR = Mathf.Max(cutR, r[i]);
            }
            if (cutR <= 0) cutR = rodR;

            // ---- output frame
            Vector3 fwd = U * tipSign;
            Vector3 down = (P1 * reelOff.x + P2 * reelOff.y).normalized;
            Vector3 up = -down;
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 origin = U * reelPos + P1 * axisC.x + P2 * axisC.y;
            float scale = handleLength / Mathf.Max(Mathf.Abs(reelPos - buttPos), 1e-4f);
            Vector3 Map(Vector3 p) { p -= origin; return new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, fwd)) * scale; }
            Vector3 Rot(Vector3 d) => new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd));

            // ---- crop: drop every triangle that reaches past the cut
            var remap = new int[v.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outT = new List<Vector4>(); var outUV = new List<Vector2>();
            bool hasN = nrm.Length == v.Length, hasT = tan.Length == v.Length, hasUV = uv.Length == v.Length;
            var subs = new List<List<int>>();
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tris = src.GetTriangles(sm);
                var keep = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], c1 = tris[t + 1], c2 = tris[t + 2];
                    if (v[a][ax] * tipSign > cut || v[c1][ax] * tipSign > cut || v[c2][ax] * tipSign > cut) continue;
                    foreach (int idx in new[] { a, c1, c2 })
                    {
                        if (remap[idx] < 0)
                        {
                            remap[idx] = outV.Count;
                            outV.Add(Map(v[idx]));
                            if (hasN) outN.Add(Rot(nrm[idx]));
                            if (hasT) { var tv = Rot(tan[idx]); outT.Add(new Vector4(tv.x, tv.y, tv.z, tan[idx].w)); }
                            if (hasUV) outUV.Add(uv[idx]);
                        }
                        keep.Add(remap[idx]);
                    }
                }
                subs.Add(keep);
            }

            // ---- reel crank: strip the scanned handle (arm + knob sticking out of the body)
            // so an animated, grabbable one can replace it
            float reelBottom = 0;
            foreach (var p in outV) reelBottom = Mathf.Min(reelBottom, p.y);
            var bodyAbsX = new List<float>();
            Vector3 bodyC = Vector3.zero; int bodyN = 0;
            foreach (var p in outV) if (p.y < reelBottom * 0.35f) bodyAbsX.Add(Mathf.Abs(p.x));
            bodyAbsX.Sort();
            float bodyHalf = bodyAbsX.Count > 0 ? bodyAbsX[(int)(bodyAbsX.Count * 0.95f)] : 0.03f * handleLength / 0.2f;
            float cutX = bodyHalf * 1.25f;
            foreach (var p in outV) if (p.y < reelBottom * 0.35f && Mathf.Abs(p.x) < bodyHalf) { bodyC += p; bodyN++; }
            bodyC /= Mathf.Max(bodyN, 1);
            int removedL = 0, removedR = 0;
            for (int sm = 0; sm < subs.Count; sm++)
            {
                var src2 = subs[sm];
                var keep2 = new List<int>(src2.Count);
                for (int t = 0; t < src2.Count; t += 3)
                {
                    bool crank = false;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = outV[src2[t + j]];
                        if (p.y < reelBottom * 0.2f && Mathf.Abs(p.x) > cutX) { crank = true; if (p.x < 0) removedL++; else removedR++; }
                    }
                    if (!crank) { keep2.Add(src2[t]); keep2.Add(src2[t + 1]); keep2.Add(src2[t + 2]); }
                }
                subs[sm] = keep2;
            }
            float crankSide = removedR > removedL ? 1f : -1f;   // default: left side (right-handed angler)

            var m = new Mesh { name = "FishingRodButt" };
            if (outV.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(outV);
            if (hasN) m.SetNormals(outN);
            if (hasT) m.SetTangents(outT);
            if (hasUV) m.SetUVs(0, outUV);
            m.subMeshCount = subs.Count;
            for (int sm = 0; sm < subs.Count; sm++) m.SetTriangles(subs[sm], sm);
            if (!hasN) m.RecalculateNormals();
            if (!hasT && hasUV) m.RecalculateTangents();
            m.RecalculateBounds();
            m.UploadMeshData(true);

            // join the blank exactly where the kept rod geometry ends (not at the cut plane,
            // which may lie inside a long removed cylinder)
            float rLimit = cutR * scale * 1.25f;
            float frontZ = float.NegativeInfinity;
            foreach (var p in outV)
                if (new Vector2(p.x, p.y).magnitude < rLimit) frontZ = Mathf.Max(frontZ, p.z);
            float endR = 0;
            foreach (var p in outV)
            {
                float rr = new Vector2(p.x, p.y).magnitude;
                if (rr < rLimit && p.z > frontZ - 0.015f) endR = Mathf.Max(endR, rr);
            }
            if (float.IsInfinity(frontZ)) frontZ = (cut - reelPos * tipSign) * scale;
            return new RodModel
            {
                mesh = m,
                cutPoint = new Vector3(0, 0, frontZ - 0.004f),
                cutRadius = endR > 0 ? endR : cutR * scale,
                crankSide = crankSide,
                crankPivot = new Vector3(crankSide * bodyHalf, bodyC.y, bodyC.z),
                reelDepth = -reelBottom,
            };
        }
    }
}
