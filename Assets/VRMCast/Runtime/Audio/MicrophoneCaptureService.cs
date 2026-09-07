using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Audio;

namespace VRMCast.Audio
{
    /// <summary>
    /// Selectable microphone (PRD 22) feeding an <see cref="AudioLevelMeter"/>. Uses Unity's Microphone API with a
    /// looping 1 s clip that is read incrementally every frame; nothing is played back or routed (broadcast audio
    /// stays OBS's business).
    /// </summary>
    public sealed class MicrophoneCaptureService : IDisposable
    {
        public const int SampleRate = 16000;
        public const int ClipSeconds = 1;

        public enum State { Stopped, RequestingPermission, PermissionDenied, Starting, Running, NoDevice, Failed }

        private readonly MonoBehaviour _host;
        private AudioClip _clip;
        private string _activeDevice;
        private int _readPosition;
        private float[] _buffer = new float[SampleRate / 10];
        private Coroutine _startRoutine;
        private bool _disposed;

        public State CurrentState { get; private set; } = State.Stopped;
        public string SelectedDevice { get; private set; }
        public bool RequestedRunning { get; private set; }
        public AudioLevelMeter Meter { get; }

        /// <summary>True while a microphone delivers samples; Hybrid lip sync falls back to Camera otherwise.</summary>
        public bool IsRunning => CurrentState == State.Running;

        public event Action<State> StateChanged;

        public MicrophoneCaptureService(MonoBehaviour host, AudioLevelSettings settings)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Meter = new AudioLevelMeter(settings);
        }

        public IReadOnlyList<string> Devices => new List<string>(Microphone.devices);

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
            if (_startRoutine != null || CurrentState == State.Running) return;
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
            Release();
            Meter.Reset();
            SetState(State.Stopped);
        }

        private IEnumerator StartRoutine()
        {
            SetState(State.RequestingPermission);
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                {
                    SetState(State.PermissionDenied);
                    _startRoutine = null;
                    yield break;
                }
            }

            var devices = Microphone.devices;
            if (devices.Length == 0)
            {
                SetState(State.NoDevice);
                _startRoutine = null;
                yield break;
            }

            var name = SelectedDevice;
            if (string.IsNullOrEmpty(name) || Array.IndexOf(devices, name) < 0)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    SetState(State.NoDevice);
                    _startRoutine = null;
                    yield break;
                }
                name = devices[0];
                SelectedDevice = name;
            }

            SetState(State.Starting);
            Release();
            try
            {
                _clip = Microphone.Start(name, loop: true, lengthSec: ClipSeconds, frequency: SampleRate);
                _activeDevice = name;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Release();
                SetState(State.Failed);
                _startRoutine = null;
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + 5f;
            while (_clip != null && Microphone.GetPosition(_activeDevice) <= 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (_clip == null || Microphone.GetPosition(_activeDevice) <= 0)
            {
                Release();
                SetState(State.Failed);
                _startRoutine = null;
                yield break;
            }

            _readPosition = 0;
            SetState(State.Running);
            _startRoutine = null;
        }

        /// <summary>Call every frame: reads the samples captured since the last call into the meter.</summary>
        public void Tick()
        {
            if (_disposed || CurrentState != State.Running || _clip == null) return;

            var writePosition = Microphone.GetPosition(_activeDevice);
            if (writePosition < 0) return;
            var total = _clip.samples;
            var available = writePosition - _readPosition;
            if (available < 0) available += total;
            if (available <= 0) return;
            if (available > _buffer.Length) _buffer = new float[available];

            if (_readPosition + available <= total)
            {
                _clip.GetData(_buffer, _readPosition);
            }
            else
            {
                // Wrap around the looping clip.
                var first = total - _readPosition;
                var head = new float[first];
                _clip.GetData(head, _readPosition);
                Array.Copy(head, 0, _buffer, 0, first);
                var tail = new float[available - first];
                _clip.GetData(tail, 0);
                Array.Copy(tail, 0, _buffer, first, tail.Length);
            }
            _readPosition = writePosition;
            Meter.Process(_buffer, available, SampleRate);
        }

        private void Release()
        {
            if (!string.IsNullOrEmpty(_activeDevice) && Microphone.IsRecording(_activeDevice))
            {
                Microphone.End(_activeDevice);
            }
            if (_clip != null)
            {
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }
            _activeDevice = null;
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
