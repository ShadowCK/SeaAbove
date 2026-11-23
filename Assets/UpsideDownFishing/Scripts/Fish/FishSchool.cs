using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>Keeps the ceiling sea populated and decides which fish goes for the lure.</summary>
    public class FishSchool : MonoBehaviour
    {
        public CollectionManager collection;
        public Transform head;
        public int maxFloatingFish = 15;

        CeilingWater m_Water;
        readonly List<FishAgent> m_InWater = new List<FishAgent>();
        readonly Queue<FishAgent> m_Floating = new Queue<FishAgent>();
        FishAgent m_Engaged;
        float m_NextAttractTime;
        int m_TargetCount;

        public FishAgent Engaged => m_Engaged;
        public System.Action<FishAgent> FishNibbled, FishBit;

        public void Init(CeilingWater water)
        {
            m_Water = water;
            foreach (var f in m_InWater) if (f) Destroy(f.gameObject);
            m_InWater.Clear();
            m_TargetCount = Mathf.Clamp(Mathf.RoundToInt(water.Room.Area * 0.9f), 6, 16);
            for (int i = 0; i < m_TargetCount; i++) Spawn();
        }

        void Spawn()
        {
            var sp = collection.PickSpecies();
            var go = FishBuilder.Create(sp, transform);
            var agent = go.AddComponent<FishAgent>();
            agent.Init(sp, m_Water, head);
            agent.Bit = a => { if (a == m_Engaged) FishBit?.Invoke(a); };
            agent.Nibbled = a => { if (a == m_Engaged) FishNibbled?.Invoke(a); };
            m_InWater.Add(agent);
        }

        /// <summary>Called every frame by the rod while the lure rests in the water.</summary>
        public void UpdateLure(Vector3 lurePos, bool inWater)
        {
            if (!inWater)
            {
                if (m_Engaged != null && m_Engaged.CurrentState != FishAgent.State.Hooked) m_Engaged.LoseInterest(1f);
                if (m_Engaged != null && m_Engaged.CurrentState != FishAgent.State.Hooked) m_Engaged = null;
                m_NextAttractTime = Time.time + Random.Range(1.5f, 4f);
                return;
            }
            if (m_Engaged != null) return;
            if (Time.time < m_NextAttractTime) return;

            FishAgent best = null; float bestScore = float.MaxValue;
            foreach (var f in m_InWater)
            {
                if (f == null || !f.CanBeAttracted) continue;
                float d = Vector3.Distance(f.transform.position, lurePos);
                float score = d * Random.Range(0.6f, 1.4f);
                if (d < 3.5f && score < bestScore) { bestScore = score; best = f; }
            }
            if (best != null) { m_Engaged = best; best.AttractTo(lurePos); }
            else m_NextAttractTime = Time.time + 1f;
        }

        public void ReleaseEngaged(bool escaped)
        {
            if (m_Engaged == null) return;
            if (escaped) m_Engaged.Escape(); else m_Engaged.LoseInterest();
            m_Engaged = null;
            m_NextAttractTime = Time.time + Random.Range(2f, 5f);
        }

        /// <summary>The engaged fish was pulled out: it leaves the sea and floats in the room.</summary>
        public FishAgent LandEngaged()
        {
            var f = m_Engaged;
            m_Engaged = null;
            if (f == null) return null;
            m_InWater.Remove(f);
            f.Land();
            m_Floating.Enqueue(f);
            while (m_Floating.Count > maxFloatingFish)
            {
                var old = m_Floating.Dequeue();
                if (old) old.gameObject.AddComponent<ShrinkAndDestroy>();
            }
            Invoke(nameof(Spawn), Random.Range(3f, 8f));
            m_NextAttractTime = Time.time + Random.Range(2f, 4f);
            return f;
        }
    }

    public class ShrinkAndDestroy : MonoBehaviour
    {
        float t; Vector3 s0;
        void Start() { s0 = transform.localScale; }
        void Update()
        {
            t += Time.deltaTime;
            transform.localScale = s0 * Mathf.Max(0, 1 - t / 0.6f);
            if (t > 0.6f) Destroy(gameObject);
        }
    }
}
