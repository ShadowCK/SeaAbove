using Unity.XR.CoreUtils;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace UpsideDownFishing
{
    /// <summary>
    /// Single entry point placed in the Main scene. Finds the XR Origin, enables passthrough,
    /// and assembles all game systems at runtime (everything is procedural, no prefabs needed).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Use the simulated room even on device.")]
        public bool forceSimulatedRoom;
        [Tooltip("Show furniture boxes from Room Understanding as translucent orange boxes.")]
        public bool debugShowFurniture;
        [Tooltip("Wipe the collection save on start (testing).")]
        public bool resetSaveOnStart;

        Camera m_Head;
        Transform m_TrackingSpace;
        RoomUnderstanding m_Room;
        CeilingWater m_Water;
        FishSchool m_School;
        RoomGeometry m_Geometry;
        RoomDecorations m_Decor;
        CollectionManager m_Collection;
        Aquarium m_Aquarium;

        void Awake()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
            {
                m_Head = origin.Camera;
                m_TrackingSpace = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            }
            else
            {
                m_Head = Camera.main;
                m_TrackingSpace = m_Head != null && m_Head.transform.parent != null ? m_Head.transform.parent : new GameObject("TrackingSpace").transform;
                Debug.LogWarning("[UDF] No XROrigin found, using Camera.main.");
            }

            // Passthrough: clear to transparent black so the VST layer shows the real room.
            m_Head.clearFlags = CameraClearFlags.SolidColor;
            m_Head.backgroundColor = new Color(0, 0, 0, 0);
            m_Head.nearClipPlane = 0.03f;
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            PXR_Manager.EnableVideoSeeThrough = true;
#endif
#if UNITY_EDITOR
            // In the editor there is no passthrough: show a soft room-ish backdrop instead.
            if (!UnityEngine.XR.XRSettings.isDeviceActive) m_Head.backgroundColor = new Color(0.78f, 0.74f, 0.7f, 1);
#endif

            m_Collection = Make<CollectionManager>("Collection");
            if (resetSaveOnStart) m_Collection.ResetProgress();
            var sfx = Make<SfxBank>("Sfx");

            m_Room = Make<RoomUnderstanding>("RoomUnderstanding");
            m_Room.trackingSpace = m_TrackingSpace;
            m_Room.head = m_Head;
            m_Room.forceSimulatedRoom = forceSimulatedRoom;
            m_Room.RoomReady += OnRoomReady;

            m_Water = Make<CeilingWater>("CeilingSea");
            m_Geometry = Make<RoomGeometry>("RoomGeometry");
            m_Geometry.debugVisible = debugShowFurniture;
            m_School = Make<FishSchool>("FishSchool");
            m_School.collection = m_Collection;
            m_School.head = m_Head.transform;
            m_Decor = Make<RoomDecorations>("Decorations");
            m_Decor.collection = m_Collection;

            // hands
            var right = new GameObject("RightHand");
            right.transform.SetParent(m_TrackingSpace, false);
            right.AddComponent<HandFollower>().hand = Hand.Right;
            var left = new GameObject("LeftHand");
            left.transform.SetParent(m_TrackingSpace, false);
            left.AddComponent<HandFollower>().hand = Hand.Left;
            var mitt = new MeshKit();
            mitt.Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.025f, 0.05f), new Color(0.6f, 0.85f, 1f), new Color(0.35f, 0.55f, 0.95f), 6, 10);
            var mittGo = MeshKit.Spawn("LeftMitt", mitt.Build("Mitt"), UDFAssets.Toon_(Color.white), left.transform);

            var rodGo = new GameObject("FishingRod");
            rodGo.transform.SetParent(right.transform, false);
            rodGo.SetActive(false); // configure before Start
            var rod = rodGo.AddComponent<FishingRod>();
            rod.water = m_Water; rod.school = m_School; rod.collection = m_Collection; rod.sfx = sfx;
            rod.leftHand = left.transform; rod.leftHandVisual = mittGo.transform;

            // aquarium + the free hand that carries landed fish into it
            m_Aquarium = Make<Aquarium>("Aquarium");
            var carrier = left.AddComponent<FishCarrier>();
            carrier.hand = Hand.Left;
            carrier.handTransform = left.transform;
            carrier.aquarium = m_Aquarium;
            carrier.collection = m_Collection;
            carrier.sfx = sfx;
            carrier.school = m_School;
            carrier.head = m_Head.transform;
            rod.carrier = carrier;
            rodGo.SetActive(true);
            carrier.rod = rod;

            var hudGo = new GameObject("HUD");
            hudGo.SetActive(false);
            var hud = hudGo.AddComponent<GameHUD>();
            hud.head = m_Head; hud.rod = rod; hud.collection = m_Collection; hud.room = m_Room; hud.leftHand = left.transform; hud.sfx = sfx; hud.carrier = carrier;
            hudGo.SetActive(true);

#if UNITY_EDITOR
            var sim = gameObject.AddComponent<EditorSimulator>();
            sim.head = m_Head;
            sim.trackingSpace = m_TrackingSpace;
#endif
        }

        T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        void OnRoomReady(RoomModel room)
        {
            m_Water.Build(room);
            m_Geometry.Build(room);
            m_School.Init(m_Water);
            m_Decor.Init(room);
            m_Aquarium.Build(room, m_Head.transform);
        }
    }
}
