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

        [Tooltip("The free hand that turns the reel crank (and its visual, snapped to the knob).")]
        public Transform leftHand;
        public Transform leftHandVisual;
        [Tooltip("Takes landed fish off the hook with the free hand and into the aquarium.")]
        public FishCarrier carrier;
        [Tooltip("Line retrieved per crank turn (m) for the base rod; scaled by the rod's reelSpeed.")]
        public float linePerTurn = 0.2f;

        public State CurrentState { get; private set; }
        public float Tension { get; private set; }
        /// <summary>World position of the (bent) rod tip, updated every frame.</summary>
        public Vector3 TipWorld => m_TipWorld;
        /// <summary>Drag knob 0 (loose) .. 1 (locked). Left thumbstick up/down.</summary>
        public float Drag { get; private set; } = 0.5f;
        /// <summary>Tension (0..1 = line strength) at which the spool starts slipping.</summary>
        public float DragLimit => Mathf.Lerp(0.38f, 1.15f, Drag);
        public bool DragSlipping { get; private set; }
        public ReelCrank Crank => m_Crank;
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
        float m_OverTension;
        Vector3 m_FishWander;
        float m_WanderTimer;
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

            if (TryBuildRealisticRod())
            {
                var knob = new Material(m_BlankMat); knob.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.1f)); knob.SetFloat("_Smoothness", 0.35f);
                BuildCrank(m_Reel.parent, m_Model.crankPivot, m_Model.crankSide, m_GuideMat, knob);
                BuildTackle();
                return;
            }

            var reel = new MeshKit();
            reel.Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.035f, 0.02f), new Color(0.95f, 0.95f, 1f), new Color(0.7f, 0.75f, 0.85f), 6, 10);
            reel.Box(new Vector3(0.03f, 0, 0), new Vector3(0.03f, 0.008f, 0.008f), new Color(1f, 0.85f, 0.3f));
            m_Reel = MeshKit.Spawn("Reel", reel.Build("Reel"), UDFAssets.Toon_(Color.white), transform).transform;
            m_Reel.localPosition = new Vector3(0, -0.04f, 0.12f);
            m_Reel.localRotation = Quaternion.Euler(0, 90, 0);
            BuildCrank(transform, m_Reel.localPosition + new Vector3(-0.022f, 0, 0), -1f, UDFAssets.Toon_(new Color(0.9f, 0.9f, 0.95f)), UDFAssets.Toon_(new Color(1f, 0.85f, 0.3f)));
            BuildTackle();
        }

        ReelCrank m_Crank;

        void BuildCrank(Transform parent, Vector3 localPos, float side, Material armMat, Material knobMat)
        {
            var go = new GameObject("ReelCrank");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            m_Crank = go.AddComponent<ReelCrank>();
            m_Crank.side = side;
            m_Crank.hand = hand == Hand.Right ? Hand.Left : Hand.Right;
            m_Crank.handTransform = leftHand;
            m_Crank.handVisual = leftHandVisual;
            m_Crank.Build(armMat, knobMat);
            m_Crank.Tick += () => sfx?.Play(SfxBank.Reel, m_Crank.KnobPosition, DragSlipping ? 0.12f : 0.22f);
        }

        void BuildTackle()
        {
            // Tripo models (same glossy style as the fish); simple primitives as a fallback
            var bobModel = TackleModels.Get("bobber", 0.075f);
            if (bobModel != null)
                m_Bobber = MeshKit.Spawn("Bobber", bobModel.mesh, new Material(bobModel.material), null).transform;
            else
            {
                var bob = new MeshKit();
                bob.Ellipsoid(new Vector3(0, 0.012f, 0), new Vector3(0.025f, 0.02f, 0.025f), Color.white, Color.white, 5, 10);
                bob.Ellipsoid(new Vector3(0, -0.01f, 0), new Vector3(0.026f, 0.022f, 0.026f), new Color(1f, 0.3f, 0.35f), new Color(1f, 0.3f, 0.35f), 5, 10);
                bob.Tube(new[] { new Vector3(0, 0.03f, 0), new Vector3(0, 0.065f, 0) }, new[] { 0.004f, 0.002f }, new Color(1f, 0.85f, 0.3f), 5);
                m_Bobber = MeshKit.Spawn("Bobber", bob.Build("Bobber"), UDFAssets.Toon_(Color.white), null).transform;
            }

            var lureModel = TackleModels.Get("lure", 0.055f);
            if (lureModel != null)
                m_Lure = MeshKit.Spawn("Lure", lureModel.mesh, new Material(lureModel.material), null).transform;
            else
            {
                var lure = new MeshKit();
                lure.Ellipsoid(Vector3.zero, new Vector3(0.012f, 0.018f, 0.012f), new Color(1f, 0.95f, 0.6f), new Color(1f, 0.6f, 0.3f), 4, 8);
                m_Lure = MeshKit.Spawn("Lure", lure.Build("Lure"), UDFAssets.Toon_(Color.white, new Color(0.5f, 0.35f, 0.1f)), null).transform;
            }

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
                    Hint = "Hold the trigger, flick the rod UP and let go to cast at the ceiling!";
                    ParkBobberAtTip(tip);
                    if (triggerPressed) SetState(State.Charging);
                    break;

                case State.Charging:
                    Hint = "Flick up! Then release the trigger";
                    ParkBobberAtTip(tip);
                    if (triggerReleased) Cast(tip);
                    break;

                case State.Flying:
                    Hint = "";
                    FlyBobber();
                    break;

                case State.Waiting:
                {
                    // a few slow crank turns twitch the lure (fish notice movement);
                    // two full turns reel it back in
                    float turns = m_Crank != null ? m_Crank.ConsumeTurns() : 0f;
                    if (m_StateTime < 0.4f) { turns = 0; m_WaitTurns = 0; }
                    m_WaitTurns += turns;
                    if (turns > 0.05f) m_Twitch = Mathf.Max(m_Twitch, 0.6f);
                    Hint = m_Crank != null && m_Crank.Grabbed ? "Crank slowly to twitch the lure... two full turns reels it in"
                         : "Waiting for a bite... (grab the crank with your left hand to twitch / reel in)";
                    FloatBobber(0f);
                    school.UpdateLure(m_Lure.position, true);
                    if (m_WaitTurns >= 2f || (triggerPressed && m_StateTime > 0.4f)) StartRetrieve();
                    break;
                }

                case State.Bite:
                    Hint = "Fish on! Yank the rod DOWN to set the hook!";
                    m_BiteWindow -= Time.deltaTime;
                    FloatBobber(0.12f);
                    float downSpeed = -VerticalTipSpeed();
                    bool easyBite = school.Engaged != null && school.Engaged.Species.beginner;
                    if (downSpeed > (easyBite ? 0.6f : 0.9f)) StartFight();
                    else if (m_BiteWindow <= 0 && easyBite)
                    {
                        // a beginner fish doesn't let go: it keeps nibbling and bites again
                        Message?.Invoke("Missed! It's still nibbling. Yank again!");
                        SetState(State.Waiting);
                    }
                    else if (m_BiteWindow <= 0)
                    {
                        Message?.Invoke("It got away... be quicker next time!");
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
                    UpdateLanded(tip);
                    break;
            }

            if (CurrentState != State.Waiting && CurrentState != State.Bite) school.UpdateLure(m_Lure.position, false);
            UpdateRodBend();
            UpdateLine(m_BentTip);
            // the bait wobbles and slowly turns on the leader, like a live shrimp
            if (m_Lure != null && CurrentState != State.Fighting && !(CurrentState == State.Landed && m_Dangle != null))
                m_Lure.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.3f) * 14f, Time.time * 35f, Mathf.Sin(Time.time * 3.1f) * 10f);
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
                SplashFx.Play(p, 0.35f);
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
        float m_WaitTurns;
        float m_DragBuzz;

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
            m_BiteWindow = f.Species.beginner ? 2.2f : 1.0f;
            CeilingWater.AddRipple(m_Bobber.position);
            XRInput.Haptic(hand, 0.9f, 0.25f);
            sfx?.Play(SfxBank.Bite, m_Bobber.position);
            SetState(State.Bite);
        }

        FishFight m_Fight;
        Vector3 m_MoveTipStart;
        float m_CueTimer;
        /// <summary>The running fight (phases, current move, stamina, hook hold); null when not fighting.</summary>
        public FishFight CurrentFight => m_Fight;

        void StartFight()
        {
            HookedFish = school.Engaged;
            if (HookedFish == null) { SetState(State.Waiting); return; }
            HookedFish.Hook();
            LineLength = Vector3.Distance(m_TipWorld, HookedFish.transform.position);
            Tension = 0.4f;
            m_OverTension = 0;
            m_FishWander = HookedFish.transform.position;
            m_Fight = new FishFight(HookedFish.Species);
            m_Fight.MoveStarted += OnMoveStarted;
            m_Fight.MoveEnded += OnMoveEnded;
            m_Fight.JumpPenalty += () => { Tension += 0.55f; Message?.Invoke("No bow! The hook worked loose"); XRInput.Haptic(hand, 1f, 0.2f); };
            OnMoveStarted(m_Fight.Move);
            XRInput.Haptic(hand, 1f, 0.15f);
            Message?.Invoke($"Hooked: {HookedFish.Species.displayName}!");
            SetState(State.Fighting);
        }

        Vector3 HeadRight()
        {
            var cam = Camera.main;
            Vector3 r = cam != null ? cam.transform.right : transform.right;
            r.y = 0;
            return r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right;
        }

        void OnMoveStarted(FishMove m)
        {
            m_MoveTipStart = m_TipWorld;
            m_CueTimer = 0;
            var fish = HookedFish;
            if (fish == null) return;
            Vector3 fp = fish.transform.position;
            Vector3 away = fp - m_TipWorld; away.y = 0; away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
            switch (m)
            {
                case FishMove.Rush:
                    SetWander(fp + away * 1.4f);
                    sfx?.Play(SfxBank.Reel, fp, 0.3f);
                    break;
                case FishMove.SideRun:
                    SetWander(fp + HeadRight() * (m_Fight.SideDir * 1.6f));
                    XRInput.Haptic(hand, 0.6f, 0.1f);
                    break;
                case FishMove.HeadShake:
                    sfx?.Play(SfxBank.Nibble, fp, 0.8f);
                    break;
                case FishMove.Jump:
                    SetWander(fp);   // holds position while winding up
                    break;
            }
        }

        void OnMoveEnded(FishMove m, bool good)
        {
            if (!good || HookedFish == null) return;
            switch (m)
            {
                case FishMove.SideRun: Message?.Invoke("Nice side pressure!"); break;
                case FishMove.HeadShake: Message?.Invoke("Held through the head shake!"); break;
                case FishMove.Jump: Message?.Invoke("Perfect bow!"); break;
            }
        }

        void SetWander(Vector3 p)
        {
            var c = water.Room.ClampInside(new Vector2(p.x, p.z), 0.3f);
            m_FishWander = new Vector3(c.x, 0, c.y);
            m_WanderTimer = 3f;
        }

        /// <summary>
        /// Line physics around the <see cref="FishFight"/> brain (tension 1.0 = breaking strain):
        ///   * the fish pulls with the current move's force (+ head-shake pulses)
        ///   * cranking retrieves line and adds tension; pumping (rod pulled away) drags it closer
        ///   * above the DRAG setting the spool slips and line pays out; drag too tight -> snap
        ///   * running out of line snaps it; a loose hook (HookHold 0) lets the fish go
        ///   * the fish cannot be landed until it is tired (it fights at the surface instead)
        /// </summary>
        void Fight(Vector3 tip, float trigger, RodDef rod)
        {
            var fish = HookedFish;
            var sp = fish.Species;
            var f = m_Fight;
            float dt = Time.deltaTime;
            var other = XRInput.Get(m_Crank != null ? m_Crank.hand : Hand.Left);

            // drag knob on the free hand's thumbstick
            if (Mathf.Abs(other.stick.y) > 0.3f)
            {
                float before = Drag;
                Drag = Mathf.Clamp01(Drag + other.stick.y * dt * 0.45f);
                if (Mathf.FloorToInt(before * 10) != Mathf.FloorToInt(Drag * 10)) { XRInput.Haptic(m_Crank != null ? m_Crank.hand : hand, 0.2f, 0.02f); sfx?.Play(SfxBank.Reel, tip, 0.1f); }
            }

            // ---- player actions
            bool cranking = m_Crank != null && m_Crank.Grabbed;
            float turnsPerSec = cranking ? Mathf.Min(m_Crank.TurnsPerSecond, 3.5f) : 0f;
            if (!other.tracked && m_Crank != null && ReelCrank.SimulatedTurnsPerSecond <= 0) { turnsPerSec = trigger * 2f; cranking = trigger > 0.1f; }
            m_Crank?.ConsumeTurns();
            float effort = Mathf.Clamp01(turnsPerSec / 2.2f);
            float reelIn = turnsPerSec * linePerTurn * (rod.reelSpeed / 0.35f);
            float pumping = Mathf.Clamp01(-VerticalTipSpeed() / 2f);
            Vector3 toFish = fish.transform.position - tip;
            float towardFish = toFish.sqrMagnitude > 1e-4f ? Vector3.Dot(TipVelocity(), toFish.normalized) : 0f;
            float sideOffset = Vector3.Dot(tip - m_MoveTipStart, HeadRight());

            // ---- fish brain
            f.Update(new FishFight.Input { dt = dt, effort = effort, tension = Tension, pumping = pumping, sideOffset = sideOffset, towardFish = towardFish, cranking = cranking });
            if (HookedFish == null || m_Fight == null) return;   // a callback ended the fight
            fish.Stamina = f.Stamina * sp.stamina;   // keep the agent in sync
            bool tired = f.Phase == FightPhase.Tired;
            float pull = sp.strength * f.PullFactor;
            float shake = f.ShakePulse * 0.35f * sp.strength;

            // ---- tension
            // reeling itself keeps the line taut (even against a small, weak fish)
            float target = (pull * (0.3f + 0.75f * effort) + pumping * 0.45f * pull + shake) / rod.lineStrength + effort * 0.25f;
            if (f.Move == FishMove.Rush && effort > 0.35f) target += 0.35f * effort;     // cranking into a run
            if (LineLength >= maxLineLength - 0.01f) target += 0.8f;                       // spooled
            Tension = Mathf.MoveTowards(Tension, target, dt * 2.5f);

            float limit = DragLimit;
            DragSlipping = Tension > limit && LineLength < maxLineLength - 0.01f;
            float payout = 0;
            if (DragSlipping)
            {
                payout = (Tension - limit) * 2.2f + pull * 0.2f;
                Tension = Mathf.Lerp(Tension, limit, 1f - Mathf.Exp(-dt * 6f));
                reelIn *= 0.05f;
                m_DragBuzz -= dt * (8f + payout * 30f);
                if (m_DragBuzz <= 0) { m_DragBuzz = 1f; sfx?.Play(SfxBank.Reel, tip, 0.18f); XRInput.Haptic(m_Crank != null ? m_Crank.hand : hand, 0.5f, 0.02f); }
            }
            if (f.Move == FishMove.Rest) reelIn *= 1.25f;    // the fish isn't resisting: line comes easily

            // ---- line length; an untired fish refuses to be pulled out (it thrashes at the surface)
            float vertNeeded = water.SurfaceY + 0.05f - tip.y;
            LineLength += (pull * 0.35f * (1f - effort * 0.7f) + payout - reelIn * (1f - pull * 0.4f) - pumping * 0.3f) * dt;
            float minLine = tired ? 0.05f : vertNeeded + 0.25f + 0.6f * Mathf.Clamp01(f.Stamina);
            LineLength = Mathf.Clamp(LineLength, Mathf.Min(minLine, maxLineLength), maxLineLength);
            if (tired && LineLength <= vertNeeded + 0.02f) { LandFish(); return; }

            if (Tension > 1f) m_OverTension += dt; else m_OverTension = Mathf.Max(0, m_OverTension - dt * 0.5f);

            // ---- feedback per move (right hand = the fish, left hand = the reel)
            m_CueTimer -= dt;
            if (f.Move == FishMove.HeadShake)
            {
                if (m_CueTimer <= 0) { XRInput.Haptic(hand, 0.9f, 0.04f); m_CueTimer = 0.12f; }
            }
            else if (f.JumpTelegraph)
            {
                float k = f.MoveTime / FishFight.JumpWindup;
                if (m_CueTimer <= 0) { XRInput.Haptic(hand, Mathf.Lerp(0.2f, 1f, k), 0.04f); m_CueTimer = Mathf.Lerp(0.25f, 0.07f, k); }
            }
            else if (m_CueTimer <= 0) { XRInput.Haptic(hand, Mathf.Clamp01(Tension * 0.6f), 0.06f); m_CueTimer = 0.05f; }
            m_Reel.Rotate(Vector3.forward, turnsPerSec * 360f * dt, Space.Self);
            Hint = FightHint(f, cranking, other.tracked, tired);

            // ---- fish position: on the line, steered by the current move
            m_WanderTimer -= dt;
            if (m_WanderTimer <= 0)
            {
                var p = water.Room.RandomPointInside(0.3f);
                m_FishWander = new Vector3(p.x, 0, p.y);
                m_WanderTimer = Random.Range(0.8f, 2f);
            }
            Vector3 tipXZ = new Vector3(tip.x, 0, tip.z);
            Vector3 wish = Vector3.ClampMagnitude(m_FishWander - tipXZ, Mathf.Sqrt(Mathf.Max(0, LineLength * LineLength - vertNeeded * vertNeeded)));
            float steer = tired ? 0.5f : (f.Move == FishMove.SideRun || f.Move == FishMove.Rush) ? 2.2f : 1.5f;
            Vector3 fishXZ = Vector3.Lerp(new Vector3(fish.transform.position.x, 0, fish.transform.position.z), tipXZ + wish, dt * steer);
            float horiz = Vector3.Distance(fishXZ, tipXZ);
            float y = tip.y + Mathf.Sqrt(Mathf.Max(vertNeeded * vertNeeded, LineLength * LineLength - horiz * horiz));
            y = Mathf.Clamp(y, water.SurfaceY + 0.05f, water.SeabedY - 0.1f);
            var clamped = water.Room.ClampInside(new Vector2(fishXZ.x, fishXZ.z), 0.15f);
            Vector3 fishPos = new Vector3(clamped.x, y, clamped.y);

            float air = f.JumpAir;
            if (f.JumpTelegraph) fishPos.y = Mathf.Lerp(fishPos.y, water.SurfaceY + 0.12f, f.MoveTime / FishFight.JumpWindup);
            if (air >= 0)
            {
                // leap out through the surface into the room, then fall back up into the sea
                Vector3 surf = new Vector3(fishPos.x, water.SurfaceY, fishPos.z);
                if (!m_JumpSplashed) { m_JumpSplashed = true; SplashFx.Play(surf, 1.4f); sfx?.Play(SfxBank.Splash, surf); XRInput.Haptic(hand, 1f, 0.2f); }
                Vector3 toTip = tip - surf; toTip.y = 0; toTip = toTip.sqrMagnitude > 1e-4f ? toTip.normalized : Vector3.forward;
                fishPos = surf + Vector3.down * (Mathf.Sin(air * Mathf.PI) * (0.35f + sp.length * 0.6f)) + toTip * (air * 0.15f);
                Vector3 look = Vector3.Lerp(Vector3.down, Vector3.up, air) + toTip * 0.4f;
                fish.transform.rotation = Quaternion.LookRotation(look.normalized, -toTip) * Quaternion.Euler(0, 0, Mathf.Sin(air * 30f) * 30f);
                fish.transform.position = fishPos;
                if (air > 0.96f && !m_JumpReentered) { m_JumpReentered = true; SplashFx.Play(surf, 0.9f); }
            }
            else
            {
                m_JumpSplashed = m_JumpReentered = false;
                Vector3 swimDir = fishPos - fish.transform.position;
                float thrash = tired ? 0.2f : f.Move == FishMove.HeadShake ? 1.2f : pull;
                fish.DriveHooked(fishPos, swimDir.sqrMagnitude > 1e-5f ? swimDir : (fishPos - tip), thrash);
            }
            if (Random.value < dt * 2f) CeilingWater.AddRipple(new Vector3(fishPos.x, water.SurfaceY, fishPos.z));
            bool nearSurface = fishPos.y < water.SurfaceY + 0.12f + sp.length * 0.3f;
            if (air < 0 && nearSurface && Random.value < dt * (f.Move == FishMove.HeadShake ? 9f : tired ? 1f : 3f))
                SplashFx.Play(new Vector3(fishPos.x, water.SurfaceY, fishPos.z), Mathf.Lerp(0.25f, 0.7f, sp.strength));

            m_Bobber.position = Vector3.Lerp(m_Bobber.position, fishPos + (tip - fishPos).normalized * 0.3f, dt * 10f);
            m_Lure.position = fishPos;

            // ---- outcomes
            if (m_OverTension > (sp.beginner ? 2f : 0.6f))
            {
                Message?.Invoke("Snap! The line broke...");
                sfx?.Play(SfxBank.Snap, tip);
                XRInput.Haptic(hand, 1f, 0.3f);
                EndFight(true);
            }
            else if (f.HookHold <= 0f)
            {
                Message?.Invoke("The hook came loose. It got away...");
                sfx?.Play(SfxBank.Splash, fishPos);
                EndFight(true);
            }
        }
        bool m_JumpSplashed, m_JumpReentered;

        string FightHint(FishFight f, bool cranking, bool otherTracked, bool tired)
        {
            int dragPct = Mathf.RoundToInt(Drag * 100);
            if (m_Crank != null && !cranking && otherTracked && f.Move != FishMove.Rush && !f.JumpTelegraph && f.JumpAir < 0)
                return "Grab the reel crank with your left hand (grip) and wind it in circles!";
            if (Tension > 0.95f) return $"Line's about to snap! Reel slower, or push the left stick down to loosen the drag ({dragPct}%)";
            if (LineLength > maxLineLength * 0.85f) return "Almost out of line! Push the left stick up to tighten the drag";
            if (tired) return "It's worn out! Reel it in and pull it out of the water!";
            switch (f.Move)
            {
                case FishMove.Rush:
                    if (!f.CounterOk) return "Stop cranking! You'll snap the line";
                    return f.Phase == FightPhase.LastStand ? "Last stand! Hold steady, don't crank!" : "It's making a run! Stop cranking, keep the rod up";
                case FishMove.Rest:
                    return "It's resting. Reel fast now!";
                case FishMove.SideRun:
                {
                    string run = f.SideDir > 0 ? "right" : "left", back = f.SideDir > 0 ? "left" : "right";
                    return f.CounterOk ? $"Hold it! Keep pulling it back to the {back}" : $"It's running {run}! Pull the rod to the {back}";
                }
                case FishMove.HeadShake:
                    return f.CounterOk ? "Head shake! Keep the line tight!" : "Line's too slack, the hook is slipping! Reel!";
                case FishMove.Jump:
                    return f.JumpTelegraph ? "It's about to jump! Get ready to bow" : "Bow! Thrust the rod tip toward the fish!";
            }
            if (f.HookHold < 0.4f) return "The hook is loosening. Keep the line tight!";
            return "Pull the rod down to drag it closer, reel as you raise it again";
        }

        void EndFight(bool escaped)
        {
            m_Fight = null;
            HookedFish = null;
            school.ReleaseEngaged(escaped);
            Tension = 0;
            DragSlipping = false;
            StartRetrieve();
        }

        // ---- landing: the fish bursts out of the sea and dangles on the taut line in a shower
        // of spray (the "hero moment"), then drops free into the room and is registered.
        const float DangleTime = 1.4f;
        FishAgent m_Dangle;
        Vector3 m_DangleFrom;
        float m_DripTimer;

        void LandFish()
        {
            var fish = HookedFish;
            DragSlipping = false;
            m_Fight = null;
            if (fish == null) { SetState(State.Landed); return; }
            m_Dangle = fish;
            m_Hanging = false;
            m_DangleFrom = fish.transform.position;
            Vector3 splash = new Vector3(m_DangleFrom.x, water.SurfaceY, m_DangleFrom.z);
            SplashFx.Play(splash, 1.8f);
            sfx?.Play(SfxBank.Splash, splash);
            XRInput.Haptic(hand, 0.9f, 0.25f);
            Tension = 0.6f;
            SetState(State.Landed);
        }

        /// <summary>Debug (editor simulator key H): hook the nearest fish and start a fight.</summary>
        public void DebugHook()
        {
            if (water == null || water.Room == null || CurrentState == State.Fighting || CurrentState == State.Landed) return;
            FishAgent pick = null; float best = float.MaxValue;
            foreach (var f in FindObjectsByType<FishAgent>(FindObjectsSortMode.None))
            {
                if (f.CurrentState != FishAgent.State.Swimming) continue;
                float d = (f.transform.position - m_TipWorld).sqrMagnitude;
                if (d < best) { best = d; pick = f; }
            }
            if (pick == null) return;
            school.DebugEngage(pick);
            StartFight();
        }

        /// <summary>Debug (editor simulator key L): hook the nearest fish and land it right away.</summary>
        public void DebugLand()
        {
            if (water == null || water.Room == null || CurrentState == State.Landed) return;
            FishAgent pick = null; float best = float.MaxValue;
            foreach (var f in FindObjectsByType<FishAgent>(FindObjectsSortMode.None))
            {
                if (f.CurrentState != FishAgent.State.Swimming) continue;
                float d = (f.transform.position - m_TipWorld).sqrMagnitude;
                if (d < best) { best = d; pick = f; }
            }
            if (pick == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var xz = water.Room.ClampInside(new Vector2(m_TipWorld.x + fwd.x * 0.5f, m_TipWorld.z + fwd.z * 0.5f), 0.3f);
            pick.transform.position = new Vector3(xz.x, water.SurfaceY + 0.12f, xz.y);
            school.DebugEngage(pick);
            pick.Hook();
            HookedFish = pick;
            LandFish();
        }

        void UpdateLanded(Vector3 tip)
        {
            if (m_Dangle == null) { ParkBobberAtTip(tip); if (m_StateTime > 1.2f) SetState(State.Idle); return; }
            float t = m_StateTime, dt = Time.deltaTime;
            var fish = m_Dangle;
            float L = fish.Species.length;
            if (t < DangleTime)
            {
                Hint = "It's out!";
                // hook point: from the break-out spot, swung toward the angler on the taut line
                Vector3 surf = new Vector3(m_DangleFrom.x, water.SurfaceY - 0.03f, m_DangleFrom.z);
                float pullIn = Mathf.SmoothStep(0f, 0.3f, t / DangleTime);
                Vector3 hook = Vector3.Lerp(surf, tip, pullIn) + Vector3.down * (0.06f + Mathf.Sin(t * 5f) * 0.015f);
                Vector3 toTip = (tip - hook); toTip.y = 0;
                if (toTip.sqrMagnitude < 1e-4f) toTip = Vector3.forward;
                Vector3 swingAxis = Vector3.Cross(Vector3.up, toTip.normalized);
                float damp = 1f - 0.5f * t / DangleTime;
                Quaternion rot = Quaternion.AngleAxis(Mathf.Sin(t * 13f) * 28f * damp, swingAxis)
                               * Quaternion.LookRotation(Vector3.up, toTip.normalized)            // hangs by the mouth
                               * Quaternion.Euler(0, 0, Mathf.Sin(t * 21f) * 40f * damp);         // body thrash
                fish.transform.rotation = rot;
                fish.transform.position = hook - fish.transform.forward * L * 0.5f;
                m_Lure.position = hook;
                m_Bobber.position = Vector3.Lerp(m_Bobber.position, hook + (tip - hook).normalized * 0.3f, dt * 10f);
                Tension = Mathf.Lerp(0.6f, 0.3f, t / DangleTime);
                m_DripTimer -= dt;
                if (m_DripTimer <= 0) { SplashFx.Drip(fish.transform.position - fish.transform.forward * L * 0.35f); m_DripTimer = 0.09f; }
                if (Random.value < dt * 5f) XRInput.Haptic(hand, 0.5f, 0.04f);
                return;
            }
            if (carrier == null)
            {
                // no carrier (fallback): register the catch, the fish drops free into the room
                var landed = school.LandEngaged();
                HookedFish = null;
                Tension = 0;
                m_Dangle = null;
                if (landed != null)
                {
                    SplashFx.Drip(landed.transform.position);
                    sfx?.Play(SfxBank.Catch, landed.transform.position);
                    XRInput.Haptic(hand, 0.6f, 0.15f);
                    collection.RegisterCatch(landed.Species, landed.Species.length * Random.Range(0.85f, 1.25f));
                    Landed?.Invoke(landed);
                }
                SetState(State.Landed);
                return;
            }
            UpdateHanging(m_BentTip, fish, dt);
        }

        // ---- after the burst: the fish is reeled up short under the rod tip and swings there on
        // the line (pendulum), until the free hand grabs it (see FishCarrier) and takes it off.
        const float HangLength = 0.3f;
        bool m_Hanging;
        Vector3 m_Mouth, m_MouthVel;
        float m_HangLen0, m_HangTime, m_Thrash;

        void UpdateHanging(Vector3 tip, FishAgent fish, float dt)
        {
            float L = fish.Species.length;
            if (!m_Hanging)
            {
                m_Hanging = true;
                school.TakeEngaged();          // it has left the sea for good
                m_Mouth = m_Lure.position;
                m_MouthVel = Vector3.zero;
                m_HangLen0 = Mathf.Max(Vector3.Distance(m_Mouth, tip), HangLength);
                m_HangTime = 0;
                m_Thrash = 0.4f;
            }
            if (carrier.Held == fish)
            {
                // the hand took it off the hook: wind the empty line back in
                m_Hanging = false;
                m_Dangle = null;
                HookedFish = null;
                Tension = 0;
                Hint = "";
                XRInput.Haptic(hand, 0.3f, 0.06f);
                StartRetrieve();
                return;
            }

            m_HangTime += dt;
            float hang = Mathf.Lerp(m_HangLen0, HangLength, Mathf.SmoothStep(0f, 1f, m_HangTime / 0.9f));
            m_MouthVel += Vector3.down * 6.5f * dt;
            m_MouthVel *= Mathf.Exp(-1.3f * dt);
            m_Mouth += m_MouthVel * dt;
            Vector3 d = m_Mouth - tip;
            float len = d.magnitude;
            if (len > hang && len > 1e-4f)
            {
                Vector3 dir = d / len;
                m_Mouth = tip + dir * hang;
                float outward = Vector3.Dot(m_MouthVel, dir);
                if (outward > 0) m_MouthVel -= dir * outward;
            }
            // now and then it thrashes on the line
            m_Thrash -= dt;
            if (m_Thrash < -Random.Range(1.4f, 3f))
            {
                m_Thrash = 0.5f;
                m_MouthVel += Random.insideUnitSphere * 0.5f;
                XRInput.Haptic(hand, 0.4f, 0.15f);
                SplashFx.Drip(fish.transform.position);
            }
            float thr = Mathf.Clamp01(m_Thrash * 2f);

            Vector3 up = tip - m_Mouth;
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            Vector3 face = Vector3.ProjectOnPlane(transform.forward, up);
            if (face.sqrMagnitude < 1e-4f) face = Vector3.ProjectOnPlane(Vector3.forward, up);
            float t = m_HangTime;
            fish.transform.rotation = Quaternion.LookRotation(up, -face.normalized)
                                    * Quaternion.Euler(0, Mathf.Sin(t * 21f) * 35f * thr + Mathf.Sin(t * 2.3f) * 6f, 0);
            fish.transform.position = m_Mouth - fish.transform.forward * L * 0.5f;
            m_Lure.position = m_Mouth;
            m_Bobber.position = Vector3.Lerp(m_Bobber.position, m_Mouth + up * Mathf.Min(0.12f, hang * 0.5f), dt * 10f);
            Tension = 0.22f + thr * 0.15f;
            if (m_HangTime < 2.5f)
            {
                m_DripTimer -= dt;
                if (m_DripTimer <= 0) { SplashFx.Drip(fish.transform.position - fish.transform.forward * L * 0.35f); m_DripTimer = 0.25f; }
            }

            if (m_HangTime > 0.4f) carrier.Offer(fish);
            Hint = carrier.Near == fish ? "Squeeze the grip to grab it!" : "Swing the fish close and reach for it with your left hand";
        }

        void StartRetrieve()
        {
            m_RetrieveFrom = m_Bobber.position;
            SetState(State.Retrieving);
        }

        // -------------------- visuals --------------------

        void UpdateLine(Vector3 tip)
        {
            bool show = CurrentState != State.Idle && CurrentState != State.Charging && (CurrentState != State.Landed || m_Dangle != null);
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
            bool dangling = CurrentState == State.Landed && m_Dangle != null;
            if (CurrentState == State.Fighting || CurrentState == State.Bite || dangling)
            {
                bend = CurrentState == State.Bite ? 0.4f
                     : dangling ? (m_Hanging ? 0.3f + Mathf.Clamp01(m_Thrash * 2f) * 0.15f : 0.75f + Mathf.Sin(m_StateTime * 13f) * 0.12f)
                     : Mathf.Clamp01(Tension) * 0.9f;
                dirLocal = transform.InverseTransformDirection((m_Bobber.position - m_TipWorld).normalized);
            }
            RebuildRodMesh(bend, dirLocal);
            // the line leaves the BENT tip (where the tip-top actually is)
            m_BentTip = transform.TransformPoint(RodLocalTip(bend, dirLocal));
        }

        Vector3 m_BentTip;

        // ------------------------------------------------------------------
        // Realistic rod: Tripo-generated butt section (grip + reel seat + spinning reel) plus a
        // procedural tapered carbon blank with guides, thread wraps and a tip-top, which bends.

        static readonly Quaternion k_RodTilt = Quaternion.Euler(-20f, 0, 0);
        static readonly float[] k_GuideT = { 0.16f, 0.33f, 0.49f, 0.63f, 0.76f, 0.88f };
        static readonly float[] k_GuideR = { 0.017f, 0.011f, 0.0075f, 0.0058f, 0.0048f, 0.0042f };
        RodModel m_Model;
        Vector3 m_ButtPos;
        Material m_BlankMat, m_GuideMat, m_WrapMat;
        readonly MeshKit[] m_Kits = { new MeshKit(), new MeshKit(), new MeshKit() };

        bool TryBuildRealisticRod()
        {
            m_Model = RodModel.Load();
            var blank = Resources.Load<Material>("UDF/Models/RodBlank");
            var guide = Resources.Load<Material>("UDF/Models/RodGuide");
            var wrap = Resources.Load<Material>("UDF/Models/RodWrap");
            if (m_Model == null || blank == null || guide == null || wrap == null) { m_Model = null; return false; }

            // grip held in the palm, reel stem just in front of the fingers, on the rod axis
            m_ButtPos = k_RodTilt * new Vector3(0, 0, 0.03f);
            var butt = new GameObject("RodButt");
            butt.transform.SetParent(transform, false);
            butt.transform.localPosition = m_ButtPos;
            butt.transform.localRotation = k_RodTilt;
            butt.AddComponent<MeshFilter>().sharedMesh = m_Model.mesh;
            var mr = butt.AddComponent<MeshRenderer>();
            mr.sharedMaterials = m_Model.materials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_BlankMat = new Material(blank);
            m_GuideMat = new Material(guide);
            m_WrapMat = new Material(wrap);
            m_RodRenderer.sharedMaterials = new[] { m_BlankMat, m_GuideMat, m_WrapMat };
            m_RodRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // the reel is part of the scanned model; keep an empty pivot so reeling code still works
            m_Reel = new GameObject("ReelPivot").transform;
            m_Reel.SetParent(butt.transform, false);
            return true;
        }

        void RebuildRealisticRod(float bend, Vector3 bendDirLocal)
        {
            var rod = collection.CurrentRod;
            // glossy navy blank with gold trim; the rod tier shows as a subtle tint + wrap colour
            m_BlankMat.SetColor("_BaseColor", Color.Lerp(new Color(0.03f, 0.06f, 0.2f), rod.color, 0.18f));
            m_WrapMat.SetColor("_BaseColor", Color.Lerp(new Color(0.95f, 0.72f, 0.28f), rod.accent, 0.3f));

            foreach (var kit in m_Kits) { kit.v.Clear(); kit.n.Clear(); kit.c.Clear(); kit.uv.Clear(); kit.t.Clear(); }
            MeshKit blankK = m_Kits[0], guideK = m_Kits[1], wrapK = m_Kits[2];

            // the blank starts a few cm inside the reel seat so the joint is hidden
            Vector3 seatEnd = m_ButtPos + k_RodTilt * m_Model.cutPoint;
            Vector3 start = m_ButtPos + k_RodTilt * (m_Model.cutPoint - new Vector3(0, 0, 0.035f));
            Vector3 tip = RodLocalTip(bend, bendDirLocal);
            Vector3 straightTip = RodLocalTip(0, Vector3.zero);
            Vector3 down0 = k_RodTilt * Vector3.down;
            float r0 = Mathf.Clamp(m_Model.cutRadius * 0.7f, 0.0055f, 0.0095f);

            const int N = 16;
            m_Path.Clear(); m_Radii.Clear();
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                m_Path.Add(Vector3.Lerp(start, straightTip, t) + (tip - straightTip) * (t * t));
                m_Radii.Add(Mathf.Lerp(r0, 0.0019f, Mathf.Pow(t, 0.85f)));
            }
            blankK.Tube(m_Path, m_Radii, Color.white, 10);
            // winding check: slim collar where the blank leaves the reel seat
            Vector3 axis0 = (straightTip - start).normalized;
            blankK.Tube(new[] { seatEnd - axis0 * 0.004f, seatEnd + axis0 * 0.006f }, new[] { r0 * 1.3f, r0 * 1.15f }, Color.white, 12);

            // guides hang on the reel side (spinning rod), rings face along the blank
            for (int g = 0; g < k_GuideT.Length; g++)
            {
                float t = k_GuideT[g];
                Vector3 p = PathAt(start, straightTip, tip, t);
                Vector3 fwd = (PathAt(start, straightTip, tip, t + 0.01f) - p).normalized;
                Vector3 down = Vector3.ProjectOnPlane(down0, fwd).normalized;
                Vector3 side = Vector3.Cross(fwd, down);
                float br = Mathf.Lerp(r0, 0.0019f, Mathf.Pow(t, 0.85f));
                float ringR = k_GuideR[g];
                float stand = br + ringR * 0.55f + 0.004f;
                Vector3 c = p + down * (stand + ringR);

                // ring
                var ring = new List<Vector3>(); var ringRad = new List<float>();
                for (int s = 0; s <= 14; s++)
                {
                    float a = s / 14f * Mathf.PI * 2f;
                    ring.Add(c + (down * Mathf.Cos(a) + side * Mathf.Sin(a)) * ringR);
                    ringRad.Add(Mathf.Max(0.0008f, ringR * 0.12f));
                }
                guideK.Tube(ring, ringRad, Color.white, 5);
                // two-leg frame
                float leg = Mathf.Max(0.0006f, ringR * 0.07f);
                Vector3 top = c - down * ringR;
                guideK.Tube(new[] { p + down * br * 0.8f - fwd * ringR * 0.9f, top }, new[] { leg, leg }, Color.white, 4);
                guideK.Tube(new[] { p + down * br * 0.8f + fwd * ringR * 0.5f, top }, new[] { leg, leg }, Color.white, 4);
                // thread wraps either side of the foot
                float wl = Mathf.Max(0.006f, ringR * 0.7f);
                wrapK.Tube(new[] { p - fwd * (ringR * 0.9f + wl), p - fwd * ringR * 0.5f }, new[] { br * 1.12f, br * 1.12f }, Color.white, 10);
                wrapK.Tube(new[] { p + fwd * ringR * 0.3f, p + fwd * (ringR * 0.5f + wl) }, new[] { br * 1.12f, br * 1.12f }, Color.white, 10);
            }
            // hook keeper wrap at the start of the blank and the tip-top
            wrapK.Tube(new[] { seatEnd + axis0 * 0.006f, seatEnd + axis0 * 0.026f }, new[] { r0 * 1.08f, r0 * 1.08f }, Color.white, 10);
            Vector3 tipFwd = (m_Path[N] - m_Path[N - 1]).normalized;
            var tipRing = new List<Vector3>(); var tipRad = new List<float>();
            Vector3 tDown = Vector3.ProjectOnPlane(down0, tipFwd).normalized, tSide = Vector3.Cross(tipFwd, tDown);
            Vector3 tc = tip + tipFwd * 0.004f;
            for (int s = 0; s <= 10; s++)
            {
                float a = s / 10f * Mathf.PI * 2f;
                tipRing.Add(tc + (tDown * Mathf.Cos(a) + tSide * Mathf.Sin(a)) * 0.0032f);
                tipRad.Add(0.0007f);
            }
            guideK.Tube(tipRing, tipRad, Color.white, 4);
            guideK.Tube(new[] { tip - tipFwd * 0.006f, tc - tipFwd * 0.001f }, new[] { 0.0021f, 0.0016f }, Color.white, 6);

            var mesh = m_RodMesh.sharedMesh;
            mesh.Clear();
            var verts = new List<Vector3>(blankK.v.Count + guideK.v.Count + wrapK.v.Count);
            var norms = new List<Vector3>(verts.Capacity);
            int[] offsets = new int[3];
            for (int k = 0; k < 3; k++) { offsets[k] = verts.Count; verts.AddRange(m_Kits[k].v); norms.AddRange(m_Kits[k].n); }
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.subMeshCount = 3;
            for (int k = 0; k < 3; k++)
            {
                var tris = m_Kits[k].t;
                for (int i = 0; i < tris.Count; i++) tris[i] += offsets[k];
                mesh.SetTriangles(tris, k);
            }
            mesh.RecalculateBounds();
        }

        static Vector3 PathAt(Vector3 start, Vector3 straightTip, Vector3 tip, float t)
            => Vector3.Lerp(start, straightTip, t) + (tip - straightTip) * (t * t);

        void RebuildRodMesh(float bend, Vector3 bendDirLocal)
        {
            if (m_RodMesh == null) return;
            if (m_Model != null) { RebuildRealisticRod(bend, bendDirLocal); return; }
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
