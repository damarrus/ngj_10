using System;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Tuning for every stream's flow visuals, authored once on GameConfig and
    /// pushed onto each StreamFlowVisual before it builds. Defaults reproduce the
    /// historical look so an unconfigured visual behaves as before.
    /// </summary>
    [Serializable]
    public struct FlowVisualSettings
    {
        public bool ShowArrows;
        public bool ShowLines;

        [Tooltip("Particle size, same for every stream regardless of its width.")]
        public float WispScale;
        [Tooltip("Override the per-stream particle colour with WispColor below.")]
        public bool OverrideWispColor;
        public Color WispColor;
        [Tooltip("Lateral band the particles spread across (world units). Independent of stream width — set it wider than the stream for a broad spray.")]
        public float WispSpread;
        [Tooltip("Particle density: how many particles per unit of path length. Higher = more, denser.")]
        public float WispsPerUnit;

        public static FlowVisualSettings Default => new FlowVisualSettings
        {
            ShowArrows = true,
            ShowLines = true,
            WispScale = 1f,
            OverrideWispColor = false,
            WispColor = Color.white,
            WispSpread = 0f,
            WispsPerUnit = 0.6f,
        };
    }

    /// <summary>
    /// Visualizes a StreamPath: static arrowheads show direction, moving wisps
    /// show flow and speed. Spawned at runtime, no per-frame allocations.
    /// </summary>
    [RequireComponent(typeof(StreamPath))]
    public class StreamFlowVisual : MonoBehaviour
    {
        [Tooltip("Particle sprites. One is picked per wisp (deterministic random from its index). Leave one for a single look, add several to vary.")]
        [SerializeField] private Sprite[] _wispSprites;
        [SerializeField] private Sprite _arrowSprite;
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private float _arrowSpacing = 2.5f;
        [Tooltip("Degrees added to a wisp's facing so its art lines up with the flow direction. 0 if the sprite points along +X; set to 90/-90 if it points up/down, etc.")]
        [SerializeField] private float _wispAngleOffset = 0f;
        [SerializeField] private float _turbulenceFrequency = 3.7f;
        [SerializeField] private float _turbulencePhasePerWisp = 1.7f;

        private FlowVisualSettings _settings = FlowVisualSettings.Default;

        private static Shader _spriteShader;

        private StreamPath _path;
        private Transform[] _wisps;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _arrows;
        private float[] _offsets;
        private float[] _lateralOffsets;
        private float _travelled;
        private bool _lastReversed;
        private Color _accent;

        // Master fade applied on top of the per-element alphas: 0 hides the whole
        // visual (menu screen), 1 shows it. The menu→game transition eases it 0→1
        // so the streams appear out of the fade with the rest of the level.
        private float _visibility = 1f;
        public void SetVisibility(float v) => _visibility = Mathf.Clamp01(v);

        // When the title screen freezes the level (timeScale 0) but the flows are kept
        // visible, drive the animation on unscaled time so they still flow. Reset to
        // scaled time on GoLive so they freeze with the level (win / future pause).
        private bool _useUnscaledTime;
        public void SetUseUnscaledTime(bool on) => _useUnscaledTime = on;
        private float DeltaTime => _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        private float TimeNow => _useUnscaledTime ? Time.unscaledTime : Time.time;

        // Three wavy longitudinal lines (lanes across the width), animated per frame.
        private static readonly float[] WavyLanes = { -0.45f, 0f, 0.45f };
        private LineRenderer[] _wavyLines;
        private float[] _wavyDistances;

        private void Awake()
        {
            _path = GetComponent<StreamPath>();
        }

        /// <summary>Set the accent color and flow tuning before Start() builds the visuals.</summary>
        public void Configure(Color color, FlowVisualSettings settings)
        {
            _color = color;
            _settings = settings;
        }

        private void Start()
        {
            _accent = Color.Lerp(_color, Color.white, 0.5f);
            if (_settings.ShowLines)
                BuildRibbon();
            if (_settings.ShowArrows)
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
            if (_wavyLines != null)
            {
                for (int i = 0; i < _wavyLines.Length; i++)
                    SetLineAlpha(_wavyLines[i], (WavyLanes[i] == 0f ? 0.18f : 0.11f) * activeMul);
                AnimateWavyLines();
            }

            // Accumulate so reversible streams animate backward without jumps.
            _travelled += _path.Speed * _path.DirectionSign * DeltaTime;

            bool reversed = _path.Reversed;
            if (reversed != _lastReversed)
            {
                _lastReversed = reversed;
                if (_arrows != null)
                    foreach (var arrow in _arrows)
                        arrow.transform.Rotate(0f, 0f, 180f);
            }

            Color wispBase = _settings.OverrideWispColor ? _settings.WispColor : _accent;
            for (int i = 0; i < _wisps.Length; i++)
            {
                float distance = Mathf.Repeat(_offsets[i] + _travelled, _path.Length);
                float cycle = distance / _path.Length;
                var sample = _path.SampleAtDistance(distance);
                var perp = new Vector2(-sample.Tangent.y, sample.Tangent.x);

                // Spread the stream across a fixed band (settings, not stream width),
                // plus the usual turbulence wobble on top.
                float lateral = _lateralOffsets[i];
                if (_path.Turbulence > 0f)
                    lateral += Mathf.Sin(TimeNow * _turbulenceFrequency + i * _turbulencePhasePerWisp) * _path.Turbulence * 0.2f;

                _wisps[i].position = (Vector3)(sample.Point + perp * lateral);
                _wisps[i].rotation = TangentRotation(sample.Tangent * _path.DirectionSign)
                    * Quaternion.Euler(0f, 0f, _wispAngleOffset);

                // Open paths fade wisps at both ends; loops flow seamlessly.
                float alpha = _path.Loop ? 0.55f : 0.55f * Mathf.Clamp01(Mathf.Min(cycle, 1f - cycle) / 0.15f);
                var c = wispBase;
                c.a = alpha * activeMul;
                _renderers[i].color = c;
            }

            if (_arrows != null)
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
                        + Mathf.Sin(d * 0.55f + TimeNow * 2f + lane * 5f) * _path.Width * 0.1f;
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
            int count = Mathf.Max(3, Mathf.RoundToInt(_path.Length * _settings.WispsPerUnit));
            _wisps = new Transform[count];
            _renderers = new SpriteRenderer[count];
            _offsets = new float[count];
            _lateralOffsets = new float[count];

            // The stream transform carries a per-stream Scale (def.Scale) that would
            // otherwise leak into the parented wisps, making them bigger on scaled-up
            // streams. Divide it out so every wisp ends up the same world size.
            Vector3 parentScale = transform.lossyScale;
            float size = _settings.WispScale; // multiplies the sprite's native size, same for every stream
            float sx = size / Mathf.Max(0.0001f, parentScale.x);
            float sy = size / Mathf.Max(0.0001f, parentScale.y);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Wisp");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PickWispSprite(i, count);
                sr.sortingOrder = 4;
                _wisps[i] = go.transform;
                _renderers[i] = sr;

                // Deterministic spread from the index.
                float t = (i + 0.5f) / count;
                _offsets[i] = Frac(t * 7.13f + 0.37f) * _path.Length;
                // Lateral position across a fixed band [-spread/2, +spread/2], from settings.
                _lateralOffsets[i] = (Frac(t * 5.27f + 0.11f) - 0.5f) * _settings.WispSpread;
                go.transform.localScale = new Vector3(sx, sy, 1f); // comet streak along flow
            }
        }

        // Deterministic per-wisp sprite pick — varies the look without per-frame randomness.
        private Sprite PickWispSprite(int index, int count)
        {
            if (_wispSprites == null || _wispSprites.Length == 0)
                return null;
            int pick = Mathf.FloorToInt(Frac((index + 0.5f) / count * 13.37f + 0.21f) * _wispSprites.Length);
            return _wispSprites[Mathf.Clamp(pick, 0, _wispSprites.Length - 1)];
        }

        private static Quaternion TangentRotation(Vector2 tangent) =>
            Quaternion.Euler(0f, 0f, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
