using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// The anti-gravity fishing rod (right hand).
    ///   Idle    : hold trigger, flick the rod up and release to cast. The bobber falls UP.
    ///   Waiting : bobber hangs under the ceiling surface, lure dangles up into the sea.
    ///   Bite    : bobber is yanked into the water — jerk the rod down (or pull trigger) to hook.
    ///   Fight   : trigger = reel. Keep the tension in the green; too much snaps the line,
    ///             slack for too long lets the fish shake free. Pull it down through the surface.
    /// </summary>
    public class FishingRod : MonoBehaviour
    {
        public enum State { Idle, Charging, Flying, Waiting, Bite, Fighting, Retrieving, Landed }

        public CeilingWater water;
        public FishSchool school;
        public CollectionManager collection;
        public SfxBank sfx;
        public Hand hand = Hand.Right;

        public float rodLength = 1.1f;
        public float antiGravity = 5.5f;
        public float maxLineLength = 7f;

        public State CurrentState { get; private set; }
        public float Tension { get; private set; }
        public float LineLength { get; private set; }
        public FishAgent HookedFish { get; private set; }
        public string Hint { get; private set; } = "";
        public System.Action<FishAgent> Landed;
        public System.Action<string> Message;

        // visuals
        MeshFilter m_RodMesh;
        MeshRenderer m_RodRenderer;
        Transform m_Bobber, m_Lure, m_Reel;
        LineRenderer m_Line, m_Leader;
        Vector3 m_TipWorld;
        Vector3 m_BobberVel;
        float m_StateTime;
        float m_BiteWindow;
        float m_OverTension, m_Slack;
        float m_HapticTimer;
        float m_ReelClick;
        Vector3 m_FishWander;
        float m_WanderTimer;
        float m_BurstTimer, m_Burst;
        Vector3 m_RetrieveFrom;
        readonly Queue<(Vector3 p, float t)> m_TipHistory = new Queue<(Vector3, float)>();
        readonly List<Vector3> m_Path = new List<Vector3>();
        readonly List<float> m_Radii = new List<float>();
        bool m_TriggerWasDown;

        void Start()
        {
            BuildVisuals();
            school.FishNibbled += OnNibble;
            school.FishBit += OnBite;
            collection.RodChanged += _ => ApplyRodLook();
            ApplyRodLook();
            SetState(State.Idle);
        }

        void BuildVisuals()
        {
            var rodGo = new GameObject("RodMesh");
            rodGo.transform.SetParent(transform, false);
            m_RodMesh = rodGo.AddComponent<MeshFilter>();
            m_RodMesh.sharedMesh = new Mesh { name = "Rod" };
            m_RodMesh.sharedMesh.MarkDynamic();
            m_RodRenderer = rodGo.AddComponent<MeshRenderer>();
            m_RodRenderer.sharedMaterial = UDFAssets.Toon_(Color.white);

            var reel = new MeshKit();
            reel.Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.035f, 0.02f), new Color(0.95f, 0.95f, 1f), new Color(0.7f, 0.75f, 0.85f), 6, 10);
            reel.Box(new Vector3(0.03f, 0, 0), new Vector3(0.03f, 0.008f, 0.008f), new Color(1f, 0.85f, 0.3f));
            m_Reel = MeshKit.Spawn("Reel", reel.Build("Reel"), UDFAssets.Toon_(Color.white), transform).transform;
            m_Reel.localPosition = new Vector3(0, -0.04f, 0.12f);
            m_Reel.localRotation = Quaternion.Euler(0, 90, 0);

            var bob = new MeshKit();
            bob.Ellipsoid(new Vector3(0, 0.012f, 0), new Vector3(0.025f, 0.02f, 0.025f), Color.white, Color.white, 5, 10);
            bob.Ellipsoid(new Vector3(0, -0.01f, 0), new Vector3(0.026f, 0.022f, 0.026f), new Color(1f, 0.3f, 0.35f), new Color(1f, 0.3f, 0.35f), 5, 10);
            bob.Tube(new[] { new Vector3(0, 0.03f, 0), new Vector3(0, 0.065f, 0) }, new[] { 0.004f, 0.002f }, new Color(1f, 0.85f, 0.3f), 5);
            m_Bobber = MeshKit.Spawn("Bobber", bob.Build("Bobber"), UDFAssets.Toon_(Color.white), null).transform;

            var lure = new MeshKit();
            lure.Ellipsoid(Vector3.zero, new Vector3(0.012f, 0.018f, 0.012f), new Color(1f, 0.95f, 0.6f), new Color(1f, 0.6f, 0.3f), 4, 8);
            m_Lure = MeshKit.Spawn("Lure", lure.Build("Lure"), UDFAssets.Toon_(Color.white, new Color(0.5f, 0.35f, 0.1f)), null).transform;

            m_Line = CreateLine("FishingLine", 0.0025f, new Color(1f, 1f, 1f, 0.85f), 16);
            m_Leader = CreateLine("Leader", 0.0015f, new Color(1f, 1f, 1f, 0.6f), 2);
        }

        LineRenderer CreateLine(string name, float width, Color c, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.sharedMaterial = UDFAssets.Glow_(Color.white);
            lr.startColor = lr.endColor = c;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        void ApplyRodLook() => RebuildRodMesh(0, Vector3.zero);

        void OnDestroy()
        {
            if (m_Bobber) Destroy(m_Bobber.gameObject);
            if (m_Lure) Destroy(m_Lure.gameObject);
        }

        void SetState(State s)
        {
            CurrentState = s;
            m_StateTime = 0;
        }

        // -----------------------------------------------------------------

        void Update()
        {
            if (water == null || water.Room == null) return;
            var input = XRInput.Get(hand);
            bool triggerDown = input.trigger > 0.55f;
            bool triggerPressed = triggerDown && !m_TriggerWasDown;
            bool triggerReleased = !triggerDown && m_TriggerWasDown;
            m_TriggerWasDown = triggerDown;
            m_StateTime += Time.deltaTime;

            if (XRInput.SecondaryDown(hand) && (CurrentState == State.Idle || CurrentState == State.Charging))
                collection.CycleRod();

            Vector3 tip = ComputeTip();
            TrackTip(tip);
            var rod = collection.CurrentRod;

            switch (CurrentState)
            {
                case State.Idle:
                    Hint = "按住扳机，向上甩竿后松开 —— 把鱼线抛向天花板！";
                    ParkBobberAtTip(tip);
                    if (triggerPressed) SetState(State.Charging);
                    break;

                case State.Charging:
                    Hint = "向上甩！然后松开扳机";
                    ParkBobberAtTip(tip);
                    if (triggerReleased) Cast(tip);
                    break;

                case State.Flying:
                    Hint = "";
                    FlyBobber();
                    break;

                case State.Waiting:
                    Hint = "等待鱼儿咬钩……（扣扳机收线）";
                    FloatBobber(0f);
                    school.UpdateLure(m_Lure.position, true);
                    if (triggerPressed && m_StateTime > 0.4f) StartRetrieve();
                    break;

                case State.Bite:
                    Hint = "咬钩了！快向下拉竿！";
                    m_BiteWindow -= Time.deltaTime;
                    FloatBobber(0.12f);
                    float downSpeed = -VerticalTipSpeed();
                    if (downSpeed > 1.0f || triggerPressed) StartFight();
                    else if (m_BiteWindow <= 0)
                    {
                        Message?.Invoke("鱼跑了……下次快一点！");
                        school.ReleaseEngaged(false);
                        SetState(State.Waiting);
                    }
                    break;

                case State.Fighting:
                    Fight(tip, input.trigger, rod);
                    break;

                case State.Retrieving:
                {
                    Hint = "";
                    float t = Mathf.Clamp01(m_StateTime / 0.5f);
                    m_Bobber.position = Vector3.Lerp(m_RetrieveFrom, tip, t * t);
                    m_Lure.position = m_Bobber.position + Vector3.up * 0.05f;
                    if (t >= 1) SetState(State.Idle);
                    break;
                }

                case State.Landed:
                    Hint = "";
                    ParkBobberAtTip(tip);
                    if (m_StateTime > 1.2f) SetState(State.Idle);
                    break;
            }

            if (CurrentState != State.Waiting && CurrentState != State.Bite) school.UpdateLure(m_Lure.position, false);
            UpdateLine(tip);
            UpdateRodBend();
        }

        Vector3 ComputeTip() => transform.TransformPoint(RodLocalTip(0, Vector3.zero));

        Vector3 RodLocalTip(float bend, Vector3 bendDirLocal)
        {
            // rod points along local +Z, tilted up ~20° from the controller
            Vector3 straight = Quaternion.Euler(-20f, 0, 0) * Vector3.forward * rodLength;
            return straight + bendDirLocal * (bend * rodLength * 0.3f);
        }

        void TrackTip(Vector3 tip)
        {
            m_TipHistory.Enqueue((tip, Time.time));
            while (m_TipHistory.Count > 0 && Time.time - m_TipHistory.Peek().t > 0.1f) m_TipHistory.Dequeue();
            m_TipWorld = tip;
        }

        Vector3 TipVelocity()
        {
            if (m_TipHistory.Count < 2) return Vector3.zero;
            var first = m_TipHistory.Peek();
            float dt = Time.time - first.t;
            return dt > 1e-4f ? (m_TipWorld - first.p) / dt : Vector3.zero;
        }

        float VerticalTipSpeed() => TipVelocity().y;

        void ParkBobberAtTip(Vector3 tip)
        {
            m_Bobber.position = tip + Vector3.up * 0.05f;
            m_Bobber.rotation = Quaternion.identity;
            m_Lure.position = m_Bobber.position + Vector3.up * 0.06f;
        }

        void Cast(Vector3 tip)
        {
            Vector3 v = TipVelocity();
            Vector3 rodDir = (tip - transform.position).normalized;
            if (v.magnitude < 1.2f) v = rodDir * 2.5f + Vector3.up; // gentle lob still floats up
            v = Vector3.ClampMagnitude(v * 1.3f, 9f);
            m_BobberVel = v;
            m_Bobber.position = tip;
            sfx?.Play(SfxBank.Cast, tip);
            XRInput.Haptic(hand, 0.3f, 0.05f);
            SetState(State.Flying);
        }

        void FlyBobber()
        {
            // anti-gravity: things fall up toward the ceiling sea
            m_BobberVel += Vector3.up * antiGravity * Time.deltaTime;
            m_BobberVel *= 1f - 0.6f * Time.deltaTime;
            Vector3 p = m_Bobber.position + m_BobberVel * Time.deltaTime;
            var room = water.Room;
            // walls: bounce softly back inside the ceiling outline
            var xz = new Vector2(p.x, p.z);
            if (!room.Contains(xz, 0.1f))
            {
                var c = room.ClampInside(xz, 0.12f);
                p.x = c.x; p.z = c.y;
                m_BobberVel.x *= -0.3f; m_BobberVel.z *= -0.3f;
            }
            m_Bobber.position = p;
            m_Lure.position = p + Vector3.up * 0.06f;
            if (p.y >= water.SurfaceY - 0.02f)
            {
                p.y = water.SurfaceY - 0.02f;
                m_Bobber.position = p;
                CeilingWater.AddRipple(p);
                sfx?.Play(SfxBank.Splash, p);
                SetState(State.Waiting);
            }
            if (m_StateTime > 4f) StartRetrieve();
        }

        void FloatBobber(float pulledIn)
        {
            Vector3 p = m_Bobber.position;
            float bob = Mathf.Sin(Time.time * 2.2f) * 0.006f;
            float twitch = Mathf.Max(0, m_Twitch) * 0.04f;
            m_Twitch -= Time.deltaTime * 4f;
            p.y = water.SurfaceY - 0.02f + bob + twitch + pulledIn;
            m_Bobber.position = Vector3.Lerp(m_Bobber.position, p, Time.deltaTime * 12f);
            m_Bobber.rotation = Quaternion.Euler(180, 0, 0); // hangs upside down under the surface
            m_Lure.position = m_Bobber.position + Vector3.up * (0.28f + Mathf.Sin(Time.time * 1.7f) * 0.02f);
        }
        float m_Twitch;

        void OnNibble(FishAgent f)
        {
            if (CurrentState != State.Waiting) return;
            m_Twitch = 1f;
            CeilingWater.AddRipple(m_Bobber.position);
            XRInput.Haptic(hand, 0.25f, 0.06f);
            sfx?.Play(SfxBank.Nibble, m_Bobber.position);
        }

        void OnBite(FishAgent f)
        {
            if (CurrentState != State.Waiting) return;
            m_BiteWindow = 1.0f;
            CeilingWater.AddRipple(m_Bobber.position);
            XRInput.Haptic(hand, 0.9f, 0.25f);
            sfx?.Play(SfxBank.Bite, m_Bobber.position);
            SetState(State.Bite);
        }

        void StartFight()
        {
            HookedFish = school.Engaged;
            if (HookedFish == null) { SetState(State.Waiting); return; }
            HookedFish.Hook();
            LineLength = Vector3.Distance(m_TipWorld, HookedFish.transform.position);
            Tension = 0.4f;
            m_OverTension = m_Slack = 0;
            m_FishWander = HookedFish.transform.position;
            XRInput.Haptic(hand, 1f, 0.15f);
            Message?.Invoke($"上钩了：{HookedFish.Species.displayName}！");
            SetState(State.Fighting);
        }

        void Fight(Vector3 tip, float reel, RodDef rod)
        {
            var fish = HookedFish;
            var sp = fish.Species;
            float dt = Time.deltaTime;
            bool tired = fish.Stamina <= 0;

            // fish fight pattern: random bursts
            m_BurstTimer -= dt;
            if (m_BurstTimer <= 0) { m_Burst = Random.value < 0.45f ? Random.Range(0.8f, 1.3f) : Random.Range(0.2f, 0.5f); m_BurstTimer = Random.Range(0.6f, 1.8f); }
            float pull = sp.strength * (tired ? 0.15f : m_Burst);

            float pumping = Mathf.Clamp01(-VerticalTipSpeed() / 2f);   // pulling the rod down helps
            float target = (pull * (0.35f + 0.9f * reel) + pumping * 0.35f * pull) / rod.lineStrength;
            if (LineLength >= maxLineLength) target += 0.6f;
            Tension = Mathf.MoveTowards(Tension, target, dt * 2.5f);

            LineLength += (pull * 0.45f * (1f - reel * 0.8f) - reel * rod.reelSpeed * (1f - pull * 0.5f) - pumping * 0.25f) * dt;
            LineLength = Mathf.Clamp(LineLength, 0.05f, maxLineLength);
            fish.Stamina -= Tension * dt * 1.4f;

            if (Tension > 1f) m_OverTension += dt; else m_OverTension = Mathf.Max(0, m_OverTension - dt * 0.5f);
            if (Tension < 0.06f && !tired) m_Slack += dt; else m_Slack = Mathf.Max(0, m_Slack - dt);

            Hint = Tension > 0.85f ? "张力太大！松开扳机！" : Tension < 0.15f && !tired ? "鱼线太松了，收线！" : tired ? "鱼累了，快收线！" : "扣扳机收线，向下压竿拉鱼";

            // haptics + reel clicks
            m_HapticTimer -= dt;
            if (m_HapticTimer <= 0) { XRInput.Haptic(hand, Mathf.Clamp01(Tension * 0.7f), 0.06f); m_HapticTimer = 0.05f; }
            if (reel > 0.1f) { m_ReelClick -= dt * reel * 12f; if (m_ReelClick <= 0) { sfx?.Play(SfxBank.Reel, tip, 0.25f); m_ReelClick = 1f; } }
            m_Reel.Rotate(Vector3.forward, reel * 900f * dt, Space.Self);

            // fish position: hanging on the line, thrashing sideways in the sea
            m_WanderTimer -= dt;
            if (m_WanderTimer <= 0)
            {
                var p = water.Room.RandomPointInside(0.3f);
                m_FishWander = new Vector3(p.x, 0, p.y);
                m_WanderTimer = Random.Range(0.8f, 2f);
            }
            float vertNeeded = water.SurfaceY + 0.05f - tip.y;
            if (LineLength <= vertNeeded + 0.02f) { LandFish(); return; }

            Vector3 tipXZ = new Vector3(tip.x, 0, tip.z);
            Vector3 wish = Vector3.ClampMagnitude(m_FishWander - tipXZ, Mathf.Sqrt(Mathf.Max(0, LineLength * LineLength - vertNeeded * vertNeeded)));
            Vector3 fishXZ = Vector3.Lerp(new Vector3(fish.transform.position.x, 0, fish.transform.position.z), tipXZ + wish, dt * (tired ? 0.5f : 1.5f));
            float horiz = Vector3.Distance(fishXZ, tipXZ);
            float y = tip.y + Mathf.Sqrt(Mathf.Max(vertNeeded * vertNeeded, LineLength * LineLength - horiz * horiz));
            y = Mathf.Clamp(y, water.SurfaceY + 0.05f, water.SeabedY - 0.1f);
            var clamped = water.Room.ClampInside(new Vector2(fishXZ.x, fishXZ.z), 0.15f);
            Vector3 fishPos = new Vector3(clamped.x, y, clamped.y);
            Vector3 swimDir = (fishPos - fish.transform.position);
            fish.DriveHooked(fishPos, swimDir.sqrMagnitude > 1e-5f ? swimDir : (fishPos - tip), tired ? 0.2f : pull);
            if (Random.value < dt * 2f) CeilingWater.AddRipple(new Vector3(fishPos.x, water.SurfaceY, fishPos.z));

            m_Bobber.position = Vector3.Lerp(m_Bobber.position, fishPos + (tip - fishPos).normalized * 0.3f, dt * 10f);
            m_Lure.position = fishPos;

            if (m_OverTension > 0.6f)
            {
                Message?.Invoke("啪！鱼线断了……");
                sfx?.Play(SfxBank.Snap, tip);
                XRInput.Haptic(hand, 1f, 0.3f);
                EndFight(true);
            }
            else if (m_Slack > 3f)
            {
                Message?.Invoke("鱼挣脱了鱼钩……");
                EndFight(true);
            }
        }

        void EndFight(bool escaped)
        {
            HookedFish = null;
            school.ReleaseEngaged(escaped);
            Tension = 0;
            StartRetrieve();
        }

        void LandFish()
        {
            var fish = school.LandEngaged();
            HookedFish = null;
            Tension = 0;
            if (fish != null)
            {
                Vector3 splash = new Vector3(fish.transform.position.x, water.SurfaceY, fish.transform.position.z);
                CeilingWater.AddRipple(splash);
                sfx?.Play(SfxBank.Splash, splash);
                sfx?.Play(SfxBank.Catch, splash);
                XRInput.Haptic(hand, 0.8f, 0.2f);
                collection.RegisterCatch(fish.Species, fish.Species.length * Random.Range(0.85f, 1.25f));
                Landed?.Invoke(fish);
            }
            SetState(State.Landed);
        }

        void StartRetrieve()
        {
            m_RetrieveFrom = m_Bobber.position;
            SetState(State.Retrieving);
        }

        // -------------------- visuals --------------------

        void UpdateLine(Vector3 tip)
        {
            bool show = CurrentState != State.Idle && CurrentState != State.Charging && CurrentState != State.Landed;
            m_Line.enabled = show;
            m_Leader.enabled = show && CurrentState != State.Retrieving;
            m_Bobber.gameObject.SetActive(true);
            if (!show) return;

            Vector3 end = m_Bobber.position;
            // slack line bows UPWARD (anti-gravity), straightens with tension
            float slack = CurrentState == State.Fighting ? (1f - Mathf.Clamp01(Tension)) * 0.25f : 0.2f;
            if (CurrentState == State.Flying || CurrentState == State.Retrieving) slack = 0.05f;
            Vector3 mid = (tip + end) * 0.5f + Vector3.up * slack * Vector3.Distance(tip, end);
            int n = m_Line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                Vector3 a = Vector3.Lerp(tip, mid, t), b = Vector3.Lerp(mid, end, t);
                m_Line.SetPosition(i, Vector3.Lerp(a, b, t));
            }
            m_Leader.SetPosition(0, m_Bobber.position);
            m_Leader.SetPosition(1, m_Lure.position);
        }

        void UpdateRodBend()
        {
            float bend = 0; Vector3 dirLocal = Vector3.zero;
            if (CurrentState == State.Fighting || CurrentState == State.Bite)
            {
                bend = CurrentState == State.Bite ? 0.4f : Mathf.Clamp01(Tension) * 0.9f;
                dirLocal = transform.InverseTransformDirection((m_Bobber.position - m_TipWorld).normalized);
            }
            RebuildRodMesh(bend, dirLocal);
        }

        void RebuildRodMesh(float bend, Vector3 bendDirLocal)
        {
            if (m_RodMesh == null) return;
            var rod = collection.CurrentRod;
            m_Path.Clear(); m_Radii.Clear();
            Vector3 handle = new Vector3(0, 0, -0.12f);
            Vector3 tip = RodLocalTip(bend, bendDirLocal);
            Vector3 straightTip = RodLocalTip(0, Vector3.zero);
            const int N = 10;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                Vector3 p = Vector3.Lerp(handle, straightTip, t);
                p += (tip - straightTip) * (t * t); // quadratic bend toward the line
                m_Path.Add(p);
                m_Radii.Add(Mathf.Lerp(0.016f, 0.003f, Mathf.Pow(t, 0.7f)));
            }
            var k = new MeshKit();
            k.Tube(m_Path.GetRange(0, 3), m_Radii.GetRange(0, 3), rod.accent, 8);   // grip
            k.Tube(m_Path.GetRange(2, N - 1), m_Radii.GetRange(2, N - 1), rod.color, 8);
            k.Ellipsoid(m_Path[N], Vector3.one * 0.008f, rod.accent, rod.accent, 4, 6);
            var mesh = m_RodMesh.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(k.v); mesh.SetNormals(k.n); mesh.SetColors(k.c); mesh.SetTriangles(k.t, 0);
            mesh.RecalculateBounds();
        }
    }
}
