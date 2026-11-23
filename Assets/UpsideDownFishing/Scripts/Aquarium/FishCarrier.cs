using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// The free (left) hand picks up fish: the one dangling on the line, a loose one in the room,
    /// or one swimming in the aquarium (reach into the water).
    ///   * Within reach a round "grab" hand icon pops up over the fish.
    ///   * Grip (or trigger) grabs it — the icon closes into a fist, the fish thrashes in hand.
    ///   * Let go over the tank (the glass rim glows gold): it splashes in and stays.
    ///   * Let go anywhere else: it is released back to the wild — it floats up into the ceiling sea and
    ///     swims off. A fresh catch counts as caught either way; taking one out of the tank and
    ///     releasing it does not count again.
    /// </summary>
    public class FishCarrier : MonoBehaviour
    {
        public Hand hand = Hand.Left;
        public Transform handTransform;
        public Aquarium aquarium;
        public CollectionManager collection;
        public SfxBank sfx;
        public FishSchool school;
        public Transform head;
        public FishingRod rod;
        ReelCrank crank => rod != null ? rod.Crank : null;

        public float grabRadius = 0.16f;

        public FishAgent Held { get; private set; }
        public FishAgent Near { get; private set; }
        public string Hint { get; private set; } = "";
        /// <summary>A fish went into the tank (the catch is registered).</summary>
        public System.Action<FishAgent> Stored;
        public System.Action<string> Message;
        /// <summary>A fish was released back to the sea. Bool: it was a fresh catch (registered just now).</summary>
        public System.Action<FishAgent, bool> Released;

        /// <summary>Editor/testing: overrides the grip button when set.</summary>
        public static bool? SimulatedGrip;

        FishAgent m_Offered;
        TankFish m_NearTank;
        bool m_HeldFromTank;
        int m_OfferFrame = -10;
        bool m_GripWas;
        float m_GrabWindow;
        Vector3 m_GrabLocalPos;
        Quaternion m_GrabLocalRot;
        float m_HeldTime, m_ClosedIconTimer, m_ThrashTimer;
        Vector3 m_PrevHandPos, m_HandVel;

        Transform m_Icon;
        Material m_IconMat;
        Texture2D m_OpenTex, m_ClosedTex;
        float m_IconScale;

        /// <summary>The rod offers the fish hanging on the line each frame it can be grabbed.</summary>
        public void Offer(FishAgent fish) { m_Offered = fish; m_OfferFrame = Time.frameCount; }

        void Start()
        {
            m_OpenTex = Resources.Load<Texture2D>("UDF/Icons/GrabHandOpen");
            m_ClosedTex = Resources.Load<Texture2D>("UDF/Icons/GrabHandClosed");
            var quad = new Mesh { name = "IconQuad" };
            quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateBounds();
            m_IconMat = UDFAssets.New(UDFAssets.Icon);
            m_IconMat.SetTexture("_BaseMap", m_OpenTex);
            var go = MeshKit.Spawn("GrabIcon", quad, m_IconMat, null);
            go.GetComponent<MeshRenderer>().sortingOrder = 10;
            m_Icon = go.transform;
            m_Icon.gameObject.SetActive(false);
        }

        void OnDestroy() { if (m_Icon != null) Destroy(m_Icon.gameObject); }

        bool GripDown()
        {
            if (SimulatedGrip.HasValue) return SimulatedGrip.Value;
            var s = XRInput.Get(hand);
            return s.tracked && (s.grip > 0.55f || s.trigger > 0.55f);
        }

        /// <summary>Distance from the hand to the fish's body (a capsule along its length).</summary>
        static float BodyDistance(FishAgent f, Vector3 p) => BodyDistance(f.transform, f.Species.length, p);

        static float BodyDistance(Transform tr, float L, Vector3 p)
        {
            Vector3 a = tr.position + tr.forward * L * 0.45f;
            Vector3 b = tr.position - tr.forward * L * 0.45f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Mathf.Max(0f, Vector3.Distance(p, a + ab * t) - L * 0.12f);
        }

        void LateUpdate()
        {
            if (handTransform == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 hp = handTransform.position;
            m_HandVel = Vector3.Lerp(m_HandVel, (hp - m_PrevHandPos) / dt, 0.5f);
            m_PrevHandPos = hp;
            bool grip = GripDown();
            bool gripPressed = grip && !m_GripWas;
            m_GripWas = grip;
            // a grab a moment too early still counts if the swinging fish comes into the hand
            if (gripPressed) m_GrabWindow = 0.4f;
            m_GrabWindow = grip ? m_GrabWindow - dt : 0f;
            bool crankBusy = crank != null && crank.Grabbed;

            if (Held == null)
            {
                Hint = "";
                // find the closest grabbable fish
                FishAgent best = null;
                TankFish bestTank = null;
                float bestD = grabRadius;
                if (!crankBusy)
                {
                    if (m_Offered != null && Time.frameCount - m_OfferFrame <= 1)
                    {
                        float d = BodyDistance(m_Offered, hp);
                        if (d < bestD) { bestD = d; best = m_Offered; }
                    }
                    foreach (var f in FishAgent.All)
                    {
                        if (f == null || !f.IsLoose) continue;
                        float d = BodyDistance(f, hp);
                        if (d < bestD) { bestD = d; best = f; }
                    }
                    // fish in the aquarium: reach into the water
                    if (aquarium != null)
                        foreach (var tf in aquarium.Fish)
                        {
                            if (tf == null || !tf.Grabbable || tf.Agent == null) continue;
                            float d = BodyDistance(tf.transform, tf.Length, hp);
                            if (d < bestD) { bestD = d; best = tf.Agent; bestTank = tf; }
                        }
                }
                if (best != null && best != Near) XRInput.Haptic(hand, 0.25f, 0.03f);
                Near = best;
                m_NearTank = bestTank;
                if (Near != null && grip && m_GrabWindow > 0f)
                {
                    bool fromTank = m_NearTank != null;
                    var f = fromTank ? aquarium.TakeFish(m_NearTank) : Near;
                    m_NearTank = null;
                    if (f != null)
                    {
                        if (fromTank) sfx?.Play(SfxBank.Splash, f.transform.position, 0.35f);
                        Grab(f, fromTank);
                    }
                    else Near = null;
                }
            }
            else
            {
                Near = null;
                m_NearTank = null;
                CarryUpdate(dt, grip);
            }
            if (crank != null) crank.blockGrab = Near != null || Held != null;
            UpdateIcon(dt);
        }

        void Grab(FishAgent f, bool fromTank = false)
        {
            Held = f;
            m_HeldFromTank = fromTank;
            Near = null;
            if (f.CurrentState != FishAgent.State.Held) f.Hold();
            // keep the fish's pose relative to the hand, but pull its body into the palm
            Vector3 local = handTransform.InverseTransformPoint(f.transform.position);
            float maxOff = f.Species.length * 0.25f;
            m_GrabLocalPos = Vector3.ClampMagnitude(local, maxOff);
            m_GrabLocalRot = Quaternion.Inverse(handTransform.rotation) * f.transform.rotation;
            m_HeldTime = 0;
            m_ClosedIconTimer = 0.5f;
            m_ThrashTimer = 0.4f;
            XRInput.Haptic(hand, 0.7f, 0.12f);
            sfx?.Play(SfxBank.Nibble, f.transform.position, 0.6f);
        }

        void CarryUpdate(float dt, bool grip)
        {
            var f = Held;
            if (f == null) { Held = null; return; }
            m_HeldTime += dt;
            // smooth into the hand, then thrash now and then
            float k = 1f - Mathf.Exp(-dt * 12f);
            Vector3 target = handTransform.TransformPoint(m_GrabLocalPos);
            f.transform.position = m_HeldTime < 0.3f ? Vector3.Lerp(f.transform.position, target, k) : target;
            float thrash = Mathf.Sin(m_HeldTime * 22f) * 18f * Mathf.Clamp01(m_ThrashTimer * 2f);
            f.transform.rotation = handTransform.rotation * m_GrabLocalRot * Quaternion.Euler(0, thrash, 0);
            m_ThrashTimer -= dt;
            if (m_ThrashTimer < -Random.Range(1.2f, 2.6f))
            {
                m_ThrashTimer = 0.45f;
                XRInput.Haptic(hand, 0.45f, 0.2f);
                if (Random.value < 0.6f) SplashFx.Drip(f.transform.position);
            }

            // upside-down tank: sealed lid, open bottom - fish are lifted up into the water from below
            bool over = aquarium != null && aquarium.IsUnderOpening(f.transform.position);
            aquarium?.SetHighlight(over ? 1f : 0.35f);
            if (m_HeldFromTank) Hint = over ? "Let go to put it back in the tank" : "Lift it back into the tank from below, or let go here to release it";
            else Hint = over ? "Let go to drop it in the tank!" : "Lift it into the tank from below to keep it, or let go anywhere to release it";

            if (grip) return;
            // let go
            Held = null;
            Hint = "";
            aquarium?.SetHighlight(0f);
            bool fresh = !m_HeldFromTank;
            m_HeldFromTank = false;
            if (over)
            {
                aquarium.AddFish(f);
                sfx?.Play(SfxBank.Splash, f.transform.position, 0.7f);
                XRInput.Haptic(hand, 0.6f, 0.15f);
                if (fresh)
                {
                    sfx?.Play(SfxBank.Catch, f.transform.position);
                    collection?.RegisterCatch(f.Species, f.Species.length * Random.Range(0.85f, 1.25f));
                    Stored?.Invoke(f);
                }
                else Message?.Invoke($"{f.Species.displayName} is back in the tank");
            }
            else
            {
                // release: a gentle upward toss, carried a little by the hand's swing
                Vector3 toss = Vector3.ClampMagnitude(m_HandVel, 2.5f) * 0.7f + Vector3.up * 0.8f;
                f.ReturnedToSea = a => { school?.Adopt(a); sfx?.Play(SfxBank.Splash, a.transform.position, 0.6f); };
                f.Release(toss);
                XRInput.Haptic(hand, 0.4f, 0.1f);
                sfx?.Play(SfxBank.Unlock, f.transform.position, 0.5f);
                if (fresh) collection?.RegisterCatch(f.Species, f.Species.length * Random.Range(0.85f, 1.25f));
                Released?.Invoke(f, fresh);
            }
        }

        void UpdateIcon(float dt)
        {
            if (m_Icon == null) return;
            m_ClosedIconTimer -= dt;
            FishAgent target = Near != null ? Near : (Held != null && m_ClosedIconTimer > 0 ? Held : null);
            bool show = target != null;
            m_IconScale = Mathf.MoveTowards(m_IconScale, show ? 1f : 0f, dt * 8f);
            m_Icon.gameObject.SetActive(m_IconScale > 0.01f);
            if (!show && m_IconScale <= 0.01f) return;
            if (target == null) return;
            m_IconMat.SetTexture("_BaseMap", Near != null ? m_OpenTex : m_ClosedTex);

            Vector3 eye = head != null ? head.position : Camera.main.transform.position;
            Vector3 fp = target.transform.position;
            Vector3 toEye = (eye - fp).normalized;
            float L = target.Species.length;
            // just above the fish, nudged toward the viewer so it never hides behind the body
            Vector3 pos = fp + Vector3.up * (L * 0.25f + 0.07f) + toEye * (L * 0.2f + 0.03f);
            m_Icon.position = pos;
            m_Icon.rotation = Quaternion.LookRotation(pos - eye, Vector3.up);
            float pulse = Near != null ? 1f + Mathf.Sin(Time.time * 7f) * 0.08f : 1.1f;
            float ease = 1f - (1f - m_IconScale) * (1f - m_IconScale);
            m_Icon.localScale = Vector3.one * 0.065f * pulse * ease;
        }
    }
}
