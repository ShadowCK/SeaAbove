using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Physical stand-ins for the real room: furniture boxes become colliders (for floating
    /// fish to steer around) and depth occluders (so fish disappear behind the real sofa).
    /// Toggle <see cref="debugVisible"/> to see them as translucent boxes.
    /// </summary>
    public class RoomGeometry : MonoBehaviour
    {
        public bool debugVisible;
        Material m_Occluder, m_Debug;

        public void Build(RoomModel room)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            if (m_Occluder == null)
            {
                m_Occluder = Resources.Load<Material>("UDF/DepthOccluder");
                if (m_Occluder == null)
                {
                    var sh = Shader.Find("UDF/DepthOccluder");
                    if (sh != null) m_Occluder = new Material(sh);
                }
                m_Debug = UDFAssets.Glow_(new Color(1f, 0.6f, 0.2f, 0.25f));
            }
            var unit = new MeshKit();
            unit.Box(Vector3.zero, Vector3.one, Color.white);
            var cube = unit.Build("UnitBox");

            foreach (var o in room.obstacles)
            {
                var mat = debugVisible ? m_Debug : m_Occluder;
                var go = MeshKit.Spawn("Obstacle_" + o.label, cube, mat, transform);
                go.layer = FishAgent.RoomLayer;
                go.transform.SetPositionAndRotation(o.center, o.rotation);
                go.transform.localScale = o.size;
                go.AddComponent<BoxCollider>();
                if (mat == null) go.GetComponent<MeshRenderer>().enabled = false;
            }
        }
    }
}
