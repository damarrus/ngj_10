using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Icarus, one-button control: space toggles wings.
    /// Wings open inside a stream = carried along its trajectory.
    /// Wings open outside = parachute (slow descent).
    /// Wings closed = ballistic flight (inertia + gravity), streams ignore him.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class IcarusController : MonoBehaviour
    {
        [SerializeField] private float _closedGravityScale = 0.8f;
        [SerializeField] private float _parachuteGravityScale = 0.3f;
        [SerializeField] private float _parachuteLinearDamping = 1.5f;
        [SerializeField] private float _captureBlend = 6f;   // velocity convergence rate inside a stream
        [SerializeField] private float _centeringGain = 3f;  // pull toward the stream centerline
        [SerializeField] private SpriteRenderer _wingsVisual;

        public bool WingsOpen { get; private set; } = true;
        public StreamPath CurrentStream { get; private set; }
        public Rigidbody2D Body { get; private set; }

        public event System.Action<bool> WingsToggled;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            ApplyWingsVisual();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                SetWings(!WingsOpen);
        }

        private void FixedUpdate()
        {
            UpdateStreamCapture();

            if (CurrentStream != null)
            {
                Body.gravityScale = 0f;
                Body.linearDamping = 0f;
                var sample = CurrentStream.SampleNearest(Body.position);
                Vector2 desired = CurrentStream.FlowVelocity(sample)
                                + (sample.Point - Body.position) * _centeringGain;
                float blend = 1f - Mathf.Exp(-_captureBlend * Time.fixedDeltaTime);
                Body.linearVelocity = Vector2.Lerp(Body.linearVelocity, desired, blend);
            }
            else if (WingsOpen)
            {
                Body.gravityScale = _parachuteGravityScale;
                Body.linearDamping = _parachuteLinearDamping;
            }
            else
            {
                Body.gravityScale = _closedGravityScale;
                Body.linearDamping = 0f;
            }
        }

        /// <summary>Respawn helper: place at a point, kill momentum, open wings.</summary>
        public void ResetAt(Vector2 position)
        {
            Body.position = position;
            Body.linearVelocity = Vector2.zero;
            CurrentStream = null;
            SetWings(true);
        }

        private void UpdateStreamCapture()
        {
            if (!WingsOpen)
            {
                CurrentStream = null;
                return;
            }
            if (CurrentStream != null && !CurrentStream.IsInside(Body.position))
                CurrentStream = null; // flung off the end of an open path
            if (CurrentStream == null)
                CurrentStream = StreamPath.TryCapture(Body.position);
        }

        private void SetWings(bool open)
        {
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
