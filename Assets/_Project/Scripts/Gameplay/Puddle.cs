using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A static puddle. While the player stands in it, it slows them down. Uses a
    /// trigger collider and applies the slow in the physics step so it stays in sync
    /// with the player's movement.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Puddle : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _slowFactor = 0.45f;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerController>();
            if (player != null) player.ApplySlow(_slowFactor);
        }
    }
}
