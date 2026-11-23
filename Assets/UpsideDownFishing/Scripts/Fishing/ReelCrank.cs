using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// The spinning reel's crank, turned by the OTHER hand like a real reel.
    ///
    ///   * Bring the left controller near the knob and hold grip (or trigger) to grab it.
    ///   * While held, the crank follows the hand's angle around the crank shaft; the
    ///     turning speed drives line retrieval (<see cref="TurnsPerSecond"/>).
    ///   * Every 1/10 turn the ratchet clicks (<see cref="Tick"/>) with a light haptic.
    ///   * The hand visual snaps onto the knob while cranking.
    ///
    /// The shaft is this transform's local X axis; the arm points along local -Y at angle 0.
    /// </summary>
    public class ReelCrank : MonoBehaviour
    {
        public Hand hand = Hand.Left;
        public Transform handTransform;
        public Transform handVisual;
        public float side = -1f;              // which way the knob sticks out (local X sign)
        public float armLength = 0.042f;
        public float grabRadius = 0.15f;
        /// <summary>Set by the fish carrier while the hand is reaching for / holding a fish.</summary>
        [System.NonSerialized] public bool blockGrab;

        /// <summary>Editor simulator: >0 cranks automatically at this many turns/s.</summary>
        public static float SimulatedTurnsPerSecond;

        public bool Grabbed { get; private set; }
        public bool HandNear { get; private set; }
        public float TurnsPerSecond { get; private set; }
        /// <summary>Total turns since last <see cref="ConsumeTurns"/>.</summary>
        public float PendingTurns { get; private set; }
        public System.Action Tick;
        public System.Action<bool> GrabChanged;

        Transform m_Arm, m_Knob;
        Material m_KnobMat;
        Color m_KnobColor;
        float m_Angle, m_PrevHandAngle, m_TickAccum, m_RawRate;
        bool m_GripWas;
        Vector3 m_VisualLocalPos; Quaternion m_VisualLocalRot;

        public Vector3 KnobPosition => m_Knob.position;

        public void Build(Material armMat, Material knobMat)
        {
            m_Arm = new GameObject("CrankArm").transform;
            m_Arm.SetParent(transform, false);

            var arm = new MeshKit();
            float out1 = side * 0.006f, out2 = side * 0.012f;
            // shaft cap, arm (slightly flattened), knob stem
            arm.Tube(new[] { Vector3.zero, new Vector3(out1, 0, 0) }, new[] { 0.007f, 0.007f }, Color.white, 12);
            arm.Box(new Vector3(out1, -armLength * 0.5f, 0), new Vector3(0.005f, armLength, 0.009f), Color.white);
            arm.Tube(new[] { new Vector3(out1, -armLength, 0), new Vector3(out2, -armLength, 0) }, new[] { 0.003f, 0.003f }, Color.white, 8);
            MeshKit.Spawn("Arm", arm.Build("CrankArm"), armMat, m_Arm);

            var knob = new MeshKit();
            knob.Ellipsoid(Vector3.zero, new Vector3(0.011f, 0.0085f, 0.0085f), Color.white, Color.white, 6, 12);
            m_Knob = MeshKit.Spawn("Knob", knob.Build("CrankKnob"), knobMat, m_Arm).transform;
            m_Knob.localPosition = new Vector3(out2 + side * 0.011f, -armLength, 0);
            m_KnobMat = knobMat;
            m_KnobColor = knobMat.HasProperty("_BaseColor") ? knobMat.GetColor("_BaseColor") : Color.gray;
        }

        /// <summary>Returns and clears the accumulated turns.</summary>
        public float ConsumeTurns() { float t = PendingTurns; PendingTurns = 0; return t; }

        void LateUpdate()
        {
            if (m_Arm == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float deltaDeg = 0;

            if (SimulatedTurnsPerSecond > 0)
            {
                SetGrabbed(true);
                deltaDeg = SimulatedTurnsPerSecond * 360f * dt;
            }
            else
            {
                var s = XRInput.Get(hand);
                bool grip = s.tracked && (s.grip > 0.55f || s.trigger > 0.55f);
                Vector3 handPos = handTransform != null ? handTransform.position : Vector3.zero;
                HandNear = s.tracked && Vector3.Distance(handPos, m_Knob.position) < grabRadius;
                if (!Grabbed && grip && !m_GripWas && HandNear && !blockGrab) SetGrabbed(true);
                else if (Grabbed && !grip) SetGrabbed(false);
                m_GripWas = grip;

                if (Grabbed)
                {
                    float a = HandAngle(handPos, out bool valid);
                    if (valid)
                    {
                        deltaDeg = Mathf.DeltaAngle(m_PrevHandAngle, a);
                        m_PrevHandAngle = a;
                    }
                }
            }

            // a real reel's anti-reverse would block backwards turns; we accept both directions
            // (much less frustrating in VR) but the crank visibly follows the hand either way
            m_Angle += deltaDeg;
            m_Arm.localRotation = Quaternion.AngleAxis(m_Angle, Vector3.right);
            float turns = Mathf.Abs(deltaDeg) / 360f;
            PendingTurns += turns;
            m_RawRate = turns / dt;
            TurnsPerSecond = Mathf.Lerp(TurnsPerSecond, Grabbed ? m_RawRate : 0f, 1f - Mathf.Exp(-dt * 8f));

            m_TickAccum += turns * 10f;
            while (m_TickAccum >= 1f)
            {
                m_TickAccum -= 1f;
                Tick?.Invoke();
                XRInput.Haptic(hand, 0.12f, 0.01f);
            }

            // hand visual rides on the knob
            if (handVisual != null && Grabbed) handVisual.position = m_Knob.position;

            // discoverability: knob glows when the free hand is close enough to grab it
            if (m_KnobMat != null)
            {
                float glow = Grabbed ? 0.35f : HandNear ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
                m_KnobMat.SetColor("_BaseColor", Color.Lerp(m_KnobColor, new Color(0.4f, 0.95f, 1f), glow * 0.7f));
            }
        }

        float HandAngle(Vector3 handPos, out bool valid)
        {
            Vector3 axis = transform.right;
            Vector3 rel = handPos - transform.position;
            Vector3 proj = rel - axis * Vector3.Dot(rel, axis);
            valid = proj.magnitude > 0.012f;
            return Vector3.SignedAngle(-transform.up, proj, axis);
        }

        void SetGrabbed(bool g)
        {
            if (Grabbed == g) return;
            Grabbed = g;
            if (g)
            {
                if (handTransform != null) m_PrevHandAngle = HandAngle(handTransform.position, out _);
                if (handVisual != null) { m_VisualLocalPos = handVisual.localPosition; m_VisualLocalRot = handVisual.localRotation; }
                XRInput.Haptic(hand, 0.35f, 0.04f);
            }
            else
            {
                if (handVisual != null) { handVisual.localPosition = m_VisualLocalPos; handVisual.localRotation = m_VisualLocalRot; }
                TurnsPerSecond = 0;
            }
            GrabChanged?.Invoke(g);
        }
    }
}
