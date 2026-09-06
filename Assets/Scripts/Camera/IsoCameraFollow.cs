using UnityEngine;
using UnityEngine.InputSystem;

namespace MedicalShiftSimulator.CameraSystem
{
    /// <summary>
    /// Follows a target with a fixed isometric tilt, while letting the player
    /// orbit horizontally (right mouse button + mouse X) and zoom (mouse
    /// wheel). The target itself never rotates with the camera - only this
    /// transform's yaw/position change.
    ///
    /// The original "follow with a fixed offset" behavior is preserved: the
    /// offset is now expressed as (pitch, yaw, distance) instead of a raw
    /// Vector3, so it can orbit, but at startup it reproduces whatever
    /// position/rotation was authored in the Inspector/scene exactly.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraFollow : MonoBehaviour
    {
        [Header("Alvo e acompanhamento")]
        [SerializeField] private Transform target;
        [SerializeField] private float followSmoothTime = 0.15f;
        [Tooltip("Altura acima do pivo do alvo que a camera mira (aprox. altura do peito).")]
        [SerializeField] private float targetHeightOffset = 0.9f;

        [Header("Inclinacao isometrica")]
        [Tooltip("Inclinacao vertical da camera (eixo X), em graus. ~50 da uma visao isometrica elevada que ainda mostra o interior dos comodos; valores mais altos se aproximam de top-down puro.")]
        [SerializeField, Range(40f, 70f)] private float pitchAngle = 50f;

        [Header("Orbita (botao direito do mouse)")]
        [Tooltip("Graus de rotacao no eixo Y por pixel de movimento horizontal do mouse.")]
        [SerializeField] private float orbitSensitivity = 0.25f;
        [SerializeField] private float orbitSmoothTime = 0.08f;

        [Header("Zoom (roda do mouse)")]
        [SerializeField] private float zoomSensitivity = 1.5f;
        [SerializeField] private float minOrthographicSize = 3f;
        [SerializeField] private float maxOrthographicSize = 14f;
        [SerializeField] private float zoomSmoothSpeed = 8f;

        private Camera _camera;
        private float _currentYaw;
        private float _targetYaw;
        private float _yawVelocity;
        private float _distance;       // orbit radius, captured from the initial offset to the target
        private float _zoomTarget;
        private Vector3 _positionVelocity;
        private bool _snapped;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            // Yaw still starts from whatever orbit angle was authored in the
            // scene; only the pitch is now an explicit, tunable setting instead
            // of being read from the initial transform.
            _currentYaw = transform.eulerAngles.y;
            _targetYaw = _currentYaw;

            _zoomTarget = _camera.orthographicSize;
        }

        private void Start()
        {
            if (target != null)
            {
                // Distance is measured against the ORIGINAL (pre-pitch-change)
                // camera position, which is still a reasonable orbit radius
                // regardless of pitch - pitch and distance are independent.
                _distance = Vector3.Distance(transform.position, TargetPoint());
            }
            if (_distance < 0.01f)
            {
                _distance = 12f;
            }
        }

        private void Update()
        {
            ReadOrbitInput();
            ReadZoomInput();
        }

        private void ReadOrbitInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.isPressed)
            {
                float deltaX = mouse.delta.ReadValue().x;
                _targetYaw += deltaX * orbitSensitivity;
            }
        }

        private void ReadZoomInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _zoomTarget = Mathf.Clamp(_zoomTarget - scroll * zoomSensitivity * 0.01f, minOrthographicSize, maxOrthographicSize);
            }
        }

        private Vector3 TargetPoint()
        {
            return target.position + Vector3.up * targetHeightOffset;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // Smoothly chase the desired yaw so the orbit feels fluid rather than
            // snapping frame-to-frame with the raw mouse delta.
            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, orbitSmoothTime);

            Quaternion rotation = Quaternion.Euler(pitchAngle, _currentYaw, 0f);
            Vector3 desiredPosition = TargetPoint() - rotation * Vector3.forward * _distance;

            if (!_snapped)
            {
                // Avoid an initial swoop from the scene-authored position/pitch to
                // the configured pitchAngle - start already in the right place.
                transform.position = desiredPosition;
                _snapped = true;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, followSmoothTime);
            }
            transform.rotation = rotation;

            if (_camera != null && !Mathf.Approximately(_camera.orthographicSize, _zoomTarget))
            {
                _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _zoomTarget, Time.deltaTime * zoomSmoothSpeed);
            }
        }
    }
}
