using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Full-screen colour wipe. Drives a single stretched Image's alpha so the
    /// whole view can fade to black and back. Starts transparent and lets input
    /// through; blocks raycasts only while opaque.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField] private float _fadeDuration = 0.4f;

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
            SetAlpha(0f);
        }

        /// <summary>Fade to fully opaque. Optional duration overrides the default.</summary>
        public IEnumerator FadeOut(float duration = -1f) => FadeTo(1f, duration);

        /// <summary>Fade back to fully transparent. Optional duration overrides the default.</summary>
        public IEnumerator FadeIn(float duration = -1f) => FadeTo(0f, duration);

        private IEnumerator FadeTo(float target, float duration)
        {
            if (duration < 0f)
                duration = _fadeDuration;

            float start = _image.color.a;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(start, target, t / duration));
                yield return null;
            }
            SetAlpha(target);
        }

        /// <summary>Fill the whole screen with <paramref name="color"/> (including its
        /// alpha), easing both RGB and alpha from the current state over the duration.
        /// Used for the victory flash (fade to opaque yellow on touching the sun).</summary>
        public IEnumerator FadeToColor(Color color, float duration)
        {
            Color start = _image.color;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                SetColor(Color.Lerp(start, color, t / duration));
                yield return null;
            }
            SetColor(color);
        }

        private void SetColor(Color c)
        {
            _image.color = c;
            _image.raycastTarget = c.a > 0.001f;
        }

        private void SetAlpha(float a)
        {
            Color c = _image.color;
            c.a = a;
            _image.color = c;
            _image.raycastTarget = a > 0.001f;
        }
    }
}
