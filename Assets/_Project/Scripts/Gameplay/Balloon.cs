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
            sr.color = isBad ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.3f, 0.7f, 1f);
        }

        private void Update()
        {
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
            _game.OnBalloonClicked(_isBad);
            Destroy(gameObject);
        }
    }
}
