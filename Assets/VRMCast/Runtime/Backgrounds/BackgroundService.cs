using System;
using System.IO;
using UnityEngine;
using VRMCast.Core.Backgrounds;
using VRMCast.Rendering;

namespace VRMCast.Backgrounds
{
    /// <summary>
    /// Background compositor (PRD 17). Solid, chroma and transparent modes are pure camera clear colors.
    /// Image mode adds an unlit quad parented to the avatar camera, sized every frame to the camera frustum and
    /// the Fill / Fit / Stretch rule. The render target always carries alpha; only the Transparent mode leaves it at 0.
    /// </summary>
    public sealed class BackgroundService : IDisposable
    {
        public const float ImagePlaneDistance = 50f;

        private readonly IRenderService _render;
        private readonly Material _imageMaterial;
        private readonly GameObject _imagePlane;
        private readonly MeshRenderer _imageRenderer;
        private readonly Mesh _quadMesh;
        private Texture2D _imageTexture;
        private bool _disposed;

        public BackgroundSettings Settings { get; }
        /// <summary>Localization key of the last image error, or null.</summary>
        public string LastError { get; private set; }
        public bool HasImage => _imageTexture != null;

        public event Action SettingsChanged;

        /// <param name="imageMaterial">An unlit textured material (serialized asset) used for the image plane. Instantiated internally.</param>
        public BackgroundService(IRenderService render, Material imageMaterial, BackgroundSettings settings = null)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            Settings = settings ?? new BackgroundSettings();

            _imageMaterial = imageMaterial != null
                ? new Material(imageMaterial)
                : new Material(Shader.Find("Unlit/Texture"));
            _imageMaterial.name = "BackgroundImage (runtime)";

            _imagePlane = new GameObject("BackgroundImagePlane");
            _imagePlane.transform.SetParent(_render.AvatarCamera.transform, worldPositionStays: false);
            _imagePlane.transform.localPosition = new Vector3(0f, 0f, ImagePlaneDistance);
            _imagePlane.transform.localRotation = Quaternion.identity;
            _quadMesh = CreateQuadMesh();
            _imagePlane.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            _imageRenderer = _imagePlane.AddComponent<MeshRenderer>();
            _imageRenderer.sharedMaterial = _imageMaterial;
            _imageRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _imageRenderer.receiveShadows = false;
            _imageRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _imageRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _imagePlane.SetActive(false);

            if (!string.IsNullOrEmpty(Settings.ImagePath))
            {
                TryLoadImage(Settings.ImagePath);
            }
            Apply();
        }

        public void SetMode(BackgroundMode mode)
        {
            if (Settings.Mode == mode) return;
            Settings.Mode = mode;
            Apply();
        }

        public void SetSolidColor(RgbaColor color)
        {
            Settings.SolidColor = color;
            Apply();
        }

        public void SetChromaColor(RgbaColor color)
        {
            Settings.ChromaColor = color;
            Apply();
        }

        public void SetImageFit(ImageFitMode fit)
        {
            Settings.ImageFit = fit;
            Apply();
        }

        /// <summary>Loads a PNG or JPEG from disk. Returns false with a user-facing <see cref="LastError"/> on failure.</summary>
        public bool TryLoadImage(string path)
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LastError = "error.imageMissing";
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LastError = "error.imageUnreadable";
                return false;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = Path.GetFileName(path),
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!texture.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(texture);
                LastError = "error.imageFormat";
                return false;
            }

            ReleaseImage();
            _imageTexture = texture;
            _imageMaterial.mainTexture = texture;
            Settings.ImagePath = path;
            Apply();
            return true;
        }

        public void ClearImage()
        {
            ReleaseImage();
            Settings.ImagePath = null;
            Apply();
        }

        /// <summary>Pushes the current settings to the camera and image plane.</summary>
        public void Apply()
        {
            var c = Settings.ClearColor;
            _render.SetClearColor(new Color(c.R, c.G, c.B, c.A));
            _imagePlane.SetActive(Settings.Mode == BackgroundMode.Image && _imageTexture != null);
            UpdateImagePlane();
            SettingsChanged?.Invoke();
        }

        /// <summary>Call once per frame after the camera moved (LateUpdate) so the plane always covers the frustum.</summary>
        public void UpdateImagePlane()
        {
            if (!_imagePlane.activeSelf || _imageTexture == null) return;

            var cam = _render.AvatarCamera;
            var distance = Mathf.Min(ImagePlaneDistance, cam.farClipPlane * 0.9f);
            var frameHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var frameWidth = frameHeight * cam.aspect;

            var fit = ImageFitSolver.Solve(_imageTexture.width, _imageTexture.height, frameWidth, frameHeight, Settings.ImageFit);
            _imagePlane.transform.localPosition = new Vector3(0f, 0f, distance);
            _imagePlane.transform.localScale = new Vector3(frameWidth * fit.ScaleX, frameHeight * fit.ScaleY, 1f);
        }

        /// <summary>Unit quad facing the camera (front face toward -Z, matching Unity's built-in Quad winding).</summary>
        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "BackgroundImageQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ReleaseImage()
        {
            if (_imageTexture == null) return;
            _imageMaterial.mainTexture = null;
            UnityEngine.Object.Destroy(_imageTexture);
            _imageTexture = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseImage();
            if (_imagePlane != null) UnityEngine.Object.Destroy(_imagePlane);
            if (_quadMesh != null) UnityEngine.Object.Destroy(_quadMesh);
            if (_imageMaterial != null) UnityEngine.Object.Destroy(_imageMaterial);
        }
    }
}
