using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Deadly obstacle: touching it sends Icarus back to the start.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class Hazard : MonoBehaviour
    {
        public static event System.Action PlayerHit;

        /// <summary>Kill the player — the one entry point other hazards (e.g. burn
        /// cones) raise too, so every deadly source flows through one event.</summary>
        public static void Kill() => PlayerHit?.Invoke();

        // Static survives play sessions when domain reload is off — clear explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlayerHit = null;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.TryGetComponent(out IcarusController _))
                Kill();
        }
    }
}
