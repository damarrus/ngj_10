using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A flickering flame above an altar whose size/opacity track the altar's anger.
    /// Procedural teardrop sprite (warm gradient), no art asset. Drive it each frame
    /// with <see cref="SetIntensity"/> (0 = out, 1 = full blaze).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class AltarFire : MonoBehaviour
    {
        [Tooltip("Flame scale at full intensity.")]
        [SerializeField] private float _baseScale = 0.85f;
        [Tooltip("Flame scale at the lowest visible intensity (>0), so a low ember still reads.")]
        [SerializeField] private float _minScale = 0.3f;
        [SerializeField] private float _flickerAmount = 0.12f;
        [SerializeField] private float _flickerSpeed = 11f;
        [Tooltip("Above the altar / player, below world UI prompts.")]
        [SerializeField] private int _sortingOrder = 10;

        private SpriteRenderer _sr;
        private float _phase;
        private float _intensity;   // 0..1, set by the altar each frame

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _sr.sprite = BuildFlameSprite(64);
            _sr.sortingOrder = _sortingOrder;
            _phase = Mathf.Repeat(transform.position.x * 7.3f, Mathf.PI * 2f); // desync per altar
            Apply();
        }

        /// <summary>Set the flame strength (0 = invisible, 1 = full). Drive each frame from anger.</summary>
        public void SetIntensity(float t01)
        {
            _intensity = Mathf.Clamp01(t01);
        }

        private void Update()
        {
            _phase += Time.deltaTime * _flickerSpeed * Mathf.Lerp(0.5f, 1.3f, _intensity);
            Apply();
        }

        private void Apply()
        {
            // No flame below a tiny threshold.
            if (_intensity <= 0.02f) { if (_sr.enabled) _sr.enabled = false; return; }
            if (!_sr.enabled) _sr.enabled = true;

            float scale = Mathf.Lerp(_minScale, _baseScale, _intensity);
            float f = 1f + Mathf.Sin(_phase) * _flickerAmount + Mathf.Sin(_phase * 2.3f) * _flickerAmount * 0.5f;
            transform.localScale = new Vector3(scale * (2f - f) * 0.7f + scale * 0.3f, scale * f, 1f);

            // brighter / more opaque as it grows
            var c = _sr.color;
            c.a = Mathf.Lerp(0.55f, 1f, _intensity);
            _sr.color = c;
        }

        /// <summary>Teardrop flame: warm gradient, wide warm base fading to a narrow bright tip.</summary>
        private static Sprite BuildFlameSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float cx = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);           // 0 bottom, 1 top
                // Flame half-width: fat near the bottom, pinching to the tip.
                float halfW = size * 0.42f * Mathf.Sin(v * Mathf.PI * 0.92f + 0.08f) * (1f - v * 0.35f);
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - cx);
                    float edge = Mathf.Clamp01((halfW - dx) / Mathf.Max(1f, halfW * 0.45f));
                    if (edge <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                    // Warm core: yellow-white centre → orange → red rim, hotter toward the tip.
                    Color hot = Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.95f, 0.6f), v);
                    Color rim = new Color(0.95f, 0.25f, 0.05f);
                    float core = Mathf.Clamp01(1f - dx / Mathf.Max(1f, halfW));
                    Color c = Color.Lerp(rim, hot, core);
                    c.a = edge;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f), size);
        }
    }
}
