using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// The room aquarium - an UPSIDE-DOWN tank, like the ceiling sea: gravity inside is reversed.
    ///   * Sealed glass lid on top (keeps the water from floating away), dark slate top frame and
    ///     corner posts, glass walls, and a completely OPEN bottom (no glass, board or base).
    ///   * The water clings to the lid; its surface is one big downward-facing sheet just above the
    ///     bottom edge of the glass. Decor (Tripo: gravel bed, rocks, corals, plants, ruin arch) is
    ///     mounted upside down on the lid, hanging into the water. Bubbles drift DOWN to the surface.
    ///   * Four Tripo wooden legs at the corners; open space underneath.
    ///   * Tripo leafy plants on the lid corners, trailing over the sides.
    /// The player lifts a caught fish up into the water from below; the fish swim upright (see
    /// TankFish). Placed on the real floor near the player, facing them.
    /// Tank local space: origin at the centre of the open bottom (glass bottom edge), +Z faces the player.
    /// </summary>
    public class Aquarium : MonoBehaviour
    {
        public const float W = 1.2f, D = 0.45f, H = 0.5f;   // glass outer size
        const float Glass = 0.006f;
        /// <summary>Height of the open bottom above the floor (the legs): fish are lifted in from below.</summary>
        public const float BottomHeight = 0.6f;
        const float SurfaceGap = 0.03f;       // the glass walls reach this far below the water surface
        const float FrameH = 0.035f, FrameW = 0.024f;
        public const int MaxFish = 12;

        public Transform Tank { get; private set; }
        /// <summary>The downward-facing water surface (tank local y).</summary>
        public float SurfaceY => SurfaceGap;
        /// <summary>Highest point fish swim to (below the decor hanging from the lid), tank local y.</summary>
        public float FishTopY { get; private set; } = H * 0.6f;
        public int FishCount => m_Fish.Count;

        static readonly Color Slate = new Color(0.17f, 0.19f, 0.24f);

        Material m_GlassMat, m_RimGlowMat;
        float m_Highlight, m_HighlightTarget;
        readonly List<TankFish> m_Fish = new List<TankFish>();

        public void Build(RoomModel room, Transform head)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            m_Fish.Clear();

            Tank = new GameObject("Tank").transform;
            Tank.SetParent(transform, false);
            Tank.localPosition = new Vector3(0, BottomHeight, 0);
            BuildLegs();
            BuildTank();
            BuildDecor();
            BuildPlants();
            BuildBubbles();
            Place(room, head);
        }

        // ---------------------------------------------------------------- placement

        void Place(RoomModel room, Transform head)
        {
            Vector3 eye = head.position;
            Vector3 fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float[] angles = { -55f, 55f, -90f, 90f, -30f, 30f, -130f, 130f, 180f, 0f };
            float[] dists = { 1.1f, 1.4f, 0.9f, 1.8f };
            float top = BottomHeight + H + 0.15f;
            Vector3 half = new Vector3(W * 0.5f + 0.1f, top * 0.5f, D * 0.5f + 0.1f);
            Vector3 best = new Vector3(eye.x + fwd.x, room.floorY, eye.z + fwd.z);
            bool found = false;
            foreach (float dist in dists)
            {
                foreach (float a in angles)
                {
                    Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * fwd;
                    Vector3 p = new Vector3(eye.x, room.floorY, eye.z) + dir * dist;
                    if (!room.Contains(new Vector2(p.x, p.z), 0.65f)) continue;
                    var rot = Quaternion.LookRotation(-dir);
                    if (Physics.CheckBox(p + Vector3.up * (top * 0.5f + 0.05f), half, rot, 1 << FishAgent.RoomLayer)) continue;
                    best = p; found = true; break;
                }
                if (found) break;
            }
            if (!found)
            {
                var xz = room.ClampInside(new Vector2(best.x, best.z), 0.65f);
                best = new Vector3(xz.x, room.floorY, xz.y);
            }
            transform.position = best;
            Vector3 toEye = eye - best; toEye.y = 0;
            transform.rotation = Quaternion.LookRotation(toEye.sqrMagnitude > 1e-4f ? toEye.normalized : -fwd);
        }

        // ---------------------------------------------------------------- legs

        void BuildLegs()
        {
            // each leg runs from the floor up past the bottom corner of the tank: it stands flush with
            // the tank's end, pressed against the front / back glass, and its bolted bracket block
            // clamps the bottom of the corner
            const float overlap = 0.14f, size = 0.085f;
            float len = BottomHeight + overlap;
            var fit = TackleModels.LoadFitted("UDF/Models/Aquarium/AquariumLeg", new Vector3(size, len, size), false, stretchXZ: true, exactY: true);
            Mesh mesh; Material mat;
            if (fit != null) { mesh = fit.mesh; mat = fit.material; }
            else
            {
                var k = new MeshKit();
                k.Box(new Vector3(0, len * 0.5f, 0), new Vector3(size * 0.8f, len, size * 0.8f), new Color(0.86f, 0.5f, 0.22f));
                k.Box(new Vector3(0, len - 0.06f, 0), new Vector3(size, 0.12f, size), new Color(0.8f, 0.46f, 0.2f));
                mesh = k.Build("AquariumLeg"); mat = UDFAssets.Toon_(Color.white);
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var go = MeshKit.Spawn("Leg", mesh, mat, transform);
                    go.transform.localPosition = new Vector3(sx * (W * 0.5f - size * 0.5f + 0.004f), 0, sz * (D * 0.5f + size * 0.5f - 0.002f));
                    // bolt side faces out of the front / back
                    go.transform.localRotation = Quaternion.Euler(0, sz > 0 ? 0 : 180, 0);
                }
        }

        // ---------------------------------------------------------------- tank

        void BuildTank()
        {
            // dark slate frame: top rim (holds the lid) + corner posts; nothing across the bottom
            var rim = new MeshKit();
            float y = H - FrameH * 0.5f;
            rim.Box(new Vector3(0, y, D * 0.5f - FrameW * 0.5f + 0.004f), new Vector3(W + 0.008f, FrameH, FrameW), Slate);
            rim.Box(new Vector3(0, y, -D * 0.5f + FrameW * 0.5f - 0.004f), new Vector3(W + 0.008f, FrameH, FrameW), Slate);
            rim.Box(new Vector3(W * 0.5f - FrameW * 0.5f + 0.004f, y, 0), new Vector3(FrameW, FrameH, D + 0.008f), Slate);
            rim.Box(new Vector3(-W * 0.5f + FrameW * 0.5f - 0.004f, y, 0), new Vector3(FrameW, FrameH, D + 0.008f), Slate);
            // lid rim around the glass cover
            rim.Box(new Vector3(0, H + 0.005f, D * 0.5f - 0.007f), new Vector3(W + 0.014f, 0.01f, 0.018f), Slate);
            rim.Box(new Vector3(0, H + 0.005f, -D * 0.5f + 0.007f), new Vector3(W + 0.014f, 0.01f, 0.018f), Slate);
            rim.Box(new Vector3(W * 0.5f - 0.007f, H + 0.005f, 0), new Vector3(0.018f, 0.01f, D + 0.014f), Slate);
            rim.Box(new Vector3(-W * 0.5f + 0.007f, H + 0.005f, 0), new Vector3(0.018f, 0.01f, D + 0.014f), Slate);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    rim.Box(new Vector3(sx * (W * 0.5f - 0.005f), H * 0.5f, sz * (D * 0.5f - 0.005f)), new Vector3(0.014f, H, 0.014f), Slate);
            MeshKit.Spawn("Frame", rim.Build("AquariumFrame"), UDFAssets.Toon_(Color.white), Tank);

            // water: held up against the lid; the box is mounted upside down so the TankWater
            // shader draws its surface (ripples, fresnel, meniscus) on the downward-facing bottom
            float top = H - 0.004f;
            Vector3 ws = new Vector3(W - Glass * 2f - 0.002f, top - SurfaceY, D - Glass * 2f - 0.002f);
            var water = new MeshKit();
            water.Box(new Vector3(0, ws.y * 0.5f, 0), ws, Color.white);
            var waterMat = UDFAssets.New(UDFAssets.TankWater);
            // a strong tint: in MR the real room shows through, pale water just disappears
            waterMat.SetColor("_ShallowColor", new Color(0.2f, 0.72f, 0.97f));
            waterMat.SetColor("_DeepColor", new Color(0.03f, 0.32f, 0.72f));
            waterMat.SetFloat("_Absorb", 3f);
            waterMat.SetFloat("_MaxAlpha", 0.72f);
            waterMat.SetVector("_Size", new Vector4(ws.x, ws.y, ws.z, 0));
            var waterGo = MeshKit.Spawn("Water", water.Build("AquariumWater"), waterMat, Tank);
            waterGo.transform.localPosition = new Vector3(0, top, 0);
            waterGo.transform.localRotation = Quaternion.Euler(0, 0, 180f);
            waterGo.GetComponent<MeshRenderer>().sortingOrder = 1;

            // glass: 4 walls + the sealed lid; the bottom stays open
            var glass = new Mesh { name = "AquariumGlass" };
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var ts = new List<int>();
            void Panel(Vector3 c, Vector3 u, Vector3 v, Vector3 n)
            {
                int i0 = vs.Count;
                vs.Add(c - u - v); vs.Add(c + u - v); vs.Add(c + u + v); vs.Add(c - u + v);
                us.Add(new Vector2(0, 0)); us.Add(new Vector2(1, 0)); us.Add(new Vector2(1, 1)); us.Add(new Vector2(0, 1));
                for (int i = 0; i < 4; i++) ns.Add(n);
                ts.Add(i0); ts.Add(i0 + 1); ts.Add(i0 + 2); ts.Add(i0); ts.Add(i0 + 2); ts.Add(i0 + 3);
            }
            float hw = W * 0.5f, hd = D * 0.5f, hh = H * 0.5f;
            Panel(new Vector3(0, hh, hd), Vector3.right * hw, Vector3.up * hh, Vector3.forward);
            Panel(new Vector3(0, hh, -hd), Vector3.right * hw, Vector3.up * hh, Vector3.back);
            Panel(new Vector3(hw, hh, 0), Vector3.forward * hd, Vector3.up * hh, Vector3.right);
            Panel(new Vector3(-hw, hh, 0), Vector3.forward * hd, Vector3.up * hh, Vector3.left);
            Panel(new Vector3(0, H + 0.002f, 0), Vector3.right * hw, Vector3.back * hd, Vector3.up);
            glass.SetVertices(vs); glass.SetNormals(ns); glass.SetUVs(0, us); glass.SetTriangles(ts, 0);
            glass.RecalculateBounds();
            m_GlassMat = UDFAssets.New(UDFAssets.Glass);
            // looked at from below the walls are seen at grazing angles: keep the fresnel low
            // or they turn milky and hide the hanging decor
            m_GlassMat.SetFloat("_FresnelAlpha", 0.16f);
            m_GlassMat.SetFloat("_Streaks", 0.2f);
            var glassGo = MeshKit.Spawn("Glass", glass, m_GlassMat, Tank);
            glassGo.GetComponent<MeshRenderer>().sortingOrder = 2;

            // glow along the open bottom edge: lights up when a fish is held underneath
            var glow = new MeshKit();
            void Edge(Vector3 a, Vector3 b, Vector3 inward)
            {
                int s = glow.v.Count;
                var hi = new Color(0.55f, 0.95f, 1f, 1f); var lo = new Color(0.35f, 0.85f, 1f, 0f);
                glow.Add(a, hi, Vector3.down); glow.Add(b, hi, Vector3.down);
                glow.Add(b + inward * 0.05f, lo, Vector3.down); glow.Add(a + inward * 0.05f, lo, Vector3.down);
                glow.Tri(s, s + 1, s + 2); glow.Tri(s, s + 2, s + 3);
            }
            float gx = W * 0.5f - Glass, gz = D * 0.5f - Glass, gy = SurfaceY - 0.002f;
            Edge(new Vector3(-gx, gy, gz), new Vector3(gx, gy, gz), Vector3.back);
            Edge(new Vector3(gx, gy, -gz), new Vector3(-gx, gy, -gz), Vector3.forward);
            Edge(new Vector3(gx, gy, gz), new Vector3(gx, gy, -gz), Vector3.left);
            Edge(new Vector3(-gx, gy, -gz), new Vector3(-gx, gy, gz), Vector3.right);
            m_RimGlowMat = UDFAssets.Glow_(new Color(1, 1, 1, 0));
            var glowGo = MeshKit.Spawn("OpeningGlow", glow.Build("OpeningGlow"), m_RimGlowMat, Tank);
            glowGo.GetComponent<MeshRenderer>().sortingOrder = 3;
        }

        // ---------------------------------------------------------------- decor (hangs from the lid)

        void BuildDecor()
        {
            var inner = new Vector3(W - Glass * 2f - 0.006f, H * 0.42f, D - Glass * 2f - 0.006f);
            var decor = TackleModels.LoadFitted("UDF/Models/Aquarium/AquariumDecor", inner, true, stretchXZ: true);
            if (decor == null) { FishTopY = H * 0.75f; return; }
            var mat = new Material(decor.material);
            mat.SetFloat("_TankLit", 2f);    // lit through the water surface below
            var go = MeshKit.Spawn("Decor", decor.mesh, mat, Tank);
            // upside down: the gravel bed presses against the lid, plants / rocks / arch hang down
            go.transform.localPosition = new Vector3(0, H - 0.003f, 0);
            go.transform.localRotation = Quaternion.Euler(0, 0, 180f);
            FishTopY = Mathf.Max(SurfaceY + 0.12f, H - 0.003f - decor.size.y * 0.6f);
        }

        // ---------------------------------------------------------------- plants on the lid corners

        void BuildPlants()
        {
            var fit = TackleModels.LoadFitted("UDF/Models/Aquarium/AquariumPlant", new Vector3(0.36f, 0.4f, 0.36f), false, readable: true);
            if (fit == null) return;
            // where do the strands hang, seen from the leafy crown?
            float h = fit.size.y;
            Vector3 tips = Vector3.zero, crown = Vector3.zero; int nt = 0, nc = 0;
            foreach (var p in fit.mesh.vertices)
            {
                if (p.y < h * 0.3f) { tips += p; nt++; }
                else if (p.y > h * 0.6f) { crown += p; nc++; }
            }
            // the lowest leaves under the crown core rest on the glass lid
            float crownMin = h;
            if (nc > 0)
            {
                crown /= nc;
                foreach (var p in fit.mesh.vertices)
                {
                    float dx = p.x - crown.x, dz = p.z - crown.z;
                    if (dx * dx + dz * dz < 0.05f * 0.05f && p.y > h * 0.35f) crownMin = Mathf.Min(crownMin, p.y);
                }
            }
            if (crownMin >= h) crownMin = h * 0.4f;
            fit.mesh.UploadMeshData(true);
            Vector3 hang = nt > 0 && nc > 0 ? tips / nt - crown : Vector3.left;
            hang.y = 0;
            if (hang.sqrMagnitude < 1e-5f) hang = Vector3.left;

            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    bool front = sz > 0;
                    float scale = front ? 0.8f : 1f;
                    // strands spill over the end wall, a little toward the front / back
                    var outward = new Vector3(sx, 0, sz * 0.5f).normalized;
                    var rot = Quaternion.FromToRotation(hang.normalized, outward);
                    Vector3 c = rot * crown * scale;
                    var anchor = new Vector3(sx * (W * 0.5f - 0.05f), H + 0.01f, sz * (D * 0.5f - 0.06f));
                    var go = MeshKit.Spawn("Plant", fit.mesh, fit.material, Tank);
                    go.transform.localRotation = rot;
                    go.transform.localScale = Vector3.one * scale;
                    go.transform.localPosition = new Vector3(anchor.x - c.x, anchor.y - crownMin * scale - 0.02f, anchor.z - c.z);
                }
        }

        void BuildBubbles()
        {
            // reversed gravity: bubbles leave the decor and drift DOWN to the surface, where they pop
            var go = new GameObject("Bubbles");
            go.transform.SetParent(Tank, false);
            float start = FishTopY + 0.04f;
            go.transform.localPosition = new Vector3(W * 0.08f, start, 0);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);   // cone +Z -> world down
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.22f);
            main.startLifetime = (start - SurfaceY - 0.01f) / 0.185f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0;
            main.maxParticles = 100;
            main.startColor = new Color(0.9f, 1f, 1f, 0.8f);
            var em = ps.emission; em.rateOverTime = 14;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4; sh.radius = 0.02f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.025f; noise.frequency = 1.5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = UDFAssets.New(UDFAssets.Bubble);
            r.sortingOrder = 1;
            ps.Play();
        }

        // ---------------------------------------------------------------- gameplay API

        /// <summary>Is a fish held at this point under the open bottom (or just up into the water)?</summary>
        public bool IsUnderOpening(Vector3 worldPos)
        {
            if (Tank == null) return false;
            Vector3 p = Tank.InverseTransformPoint(worldPos);
            return Mathf.Abs(p.x) < W * 0.5f - 0.02f && Mathf.Abs(p.z) < D * 0.5f - 0.02f
                   && p.y < SurfaceY + 0.08f && p.y > -0.55f;
        }

        /// <summary>0 = off, 0.35 = "carry the fish here" guide, 1 = "let go now".</summary>
        public void SetHighlight(float amount) => m_HighlightTarget = amount;

        /// <summary>Centre of the downward-facing water surface.</summary>
        public Vector3 OpeningCenter => Tank != null ? Tank.TransformPoint(new Vector3(0, SurfaceY, 0)) : transform.position;

        /// <summary>Puts the fish in the tank (it is lifted up through the surface and swims off).</summary>
        public void AddFish(FishAgent fish)
        {
            var go = fish.gameObject;
            // the FishAgent sleeps while the fish lives in the tank (it wakes up if taken out)
            fish.enabled = false;
            go.layer = 0;
            var tf = go.AddComponent<TankFish>();
            tf.Init(this, fish);
            m_Fish.Add(tf);
            while (m_Fish.Count > MaxFish)
            {
                var old = m_Fish[0];
                m_Fish.RemoveAt(0);
                if (old) old.gameObject.AddComponent<ShrinkAndDestroy>();
            }
        }

        public System.Collections.Generic.IReadOnlyList<TankFish> Fish => m_Fish;

        /// <summary>Takes a fish out of the tank by hand (reaching up into the water): a held fish again.</summary>
        public FishAgent TakeFish(TankFish tf)
        {
            if (tf == null || !m_Fish.Remove(tf)) return null;
            var agent = tf.Agent;
            Vector3 p = tf.transform.localPosition;
            SplashFx.Drip(Tank.TransformPoint(new Vector3(p.x, SurfaceY, p.z)));
            tf.Restore();
            Destroy(tf);
            if (agent == null) return null;
            agent.enabled = true;
            agent.Hold();
            return agent;
        }

        /// <summary>Half extents of the swimmable water in X / Z (inside the glass).</summary>
        public Vector3 InnerHalfExtents => new Vector3(W * 0.5f - Glass - 0.01f, H, D * 0.5f - Glass - 0.01f);

        void Update()
        {
            m_Highlight = Mathf.MoveTowards(m_Highlight, m_HighlightTarget, Time.deltaTime * 4f);
            if (m_GlassMat != null) m_GlassMat.SetFloat("_Highlight", m_Highlight * 0.6f);
            if (m_RimGlowMat != null)
            {
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * (2.5f + 4f * m_Highlight));
                m_RimGlowMat.SetColor("_BaseColor", new Color(1, 1, 1, m_Highlight * pulse));
            }
        }
    }
}
