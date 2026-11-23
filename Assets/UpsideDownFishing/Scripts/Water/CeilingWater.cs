using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Turns the detected ceiling polygon into an upside-down sea: a water surface at ceiling
    /// height, a "sea volume" extending above the real ceiling (seen through the surface), an
    /// inverted seabed with seaweed/rocks hanging from the top, and bubbles drifting "up"
    /// (toward the floor) to the surface.
    /// </summary>
    public class CeilingWater : MonoBehaviour
    {
        public float depth = 1.6f;
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

            BuildSeabed(room);
            BuildBubbles(room);
        }

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
            float[] distances = { 0f, 0.05f, 0.12f, 0.25f };
            var rings = new List<List<Vector2>>();
            foreach (var d in distances) rings.Add(d == 0 ? new List<Vector2>(poly) : Geometry.Inset(poly, d));

            var k = new MeshKit();
            int n = poly.Count;
            var ringStart = new List<int>();
            for (int r = 0; r < rings.Count; r++)
            {
                ringStart.Add(k.v.Count);
                foreach (var p in rings[r]) k.Add(new Vector3(p.x, y, p.y), Color.white, Vector3.down, new Vector2(0, distances[r]));
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
            return k.Build("CeilingWaterSurface");
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

        void BuildSeabed(RoomModel room)
        {
            var k = new MeshKit();
            var rnd = new System.Random(1234);
            float R() => (float)rnd.NextDouble();
            float area = room.Area;
            int weeds = Mathf.Clamp(Mathf.RoundToInt(area * 2.5f), 8, 60);
            Color[] weedColors = { new Color(0.3f, 0.85f, 0.45f), new Color(0.2f, 0.7f, 0.55f), new Color(0.55f, 0.9f, 0.35f) };
            Color[] coral = { new Color(1f, 0.5f, 0.6f), new Color(1f, 0.7f, 0.35f), new Color(0.8f, 0.5f, 1f) };
            var b = room.HorizontalBounds;

            for (int i = 0; i < weeds; i++)
            {
                Vector2 p = RandomInside(room, b, R, 0.15f);
                float len = Mathf.Lerp(0.35f, 0.9f, R());
                var path = new List<Vector3>(); var rad = new List<float>();
                float phase = R() * 6f;
                for (int s = 0; s <= 6; s++)
                {
                    float t = s / 6f;
                    // hangs downward from the seabed toward the surface
                    path.Add(new Vector3(p.x + Mathf.Sin(t * 3 + phase) * 0.05f, SeabedY - t * len, p.y + Mathf.Cos(t * 2.3f + phase) * 0.05f));
                    rad.Add(Mathf.Lerp(0.025f, 0.005f, t));
                }
                k.Tube(path, rad, weedColors[i % weedColors.Length], 5);
            }
            int rocks = Mathf.Clamp(Mathf.RoundToInt(area * 1.2f), 5, 30);
            for (int i = 0; i < rocks; i++)
            {
                Vector2 p = RandomInside(room, b, R, 0.2f);
                float r = Mathf.Lerp(0.08f, 0.22f, R());
                k.Ellipsoid(new Vector3(p.x, SeabedY - r * 0.25f, p.y), new Vector3(r, r * 0.55f, r * 0.9f),
                    new Color(0.45f, 0.5f, 0.7f), new Color(0.65f, 0.7f, 0.85f), 5, 8);
                if (R() > 0.5f)
                {
                    Color cc = coral[i % coral.Length];
                    k.Ellipsoid(new Vector3(p.x + r * 0.4f, SeabedY - r * 0.6f, p.y), new Vector3(r * 0.3f, r * 0.5f, r * 0.3f), cc, Color.Lerp(cc, Color.white, 0.4f), 5, 8);
                }
            }
            var go = MeshKit.Spawn("UpsideDownSeabed", k.Build("Seabed"), UDFAssets.Toon_(Color.white), transform);
            go.GetComponent<MeshRenderer>().sharedMaterial.SetColor("_Emission", new Color(0.05f, 0.08f, 0.12f));
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
            go.transform.position = new Vector3(b.center.x, SeabedY - 0.05f, b.center.z);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down); // emit "up" in the inverted sea = toward the floor
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = depth / 0.25f;
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
            r.sharedMaterial = UDFAssets.Glow_(Color.white, 1f);
            go.AddComponent<BubbleClipper>().water = this;
            ps.Play();
        }
    }

    /// <summary>Kills bubbles that reach the surface or leave the ceiling outline, with a ripple.</summary>
    public class BubbleClipper : MonoBehaviour
    {
        public CeilingWater water;
        ParticleSystem m_Ps;
        ParticleSystem.Particle[] m_Buf;

        void Awake() { m_Ps = GetComponent<ParticleSystem>(); m_Buf = new ParticleSystem.Particle[256]; }

        void LateUpdate()
        {
            if (water == null || water.Room == null) return;
            int n = m_Ps.GetParticles(m_Buf);
            for (int i = 0; i < n; i++)
            {
                var p = m_Buf[i].position;
                if (!water.IsInsideWaterXZ(p, 0.02f)) m_Buf[i].remainingLifetime = 0;
                else if (p.y <= water.SurfaceY + 0.02f) m_Buf[i].remainingLifetime = 0;
            }
            m_Ps.SetParticles(m_Buf, n);
        }
    }
}
