using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Pure shape math: turns shape inputs into a list of local waypoints.
    /// Shared by StreamShapeGenerator (spawns waypoint children) and the Map
    /// Editor (draws the path without instantiating anything).
    /// </summary>
    public static class StreamShapeBuilder
    {
        /// <summary>Build local-space points for a stream definition.</summary>
        public static List<Vector2> Build(StreamDef def, out bool loop)
        {
            if (def.IsCircle)
            {
                loop = true;
                return BuildCircle(def.CircleRadius, def.CirclePointCount, def.Reverse);
            }
            if (def.UsesCustomPoints)
            {
                loop = def.CustomLoop;
                var custom = new List<Vector2>(def.CustomPoints);
                if (def.Reverse) custom.Reverse(); // direction follows point order (matches runtime)
                return custom;
            }
            var pts = Build(def.Shape, def.Size, def.Size2, def.Count, def.Turns, def.Seed,
                def.Scale, out loop);
            if (def.Reverse) pts.Reverse();
            return pts;
        }

        /// <summary>Closed ring of `count` points at the given radius, centred on origin.</summary>
        public static List<Vector2> BuildCircle(float radius, int count, bool reverse)
        {
            float r = Mathf.Max(0.5f, radius);
            int n = Mathf.Max(3, count);
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n * 2f * Mathf.PI;
                pts.Add(new Vector2(r * Mathf.Cos(t), r * Mathf.Sin(t)));
            }
            if (reverse) pts.Reverse();
            return pts;
        }

        public static List<Vector2> Build(StreamShape shape, float size, float size2,
            int count, float turns, int seed, float scale, out bool loop)
        {
            loop = false;
            var pts = new List<Vector2>();
            float L = Mathf.Max(0.5f, size);
            float A = size2;
            int n = Mathf.Max(1, count);

            switch (shape)
            {
                case StreamShape.Line:
                    pts.Add(Vector2.zero);
                    pts.Add(new Vector2(L, 0f));
                    break;

                case StreamShape.Arc:
                {
                    float sweep = Mathf.Clamp(turns <= 5f ? turns * 90f : turns, 15f, 350f);
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

                case StreamShape.Spiral:
                {
                    float t = Mathf.Max(0.5f, turns);
                    int steps = Mathf.Max(8, Mathf.CeilToInt(t * 12f));
                    for (int i = 0; i <= steps; i++)
                    {
                        float u = (float)i / steps;
                        float r = Mathf.Lerp(Mathf.Max(0.2f, A), L, u);
                        float ang = t * 2f * Mathf.PI * u;
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

                case StreamShape.LoopTheLoop:
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

                case StreamShape.Corkscrew:
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

                case StreamShape.Star:
                    loop = true;
                    AddRing(pts, Mathf.Max(24, n * 10), t => L + A * Mathf.Cos(n * t));
                    break;

                case StreamShape.NoisePath:
                    Sample(pts, 24, t => new Vector2(t * L,
                        (Mathf.PerlinNoise(t * n + seed * 7.77f, 0.5f) - 0.5f) * 2f * A));
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

            if (scale != 1f)
                for (int i = 0; i < pts.Count; i++)
                    pts[i] *= scale;
            return pts;
        }

        /// <summary>
        /// Catmull-Rom subdivision so waypoint corners become smooth curves. Shared
        /// by the runtime (StreamPath) and the Map Editor preview so both show the
        /// exact same path. Fewer than 3 points pass through unchanged.
        /// </summary>
        public static List<Vector2> Smooth(IReadOnlyList<Vector2> raw, bool loop, int subdiv = 6)
        {
            if (raw.Count < 3)
                return new List<Vector2>(raw);

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
            return pts;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
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
