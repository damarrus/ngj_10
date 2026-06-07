using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Animated background of large soft circles drifting slowly upward with a
    /// gentle horizontal sway — a cheap "bokeh" look that gives the scene depth
    /// without any art assets. Each circle has its own size, speed, alpha and
    /// sway so they read as parallax layers. Circles that drift past the top edge
    /// wrap back to the bottom, so the field is endless with a fixed object count.
    /// WebGL-friendly: a handful of transparent sprites, no shaders, no per-frame
    /// allocation.
    /// </summary>
    public class BokehBackground : MonoBehaviour
    {
        [Header("Field")]
        [SerializeField] private int _count = 14;
        [SerializeField] private int _sortingOrder = -10; // behind balloons (default 0)

        [Header("Size (world units, diameter)")]
        [SerializeField] private float _minSize = 1.5f;
        [SerializeField] private float _maxSize = 4.5f;

        [Header("Motion")]
        [SerializeField] private float _minRiseSpeed = 0.15f;
        [SerializeField] private float _maxRiseSpeed = 0.6f;
        [SerializeField] private float _maxSwayAmplitude = 0.6f;
        [SerializeField] private float _minSwayFrequency = 0.1f;
        [SerializeField] private float _maxSwayFrequency = 0.4f;

        [Header("Look")]
        [SerializeField] private float _minAlpha = 0.04f;
        [SerializeField] private float _maxAlpha = 0.16f;
        [SerializeField] private Color _tint = new Color(0.6f, 0.8f, 1f);

        private struct Circle
        {
            public Transform Transform;
            public float RiseSpeed;
            public float BaseX;
            public float SwayAmplitude;
            public float SwayFrequency;
            public float Phase;
        }

        private Camera _camera;
        private Sprite _sprite;
        private Circle[] _circles;
        private float _bottomY;
        private float _topY;

        private void Start()
        {
            _camera = Camera.main;
            _sprite = CircleSprite.Build(64, 0.6f); // fuzzy edge for soft bokeh

            ComputeBounds();

            _circles = new Circle[_count];
            for (int i = 0; i < _count; i++)
            {
                _circles[i] = CreateCircle(spawnAcrossScreen: true);
            }
        }

        private void Update()
        {
            for (int i = 0; i < _circles.Length; i++)
            {
                var c = _circles[i];
                var pos = c.Transform.position;
                pos.y += c.RiseSpeed * Time.deltaTime;
                c.Phase += c.SwayFrequency * Time.deltaTime;
                pos.x = c.BaseX + Mathf.Sin(c.Phase) * c.SwayAmplitude;

                float radius = c.Transform.localScale.x * 0.5f;
                if (pos.y - radius > _topY)
                {
                    // Wrapped past the top: recycle to the bottom, re-randomized.
                    Recycle(ref c);
                    pos = c.Transform.position;
                }
                else
                {
                    c.Transform.position = pos;
                }

                _circles[i] = c;
            }
        }

        private void ComputeBounds()
        {
            float halfHeight = _camera.orthographicSize;
            float centerY = _camera.transform.position.y;
            _bottomY = centerY - halfHeight;
            _topY = centerY + halfHeight;
        }

        private Circle CreateCircle(bool spawnAcrossScreen)
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            float centerX = _camera.transform.position.x;

            var go = new GameObject("Bokeh");
            go.transform.SetParent(transform, worldPositionStays: false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.sortingOrder = _sortingOrder;

            float size = Random.Range(_minSize, _maxSize);
            go.transform.localScale = Vector3.one * size;

            // Larger circles read as "closer" — drift faster and brighter (parallax).
            float t = Mathf.InverseLerp(_minSize, _maxSize, size);
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, t);
            sr.color = new Color(_tint.r, _tint.g, _tint.b, alpha);

            float x = Random.Range(centerX - halfWidth, centerX + halfWidth);
            float y = spawnAcrossScreen
                ? Random.Range(_bottomY, _topY) // initial fill spreads them out
                : _bottomY - size * 0.5f;        // recycled ones enter from below
            go.transform.position = new Vector3(x, y, 0f);

            return new Circle
            {
                Transform = go.transform,
                RiseSpeed = Mathf.Lerp(_minRiseSpeed, _maxRiseSpeed, t),
                BaseX = x,
                SwayAmplitude = Random.Range(0f, _maxSwayAmplitude),
                SwayFrequency = Random.Range(_minSwayFrequency, _maxSwayFrequency),
                Phase = Random.Range(0f, Mathf.PI * 2f),
            };
        }

        private void Recycle(ref Circle c)
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            float centerX = _camera.transform.position.x;

            float size = Random.Range(_minSize, _maxSize);
            c.Transform.localScale = Vector3.one * size;

            float t = Mathf.InverseLerp(_minSize, _maxSize, size);
            var sr = c.Transform.GetComponent<SpriteRenderer>();
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, t);
            sr.color = new Color(_tint.r, _tint.g, _tint.b, alpha);

            float x = Random.Range(centerX - halfWidth, centerX + halfWidth);
            c.BaseX = x;
            c.RiseSpeed = Mathf.Lerp(_minRiseSpeed, _maxRiseSpeed, t);
            c.SwayAmplitude = Random.Range(0f, _maxSwayAmplitude);
            c.SwayFrequency = Random.Range(_minSwayFrequency, _maxSwayFrequency);
            c.Transform.position = new Vector3(x, _bottomY - size * 0.5f, 0f);
        }
    }
}
