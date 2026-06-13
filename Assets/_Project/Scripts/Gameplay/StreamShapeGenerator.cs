using UnityEngine;

namespace Ngj10.Gameplay
{
    public enum StreamShape
    {
        Line,
        Arc,
        Circle,
        Ellipse,
        SineWave,
        Zigzag,
        Spiral,
        FigureEight,
        SCurve,
        Hill,
        Valley,
        LoopTheLoop,
        Corkscrew,
        Stairs,
        Star,
        NoisePath,
        RoundedRect,
        Heart,
    }

    /// <summary>
    /// Parametric waypoint generator: pick a shape, tweak 2-4 numbers, the
    /// waypoint children are rebuilt from StreamShapeBuilder. Points are local —
    /// move/rotate the root to place the stream in the level.
    /// </summary>
    [RequireComponent(typeof(StreamPath))]
    public class StreamShapeGenerator : MonoBehaviour
    {
        [SerializeField] private StreamShape _shape = StreamShape.Line;
        [SerializeField] private float _size = 8f;    // main dimension: length / radius / width
        [SerializeField] private float _size2 = 3f;   // secondary: height / amplitude / inner radius
        [SerializeField] private int _count = 3;      // periods / steps / petals / loops
        [SerializeField] private float _turns = 2f;   // spiral turns; for Arc: sweep in degrees
        [SerializeField] private int _seed;           // NoisePath only
        [SerializeField] private bool _reverse;       // flip flow direction
        [SerializeField] private float _scale = 1f;   // uniform multiplier on the whole figure
        [SerializeField] private bool _autoRegenerate = true;

        public bool AutoRegenerate => _autoRegenerate;
        public StreamShape Shape => _shape;

        /// <summary>Set shape inputs from level data; call Generate() afterwards to rebuild waypoints.</summary>
        public void Configure(StreamShape shape, float size, float size2, int count,
            float turns, int seed, bool reverse, float scale = 1f)
        {
            _shape = shape;
            _size = size;
            _size2 = size2;
            _count = count;
            _turns = turns;
            _seed = seed;
            _reverse = reverse;
            _scale = scale;
        }

        public void Generate()
        {
            var points = StreamShapeBuilder.Build(_shape, _size, _size2, _count, _turns, _seed, _scale, out bool loop);
            if (_reverse) points.Reverse();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Waypoint"))
                    DestroyImmediate(child.gameObject);
            }

            for (int i = 0; i < points.Count; i++)
            {
                var wp = new GameObject("Waypoint" + i);
                wp.transform.SetParent(transform, false);
                wp.transform.localPosition = points[i];
            }

            GetComponent<StreamPath>().SetLoop(loop);
        }
    }
}
