using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Scales an object in from zero with a small overshoot when it appears, then
    /// removes itself. Used for sheep and trees popping onto the field.
    /// </summary>
    public class ScalePopIn : MonoBehaviour
    {
        [SerializeField] private float _duration = 0.4f;

        private Vector3 _targetScale;
        private float _t;

        private void Awake()
        {
            _targetScale = transform.localScale;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            _t += Time.deltaTime / _duration;
            float k = Mathf.Clamp01(_t);
            // ease-out-back overshoot
            float s = EaseOutBack(k);
            transform.localScale = _targetScale * s;
            if (k >= 1f)
            {
                transform.localScale = _targetScale;
                Destroy(this);
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float xm1 = x - 1f;
            return 1f + c3 * xm1 * xm1 * xm1 + c1 * xm1 * xm1;
        }
    }
}
