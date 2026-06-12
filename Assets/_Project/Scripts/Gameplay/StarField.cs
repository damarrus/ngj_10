using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Twinkling stars over the dark (lower) part of the sky. Brighter near the
    /// bottom where the sky is darkest, fading toward the bright top.
    /// </summary>
    public class StarField : MonoBehaviour
    {
        [SerializeField] private Sprite _sprite;
        [SerializeField] private int _count = 40;
        [SerializeField] private Vector2 _area = new Vector2(40f, 18f);

        private SpriteRenderer[] _stars;
        private float[] _phases;
        private float[] _heightFades;

        private void Awake()
        {
            _stars = new SpriteRenderer[_count];
            _phases = new float[_count];
            _heightFades = new float[_count];
            for (int i = 0; i < _count; i++)
            {
                var go = new GameObject("Star");
                go.transform.SetParent(transform, false);
                float u = Frac(i * 0.6180f + 0.13f);
                float v = Frac(i * 0.3819f + 0.41f);
                go.transform.localPosition = new Vector3((u - 0.5f) * _area.x, (v - 0.5f) * _area.y, 0f);
                float size = Mathf.Lerp(0.05f, 0.1f, Frac(i * 7.7f));
                go.transform.localScale = new Vector3(size, size, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.sortingOrder = -9;
                _stars[i] = sr;
                _phases[i] = i * 1.7f;
                _heightFades[i] = Mathf.Clamp01(1f - v); // dimmer toward the top
            }
        }

        private void Update()
        {
            for (int i = 0; i < _stars.Length; i++)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f + _phases[i]);
                var c = Color.white;
                c.a = (0.18f + 0.4f * twinkle) * _heightFades[i];
                _stars[i].color = c;
            }
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
