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
        private bool _stopped; // frozen on win — the timer holds at the sun-touch value
        private bool _halted;  // run over on death — measuring stops, timer reads 0
        private Vector2 _lastPos;
        private float _spawnY;
        private float _runStartTime;

        /// <summary>Milliseconds elapsed since the run began (first flight). Counts up
        /// while flying, freezes when <see cref="Stop"/> is called (the win), and resets
        /// to 0 with the next run (respawn after death/restart).</summary>
        public int RunMs { get; private set; }

        /// <summary>True once the player has taken flight this run (the timer is live).</summary>
        public bool IsRunning => _running;

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
            // Parked at spawn (respawn after death/restart): no run in progress, and both
            // the win-freeze and the death-halt are cleared so the next flight starts a
            // fresh, live timer.
            if (_icarus.IsWaitingForInput)
            {
                _running = false;
                _stopped = false;
                _halted = false;
                return;
            }

            // Halted on death: the run is over: stop measuring. The timer was already
            // zeroed by ResetTimer so the death fade shows 00:00:000, not a ticking clock.
            if (_halted)
                return;

            // Frozen on win: hold all totals (incl. the timer) at the sun-touch value.
            if (_stopped)
                return;

            if (!_running)
            {
                _running = true;
                PathMeters = 0f;
                MaxHeightMeters = 0f;
                MaxSpeed = 0f;
                TimeToMaxMs = 0;
                RunMs = 0;
                _runStartTime = Time.time;
                _lastPos = _icarus.Body.position;
                _spawnY = _lastPos.y;
            }

            RunMs = Mathf.RoundToInt((Time.time - _runStartTime) * 1000f);

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

        /// <summary>Freeze the run timer at its current value (called the instant the sun
        /// is touched). The next respawn clears the freeze.</summary>
        public void Stop() => _stopped = true;

        /// <summary>Zero the timer and stop measuring (called on death). The clock reads
        /// 00:00:000 through the death fade instead of ticking on. Other run totals keep
        /// their death-moment values for the leaderboard submit; the next respawn resets
        /// everything for a fresh run.</summary>
        public void ResetTimer()
        {
            RunMs = 0;
            _halted = true;
        }

        /// <summary>Format milliseconds as "mm:ss:mmm" (minutes:seconds:milliseconds).</summary>
        public static string FormatMs(int ms)
        {
            if (ms < 0)
                ms = 0;
            int minutes = ms / 60000;
            int seconds = ms / 1000 % 60;
            int millis = ms % 1000;
            return $"{minutes:00}:{seconds:00}:{millis:000}";
        }
    }
}
