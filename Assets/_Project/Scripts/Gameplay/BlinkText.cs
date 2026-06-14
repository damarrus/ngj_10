using TMPro;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Pulses a TMP label's alpha in a sine wave — the "Продолжить" prompt on the win
    /// screen. Runs on unscaled time so it keeps blinking while the game is frozen
    /// (timeScale 0 on the win).
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class BlinkText : MonoBehaviour
    {
        [Tooltip("Full blink cycles per second.")]
        [SerializeField] private float _speed = 1.5f;
        [Tooltip("Alpha at the dim end of the pulse (bright end is always 1).")]
        [SerializeField, Range(0f, 1f)] private float _minAlpha = 0.2f;

        private TextMeshProUGUI _label;

        private void Awake() => _label = GetComponent<TextMeshProUGUI>();

        private void Update()
        {
            float k = (Mathf.Sin(Time.unscaledTime * _speed * Mathf.PI * 2f) + 1f) * 0.5f;
            var c = _label.color;
            c.a = Mathf.Lerp(_minAlpha, 1f, k);
            _label.color = c;
        }
    }
}
