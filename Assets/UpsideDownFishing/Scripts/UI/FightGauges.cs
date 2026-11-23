using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// The fight UI: two small frosted-glass capsules instead of a panel of bars.
    ///  * Fish capsule, floating just left of the hooked fish: fish icon + cyan stamina bar.
    ///  * Line capsule, standing beside the rod: hook icon (turns red while the hook is working loose)
    ///    over a curved tension gauge filling bottom → top, green → yellow → red (about to snap).
    ///    The faint track shows the slack zone (amber, bottom) and the danger zone (red, top); a white
    ///    notch marks where the drag starts giving line. "Drag n%" shows briefly when the drag changes.
    /// Content is laid out in "metres at 1 m" and scaled by distance, so both keep the same apparent
    /// size wherever the fish / rod are; they draw on top of the room (ZTest Always).
    /// </summary>
    public class FightGauges : MonoBehaviour
    {
        const float ZoneLoose = 0.15f, ZoneDanger = 0.85f;
        const int Order = 20;   // above every world transparent (water, glass, particles)

        // fish capsule (1 m units)
        const float FishW = 0.23f, FishH = 0.062f, BarX0 = -0.046f, BarW = 0.142f, BarH = 0.02f;
        // line capsule + arc gauge (1 m units): arc = part of a circle whose centre sits lower-left
        const float LineW = 0.1f, LineH = 0.25f, LineScale = 1.6f;   // shown 1.6x (it sits close, by the rod)
        static readonly Vector2 ArcC = new Vector2(-0.18f, -0.065f);
        const float ArcR = 0.2f, ArcA0 = -10f, ArcA1 = 35f, ArcT0 = 0.016f, ArcT1 = 0.026f;

        static readonly Color Green = new Color(0.35f, 0.92f, 0.45f), Lime = new Color(0.8f, 0.95f, 0.3f),
            Yellow = new Color(1f, 0.8f, 0.22f), Red = new Color(1f, 0.28f, 0.25f), Cyan = new Color(0.45f, 0.93f, 1f);

        Camera m_Head;
        FishingRod m_Rod;
        Transform m_FishRoot, m_LineRoot, m_DragTick;
        Mesh m_StaminaMesh, m_TensionMesh;
        Material m_LineGlass, m_FishGlass, m_HookIcon;
        TextMesh m_MoveText, m_DragText;
        float m_ShownTension, m_ShownStamina, m_LastDrag, m_DragTextTimer, m_Appear;
        bool m_Was;

        public void Init(Camera head, FishingRod rod, Font font)
        {
            m_Head = head;
            m_Rod = rod;
            m_LastDrag = rod.Drag;

            // ---- fish capsule
            m_FishRoot = new GameObject("FishGauge").transform;
            m_FishRoot.SetParent(transform, false);
            m_FishGlass = HudMat(Color.white);
            Spawn("Glass", Glass(FishW, FishH), m_FishGlass, m_FishRoot, 0);
            Icon("FishIcon", "UDF/Icons/FishIcon", m_FishRoot, new Vector3(-0.077f, 0, 0), 0.05f);
            Spawn("Track", Capsule(new Mesh(), BarW, BarH, new Color(0.05f, 0.16f, 0.3f, 0.45f)), HudMat(Color.white), m_FishRoot, 1)
                .transform.localPosition = new Vector3(BarX0 + BarW * 0.5f, 0, 0);
            m_StaminaMesh = new Mesh { name = "StaminaFill" };
            Spawn("Fill", m_StaminaMesh, HudMat(Color.white), m_FishRoot, 2);
            m_MoveText = Text("Move", font, m_FishRoot, new Vector3(0, -0.058f, 0), 0.0026f);

            // ---- line capsule
            m_LineRoot = new GameObject("LineGauge").transform;
            m_LineRoot.SetParent(transform, false);
            m_LineGlass = HudMat(Color.white);
            Spawn("Glass", Glass(LineW, LineH), m_LineGlass, m_LineRoot, 0);
            m_HookIcon = Icon("HookIcon", "UDF/Icons/HookIcon", m_LineRoot, new Vector3(0, 0.086f, 0), 0.055f);
            var track = new Mesh { name = "TensionTrack" };
            Arc(track, 1f, u => u < ZoneLoose ? new Color(1f, 0.75f, 0.3f, 0.38f) : u > ZoneDanger ? new Color(1f, 0.35f, 0.3f, 0.26f) : new Color(1f, 1f, 1f, 0.3f));
            Spawn("Track", track, HudMat(Color.white), m_LineRoot, 1);
            m_TensionMesh = new Mesh { name = "TensionFill" };
            Spawn("Fill", m_TensionMesh, HudMat(Color.white), m_LineRoot, 2);
            var tick = new MeshKit();
            tick.Box(Vector3.zero, new Vector3(ArcT1 + 0.014f, 0.0045f, 0.001f), Color.white);
            m_DragTick = Spawn("DragTick", tick.Build("DragTick"), HudMat(new Color(1f, 1f, 1f, 0.95f)), m_LineRoot, 3).transform;
            m_DragText = Text("Drag", font, m_LineRoot, new Vector3(0, -0.148f, 0), 0.0024f);

            m_FishRoot.gameObject.SetActive(false);
            m_LineRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ building

        static Material HudMat(Color c)
        {
            var m = UDFAssets.Glow_(c);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            return m;
        }

        static GameObject Spawn(string name, Mesh mesh, Material mat, Transform parent, int order)
        {
            var go = MeshKit.Spawn(name, mesh, mat, parent);
            go.GetComponent<MeshRenderer>().sortingOrder = Order + order;
            return go;
        }

        static Material Icon(string name, string tex, Transform parent, Vector3 pos, float size)
        {
            var k = new MeshKit();
            float s = size * 0.5f;
            int a = k.Add(new Vector3(-s, -s, 0), Color.white, Vector3.back, new Vector2(0, 0));
            int b = k.Add(new Vector3(s, -s, 0), Color.white, Vector3.back, new Vector2(1, 0));
            int c = k.Add(new Vector3(s, s, 0), Color.white, Vector3.back, new Vector2(1, 1));
            int d = k.Add(new Vector3(-s, s, 0), Color.white, Vector3.back, new Vector2(0, 1));
            k.Tri(a, c, b); k.Tri(a, d, c);
            var mat = UDFAssets.New(UDFAssets.Icon);
            mat.SetTexture("_BaseMap", Resources.Load<Texture2D>(tex));
            Spawn(name, k.Build(name), mat, parent, 5).transform.localPosition = pos + Vector3.back * 0.001f;
            return mat;
        }

        static TextMesh Text(string name, Font font, Transform parent, Vector3 pos, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.richText = true;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = Order + 10;
            return tm;
        }

        /// <summary>Outline of a rounded rectangle (centre origin), counter-clockwise.</summary>
        static List<Vector3> Outline(float w, float h, float r, int perCorner = 8)
        {
            var pts = new List<Vector3>();
            float hx = w * 0.5f - r, hy = h * 0.5f - r;
            Vector2[] centres = { new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= perCorner; i++)
                {
                    float a = (c * 90f + i * 90f / perCorner) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(centres[c].x + Mathf.Cos(a) * r, centres[c].y + Mathf.Sin(a) * r, 0));
                }
            return pts;
        }

        /// <summary>Frosted glass capsule: pale fill brighter at the top, white rim, soft outer edge.</summary>
        static Mesh Glass(float w, float h)
        {
            float r = Mathf.Min(w, h) * 0.5f;
            const float rim = 0.0035f, soft = 0.005f;
            var inner = Outline(w - rim * 2f, h - rim * 2f, r - rim);
            var edge = Outline(w, h, r);
            var outer = Outline(w + soft * 2f, h + soft * 2f, r + soft);
            var k = new MeshKit();
            Color Fill(float y) => Color.Lerp(new Color(0.62f, 0.8f, 0.95f, 0.3f), new Color(0.88f, 0.96f, 1f, 0.42f), Mathf.InverseLerp(-h * 0.5f, h * 0.5f, y));
            int centre = k.Add(Vector3.zero, Fill(0), Vector3.back);
            int n = inner.Count, i0 = k.v.Count;
            foreach (var p in inner) k.Add(p, Fill(p.y), Vector3.back);
            int e0 = k.v.Count;
            foreach (var p in edge) k.Add(p, new Color(1f, 1f, 1f, 0.75f), Vector3.back);
            int o0 = k.v.Count;
            foreach (var p in outer) k.Add(p, new Color(1f, 1f, 1f, 0f), Vector3.back);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                k.Tri(centre, i0 + j, i0 + i);
                k.Tri(i0 + i, i0 + j, e0 + j); k.Tri(i0 + i, e0 + j, e0 + i);
                k.Tri(e0 + i, e0 + j, o0 + j); k.Tri(e0 + i, o0 + j, o0 + i);
            }
            return k.Build("GlassCapsule");
        }

        /// <summary>Solid capsule (centre origin), written into an existing mesh.</summary>
        static Mesh Capsule(Mesh m, float w, float h, Color c)
        {
            w = Mathf.Max(w, h);
            var pts = Outline(w, h, h * 0.5f, 6);
            var v = new List<Vector3>(pts.Count + 1) { Vector3.zero };
            v.AddRange(pts);
            var cols = new List<Color>(v.Count);
            for (int i = 0; i < v.Count; i++) cols.Add(c);
            var t = new List<int>();
            for (int i = 0; i < pts.Count; i++) { t.Add(0); t.Add(1 + (i + 1) % pts.Count); t.Add(1 + i); }
            m.Clear();
            m.SetVertices(v); m.SetColors(cols); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static float ArcAngle(float u) => Mathf.Lerp(ArcA0, ArcA1, u) * Mathf.Deg2Rad;
        static Vector3 ArcPoint(float u, float radius)
        {
            float a = ArcAngle(u);
            return new Vector3(ArcC.x + Mathf.Cos(a) * radius, ArcC.y + Mathf.Sin(a) * radius, 0);
        }

        /// <summary>The curved gauge band from u = 0 (bottom) to u1, coloured per position, round-ended.</summary>
        static void Arc(Mesh m, float u1, System.Func<float, Color> col)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            u1 = Mathf.Clamp01(u1);
            int seg = Mathf.Max(1, Mathf.CeilToInt(40 * u1));
            for (int i = 0; i <= seg; i++)
            {
                float u = u1 * i / seg;
                float th = Mathf.Lerp(ArcT0, ArcT1, u) * 0.5f;
                v.Add(ArcPoint(u, ArcR - th)); v.Add(ArcPoint(u, ArcR + th));
                var cc = col(u); c.Add(cc); c.Add(cc);
                if (i < seg) { int b = i * 2; t.Add(b); t.Add(b + 1); t.Add(b + 3); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
            }
            // round caps at both ends
            void Cap(float u, float dirSign)
            {
                float th = Mathf.Lerp(ArcT0, ArcT1, u) * 0.5f;
                Vector3 centre = ArcPoint(u, ArcR);
                float a = ArcAngle(u);
                var tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0) * dirSign;
                var normal = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                int ci = v.Count; v.Add(centre); c.Add(col(u));
                const int n = 8;
                for (int i = 0; i <= n; i++)
                {
                    float b = Mathf.PI * i / n;
                    v.Add(centre + (normal * Mathf.Cos(b) + tangent * Mathf.Sin(b)) * th); c.Add(col(u));
                    if (i < n) { t.Add(ci); t.Add(ci + 1 + i); t.Add(ci + 2 + i); }
                }
            }
            Cap(0f, -1f);
            Cap(u1, 1f);
            m.Clear();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }

        static Color TensionColor(float u)
        {
            if (u < 0.55f) return Color.Lerp(Green, Lime, u / 0.55f);
            if (u < ZoneDanger) return Color.Lerp(Lime, Yellow, (u - 0.55f) / (ZoneDanger - 0.55f));
            return Color.Lerp(Yellow, Red, (u - ZoneDanger) / (1f - ZoneDanger));
        }

        static string MoveLabel(FishFight f)
        {
            if (f.Phase == FightPhase.Tired) return "<color=#8CFF9A>TIRED</color>";
            switch (f.Move)
            {
                case FishMove.Rush: return "<color=#FF8A65>RUN!</color>";
                case FishMove.Rest: return "<color=#8CFF9A>RESTING</color>";
                case FishMove.SideRun: return "<color=#FFD54A>SIDE RUN!</color>";
                case FishMove.HeadShake: return "<color=#FFD54A>HEAD SHAKE!</color>";
                case FishMove.Jump: return "<color=#FF5252>JUMP!</color>";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (m_Head == null || m_Rod == null) return;
            bool fighting = m_Rod.CurrentState == FishingRod.State.Fighting && m_Rod.HookedFish != null && m_Rod.HookedFish.Species != null;
            m_FishRoot.gameObject.SetActive(fighting);
            m_LineRoot.gameObject.SetActive(fighting);
            if (!fighting) { m_Was = false; m_Appear = 0f; return; }

            float dt = Time.deltaTime;
            Transform h = m_Head.transform;
            Vector3 eye = h.position, right = h.right, up = h.up;
            var fish = m_Rod.HookedFish;
            var fight = m_Rod.CurrentFight;
            m_Appear = Mathf.MoveTowards(m_Appear, 1f, dt * 5f);
            float pop = Mathf.SmoothStep(0.6f, 1f, m_Appear);

            // fish capsule: just left of the fish, a touch below its centre
            Vector3 fp = fish.transform.position;
            float df = Vector3.Distance(eye, fp);
            Vector3 fishTarget = fp - right * (FishW * 0.5f * df + fish.Species.length * 0.6f + 0.02f * df) - up * 0.012f * df;
            Place(m_FishRoot, fishTarget, eye, up, pop, dt, 10f);

            // line capsule: beside the rod, about halfway out to the tip, just clear of it on its
            // right-hand side as seen from the eye (however the rod is tilted)
            Vector3 grip = m_Rod.transform.position, tip = m_Rod.TipWorld;
            Vector3 along = Vector3.Lerp(grip, tip, 0.55f);
            float dl = Vector3.Distance(eye, along);
            Vector3 view = (along - eye) / Mathf.Max(dl, 1e-4f);
            Vector3 rodDir = Vector3.ProjectOnPlane(tip - grip, view);
            Vector3 side = rodDir.sqrMagnitude > 1e-6f ? Vector3.Cross(view, rodDir).normalized : right;
            if (Vector3.Dot(side, right) < 0f) side = -side;
            Place(m_LineRoot, along + side * ((LineW * 0.5f * LineScale + 0.05f) * dl), eye, up, pop * LineScale, dt, 12f);
            m_Was = true;

            // stamina
            float stamina = Mathf.Clamp01(fish.Stamina / Mathf.Max(0.01f, fish.Species.stamina));
            m_ShownStamina = m_Appear < 0.05f ? stamina : Mathf.Lerp(m_ShownStamina, stamina, 1f - Mathf.Exp(-dt * 6f));
            float fw = Mathf.Max(BarH, BarW * m_ShownStamina);
            Capsule(m_StaminaMesh, fw, BarH * 0.78f, Cyan);
            m_StaminaMesh.SetVertices(OffsetX(m_StaminaMesh, BarX0 + fw * 0.5f));
            m_StaminaMesh.RecalculateBounds();
            m_MoveText.text = fight != null ? MoveLabel(fight) : "";

            // tension: the raw value jumps every frame (bursts, head shakes): show a calm gauge
            float raw = Mathf.Clamp01(m_Rod.Tension);
            if (m_Appear < 0.05f) m_ShownTension = raw;
            float smoothed = Mathf.Lerp(m_ShownTension, raw, 1f - Mathf.Exp(-dt * 5f));
            m_ShownTension = Mathf.MoveTowards(m_ShownTension, smoothed, dt * 0.7f);
            float t = m_ShownTension;
            Arc(m_TensionMesh, Mathf.Max(0.02f, t), TensionColor);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            m_LineGlass.SetColor("_BaseColor",
                t > ZoneDanger ? Color.Lerp(Color.white, new Color(1f, 0.45f, 0.42f), 0.5f + 0.5f * pulse)
                : t < ZoneLoose ? Color.Lerp(Color.white, new Color(1f, 0.85f, 0.5f), 0.3f) : Color.white);

            // drag notch: where the spool starts slipping
            float dragU = Mathf.Clamp01(m_Rod.DragLimit);
            m_DragTick.gameObject.SetActive(m_Rod.DragLimit <= 1f);
            m_DragTick.localPosition = ArcPoint(dragU, ArcR) + Vector3.back * 0.0005f;
            m_DragTick.localRotation = Quaternion.Euler(0, 0, ArcAngle(dragU) * Mathf.Rad2Deg);

            // hook icon: red and blinking while the hook is working loose
            float hold = fight != null ? Mathf.Clamp01(fight.HookHold) : 1f;
            m_HookIcon.SetColor("_BaseColor", hold < 0.35f ? Color.Lerp(Color.white, Red, 0.4f + 0.6f * pulse) : Color.white);

            // drag readout: shown for a moment after the drag is changed, and while line is going out
            if (!Mathf.Approximately(m_Rod.Drag, m_LastDrag)) { m_DragTextTimer = 1.6f; m_LastDrag = m_Rod.Drag; }
            m_DragTextTimer -= dt;
            int pct = Mathf.RoundToInt(m_Rod.Drag * 100f);
            m_DragText.text = m_Rod.DragSlipping ? $"<color=#FFB74A>Line out</color>  <color=#66F2FF>Drag {pct}%</color>"
                            : m_DragTextTimer > 0f ? $"<color=#66F2FF>Drag {pct}%</color>" : "";
        }

        void Place(Transform root, Vector3 target, Vector3 eye, Vector3 up, float pop, float dt, float follow)
        {
            root.position = !m_Was ? target : Vector3.Lerp(root.position, target, 1f - Mathf.Exp(-dt * follow));
            root.rotation = Quaternion.LookRotation(root.position - eye, up);
            root.localScale = Vector3.one * (Vector3.Distance(eye, root.position) * pop);
        }

        static List<Vector3> OffsetX(Mesh m, float x)
        {
            var v = new List<Vector3>();
            m.GetVertices(v);
            for (int i = 0; i < v.Count; i++) v[i] += new Vector3(x, 0, 0);
            return v;
        }
    }
}
