using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Visual-only wind: a set of translucent streaks drifting across the screen in
    /// the wind direction, wrapping around so the flow is continuous. Spawns its own
    /// streak sprites from a template at runtime.
    /// </summary>
    public class WindStream : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _streakTemplate;
        [SerializeField] private int _count = 14;
        [SerializeField] private float _speed = 6f;
        [SerializeField] private int _sortingOrder = 40;

        private Camera _cam;
        private Vector2 _dir;
        private Vector2 _perp;
        private float _spanAlong;   // travel extent before wrap
        private float _spanAcross;  // spread perpendicular
        private Transform[] _streaks;
        private float[] _offsets;   // distance along dir

        public void Init(Vector2 dir, Camera cam)
        {
            _cam = cam != null ? cam : Camera.main;
            _dir = dir.normalized;
            _perp = new Vector2(-_dir.y, _dir.x);

            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;
            float diag = Mathf.Sqrt(halfW * halfW + halfH * halfH);
            _spanAlong = diag * 2f + 2f;
            _spanAcross = diag * 2f;

            float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;

            _streaks = new Transform[_count];
            _offsets = new float[_count];
            for (int i = 0; i < _count; i++)
            {
                var sr = Instantiate(_streakTemplate, transform);
                sr.gameObject.SetActive(true);
                sr.sortingOrder = _sortingOrder;
                sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                _streaks[i] = sr.transform;
                _offsets[i] = Random.Range(0f, _spanAlong);
                PlaceStreak(i, Random.Range(-_spanAcross * 0.5f, _spanAcross * 0.5f));
            }
            if (_streakTemplate != null) _streakTemplate.gameObject.SetActive(false);
        }

        private readonly System.Collections.Generic.Dictionary<int, float> _across = new();

        private void PlaceStreak(int i, float across)
        {
            _across[i] = across;
            UpdateStreakPos(i);
        }

        private void UpdateStreakPos(int i)
        {
            Vector2 c = _cam.transform.position;
            Vector2 start = c - _dir * (_spanAlong * 0.5f);
            float acr = _across.TryGetValue(i, out var v) ? v : 0f;
            Vector2 pos = start + _dir * _offsets[i] + _perp * acr;
            _streaks[i].position = new Vector3(pos.x, pos.y, 0f);
        }

        private void Update()
        {
            if (_streaks == null) return;
            float d = _speed * Time.deltaTime;
            for (int i = 0; i < _streaks.Length; i++)
            {
                _offsets[i] += d;
                if (_offsets[i] > _spanAlong)
                {
                    _offsets[i] -= _spanAlong;
                    _across[i] = Random.Range(-_spanAcross * 0.5f, _spanAcross * 0.5f);
                }
                UpdateStreakPos(i);
            }
        }
    }
}
