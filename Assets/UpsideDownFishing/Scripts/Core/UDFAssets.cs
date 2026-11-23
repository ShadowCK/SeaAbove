using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Loads the base materials from Resources/UDF (created by the editor setup menu so the
    /// custom shaders are included in builds) and hands out tinted copies.
    /// </summary>
    public static class UDFAssets
    {
        public const string Toon = "Toon", CeilingWater = "CeilingWater", WaterVolume = "WaterVolume", Glow = "Glow";
        static readonly Dictionary<string, Material> s_Base = new Dictionary<string, Material>();

        static readonly Dictionary<string, string> s_Shaders = new Dictionary<string, string>
        {
            { Toon, "UDF/Toon" }, { CeilingWater, "UDF/CeilingWater" }, { WaterVolume, "UDF/WaterVolume" }, { Glow, "UDF/Glow" },
        };

        public static Material Base(string name)
        {
            if (s_Base.TryGetValue(name, out var m) && m != null) return m;
            m = Resources.Load<Material>("UDF/" + name);
            if (m == null)
            {
                var shader = Shader.Find(s_Shaders[name]);
                if (shader == null)
                {
                    Debug.LogError($"[UDF] Missing shader {s_Shaders[name]}. Run 'UpsideDownFishing > Setup Project'.");
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                m = new Material(shader) { name = "UDF_" + name };
            }
            s_Base[name] = m;
            return m;
        }

        public static Material New(string name) => new Material(Base(name));

        public static Material Toon_(Color baseColor, Color? emission = null)
        {
            var m = New(Toon);
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_ShadeColor", Color.Lerp(new Color(0.45f, 0.5f, 0.85f), baseColor, 0.25f));
            if (emission.HasValue) m.SetColor("_Emission", emission.Value);
            return m;
        }

        public static Material Glow_(Color c, float soft = 0f)
        {
            var m = New(Glow);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Soft", soft);
            return m;
        }
    }
}
