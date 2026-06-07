using System.Collections;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// One balloon. Rises from the bottom with a per-instance speed and a slight
    /// horizontal sway. Good balloons score points when clicked; bad ones punish.
    /// Spawned and configured by <see cref="BalloonSpawner"/>; reports clicks
    /// back to <see cref="BalloonGameController"/>.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Balloon : MonoBehaviour
    {
        private BalloonGameController _game;
        private float _riseSpeed;
        private float _swayAmplitude;
        private float _swayFrequency;
        private float _baseX;
        private float _phase;
        private float _topY;
        private bool _isBad;
        private bool _popped;
        private Color _color;
        private Sprite _sprite;

        [Header("Pop")]
        [SerializeField] private float _popDuration = 0.09f;
        [SerializeField] private float _popBulge = 1.25f; // peak scale before collapse

        /// <summary>Called once right after instantiation, before the balloon lives.</summary>
        public void Init(
            BalloonGameController game,
            bool isBad,
            float riseSpeed,
            float swayAmplitude,
            float swayFrequency,
            float topY)
        {
            _game = game;
            _isBad = isBad;
            _riseSpeed = riseSpeed;
            _swayAmplitude = swayAmplitude;
            _swayFrequency = swayFrequency;
            _topY = topY;

            _baseX = transform.position.x;
            // Random phase so balloons don't sway in unison.
            _phase = Mathf.Repeat(_baseX * 7.13f, Mathf.PI * 2f);

            var sr = GetComponent<SpriteRenderer>();
            _color = isBad ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.3f, 0.7f, 1f);
            _sprite = sr.sprite;
            sr.color = _color;
        }

        private void Update()
        {
            // Frozen while the pop animation plays out (the coroutine drives it).
            if (_popped)
            {
                return;
            }

            var pos = transform.position;
            pos.y += _riseSpeed * Time.deltaTime;
            _phase += _swayFrequency * Time.deltaTime;
            pos.x = _baseX + Mathf.Sin(_phase) * _swayAmplitude;
            transform.position = pos;

            // Escaped past the top edge: despawn (no penalty for missing one).
            if (pos.y > _topY)
            {
                Destroy(gameObject);
            }
        }

        private void OnMouseDown()
        {
            if (_popped)
            {
                return;
            }

            _popped = true;

            // Gameplay reacts instantly (score + sound) — the squash/burst is
            // pure feedback and must not delay or gate the click.
            _game.OnBalloonClicked(_isBad);

            // Stop further clicks on this dying balloon.
            var col = GetComponent<CircleCollider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            PopBurst.Spawn(transform.position, _color, _sprite);
            StartCoroutine(PopAnimation());
        }

        /// <summary>Quick bulge-then-collapse squash, then despawn.</summary>
        private IEnumerator PopAnimation()
        {
            Vector3 baseScale = transform.localScale;
            float t = 0f;
            while (t < _popDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / _popDuration);
                // Bulge up in the first third, then collapse to zero.
                float scale = p < 0.33f
                    ? Mathf.Lerp(1f, _popBulge, p / 0.33f)
                    : Mathf.Lerp(_popBulge, 0f, (p - 0.33f) / 0.67f);
                transform.localScale = baseScale * scale;
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
