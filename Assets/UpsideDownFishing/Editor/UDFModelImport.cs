using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UpsideDownFishing.EditorTools
{
    /// <summary>
    /// Prepares Tripo-generated FBX models dropped into Resources/UDF/Models/Seabed:
    /// extracts the embedded textures (1024 max for mobile), and gives each model a
    /// UDF/Underwater material (albedo + normal map) so it gets sea fog and caustics.
    /// </summary>
    public static class UDFModelImport
    {
        public const string SeabedDir = "Assets/UpsideDownFishing/Resources/UDF/Models/Seabed";

        public const string FishDir = "Assets/UpsideDownFishing/Resources/UDF/Models/Fish";

        [MenuItem("UpsideDownFishing/Import Seabed Models", priority = 40)]
        public static void ImportSeabedMenu() => Debug.Log(ImportSeabed());

        [MenuItem("UpsideDownFishing/Import Fish Models", priority = 41)]
        public static void ImportFishMenu() => Debug.Log(ImportFish());

        public static string ImportSeabed() => ImportFolder(SeabedDir, false);

        public const string RodFbx = "Assets/UpsideDownFishing/Resources/UDF/Models/FishingRod.fbx";

        [MenuItem("UpsideDownFishing/Recolor Rod (navy + gold)", priority = 43)]
        public static void RecolorRodMenu() => Debug.Log(RecolorRod());

        /// <summary>
        /// Art direction: glossy navy rod/reel with gold trim. Recolours the scanned rod's
        /// albedo: dark, unsaturated parts (black anodised metal, rubber) become navy; cork,
        /// silver and existing gold details are kept. Writes FishingRod_BaseColor_Navy.png.
        /// </summary>
        public static string RecolorRod()
        {
            string dir = Path.GetDirectoryName(RodFbx).Replace('\\', '/');
            string texDir = dir + "/FishingRodTextures";
            string src = texDir + "/tripo_model_basecolor.jpg";
            string dst = texDir + "/FishingRod_BaseColor_Navy.png";
            if (!File.Exists(src)) return "missing " + src;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            var px = tex.GetPixels();
            var navy = new Color(0.07f, 0.13f, 0.45f);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float sat = mx > 1e-3f ? (mx - mn) / mx : 0f;
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                // weight: dark & grey -> navy; saturated (cork, gold) and bright (silver) stay
                float w = Mathf.Clamp01((0.5f - lum) / 0.25f) * Mathf.Clamp01((0.4f - sat) / 0.2f);
                var target = navy * (0.55f + lum * 2.4f);
                target.a = c.a;
                px[i] = Color.Lerp(c, target, w * 0.9f);
            }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(dst, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dst);
            var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
            ti.sRGBTexture = true; ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.SaveAndReimport();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "/FishingRod.mat");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dst));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/tripo_model_normal.jpg"));
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/FishingRod_MetallicSmoothness.png"));
            EditorUtility.SetDirty(mat);
            // make sure the FBX's embedded material still maps to FishingRod.mat
            var imp = (ModelImporter)AssetImporter.GetAtPath(RodFbx);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(RodFbx).OfType<Material>().Where(m => m != mat).ToList())
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
            imp.SaveAndReimport();
            AssetDatabase.SaveAssets();
            return "recoloured -> " + dst;
        }

        [MenuItem("UpsideDownFishing/Import Rod Model", priority = 42)]
        public static void ImportRodMenu() => Debug.Log(ImportRod());

        /// <summary>
        /// The Tripo rod (grip + reel seat + reel; the blank is procedural): extract textures,
        /// pack metallic (R) + smoothness (A) for URP Lit, point FishingRod.mat at them.
        /// </summary>
        public static string ImportRod()
        {
            string dir = Path.GetDirectoryName(RodFbx).Replace('\\', '/');
            string texDir = dir + "/FishingRodTextures";
            if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder(dir, "FishingRodTextures");
            var imp = (ModelImporter)AssetImporter.GetAtPath(RodFbx);
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.isReadable = true; imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.ExtractTextures(texDir);
            imp.SaveAndReimport();
            AssetDatabase.Refresh();

            string basePath = null, normPath = null, metPath = null, roughPath = null;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g); string l = Path.GetFileName(p).ToLowerInvariant();
                if (l.Contains("basecolor")) basePath = p; else if (l.Contains("normal")) normPath = p;
                else if (l.Contains("metallic")) metPath = p; else if (l.Contains("roughness")) roughPath = p;
            }
            string packedPath = texDir + "/FishingRod_MetallicSmoothness.png";
            if (metPath != null && roughPath != null)
            {
                var met = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); met.LoadImage(File.ReadAllBytes(metPath));
                var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); rough.LoadImage(File.ReadAllBytes(roughPath));
                const int w = 1024, h = 1024;
                var packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                        float m = met.GetPixelBilinear(u, v).r, r = rough.GetPixelBilinear(u, v).g;
                        px[y * w + x] = new Color(m, m, m, 1f - r);
                    }
                packed.SetPixels(px); packed.Apply();
                File.WriteAllBytes(packedPath, packed.EncodeToPNG());
                Object.DestroyImmediate(met); Object.DestroyImmediate(rough); Object.DestroyImmediate(packed);
                AssetDatabase.ImportAsset(packedPath);
                AssetDatabase.DeleteAsset(metPath); AssetDatabase.DeleteAsset(roughPath);
            }
            void SetTex(string p, bool normal, bool srgb)
            {
                if (p == null || AssetImporter.GetAtPath(p) == null) return;
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb; ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.SaveAndReimport();
            }
            SetTex(packedPath, false, false); SetTex(basePath, false, true); SetTex(normPath, true, false);

            string matPath = dir + "/FishingRod.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", basePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(basePath) : null);
            mat.SetTexture("_BumpMap", normPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) : null);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f); mat.SetFloat("_Smoothness", 1f);
            EditorUtility.SetDirty(mat);
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(RodFbx).OfType<Material>().Where(m => m != mat).ToList())
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
            imp.SaveAndReimport();
            AssetDatabase.SaveAssets();

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(RodFbx);
            var mf = go.GetComponentInChildren<MeshFilter>();
            return $"rod: verts={mf.sharedMesh.vertexCount} readable={mf.sharedMesh.isReadable} mat={go.GetComponentInChildren<Renderer>().sharedMaterial.name} base={basePath} normal={normPath}";
        }

        /// <summary>Fish meshes stay readable: FishModels re-orients and rescales them at runtime.</summary>
        public static string ImportFish() => ImportFolder(FishDir, true);

        public const string TackleDir = "Assets/UpsideDownFishing/Resources/UDF/Models/Tackle";

        [MenuItem("UpsideDownFishing/Import Tackle Models (bobber, lure)", priority = 44)]
        public static void ImportTackleMenu() => Debug.Log(ImportTackle());

        /// <summary>Bobber + lure: same treatment as the fish (readable, double-sided, glossy).</summary>
        public static string ImportTackle() => ImportFolder(TackleDir, true);

        public const string AquariumDir = "Assets/UpsideDownFishing/Resources/UDF/Models/Aquarium";

        [MenuItem("UpsideDownFishing/Import Aquarium Models", priority = 45)]
        public static void ImportAquariumMenu() => Debug.Log(ImportAquarium());

        /// <summary>
        /// Upside-down aquarium: wooden leg + lid-corner plant (live in the room: URP Lit like the rod)
        /// and the decor bed (hangs in the tank water: Underwater shader with caustics). All readable:
        /// Aquarium re-centres / scales / flips them.
        /// </summary>
        public static string ImportAquarium()
        {
            // re-generated models: drop the previously extracted textures so stale ones don't linger
            foreach (var fbx in Directory.GetFiles(AquariumDir, "*.fbx"))
            {
                string texDir = $"{AquariumDir}/{Path.GetFileNameWithoutExtension(fbx)}_Tex";
                if (AssetDatabase.IsValidFolder(texDir)) AssetDatabase.DeleteAsset(texDir);
            }
            // detailed Tripo textures: keep 2048 here (the aquarium is seen up close)
            string log = ImportFolder(AquariumDir, true, 2048);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            // stand (wood) and vines (leaves) live in the room: URP Lit like the rod
            void ToLit(string name, float smoothness, bool twoSided)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{AquariumDir}/{name}.mat");
                if (mat == null || lit == null) return;
                var albedo = mat.GetTexture("_BaseMap");
                var normal = mat.GetTexture("_BumpMap");
                mat.shader = lit;
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BaseMap", albedo);
                if (normal) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Cull", twoSided ? 0f : 2f);
                mat.doubleSidedGI = twoSided;
                EditorUtility.SetDirty(mat);
            }
            ToLit("AquariumLeg", 0.5f, false);
            ToLit("AquariumPlant", 0.35f, true);
            AssetDatabase.SaveAssets();
            var decor = AssetDatabase.LoadAssetAtPath<Material>(AquariumDir + "/AquariumDecor.mat");
            if (decor != null) { decor.SetFloat("_Caustics", 1.2f); decor.SetFloat("_Smoothness", 0.3f); EditorUtility.SetDirty(decor); AssetDatabase.SaveAssets(); }
            return log;
        }

        static string ImportFolder(string dir, bool fish, int maxTexture = 1024)
        {
            var log = new StringBuilder();
            if (!Directory.Exists(dir)) return "no folder " + dir;
            var shader = Shader.Find("UDF/Underwater");
            if (shader == null) return "UDF/Underwater shader missing";

            foreach (var fbx in Directory.GetFiles(dir, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                string name = Path.GetFileNameWithoutExtension(fbx);
                var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.meshCompression = fish ? ModelImporterMeshCompression.Off : ModelImporterMeshCompression.Medium;
                imp.isReadable = fish;

                string texDir = $"{dir}/{name}_Tex";
                if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder(dir, name + "_Tex");
                imp.ExtractTextures(texDir);
                imp.SaveAndReimport();
                AssetDatabase.Refresh();

                Texture2D albedo = null, normal = null;
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    string lower = Path.GetFileName(p).ToLowerInvariant();
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    ti.maxTextureSize = maxTexture;
                    ti.mipmapEnabled = true;
                    if (lower.Contains("normal")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
                    else if (lower.Contains("basecolor") || lower.Contains("albedo") || lower.Contains("diffuse")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; }
                    else
                    {
                        // metallic / roughness are not used underwater: delete so they don't ship
                        AssetDatabase.DeleteAsset(p);
                        continue;
                    }
                    ti.SaveAndReimport();
                    if (lower.Contains("normal")) normal = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    else albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }

                string matPath = $"{dir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = shader;
                mat.enableInstancing = true;
                if (albedo) mat.SetTexture("_BaseMap", albedo);
                if (normal) mat.SetTexture("_BumpMap", normal);
                bool coral = name.StartsWith("Coral");
                mat.SetFloat("_Smoothness", fish ? 0.6f : coral ? 0.3f : 0.2f);
                mat.SetFloat("_Caustics", fish ? 0.6f : 1f);
                mat.SetFloat("_RimLight", fish ? 0.25f : 0f);
                mat.SetFloat("_Cull", fish || coral ? 0f : 2f);   // fins / fans / thin branches are double-sided
                EditorUtility.SetDirty(mat);

                foreach (var src in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>())
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
                imp.SaveAndReimport();

                var mesh = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault();
                log.AppendLine($"{name}: verts={(mesh ? mesh.vertexCount : 0)} bounds={(mesh ? mesh.bounds.size.ToString("F2") : "-")} albedo={(albedo ? albedo.name : "none")} normal={(normal ? normal.name : "none")}");
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
