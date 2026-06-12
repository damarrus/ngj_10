using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Icarus, one-button control: HOLD space (or mouse) = wings open,
    /// release = wings folded.
    /// Wings open inside a stream = carried along its trajectory.
    /// Wings open outside = parachute (slow descent).
    /// Wings closed = ballistic flight (inertia + gravity), streams ignore him.
    /// After a respawn he hovers in place until the first hold.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class IcarusController : MonoBehaviour
    {
        [SerializeField] private float _closedGravityScale = 0.8f;
        [SerializeField] private float _parachuteGravityScale = 0.3f;
        [SerializeField] private float _parachuteLinearDamping = 1.5f;
        [SerializeField] private SpriteRenderer _wingsVisual;

        public bool WingsOpen { get; private set; } = true;
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
                Body = GetComponent<Rigidbody2D>();
        }

        private bool _waitingForInput;

        private void Update()
        {
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
                Body.gravityScale = 0f;
                return;
            }

            UpdateStreamCapture();

            if (CurrentStream != null)
            {
                Body.gravityScale = 0f;
                Body.linearDamping = 0f;
                // Grip drives both the centering pull and how fast velocity converges
                // to the flow (grip 3 reproduces the old 3/6 defaults).
                var sample = CurrentStream.SampleNearest(Body.position);
                Vector2 desired = CurrentStream.FlowVelocity(sample)
                                + (sample.Point - Body.position) * CurrentStream.Grip;
                float blend = 1f - Mathf.Exp(-CurrentStream.Grip * 2f * Time.fixedDeltaTime);
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

        /// <summary>Respawn helper: place at a point, kill momentum, wait for input.</summary>
        public void ResetAt(Vector2 position)
        {
            EnsureBody();
            Body.position = position;
            Body.linearVelocity = Vector2.zero;
            CurrentStream = null;
            _waitingForInput = true;
            SetWings(false);
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
