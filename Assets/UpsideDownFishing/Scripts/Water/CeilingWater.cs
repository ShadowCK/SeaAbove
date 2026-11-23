using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Turns the detected ceiling polygon into an upside-down sea whose SURFACE is the ceiling:
    /// a wavy water surface at ceiling height, and above it a deep sea that fades to blue (no
    /// visible sea floor), with kelp fronds reaching down out of the depths, bubbles drifting
    /// "up" (toward the floor) to the surface, caustics on the real walls and air bubbles in the room.
    /// </summary>
    public class CeilingWater : MonoBehaviour
    {
        [Tooltip("How far the sea extends above the ceiling. Deep enough that it fades to blue: no visible sea floor.")]
        public float depth = 3.2f;
        /// <summary>The bright band above the surface where fish, bubbles and particles live.</summary>
        public const float ShallowBand = 1.3f;
        [Tooltip("Surface sits slightly below the real ceiling so it never z-fights with it.")]
        public float surfaceOffset = 0.03f;

        public RoomModel Room { get; private set; }
        public float SurfaceY => Room.ceilingY - surfaceOffset;
        public float SeabedY => SurfaceY + depth;

        static readonly Vector4[] s_Ripples = new Vector4[4];
        static int s_RippleIndex;
        static readonly int RipplesId = Shader.PropertyToID("_UDF_Ripples");

        public void Build(RoomModel room)
        {
            Room = room;
            foreach (Transform c in transform) Destroy(c.gameObject);
            for (int i = 0; i < 4; i++) s_Ripples[i] = new Vector4(0, 0, 0, -100);
            Shader.SetGlobalVectorArray(RipplesId, s_Ripples);

            var surfaceMat = UDFAssets.New(UDFAssets.CeilingWater);
            MeshKit.Spawn("WaterSurface", BuildSurface(room.ceilingPolygon, SurfaceY), surfaceMat, transform);

            var volMat = UDFAssets.New(UDFAssets.WaterVolume);
            volMat.SetFloat("_SurfaceY", SurfaceY);
            volMat.SetFloat("_Depth", depth);
            MeshKit.Spawn("SeaVolume", BuildVolume(room.ceilingPolygon, SurfaceY, SeabedY), volMat, transform);
            SetSeaGlobals(volMat);

            BuildKelp(room);
            BuildBubbles(room);
            BuildMarineSnow(room);
            BuildWallCaustics(room);
            BuildAirBubbles(room);
        }

        /// <summary>Thin quads hugging the real walls below the water, lit by additive caustics.</summary>
        void BuildWallCaustics(RoomModel room)
        {
            var poly = room.ceilingPolygon;
            float top = SurfaceY + 0.01f;
            float bottom = Mathf.Max(room.floorY + 0.2f, SurfaceY - 1.4f);
            var k = new MeshKit();
            float along = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                Vector2 e = b - a; float len = e.magnitude;
                if (len < 1e-3f) continue;
                Vector2 inward = new Vector2(-e.y, e.x) / len;           // left normal of a CCW edge
                if (!room.Contains((a + b) * 0.5f + inward * 0.05f, 0f)) inward = -inward;
                Vector2 a2 = a + inward * 0.006f, b2 = b + inward * 0.006f;
                const int rows = 6;
                int s = k.v.Count;
                for (int r = 0; r <= rows; r++)
                {
                    float y = Mathf.Lerp(top, bottom, r / (float)rows);
                    k.Add(new Vector3(a2.x, y, a2.y), Color.white, null, new Vector2(along, SurfaceY - y));
                    k.Add(new Vector3(b2.x, y, b2.y), Color.white, null, new Vector2(along + len, SurfaceY - y));
                }
                for (int r = 0; r < rows; r++) { int i0 = s + r * 2; k.Tri(i0, i0 + 1, i0 + 3); k.Tri(i0, i0 + 3, i0 + 2); }
                along += len;
            }
            var go = MeshKit.Spawn("WallCaustics", k.Build("WallCaustics"), UDFAssets.New(UDFAssets.WallCaustics), transform);
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        /// <summary>Shared underwater fog/light (UDF_Underwater.hlsl) matches the sea volume.</summary>
        void SetSeaGlobals(Material volMat)
        {
            Shader.SetGlobalFloat("_UDF_WaterOn", 1f);
            Shader.SetGlobalFloat("_UDF_SurfaceY", SurfaceY);
            Shader.SetGlobalFloat("_UDF_SeaDepth", depth);
            Shader.SetGlobalVector("_UDF_DeepWater", volMat.HasProperty("_DeepWater") ? (Vector4)volMat.GetColor("_DeepWater") : new Vector4(0.015f, 0.1f, 0.17f, 1));
            Shader.SetGlobalVector("_UDF_Absorption", volMat.HasProperty("_Absorption") ? volMat.GetVector("_Absorption") : new Vector4(0.95f, 0.32f, 0.22f, 0));
            // sunlight enters through the surface, which is BELOW the upside-down sea
            Shader.SetGlobalVector("_UDF_SeaLightDir", new Vector3(0.25f, -1f, 0.15f).normalized);
            Shader.SetGlobalVector("_UDF_SeaLightColor", new Color(1f, 1f, 0.94f) * 1.45f);   // sunny shallow reef
        }

        void OnDestroy() => Shader.SetGlobalFloat("_UDF_WaterOn", 0f);

        /// <summary>Registers an expanding ripple ring on the surface.</summary>
        public static void AddRipple(Vector3 worldPos)
        {
            s_Ripples[s_RippleIndex] = new Vector4(worldPos.x, worldPos.y, worldPos.z, Time.timeSinceLevelLoad);
            s_RippleIndex = (s_RippleIndex + 1) % s_Ripples.Length;
            Shader.SetGlobalVectorArray(RipplesId, s_Ripples);
        }

        public bool IsInsideWaterXZ(Vector3 p, float margin = 0.05f) => Room.Contains(new Vector2(p.x, p.z), margin);

        // ------------------------------------------------------------------

        static Mesh BuildSurface(List<Vector2> poly, float y)
        {
            // Concentric inset rings so uv.y carries the true distance to the wall (foam rim).
            // dense near the wall so the rounded lip (shader) is smooth
            float[] distances = { 0f, 0.03f, 0.07f, 0.12f, 0.19f, 0.28f, 0.4f };
            var rings = new List<List<Vector2>>();
            foreach (var d in distances) rings.Add(d == 0 ? new List<Vector2>(poly) : Geometry.Inset(poly, d));

            var k = new MeshKit();
            int n = poly.Count;
            var ringStart = new List<int>();
            var wallDir = new List<Vector2>();
            for (int r = 0; r < rings.Count; r++)
            {
                ringStart.Add(k.v.Count);
                for (int i = 0; i < rings[r].Count; i++)
                {
                    var p = rings[r][i];
                    k.Add(new Vector3(p.x, y, p.y), Color.white, Vector3.down, new Vector2(0, distances[r]));
                    Vector2 toWall = r == 0 ? poly[i] - rings[1][i] : poly[i] - p;
                    wallDir.Add(toWall.sqrMagnitude > 1e-8f ? toWall.normalized : Vector2.zero);
                }
            }
            for (int r = 0; r < rings.Count - 1; r++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int a = ringStart[r] + i, b = ringStart[r] + j, c = ringStart[r + 1] + j, d = ringStart[r + 1] + i;
                    k.Tri(a, b, c); k.Tri(a, c, d);
                }
            var inner = rings[rings.Count - 1];
            var tris = Geometry.Triangulate(inner);
            int baseIdx = ringStart[rings.Count - 1];
            for (int i = 0; i < tris.Count; i += 3) k.Tri(baseIdx + tris[i], baseIdx + tris[i + 1], baseIdx + tris[i + 2]);
            // interior uv.y should read "far from the wall"
            for (int i = baseIdx; i < k.uv.Count; i++) k.uv[i] = new Vector2(0, 1f);

            // tessellate so the vertex shader's waves have geometry to move
            var verts = new List<Vector3>(k.v);
            var uvs = new List<Vector2>(k.uv);
            var idx = new List<int>(k.t);
            Refine(verts, uvs, wallDir, idx, 0.08f, 60000);
            var m = new Mesh { name = "CeilingWaterSurface" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.down;
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetUVs(1, wallDir);
            m.SetTriangles(idx, 0);
            // vertex waves + the rim lip move the surface; pad bounds so it is never culled early
            var bounds = m.bounds; bounds.Expand(new Vector3(0.1f, 0.4f, 0.1f)); m.bounds = bounds;
            return m;
        }

        /// <summary>
        /// Conforming (red-green) refinement: every edge longer than <paramref name="maxEdge"/> is
        /// split at its midpoint, shared between both neighbours, so no T-junction cracks appear
        /// when the vertex shader displaces the surface.
        /// </summary>
        static void Refine(List<Vector3> v, List<Vector2> uv, List<Vector2> uv2, List<int> tris, float maxEdge, int maxVerts)
        {
            float max2 = maxEdge * maxEdge;
            for (int iter = 0; iter < 12 && v.Count < maxVerts; iter++)
            {
                var mid = new Dictionary<long, int>();
                var outT = new List<int>(tris.Count * 2);
                bool any = false;
                bool Long(int a, int b) => (v[a] - v[b]).sqrMagnitude > max2;
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!mid.TryGetValue(key, out int m))
                    {
                        m = v.Count;
                        v.Add((v[a] + v[b]) * 0.5f);
                        uv.Add((uv[a] + uv[b]) * 0.5f);
                        uv2.Add((uv2[a] + uv2[b]) * 0.5f);
                        mid[key] = m;
                    }
                    return m;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    bool m0 = Long(a, b), m1 = Long(b, c), m2 = Long(c, a);
                    int marks = (m0 ? 1 : 0) + (m1 ? 1 : 0) + (m2 ? 1 : 0);
                    if (marks == 0) { outT.Add(a); outT.Add(b); outT.Add(c); continue; }
                    any = true;
                    if (marks == 3)
                    {
                        int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                        outT.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                        continue;
                    }
                    // rotate (keeping winding) into a canonical pattern
                    for (int r = 0; r < 3; r++)
                    {
                        bool ok = marks == 1 ? m0 : !m2;   // 1 mark: on ab;  2 marks: ca unmarked
                        if (ok) break;
                        (a, b, c) = (b, c, a);
                        (m0, m1, m2) = (m1, m2, m0);
                    }
                    if (marks == 1)
                    {
                        int ab = Mid(a, b);
                        outT.AddRange(new[] { a, ab, c, ab, b, c });
                    }
                    else
                    {
                        int ab = Mid(a, b), bc = Mid(b, c);
                        outT.AddRange(new[] { ab, b, bc, a, ab, bc, a, bc, c });
                    }
                }
                tris.Clear(); tris.AddRange(outT);
                if (!any) break;
            }
        }

        static Mesh BuildVolume(List<Vector2> poly, float bottom, float top)
        {
            var k = new MeshKit();
            int n = poly.Count;
            // side walls (outward normals; shader culls front so we see the inside)
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector3 out3 = new Vector3(b.y - a.y, 0, -(b.x - a.x)).normalized; // right normal of CCW edge = outward
                int i0 = k.Add(new Vector3(a.x, bottom, a.y), Color.white, out3);
                int i1 = k.Add(new Vector3(b.x, bottom, b.y), Color.white, out3);
                int i2 = k.Add(new Vector3(b.x, top, b.y), Color.white, out3);
                int i3 = k.Add(new Vector3(a.x, top, a.y), Color.white, out3);
                k.Tri(i0, i2, i1); k.Tri(i0, i3, i2);
            }
            // top cap (the upside-down seabed), outward = up
            var tris = Geometry.Triangulate(poly);
            int s = k.v.Count;
            foreach (var p in poly) k.Add(new Vector3(p.x, top, p.y), Color.white, Vector3.up);
            for (int i = 0; i < tris.Count; i += 3) k.Tri(s + tris[i], s + tris[i + 2], s + tris[i + 1]);
            return k.Build("SeaVolume");
        }

        /// <summary>
        /// Kelp fronds growing out of the unseen depths: their roots vanish in the deep-water
        /// fog high above, only the long swaying blades reach down toward the surface.
        /// Mostly near the walls so the middle of the ceiling stays open water.
        /// </summary>
        void BuildKelp(RoomModel room)
        {
            var rnd = new System.Random(77);
            float R() => (float)rnd.NextDouble();
            var b = room.HorizontalBounds;
            int clumps = Mathf.Clamp(Mathf.RoundToInt(room.Area * 0.45f), 3, 12);
            var k = new MeshKit();
            Color baseCol = new Color(0.12f, 0.2f, 0.07f), tipCol = new Color(0.5f, 0.62f, 0.18f), redTip = new Color(0.55f, 0.36f, 0.14f);
            for (int c = 0; c < clumps; c++)
            {
                // bias toward the walls: pick a few candidates, keep the one nearest the outline
                Vector2 p = RandomInside(room, b, R, 0.25f);
                for (int tries = 0; tries < 3; tries++)
                {
                    var q = RandomInside(room, b, R, 0.25f);
                    if (!room.Contains(q, 0.7f) && room.Contains(p, 0.7f)) p = q;
                }
                int blades = 3 + (int)(R() * 4);
                for (int i = 0; i < blades; i++)
                {
                    // root far up in the fog; tip 0.15-0.6 m above the surface
                    float tipY = SurfaceY + Mathf.Lerp(0.15f, 0.6f, R());
                    float rootY = SurfaceY + depth * Mathf.Lerp(0.75f, 0.95f, R());
                    float len = rootY - tipY;
                    Vector3 anchor = new Vector3(p.x + (R() - 0.5f) * 0.18f, rootY, p.y + (R() - 0.5f) * 0.18f);
                    float width = Mathf.Lerp(0.04f, 0.085f, R());
                    float yaw = R() * Mathf.PI * 2f, twist = (R() - 0.5f) * 3f, lean = (R() - 0.5f) * 0.25f;
                    Color tip = Color.Lerp(tipCol, redTip, R() * 0.4f);
                    const int S = 16;
                    int start = k.v.Count;
                    for (int s = 0; s <= S; s++)
                    {
                        float t = s / (float)S;
                        float a = yaw + twist * t;
                        Vector3 across = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        Vector3 bendDir = new Vector3(Mathf.Cos(yaw + 1.3f), 0, Mathf.Sin(yaw + 1.3f));
                        Vector3 centre = anchor + Vector3.down * (t * len) + bendDir * (lean * t * t * len)
                                         + across * Mathf.Sin(t * 14f + c) * 0.012f;            // ruffled edge
                        float w = width * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1, t)) * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 0.97f, t));
                        Vector3 nrm = Vector3.Cross(across, Vector3.down).normalized;
                        Color col = Color.Lerp(baseCol, tip, Mathf.SmoothStep(0, 1, t));
                        k.Add(centre - across * w * 0.5f, col, nrm, new Vector2(0, t));
                        k.Add(centre + across * w * 0.5f, col, nrm, new Vector2(1, t));
                    }
                    for (int s = 0; s < S; s++)
                    {
                        int i0 = start + s * 2;
                        k.Tri(i0, i0 + 2, i0 + 1); k.Tri(i0 + 1, i0 + 2, i0 + 3);
                    }
                }
            }
            var mat = UDFAssets.New(UDFAssets.Underwater);
            mat.SetFloat("_UseVertexColor", 1f);
            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_Sway", 0.16f);
            mat.SetFloat("_SwayFreq", 0.9f);
            mat.SetFloat("_Translucency", 0.7f);
            mat.SetFloat("_Smoothness", 0.45f);
            mat.SetFloat("_Caustics", 0.8f);
            MeshKit.Spawn("Kelp", k.Build("Kelp"), mat, transform);
        }

        void BuildMarineSnow(RoomModel room)
        {
            var go = new GameObject("MarineSnow");
            go.transform.SetParent(transform, false);
            var b = room.HorizontalBounds;
            go.transform.position = new Vector3(b.center.x, SurfaceY + ShallowBand * 0.5f + 0.05f, b.center.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.009f);
            main.startColor = new Color(0.85f, 0.95f, 0.9f, 0.45f);
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(room.Area * 1.2f, 8f, 26f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(b.size.x, ShallowBand, b.size.z);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);      // slow current
            vel.y = new ParticleSystem.MinMaxCurve(-0.006f, 0.006f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.015f; noise.frequency = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = UDFAssets.Glow_(Color.white, 1f);
            go.AddComponent<BubbleClipper>().water = this;
            ps.Play();
        }

        static Vector2 RandomInside(RoomModel room, Bounds b, System.Func<float> R, float margin)
        {
            for (int i = 0; i < 30; i++)
            {
                var p = new Vector2(Mathf.Lerp(b.min.x, b.max.x, R()), Mathf.Lerp(b.min.z, b.max.z, R()));
                if (room.Contains(p, margin)) return p;
            }
            return room.Centroid;
        }

        void BuildBubbles(RoomModel room)
        {
            var go = new GameObject("Bubbles");
            go.transform.SetParent(transform, false);
            var b = room.HorizontalBounds;
            go.transform.position = new Vector3(b.center.x, SurfaceY + ShallowBand, b.center.z);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down); // emit "up" in the inverted sea = toward the floor
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = ShallowBand / 0.25f;
            main.startSpeed = 0.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.startColor = new Color(0.85f, 1f, 1f, 0.8f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(room.Area * 2f, 6f, 40f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(b.size.x, b.size.z, 0.01f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = UDFAssets.New(UDFAssets.Bubble);
            main.startColor = Color.white;
            var fadeIn = ps.colorOverLifetime; fadeIn.enabled = true;          // emerge out of the deep blue
            var fg = new Gradient();
            fg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.35f), new GradientAlphaKey(1, 1) });
            fadeIn.color = fg;
            go.AddComponent<BubbleClipper>().water = this;
            ps.Play();
        }

        /// <summary>Air bubbles drifting in the room under the sea and rising into it.</summary>
        void BuildAirBubbles(RoomModel room)
        {
            SplashFx.Init(this);
            var go = new GameObject("AirBubbles");
            go.transform.SetParent(transform, false);
            var b = room.HorizontalBounds;
            float span = Mathf.Min(1.3f, SurfaceY - room.floorY - 0.6f);
            go.transform.position = new Vector3(b.center.x, SurfaceY - 0.15f - span * 0.5f, b.center.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 24f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.055f);
            main.startColor = Color.white;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(room.Area * 0.18f, 1.5f, 5f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(b.size.x * 0.85f, Mathf.Max(0.2f, span), b.size.z * 0.85f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);      // rising toward the sea
            vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.03f; noise.frequency = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(1, 0.9f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = UDFAssets.New(UDFAssets.Bubble);
            var clip = go.AddComponent<BubbleClipper>(); clip.water = this; clip.mode = BubbleClipper.Mode.ReturnToSurface;
            ps.Play();
        }
    }

    /// <summary>
    /// Kills particles that cross the water surface or leave the ceiling outline.
    ///   Underwater      : lives in the sea, dies when it reaches the surface (going down)
    ///   ReturnToSurface : lives in the room, dies when it gets back up into the water
    /// </summary>
    public class BubbleClipper : MonoBehaviour
    {
        public enum Mode { Underwater, ReturnToSurface }
        public CeilingWater water;
        public Mode mode = Mode.Underwater;
        ParticleSystem m_Ps;
        ParticleSystem.Particle[] m_Buf;

        void Awake() { m_Ps = GetComponent<ParticleSystem>(); m_Buf = new ParticleSystem.Particle[Mathf.Max(256, m_Ps.main.maxParticles)]; }

        void LateUpdate()
        {
            if (water == null) water = FindAnyObjectByType<CeilingWater>();
            if (water == null || water.Room == null) return;
            if (m_Buf.Length < m_Ps.main.maxParticles) m_Buf = new ParticleSystem.Particle[m_Ps.main.maxParticles];
            int n = m_Ps.GetParticles(m_Buf);
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                var p = m_Buf[i].position;
                bool dead = !water.IsInsideWaterXZ(p, 0.02f)
                         || (mode == Mode.Underwater ? p.y <= water.SurfaceY + 0.02f
                                                     : p.y >= water.SurfaceY + 0.005f && m_Buf[i].totalVelocity.y > 0);
                if (dead) m_Buf[i].remainingLifetime = 0;
            }
            m_Ps.SetParticles(m_Buf, n);
        }
    }
}
