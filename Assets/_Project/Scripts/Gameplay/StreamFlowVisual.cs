using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Visualizes a StreamPath: static arrowheads show direction, moving wisps
    /// show flow and speed. Spawned at runtime, no per-frame allocations.
    /// </summary>
    [RequireComponent(typeof(StreamPath))]
    public class StreamFlowVisual : MonoBehaviour
    {
        [SerializeField] private Sprite _wispSprite;
        [SerializeField] private Sprite _arrowSprite;
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private float _wispsPerUnit = 0.6f;
        [SerializeField] private float _arrowSpacing = 2.5f;

        private StreamPath _path;
        private Transform[] _wisps;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _arrows;
        private float[] _offsets;
        private float _travelled;
        private bool _lastReversed;
        private bool _lastActive = true;
        private Color _accent;
        private LineRenderer _ribbonGlow;
        private LineRenderer _ribbonCore;

        private void Awake()
        {
            _path = GetComponent<StreamPath>();
        }

        private void Start()
        {
            _accent = Color.Lerp(_color, Color.white, 0.5f);
            BuildRibbon();
            SpawnArrows();
            SpawnWisps();
        }

        /// <summary>Continuous glowing ribbon along the path: wide soft glow + bright core.</summary>
        private void BuildRibbon()
        {
            int count = Mathf.Max(8, Mathf.CeilToInt(_path.Length / 0.4f)) + 1;
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float d = (float)i / (count - 1) * _path.Length;
                positions[i] = _path.SampleAtDistance(_path.Loop ? d % _path.Length : d).Point;
            }
            _ribbonGlow = CreateLine("RibbonGlow", _path.Width * 0.55f, RibbonColor(0.10f), 0, positions);
            _ribbonCore = CreateLine("RibbonCore", 0.3f, RibbonColor(0.5f), 1, positions);
        }

        private Color RibbonColor(float alpha)
        {
            var c = Color.Lerp(_color, Color.white, 0.35f);
            c.a = alpha;
            return c;
        }

        private LineRenderer CreateLine(string name, float width, Color color, int order, Vector3[] positions)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = positions.Length;
            lr.SetPositions(positions);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.sortingOrder = order;
            lr.loop = _path.Loop;
            return lr;
        }

        private void Update()
        {
            // Pulsing streams dim to a ghost while off.
            bool active = _path.IsActive;
            float activeMul = active ? 1f : 0.15f;
            if (active != _lastActive)
            {
                _lastActive = active;
                SetLineAlpha(_ribbonGlow, 0.10f * activeMul);
                SetLineAlpha(_ribbonCore, 0.5f * activeMul);
            }

            // Accumulate so reversible streams animate backward without jumps.
            _travelled += _path.Speed * _path.DirectionSign * Time.deltaTime;

            bool reversed = _path.Reversed;
            if (reversed != _lastReversed)
            {
                _lastReversed = reversed;
                foreach (var arrow in _arrows)
                    arrow.transform.Rotate(0f, 0f, 180f);
            }

            for (int i = 0; i < _wisps.Length; i++)
            {
                float distance = Mathf.Repeat(_offsets[i] + _travelled, _path.Length);
                float cycle = distance / _path.Length;
                var sample = _path.SampleAtDistance(distance);

                Vector3 pos = sample.Point;
                if (_path.Turbulence > 0f)
                {
                    var perp = new Vector2(-sample.Tangent.y, sample.Tangent.x);
                    pos += (Vector3)(perp * (Mathf.Sin(Time.time * 3.7f + i * 1.7f) * _path.Turbulence * 0.2f));
                }
                _wisps[i].position = pos;
                _wisps[i].rotation = TangentRotation(sample.Tangent * _path.DirectionSign);

                // Open paths fade wisps at both ends; loops flow seamlessly.
                float alpha = _path.Loop ? 0.8f : 0.8f * Mathf.Clamp01(Mathf.Min(cycle, 1f - cycle) / 0.15f);
                var c = _accent;
                c.a = alpha * activeMul;
                _renderers[i].color = c;
            }

            for (int i = 0; i < _arrows.Length; i++)
            {
                var c = _accent;
                c.a = 0.5f * activeMul;
                _arrows[i].color = c;
            }
        }

        private static void SetLineAlpha(LineRenderer line, float alpha)
        {
            var c = line.startColor;
            c.a = alpha;
            line.startColor = c;
            line.endColor = c;
        }

        private void SpawnArrows()
        {
            int count = Mathf.Max(2, Mathf.RoundToInt(_path.Length / _arrowSpacing));
            _arrows = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                float distance = (i + 0.5f) / count * _path.Length;
                var sample = _path.SampleAtDistance(distance);
                var go = new GameObject("Arrow");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(sample.Point, TangentRotation(sample.Tangent));
                go.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _arrowSprite;
                sr.color = new Color(_accent.r, _accent.g, _accent.b, 0.5f);
                sr.sortingOrder = 3;
                _arrows[i] = sr;
            }
        }

        private void SpawnWisps()
        {
            int count = Mathf.Max(3, Mathf.RoundToInt(_path.Length * _wispsPerUnit));
            _wisps = new Transform[count];
            _renderers = new SpriteRenderer[count];
            _offsets = new float[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Wisp");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _wispSprite;
                sr.sortingOrder = 4;
                _wisps[i] = go.transform;
                _renderers[i] = sr;

                // Deterministic spread from the index.
                float t = (i + 0.5f) / count;
                _offsets[i] = Frac(t * 7.13f + 0.37f) * _path.Length;
                float size = Mathf.Lerp(0.4f, 0.75f, Frac(t * 11.9f));
                go.transform.localScale = new Vector3(size, size * 0.3f, 1f); // dash stretched along flow
            }
        }

        private static Quaternion TangentRotation(Vector2 tangent) =>
            Quaternion.Euler(0f, 0f, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
