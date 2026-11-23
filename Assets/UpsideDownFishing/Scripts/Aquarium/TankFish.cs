using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// A caught fish living in the upside-down room aquarium (gravity reversed, like the ceiling
    /// sea): lifted up through the downward-facing water surface by the player, it shoots up into
    /// the water, levels out and settles, then cruises lazily between random points between the
    /// surface (below) and the decor hanging from the lid. Big fish are shrunk to fit the tank.
    /// Unlike the ceiling sea the tank fish swim UPRIGHT: seen up close at eye level a belly-up fish
    /// reads as a dead fish, and the water / decor / bubbles already show the reversed gravity.
    /// </summary>
    public class TankFish : MonoBehaviour
    {
        Aquarium m_Tank;
        public FishAgent Agent { get; private set; }
        public FishSpecies Species { get; private set; }
        /// <summary>Length in the tank (big fish are shrunk to fit).</summary>
        public float Length => m_Len;
        /// <summary>Settled in the water (not still coming in): can be taken out by hand.</summary>
        public bool Grabbable => !m_Entering;
        Vector3 m_OrigScale;
        float m_Len, m_Speed, m_Retarget, m_Time;
        Vector3 m_Target, m_Vel, m_Heading;
        bool m_Entering = true, m_Splashed;

        /// <summary>Upright swimming pose heading along dir.</summary>
        static Quaternion Swim(Vector3 dir) => Quaternion.LookRotation(dir, Vector3.up);

        public void Init(Aquarium tank, FishAgent agent)
        {
            var sp = agent.Species;
            Agent = agent;
            Species = sp;
            m_Tank = tank;
            m_OrigScale = transform.localScale;
            transform.SetParent(tank.Tank, true);
            float scale = Mathf.Min(1f, 0.3f / Mathf.Max(sp.length, 0.01f));
            transform.localScale *= scale;
            m_Len = sp.length * scale;
            m_Speed = Mathf.Clamp(sp.swimSpeed * 0.35f, 0.06f, 0.14f) * Random.Range(0.8f, 1.2f);
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.material.SetFloat("_TankLit", 2f);   // lit through the surface below
            m_Vel = Vector3.up * 0.3f;
            float h = Random.Range(0f, Mathf.PI * 2f);
            m_Heading = new Vector3(Mathf.Cos(h), 0, Mathf.Sin(h) * 0.4f).normalized;
            PickTarget();
        }

        /// <summary>Undo the tank changes (scale, lighting, parent) before it leaves the tank.</summary>
        public void Restore()
        {
            transform.SetParent(null, true);
            transform.localScale = m_OrigScale;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.material.SetFloat("_TankLit", 0f);
        }

        float MinY => m_Tank.SurfaceY + 0.03f + m_Len * 0.25f;
        float MaxY => Mathf.Max(MinY + 0.02f, m_Tank.FishTopY - m_Len * 0.2f);

        void PickTarget()
        {
            var e = m_Tank.InnerHalfExtents;
            float m = m_Len * 0.55f;
            m_Target = new Vector3(Random.Range(-e.x + m, e.x - m),
                                   Random.Range(MinY + 0.02f, MaxY),
                                   Random.Range(-e.z + m * 0.6f, e.z - m * 0.6f));
            m_Retarget = Random.Range(3f, 7f);
        }

        void Update()
        {
            if (m_Tank == null || m_Tank.Tank == null) return;
            float dt = Time.deltaTime;
            m_Time += dt;
            var e = m_Tank.InnerHalfExtents;
            float surface = m_Tank.SurfaceY;
            Vector3 p = transform.localPosition;
            float cx = e.x - m_Len * 0.45f, cz = e.z - m_Len * 0.2f;

            if (m_Entering)
            {
                bool inWater = p.y > surface;
                if (!inWater)
                {
                    // lifted in from below: drawn up and in under the opening, nose first
                    p.x = Mathf.MoveTowards(p.x, Mathf.Clamp(p.x, -cx, cx), dt * 1.0f);
                    p.z = Mathf.MoveTowards(p.z, Mathf.Clamp(p.z, -cz, cz), dt * 1.0f);
                    m_Vel = Vector3.MoveTowards(m_Vel, Vector3.up * 0.8f, dt * 4f);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(Vector3.up, -m_Heading), dt * 8f);
                }
                else
                {
                    if (!m_Splashed)
                    {
                        m_Splashed = true;
                        SplashFx.Drip(m_Tank.Tank.TransformPoint(new Vector3(p.x, surface, p.z)));
                    }
                    // into the water: glide on, slow down and level out
                    m_Vel = Vector3.Lerp(m_Vel, m_Heading * 0.12f, 1f - Mathf.Exp(-4f * dt));
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, Swim(m_Heading), dt * 3f);
                    p.x = Mathf.Clamp(p.x, -cx, cx);
                    p.z = Mathf.Clamp(p.z, -cz, cz);
                }
                p += m_Vel * dt;
                p.y = Mathf.Min(p.y, MaxY);
                transform.localPosition = p;
                if (inWater && (m_Time > 3f || (p.y > MinY + 0.04f && Quaternion.Angle(transform.localRotation, Swim(m_Heading)) < 12f)))
                    m_Entering = false;
                return;
            }

            m_Retarget -= dt;
            Vector3 to = m_Target - p;
            if (m_Retarget <= 0 || to.sqrMagnitude < 0.0025f) { PickTarget(); to = m_Target - p; }
            to.y *= 0.6f;
            if (to.sqrMagnitude > 1e-6f)
                transform.localRotation = Quaternion.Slerp(transform.localRotation, Swim(to.normalized), dt * 1.6f);
            float bob = Mathf.Sin(m_Time * 1.7f + GetInstanceID()) * 0.004f;
            p += transform.localRotation * Vector3.forward * m_Speed * dt + Vector3.up * bob * dt;
            p.x = Mathf.Clamp(p.x, -cx, cx);
            p.z = Mathf.Clamp(p.z, -cz, cz);
            p.y = Mathf.Clamp(p.y, MinY, MaxY);
            transform.localPosition = p;
        }
    }
}
