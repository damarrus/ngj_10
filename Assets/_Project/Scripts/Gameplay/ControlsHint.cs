using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Start-screen control hint animation. Layout is built on the scene (mouse,
    /// key base + cap, Icarus, labels) — this only drives the press loop:
    /// • the mouse's left-button fill fades in,
    /// • the key cap sinks onto its base and tints yellow,
    /// • Icarus swaps from folded wings to spread wings,
    /// with a hold pause on each state so "keep holding" reads. Wire the refs in
    /// the inspector.
    /// </summary>
    public class ControlsHint : MonoBehaviour
    {
        [Header("Mouse")]
        [Tooltip("Highlight overlay on the mouse's left button — alpha 0..1.")]
        [SerializeField] private Image _mouseFill;

        [Header("Key")]
        [Tooltip("The key cap that sinks onto its base when held.")]
        [SerializeField] private RectTransform _keyCap;
        [Tooltip("The key cap image, tinted yellow on press.")]
        [SerializeField] private Image _keyFace;
        [Tooltip("How far (px) the key cap sinks when held.")]
        [SerializeField] private float _keyTravel = 6f;

        [Header("Icarus")]
        [SerializeField] private Image _icarus;
        [SerializeField] private Sprite _icarusClosed;
        [SerializeField] private Sprite _icarusOpen;

        [Header("Loop timing (seconds)")]
        [SerializeField] private float _holdReleased = 0.85f;
        [SerializeField] private float _holdPressed = 0.85f;
        [SerializeField] private float _transition = 0.16f;

        private static readonly Color KeyUp = new Color(0.94f, 0.96f, 1f, 0.95f);
        private static readonly Color KeyDown = new Color(1f, 0.86f, 0.45f, 0.95f);

        private Vector2 _keyCapHome;
        private bool _wingsOpen;

        private void Awake()
        {
            if (_keyCap != null)
                _keyCapHome = _keyCap.anchoredPosition;
        }

        private void OnEnable() => StartCoroutine(Loop());

        private void OnDisable()
        {
            StopAllCoroutines();
            Apply(0f);
            SetWings(false);
        }

        private IEnumerator Loop()
        {
            while (true)
            {
                SetWings(true);
                yield return Tween(0f, 1f, _transition);
                yield return Wait(_holdPressed);
                SetWings(false);
                yield return Tween(1f, 0f, _transition);
                yield return Wait(_holdReleased);
            }
        }

        private IEnumerator Tween(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime; // start screen freezes the level (timeScale 0)
                Apply(Mathf.SmoothStep(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            Apply(to);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>pressed = 0 (released) .. 1 (fully held).</summary>
        private void Apply(float pressed)
        {
            if (_mouseFill != null)
            {
                var c = _mouseFill.color;
                c.a = pressed;
                _mouseFill.color = c;
            }
            if (_keyCap != null)
                _keyCap.anchoredPosition = _keyCapHome + new Vector2(0f, -_keyTravel * pressed);
            if (_keyFace != null)
                _keyFace.color = Color.Lerp(KeyUp, KeyDown, pressed);
        }

        private void SetWings(bool open)
        {
            if (_wingsOpen == open && _icarus != null && _icarus.sprite != null) return;
            _wingsOpen = open;
            if (_icarus != null)
                _icarus.sprite = open ? _icarusOpen : _icarusClosed;
        }
    }
}
