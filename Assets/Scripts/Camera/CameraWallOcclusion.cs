using System.Collections.Generic;
using UnityEngine;

namespace MedicalShiftSimulator.CameraSystem
{
    /// <summary>
    /// Detects walls between the camera and the room around its target every
    /// frame and tells their WallFade component to hide/reappear. Casts a
    /// small fan of SphereCasts (a "fat ray") - one straight at the target
    /// plus a few offset toward the camera's local left/right/up/down - rather
    /// than a single thin Raycast, so a wall is caught both when it sits
    /// directly between camera and player AND when it's off to the side but
    /// still covering a meaningful chunk of the room's view. Every cast uses
    /// *NonAlloc/*All (never a single hit) so several walls stacked along the
    /// same line of sight are all handled at once.
    ///
    /// Only objects that both sit on wallLayerMask AND carry a WallFade
    /// component are ever touched - beds, patients, monitors, floors and
    /// other clinical objects are never affected, regardless of layer.
    /// </summary>
    public class CameraWallOcclusion : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private LayerMask wallLayerMask = ~0;
        [SerializeField] private float castRadius = 0.35f;
        [SerializeField] private float targetHeightOffset = 0.9f;
        [Tooltip("How far (in meters, along the camera's local right/up) the side sample points are offset from the target, to also catch walls blocking the room peripherally rather than only directly in front of the character.")]
        [SerializeField] private float sideSampleSpread = 1.1f;
        [SerializeField] private int maxHitsPerCast = 8;

        private readonly HashSet<WallFade> _currentlyHidden = new HashSet<WallFade>();
        private readonly HashSet<WallFade> _hiddenLastFrame = new HashSet<WallFade>();
        private RaycastHit[] _hitsBuffer;
        private Vector3[] _sampleOffsets;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void Awake()
        {
            _hitsBuffer = new RaycastHit[Mathf.Max(1, maxHitsPerCast)];
            _sampleOffsets = new Vector3[5];
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            _currentlyHidden.Clear();

            Vector3 basePoint = target.position + Vector3.up * targetHeightOffset;
            Vector3 right = transform.right;
            Vector3 up = Vector3.up;

            // Center sample (straight camera-to-character line) plus a small
            // cross of side samples so walls blocking the room peripherally -
            // not just the exact line to the character - are also caught.
            if (_sampleOffsets == null || _sampleOffsets.Length != 5)
            {
                _sampleOffsets = new Vector3[5];
            }
            _sampleOffsets[0] = Vector3.zero;
            _sampleOffsets[1] = right * sideSampleSpread;
            _sampleOffsets[2] = -right * sideSampleSpread;
            _sampleOffsets[3] = up * sideSampleSpread * 0.6f;
            _sampleOffsets[4] = -up * sideSampleSpread * 0.4f;

            Vector3 origin = transform.position;

            for (int s = 0; s < _sampleOffsets.Length; s++)
            {
                Vector3 samplePoint = basePoint + _sampleOffsets[s];
                Vector3 toSample = samplePoint - origin;
                float distance = toSample.magnitude;
                if (distance < 0.01f) continue;

                Vector3 direction = toSample / distance;
                int hitCount = Physics.SphereCastNonAlloc(origin, castRadius, direction, _hitsBuffer, distance, wallLayerMask, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    var hitCollider = _hitsBuffer[i].collider;
                    if (hitCollider == null) continue;

                    var wall = hitCollider.GetComponentInParent<WallFade>();
                    if (wall != null)
                    {
                        _currentlyHidden.Add(wall);
                    }
                }
            }

            foreach (var wall in _currentlyHidden)
            {
                wall.SetHidden(true);
            }

            foreach (var wall in _hiddenLastFrame)
            {
                if (!_currentlyHidden.Contains(wall))
                {
                    wall.SetHidden(false);
                }
            }

            _hiddenLastFrame.Clear();
            foreach (var wall in _currentlyHidden)
            {
                _hiddenLastFrame.Add(wall);
            }
        }
    }
}
