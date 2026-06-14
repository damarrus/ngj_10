using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Drifting cloud sprites behind the action. Each cloud picks a random art
    /// variant and a depth in [0,1]: far clouds (depth→0) are small, faint and slow;
    /// near clouds (depth→1) are large, opaque and fast. They drift horizontally and
    /// wrap to the far edge once they leave the camera's view, so a small fixed pool
    /// fills an endless sky with no per-frame allocation. Vertically the band tracks
    /// the camera loosely (parallax) so clouds stay on screen as Icarus climbs.
    /// </summary>
    public class CloudBackdrop : MonoBehaviour
    {
        [SerializeField] private Sprite[] _sprites;
        [SerializeField] private int _count = 7;

        [Tooltip("Horizontal margin past the screen edge before a cloud wraps.")]
        [SerializeField] private float _edgeMargin = 6f;
        [Tooltip("Vertical spread of the cloud band around the camera centre.")]
        [SerializeField] private float _bandHeight = 14f;

        [Tooltip("Drift speed range (units/s) mapped by depth: far → near.")]
        [SerializeField] private Vector2 _speedRange = new Vector2(0.4f, 1.6f);
        [Tooltip("Sprite world scale range mapped by depth: far → near.")]
        [SerializeField] private Vector2 _scaleRange = new Vector2(0.7f, 2f);
        [Tooltip("Alpha range mapped by depth: far → near.")]
        [SerializeField] private Vector2 _alphaRange = new Vector2(0.2f, 0.7f);

        [Tooltip("How strongly clouds follow the camera vertically (0 = fixed sky, 1 = locked).")]
        [SerializeField, Range(0f, 1f)] private float _verticalParallax = 0.6f;

        [SerializeField] private int _sortingOrder = -50;

        private struct Cloud
        {
            public Transform Tf;
            public SpriteRenderer Sr;
            public float Depth;   // 0 far .. 1 near
            public float Speed;   // signed units/s
            public float BaseY;   // world Y around the band centre
        }

        private Cloud[] _clouds;
        private Camera _cam;
        private float _halfWidth;

        private void Awake()
        {
            _cam = Camera.main;
            if (_sprites == null || _sprites.Length == 0)
            {
                enabled = false;
                return;
            }

            _clouds = new Cloud[_count];
            for (int i = 0; i < _count; i++)
                _clouds[i] = Spawn(i, initialScatter: true);
        }

        private Cloud Spawn(int i, bool initialScatter)
        {
            var go = new GameObject("Cloud");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprites[Mathf.Abs(Hash(i * 2 + 1)) % _sprites.Length];
            sr.sortingOrder = _sortingOrder;

            float depth = Frac(i * 0.6180339f + 0.123f);
            int dir = (Hash(i) & 1) == 0 ? 1 : -1;

            var c = new Cloud
            {
                Tf = go.transform,
                Sr = sr,
                Depth = depth,
                Speed = dir * Mathf.Lerp(_speedRange.x, _speedRange.y, depth),
                BaseY = (Frac(i * 0.2710f + 0.31f) - 0.5f) * _bandHeight,
            };

            float scale = Mathf.Lerp(_scaleRange.x, _scaleRange.y, depth);
            c.Tf.localScale = new Vector3(scale, scale, 1f);
            var col = sr.color;
            col.a = Mathf.Lerp(_alphaRange.x, _alphaRange.y, depth);
            sr.color = col;

            PlaceX(ref c, initialScatter ? Frac(i * 0.391f + 0.05f) : (c.Speed > 0f ? 0f : 1f), i);
            return c;
        }

        private void HalfWidth() => _halfWidth = _cam.orthographicSize * _cam.aspect;

        // t in [0,1] across the spawn span (left edge .. right edge incl. margins).
        private void PlaceX(ref Cloud c, float t, int i)
        {
            HalfWidth();
            float span = (_halfWidth + _edgeMargin) * 2f;
            float camX = _cam.transform.position.x;
            float x = camX - (_halfWidth + _edgeMargin) + t * span;
            float camY = _cam.transform.position.y;
            float y = camY * _verticalParallax + c.BaseY;
            c.Tf.position = new Vector3(x, y, 0f);
        }

        private void Update()
        {
            HalfWidth();
            float camX = _cam.transform.position.x;
            float camY = _cam.transform.position.y;
            float bound = _halfWidth + _edgeMargin;

            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                Vector3 p = c.Tf.position;
                p.x += c.Speed * Time.deltaTime;

                // Wrap to the opposite edge once fully past the view + margin.
                if (c.Speed > 0f && p.x > camX + bound)
                    p.x = camX - bound;
                else if (c.Speed < 0f && p.x < camX - bound)
                    p.x = camX + bound;

                p.y = camY * _verticalParallax + c.BaseY;
                c.Tf.position = p;
            }
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        // Cheap deterministic int hash for stable per-index variety.
        private static int Hash(int x)
        {
            x = (x ^ 61) ^ (x >> 16);
            x += x << 3;
            x ^= x >> 4;
            x *= 0x27d4eb2d;
            x ^= x >> 15;
            return x;
        }
    }
}
