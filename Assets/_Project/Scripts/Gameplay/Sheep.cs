using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A sheep that flees from the player: within a detection radius it runs directly
    /// away, accelerating the closer the player gets, so it can never be caught — only
    /// herded. Delivered by pushing it into a requesting altar's zone (see Altar).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Sheep : MonoBehaviour
    {
        [SerializeField] private float _detectRadius = 2f;
        [SerializeField] private float _minSpeed = 3f;      // when player is at the edge of detection
        [SerializeField] private float _maxSpeed = 8f;      // when player is right on top of it
        [SerializeField] private float _wanderSpeed = 0.4f;
        [Tooltip("How far the sheep strays from its home point while idly wandering.")]
        [SerializeField] private float _wanderRadius = 1f;
        [Tooltip("Distance from a screen edge at which the sheep starts steering away from it.")]
        [SerializeField] private float _edgeAvoidDistance = 1.5f;
        [SerializeField] private Camera _camera;

        private Rigidbody2D _rb;
        private PlayerController _player;
        private Vector2 _home;          // wanders around this; updated to wherever it gets pushed
        private Vector2 _wanderTarget;
        private float _wanderTimer;

        public Vector2 Position => _rb != null ? _rb.position : (Vector2)transform.position;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_camera == null) _camera = Camera.main;
        }

        private void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            _home = Position;
            PickWanderTarget();
        }

        private void FixedUpdate()
        {
            bool fleeing = false;
            Vector2 vel = Wander();

            if (_player != null)
            {
                Vector2 away = Position - _player.Position;
                float dist = away.magnitude;
                if (dist < _detectRadius && dist > 0.0001f)
                {
                    float t = 1f - (dist / _detectRadius);          // 0 at edge, 1 when adjacent
                    float speed = Mathf.Lerp(_minSpeed, _maxSpeed, t);

                    // Blend the straight-away direction with a push off the screen edges so
                    // the sheep slides along a wall instead of pinning itself into a corner.
                    Vector2 dir = (away / dist) + EdgeAvoidance();
                    if (dir.sqrMagnitude < 0.0001f) dir = away / dist;   // degenerate fallback
                    vel = dir.normalized * speed;
                    fleeing = true;
                }
            }

            _rb.linearVelocity = vel;
            ClampToView();

            // Wherever the sheep is driven (fled or shoved by the player) becomes its
            // new home, so it then wanders around that fresh spot.
            if (fleeing) _home = Position;
        }

        private Vector2 Wander()
        {
            _wanderTimer -= Time.fixedDeltaTime;
            if (_wanderTimer <= 0f) PickWanderTarget();

            Vector2 toTarget = _wanderTarget - Position;
            if (toTarget.sqrMagnitude < 0.01f) return Vector2.zero;
            return toTarget.normalized * _wanderSpeed;
        }

        private void PickWanderTarget()
        {
            _wanderTarget = _home + Random.insideUnitCircle * _wanderRadius;
            _wanderTimer = Random.Range(1.5f, 3.5f);
        }

        /// <summary>
        /// Inward steering that ramps up from 0 to 1 per axis as the sheep gets within
        /// <see cref="_edgeAvoidDistance"/> of a screen edge. Added to the flee direction so
        /// it veers parallel to the wall rather than running straight into it.
        /// </summary>
        private Vector2 EdgeAvoidance()
        {
            if (_camera == null || !_camera.orthographic || _edgeAvoidDistance <= 0f)
                return Vector2.zero;

            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            Vector2 c = _camera.transform.position;
            Vector2 p = _rb.position;

            float left = (p.x - (c.x - halfW));
            float right = ((c.x + halfW) - p.x);
            float bottom = (p.y - (c.y - halfH));
            float top = ((c.y + halfH) - p.y);

            float d = _edgeAvoidDistance;
            Vector2 push = Vector2.zero;
            push.x += Mathf.Clamp01(1f - left / d);     // near left edge → push right
            push.x -= Mathf.Clamp01(1f - right / d);    // near right edge → push left
            push.y += Mathf.Clamp01(1f - bottom / d);   // near bottom → push up
            push.y -= Mathf.Clamp01(1f - top / d);      // near top → push down
            return push;
        }

        private void ClampToView()
        {
            if (_camera == null || !_camera.orthographic) return;
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            Vector2 c = _camera.transform.position;
            float m = 0.3f;
            Vector2 p = _rb.position;
            p.x = Mathf.Clamp(p.x, c.x - halfW + m, c.x + halfW - m);
            p.y = Mathf.Clamp(p.y, c.y - halfH + m, c.y + halfH - m);
            _rb.position = p;
        }
    }
}
