using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Periodically spawns a resource (sheep or tree) at a random field position,
    /// up to a cap. Spawns appear with a scale-in pop and never inside any altar's
    /// circular deliver zone.
    /// </summary>
    public class FieldSpawner : MonoBehaviour
    {
        private enum Kind { Sheep, Tree, Berry }

        [SerializeField] private Kind _kind = Kind.Sheep;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _parent;
        [SerializeField] private int _maxOnField = 5;
        [SerializeField] private float _interval = 4f;
        [SerializeField] private float _edgeMargin = 1f;
        [Tooltip("Extra clearance added around each altar's deliver zone when picking a spawn spot.")]
        [SerializeField] private float _zonePadding = 0.5f;
        [SerializeField] private float _firstDelay = 1.5f;

        private float _cd;
        private Altar[] _altars;

        private void Start()
        {
            if (_camera == null) _camera = Camera.main;
            _altars = FindObjectsByType<Altar>(FindObjectsSortMode.None);
            _cd = _firstDelay;
        }

        private void Update()
        {
            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            _cd = _interval;

            if (_prefab == null || _camera == null) return;
            if (Count() >= _maxOnField) return;

            if (TryFindSpot(out var pos))
            {
                var go = Instantiate(_prefab, _parent != null ? _parent : transform);
                go.transform.position = pos;
            }
        }

        private int Count()
        {
            switch (_kind)
            {
                case Kind.Tree:
                    return FindObjectsByType<Tree>(FindObjectsSortMode.None).Length;
                case Kind.Berry:
                    return CountCarryable(ResourceType.Berry);
                default:
                    return FindObjectsByType<Sheep>(FindObjectsSortMode.None).Length;
            }
        }

        private static int CountCarryable(ResourceType type)
        {
            int n = 0;
            foreach (var c in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
                if (c.Type == type) n++;
            return n;
        }

        private bool TryFindSpot(out Vector2 pos)
        {
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            Vector2 c = _camera.transform.position;

            for (int i = 0; i < 16; i++)
            {
                var p = new Vector2(
                    Random.Range(c.x - halfW + _edgeMargin, c.x + halfW - _edgeMargin),
                    Random.Range(c.y - halfH + _edgeMargin, c.y + halfH - _edgeMargin));
                if (!InsideAnyZone(p)) { pos = p; return true; }
            }
            pos = default;
            return false;   // couldn't find a spot clear of every altar zone this tick
        }

        private bool InsideAnyZone(Vector2 p)
        {
            if (_altars == null) return false;
            foreach (var a in _altars)
            {
                if (a == null) continue;
                float r = a.ZoneRadius + _zonePadding;
                if ((p - a.ZoneCenter).sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
