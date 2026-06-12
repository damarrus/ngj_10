using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A harvestable tree. The player must stand in the left zone and the right zone
    /// (order irrelevant) for a short time each; once both are done the tree drops a
    /// log and disappears. Each zone shows a radial progress ring.
    /// </summary>
    public class Tree : MonoBehaviour
    {
        [SerializeField] private GameObject _logPrefab;
        [SerializeField] private Transform _leftZone;
        [SerializeField] private Transform _rightZone;
        [SerializeField] private Image _leftFill;
        [SerializeField] private Image _rightFill;
        [SerializeField] private float _zoneRadius = 0.6f;
        [SerializeField] private float _holdTime = 1f;

        private PlayerController _player;
        private float _leftProgress;
        private float _rightProgress;
        private bool _harvested;

        private void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            UpdateFills();
        }

        private void Update()
        {
            if (_harvested || _player == null) return;

            Vector2 p = _player.Position;
            float dt = Time.deltaTime;

            if (_leftZone != null && InZone(p, _leftZone.position))
                _leftProgress = Mathf.Min(_holdTime, _leftProgress + dt);
            if (_rightZone != null && InZone(p, _rightZone.position))
                _rightProgress = Mathf.Min(_holdTime, _rightProgress + dt);

            UpdateFills();

            if (_leftProgress >= _holdTime && _rightProgress >= _holdTime)
                Harvest();
        }

        private bool InZone(Vector2 p, Vector2 zone)
            => (p - zone).sqrMagnitude <= _zoneRadius * _zoneRadius;

        private void UpdateFills()
        {
            if (_leftFill != null) _leftFill.fillAmount = _leftProgress / _holdTime;
            if (_rightFill != null) _rightFill.fillAmount = _rightProgress / _holdTime;
        }

        private void Harvest()
        {
            _harvested = true;
            if (_logPrefab != null)
            {
                var log = Instantiate(_logPrefab, transform.parent);
                log.transform.position = transform.position;
            }
            Destroy(gameObject);
        }
    }
}
