using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Falls from the sky onto a fixed target. A red danger zone on the ground marks
    /// the impact area (grows as it descends). On impact, any player within the blast
    /// radius blinks red (no damage yet).
    /// </summary>
    public class Meteor : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _zone;     // red danger telegraph
        [SerializeField] private float _fallDuration = 1f;
        [SerializeField] private float _startHeight = 6f;
        [Tooltip("Max horizontal offset of the spawn point, so meteors come in at a slight random angle instead of straight down.")]
        [SerializeField] private float _maxAngleOffset = 3.5f;
        [SerializeField] private float _blastRadius = 1.6f;
        [SerializeField] private float _zoneVisualScale = 3.2f; // world diameter of the red zone sprite
        [SerializeField] private GameObject _impactPrefab;      // explosion flash on impact

        private Vector2 _target;
        private Vector2 _from;     // randomised spawn point (angled)
        private float _t;

        /// <summary>Call right after instantiation to aim the meteor.</summary>
        public void Init(Vector2 target)
        {
            _target = target;
            // Slight random slant: offset the spawn point sideways so it arcs in at an angle.
            float dx = Random.Range(-_maxAngleOffset, _maxAngleOffset);
            _from = _target + new Vector2(dx, _startHeight);
            transform.position = _from;
            // orient the body along its travel direction so the slant reads visually
            if (_body != null)
            {
                Vector2 dir = (_target - _from).normalized;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f; // sprite points "down" along travel
                _body.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            }
            if (_zone != null)
            {
                _zone.transform.position = _target;
                _zone.transform.localScale = Vector3.one * (_zoneVisualScale * 0.4f);
            }
        }

        private void Update()
        {
            _t += Time.deltaTime / _fallDuration;
            float k = Mathf.Clamp01(_t);

            // Ease-in: starts slow, accelerates into the ground for a punchier fall.
            float kAccel = k * k;
            transform.position = Vector2.Lerp(_from, _target, kAccel);

            if (_zone != null)
            {
                _zone.transform.position = _target;   // stays on the ground
                float s = Mathf.Lerp(_zoneVisualScale * 0.4f, _zoneVisualScale, k);
                _zone.transform.localScale = new Vector3(s, s, 1f);
                // pulse the zone alpha so it reads as a warning
                var c = _zone.color;
                c.a = Mathf.Lerp(0.5f, 1f, Mathf.PingPong(_t * 4f, 1f));
                _zone.color = c;
            }

            if (k >= 1f) Impact();
        }

        private void Impact()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null && (player.Position - _target).sqrMagnitude <= _blastRadius * _blastRadius)
                player.Blink(0.4f);

            if (_impactPrefab != null)
            {
                var fx = Instantiate(_impactPrefab, transform.parent);
                fx.transform.position = _target;
            }

            Destroy(gameObject);
        }
    }
}
