using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Paints its <see cref="Image"/> with a vertical colour gradient (top → bottom),
    /// baked once into a 1×N texture from an inspector-authored <see cref="Gradient"/>.
    /// A 1-pixel-wide, tall texture sampled by a stretched Image gives a clean vertical
    /// ramp with no per-frame cost — the texture is built in Awake and never touched
    /// again. Tune the look entirely from the Gradient field; no external art file.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class GradientBackground : MonoBehaviour
    {
        [Tooltip("Top of the bar = top of the screen. Author the sky ramp here.")]
        [SerializeField] private Gradient _gradient = DefaultSky();

        [Tooltip("Vertical resolution of the baked ramp. 256 is smooth and tiny.")]
        [SerializeField] private int _resolution = 256;

        private Texture2D _texture;

        private void Awake() => Rebuild();

        private void OnValidate()
        {
            // Live preview while tuning the gradient in the editor.
            if (isActiveAndEnabled)
                Rebuild();
        }

        private void Rebuild()
        {
            int h = Mathf.Max(2, _resolution);
            if (_texture == null || _texture.height != h)
            {
                _texture = new Texture2D(1, h, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            // Row 0 is the bottom of the texture; the gradient's t=1 is the top of the
            // screen, so map the top row to t=1.
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);
                _texture.SetPixel(0, y, _gradient.Evaluate(t));
            }
            _texture.Apply();

            var image = GetComponent<Image>();
            var rect = new Rect(0f, 0f, 1f, h);
            image.sprite = Sprite.Create(_texture, rect, new Vector2(0.5f, 0.5f));
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        // Sunset sky used in the reference art: warm peach at the top, through pink and
        // violet, down to a deep indigo.
        private static Gradient DefaultSky()
        {
            var g = new Gradient();
            g.colorKeys = new[]
            {
                new GradientColorKey(new Color(0.13f, 0.16f, 0.42f), 0.00f), // bottom indigo
                new GradientColorKey(new Color(0.30f, 0.28f, 0.55f), 0.30f), // violet
                new GradientColorKey(new Color(0.93f, 0.55f, 0.55f), 0.62f), // pink
                new GradientColorKey(new Color(0.97f, 0.78f, 0.55f), 1.00f), // top peach
            };
            g.alphaKeys = new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            };
            return g;
        }
    }
}
