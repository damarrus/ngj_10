using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Gives an <see cref="Image"/> rounded corners of an arbitrary radius by baking a
    /// 9-slice rounded-rect sprite into a tiny texture at runtime. Mirrors the approach
    /// in <see cref="GradientBackground"/>: built once in Awake from inspector fields, no
    /// art asset, WebGL-safe. The 1px stretchable center band keeps the corner radius and
    /// the stroke width constant at any Image size.
    ///
    /// <see cref="_strokeWidth"/> &gt; 0 bakes a thin outline ring of that pixel width
    /// (a clean rounded border for the transparent START button); 0 bakes a filled panel.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class RoundedRectSprite : MonoBehaviour
    {
        [Tooltip("Corner radius in pixels of the baked sprite. Larger = rounder corners.")]
        [SerializeField] private int _radius = 20;

        [Tooltip("Outline thickness in pixels. 0 = filled panel; >0 = ring/border only.")]
        [SerializeField] private int _strokeWidth = 0;

        private Texture2D _texture;

        private void Awake() => Rebuild();

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Rebuild();
        }

        private void Rebuild()
        {
            int r = Mathf.Max(2, _radius);
            int size = r * 2 + 1; // +1 = stretchable center band for the 9-slice

            if (_texture == null || _texture.width != size)
            {
                _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            bool ring = _strokeWidth > 0;
            float stroke = _strokeWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from this pixel to the outer rounded edge (>=0 inside).
                    // In the center row/column (index == r) the corner term is 0, so the
                    // edge distance collapses to the straight side distance — a uniform
                    // stroke along the flat edges, rounded at the corners.
                    float dx = x < r ? r - x : (x > r ? x - r - 1 : 0f);
                    float dy = y < r ? r - y : (y > r ? y - r - 1 : 0f);
                    float corner = Mathf.Sqrt(dx * dx + dy * dy);

                    // Straight-edge distance (how deep the pixel is from the nearest side).
                    float ex = x <= r ? x : (size - 1 - x);
                    float ey = y <= r ? y : (size - 1 - y);
                    float edge = (x < r || x > r) && (y < r || y > r)
                        ? r - corner                 // inside a corner quadrant
                        : Mathf.Min(ex, ey);         // along a flat edge

                    float a;
                    if (ring)
                    {
                        // Opaque only within `stroke` px of the outer edge.
                        float inner = edge - stroke;
                        a = Mathf.Clamp01(edge + 0.5f) - Mathf.Clamp01(inner + 0.5f);
                    }
                    else
                    {
                        a = Mathf.Clamp01(edge + 0.5f); // filled, AA outer edge
                    }
                    _texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            }
            _texture.Apply();

            var border = new Vector4(r, r, r, r);
            var sprite = Sprite.Create(
                _texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);

            var image = GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true; // alpha is baked into the texture, not 9-slice fill
            image.pixelsPerUnitMultiplier = 1f;
        }
    }
}
