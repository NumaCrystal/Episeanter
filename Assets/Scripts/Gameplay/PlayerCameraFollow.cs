using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace SailingSandbox.Gameplay
{
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(Camera))]
    public class PlayerCameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 cameraOffset = new(0f, 10f, -6.4f);
        [SerializeField] private Vector3 cameraRotation = new(60f, 0f, 0f);
        [SerializeField] private float positionSmoothTime = 0.45f;
        [SerializeField] private float lookAheadTime = 0.25f;
        [SerializeField] private float maxLookAheadDistance = 3f;
        [SerializeField] private float lookAheadSmoothSpeed = 6f;
        [SerializeField] private float lookAheadReturnDelay = 1f;
        [SerializeField] private float lookAheadReturnSpeed = 2f;
        [SerializeField] private float rotationSmoothSpeed = 8f;
        [SerializeField] private float rotationSpeed = 90f;
        [SerializeField] private float resetSmoothTime = 0.35f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float minDistance = 4f;
        [SerializeField] private float maxDistance = 20f;

        private Transform _cameraPivot;
        private Camera _camera;
        private Transform _target;
        private float _yaw;
        private float _pitch;
        private float _distance;
        private Vector3 _positionFollowVelocity;
        private Vector3 _lastTargetPosition;
        private Vector3 _smoothedTargetVelocity;
        private float _lookAheadIdleTime;
        private float _resetYaw;
        private float _resetPitch;
        private float _resetDistance;
        private float _yawResetVelocity;
        private float _pitchResetVelocity;
        private float _distanceResetVelocity;
        private bool _isResetting;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                if (NetworkClient.localPlayer == null)
                    return;

                _target = NetworkClient.localPlayer.transform;
                CreatePivot();
            }

            if (_cameraPivot == null)
                return;

            UpdateCameraInput();
            Vector3 lookAhead = UpdateLookAhead();

            _cameraPivot.position = Vector3.SmoothDamp(
                _cameraPivot.position,
                _target.position + lookAhead,
                ref _positionFollowVelocity,
                Mathf.Max(0.01f, positionSmoothTime));
            _cameraPivot.rotation = Quaternion.Slerp(
                _cameraPivot.rotation,
                _target.rotation * Quaternion.Euler(0f, _yaw, 0f),
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));

            float pitchRadians = _pitch * Mathf.Deg2Rad;
            _camera.transform.localPosition = new Vector3(
                0f,
                Mathf.Sin(pitchRadians) * _distance,
                -Mathf.Cos(pitchRadians) * _distance);
            _camera.transform.localRotation = Quaternion.Euler(
                _pitch,
                0f,
                cameraRotation.z);
        }

        private void CreatePivot()
        {
            GameObject pivotObject = new("PlayerCameraPivot");
            _cameraPivot = pivotObject.transform;
            _cameraPivot.position = _target.position;
            _cameraPivot.rotation = _target.rotation;
            _lastTargetPosition = _target.position;
            _smoothedTargetVelocity = Vector3.zero;
            _lookAheadIdleTime = 0f;

            InitializeCameraSettings();

            transform.SetParent(_cameraPivot, false);
        }

        private Vector3 UpdateLookAhead()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return Vector3.zero;

            Vector3 targetVelocity = (_target.position - _lastTargetPosition) / deltaTime;
            _lastTargetPosition = _target.position;
            targetVelocity.y = 0f;

            if (targetVelocity.sqrMagnitude > 0.01f)
            {
                _lookAheadIdleTime = 0f;
                float velocityFactor = 1f - Mathf.Exp(
                    -lookAheadSmoothSpeed * deltaTime);
                _smoothedTargetVelocity = Vector3.Lerp(
                    _smoothedTargetVelocity,
                    targetVelocity,
                    velocityFactor);
            }
            else
            {
                _lookAheadIdleTime += deltaTime;
                if (_lookAheadIdleTime >= lookAheadReturnDelay)
                {
                    float returnFactor = 1f - Mathf.Exp(
                        -lookAheadReturnSpeed * deltaTime);
                    _smoothedTargetVelocity = Vector3.Lerp(
                        _smoothedTargetVelocity,
                        Vector3.zero,
                        returnFactor);
                }
            }

            Vector3 lookAhead = _smoothedTargetVelocity * lookAheadTime;
            return Vector3.ClampMagnitude(
                lookAhead,
                Mathf.Max(0f, maxLookAheadDistance));
        }

        private void UpdateCameraInput()
        {
            if (Keyboard.current != null
                && Keyboard.current.numpad5Key.wasPressedThisFrame)
            {
                BeginCameraReset();
                return;
            }

            if (_isResetting)
            {
                UpdateCameraReset();
                return;
            }

            if (Keyboard.current == null)
                return;

            float deltaTime = Time.deltaTime;
            float yawInput = GetKeyValue(Keyboard.current.numpad6Key)
                             - GetKeyValue(Keyboard.current.numpad4Key);
            float pitchInput = GetKeyValue(Keyboard.current.numpad8Key)
                               - GetKeyValue(Keyboard.current.numpad2Key);

            _yaw += yawInput * rotationSpeed * deltaTime;
            _pitch = Mathf.Clamp(
                _pitch + pitchInput * rotationSpeed * deltaTime,
                Mathf.Min(minPitch, maxPitch),
                Mathf.Max(minPitch, maxPitch));

            float zoomOutInput = Mathf.Max(
                GetKeyValue(Keyboard.current.numpadMinusKey),
                GetKeyValue(Keyboard.current.numpad7Key));
            float zoomInInput = Mathf.Max(
                GetKeyValue(Keyboard.current.numpadPlusKey),
                GetKeyValue(Keyboard.current.numpad9Key));
            float zoomInput = zoomOutInput - zoomInInput;
            _distance = Mathf.Clamp(
                _distance + zoomInput * zoomSpeed * deltaTime,
                Mathf.Min(minDistance, maxDistance),
                Mathf.Max(minDistance, maxDistance));
        }

        private void InitializeCameraSettings()
        {
            _yaw = cameraRotation.y;
            _pitch = Mathf.Clamp(
                cameraRotation.x,
                Mathf.Min(minPitch, maxPitch),
                Mathf.Max(minPitch, maxPitch));
            _distance = Mathf.Clamp(
                cameraOffset.magnitude,
                Mathf.Min(minDistance, maxDistance),
                Mathf.Max(minDistance, maxDistance));
        }

        private void BeginCameraReset()
        {
            _resetYaw = cameraRotation.y;
            _resetPitch = Mathf.Clamp(
                cameraRotation.x,
                Mathf.Min(minPitch, maxPitch),
                Mathf.Max(minPitch, maxPitch));
            _resetDistance = Mathf.Clamp(
                cameraOffset.magnitude,
                Mathf.Min(minDistance, maxDistance),
                Mathf.Max(minDistance, maxDistance));
            _yawResetVelocity = 0f;
            _pitchResetVelocity = 0f;
            _distanceResetVelocity = 0f;
            _isResetting = true;
        }

        private void UpdateCameraReset()
        {
            _yaw = Mathf.SmoothDampAngle(
                _yaw,
                _resetYaw,
                ref _yawResetVelocity,
                Mathf.Max(0.01f, resetSmoothTime));
            _pitch = Mathf.SmoothDamp(
                _pitch,
                _resetPitch,
                ref _pitchResetVelocity,
                Mathf.Max(0.01f, resetSmoothTime));
            _distance = Mathf.SmoothDamp(
                _distance,
                _resetDistance,
                ref _distanceResetVelocity,
                Mathf.Max(0.01f, resetSmoothTime));

            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _resetYaw)) < 0.01f
                && Mathf.Abs(_pitch - _resetPitch) < 0.01f
                && Mathf.Abs(_distance - _resetDistance) < 0.01f)
            {
                _yaw = _resetYaw;
                _pitch = _resetPitch;
                _distance = _resetDistance;
                _isResetting = false;
            }
        }

        private static float GetKeyValue(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            return key.isPressed ? 1f : 0f;
        }

        private void OnDestroy()
        {
            if (_cameraPivot != null)
                Destroy(_cameraPivot.gameObject);
        }
    }
}
