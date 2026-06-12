using System;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A small ground marker the player runs through as part of a Run quest. The
    /// first time the player enters, it reports in and pops out. Procedural ring
    /// sprite, no art asset. Spawned and tracked by the owning <see cref="Altar"/>.
    /// </summary>
    public class RunZone : MonoBehaviour
    {
        [SerializeField] private float _radius = 0.7f;
        [SerializeField] private Color _color = new Color(0.4f, 0.85f, 1f, 0.9f);
        [SerializeField] private float _pulseSpeed = 3f;
        [SerializeField] private float _popTime = 0.25f;

        /// <summary>Raised once, when the player first runs through this zone.</summary>
        public event Action<RunZone> Entered;

        private SpriteRenderer _sr;
        private PlayerController _player;
        private bool _done;
        private float _phase;
        private float _popT = -1f;
        private Color _base;

        public Vector2 Position => transform.position;

        private void Awake()
        {
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = BuildRing(96, 0.28f);
            _sr.color = _color;
            _sr.sortingOrder = -45; // above grass, below props/player
            _base = _color;
            transform.localScale = Vector3.one * (_radius * 2f);
        }

        private void Start() => _player = FindAnyObjectByType<PlayerController>();

        private void Update()
        {
            if (_popT >= 0f)
            {
                _popT += Time.deltaTime / _popTime;
                float k = Mathf.Clamp01(_popT);
                transform.localScale = Vector3.one * (_radius * 2f) * Mathf.Lerp(1.3f, 0f, k * k);
                var c = _base; c.a = (1f - k) * _base.a; _sr.color = c;
                if (k >= 1f) Destroy(gameObject);
                return;
            }

            // gentle pulse while waiting
            _phase += Time.deltaTime * _pulseSpeed;
            float s = 1f + Mathf.Sin(_phase) * 0.08f;
            transform.localScale = Vector3.one * (_radius * 2f) * s;

            if (_done || _player == null) return;
            if ((_player.Position - (Vector2)transform.position).sqrMagnitude <= _radius * _radius)
            {
                _done = true;
                Entered?.Invoke(this);
                _popT = 0f; // start pop-out
            }
        }

        private static Sprite BuildRing(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f, outer = r - 1f, inner = outer * (1f - thickness * 2f);
            float soft = Mathf.Max(1f, outer * 0.06f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((outer - d) / soft) * Mathf.Clamp01((d - inner) / soft);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
