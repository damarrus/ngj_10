using System.Collections.Generic;
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
    /// waypoint children are rebuilt. Points are local — move/rotate the root
    /// to place the stream in the level. Generation runs in the editor
    /// (custom inspector), never at runtime.
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
        [SerializeField] private bool _autoRegenerate = true;

        public bool AutoRegenerate => _autoRegenerate;
        public StreamShape Shape => _shape;

        public void Generate()
        {
            var points = Build(out bool loop);
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

            GetComponent<StreamPath>().SetLoopFromGenerator(loop);
        }

        private List<Vector2> Build(out bool loop)
        {
            loop = false;
            var pts = new List<Vector2>();
            float L = Mathf.Max(0.5f, _size);
            float A = _size2;
            int n = Mathf.Max(1, _count);

            switch (_shape)
            {
                case StreamShape.Line:
                    pts.Add(Vector2.zero);
                    pts.Add(new Vector2(L, 0f));
                    break;

                case StreamShape.Arc: // top-centered circular arc, sweep in degrees (_turns)
                {
                    float sweep = Mathf.Clamp(_turns <= 5f ? _turns * 90f : _turns, 15f, 350f);
                    int steps = Mathf.Max(4, Mathf.CeilToInt(sweep / 15f));
                    for (int i = 0; i <= steps; i++)
                    {
                        float ang = (90f + sweep * 0.5f - sweep * i / steps) * Mathf.Deg2Rad;
                        pts.Add(new Vector2(L * Mathf.Cos(ang), L * Mathf.Sin(ang)));
                    }
                    break;
                }

                case StreamShape.Circle:
                    loop = true;
                    AddRing(pts, 16, t => L);
                    break;

                case StreamShape.Ellipse:
                    loop = true;
                    for (int i = 0; i < 18; i++)
                    {
                        float t = i / 18f * 2f * Mathf.PI;
                        pts.Add(new Vector2(L * Mathf.Cos(t), A * Mathf.Sin(t)));
                    }
                    break;

                case StreamShape.SineWave:
                    Sample(pts, n * 8, t => new Vector2(t * L, A * Mathf.Sin(2f * Mathf.PI * n * t)));
                    break;

                case StreamShape.Zigzag:
                    pts.Add(Vector2.zero);
                    for (int i = 1; i < n * 2; i++)
                        pts.Add(new Vector2(L * i / (n * 2f), (i % 2 == 1) ? A : -A));
                    pts.Add(new Vector2(L, 0f));
                    break;

                case StreamShape.Spiral: // from radius _size2 to radius _size over _turns turns
                {
                    float turns = Mathf.Max(0.5f, _turns);
                    int steps = Mathf.Max(8, Mathf.CeilToInt(turns * 12f));
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = (float)i / steps;
                        float r = Mathf.Lerp(Mathf.Max(0.2f, A), L, t);
                        float ang = turns * 2f * Mathf.PI * t;
                        pts.Add(new Vector2(r * Mathf.Cos(ang), r * Mathf.Sin(ang)));
                    }
                    break;
                }

                case StreamShape.FigureEight:
                    loop = true;
                    for (int i = 0; i < 16; i++)
                    {
                        float t = i / 16f * 2f * Mathf.PI;
                        float denom = 1f + Mathf.Sin(t) * Mathf.Sin(t);
                        pts.Add(new Vector2(L * Mathf.Cos(t) / denom,
                            A * 2f * Mathf.Sin(t) * Mathf.Cos(t) / denom));
                    }
                    break;

                case StreamShape.SCurve:
                    Sample(pts, 12, t => new Vector2(t * L, A * Mathf.SmoothStep(0f, 1f, t)));
                    break;

                case StreamShape.Hill:
                    Sample(pts, 12, t => new Vector2(t * L, A * 4f * t * (1f - t)));
                    break;

                case StreamShape.Valley:
                    Sample(pts, 12, t => new Vector2(t * L, -A * 4f * t * (1f - t)));
                    break;

                case StreamShape.LoopTheLoop: // straight in, full vertical loop, straight out
                {
                    float r = Mathf.Max(0.5f, A);
                    pts.Add(Vector2.zero);
                    pts.Add(new Vector2(L * 0.3f, 0f));
                    var center = new Vector2(L * 0.35f, r);
                    for (int i = 1; i <= 12; i++)
                    {
                        float ang = (-90f + 360f * i / 12f) * Mathf.Deg2Rad;
                        pts.Add(center + new Vector2(r * Mathf.Cos(ang), r * Mathf.Sin(ang)));
                    }
                    pts.Add(new Vector2(L * 0.6f, 0f));
                    pts.Add(new Vector2(L, 0f));
                    break;
                }

                case StreamShape.Corkscrew: // chain of loops advancing right
                {
                    int steps = n * 14;
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = (float)i / steps;
                        float ang = 2f * Mathf.PI * n * t - Mathf.PI * 0.5f;
                        pts.Add(new Vector2(t * L + A * Mathf.Cos(ang), A * (1f + Mathf.Sin(ang))));
                    }
                    break;
                }

                case StreamShape.Stairs:
                {
                    float dx = L / n;
                    float dy = A / n;
                    pts.Add(Vector2.zero);
                    for (int i = 0; i < n; i++)
                    {
                        pts.Add(new Vector2(dx * (i + 1), dy * i));
                        pts.Add(new Vector2(dx * (i + 1), dy * (i + 1)));
                    }
                    break;
                }

                case StreamShape.Star: // rose curve: _count petals
                    loop = true;
                    AddRing(pts, Mathf.Max(24, n * 10), t => L + A * Mathf.Cos(n * t));
                    break;

                case StreamShape.NoisePath:
                    Sample(pts, 24, t => new Vector2(t * L,
                        (Mathf.PerlinNoise(t * n + _seed * 7.77f, 0.5f) - 0.5f) * 2f * A));
                    break;

                case StreamShape.RoundedRect:
                    loop = true;
                    pts.Add(new Vector2(L * 0.5f, 0f));
                    pts.Add(new Vector2(L * 0.5f, A * 0.5f));
                    pts.Add(new Vector2(0f, A * 0.5f));
                    pts.Add(new Vector2(-L * 0.5f, A * 0.5f));
                    pts.Add(new Vector2(-L * 0.5f, 0f));
                    pts.Add(new Vector2(-L * 0.5f, -A * 0.5f));
                    pts.Add(new Vector2(0f, -A * 0.5f));
                    pts.Add(new Vector2(L * 0.5f, -A * 0.5f));
                    break;

                case StreamShape.Heart:
                    loop = true;
                    for (int i = 0; i < 20; i++)
                    {
                        float t = i / 20f * 2f * Mathf.PI;
                        float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                        float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t)
                                - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                        pts.Add(new Vector2(x, y) * (L / 32f));
                    }
                    break;
            }
            return pts;
        }

        private static void Sample(List<Vector2> pts, int steps, System.Func<float, Vector2> f)
        {
            for (int i = 0; i <= steps; i++)
                pts.Add(f((float)i / steps));
        }

        private static void AddRing(List<Vector2> pts, int steps, System.Func<float, float> radius)
        {
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / steps * 2f * Mathf.PI;
                pts.Add(new Vector2(radius(t) * Mathf.Cos(t), radius(t) * Mathf.Sin(t)));
            }
        }
    }
}
