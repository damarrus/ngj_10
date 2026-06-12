using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Moves the object back and forth from its start position. For patrolling hazards.</summary>
    public class Oscillator : MonoBehaviour
    {
        [SerializeField] private Vector2 _travel = new Vector2(0f, 3f);
        [SerializeField] private float _period = 3f;

        private Vector3 _origin;

        private void Awake() => _origin = transform.position;

        private void Update()
        {
            float t = Mathf.PingPong(Time.time * 2f / _period, 1f);
            transform.position = _origin + (Vector3)(_travel * Mathf.SmoothStep(0f, 1f, t));
        }
    }
}
