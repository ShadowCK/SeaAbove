using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Tiny procedural sound bank so the prototype has feedback without audio assets.
    /// Replace with real clips later (same keys).
    /// </summary>
    public class SfxBank : MonoBehaviour
    {
        public const string Cast = "cast", Splash = "splash", Nibble = "nibble", Bite = "bite", Reel = "reel", Snap = "snap", Catch = "catch", Unlock = "unlock";
        const int Rate = 44100;

        readonly Dictionary<string, AudioClip> m_Clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> m_Pool = new List<AudioSource>();
        int m_Next;

        void Awake()
        {
            m_Clips[Cast] = Make(Cast, 0.35f, (t, d) => Noise() * Env(t, d, 0.01f) * 0.25f * (1 - t / d) + Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(900, 300, t / d) * t) * 0.15f * Env(t, d, 0.01f));
            m_Clips[Splash] = Make(Splash, 0.6f, (t, d) => Noise() * Mathf.Exp(-t * 9f) * 0.5f + Mathf.Sin(2 * Mathf.PI * (500 - 400 * t) * t) * Mathf.Exp(-t * 14f) * 0.3f);
            m_Clips[Nibble] = Make(Nibble, 0.12f, (t, d) => Mathf.Sin(2 * Mathf.PI * 1200 * t) * Mathf.Exp(-t * 40f) * 0.35f);
            m_Clips[Bite] = Make(Bite, 0.4f, (t, d) => (Mathf.Sin(2 * Mathf.PI * 220 * t) + Noise() * 0.4f) * Mathf.Exp(-t * 7f) * 0.5f);
            m_Clips[Reel] = Make(Reel, 0.03f, (t, d) => Noise() * Mathf.Exp(-t * 200f) * 0.5f);
            m_Clips[Snap] = Make(Snap, 0.25f, (t, d) => Noise() * Mathf.Exp(-t * 25f) * 0.8f);
            m_Clips[Catch] = Make(Catch, 0.9f, (t, d) => Arp(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f));
            m_Clips[Unlock] = Make(Unlock, 1.0f, (t, d) => Arp(t, new[] { 783.99f, 987.77f, 1174.66f, 1567.98f, 1975.53f }, 0.09f));
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0.85f;
                src.minDistance = 0.5f;
                src.maxDistance = 8f;
                m_Pool.Add(src);
            }
        }

        public void Play(string key, Vector3 pos, float volume = 1f)
        {
            if (!m_Clips.TryGetValue(key, out var clip)) return;
            var src = m_Pool[m_Next];
            m_Next = (m_Next + 1) % m_Pool.Count;
            src.transform.position = pos;
            src.pitch = Random.Range(0.94f, 1.06f);
            src.PlayOneShot(clip, volume);
        }

        static float Noise() => Random.value * 2f - 1f;
        static float Env(float t, float d, float attack) => Mathf.Clamp01(t / attack) * Mathf.Clamp01((d - t) / (d * 0.5f));

        static float Arp(float t, float[] notes, float step)
        {
            float s = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                float lt = t - i * step;
                if (lt < 0) continue;
                float e = Mathf.Exp(-lt * 5f);
                s += (Mathf.Sin(2 * Mathf.PI * notes[i] * lt) + 0.3f * Mathf.Sin(4 * Mathf.PI * notes[i] * lt)) * e * 0.18f;
            }
            return s;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, seconds), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
