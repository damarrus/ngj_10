using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Per-run flight stats for Icarus, in metres (1 world unit = 1 m). Accumulates
    /// path length (total distance travelled) and tracks the peak height above the
    /// spawn point while flying. A run is the span between two spawns: it starts when
    /// Icarus leaves the parked state and ends on the next respawn/death.
    ///
    /// Drives no game logic itself — it just measures and raises events. The
    /// <see cref="AchievementReporter"/> listens and turns these into achievement
    /// reports, keeping the engine and the controller free of achievement knowledge.
    /// </summary>
    [RequireComponent(typeof(IcarusController))]
    public class RunStats : MonoBehaviour
    {
        private IcarusController _icarus;

        private bool _running;
        private Vector2 _lastPos;
        private float _spawnY;
        private float _runStartTime;

        /// <summary>Distance travelled this run so far, in metres.</summary>
        public float PathMeters { get; private set; }

        /// <summary>Peak height above the spawn point this run so far, in metres.</summary>
        public float MaxHeightMeters { get; private set; }

        /// <summary>Peak speed this run so far, in metres/second.</summary>
        public float MaxSpeed { get; private set; }

        /// <summary>Milliseconds from the start of the run (first flight) to the moment
        /// the current <see cref="MaxHeightMeters"/> peak was set. Used by the
        /// leaderboard as the tie-break: at equal height, the faster climb wins.</summary>
        public int TimeToMaxMs { get; private set; }

        /// <summary>Raised live as the running totals grow (path, maxHeight). Lets
        /// listeners report progress mid-flight so an achievement can pop the instant
        /// its threshold is crossed, not only at the end of the run.</summary>
        public event System.Action<float, float> Updated;

        private void Awake() => _icarus = GetComponent<IcarusController>();

        private void FixedUpdate()
        {
            // Parked at spawn: no run in progress. The first frame after the player
            // takes flight begins a fresh run with zeroed totals.
            if (_icarus.IsWaitingForInput)
            {
                _running = false;
                return;
            }

            if (!_running)
            {
                _running = true;
                PathMeters = 0f;
                MaxHeightMeters = 0f;
                MaxSpeed = 0f;
                TimeToMaxMs = 0;
                _runStartTime = Time.time;
                _lastPos = _icarus.Body.position;
                _spawnY = _lastPos.y;
            }

            Vector2 pos = _icarus.Body.position;
            PathMeters += Vector2.Distance(pos, _lastPos);
            _lastPos = pos;

            float height = pos.y - _spawnY;
            if (height > MaxHeightMeters)
            {
                MaxHeightMeters = height;
                TimeToMaxMs = Mathf.RoundToInt((Time.time - _runStartTime) * 1000f);
            }

            float speed = _icarus.Body.linearVelocity.magnitude;
            if (speed > MaxSpeed)
                MaxSpeed = speed;

            Updated?.Invoke(PathMeters, MaxHeightMeters);
        }
    }
}
