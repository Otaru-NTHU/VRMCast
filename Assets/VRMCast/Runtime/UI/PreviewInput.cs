using UnityEngine.UIElements;
using VRMCast.CameraControl;

namespace VRMCast.UI
{
    /// <summary>
    /// Pointer gestures on the preview element (PRD 16.2), kept inside the preview so they never conflict with
    /// dragging other UI:
    ///   scroll wheel            zoom
    ///   left drag               orbit
    ///   shift + left drag       pan
    ///   right or middle drag    pan
    /// </summary>
    public sealed class PreviewInput
    {
        private readonly VisualElement _target;
        private readonly AvatarCameraController _camera;
        private int _activePointer = -1;
        private bool _panning;

        public PreviewInput(VisualElement target, AvatarCameraController camera)
        {
            _target = target;
            _camera = camera;
            _target.RegisterCallback<WheelEvent>(OnWheel);
            _target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        public void Dispose()
        {
            _target.UnregisterCallback<WheelEvent>(OnWheel);
            _target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            _target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            _target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            _target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        private void OnWheel(WheelEvent evt)
        {
            // Scrolling up (negative delta) zooms in.
            _camera.ZoomBy(-evt.delta.y);
            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_activePointer != -1) return;
            _activePointer = evt.pointerId;
            _panning = evt.button != 0 || evt.shiftKey;
            _target.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _activePointer || !_target.HasPointerCapture(evt.pointerId)) return;
            var d = evt.deltaPosition;
            if (_panning) _camera.PanByPixels(d.x, d.y);
            else _camera.OrbitByPixels(d.x, d.y);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _activePointer) return;
            _target.ReleasePointer(evt.pointerId);
            _activePointer = -1;
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            _activePointer = -1;
        }
    }
}
