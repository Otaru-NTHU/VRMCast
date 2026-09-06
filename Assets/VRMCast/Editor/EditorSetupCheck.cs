using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRMCast.Editor
{
    /// <summary>
    /// Self-heals the project on first open: if the hand-authored UI assets did not deserialize (for example a
    /// Unity version changed a built-in fileID), rebuilds the scene and assets once per editor session.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorSetupCheck
    {
        private const string SessionKey = "VRMCast.EditorSetupCheck.Ran";

        static EditorSetupCheck()
        {
            EditorApplication.delayCall += Check;
        }

        private static void Check()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(SessionKey, true);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(MainSceneBuilder.PanelSettingsPath);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(MainSceneBuilder.ThemePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MainSceneBuilder.MaterialPath);
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainSceneBuilder.LayoutPath);

            var healthy = panel != null && panel.themeStyleSheet == theme && theme != null
                          && material != null && material.shader != null && material.shader.name == "Unlit/Texture"
                          && layout != null;
            if (healthy) return;

            Debug.LogWarning("VRMCast: UI assets did not load as expected; rebuilding the Main scene and UI assets.");
            MainSceneBuilder.Build();
        }
    }
}
