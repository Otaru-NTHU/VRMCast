using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRMCast.App;

namespace VRMCast.Editor
{
    /// <summary>
    /// Regenerates the Main scene and the UI assets it depends on. The committed scene was authored to match this
    /// builder; if Unity ever reports missing references (for example after a Unity upgrade changes serialized
    /// formats), run <c>VRMCast &gt; Setup &gt; Rebuild Main Scene</c> and commit the result.
    /// The build script runs this first so a player build never depends on a hand-edited scene.
    /// </summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/VRMCast/Scenes/Main.unity";
        public const string UiFolder = "Assets/VRMCast/UI";
        public const string PanelSettingsPath = UiFolder + "/VRMCastPanelSettings.asset";
        public const string ThemePath = UiFolder + "/VRMCastRuntimeTheme.tss";
        public const string LayoutPath = UiFolder + "/Main.uxml";
        public const string MaterialPath = UiFolder + "/BackgroundImage.mat";
        public const string FaceLandmarkerModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/face_landmarker_v2_with_blendshapes.bytes";
        public const string PoseLandmarkerModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/pose_landmarker_lite.bytes";
        public const string HandLandmarkerModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/hand_landmarker.bytes";
        public const string HolisticLandmarkerModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/holistic_landmarker.bytes";

        [MenuItem("VRMCast/Setup/Rebuild Main Scene")]
        public static void RebuildFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("VRMCast", "Exit Play Mode before rebuilding the scene.", "OK");
                return;
            }
            Build();
            EditorUtility.DisplayDialog("VRMCast", "Main scene and UI assets were rebuilt.", "OK");
        }

        public static void Build()
        {
            var panelSettings = EnsurePanelSettings();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            if (layout == null) Debug.LogError($"MainSceneBuilder: {LayoutPath} not found.");
            var material = EnsureMaterial();

            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var app = FindOrCreate(scene, "App");
            // A component whose script no longer resolves (for example a built-in fileID that changed between
            // Unity versions) would otherwise linger as "referenced script (Unknown) is missing".
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(app);
            var bootstrap = app.GetComponent<AppBootstrap>() ?? app.AddComponent<AppBootstrap>();
            var document = app.GetComponent<UIDocument>() ?? app.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;

            var so = new SerializedObject(bootstrap);
            so.FindProperty("_uiDocument").objectReferenceValue = document;
            so.FindProperty("_backgroundImageMaterial").objectReferenceValue = material;
            var model = AssetDatabase.LoadAssetAtPath<TextAsset>(FaceLandmarkerModelPath);
            if (model != null) so.FindProperty("_faceLandmarkerModel").objectReferenceValue = model;
            var poseModel = AssetDatabase.LoadAssetAtPath<TextAsset>(PoseLandmarkerModelPath);
            if (poseModel != null) so.FindProperty("_poseLandmarkerModel").objectReferenceValue = poseModel;
            var handModel = AssetDatabase.LoadAssetAtPath<TextAsset>(HandLandmarkerModelPath);
            if (handModel != null) so.FindProperty("_handLandmarkerModel").objectReferenceValue = handModel;
            var holisticModel = AssetDatabase.LoadAssetAtPath<TextAsset>(HolisticLandmarkerModelPath);
            if (holisticModel != null) so.FindProperty("_holisticLandmarkerModel").objectReferenceValue = holisticModel;
            if (poseModel == null) Debug.LogWarning("MainSceneBuilder: MediaPipe package not installed; face tracking model left unassigned. Run Scripts/setup-mediapipe.sh.");
            so.ApplyModifiedPropertiesWithoutUndo();

            var lightObject = FindOrCreate(scene, "Key Light");
            var light = lightObject.GetComponent<Light>() ?? lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(35f, 20f, 0f);
            lightObject.transform.position = new Vector3(0f, 3f, 3f);
            RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);

            // Renders nothing; only clears the window behind the UI so the OS never shows garbage before the
            // first UI frame. The avatar camera renders exclusively into the OutputRenderTexture.
            var clearObject = FindOrCreate(scene, "ScreenClearCamera");
            var clearCamera = clearObject.GetComponent<Camera>() ?? clearObject.AddComponent<Camera>();
            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(0.117f, 0.117f, 0.133f);
            clearCamera.cullingMask = 0;
            clearCamera.orthographic = true;
            clearCamera.depth = -100;
            clearCamera.allowHDR = false;
            clearCamera.allowMSAA = false;
            clearCamera.nearClipPlane = 0.3f;
            clearCamera.farClipPlane = 1f;
            clearObject.transform.position = new Vector3(0f, -100f, 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"MainSceneBuilder: rebuilt {ScenePath}.");
        }

        private static GameObject FindOrCreate(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        public static PanelSettings EnsurePanelSettings()
        {
            Directory.CreateDirectory(UiFolder);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
                theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            }

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.referenceDpi = 96f;
            settings.fallbackDpi = 96f;
            settings.clearColor = false;
            settings.clearDepthStencil = true;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        public static Material EnsureMaterial()
        {
            var shader = Shader.Find("Unlit/Texture");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "BackgroundImage" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader == null || material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        public static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
            {
                if (s.path == ScenePath) { s.enabled = true; EditorBuildSettings.scenes = scenes; return; }
            }
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
