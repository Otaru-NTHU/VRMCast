using System;
using UnityEngine;
using VRMCast.Core.Rendering;

namespace VRMCast.Rendering
{
    public sealed class RenderService : IRenderService
    {
        public const string CameraObjectName = "AvatarCamera";
        public const int DepthBits = 24;
        public const int AntiAliasing = 4;

        private readonly GameObject _cameraObject;
        private Color _clearColor = Color.green;
        private bool _disposed;

        public Camera AvatarCamera { get; }
        public RenderTexture OutputTexture { get; private set; }
        public OutputSettings Settings { get; private set; }

        public event Action<OutputSettings> SettingsChanged;
        public event Action<RenderTexture, double> FrameRendered;

        /// <param name="parent">Scene object the camera is created under (the bootstrap object).</param>
        /// <param name="initial">Initial output settings; the product default is 1920x1080 @ 30.</param>
        public RenderService(Transform parent, OutputSettings initial)
        {
            _cameraObject = new GameObject(CameraObjectName);
            _cameraObject.transform.SetParent(parent, worldPositionStays: false);

            AvatarCamera = _cameraObject.AddComponent<Camera>();
            AvatarCamera.clearFlags = CameraClearFlags.SolidColor;
            AvatarCamera.backgroundColor = _clearColor;
            AvatarCamera.nearClipPlane = 0.05f;
            AvatarCamera.farClipPlane = 100f;
            AvatarCamera.fieldOfView = 30f;
            AvatarCamera.allowHDR = false;
            AvatarCamera.allowMSAA = true;
            AvatarCamera.useOcclusionCulling = false;
            AvatarCamera.depth = 0;
            // Nothing on the UI layer exists in the 3D scene, but keep the mask explicit so a future
            // in-world overlay cannot leak into the broadcast frame.
            AvatarCamera.cullingMask = ~LayerMask.GetMask("UI");

            SetOutputSettings(initial);
            Camera.onPostRender += OnCameraPostRender;
        }

        public void SetOutputSettings(OutputSettings settings)
        {
            if (OutputTexture != null && settings == Settings) return;

            var old = OutputTexture;
            var rt = new RenderTexture(settings.Width, settings.Height, DepthBits, RenderTextureFormat.ARGB32)
            {
                name = $"OutputRenderTexture {settings.Width}x{settings.Height}",
                antiAliasing = AntiAliasing,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();

            OutputTexture = rt;
            Settings = settings;
            AvatarCamera.targetTexture = rt;
            AvatarCamera.aspect = settings.Aspect;

            // Render cadence is decoupled from tracking (PRD 3.4); the output fps is the render target.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = settings.Fps;

            SettingsChanged?.Invoke(settings);

            if (old != null)
            {
                old.Release();
                UnityEngine.Object.Destroy(old);
            }
        }

        public void SetClearColor(Color color)
        {
            _clearColor = color;
            AvatarCamera.backgroundColor = color;
        }

        private void OnCameraPostRender(Camera cam)
        {
            if (_disposed || cam != AvatarCamera || OutputTexture == null) return;
            FrameRendered?.Invoke(OutputTexture, Time.realtimeSinceStartupAsDouble);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Camera.onPostRender -= OnCameraPostRender;
            if (AvatarCamera != null) AvatarCamera.targetTexture = null;
            if (OutputTexture != null)
            {
                OutputTexture.Release();
                UnityEngine.Object.Destroy(OutputTexture);
                OutputTexture = null;
            }
            if (_cameraObject != null) UnityEngine.Object.Destroy(_cameraObject);
        }
    }
}
