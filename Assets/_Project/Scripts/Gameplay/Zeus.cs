using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Runtime Zeus node: an anchor that periodically electrifies a chosen set of
    /// streams. On a pulse it strikes each target (a jagged bolt from the anchor to
    /// the stream) and runs lightning along the stream's path for the charge window;
    /// Icarus caught inside a charged stream's band has his wings shocked via
    /// <see cref="ShockState"/>. Purely code-driven (no prefab art) — built by
    /// LevelBuilder from a <see cref="ZeusDef"/>, like the Burner.
    /// </summary>
    public class Zeus : MonoBehaviour
    {
        [SerializeField] private Color _boltColor = new Color(1f, 0.95f, 0.35f, 0.9f);
        [SerializeField] private float _boltWidth = 0.12f;
        [SerializeField] private int _pointsPerUnit = 2;   // bolt jaggedness density
        [SerializeField] private float _jitter = 0.25f;    // bolt zigzag amplitude
        [SerializeField] private float _reseedInterval = 0.06f; // how often the bolt redraws

        private float _fireInterval;
        private float _electrifyDuration;
        private float _phaseOffset;

        private StreamPath[] _targets;
        private LineRenderer[] _strikes; // anchor -> stream
        private LineRenderer[] _runs;    // along the stream path

        private ShockState _shock;
        private float _time; // local clock so the pulse survives a paused start

        private static Material _boltMaterial;

        public void Configure(ZeusDef def, StreamPath[] allStreams)
        {
            _fireInterval = Mathf.Max(0.01f, def.FireInterval);
            _electrifyDuration = Mathf.Clamp(def.ElectrifyDuration, 0f, _fireInterval);
            _phaseOffset = def.PhaseOffset;

            ResolveTargets(def, allStreams);
            BuildRenderers();
            _shock = FindAnyObjectByType<ShockState>();
        }

        private void ResolveTargets(ZeusDef def, StreamPath[] allStreams)
        {
            int[] idx = def.TargetStreams ?? System.Array.Empty<int>();
            var list = new System.Collections.Generic.List<StreamPath>(idx.Length);
            foreach (int i in idx)
            {
                if (i >= 0 && i < allStreams.Length && allStreams[i] != null)
                    list.Add(allStreams[i]);
            }
            _targets = list.ToArray();
        }

        private void BuildRenderers()
        {
            EnsureMaterial();
            _strikes = new LineRenderer[_targets.Length];
            _runs = new LineRenderer[_targets.Length];
            for (int i = 0; i < _targets.Length; i++)
            {
                _strikes[i] = NewLine("Strike" + i, _boltWidth);
                _runs[i] = NewLine("Run" + i, _boltWidth * 0.75f);
            }
        }

        private LineRenderer NewLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = _boltMaterial;
            lr.startColor = lr.endColor = _boltColor;
            lr.numCapVertices = 2;
            lr.sortingOrder = 3; // above cones, below the player
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            return lr;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            bool charged = IsCharged();

            Vector2 origin = transform.position;
            Vector2? icarus = _shock != null ? (Vector2?)_shock.transform.position : null;

            for (int i = 0; i < _targets.Length; i++)
            {
                StreamPath stream = _targets[i];
                if (stream == null) { Hide(i); continue; }

                _strikes[i].enabled = charged;
                _runs[i].enabled = charged;
                if (!charged) continue;

                StreamPath.PathSample near = stream.SampleNearest(origin);
                DrawStrike(_strikes[i], origin, near.Point);
                DrawRun(_runs[i], stream);

                if (icarus.HasValue && _shock != null
                    && InBand(stream, icarus.Value))
                {
                    _shock.Shock();
                }
            }
        }

        // Charged for ElectrifyDuration seconds out of each FireInterval cycle.
        private bool IsCharged()
        {
            float t = Mathf.Repeat(_time + _phaseOffset, _fireInterval);
            return t < _electrifyDuration;
        }

        private static bool InBand(StreamPath stream, Vector2 point)
        {
            float half = stream.Width * 0.5f;
            if (half <= 0f) return false;
            return stream.SampleNearest(point).DistanceToPath <= half;
        }

        private void Hide(int i)
        {
            _strikes[i].enabled = false;
            _runs[i].enabled = false;
        }

        // A jagged two-point bolt from the anchor to the struck point.
        private void DrawStrike(LineRenderer lr, Vector2 from, Vector2 to)
        {
            Vector2 dir = to - from;
            float len = dir.magnitude;
            int count = Mathf.Max(2, Mathf.RoundToInt(len * _pointsPerUnit));
            Vector2 perp = len > 0.0001f ? new Vector2(-dir.y, dir.x) / len : Vector2.up;

            lr.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector2 p = from + dir * t;
                if (i != 0 && i != count - 1)
                    p += perp * (RandomJitter(i) * _jitter);
                lr.SetPosition(i, p);
            }
        }

        // Lightning running along the whole stream path, jittered off the axis.
        private void DrawRun(LineRenderer lr, StreamPath stream)
        {
            float len = stream.Length;
            if (len <= 0f) { lr.positionCount = 0; return; }
            int count = Mathf.Clamp(Mathf.RoundToInt(len * _pointsPerUnit), 2, 128);

            lr.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                var s = stream.SampleAtDistance(t * len);
                Vector2 perp = new Vector2(-s.Tangent.y, s.Tangent.x);
                float edge = (i == 0 || i == count - 1) ? 0f : RandomJitter(i) * _jitter;
                lr.SetPosition(i, s.Point + perp * edge);
            }
        }

        // Deterministic-per-frame zigzag: reseeds on the local clock so the bolt
        // flickers without per-frame allocation or Random state churn.
        private float RandomJitter(int i)
        {
            float seed = Mathf.Floor(_time / _reseedInterval);
            float n = Mathf.Sin((i * 12.9898f + seed * 78.233f)) * 43758.5453f;
            return (n - Mathf.Floor(n)) * 2f - 1f; // [-1, 1]
        }

        private static void EnsureMaterial()
        {
            if (_boltMaterial != null) return;
            // Unlit additive-ish sprite shader keeps the bolt bright and merge-safe.
            _boltMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        // Domain-reload-off safety: drop the cached material between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _boltMaterial = null;
    }
}
