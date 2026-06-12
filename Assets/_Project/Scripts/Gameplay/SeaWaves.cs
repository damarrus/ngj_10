using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Animated sine wave lines along the sea surface.</summary>
    public class SeaWaves : MonoBehaviour
    {
        [SerializeField] private float _width = 44f;
        [SerializeField] private int _lineCount = 3;
        [SerializeField] private float _step = 0.6f;

        private static Shader _spriteShader;

        private LineRenderer[] _lines;

        private void Awake()
        {
            if (_spriteShader == null)
                _spriteShader = Shader.Find("Sprites/Default");

            _lines = new LineRenderer[_lineCount];
            int points = Mathf.CeilToInt(_width / _step) + 1;
            for (int i = 0; i < _lineCount; i++)
            {
                var go = new GameObject("Wave" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = points;
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.material = new Material(_spriteShader);
                var c = new Color(0.51f, 0.71f, 0.86f, 0.14f - i * 0.035f);
                lr.startColor = c;
                lr.endColor = c;
                lr.sortingOrder = -3;
                _lines[i] = lr;
            }
        }

        private void Update()
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                int points = line.positionCount;
                for (int k = 0; k < points; k++)
                {
                    float x = -_width * 0.5f + k * _step;
                    float y = -i * 0.28f
                        + Mathf.Sin(x * 1.7f + Time.time * (1.2f + i * 0.3f) + i * 2f) * 0.07f;
                    line.SetPosition(k, new Vector3(x, y, 0f));
                }
            }
        }
    }
}
