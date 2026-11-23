using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>Builds a stylized low-poly fish mesh per species (head toward local +Z).</summary>
    public static class FishBuilder
    {
        static readonly Dictionary<string, Mesh> s_Meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();

        public static GameObject Create(FishSpecies sp, Transform parent)
        {
            var go = MeshKit.Spawn(sp.englishName, GetMesh(sp), GetMaterial(sp), parent);
            return go;
        }

        public static Material GetMaterial(FishSpecies sp)
        {
            if (s_Materials.TryGetValue(sp.id, out var m)) return m;
            m = UDFAssets.Toon_(Color.white, sp.emission);
            float L = sp.length;
            m.SetFloat("_SwimAmp", sp.shape == BodyShape.Eel ? L * 0.12f : sp.shape == BodyShape.Ray ? 0f : L * 0.07f);
            m.SetFloat("_SwimFreq", sp.shape == BodyShape.Eel ? 14f / L : 5f / L);
            m.SetFloat("_SwimSpeed", sp.shape == BodyShape.Whale ? 3f : 7f);
            m.SetFloat("_HeadZ", L * 0.25f);
            m.SetColor("_RimColor", Color.Lerp(Color.white, sp.finColor, 0.5f));
            s_Materials[sp.id] = m;
            return m;
        }

        public static Mesh GetMesh(FishSpecies sp)
        {
            if (s_Meshes.TryGetValue(sp.id, out var mesh)) return mesh;
            var k = new MeshKit();
            float L = sp.length;
            float h, w;
            switch (sp.shape)
            {
                case BodyShape.Round: h = 0.42f * L; w = 0.4f * L; break;
                case BodyShape.Eel: h = 0.09f * L; w = 0.06f * L; break;
                case BodyShape.Ray: h = 0.09f * L; w = 0.55f * L; break;
                case BodyShape.Whale: h = 0.32f * L; w = 0.3f * L; break;
                default: h = 0.3f * L; w = 0.15f * L; break;
            }

            // --- body (lathe along z) ---
            int rings = sp.shape == BodyShape.Eel ? 18 : 10, segs = 12;
            float bodyStart = -L * 0.5f, bodyLen = L * (sp.shape == BodyShape.Ray ? 0.65f : 0.85f);
            int start = k.v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float prof = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.1f, 1f, t)), sp.shape == BodyShape.Round ? 0.45f : 0.6f);
                if (sp.shape == BodyShape.Eel) prof = Mathf.Lerp(0.45f, 1f, Mathf.Sin(Mathf.PI * Mathf.Lerp(0.05f, 0.95f, t)));
                float z = bodyStart + (L - bodyLen) * (sp.shape == BodyShape.Ray ? 0.9f : 0.6f) + t * bodyLen;
                for (int s = 0; s <= segs; s++)
                {
                    float a = 2 * Mathf.PI * s / segs;
                    float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                    var p = new Vector3(cx * w * 0.5f * prof, cy * h * 0.5f * prof, z);
                    var nrm = new Vector3(cx / Mathf.Max(w, 1e-3f), cy / Mathf.Max(h, 1e-3f), 0).normalized;
                    float belly = Mathf.SmoothStep(0, 1, cy * 0.5f + 0.5f);
                    k.Add(p, Color.Lerp(sp.bellyColor, sp.topColor, belly), nrm);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = start + r * (segs + 1) + s, b = a + segs + 1;
                    k.Tri(a, a + 1, b); k.Tri(a + 1, b + 1, b);
                }

            float tailZ = bodyStart + (L - bodyLen) * (sp.shape == BodyShape.Ray ? 0.9f : 0.6f);
            float headZ = tailZ + bodyLen;
            Color fin = sp.finColor;

            // --- tail ---
            switch (sp.shape)
            {
                case BodyShape.Whale: // horizontal flukes
                    k.Fin(new Vector3(0, 0, tailZ + 0.02f * L), new Vector3(0.3f * L, 0.02f * L, tailZ - 0.15f * L), new Vector3(0.05f * L, 0, tailZ - 0.08f * L), fin);
                    k.Fin(new Vector3(0, 0, tailZ + 0.02f * L), new Vector3(-0.05f * L, 0, tailZ - 0.08f * L), new Vector3(-0.3f * L, 0.02f * L, tailZ - 0.15f * L), fin);
                    break;
                case BodyShape.Ray: // thin whip tail
                    k.Tube(new[] { new Vector3(0, 0, tailZ + 0.02f), new Vector3(0, 0.01f * L, tailZ - 0.45f * L) }, new[] { 0.015f * L, 0.002f }, fin, 5);
                    break;
                case BodyShape.Eel:
                    k.Fin(new Vector3(0, 0, tailZ + 0.05f * L), new Vector3(0, 0.07f * L, tailZ - 0.06f * L), new Vector3(0, -0.07f * L, tailZ - 0.06f * L), fin);
                    break;
                default:
                    float ts = sp.shape == BodyShape.Round ? 0.22f : 0.3f;
                    k.Fin(new Vector3(0, 0, tailZ + 0.06f * L), new Vector3(0, ts * L, tailZ - ts * 0.7f * L), new Vector3(0, 0.02f * L, tailZ - ts * 0.35f * L), fin);
                    k.Fin(new Vector3(0, 0, tailZ + 0.06f * L), new Vector3(0, -0.02f * L, tailZ - ts * 0.35f * L), new Vector3(0, -ts * L, tailZ - ts * 0.7f * L), fin);
                    break;
            }

            // --- dorsal / pectoral fins ---
            if (sp.shape == BodyShape.Ray)
            {
                // wing tips get the fin colour
                k.Fin(new Vector3(w * 0.45f, 0, tailZ + bodyLen * 0.3f), new Vector3(w * 0.75f, 0.01f, tailZ + bodyLen * 0.45f), new Vector3(w * 0.45f, 0, tailZ + bodyLen * 0.7f), fin);
                k.Fin(new Vector3(-w * 0.45f, 0, tailZ + bodyLen * 0.3f), new Vector3(-w * 0.45f, 0, tailZ + bodyLen * 0.7f), new Vector3(-w * 0.75f, 0.01f, tailZ + bodyLen * 0.45f), fin);
            }
            else
            {
                float dz = tailZ + bodyLen * (sp.shape == BodyShape.Eel ? 0.1f : 0.35f);
                float dl = bodyLen * (sp.shape == BodyShape.Eel ? 0.8f : 0.3f);
                float dh = sp.shape == BodyShape.Eel ? h * 0.9f : h * 0.55f;
                k.Fin(new Vector3(0, h * 0.42f, dz), new Vector3(0, h * 0.45f, dz + dl), new Vector3(0, h * 0.45f + dh, dz + dl * 0.25f), fin);
                if (sp.shape != BodyShape.Eel)
                {
                    float pz = tailZ + bodyLen * 0.62f;
                    k.Fin(new Vector3(w * 0.45f, -h * 0.1f, pz), new Vector3(w * 0.45f + 0.12f * L, -h * 0.35f, pz - 0.1f * L), new Vector3(w * 0.45f, -h * 0.2f, pz - 0.06f * L), fin);
                    k.Fin(new Vector3(-w * 0.45f, -h * 0.1f, pz), new Vector3(-w * 0.45f, -h * 0.2f, pz - 0.06f * L), new Vector3(-w * 0.45f - 0.12f * L, -h * 0.35f, pz - 0.1f * L), fin);
                }
            }

            // --- big cartoon eyes ---
            float eyeZ = headZ - bodyLen * (sp.shape == BodyShape.Eel ? 0.04f : 0.17f);
            float eyeR = Mathf.Max(0.012f, L * (sp.shape == BodyShape.Eel ? 0.035f : 0.06f));
            float eyeX = sp.shape == BodyShape.Ray ? w * 0.18f : w * 0.36f;
            float eyeY = sp.shape == BodyShape.Ray ? h * 0.4f : h * 0.12f;
            foreach (float sx in new[] { 1f, -1f })
            {
                k.Ellipsoid(new Vector3(sx * eyeX, eyeY, eyeZ), Vector3.one * eyeR, Color.white, Color.white, 5, 8);
                k.Ellipsoid(new Vector3(sx * (eyeX + eyeR * 0.55f), eyeY + eyeR * 0.05f, eyeZ + eyeR * 0.25f), Vector3.one * eyeR * 0.55f, new Color(0.08f, 0.08f, 0.15f), new Color(0.05f, 0.05f, 0.1f), 4, 6);
            }

            mesh = k.Build("Fish_" + sp.id);
            s_Meshes[sp.id] = mesh;
            return mesh;
        }
    }
}
