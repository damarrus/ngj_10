using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Air stream as a polyline trajectory built from child transforms
    /// (waypoints). Carries Icarus along the path while his wings are open.
    /// Closed loops circulate forever; open paths fling the player off the end.
    /// </summary>
    public class StreamPath : MonoBehaviour
    {
        [SerializeField] private bool _loop;
        [SerializeField] private float _speed = 5f;
        [SerializeField] private float _width = 3f;
        [SerializeField] private float _activeDuration;   // pulse: seconds on (0 = always on)
        [SerializeField] private float _inactiveDuration; // pulse: seconds off
        [SerializeField] private float _reverseInterval;  // flips flow direction every N seconds (0 = never)
        [SerializeField] private float _turbulence;       // perpendicular wobble amplitude
        [SerializeField] private float _grip = 3f;        // hold strength: centering pull + velocity convergence
        [SerializeField] private float _speedEnd;         // linear ramp target at the path end (0 = constant)
        [SerializeField] private float _exitBoost = 1f;   // velocity multiplier on wings-fold exit
        [SerializeField] private float _z;                // Legacy-model capture priority (higher wins)

        public bool Loop => _loop;
        public float Speed => _speed;
        public float Width => _width;
        public float Turbulence => _turbulence;
        public float Grip => _grip;
        public float ExitBoost => _exitBoost;
        public float Z => _z;
        public float Length { get; private set; }

        /// <summary>Pulsing streams turn off periodically and drop the player.</summary>
        public bool IsActive =>
            _inactiveDuration <= 0f ||
            Time.time % (_activeDuration + _inactiveDuration) < _activeDuration;

        public bool Reversed =>
            _reverseInterval > 0f && Mathf.FloorToInt(Time.time / _reverseInterval) % 2 == 1;

        public float DirectionSign => Reversed ? -1f : 1f;

        /// <summary>Flow speed at a distance along the path (linear start→end ramp).</summary>
        public float SpeedAt(float distance)
        {
            if (_speedEnd <= 0f || Length <= 0f)
                return _speed;
            return Mathf.Lerp(_speed, _speedEnd, Mathf.Clamp01(distance / Length));
        }

        /// <summary>Flow velocity at a sampled path point: tangent flow + turbulence wobble.</summary>
        public Vector2 FlowVelocity(PathSample sample)
        {
            Vector2 flow = sample.Tangent * (SpeedAt(sample.DistanceAlong) * DirectionSign);
            if (_turbulence > 0f)
                flow += new Vector2(-sample.Tangent.y, sample.Tangent.x)
                      * (_turbulence * Mathf.Sin(Time.time * 3.7f));
            return flow;
        }

        public struct PathSample
        {
            public Vector2 Point;
            public Vector2 Tangent;
            public float DistanceToPath;
            public float DistanceAlong;
        }

        private Vector2[] _points;

        private static readonly List<StreamPath> All = new List<StreamPath>();

        // Static survives play sessions when domain reload is off — clear explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();

        /// <summary>All enabled streams — the controller samples the whole field each step.</summary>
        public static System.Collections.Generic.IReadOnlyList<StreamPath> Streams => All;

        private void OnEnable()
        {
            BuildCache();
            All.Add(this);
        }

        /// <summary>
        /// Rebuild the cached polyline from the current waypoint children. The
        /// LevelBuilder spawns the stream prefab first (OnEnable caches an empty
        /// path) and adds the waypoints afterwards, so it must call this once the
        /// children exist — otherwise the path stays empty and a respawn drops
        /// the player into the void.
        /// </summary>
        public void RebuildPath() => BuildCache();

        private void OnDisable() => All.Remove(this);

        public PathSample SampleNearest(Vector2 position)
        {
            var best = new PathSample { DistanceToPath = float.MaxValue };
            int segments = SegmentCount();
            float walked = 0f;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = _points[i];
                Vector2 b = _points[(i + 1) % _points.Length];
                Vector2 ab = b - a;
                float segLen = ab.magnitude;
                float t = Mathf.Clamp01(Vector2.Dot(position - a, ab) / ab.sqrMagnitude);
                Vector2 point = a + ab * t;
                float dist = Vector2.Distance(position, point);
                if (dist < best.DistanceToPath)
                {
                    best.Point = point;
                    best.Tangent = ab.normalized;
                    best.DistanceToPath = dist;
                    best.DistanceAlong = walked + segLen * t;
                }
                walked += segLen;
            }
            return best;
        }

        public PathSample SampleAtDistance(float distance)
        {
            if (_points == null) BuildCache();
            distance = _loop
                ? Mathf.Repeat(distance, Length)
                : Mathf.Clamp(distance, 0f, Length);

            int segments = SegmentCount();
            float walked = 0f;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = _points[i];
                Vector2 b = _points[(i + 1) % _points.Length];
                float segLen = Vector2.Distance(a, b);
                if (walked + segLen >= distance && segLen > 0f)
                {
                    float t = (distance - walked) / segLen;
                    return new PathSample { Point = Vector2.Lerp(a, b, t), Tangent = (b - a).normalized };
                }
                walked += segLen;
            }
            Vector2 lastA = _points[segments - 1];
            Vector2 lastB = _points[segments % _points.Length];
            return new PathSample { Point = lastB, Tangent = (lastB - lastA).normalized };
        }

        private int SegmentCount() => _loop ? _points.Length : _points.Length - 1;

        /// <summary>Whoever rebuilds the waypoints (generator or custom path) sets the loop flag.</summary>
        internal void SetLoop(bool loop) => _loop = loop;

        /// <summary>Apply runtime parameters from level data (loop is set by the shape generator).</summary>
        public void Configure(float speed, float width, float activeDuration,
            float inactiveDuration, float reverseInterval, float turbulence, float grip = 3f,
            float speedEnd = 0f, float exitBoost = 1f, float z = 0f)
        {
            _speed = speed;
            _width = width;
            _activeDuration = activeDuration;
            _inactiveDuration = inactiveDuration;
            _reverseInterval = reverseInterval;
            _turbulence = turbulence;
            _grip = grip;
            _speedEnd = speedEnd;
            _exitBoost = exitBoost;
            _z = z;
        }

        private void BuildCache()
        {
            int count = transform.childCount;
            var raw = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Waypoint"))
                    raw.Add(child.position);
            }
            _points = Smooth(raw, _loop);

            Length = 0f;
            int segments = SegmentCount();
            for (int i = 0; i < segments; i++)
                Length += Vector2.Distance(_points[i], _points[(i + 1) % _points.Length]);
        }

        /// <summary>Catmull-Rom subdivision (shared with the Map Editor preview).</summary>
        private static Vector2[] Smooth(List<Vector2> raw, bool loop)
            => StreamShapeBuilder.Smooth(raw, loop).ToArray();

        private void OnDrawGizmos()
        {
            BuildCache();
            if (_points == null || _points.Length < 2) return;
            Gizmos.color = Color.cyan;
            int segments = SegmentCount();
            for (int i = 0; i < segments; i++)
                Gizmos.DrawLine(_points[i], _points[(i + 1) % _points.Length]);
        }
    }
}
