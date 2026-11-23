using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace UpsideDownFishing
{
    /// <summary>
    /// Mouse/keyboard stand-in for the headset when no XR device is active (Editor Play mode).
    ///   Right mouse drag : look around        WASD/QE : move head
    ///   Mouse            : aim rod            Left mouse : trigger (hold, flick mouse up, release = cast; hold = reel)
    ///   B : switch rod   Y : collection book  X : rescan room
    ///   R : crank the reel (Shift = faster)   Mouse wheel : drag knob   H : hook a fish   L : test a landing
    ///   G : left grip (grab a fish / let go under the open bottom of the aquarium)
    /// </summary>
    public class EditorSimulator : MonoBehaviour
    {
        public Camera head;
        public Transform trackingSpace;
        float m_Yaw, m_Pitch = 15f;
        Vector3 m_PrevRight, m_PrevLeft;
        bool m_Active;

        void Start()
        {
            m_Active = !XRSettings.isDeviceActive;
            if (!m_Active) { enabled = false; return; }
            foreach (var b in head.GetComponents<Behaviour>())
                if (b.GetType().Name == "TrackedPoseDriver") b.enabled = false;
            head.transform.localPosition = new Vector3(0, 1.6f, 0);
            XRInput.SimulatedSource = Sample;
            Debug.Log("[UDF] Editor simulator active: RMB look, WASD move, LMB trigger, B rod, Y book, X rescan.");
        }

        void OnDestroy() { if (m_Active) { XRInput.SimulatedSource = null; ReelCrank.SimulatedTurnsPerSecond = 0; } }

        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null || kb == null) return;
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * 0.15f;
                m_Yaw += d.x; m_Pitch = Mathf.Clamp(m_Pitch - d.y, -80, 80);
            }
            head.transform.localRotation = Quaternion.Euler(m_Pitch, m_Yaw, 0);
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move += Vector3.down;
            head.transform.localPosition += Quaternion.Euler(0, m_Yaw, 0) * move * (1.5f * Time.deltaTime);
            // R = crank the reel with the left hand (Shift+R = fast); scroll wheel = drag knob
            ReelCrank.SimulatedTurnsPerSecond = kb.rKey.isPressed ? (kb.leftShiftKey.isPressed ? 2.6f : 1.4f) : 0f;
            // L = test the landing moment (hooks and lands the nearest fish)
            if (kb.lKey.wasPressedThisFrame) TestLanding();
            // H = hook the nearest fish and start a fight
            if (kb.hKey.wasPressedThisFrame) TestHook();
        }

        public static void TestHook()
        {
            var rod = FindAnyObjectByType<FishingRod>();
            if (rod != null) rod.DebugHook();
        }

        /// <summary>Same as pressing L; callable from editor tooling.</summary>
        public static void TestLanding()
        {
            var rod = FindAnyObjectByType<FishingRod>();
            if (rod != null) rod.DebugLand();
        }

        /// <summary>Testing: put the simulated left controller at this world position (G = grip).</summary>
        public static Vector3? LeftHandWorldOverride;

        XRInput.HandState Sample(Hand hand)
        {
            var s = new XRInput.HandState { tracked = true };
            var mouse = Mouse.current; var kb = Keyboard.current;
            Transform h = head.transform;
            if (hand == Hand.Right)
            {
                Vector3 world = h.position + h.right * 0.18f - h.up * 0.22f + h.forward * 0.3f;
                Vector3 aim = h.forward;
                if (mouse != null)
                {
                    Ray r = head.ScreenPointToRay(mouse.position.ReadValue());
                    aim = r.direction;
                }
                s.localPosition = trackingSpace.InverseTransformPoint(world);
                // rod mesh is tilted 20° up from the controller, compensate so it follows the cursor
                s.localRotation = Quaternion.Inverse(trackingSpace.rotation) * Quaternion.LookRotation(aim, Vector3.up) * Quaternion.Euler(20f, 0, 0);
                s.localVelocity = (s.localPosition - m_PrevRight) / Mathf.Max(Time.deltaTime, 1e-4f);
                m_PrevRight = s.localPosition;
                s.trigger = mouse != null && mouse.leftButton.isPressed ? 1f : 0f;
                s.secondaryButton = kb != null && kb.bKey.isPressed;
                s.primaryButton = kb != null && kb.aKey.isPressed && kb.leftShiftKey.isPressed;
            }
            else
            {
                Vector3 world = LeftHandWorldOverride ?? (h.position - h.right * 0.2f - h.up * 0.3f + h.forward * 0.35f);
                s.grip = kb != null && kb.gKey.isPressed ? 1f : 0f;
                s.localPosition = trackingSpace.InverseTransformPoint(world);
                s.localRotation = Quaternion.Inverse(trackingSpace.rotation) * Quaternion.LookRotation(-h.up + h.forward * 0.6f, h.forward);
                s.localVelocity = (s.localPosition - m_PrevLeft) / Mathf.Max(Time.deltaTime, 1e-4f);
                m_PrevLeft = s.localPosition;
                s.secondaryButton = kb != null && kb.yKey.isPressed;
                s.primaryButton = kb != null && kb.xKey.isPressed;
                if (mouse != null) s.stick = new Vector2(0, Mathf.Clamp(mouse.scroll.ReadValue().y * 0.5f, -1f, 1f));
            }
            return s;
        }
    }
}
