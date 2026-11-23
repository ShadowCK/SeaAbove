using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>Spawns unlocked decorations as gently floating objects in the real room.</summary>
    public class RoomDecorations : MonoBehaviour
    {
        public CollectionManager collection;
        RoomModel m_Room;
        readonly Dictionary<string, GameObject> m_Spawned = new Dictionary<string, GameObject>();

        public void Init(RoomModel room)
        {
            m_Room = room;
            foreach (var go in m_Spawned.Values) if (go) Destroy(go);
            m_Spawned.Clear();
            foreach (var id in collection.Data.unlockedDecor) Spawn(id);
            collection.DecorUnlocked -= OnUnlocked;
            collection.DecorUnlocked += OnUnlocked;
        }

        void OnUnlocked(DecorDef d) => Spawn(d.id);

        void Spawn(string id)
        {
            if (m_Room == null || m_Spawned.ContainsKey(id)) return;
            var k = new MeshKit();
            Color glow = Color.black;
            switch (id)
            {
                case "jelly_lantern":
                {
                    var c = new Color(1f, 0.6f, 0.85f);
                    k.Ellipsoid(Vector3.zero, new Vector3(0.12f, 0.09f, 0.12f), Color.white, c, 6, 12);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.07f;
                        k.Tube(new[] { o + Vector3.down * 0.02f, o * 1.1f + Vector3.down * 0.15f, o * 0.8f + Vector3.down * 0.3f }, new[] { 0.008f, 0.006f, 0.002f }, c, 5);
                    }
                    glow = new Color(0.35f, 0.15f, 0.3f);
                    break;
                }
                case "bubble_ring":
                {
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i * Mathf.PI * 2 / 14f;
                        k.Ellipsoid(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.18f, Vector3.one * Mathf.Lerp(0.025f, 0.045f, (i % 3) / 2f), new Color(0.8f, 1f, 1f), new Color(0.5f, 0.85f, 1f), 5, 8);
                    }
                    glow = new Color(0.05f, 0.15f, 0.2f);
                    break;
                }
                case "coral_chime":
                {
                    k.Tube(new[] { Vector3.zero, Vector3.down * 0.05f }, new[] { 0.005f, 0.005f }, Color.white, 5);
                    k.Ellipsoid(Vector3.down * 0.06f, new Vector3(0.15f, 0.015f, 0.15f), new Color(1f, 0.7f, 0.5f), new Color(1f, 0.5f, 0.5f), 4, 12);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2 / 5f;
                        Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.12f + Vector3.down * 0.06f;
                        k.Tube(new[] { o, o + Vector3.down * 0.15f }, new[] { 0.002f, 0.002f }, Color.white, 4);
                        k.Ellipsoid(o + Vector3.down * 0.18f, new Vector3(0.02f, 0.04f, 0.02f), new Color(1f, 0.55f, 0.65f), new Color(1f, 0.8f, 0.6f), 4, 6);
                    }
                    break;
                }
                default: // star_mobile
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2 / 5f;
                        Vector3 o = new Vector3(Mathf.Cos(a) * 0.2f, -0.1f * (i % 2), Mathf.Sin(a) * 0.2f);
                        k.Ellipsoid(o, new Vector3(0.05f, 0.05f, 0.012f), new Color(1f, 0.95f, 0.5f), new Color(1f, 0.8f, 0.3f), 4, 5);
                    }
                    glow = new Color(0.3f, 0.25f, 0.05f);
                    break;
                }
            }
            var go = MeshKit.Spawn("Decor_" + id, k.Build(id), UDFAssets.Toon_(Color.white, glow), transform);
            var p = m_Room.RandomPointInside(0.6f);
            go.transform.position = new Vector3(p.x, Mathf.Lerp(m_Room.floorY, m_Room.ceilingY, 0.62f), p.y);
            go.AddComponent<FloatBob>();
            m_Spawned[id] = go;
        }
    }

    public class FloatBob : MonoBehaviour
    {
        Vector3 m_Base; float m_Phase;
        void Start() { m_Base = transform.position; m_Phase = Random.value * 10f; }
        void Update()
        {
            float t = Time.time + m_Phase;
            transform.position = m_Base + new Vector3(Mathf.Sin(t * 0.3f) * 0.08f, Mathf.Sin(t * 0.7f) * 0.05f, Mathf.Cos(t * 0.25f) * 0.08f);
            transform.Rotate(0, 12f * Time.deltaTime, 0, Space.World);
        }
    }
}
