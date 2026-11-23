using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Water spray for the upside-down sea. Droplets burst DOWN out of the surface into the
    /// room and, because the room has anti-gravity toward the sea, arc back up into it.
    ///   Play(pos, strength) : splash at a surface point (cast, thrash, landing)
    ///   Drip(pos)           : a few drops shed from a fish hanging on the line
    /// Three shared particle systems (droplets, mist, bubbles) are emitted into, so any
    /// number of splashes costs no extra objects.
    /// </summary>
    public static class SplashFx
    {
        static ParticleSystem s_Drops, s_Mist, s_Bubbles;
        static CeilingWater s_Water;

        public static void Init(CeilingWater water)
        {
            s_Water = water;
            if (s_Drops != null) return;
            var root = new GameObject("SplashFx").transform;
            root.SetParent(water.transform.parent, false);
            s_Drops = Make(root, "Drops", UDFAssets.Glow_(new Color(0.85f, 0.97f, 1f), 0.6f), 500, true);
            s_Mist = Make(root, "Mist", UDFAssets.Glow_(new Color(0.9f, 1f, 1f), 1f), 80, false);
            s_Bubbles = Make(root, "Bubbles", UDFAssets.New(UDFAssets.Bubble), 120, false);
        }

        static ParticleSystem Make(Transform parent, string name, Material mat, int max, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.5f; }
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.AddComponent<BubbleClipper>().mode = BubbleClipper.Mode.ReturnToSurface;
            ps.Play();
            return ps;
        }

        /// <summary>Splash at a point on (or just below) the water surface.</summary>
        public static void Play(Vector3 pos, float strength)
        {
            if (s_Drops == null || s_Water == null) return;
            float g = Physics.gravity.y;   // anti-gravity: pull back up toward the sea
            var ep = new ParticleSystem.EmitParams();
            int drops = Mathf.RoundToInt(28 * strength);
            for (int i = 0; i < drops; i++)
            {
                Vector3 dir = (Vector3.down + Random.insideUnitSphere * 0.75f).normalized;
                float speed = Random.Range(0.8f, 2.4f) * Mathf.Sqrt(strength);
                ep.position = pos + Random.insideUnitSphere * 0.04f + Vector3.down * 0.01f;
                ep.velocity = dir * speed;
                ep.startSize = Random.Range(0.006f, 0.016f);
                ep.startLifetime = Random.Range(0.6f, 1.2f);
                ep.startColor = new Color(1, 1, 1, Random.Range(0.6f, 1f));
                s_Drops.Emit(ep, 1);
            }
            var dm = s_Drops.main; dm.gravityModifier = g < 0 ? 0.45f * -1f : 0.45f;   // negative = toward ceiling

            int mist = Mathf.RoundToInt(6 * strength);
            for (int i = 0; i < mist; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.06f + Vector3.down * 0.03f;
                ep.velocity = (Vector3.down * 0.4f + Random.insideUnitSphere * 0.3f) * strength;
                ep.startSize = Random.Range(0.06f, 0.16f) * Mathf.Sqrt(strength);
                ep.startLifetime = Random.Range(0.35f, 0.7f);
                ep.startColor = new Color(1, 1, 1, 0.28f);
                s_Mist.Emit(ep, 1);
            }

            int bub = Mathf.RoundToInt(8 * strength);
            for (int i = 0; i < bub; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.08f + Vector3.down * Random.Range(0.02f, 0.2f);
                ep.velocity = Vector3.down * Random.Range(0.2f, 0.6f) + Random.insideUnitSphere * 0.15f;
                ep.startSize = Random.Range(0.012f, 0.035f);
                ep.startLifetime = Random.Range(1.5f, 3f);
                ep.startColor = Color.white;
                s_Bubbles.Emit(ep, 1);
            }
            var bm = s_Bubbles.main; bm.gravityModifier = -0.04f;   // drift slowly back up
            CeilingWater.AddRipple(new Vector3(pos.x, s_Water.SurfaceY, pos.z));
        }

        /// <summary>A few drops falling off something hanging under the water.</summary>
        public static void Drip(Vector3 pos)
        {
            if (s_Drops == null) return;
            var ep = new ParticleSystem.EmitParams();
            int n = Random.Range(1, 3);
            for (int i = 0; i < n; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.02f;
                ep.velocity = Random.insideUnitSphere * 0.25f + Vector3.down * 0.15f;
                ep.startSize = Random.Range(0.005f, 0.011f);
                ep.startLifetime = Random.Range(0.6f, 1.0f);
                ep.startColor = new Color(1, 1, 1, 0.9f);
                s_Drops.Emit(ep, 1);
            }
        }
    }
}
