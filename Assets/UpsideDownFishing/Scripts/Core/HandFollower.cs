using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>Drives a transform (child of the tracking space) from controller pose.</summary>
    public class HandFollower : MonoBehaviour
    {
        public Hand hand;

        void Update() => Apply();
        void OnBeforeRender_() => Apply();

        void OnEnable() { Application.onBeforeRender += OnBeforeRender_; }
        void OnDisable() { Application.onBeforeRender -= OnBeforeRender_; }

        void Apply()
        {
            var s = XRInput.Get(hand);
            if (!s.tracked) return;
            transform.localPosition = s.localPosition;
            transform.localRotation = s.localRotation;
        }
    }
}
