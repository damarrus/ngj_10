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
        [SerializeField] private float _turbulenceFrequency = 3.7f;
        [SerializeField] private float _turbulencePhasePerWisp = 1.7f;

        private static Shader _spriteShader;

        private StreamPath _path;
        private Transform[] _wisps;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _arrows;
        private float[] _offsets;
        private float _travelled;
        private bool _lastReversed;
        private Color _accent;

        // Master fade applied on top of the per-element alphas: 0 hides the whole
        // visual (menu screen), 1 shows it. The menu→game transition eases it 0→1
        // so the streams appear out of the fade with the rest of the level.
        private float _visibility = 1f;
        public void SetVisibility(float v) => _visibility = Mathf.Clamp01(v);

        // Three wavy longitudinal lines (lanes across the width), animated per frame.
        private static readonly float[] WavyLanes = { -0.45f, 0f, 0.45f };
        private LineRenderer[] _wavyLines;
        private float[] _wavyDistances;

        private void Awake()
        {
            _path = GetComponent<StreamPath>();
        }

        /// <summary>Set the accent color before Start() builds the visuals.</summary>
        public void Configure(Color color) => _color = color;

        private void Start()
        {
            _accent = Color.Lerp(_color, Color.white, 0.5f);
            BuildRibbon();
            SpawnArrows();
            SpawnWisps();
        }

        /// <summary>
        /// Prototype-style flow lines: three thin wavy lines flowing along the path
        /// (animated in Update). The wide capsule capture-zone band is editor-only —
        /// in game we show just direction (arrows) and the flow effects (wisps + lines).
        /// </summary>
        private void BuildRibbon()
        {
            int wavyPoints = Mathf.Max(8, Mathf.CeilToInt(_path.Length / 0.7f)) + 1;
            _wavyDistances = new float[wavyPoints];
            for (int k = 0; k < wavyPoints; k++)
                _wavyDistances[k] = (float)k / (wavyPoints - 1) * _path.Length;

            _wavyLines = new LineRenderer[WavyLanes.Length];
            var buffer = new Vector3[wavyPoints];
            for (int i = 0; i < WavyLanes.Length; i++)
            {
                float alpha = WavyLanes[i] == 0f ? 0.18f : 0.11f;
                _wavyLines[i] = CreateLine("Wavy" + i, 0.06f, RibbonColor(alpha), 1, buffer);
            }
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
            if (_spriteShader == null)
                _spriteShader = Shader.Find("Sprites/Default");
            lr.material = new Material(_spriteShader);
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
            // Pulsing streams dim to a ghost while off. Fold in the master visibility
            // so the menu hide / transition fade scales every element together.
            bool active = _path.IsActive;
            float activeMul = (active ? 1f : 0.15f) * _visibility;
            for (int i = 0; i < _wavyLines.Length; i++)
                SetLineAlpha(_wavyLines[i], (WavyLanes[i] == 0f ? 0.18f : 0.11f) * activeMul);

            AnimateWavyLines();

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
                    pos += (Vector3)(perp * (Mathf.Sin(Time.time * _turbulenceFrequency + i * _turbulencePhasePerWisp) * _path.Turbulence * 0.2f));
                }
                _wisps[i].position = pos;
                _wisps[i].rotation = TangentRotation(sample.Tangent * _path.DirectionSign);

                // Open paths fade wisps at both ends; loops flow seamlessly.
                float alpha = _path.Loop ? 0.55f : 0.55f * Mathf.Clamp01(Mathf.Min(cycle, 1f - cycle) / 0.15f);
                var c = _accent;
                c.a = alpha * activeMul;
                _renderers[i].color = c;
            }

            for (int i = 0; i < _arrows.Length; i++)
            {
                var c = _accent;
                c.a = 0.3f * activeMul;
                _arrows[i].color = c;
            }
        }

        private void AnimateWavyLines()
        {
            for (int i = 0; i < _wavyLines.Length; i++)
            {
                float lane = WavyLanes[i];
                var line = _wavyLines[i];
                for (int k = 0; k < _wavyDistances.Length; k++)
                {
                    float d = _wavyDistances[k];
                    var sample = _path.SampleAtDistance(d);
                    var perp = new Vector2(-sample.Tangent.y, sample.Tangent.x);
                    float offset = lane * _path.Width * 0.36f
                        + Mathf.Sin(d * 0.55f + Time.time * 2f + lane * 5f) * _path.Width * 0.1f;
                    line.SetPosition(k, sample.Point + perp * offset);
                }
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
                go.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _arrowSprite;
                sr.color = new Color(_accent.r, _accent.g, _accent.b, 0.3f);
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
                float size = Mathf.Lerp(0.7f, 1.1f, Frac(t * 11.9f));
                go.transform.localScale = new Vector3(size, size * 0.12f, 1f); // comet streak along flow
            }
        }

        private static Quaternion TangentRotation(Vector2 tangent) =>
            Quaternion.Euler(0f, 0f, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
