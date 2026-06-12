using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Destroys its GameObject after a delay. Used for one-shot burst effects.</summary>
    public class TimedDestroy : MonoBehaviour
    {
        private float _t;

        public static void Attach(GameObject go, float seconds)
        {
            if (go == null) return;
            var td = go.AddComponent<TimedDestroy>();
            td._t = seconds;
        }

        private void Update()
        {
            _t -= Time.deltaTime;
            if (_t <= 0f) Destroy(gameObject);
        }
    }
}
