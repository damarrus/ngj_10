using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A short expanding explosion flash: a white-hot core punches out, tinted to a
    /// fiery orange as it expands and fades. Cheap, no particles.
    /// </summary>
    public class ImpactFlash : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _sprite;
        [SerializeField] private float _duration = 0.45f;
        [SerializeField] private float _startScale = 0.5f;
        [SerializeField] private float _endScale = 4.2f;
        [Tooltip("Hot flash colour at the very start of the blast.")]
        [SerializeField] private Color _hotColor = new Color(1f, 0.97f, 0.8f, 1f);
        [Tooltip("Cooler fiery colour the flash settles into as it expands.")]
        [SerializeField] private Color _fireColor = new Color(1f, 0.45f, 0.1f, 1f);

        private float _t;

        private void Awake()
        {
            if (_sprite == null) _sprite = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _t += Time.deltaTime / _duration;
            float k = Mathf.Clamp01(_t);

            // Ease-out expansion: bursts fast, then eases.
            float s = Mathf.Lerp(_startScale, _endScale, 1f - (1f - k) * (1f - k));
            transform.localScale = new Vector3(s, s, 1f);

            if (_sprite != null)
            {
                // Colour: white-hot for the first instant, cooling to fire.
                Color c = Color.Lerp(_hotColor, _fireColor, Mathf.Clamp01(k * 2.2f));
                // Alpha: a brief over-bright punch (>1 reads as a hard flash) then fade out.
                float punch = k < 0.12f ? 1f : Mathf.Clamp01(1f - (k - 0.12f) / 0.88f);
                c.a = punch;
                _sprite.color = c;
            }
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
