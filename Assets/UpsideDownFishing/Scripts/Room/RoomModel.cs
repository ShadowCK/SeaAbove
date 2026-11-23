using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// World-space description of the real room, produced by <see cref="RoomUnderstanding"/>.
    /// The ceiling polygon is stored as XZ points (counter-clockwise when seen from above).
    /// </summary>
    public class RoomModel
    {
        public float floorY;
        public float ceilingY;
        public readonly List<Vector2> ceilingPolygon = new List<Vector2>();
        public readonly List<Obstacle> obstacles = new List<Obstacle>();
        public bool fromSceneCapture;

        public struct Obstacle
        {
            public string label;
            public Vector3 center;
            public Quaternion rotation;
            public Vector3 size;
        }

        public float Height => ceilingY - floorY;

        public Vector2 Centroid
        {
            get
            {
                Vector2 c = Vector2.zero;
                foreach (var p in ceilingPolygon) c += p;
                return ceilingPolygon.Count > 0 ? c / ceilingPolygon.Count : Vector2.zero;
            }
        }

        public Bounds HorizontalBounds
        {
            get
            {
                var b = new Bounds(new Vector3(Centroid.x, 0, Centroid.y), Vector3.zero);
                foreach (var p in ceilingPolygon) b.Encapsulate(new Vector3(p.x, 0, p.y));
                return b;
            }
        }

        public float Area
        {
            get
            {
                float a = 0;
                for (int i = 0, j = ceilingPolygon.Count - 1; i < ceilingPolygon.Count; j = i++)
                    a += ceilingPolygon[j].x * ceilingPolygon[i].y - ceilingPolygon[i].x * ceilingPolygon[j].y;
                return Mathf.Abs(a) * 0.5f;
            }
        }

        public bool Contains(Vector2 p, float margin = 0f)
        {
            if (!Geometry.PointInPolygon(p, ceilingPolygon)) return false;
            return margin <= 0f || Geometry.DistanceToPolygonEdge(p, ceilingPolygon) >= margin;
        }

        /// <summary>Closest point that is inside the polygon by at least <paramref name="margin"/>.</summary>
        public Vector2 ClampInside(Vector2 p, float margin)
        {
            if (Contains(p, margin)) return p;
            // walk toward the centroid until inside
            Vector2 c = Centroid;
            for (int i = 1; i <= 20; i++)
            {
                Vector2 q = Vector2.Lerp(p, c, i / 20f);
                if (Contains(q, margin)) return q;
            }
            return c;
        }

        public Vector2 RandomPointInside(float margin)
        {
            var b = HorizontalBounds;
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector2(Random.Range(b.min.x, b.max.x), Random.Range(b.min.z, b.max.z));
                if (Contains(p, margin)) return p;
            }
            return Centroid;
        }

        /// <summary>Fallback room used in the editor or when no scene capture data exists.</summary>
        public static RoomModel CreateSimulated(Vector3 userPos, float floorY = 0f)
        {
            var room = new RoomModel { floorY = floorY, ceilingY = floorY + 2.6f };
            float hx = 2.2f, hz = 2.6f;
            Vector2 c = new Vector2(userPos.x, userPos.z + 0.6f);
            room.ceilingPolygon.Add(c + new Vector2(-hx, -hz));
            room.ceilingPolygon.Add(c + new Vector2(hx, -hz));
            room.ceilingPolygon.Add(c + new Vector2(hx, hz * 0.4f));
            room.ceilingPolygon.Add(c + new Vector2(hx * 0.4f, hz));   // L-ish corner to exercise non-rect ceilings
            room.ceilingPolygon.Add(c + new Vector2(-hx, hz));
            room.obstacles.Add(new Obstacle { label = "Table", center = new Vector3(c.x + 1.2f, floorY + 0.38f, c.y + 1.0f), rotation = Quaternion.identity, size = new Vector3(1.2f, 0.76f, 0.7f) });
            room.obstacles.Add(new Obstacle { label = "Sofa", center = new Vector3(c.x - 1.6f, floorY + 0.45f, c.y), rotation = Quaternion.Euler(0, 90, 0), size = new Vector3(2.0f, 0.9f, 0.9f) });
            room.obstacles.Add(new Obstacle { label = "Lamp", center = new Vector3(c.x + 1.8f, floorY + 0.8f, c.y - 2.0f), rotation = Quaternion.identity, size = new Vector3(0.35f, 1.6f, 0.35f) });
            return room;
        }
    }
}
