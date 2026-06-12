using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A bolt that flies in a fixed direction at constant speed. Blinks the player
    /// red on contact (no damage yet). Self-destructs after its lifetime.
    /// </summary>
    public class Lightning : MonoBehaviour
    {
        [SerializeField] private float _speed = 7f;
        [SerializeField] private float _lifetime = 4f;
        [SerializeField] private float _hitRadius = 0.35f;

        private Vector2 _dir;
        private float _life;

        public void Init(Vector2 startPos, Vector2 dir)
        {
            transform.position = startPos;
            _dir = dir.normalized;
            // orient sprite along travel direction
            float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang - 90f);
        }

        private void Update()
        {
            transform.position += (Vector3)(_dir * _speed * Time.deltaTime);

            var player = FindAnyObjectByType<PlayerController>();
            if (player != null && (player.Position - (Vector2)transform.position).sqrMagnitude <= _hitRadius * _hitRadius)
            {
                player.Blink(0.3f);
                Destroy(gameObject);
                return;
            }

            _life += Time.deltaTime;
            if (_life >= _lifetime) Destroy(gameObject);
        }
    }
}
