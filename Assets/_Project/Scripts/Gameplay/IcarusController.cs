using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Icarus, one-button control: HOLD space (or mouse) = wings open,
    /// release = wings folded.
    ///
    /// Physics is a port of the web prototype's "influence field" model:
    /// open wings sample EVERY stream — each pulls toward its flow with a
    /// smoothstep weight that fades from the axis to the band edge, overlapping
    /// streams blend, the strongest one adds a capped centering pull, and
    /// gravity acts everywhere (you sag on weak edges). Closed wings ignore
    /// streams entirely: ballistic dive with a terminal speed.
    /// After a respawn he hovers in place until the first hold.
    /// </summary>
    /// <summary>Which flight physics drives Icarus (runtime-switchable for comparison).</summary>
    public enum FlightModel
    {
        Field,  // prototype port: all streams blend, soft edges, gravity everywhere
        Legacy, // rails: one stream captures fully, no gravity inside, parachute drag
    }

    [RequireComponent(typeof(Rigidbody2D))]
    public class IcarusController : MonoBehaviour
    {
        [Header("Flight model (T toggles in play)")]
        [SerializeField] private FlightModel _flightModel = FlightModel.Legacy;

        public FlightModel Model => _flightModel;

        [Header("Gravity / air (prototype numbers, units)")]
        [SerializeField] private float _gravity = 17.5f;          // u/s², applies in every state
        [SerializeField] private float _openFallTerminal = 2.4f;  // parachute sink speed
        [SerializeField] private float _openDragX = 1.6f;         // horizontal decay, wings open, no stream
        [SerializeField] private float _openMaxSpeed = 12.2f;     // overall cap with wings open
        [SerializeField] private float _closedFallTerminal = 13.6f;
        [SerializeField] private float _closedDragX = 0.25f;

        [Header("Stream field")]
        [SerializeField] private float _centeringMaxSpeed = 3.6f; // cap on the centering pull

        [Header("Legacy model: wings-closed dive")]
        [Tooltip("Downward pull while rising (v.y > 0). Low keeps the stream's launch inertia so he flies out.")]
        [SerializeField] private float _legacyDiveRiseGravity = 7.85f; // 0.8*9.81 = original old
        [Tooltip("Downward pull while falling (v.y < 0). High = snappy plummet.")]
        [SerializeField] private float _legacyDiveFallGravity = 7.85f; // 0.8*9.81 = original old
        [Tooltip("Max fall speed, u/s. 0 = uncapped (original old behaviour).")]
        [SerializeField] private float _legacyDiveTerminal;            // 0 = no cap

        [SerializeField] private SpriteRenderer _wingsVisual;

        public bool WingsOpen { get; private set; } = true;

        /// <summary>Strongest stream currently influencing him (null when free).</summary>
        public StreamPath CurrentStream { get; private set; }

        public Rigidbody2D Body { get; private set; }

        public event System.Action<bool> WingsToggled;

        private void Awake()
        {
            EnsureBody();
            ApplyWingsVisual();
        }

        // GameConfig (exec order -100) can call ResetAt via Begin() before our own
        // Awake runs, so resolve the body lazily instead of trusting Awake order.
        private void EnsureBody()
        {
            if (Body == null)
            {
                Body = GetComponent<Rigidbody2D>();
                Body.gravityScale = 0f; // gravity is integrated manually (prototype model)
            }
        }

        private bool _waitingForInput;

        /// <summary>Parked at spawn until the first hold — the level skips kill checks.</summary>
        public bool IsWaitingForInput => _waitingForInput;

        // Wings forced folded and unresponsive to input while > 0 (Zeus shock).
        private float _wingBlock;

        /// <summary>True while a Zeus shock holds the wings folded.</summary>
        public bool WingsBlocked => _wingBlock > 0f;

        /// <summary>Force wings folded and ignore hold input for the given seconds.
        /// Re-calling refreshes the window. Driven by ShockState.</summary>
        public void BlockWings(float seconds)
        {
            _wingBlock = Mathf.Max(_wingBlock, seconds);
            if (WingsOpen)
                SetWings(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                _flightModel = _flightModel == FlightModel.Field ? FlightModel.Legacy : FlightModel.Field;
                Debug.Log("[Icarus] Flight model: " + _flightModel);
            }

            // Shock holds the wings folded and deaf to input until it expires.
            if (_wingBlock > 0f)
            {
                _wingBlock -= Time.deltaTime;
                if (WingsOpen)
                    SetWings(false);
                return;
            }

            bool held = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
            if (_waitingForInput)
            {
                if (!held) return;
                _waitingForInput = false;
            }
            if (held != WingsOpen)
                SetWings(held);
        }

        private void FixedUpdate()
        {
            if (_waitingForInput)
            {
                // Parked at the spawn point until the player holds the button.
                Body.linearVelocity = Vector2.zero;
                return;
            }

            if (_flightModel == FlightModel.Field)
                FixedUpdateField();
            else
                FixedUpdateLegacy();
        }

        private void FixedUpdateField()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 v = Body.linearVelocity;
            v.y -= _gravity * dt;

            if (WingsOpen)
                v = ApplyStreamField(v, dt);
            else
            {
                CurrentStream = null;
                v.x *= Mathf.Exp(-_closedDragX * dt);
                if (v.y < -_closedFallTerminal)
                    v.y = Mathf.Lerp(v.y, -_closedFallTerminal, 1f - Mathf.Exp(-4f * dt));
            }

            Body.linearVelocity = v;
        }

        // ── Legacy model: hard capture by one stream, rails inside, parachute outside ──

        private void FixedUpdateLegacy()
        {
            float dt = Time.fixedDeltaTime;
            UpdateLegacyCapture();
            Vector2 v = Body.linearVelocity;

            if (CurrentStream != null)
            {
                // Rails: gravity off, velocity converges to flow + centering.
                var sample = CurrentStream.SampleNearest(Body.position);
                Vector2 desired = sample.Tangent
                        * (CurrentStream.SpeedAt(sample.DistanceAlong) * CurrentStream.DirectionSign)
                    + (sample.Point - Body.position) * CurrentStream.Grip;
                if (CurrentStream.Turbulence > 0f)
                {
                    var perp = new Vector2(-sample.Tangent.y, sample.Tangent.x);
                    desired += perp * (CurrentStream.Turbulence * Mathf.Sin(Time.time * 3.7f));
                }
                float blend = 1f - Mathf.Exp(-CurrentStream.CatchRate * 2f * dt);
                v = Vector2.Lerp(v, desired, blend);
            }
            else if (WingsOpen)
            {
                // Parachute: weak gravity, strong drag on both axes.
                v.y -= 0.3f * 9.81f * dt;
                v *= Mathf.Exp(-1.5f * dt);
            }
            else
            {
                // Ballistic dive: light gravity while rising keeps the stream's launch
                // inertia (he flies out), heavier while falling makes the descent snappy.
                float g = v.y > 0f ? _legacyDiveRiseGravity : _legacyDiveFallGravity;
                v.y -= g * dt;
                if (_legacyDiveTerminal > 0f && v.y < -_legacyDiveTerminal)
                    v.y = -_legacyDiveTerminal;
            }

            Body.linearVelocity = v;
        }

        private void UpdateLegacyCapture()
        {
            if (!WingsOpen)
            {
                CurrentStream = null;
                return;
            }

            Vector2 pos = Body.position;

            // Hold the current stream until clearly outside its band, but a stream
            // with strictly higher Z covering the player overrides the held one.
            if (CurrentStream != null)
            {
                var sample = CurrentStream.SampleNearest(pos);
                bool held = CurrentStream.IsActive &&
                    sample.DistanceToPath <= CurrentStream.Width * 0.7f;
                if (held)
                {
                    var stealer = BestCovering(pos, aboveZ: CurrentStream.Z);
                    if (stealer != null) CurrentStream = stealer;
                    return;
                }
                CurrentStream = null;
            }

            // Free: capture the highest-Z stream covering the player.
            CurrentStream = BestCovering(pos, aboveZ: float.NegativeInfinity);
        }

        /// <summary>
        /// Highest-Z stream whose band covers <paramref name="pos"/> and whose Z exceeds
        /// <paramref name="aboveZ"/>; ties on Z break toward the deepest coverage.
        /// Null when nothing qualifies. With all streams at Z 0 this reduces to the old
        /// "deepest stream wins" capture.
        /// </summary>
        private static StreamPath BestCovering(Vector2 pos, float aboveZ)
        {
            StreamPath best = null;
            float bestZ = aboveZ;
            float bestDepth = 0f;
            foreach (var stream in StreamPath.Streams)
            {
                if (!stream.IsActive) continue;
                var sample = stream.SampleNearest(pos);
                float depth = stream.Width * 0.5f - sample.DistanceToPath;
                if (depth <= 0f) continue; // outside the band
                if (stream.Z > bestZ || (best != null && stream.Z == bestZ && depth > bestDepth))
                {
                    bestZ = stream.Z;
                    bestDepth = depth;
                    best = stream;
                }
            }
            return best;
        }

        /// <summary>
        /// The prototype's field blend: weight every stream by smoothstep depth,
        /// sum their flow directions, pull toward the strongest stream's axis,
        /// converge velocity at a rate scaled by the total influence.
        /// </summary>
        private Vector2 ApplyStreamField(Vector2 v, float dt)
        {
            Vector2 sumDir = Vector2.zero;
            float weightSq = 0f;
            float speedAccum = 0f;
            float weightAccum = 0f;
            StreamPath strongest = null;
            float strongestWeight = 0f;
            StreamPath.PathSample strongestSample = default;

            Vector2 pos = Body.position;

            // Z = layer: only the highest-Z streams covering the player carry him;
            // lower layers are ignored where a higher one overlaps. Streams on the
            // same top layer still blend softly below. All-zero Z keeps every stream.
            float topZ = float.NegativeInfinity;
            foreach (var stream in StreamPath.Streams)
            {
                if (!stream.IsActive) continue;
                var sample = stream.SampleNearest(pos);
                float half = stream.Width * 0.5f;
                if (half <= 0f || sample.DistanceToPath >= half) continue;
                if (stream.Z > topZ) topZ = stream.Z;
            }

            foreach (var stream in StreamPath.Streams)
            {
                if (!stream.IsActive) continue;
                var sample = stream.SampleNearest(pos);
                float half = stream.Width * 0.5f;
                if (half <= 0f || sample.DistanceToPath >= half) continue;
                if (stream.Z < topZ) continue; // covered by a higher layer

                float linear = 1f - sample.DistanceToPath / half;
                float weight = linear * linear * (3f - 2f * linear); // smoothstep edge->axis

                sumDir += sample.Tangent * (stream.DirectionSign * weight);
                weightSq += weight * weight;
                speedAccum += stream.SpeedAt(sample.DistanceAlong) * weight;
                weightAccum += weight;

                if (weight > strongestWeight)
                {
                    strongestWeight = weight;
                    strongest = stream;
                    strongestSample = sample;
                }
            }

            float dirMagnitude = sumDir.magnitude;
            float influence = Mathf.Min(dirMagnitude, Mathf.Sqrt(weightSq));

            if (influence > 0.01f && strongest != null)
            {
                // Capped pull toward the strongest stream's axis.
                Vector2 toAxis = strongestSample.Point - pos;
                float distance = toAxis.magnitude;
                Vector2 centering = distance > 0.01f
                    ? toAxis / distance * Mathf.Min(distance * strongest.Grip * 1.67f, _centeringMaxSpeed)
                    : Vector2.zero;

                float flowSpeed = weightAccum > 0f ? speedAccum / weightAccum : 0f;
                Vector2 target = sumDir / dirMagnitude * (flowSpeed * Mathf.Min(influence, 1.3f)) + centering;

                if (strongest.Turbulence > 0f)
                {
                    var perp = new Vector2(-strongestSample.Tangent.y, strongestSample.Tangent.x);
                    target += perp * (strongest.Turbulence * Mathf.Sin(Time.time * 3.7f));
                }

                // CatchRate 3 reproduces the prototype's catch rate of 10.
                float catchRate = strongest.CatchRate * 3.33f;
                float blend = 1f - Mathf.Exp(-catchRate * Mathf.Min(influence, 1f) * dt);
                v = Vector2.Lerp(v, target, blend);

                CurrentStream = influence > 0.08f ? strongest : null;
            }
            else
            {
                // Open wings, no stream: parachute — horizontal drag, slow sink.
                CurrentStream = null;
                v.x *= Mathf.Exp(-_openDragX * dt);
                if (v.y < -_openFallTerminal)
                    v.y = Mathf.Lerp(v.y, -_openFallTerminal, 1f - Mathf.Exp(-5f * dt));
            }

            float speed = v.magnitude;
            if (speed > _openMaxSpeed)
                v *= _openMaxSpeed / speed;
            return v;
        }

        /// <summary>Respawn helper: place at a point, kill momentum, wait for input.</summary>
        public void ResetAt(Vector2 position)
        {
            EnsureBody();
            Body.position = position;
            Body.linearVelocity = Vector2.zero;
            CurrentStream = null;
            _waitingForInput = true;
            _wingBlock = 0f;
            SetWings(false);
            if (TryGetComponent(out BurnState burn))
                burn.ResetHeat();
            if (TryGetComponent(out ShockState shock))
                shock.ResetShock();
        }

        private void SetWings(bool open)
        {
            // Folding wings while carried: the stream's exit boost multiplies the
            // launch velocity — per-stream catapult feel.
            if (!open && WingsOpen && CurrentStream != null)
                Body.linearVelocity *= CurrentStream.ExitBoost;

            WingsOpen = open;
            ApplyWingsVisual();
            WingsToggled?.Invoke(open);
        }

        private void ApplyWingsVisual()
        {
            if (_wingsVisual != null)
                _wingsVisual.enabled = WingsOpen;
        }
    }
}
