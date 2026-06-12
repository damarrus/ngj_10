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

        public bool Loop => _loop;
        public float Speed => _speed;
        public float Width => _width;
        public float Turbulence => _turbulence;
        public float Length { get; private set; }

        /// <summary>Pulsing streams turn off periodically and drop the player.</summary>
        public bool IsActive =>
            _inactiveDuration <= 0f ||
            Time.time % (_activeDuration + _inactiveDuration) < _activeDuration;

        public bool Reversed =>
            _reverseInterval > 0f && Mathf.FloorToInt(Time.time / _reverseInterval) % 2 == 1;

        public float DirectionSign => Reversed ? -1f : 1f;

        /// <summary>Flow velocity at a sampled path point: tangent flow + turbulence wobble.</summary>
        public Vector2 FlowVelocity(PathSample sample)
        {
            Vector2 flow = sample.Tangent * (_speed * DirectionSign);
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
        }

        private Vector2[] _points;

        private static readonly List<StreamPath> All = new List<StreamPath>();

        // Static survives play sessions when domain reload is off — clear explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();

        /// <summary>Stream whose capture zone contains the position, or null.</summary>
        public static StreamPath TryCapture(Vector2 position)
        {
            foreach (var stream in All)
                if (stream.IsActive && stream.SampleNearest(position).DistanceToPath <= stream._width * 0.5f)
                    return stream;
            return null;
        }

        /// <summary>Slightly wider than capture so a carried player is not dropped on the boundary.</summary>
        public bool IsInside(Vector2 position) =>
            IsActive && SampleNearest(position).DistanceToPath <= _width * 0.7f;

        private void OnEnable()
        {
            BuildCache();
            All.Add(this);
        }

        private void OnDisable() => All.Remove(this);

        public PathSample SampleNearest(Vector2 position)
        {
            var best = new PathSample { DistanceToPath = float.MaxValue };
            int segments = SegmentCount();
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = _points[i];
                Vector2 b = _points[(i + 1) % _points.Length];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(position - a, ab) / ab.sqrMagnitude);
                Vector2 point = a + ab * t;
                float dist = Vector2.Distance(position, point);
                if (dist < best.DistanceToPath)
                {
                    best.Point = point;
                    best.Tangent = ab.normalized;
                    best.DistanceToPath = dist;
                }
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

        /// <summary>Shape generator owns the loop flag when it rebuilds waypoints.</summary>
        internal void SetLoopFromGenerator(bool loop) => _loop = loop;

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

        /// <summary>Catmull-Rom subdivision so waypoint corners become smooth curves.</summary>
        private static Vector2[] Smooth(List<Vector2> raw, bool loop)
        {
            const int subdiv = 6;
            if (raw.Count < 3) return raw.ToArray();

            var pts = new List<Vector2>(raw.Count * subdiv);
            int segCount = loop ? raw.Count : raw.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                Vector2 p0 = raw[loop ? (i - 1 + raw.Count) % raw.Count : Mathf.Max(i - 1, 0)];
                Vector2 p1 = raw[i];
                Vector2 p2 = raw[(i + 1) % raw.Count];
                Vector2 p3 = raw[loop ? (i + 2) % raw.Count : Mathf.Min(i + 2, raw.Count - 1)];
                for (int s = 0; s < subdiv; s++)
                    pts.Add(CatmullRom(p0, p1, p2, p3, (float)s / subdiv));
            }
            if (!loop) pts.Add(raw[raw.Count - 1]);
            return pts.ToArray();
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

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
