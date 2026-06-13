using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Runtime Zeus node: an anchor that fires bolts into one or more target
    /// areas. Each <see cref="ZeusAreaDef"/> runs on its own clock — a first-strike
    /// delay, then a fixed period between strikes. On a strike a jagged bolt grows
    /// from the anchor toward the area over the area's flight time; the frame it
    /// lands the area's ring flashes, and Icarus standing inside the ellipse has
    /// his wings shocked via <see cref="ShockState"/> while a forked bolt arcs to
    /// him for the flash window. Purely code-driven (no prefab art) — built by
    /// LevelBuilder from a <see cref="ZeusDef"/>, like the Burner.
    /// </summary>
    public class Zeus : MonoBehaviour
    {
        [SerializeField] private Color _boltColor = new Color(1f, 0.95f, 0.35f, 0.9f);
        [SerializeField] private float _boltWidth = 0.12f;
        [SerializeField] private int _pointsPerUnit = 2;   // bolt jaggedness density
        [SerializeField] private float _jitter = 0.25f;    // bolt zigzag amplitude
        [SerializeField] private float _reseedInterval = 0.06f; // how often the bolt redraws

        // The struck bolt lingers on screen briefly after it lands so the hit reads.
        [SerializeField] private float _flashDuration = 0.12f;

        [Header("Area ring (lights up where the bolt lands)")]
        [SerializeField] private Color _ringColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        [SerializeField] private float _ringWidth = 0.08f;
        [SerializeField] private int _ringSegments = 32;

        // Ring fades out over its own (longer) window so the landing lingers and
        // reads smoothly, independent of the short bolt/fork flash.
        [SerializeField] private float _ringFadeDuration = 0.45f;

        [Header("Burst (bolts spraying out from the centre on impact)")]
        [SerializeField] private int _burstCount = 6;       // bolts per strike
        [SerializeField] private float _burstWidth = 0.06f;

        // One live timer + visual per area.
        private struct Area
        {
            public Vector2 Center;
            public float RadiusX, RadiusY;
            public float Period;
            public float FlightTime;
            public float NextStrikeTime; // local-clock time the current bolt lands
            public float LaunchTime;      // local-clock time the current bolt launched
            public LineRenderer Bolt;
            public LineRenderer Ring;     // ellipse outline, flashes on strike
            public LineRenderer Fork;     // extra bolt arcing to Icarus when he's hit
            public LineRenderer[] Bursts; // bolts spraying from centre to the edge
            public Vector2[] BurstEnds;   // frozen edge endpoints, reseeded per strike
            public bool HitThisStrike;    // Icarus was inside on arrival — fork + hold it
            public Vector2 HitPoint;      // where the fork ends (Icarus at arrival)
        }

        private Area[] _areas;
        private ShockState _shock;
        private float _time; // local clock so timers survive a paused start

        private static Material _boltMaterial;

        public void Configure(ZeusDef def)
        {
            BuildAreas(def);
            _shock = FindAnyObjectByType<ShockState>();
        }

        private void BuildAreas(ZeusDef def)
        {
            EnsureMaterial();
            ZeusAreaDef[] defs = def.Areas ?? System.Array.Empty<ZeusAreaDef>();
            _areas = new Area[defs.Length];
            Vector2 anchor = transform.position;

            for (int i = 0; i < defs.Length; i++)
            {
                ZeusAreaDef d = defs[i];
                float flight = Mathf.Max(0f, d.FlightTime);
                _areas[i] = new Area
                {
                    Center = anchor + d.Offset,
                    RadiusX = Mathf.Max(0.01f, d.RadiusX),
                    RadiusY = Mathf.Max(0.01f, d.RadiusY),
                    Period = Mathf.Max(0.05f, d.Period),
                    FlightTime = flight,
                    // First bolt lands StartDelay (+ flight) after start; launch is flight before.
                    NextStrikeTime = Mathf.Max(0f, d.StartDelay) + flight,
                    LaunchTime = Mathf.Max(0f, d.StartDelay),
                    Bolt = NewLine("Bolt" + i, _boltWidth, _boltColor),
                    Fork = NewLine("Fork" + i, _boltWidth, _boltColor),
                    Ring = NewLine("Ring" + i, _ringWidth, _ringColor, loop: true),
                    Bursts = new LineRenderer[Mathf.Max(0, _burstCount)],
                    BurstEnds = new Vector2[Mathf.Max(0, _burstCount)],
                };
                for (int b = 0; b < _areas[i].Bursts.Length; b++)
                    _areas[i].Bursts[b] = NewLine($"Burst{i}_{b}", _burstWidth, _boltColor);
                BuildRing(ref _areas[i]);
            }
        }

        // Bake the ellipse outline into the ring renderer once — it only toggles
        // visibility/colour at runtime, never its shape.
        private void BuildRing(ref Area a)
        {
            a.Ring.positionCount = _ringSegments;
            for (int i = 0; i < _ringSegments; i++)
            {
                float ang = i / (float)_ringSegments * Mathf.PI * 2f;
                a.Ring.SetPosition(i, a.Center
                    + new Vector2(Mathf.Cos(ang) * a.RadiusX, Mathf.Sin(ang) * a.RadiusY));
            }
        }

        private LineRenderer NewLine(string name, float width, Color color, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.material = _boltMaterial;
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 2;
            lr.sortingOrder = 3; // above cones, below the player
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            lr.enabled = false;
            return lr;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            Vector2 anchor = transform.position;
            Vector2? icarus = _shock != null ? (Vector2?)_shock.transform.position : null;

            for (int i = 0; i < _areas.Length; i++)
                TickArea(ref _areas[i], anchor, icarus);
        }

        // One area's lifecycle: grow the bolt during flight, resolve the hit on
        // arrival, hold the flash, then schedule the next strike.
        private void TickArea(ref Area a, Vector2 anchor, Vector2? icarus)
        {
            float landed = _time - a.NextStrikeTime; // >=0 once the bolt has arrived

            if (landed < 0f)
            {
                // In flight: draw the bolt growing from the anchor toward the area.
                float t = a.FlightTime > 0f
                    ? Mathf.Clamp01((_time - a.LaunchTime) / a.FlightTime)
                    : 1f;
                Vector2 tip = Vector2.Lerp(anchor, a.Center, t);
                a.Bolt.enabled = true;
                DrawBolt(a.Bolt, anchor, tip);
                a.Ring.enabled = false;
                a.Fork.enabled = false;
                SetBurstEnabled(ref a, false);
                return;
            }

            // Resolve the strike exactly on the frame it lands.
            if (_time - Time.deltaTime < a.NextStrikeTime)
            {
                a.HitThisStrike = icarus.HasValue && _shock != null && InEllipse(a, icarus.Value);
                if (a.HitThisStrike)
                {
                    a.HitPoint = icarus.Value;
                    _shock.Shock();
                }
                SeedBurst(ref a); // freeze a fresh random spray for this strike
            }

            // Short flash: full bolt to the centre, plus the fork to a struck Icarus.
            bool inFlash = landed < _flashDuration;
            a.Bolt.enabled = inFlash;
            a.Fork.enabled = inFlash && a.HitThisStrike;
            if (inFlash)
            {
                DrawBolt(a.Bolt, anchor, a.Center);
                if (a.HitThisStrike)
                    DrawBolt(a.Fork, a.Center, a.HitPoint);
            }

            // Ring + burst linger on the longer window with an ease-out fade.
            if (landed < _ringFadeDuration)
            {
                float k = 1f - Mathf.Clamp01(landed / _ringFadeDuration);
                float fade = k * k; // quadratic ease-out tail
                a.Ring.enabled = true;
                SetAlpha(a.Ring, _ringColor.a * fade);

                // Bolts spraying from the centre to their frozen endpoints, redrawn
                // each frame so they keep flickering as they fade.
                if (a.Bursts != null)
                {
                    for (int b = 0; b < a.Bursts.Length; b++)
                    {
                        a.Bursts[b].enabled = true;
                        DrawBolt(a.Bursts[b], a.Center, a.BurstEnds[b]);
                        SetAlpha(a.Bursts[b], _boltColor.a * fade);
                    }
                }
                return;
            }

            // Both windows over: hide everything and arm the next strike.
            a.Bolt.enabled = false;
            a.Ring.enabled = false;
            a.Fork.enabled = false;
            SetBurstEnabled(ref a, false);
            a.HitThisStrike = false;
            a.LaunchTime = a.NextStrikeTime + a.Period - a.FlightTime;
            a.NextStrikeTime += a.Period;
        }

        // Random spray: each burst bolt ends at a random angle, somewhere between
        // half-radius and the ellipse edge, frozen for this strike's lifetime.
        private void SeedBurst(ref Area a)
        {
            if (a.Bursts == null) return;
            for (int b = 0; b < a.Bursts.Length; b++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float reach = Random.Range(0.5f, 1f);
                a.BurstEnds[b] = a.Center + new Vector2(
                    Mathf.Cos(ang) * a.RadiusX * reach,
                    Mathf.Sin(ang) * a.RadiusY * reach);
            }
        }

        private static void SetBurstEnabled(ref Area a, bool on)
        {
            if (a.Bursts == null) return;
            for (int b = 0; b < a.Bursts.Length; b++)
                a.Bursts[b].enabled = on;
        }

        private static void SetAlpha(LineRenderer lr, float alpha)
        {
            Color c = lr.startColor;
            c.a = alpha;
            lr.startColor = lr.endColor = c;
        }

        private static bool InEllipse(in Area a, Vector2 p)
        {
            Vector2 d = p - a.Center;
            float nx = d.x / a.RadiusX;
            float ny = d.y / a.RadiusY;
            return nx * nx + ny * ny <= 1f;
        }

        // A jagged two-point bolt from the anchor to the struck point.
        private void DrawBolt(LineRenderer lr, Vector2 from, Vector2 to)
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
