using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A pickup-able resource on the field (a sheep or a log). The player can pick it
    /// up with E when carrying nothing; it is then offered to an altar that wants it.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Carryable : MonoBehaviour, IInteractable
    {
        [SerializeField] private ResourceType _type = ResourceType.Sheep;
        [Tooltip("Sprite tint applied while the player is close enough to pick this up.")]
        [SerializeField] private Color _highlightColor = new Color(1f, 0.95f, 0.5f, 1f);

        private SpriteRenderer _sr;
        private Color _baseColor;
        private bool _highlighted;

        public ResourceType Type => _type;

        public Vector2 Position => transform.position;

        public bool CanInteract(PlayerController player) => player.CarriedItem == null;

        public void Interact(PlayerController player) => player.PickUp(this);

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _baseColor = _sr.color;
        }

        /// <summary>Tint the item to signal it's the current pickup target. Idempotent.</summary>
        public void SetHighlighted(bool on)
        {
            if (_sr == null || _highlighted == on) return;
            _highlighted = on;
            _sr.color = on ? _highlightColor : _baseColor;
        }
    }
}
