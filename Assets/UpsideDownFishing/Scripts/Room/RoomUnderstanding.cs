using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace UpsideDownFishing
{
    /// <summary>
    /// Builds a <see cref="RoomModel"/> from PICO Scene Capture (Room Understanding):
    /// Ceiling / Floor polygons and furniture boxes. Falls back to a simulated room in the
    /// editor or when the user has not scanned a room.
    /// </summary>
    public class RoomUnderstanding : MonoBehaviour
    {
        public enum Status { Idle, Querying, WaitingForRoomScan, Ready }

        [Tooltip("Tracking-space root (XR Origin > Camera Offset). Anchor poses are relative to it.")]
        public Transform trackingSpace;
        public Camera head;
        [Tooltip("Force the simulated room even on device (for quick tests).")]
        public bool forceSimulatedRoom;
        public float queryTimeout = 8f;

        public Status CurrentStatus { get; private set; }
        public RoomModel Room { get; private set; }
        public event Action<RoomModel> RoomReady;
        public string StatusMessage { get; private set; } = "";

        bool m_RoomScanRequested;

        IEnumerator Start()
        {
            yield return null; // let XR initialise
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            if (!forceSimulatedRoom)
            {
                PXR_Manager.EnableVideoSeeThrough = true;
                StartSceneCapture();
                float t = 0;
                while (CurrentStatus != Status.Ready && t < queryTimeout) { t += Time.deltaTime; yield return null; }
                if (CurrentStatus == Status.Ready) yield break;
                if (CurrentStatus == Status.WaitingForRoomScan) yield break; // resumes when the scan finishes
                Debug.LogWarning("[UDF] Scene capture timed out, using a simulated room.");
            }
#endif
            yield return new WaitForSeconds(0.2f);
            UseSimulatedRoom();
        }

        void UseSimulatedRoom()
        {
            Vector3 headPos = head != null ? head.transform.position : Vector3.up * 1.6f;
            float floor = trackingSpace != null ? trackingSpace.position.y : 0f;
            var room = RoomModel.CreateSimulated(headPos, floor);
            // Keep the water reachable: ceiling roughly 1.0 m above the head.
            room.ceilingY = Mathf.Max(floor + 2.3f, headPos.y + 0.9f);
            Publish(room, "Simulated room");
        }

        void Publish(RoomModel room, string msg)
        {
            Geometry.MakeCCW(room.ceilingPolygon);
            Room = room;
            CurrentStatus = Status.Ready;
            StatusMessage = msg;
            Debug.Log($"[UDF] Room ready: {msg}, ceiling={room.ceilingY:F2} floor={room.floorY:F2} area={room.Area:F1}m², obstacles={room.obstacles.Count}");
            RoomReady?.Invoke(room);
        }

#if ENABLE_PICO_XR_SDK
        void OnEnable() { PXR_Manager.SceneAnchorDataUpdated += OnSceneAnchorDataUpdated; }
        void OnDisable() { PXR_Manager.SceneAnchorDataUpdated -= OnSceneAnchorDataUpdated; }

        void OnSceneAnchorDataUpdated()
        {
            if (CurrentStatus != Status.Ready || m_RoomScanRequested) LoadSceneData();
        }

        async void StartSceneCapture()
        {
            CurrentStatus = Status.Querying;
            StatusMessage = "Reading your room...";
            var r = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture);
            if (r != PxrResult.SUCCESS)
            {
                Debug.LogWarning("[UDF] SceneCapture provider failed: " + r);
                CurrentStatus = Status.Idle;
                return;
            }
            LoadSceneData();
        }

        async void LoadSceneData()
        {
            var result = await PXR_MixedReality.QuerySceneAnchorAsync((PxrSemanticLabel[])null);
            if (this == null) return;
            if (result.result != PxrResult.SUCCESS)
            {
                Debug.LogWarning("[UDF] QuerySceneAnchor failed: " + result.result);
                return;
            }

            if (result.anchorHandleList.Count == 0)
            {
                if (m_RoomScanRequested) return;
                // No room has been scanned yet: launch the system room-capture flow.
                m_RoomScanRequested = true;
                CurrentStatus = Status.WaitingForRoomScan;
                StatusMessage = "Please scan your room first";
                var scan = await PXR_MixedReality.StartSceneCaptureAsync();
                if (this == null) return;
                if (scan == PxrResult.SUCCESS) LoadSceneData();
                else UseSimulatedRoom();
                return;
            }

            var room = new RoomModel { fromSceneCapture = true };
            bool hasCeiling = false, hasFloor = false;
            float ceilingSum = 0; int ceilingCount = 0;

            foreach (ulong handle in result.anchorHandleList)
            {
                if (PXR_MixedReality.GetSceneSemanticLabel(handle, out var label) != PxrResult.SUCCESS) continue;
                if (PXR_MixedReality.LocateAnchor(handle, out var pos, out var rot) != PxrResult.SUCCESS) continue;
                pos = ToWorld(pos); rot = ToWorld(rot);

                switch (label)
                {
                    case PxrSemanticLabel.Ceiling:
                        if (PXR_MixedReality.GetScenePolygonData(handle, out var cv) == PxrResult.SUCCESS && cv.Length >= 3)
                        {
                            var world = PolygonToWorld(cv, pos, rot);
                            var poly = new List<Vector2>();
                            foreach (var w in world) { poly.Add(new Vector2(w.x, w.z)); ceilingSum += w.y; ceilingCount++; }
                            // keep the largest ceiling polygon
                            if (!hasCeiling || Mathf.Abs(Geometry.SignedArea(poly)) > Mathf.Abs(Geometry.SignedArea(room.ceilingPolygon)))
                            {
                                room.ceilingPolygon.Clear();
                                room.ceilingPolygon.AddRange(poly);
                            }
                            hasCeiling = true;
                        }
                        break;
                    case PxrSemanticLabel.Floor:
                        room.floorY = pos.y;
                        hasFloor = true;
                        if (!hasCeiling && PXR_MixedReality.GetScenePolygonData(handle, out var fv) == PxrResult.SUCCESS && fv.Length >= 3)
                        {
                            // Use the floor outline as a ceiling footprint until a ceiling is found.
                            room.ceilingPolygon.Clear();
                            foreach (var w in PolygonToWorld(fv, pos, rot)) room.ceilingPolygon.Add(new Vector2(w.x, w.z));
                        }
                        break;
                    case PxrSemanticLabel.Wall:
                    case PxrSemanticLabel.Door:
                    case PxrSemanticLabel.Window:
                    case PxrSemanticLabel.Opening:
                    case PxrSemanticLabel.WallArt:
                    case PxrSemanticLabel.VirtualWall:
                    case PxrSemanticLabel.Unknown:
                        break;
                    default:
                        if (PXR_MixedReality.GetSceneBox3DData(handle, out var bpos, out var brot, out var extent) == PxrResult.SUCCESS)
                        {
                            room.obstacles.Add(new RoomModel.Obstacle
                            {
                                label = label.ToString(),
                                center = pos + rot * bpos,
                                rotation = rot,
                                size = new Vector3(Mathf.Abs(extent.x), Mathf.Abs(extent.y), Mathf.Abs(extent.z)),
                            });
                        }
                        break;
                }
            }

            if (room.ceilingPolygon.Count < 3)
            {
                Debug.LogWarning("[UDF] No ceiling/floor polygon in scene data, using simulated room.");
                UseSimulatedRoom();
                return;
            }

            if (!hasFloor) room.floorY = trackingSpace != null ? trackingSpace.position.y : 0f;
            room.ceilingY = ceilingCount > 0 ? ceilingSum / ceilingCount : room.floorY + 2.6f;
            if (room.Height < 1.8f || room.Height > 6f) room.ceilingY = room.floorY + 2.6f; // sanity
            m_RoomScanRequested = false;
            Publish(room, hasCeiling ? "Ceiling detected" : "Using floor outline");
        }

        /// <summary>
        /// Polygon vertices are in the anchor's local 2D plane. The SDK documents them as XY of the
        /// anchor frame; if that yields a non-horizontal outline we fall back to the XZ interpretation.
        /// </summary>
        static List<Vector3> PolygonToWorld(Vector2[] verts, Vector3 pos, Quaternion rot)
        {
            var xy = new List<Vector3>();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                var w = pos + rot * new Vector3(v.x, v.y, 0);
                xy.Add(w); minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y);
            }
            if (maxY - minY < 0.25f) return xy;
            var xz = new List<Vector3>();
            foreach (var v in verts) xz.Add(pos + rot * new Vector3(v.x, 0, v.y));
            return xz;
        }
#endif

        Vector3 ToWorld(Vector3 p) => trackingSpace != null ? trackingSpace.TransformPoint(p) : p;
        Quaternion ToWorld(Quaternion q) => trackingSpace != null ? trackingSpace.rotation * q : q;

        /// <summary>Re-run detection (e.g. after the user rescans the room).</summary>
        public void Rescan()
        {
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            m_RoomScanRequested = true;
            CurrentStatus = Status.WaitingForRoomScan;
            RescanAsync();
#else
            UseSimulatedRoom();
#endif
        }

#if ENABLE_PICO_XR_SDK
        async void RescanAsync()
        {
            var scan = await PXR_MixedReality.StartSceneCaptureAsync();
            if (this != null && scan == PxrResult.SUCCESS) LoadSceneData();
        }
#endif
    }
}
