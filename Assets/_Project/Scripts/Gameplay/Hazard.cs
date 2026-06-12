using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Deadly obstacle: touching it sends Icarus back to the start.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class Hazard : MonoBehaviour
    {
        public static event System.Action PlayerHit;

        // Static survives play sessions when domain reload is off — clear explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlayerHit = null;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.TryGetComponent(out IcarusController _))
                PlayerHit?.Invoke();
        }
    }
}
