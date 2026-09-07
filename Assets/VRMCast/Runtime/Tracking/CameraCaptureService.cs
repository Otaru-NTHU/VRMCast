using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRMCast.Tracking
{
    /// <summary>
    /// Selectable webcam source (PRD 6) on top of <see cref="WebCamTexture"/>: every camera macOS exposes to the app
    /// (built-in, USB, Continuity Camera) appears by name; nothing is hard-coded. Handles permission, device loss and
    /// recovery without switching cameras behind the user's back.
    /// </summary>
    public sealed class CameraCaptureService : IDisposable
    {
        public const int PreferredWidth = 1280;
        public const int PreferredHeight = 720;
        public const int PreferredFps = 30;
        public const float StallSeconds = 2.5f;
        public const float RecoveryIntervalSeconds = 2f;

        public enum State { Stopped, RequestingPermission, PermissionDenied, Starting, Running, Stalled, NoDevice, Failed }

        private readonly MonoBehaviour _host;
        private WebCamTexture _texture;
        private Coroutine _startRoutine;
        private float _lastFrameTime;
        private float _lastRecoveryAttempt;
        private bool _disposed;

        public State CurrentState { get; private set; } = State.Stopped;
        public string SelectedDevice { get; private set; }
        public bool MirrorPreview { get; set; } = true;
        public bool RequestedRunning { get; private set; }

        /// <summary>Live texture while running; null otherwise. Do not cache across state changes.</summary>
        public WebCamTexture Texture => CurrentState == State.Running || CurrentState == State.Stalled ? _texture : null;

        public bool HasFreshFrame => CurrentState == State.Running && _texture != null && _texture.didUpdateThisFrame;

        public event Action<State> StateChanged;
        public event Action DevicesChanged;

        /// <param name="host">MonoBehaviour used to run coroutines (the bootstrap).</param>
        public CameraCaptureService(MonoBehaviour host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public IReadOnlyList<string> Devices
        {
            get
            {
                var names = new List<string>();
                foreach (var d in WebCamTexture.devices) names.Add(d.name);
                return names;
            }
        }

        public IReadOnlyList<string> RefreshDevices()
        {
            var names = Devices;
            DevicesChanged?.Invoke();
            return names;
        }

        /// <summary>Selects a device by name and (re)starts capture if it was running.</summary>
        public void Select(string deviceName)
        {
            if (deviceName == SelectedDevice) return;
            var wasRunning = RequestedRunning;
            Stop();
            SelectedDevice = deviceName;
            if (wasRunning) Start();
        }

        public void Start()
        {
            if (_disposed) return;
            RequestedRunning = true;
            if (_startRoutine != null) return;
            _startRoutine = _host.StartCoroutine(StartRoutine());
        }

        public void Stop()
        {
            RequestedRunning = false;
            if (_startRoutine != null)
            {
                _host.StopCoroutine(_startRoutine);
                _startRoutine = null;
            }
            ReleaseTexture();
            SetState(State.Stopped);
        }

        private IEnumerator StartRoutine()
        {
            SetState(State.RequestingPermission);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
                if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                {
                    SetState(State.PermissionDenied);
                    _startRoutine = null;
                    yield break;
                }
            }

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                SetState(State.NoDevice);
                _startRoutine = null;
                yield break;
            }

            var name = SelectedDevice;
            var found = false;
            foreach (var d in devices) if (d.name == name) { found = true; break; }
            if (!found)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    // The chosen camera is gone: report it instead of silently switching (PRD 6.3).
                    SetState(State.NoDevice);
                    _startRoutine = null;
                    yield break;
                }
                name = devices[0].name;
                SelectedDevice = name;
            }

            SetState(State.Starting);
            ReleaseTexture();
            try
            {
                _texture = new WebCamTexture(name, PreferredWidth, PreferredHeight, PreferredFps);
                _texture.Play();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ReleaseTexture();
                SetState(State.Failed);
                _startRoutine = null;
                yield break;
            }

            // macOS delivers the first real frame a little later; until then width is 16.
            var deadline = Time.realtimeSinceStartup + 8f;
            while (_texture != null && _texture.width <= 16 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (_texture == null || _texture.width <= 16)
            {
                ReleaseTexture();
                SetState(State.Failed);
                _startRoutine = null;
                yield break;
            }

            _lastFrameTime = Time.realtimeSinceStartup;
            SetState(State.Running);
            _startRoutine = null;
        }

        /// <summary>Call every frame: watches for a stalled device and retries periodically.</summary>
        public void Tick()
        {
            if (_disposed || !RequestedRunning) return;

            switch (CurrentState)
            {
                case State.Running:
                    if (_texture == null) { SetState(State.Failed); break; }
                    if (_texture.didUpdateThisFrame) _lastFrameTime = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - _lastFrameTime > StallSeconds) SetState(State.Stalled);
                    break;

                case State.Stalled:
                    if (_texture != null && _texture.didUpdateThisFrame)
                    {
                        _lastFrameTime = Time.realtimeSinceStartup;
                        SetState(State.Running);
                    }
                    else TryRecover();
                    break;

                case State.NoDevice:
                case State.Failed:
                    TryRecover();
                    break;
            }
        }

        private void TryRecover()
        {
            if (_startRoutine != null || Time.realtimeSinceStartup - _lastRecoveryAttempt < RecoveryIntervalSeconds) return;
            _lastRecoveryAttempt = Time.realtimeSinceStartup;
            _startRoutine = _host.StartCoroutine(StartRoutine());
        }

        private void ReleaseTexture()
        {
            if (_texture == null) return;
            if (_texture.isPlaying) _texture.Stop();
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }

        private void SetState(State state)
        {
            if (CurrentState == state) return;
            CurrentState = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
