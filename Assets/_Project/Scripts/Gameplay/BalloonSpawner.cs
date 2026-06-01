using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Spawns balloons at the bottom of the screen on an interval, each with a
    /// randomized horizontal position, rise speed and sway. Builds a runtime
    /// circle sprite so no art assets are needed. Owned by
    /// <see cref="BalloonGameController"/>, which toggles spawning on/off.
    /// </summary>
    public class BalloonSpawner : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float _spawnInterval = 0.45f;

        [Header("Movement ranges")]
        [SerializeField] private float _minRiseSpeed = 2.0f;
        [SerializeField] private float _maxRiseSpeed = 4.5f;
        [SerializeField] private float _maxSwayAmplitude = 1.2f;
        [SerializeField] private float _minSwayFrequency = 1.0f;
        [SerializeField] private float _maxSwayFrequency = 3.0f;

        [Header("Balloon mix")]
        [Range(0f, 1f)]
        [SerializeField] private float _badChance = 0.3f;
        [SerializeField] private float _balloonRadius = 0.5f;

        private BalloonGameController _game;
        private Camera _camera;
        private Sprite _circleSprite;
        private float _timer;
        private bool _spawning;

        public void Begin(BalloonGameController game)
        {
            _game = game;
            _camera = Camera.main;
            _circleSprite = BuildCircleSprite();
            _timer = 0f;
            _spawning = true;
        }

        public void Stop()
        {
            _spawning = false;
        }

        private void Update()
        {
            if (!_spawning)
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = _spawnInterval;
                Spawn();
            }
        }

        private void Spawn()
        {
            // World-space screen extents from the orthographic camera.
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            float centerX = _camera.transform.position.x;
            float centerY = _camera.transform.position.y;

            float margin = _balloonRadius + _maxSwayAmplitude;
            float x = Random.Range(centerX - halfWidth + margin, centerX + halfWidth - margin);
            float spawnY = centerY - halfHeight - _balloonRadius;
            float topY = centerY + halfHeight + _balloonRadius;

            var go = new GameObject("Balloon");
            go.transform.position = new Vector3(x, spawnY, 0f);
            go.transform.localScale = Vector3.one * (_balloonRadius * 2f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _circleSprite;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f; // sprite is unit-sized; scale handles world radius

            bool isBad = Random.value < _badChance;
            float riseSpeed = Random.Range(_minRiseSpeed, _maxRiseSpeed);
            float swayAmplitude = Random.Range(0f, _maxSwayAmplitude);
            float swayFrequency = Random.Range(_minSwayFrequency, _maxSwayFrequency);

            var balloon = go.AddComponent<Balloon>();
            balloon.Init(_game, isBad, riseSpeed, swayAmplitude, swayFrequency, topY);
        }

        /// <summary>Generate a soft white circle sprite at runtime (no art needed).</summary>
        private static Sprite BuildCircleSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    bool inside = dx * dx + dy * dy <= r * r;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size); // pixelsPerUnit = size -> sprite is 1 world unit wide
        }
    }
}
