using UnityEngine;
using UnityEngine.Rendering;

namespace MedicalShiftSimulator.CameraSystem
{
    /// <summary>
    /// Attach to a wall (or wall segment) so CameraWallOcclusion can fade it out
    /// when it blocks the view of the player, and fade it back in automatically
    /// once it no longer does.
    ///
    /// A single private material instance is created once in Awake and reused
    /// for the object's whole lifetime - the shared material asset used by
    /// every other wall is never modified, so this never permanently alters
    /// any material. As a robustness net (in case the transparent-surface
    /// trick doesn't render as expected on some shader/pipeline variant), the
    /// Renderer is fully disabled once a wall reaches the fully-hidden state
    /// and re-enabled the instant it starts fading back in - so "invisible"
    /// is guaranteed even if "transparent" isn't visually perfect.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    [DisallowMultipleComponent]
    public class WallFade : MonoBehaviour
    {
        [SerializeField, Range(0.02f, 0.5f)] private float hiddenAlpha = 0.12f;
        [Tooltip("Alpha units per second. ~4.4 gives a full 1.0->hiddenAlpha fade in roughly 0.2s (the requested ~0.15-0.30s range).")]
        [SerializeField] private float fadeSpeed = 4.4f;

        private Renderer _renderer;
        private Material _instanceMaterial;
        private Color _opaqueColor = Color.white;
        private float _currentAlpha = 1f;
        private float _targetAlpha = 1f;
        private bool _isTransparentMode;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _instanceMaterial = new Material(_renderer.sharedMaterial);
            _renderer.material = _instanceMaterial;
            _opaqueColor = GetColor(_instanceMaterial);
        }

        /// <summary>Requests the wall fade toward hidden (true) or fully visible (false).</summary>
        public void SetHidden(bool hidden)
        {
            _targetAlpha = hidden ? hiddenAlpha : 1f;
            if (!hidden && !_renderer.enabled)
            {
                _renderer.enabled = true; // re-enable immediately so the fade-in is visible
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(_currentAlpha, _targetAlpha))
            {
                return;
            }

            _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, fadeSpeed * Time.deltaTime);
            ApplyAlpha();

            bool reachedHiddenTarget = _targetAlpha <= hiddenAlpha + 0.001f && _currentAlpha <= hiddenAlpha + 0.001f;
            bool reachedVisibleTarget = _targetAlpha >= 0.999f && _currentAlpha >= 0.999f;

            if (reachedHiddenTarget)
            {
                _renderer.enabled = false; // guaranteed invisible + saves draw cost
            }
            else if (reachedVisibleTarget && _isTransparentMode)
            {
                RestoreOpaque();
            }
        }

        private void ApplyAlpha()
        {
            if (_currentAlpha < 0.999f && !_isTransparentMode)
            {
                EnterTransparentMode();
            }

            Color c = _opaqueColor;
            c.a = _currentAlpha;
            SetColor(_instanceMaterial, c);
        }

        private void EnterTransparentMode()
        {
            _instanceMaterial.SetFloat(SurfaceId, 1f); // 1 = Transparent surface type (URP Lit)
            _instanceMaterial.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
            _instanceMaterial.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
            _instanceMaterial.SetInt(ZWriteId, 0);
            _instanceMaterial.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            _instanceMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            _instanceMaterial.renderQueue = (int)RenderQueue.Transparent;
            _isTransparentMode = true;
        }

        private void RestoreOpaque()
        {
            _instanceMaterial.SetFloat(SurfaceId, 0f);
            _instanceMaterial.SetInt(ZWriteId, 1);
            _instanceMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _instanceMaterial.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            _instanceMaterial.renderQueue = -1; // back to the shader's default queue
            _isTransparentMode = false;
        }

        private static Color GetColor(Material mat)
        {
            if (mat.HasProperty(BaseColorId)) return mat.GetColor(BaseColorId);
            if (mat.HasProperty(ColorId)) return mat.GetColor(ColorId);
            return Color.white;
        }

        private static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
        }

        private void OnDestroy()
        {
            if (_instanceMaterial != null)
            {
                Destroy(_instanceMaterial);
            }
        }
    }
}
