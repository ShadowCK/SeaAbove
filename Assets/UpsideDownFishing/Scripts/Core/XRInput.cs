using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace UpsideDownFishing
{
    public enum Hand { Left, Right }

    /// <summary>
    /// Thin wrapper over UnityEngine.XR.InputDevices for PICO controllers, with an
    /// optional editor simulator (see <see cref="EditorSimulator"/>) that overrides values
    /// when no headset is connected.
    /// </summary>
    public static class XRInput
    {
        public struct HandState
        {
            public bool tracked;
            public Vector3 localPosition;     // in tracking space (Camera Offset)
            public Quaternion localRotation;
            public Vector3 localVelocity;
            public float trigger;
            public float grip;
            public bool primaryButton;        // A / X
            public bool secondaryButton;      // B / Y
            public Vector2 stick;
        }

        static readonly List<InputDevice> s_Devices = new List<InputDevice>();
        static HandState s_Left, s_Right, s_PrevLeft, s_PrevRight;
        static int s_Frame = -1;

        /// <summary>Set by the editor simulator; when non-null it replaces device input.</summary>
        public static System.Func<Hand, HandState> SimulatedSource;

        public static HandState Get(Hand hand)
        {
            Refresh();
            return hand == Hand.Left ? s_Left : s_Right;
        }

        public static bool PrimaryDown(Hand hand)
        {
            Refresh();
            return hand == Hand.Left ? s_Left.primaryButton && !s_PrevLeft.primaryButton
                                     : s_Right.primaryButton && !s_PrevRight.primaryButton;
        }

        public static bool SecondaryDown(Hand hand)
        {
            Refresh();
            return hand == Hand.Left ? s_Left.secondaryButton && !s_PrevLeft.secondaryButton
                                     : s_Right.secondaryButton && !s_PrevRight.secondaryButton;
        }

        public static void Haptic(Hand hand, float amplitude, float seconds)
        {
            if (SimulatedSource != null) return;
            var device = InputDevices.GetDeviceAtXRNode(hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.isValid && device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), seconds);
        }

        static void Refresh()
        {
            if (s_Frame == Time.frameCount) return;
            s_Frame = Time.frameCount;
            s_PrevLeft = s_Left;
            s_PrevRight = s_Right;
            if (SimulatedSource != null)
            {
                s_Left = SimulatedSource(Hand.Left);
                s_Right = SimulatedSource(Hand.Right);
                return;
            }
            s_Left = Read(XRNode.LeftHand);
            s_Right = Read(XRNode.RightHand);
        }

        static HandState Read(XRNode node)
        {
            var s = new HandState { localRotation = Quaternion.identity };
            s_Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, s_Devices);
            if (s_Devices.Count == 0) return s;
            var d = s_Devices[0];
            d.TryGetFeatureValue(CommonUsages.isTracked, out s.tracked);
            d.TryGetFeatureValue(CommonUsages.devicePosition, out s.localPosition);
            d.TryGetFeatureValue(CommonUsages.deviceRotation, out s.localRotation);
            d.TryGetFeatureValue(CommonUsages.deviceVelocity, out s.localVelocity);
            d.TryGetFeatureValue(CommonUsages.trigger, out s.trigger);
            d.TryGetFeatureValue(CommonUsages.grip, out s.grip);
            d.TryGetFeatureValue(CommonUsages.primaryButton, out s.primaryButton);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out s.secondaryButton);
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out s.stick);
            if (!s.tracked && s.localPosition != Vector3.zero) s.tracked = true;
            return s;
        }
    }
}
