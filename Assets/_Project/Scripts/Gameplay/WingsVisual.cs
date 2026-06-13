using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Procedural Icarus look, styled after the web prototype: cream body and
    /// head, a fan of feathers per wing that spreads when wings open and folds
    /// when closed, body tilt with horizontal speed, soft halo inside a stream.
    /// </summary>
    public class WingsVisual : MonoBehaviour
    {
        [SerializeField] private Sprite _sprite;
        [SerializeField] private int _feathersPerWing = 6;
        [SerializeField] private float _spreadRate = 12f;
        [SerializeField] private float _tiltPerSpeed = 2.8f; // degrees per unit of vx

        private static readonly Color FeatherColor = new Color(0.957f, 0.914f, 0.824f, 0.95f);
        private static readonly Color BodyColor = new Color(0.941f, 0.890f, 0.784f);

        private IcarusController _controller;
        private Transform[] _feathers;
        private float[] _sides;
        private float[] _lengths;
        private SpriteRenderer _halo;
        private float _spread;
        private float _haloAlpha;

        private void Awake()
        {
            _controller = GetComponentInParent<IcarusController>();
            Build();
        }

        private void Build()
        {
            _halo = NewSprite("Halo", new Vector3(0f, 0f, 0f), new Vector3(1.4f, 1.4f, 1f),
                new Color(1f, 1f, 1f, 0f), 7);

            // Prototype proportions: slim drop body, small head above, two stick legs.
            NewSprite("Body", Vector3.zero, new Vector3(0.20f, 0.36f, 1f), BodyColor, 10);
            NewSprite("Head", new Vector3(0f, 0.245f, 0f), new Vector3(0.18f, 0.18f, 1f), BodyColor, 11);
            var legColor = new Color(0.851f, 0.784f, 0.651f);
            NewSprite("LegL", new Vector3(-0.05f, -0.26f, 0f), new Vector3(0.035f, 0.16f, 1f), legColor, 9);
            NewSprite("LegR", new Vector3(0.05f, -0.26f, 0f), new Vector3(0.035f, 0.16f, 1f), legColor, 9);

            int total = _feathersPerWing * 2;
            _feathers = new Transform[total];
            _sides = new float[total];
            _lengths = new float[total];
            int index = 0;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int d = 0; d < _feathersPerWing; d++)
                {
                    float length = 0.45f + d * 0.11f;
                    var sr = NewSprite("Feather", Vector3.zero,
                        new Vector3(length, 0.085f, 1f), FeatherColor, 9);
                    _feathers[index] = sr.transform;
                    _sides[index] = s;
                    _lengths[index] = length;
                    index++;
                }
            }
        }

        private SpriteRenderer NewSprite(string name, Vector3 localPos, Vector3 scale, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            bool open = _controller != null && _controller.WingsOpen;
            _spread = Mathf.Lerp(_spread, open ? 1f : 0f, 1f - Mathf.Exp(-_spreadRate * Time.deltaTime));

            float vx = _controller != null && _controller.Body != null
                ? _controller.Body.linearVelocity.x : 0f;
            float tilt = Mathf.Clamp(vx * -_tiltPerSpeed, -32f, 32f);
            transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

            var shoulder = new Vector3(0f, 0.06f, 0f);
            float lengthMul = 0.45f + 0.55f * _spread;
            for (int i = 0; i < _feathers.Length; i++)
            {
                int d = i % _feathersPerWing;
                // Folded: feathers hang behind the body; open: fan from low to high.
                float openAngle = 8f + d * 13f;
                float foldAngle = -72f + d * 5f;
                float angle = Mathf.Lerp(foldAngle, openAngle, _spread);
                float worldAngle = _sides[i] > 0f ? angle : 180f - angle;

                float half = _lengths[i] * lengthMul * 0.5f;
                float rad = worldAngle * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
                _feathers[i].localPosition = shoulder + dir * (half + 0.05f);
                _feathers[i].localRotation = Quaternion.Euler(0f, 0f, worldAngle);
                _feathers[i].localScale = new Vector3(_lengths[i] * lengthMul, 0.085f, 1f);
            }

            bool carried = _controller != null && _controller.CurrentStream != null && _spread > 0.5f;
            _haloAlpha = Mathf.Lerp(_haloAlpha, carried ? 0.14f : 0f, 1f - Mathf.Exp(-8f * Time.deltaTime));
            var hc = _halo.color;
            hc.a = _haloAlpha;
            _halo.color = hc;
        }
    }
}
