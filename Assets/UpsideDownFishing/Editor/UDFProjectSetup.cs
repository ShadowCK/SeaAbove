using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace UpsideDownFishing.EditorTools
{
    /// <summary>
    /// One-click project setup: creates the shader materials under Resources (so the custom
    /// shaders ship in builds), builds the Main scene from the PICO template scene, enables the
    /// MR features (VST + Scene Capture) and registers the scene in Build Settings.
    /// </summary>
    public static class UDFProjectSetup
    {
        const string Root = "Assets/UpsideDownFishing";
        const string MatDir = Root + "/Resources/UDF";
        public const string MainScene = Root + "/Scenes/Main.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";

        static readonly (string name, string shader)[] Materials =
        {
            ("Toon", "UDF/Toon"), ("CeilingWater", "UDF/CeilingWater"), ("WaterVolume", "UDF/WaterVolume"),
            ("Glow", "UDF/Glow"), ("DepthOccluder", "UDF/DepthOccluder"),
        };

        [MenuItem("UpsideDownFishing/Setup Project (materials + Main scene)", priority = 0)]
        public static void SetupMenu() => Debug.Log(Setup());

        /// <summary>Entry point for -batchmode -executeMethod.</summary>
        public static void SetupBatch()
        {
            string log = "";
            try { log = Setup(); Debug.Log(log); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
            File.WriteAllText("Logs/udf_setup.txt", log);
            EditorApplication.Exit(0);
        }

        public static string Setup()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();

            foreach (var (name, shaderName) in Materials)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) { log.AppendLine($"ERROR shader not found: {shaderName}"); continue; }
                string path = $"{MatDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); log.AppendLine("Created " + path); }
                else if (mat.shader != shader) { mat.shader = shader; EditorUtility.SetDirty(mat); }
                mat.enableInstancing = true;
            }
            AssetDatabase.SaveAssets();

            // Main scene = template scene (XR Origin + PICO setup) + our bootstrap.
            if (!File.Exists(MainScene))
            {
                if (!File.Exists(TemplateScene)) { log.AppendLine("ERROR template scene missing: " + TemplateScene); return log.ToString(); }
                AssetDatabase.CopyAsset(TemplateScene, MainScene);
                log.AppendLine("Copied template scene -> " + MainScene);
            }
            var scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            var boot = Object.FindAnyObjectByType<GameBootstrap>();
            if (boot == null)
            {
                var go = new GameObject("UpsideDownFishing");
                go.AddComponent<GameBootstrap>();
                log.AppendLine("Added GameBootstrap");
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.nearClipPlane = 0.03f;
            }
            var light = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (light != null)
            {
                // warm top-down-ish key light so the upside-down fish read nicely from below
                light.transform.rotation = Quaternion.Euler(70f, -30f, 0);
                light.color = new Color(1f, 0.96f, 0.9f);
                light.shadows = LightShadows.None;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            log.AppendLine("Build Settings scenes = [" + MainScene + "]");

#if ENABLE_PICO_XR_SDK
            var cfg = PXR_ProjectSetting.GetProjectConfig();
            cfg.videoSeeThrough = true;
            cfg.sceneCapture = true;
            EditorUtility.SetDirty(cfg);
            PXR_ProjectSetting.SaveAssets();
            log.AppendLine($"PICO features: videoSeeThrough={cfg.videoSeeThrough} sceneCapture={cfg.sceneCapture}");
#endif
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general != null && general.Manager != null)
                foreach (var l in general.Manager.activeLoaders) log.AppendLine("Android XR loader: " + l.GetType().FullName);
            else log.AppendLine("WARNING: no XR Plug-in Management settings for Android");

            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.xicheng.upsidedownfishing");
            AssetDatabase.SaveAssets();
            log.AppendLine("Setup complete.");
            return log.ToString();
        }
    }
}
